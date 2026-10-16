using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

// A ship's trip through a lane is driven by the ship's own game logic, from a
// state saved in the ship's ModStorage. Whichever server has the ship loaded
// finishes the trip: after a reload, when the departure computer is gone, or on
// a cluster after the ship moved to another node. The departure computer only
// runs the docking and hands the ship over at its end.

namespace Psycho.TradeLanes
{
    public class TransitState
    {
        // The last ring of the lane
        public SerializableVector3D ExitPosition;
        public SerializableVector3D ExitForward;
        public SerializableVector3D ExitUp;

        // The trip ends when the ship touches this box, oriented like the ring
        public SerializableVector3D ArrivalBoxCenter;
        public SerializableVector3D ArrivalBoxHalfExtent;

        // m/s
        public float Speed;

        // Identities whose USE control (F) is blocked until the trip ends
        public List<long> SeatLocked = new List<long>();
    }

    // Values kept in an entity's ModStorage as XML. Each key has to be claimed in
    // Data/EntityComponents.sbc, or the game drops it when it saves.
    public static class ModStore
    {
        public static T Load<T>(IMyEntity entity, Guid key)
            where T : class
        {
            string xml;
            if (entity?.Storage == null || !entity.Storage.TryGetValue(key, out xml))
                return null;
            try
            {
                return MyAPIGateway.Utilities.SerializeFromXML<T>(xml);
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine(
                    $"TradeLanes: unreadable ModStorage {key} on {entity.EntityId}: {e.Message}"
                );
                return null;
            }
        }

        // null removes the value
        public static void Save<T>(IMyEntity entity, Guid key, T value)
            where T : class
        {
            if (value == null)
            {
                entity.Storage?.RemoveValue(key);
                return;
            }
            if (entity.Storage == null)
                entity.Storage = new MyModStorageComponent();
            entity.Storage.SetValue(key, MyAPIGateway.Utilities.SerializeToXML(value));
        }
    }

    public partial class CustomGridLogic
    {
        static readonly Guid TransitKey = new Guid("9de1a7eb-af07-4a4c-8423-a41c6ab04d2d");

        // Closer to the exit than this (km) the ship drops out of the lane
        const float BreakOffKilometers = 0.01f;

        // Speed a ship leaves the lane with, m/s
        const float ExitSpeed = 50f;

        TransitState Transit;
        Vector3D TransitLastPosition;
        double TransitLastTime;

        public bool IsInTransit => Transit != null;

        // Server: the ship leaves the dock into the lane
        public void EnterLane(MatrixD exit, MyOrientedBoundingBoxD arrivalBox, float speed)
        {
            Transit = new TransitState
            {
                ExitPosition = exit.Translation,
                ExitForward = exit.Forward,
                ExitUp = exit.Up,
                ArrivalBoxCenter = arrivalBox.Center,
                ArrivalBoxHalfExtent = arrivalBox.HalfExtent,
                Speed = speed,
                SeatLocked = TradeLaneNetwork.LockSeats(Grid),
            };
            ModStore.Save(Grid, TransitKey, Transit);
            TradeLaneComputerBlockLogic.GridIgnore.Add(Grid);
            WatchGroup();
            Instruct(ShipSystem.TradeLaneFlight);
        }

        // Server: picks up a trip saved with the ship
        void ResumeTransit()
        {
            Transit = ModStore.Load<TransitState>(Grid, TransitKey);
            if (Transit == null)
                return;

            // The block is client side state, a reload or another server lost it
            TradeLaneNetwork.SetSeatLock(Transit.SeatLocked, true);
            TradeLaneComputerBlockLogic.GridIgnore.Add(Grid);
            WatchGroup();
            Instruct(ShipSystem.TradeLaneFlight);
        }

        void LeaveLane(ShipSystem state)
        {
            var seatLocked = Transit.SeatLocked;
            Transit = null;
            ModStore.Save<TransitState>(Grid, TransitKey, null);
            TradeLaneComputerBlockLogic.GridIgnore.Remove(Grid);
            UnwatchAll();
            Instruct(state);
            TradeLaneNetwork.SetSeatLock(seatLocked, false);
        }

        // Server: runs the state change here and plays its effects on the clients
        public void Instruct(ShipSystem state)
        {
            ExecShipSystems(state);
            TradeLaneNetwork.SendShipSystem(Grid, state);
        }

        // Server, every frame: moves the ship down the lane until it reaches the
        // exit ring, or the pilot breaks off by turning the dampeners on
        void DriveTransit()
        {
            if (Grid.Physics == null || !Grid.Physics.Enabled)
                return;

            if (!WatchGroup())
            {
                DropOut("a grid disconnected");
                return;
            }
            if (WarheadAboutToGoOff())
            {
                DropOut("a warhead countdown is running out");
                return;
            }

            var exit = MatrixD.CreateWorld(
                Transit.ExitPosition,
                Transit.ExitForward,
                Transit.ExitUp
            );
            var position = Grid.WorldMatrix.Translation;

            // Aims the grid's origin so that the center of its bounding box gets to the ring
            var target = exit.Translation + position - Grid.PositionComp.WorldAABB.Center;
            var direction = Vector3D.Normalize(target - position);
            var distance = Vector3D.Distance(position, target + exit.Forward);
            var kilometers = MathHelper.RoundOn2((float)distance / 1000f);

            var arrivalBox = new MyOrientedBoundingBoxD(
                Transit.ArrivalBoxCenter,
                Transit.ArrivalBoxHalfExtent,
                Quaternion.CreateFromRotationMatrix(exit)
            );

            if (TradeLaneComputerBlockLogic.CheckIfGridIsInTriggerBox(ref arrivalBox, Grid))
            {
                Disengage(direction, "Arrived.");
            }
            else if (kilometers > BreakOffKilometers && !Grid.DampenersEnabled)
            {
                var speed =
                    kilometers > 10
                        ? Transit.Speed
                        : MathHelper.Clamp(
                            Transit.Speed * MathHelper.Clamp(kilometers / 5, 0f, 1f),
                            ExitSpeed,
                            Transit.Speed
                        );
                TradeLaneComputerBlockLogic.SetGridDirectionzzz(Grid, exit, 0.1f);

                var matrix = Grid.WorldMatrix;
                matrix.Translation +=
                    direction * speed * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;
                Grid.Teleport(matrix);
            }
            else if (kilometers > BreakOffKilometers)
            {
                // The pilot turned the dampeners on
                Grid.Physics.LinearVelocity = direction * ExitSpeed;
                LeaveLane(ShipSystem.TradeLaneBreakOff);
            }
            else
            {
                SetDampeners();
                LeaveLane(ShipSystem.TradeLaneBreakOff);
            }

            TradeLaneNetwork.Notify(
                Grid,
                $"Distance: {kilometers:F0}Km | Speed: {MeasuredSpeed():F0}",
                16
            );
        }

        // Toward the exit ring, the way DriveTransit moves the ship
        Vector3D LaneDirection()
        {
            return Vector3D.Normalize(
                (Vector3D)Transit.ExitPosition - Grid.PositionComp.WorldAABB.Center
            );
        }

        // The ship leaves the lane with the gate's effects, slowing down
        void Disengage(Vector3D direction, string text)
        {
            SetDampeners();

            // Every grid of the ship, or a part that just came off rams the rest
            foreach (var grid in Watched)
                if (grid.Physics != null)
                    grid.Physics.LinearVelocity = direction * ExitSpeed;
            LeaveLane(ShipSystem.TradeLaneDisengage);
            TradeLaneNetwork.Notify(Grid, text);
        }

        // Brakes the ship at the end of the trip. The pilot can be offline when a
        // trip resumed after a restart ends, then any seat that controls the
        // thrusters will do.
        void SetDampeners()
        {
            var controller = TradeLaneComputerBlockLogic.GetController(Grid);
            if (controller == null)
                foreach (var seat in TradeLaneComputerBlockLogic.GetShipControllersFromGrid(Grid))
                    if (seat.ControlThrusters)
                    {
                        controller = seat;
                        break;
                    }
            if (controller != null)
                controller.DampenersOverride = true;
        }

        // The ship is teleported, its physics speed means nothing
        double MeasuredSpeed()
        {
            var position = Grid.WorldMatrix.Translation;
            var time = MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds;
            var speed =
                time > TransitLastTime
                    ? Vector3D.Distance(position, TransitLastPosition) / (time - TransitLastTime)
                    : 0;
            TransitLastPosition = position;
            TransitLastTime = time;
            return speed;
        }
    }
}
