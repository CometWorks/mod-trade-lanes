using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI;
using ProtoBuf;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using static VRageRender.MyBillboard;
using VRage.Game.Entity.UseObject;
using VRage.Network;
using Sandbox.Game.Localization;
using Sandbox.Game.World;
using VRage.Library.Utils;
//using static VRage.Audio.MyCueBank;
using VRage.Audio;
using VRage.Voxels;
using static VRage.Game.MyObjectBuilder_BehaviorTreeDecoratorNode;

using System.IO;
using System.Xml.Serialization;
using System.Runtime.Remoting.Messaging;
using static VRage.Game.MyObjectBuilder_Checkpoint;
using VRage.ObjectBuilders.Voxels;
using System.Net;
using System.Net.Sockets;
using ParallelTasks;
using static VRage.Game.MyObjectBuilder_CurveDefinition;
using ObjectBuilders.SafeZone;
using Sandbox.Game.WorldEnvironment.ObjectBuilders;
using Sandbox.Engine.Platform;
using Sandbox.Game.Lights;
using VRageRender.Lights;
using VRage;
using VRage.Game.Entities;
using static VRage.Game.MyObjectBuilder_SessionComponentMission;
using SpaceEngineers.Game.Entities.Blocks.SafeZone;
using VRage.Game.ObjectBuilders.Components;
using Sandbox.Game.Entities.Cube;
using System.Net.NetworkInformation;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Sandbox.Game.GUI;
using static VRage.Game.MyObjectBuilder_ControllerSchemaDefinition;
//using Microsoft.Xml.Serialization.GeneratedAssembly;
//using static System.Collections.Specialized.BitVector32;

using Psycho.Utils;
using VRageRender.Import;
using VRage.Game.Models;
using VRage.Input;
//using static Sandbox.Game.World.MyPlayer;

namespace Psycho.TradeLanes
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), false)]
    public partial class CustomGridLogic : MyGameLogicComponent
    {
        // 79-80
        // 299-300
        // 1000- --- ???

        Utils.Utils Util = new Utils.Utils();

        public bool Dock = false;

        // SE default
        float LargeShipMaxSpeed = 100f;
        float SmallShipMaxSpeed = 100f;

        //ShipJumpDriveCharging
        //ShipPrototechJumpDriveJumpIn
        //ShipJumpDriveRecharge
        //BlockSafeZone
        //ShipJumpDriveJumpOut

        string TradeLaneDockingSoundName = "ShipJumpDriveCharging";
        string TradeLaneJumpSoundName = "ShipPrototechJumpDriveJumpIn";
        string TradeLaneDisengageSoundName = "ShipJumpDriveJumpOut";
        string TradeLaneBreakOffSoundName = "ShipJumpDriveJumpOut";
        string TradeLaneFlightSoundName = "ShipJumpDriveRecharge";

        string TradeLaneDockingParticleName = "XXXXXXXXXXXXXXXXXXXXXXXX";
        string TradeLaneJumpParticleName = "XXXXXXXXXXXXXXXXXXXXXXXX";
        string TradeLaneDisengageParticleName = "XXXXXXXXXXXXXXXXXXXXXXXX";
        string TradeLaneBreakOffParticleName = "XXXXXXXXXXXXXXXXXXXXXXXX";
        string TradeLaneFlightParticleName = "XXXXXXXXXXXXXXXXXXXXXXXX";

        /*
        WarpY
        MyWarpY

        TradeLaneStarsAndTunnel
        TradeLaneStars
        */
        string ShipParticlesName = "WarpY";
        string PathParticlesName = "TradeLaneStarsAndTunnel";

        //WarpY
        //MyWarpY

        public MyCubeGrid Grid = null;
        public MyShipController ShipController = null;

        public ShipSystem ShipSystemState;

        public enum ShipSystem : int
        {
            RequestDocking,
            Waiting,
            Docking,
            Jump,
            CancelDocking,
            TradeLaneFlight,
            TradeLaneMergeOnto,
            TradeLaneBreakOff,
            TradeLaneDisengage
        }

        public ShipFlightStates ShipFlightState;

        public enum ShipFlightStates : int
        {
            RequestDocking,
            Waiting,
            Docking,
            Jump,
            CancelDocking,
            TradeLaneFlight,
            TradeLaneMergeOnto,
            TradeLaneBreakOff,
            TradeLaneDisengage
        }

        bool InTransit = false;

        TradeLaneSystem sessionHandler => TradeLaneSystem.Instance;

        MyParticleEffect ShipParticles = null; // Refers to particles bound to ships
        MyParticleEffect PathPartciles = null; // Refers to particles bound to the path the ship is traveling on

        MyEntity3DSoundEmitter ShipSoundEmitter = null;
        MySoundPair ActiveSound;

        MySoundPair TradeLaneDockingSound = new MySoundPair();
        MySoundPair TradeLaneJumpSound = new MySoundPair();
        MySoundPair TradeLaneFlightSound = new MySoundPair();
        MySoundPair TradeLaneDisengageSound = new MySoundPair();
        MySoundPair TradeLaneBreakOffSound = new MySoundPair();

        public bool IgnoreLimits = false;
        public bool ParticlesSpawned = false;

        List<MyCubeGrid> Grids = new List<MyCubeGrid>();
        private double maxSpeed;

        private MyOrientedBoundingBoxD GridBB = new MyOrientedBoundingBoxD();

        List<IMyPlayer> Players = new List<IMyPlayer>();
        Dictionary<IMyPlayer, Vector3D> PlayerPos = new Dictionary<IMyPlayer, Vector3D>();
        Dictionary<IMyPlayer, IMyCockpit> PlayerSeat = new Dictionary<IMyPlayer, IMyCockpit>();

        private Vector3D _previousPosition = Vector3D.Zero;
        private double _previousTime = 0;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            //MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
            //base.Init(objectBuilder);
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void Close()
        {
            // A ship closed mid-trip keeps its trip and its seat lock: it may be
            // going on on another server of a cluster
            TradeLaneComputerBlockLogic.GridIgnore.Remove(Grid);
            UnwatchAll();
            StopSoundEmitter(ShipSoundEmitter, true);
            RemoveSoundEmitter(ref ShipSoundEmitter);
        }

        public override void UpdateOnceBeforeFrame()
        {
            Grid = Entity as MyCubeGrid;

            if (Grid == null)
                return;

            LargeShipMaxSpeed = sessionHandler.LargeShipMaxSpeed;
            SmallShipMaxSpeed = sessionHandler.SmallShipMaxSpeed;

            ShipSoundEmitter = SpawnSoundEmitter(Grid);

            TradeLaneDockingSound = GetSoundPair(TradeLaneDockingSoundName);
            TradeLaneJumpSound = GetSoundPair(TradeLaneJumpSoundName);
            TradeLaneFlightSound = GetSoundPair(TradeLaneFlightSoundName);
            TradeLaneDisengageSound = GetSoundPair(TradeLaneDisengageSoundName);
            TradeLaneBreakOffSound = GetSoundPair(TradeLaneBreakOffSoundName);

            //ShipSoundEmitter = SpawnSoundEmitter(Grid);
            //TradeLaneDockingSound = GetSoundPair(TradeLaneDockingSoundName);

            //MyAPIGateway.Input.ShowCursor(true);

            if (MyAPIGateway.Session.IsServer)
                ResumeTransit();

            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;
        }
        
        public override void UpdateBeforeSimulation100()
        {
            try
            {
                var matrix = Grid.WorldMatrix;

                Quaternion.CreateFromRotationMatrix(ref matrix, out GridBB.Orientation);
                GridBB.Center = Grid.PositionComp.WorldAABB.Center;
                GridBB.HalfExtent.X = Grid.PositionComp.WorldAABB.HalfExtents.X * 2;
                GridBB.HalfExtent.Y = Grid.PositionComp.WorldAABB.HalfExtents.Y * 2;
                GridBB.HalfExtent.Z = Grid.PositionComp.WorldAABB.HalfExtents.Z * 2;
                GridBB.HalfExtent = Grid.PositionComp.WorldAABB.HalfExtents * 2;

                //RunTime();
                //MyAPIGateway.Utilities.ShowMessage("TRADE LANES CustomGridLogic", "Running on grid " + Grid.DisplayName);
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES CGL UpdateBeforeSimulation100", e.Message);
            }
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                //RunTime();
                /*
                if (!IgnoreLimits && !TradeLaneComputerBlockLogic.GridIgnore.Contains(Grid))
                    Grid.Physics.LinearVelocity = Vector3D.ClampToSphere(Grid.Physics.LinearVelocity, Grid.GridSizeEnum == MyCubeSize.Large ? LargeShipMaxSpeed : SmallShipMaxSpeed);
                */
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES CGL UpdateBeforeSimulation", e.Message);
            }
        }
        
        public override void UpdateAfterSimulation()
        {
            try
            {
                if (Transit != null)
                    DriveTransit();
                RunTime();
                /*
                if (!IgnoreLimits)
                    Grid.Physics.LinearVelocity = Vector3D.ClampToSphere(Grid.Physics.LinearVelocity, Grid.GridSizeEnum == MyCubeSize.Large ? LargeShipMaxSpeed : SmallShipMaxSpeed);
                */
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES CGL UpdateAfterSimulation", e.Message);
            }
        }

        private void RunTime()
        {
            if (Grid == null || Grid.Physics == null)
                return;

            // in session comp update:
            /*
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(MyAPIGateway.Session.Player.Character);
            MyAPIGateway.Utilities.ShowNotification($"player={player}", 16);
            */

            /*
            // Not actually sure if it should be called after or before sim.
            if (TradeLaneComputerBlockLogic.GridIgnore.Contains(Grid))
            {
                if (Grids.Count == 0)
                {
                    Grids = Grid.GetConnectedGrids(GridLinkTypeEnum.Physical | GridLinkTypeEnum.NoContactDamage);

                    MyAPIGateway.Utilities.ShowMessage("TRADE LANES CustomGridLogic", "Found " + Grids.Count + " grids to ignore limits on.");

                    if (Grids.Count > 0)
                    {
                        foreach (var grid in Grids)
                        {
                            if (grid == null || grid == Grid)
                                continue;

                            var logic = grid.GameLogic.GetAs<CustomGridLogic>();
                            logic.IgnoreLimits = true;

                            //grid.Physics.Enabled = false;
                            //grid.Physics.Deactivate();
                        }
                    }
                }
                IgnoreLimits = true;
                return;
            }
            else
            {
                if (Grids.Count > 0)
                {
                    foreach (var grid in Grids)
                    {
                        if (grid == null || grid == Grid)
                            continue;

                        var logic = grid.GameLogic.GetAs<CustomGridLogic>();
                        logic.IgnoreLimits = false;

                        //grid.Physics.Enabled = true;
                        //grid.Physics.Activate();
                    }
                    Grids.Clear();
                }
            }
            */
            //Vector4 color = Color.Yellow.ToVector4() * 12;
            //DrawOBB(GridBB, color);

            if (InTransit)
            {
                try
                {
                    //MovePlayers();
                    TeleportPlayers();
                }
                catch (Exception e)
                {
                    MyAPIGateway.Utilities.ShowMessage("TRADE LANES CGL (InTransit)", e.Message);
                }
            }

            if (!IgnoreLimits)
            {
                var grid = Grid as IMyCubeGrid;

                if (grid?.Physics != null && grid.ControlSystem != null)
                {
                    var controller = grid.ControlSystem.CurrentShipController as IMyShipController;
                    var con = grid.ControlSystem.CurrentShipController as MyShipController;
                    if (controller != null && controller.IsUnderControl)
                    {
                        // Get current velocity in world space
                        Vector3D currentVelocity = grid.Physics.LinearVelocity;

                        // Transform velocity to local grid space
                        Vector3D localVelocity = Vector3D.TransformNormal(currentVelocity, MatrixD.Transpose(grid.WorldMatrix));

                        /*
                        // Define speed limits for each direction
                        double forwardSpeedLimit = 100.0; // Example limit for forward/backward
                        double rightSpeedLimit = 50.0;    // Example limit for right/left
                        double upSpeedLimit = 30.0;       // Example limit for up/down
                        */

                        //LargeShipMaxSpeed = 300f;
                        //SmallShipMaxSpeed = 100f;

                        double forwardSpeedLimit = Grid.GridSizeEnum == MyCubeSize.Large ? LargeShipMaxSpeed : SmallShipMaxSpeed;
                        double rightSpeedLimit = Grid.GridSizeEnum == MyCubeSize.Large ? LargeShipMaxSpeed : SmallShipMaxSpeed;
                        double upSpeedLimit = Grid.GridSizeEnum == MyCubeSize.Large ? LargeShipMaxSpeed : SmallShipMaxSpeed;

                        // Adjust MoveIndicator based on speed limits
                        Vector3 moveIndicator = controller.MoveIndicator;

                        // Forward/Backward (Z-axis in local space)
                        if (Math.Abs(localVelocity.Z) > forwardSpeedLimit)
                        {
                            moveIndicator.Z = 0; // Stop forward/backward movement
                        }

                        // Right/Left (X-axis in local space)
                        if (Math.Abs(localVelocity.X) > rightSpeedLimit)
                        {
                            moveIndicator.X = 0; // Stop right/left movement
                        }

                        // Up/Down (Y-axis in local space)
                        if (Math.Abs(localVelocity.Y) > upSpeedLimit)
                        {
                            moveIndicator.Y = 0; // Stop up/down movement
                        }

                        // Apply the adjusted MoveIndicator
                        //controller.MoveIndicator = moveIndicator;

                        //con.EntityThrustComponent.ControlThrust = Vector3.Zero;

                        //Vector3D desiredVelocity = Vector3D.ClampToSphere(currentVelocity, Grid.GridSizeEnum == MyCubeSize.Large ? LargeShipMaxSpeed : SmallShipMaxSpeed);
                        //Vector3D force = (desiredVelocity - currentVelocity) * grid.Physics.Mass;

                        // Apply the adjusted MoveIndicator indirectly by controlling thrust or forces
                        Vector3D desiredVelocity = Vector3D.ClampToSphere(currentVelocity, forwardSpeedLimit);
                        Vector3D force = (desiredVelocity - currentVelocity) * grid.Physics.Mass;

                        var limit = (Grid.GridSizeEnum == MyCubeSize.Large ? LargeShipMaxSpeed : SmallShipMaxSpeed);

                        if (controller.MoveIndicator != Vector3.Zero && currentVelocity.Length() > limit)
                            grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, force, null, null);
                        //MyAPIGateway.Utilities.ShowMessage("TRADE LANES CustomGridLogic", "Running on grid " + Grid.DisplayName);
                    }
                }

                //if (Grid.GridSizeEnum == MyCubeSize.Small)
                    //Grid.Physics.LinearVelocity = Vector3D.ClampToSphere(Grid.Physics.LinearVelocity, Grid.GridSizeEnum == MyCubeSize.Large ? LargeShipMaxSpeed : SmallShipMaxSpeed);
            }

            //base.UpdateAfterSimulation();
            // Custom logic for the grid
            //MyAPIGateway.Utilities.ShowNotification("Custom logic running on grid!", 1000);
        }

        private void MovePlayers()
        {
            if (Grid != null && Grid.PositionComp != null)
            {
                // Get the current position and time
                Vector3D currentPosition = Grid.PositionComp.WorldAABB.Center;
                double currentTime = MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds;

                // Calculate the time difference
                double deltaTime = currentTime - _previousTime;

                // Ensure deltaTime is valid to avoid division by zero
                if (deltaTime > 0)
                {
                    // Calculate the velocity as displacement over time
                    Vector3D calculatedVelocity = (currentPosition - _previousPosition) / deltaTime;

                    // Use the calculated velocity if Grid.Physics.LinearVelocity is zero
                    Vector3D gridVelocity = Grid.Physics?.LinearVelocity ?? Vector3D.Zero;
                    if (gridVelocity == Vector3D.Zero)
                    {
                        gridVelocity = calculatedVelocity;
                    }

                    // Apply the velocity to players in transit
                    if (InTransit)
                    {
                        List<MyEntity> intersectingEntities = new List<MyEntity>();
                        MyGamePruningStructure.GetAllEntitiesInOBB(ref GridBB, intersectingEntities);

                        var players = intersectingEntities.OfType<IMyPlayer>();
                        foreach (var player in players)
                        {
                            if (player?.Character != null && player.Character.Physics != null)
                            {
                                player.Character.Physics.LinearVelocity = gridVelocity;
                            }
                        }
                    }
                }

                // Update the previous position and time for the next frame
                _previousPosition = currentPosition;
                _previousTime = currentTime;
            }
        }

        private void TeleportPlayers()
        {
            //List<MyEntity> intersectingEntities = new List<MyEntity>();

            //MyGamePruningStructure.GetAllEntitiesInOBB(ref GridBB, intersectingEntities);

            //return intersectingEntities.OfType<MyCubeGrid>().FirstOrDefault();

            //var list = intersectingEntities.OfType<IMyPlayer>() as List<IMyPlayer>;

            if (Players == null)
            {
                //MyAPIGateway.Utilities.ShowMessage("TRADE LANES CustomGridLogic", "Players was null");
                return;
            }

            if (Players.Count == 0)
            {
                //MyAPIGateway.Utilities.ShowMessage("TRADE LANES CustomGridLogic", "No players found in trade lane.");
                return;
            }

            MovePlayersWithGrid();
            return;

            foreach (var player in Players)
            {
                //var grid = entity as MyCubeGrid;

                if (player.Character != null)
                {
                    try
                    {
                        if (player.Character.Physics == null)
                            continue;

                        if (!IsPlayerInsideGrid(player))
                            continue;

                        /*
                        Vector3D gridVelocity = Grid.Physics?.LinearVelocity ?? Vector3D.Zero;
                        player.Character.Physics.LinearVelocity = gridVelocity;

                        Vector3D playerWorldPosition = player.Character.WorldMatrix.Translation;
                        Vector3D playerLocalPosition = Vector3D.Transform(playerWorldPosition, Grid.PositionComp.WorldMatrixInvScaled);
                        var newPos = player.Character.WorldMatrix;
                        newPos.Translation = Vector3D.Transform(playerLocalPosition, Grid.WorldMatrix);
                        player.Character.Teleport(newPos);
                        */

                        //MyAPIGateway.Utilities.ShowMessage("TRADE LANES CGL (foreach player)", "MOVE!");
                    }
                    catch (Exception e)
                    {
                        MyAPIGateway.Utilities.ShowMessage("TRADE LANES CGL (foreach player)", e.Message);
                    }
                }
            }
        }

        private void MovePlayersWithGrid()
        {
            if (Players == null || Players.Count == 0 || Grid == null)
                return;

            foreach (var player in Players)
            {
                if (player?.Character == null || player.Character.Physics == null)
                    continue;

                if (PlayerSeat.ContainsKey(player))
                {
                    var seat = PlayerSeat[player];
                    if (seat != null && seat.Pilot == null)
                    {
                        // Attach the player back to the seat
                        seat.AttachPilot(player.Character);
                        MyAPIGateway.Utilities.ShowMessage("TRADE LANES", $"{player.DisplayName} has been seated in {seat.DisplayName}.");
                    }
                    continue; // Skip further processing for seated players
                }

                return;
                //player.Character.Kill();

                    // Check if the player is inside the grid's bounding box
                    //if (!IsPlayerInsideGrid(player))
                    //    continue;

                    // Get the grid's velocity
                    Vector3D gridVelocity = Grid.Physics?.LinearVelocity ?? Vector3D.Zero;

                // Apply the grid's velocity to the player
                player.Character.Physics.LinearVelocity = gridVelocity;

                // Transform the player's position relative to the grid
                Vector3D playerWorldPosition = player.Character.WorldMatrix.Translation;
                Vector3D playerLocalPosition = Vector3D.Transform(playerWorldPosition, Grid.PositionComp.WorldMatrixInvScaled);
                if (!PlayerPos.ContainsKey(player))
                    PlayerPos[player] = playerLocalPosition;
                else
                    playerLocalPosition = PlayerPos[player];

                /*
                // Add input-based movement
                Vector3D forwardDirection = Vector3D.Forward;
                Vector3D upDirection = Vector3D.Up;
                Vector3D rightDirection = Vector3D.Right; // Local X-axis for left/right movement
                double movementSpeed = 0.1; // Adjust this value for desired movement speed

                if (MyAPIGateway.Input.IsKeyPress(MyKeys.W)) // Move forward
                {
                    playerLocalPosition -= forwardDirection * movementSpeed;
                }
                if (MyAPIGateway.Input.IsKeyPress(MyKeys.S)) // Move backward
                {
                    playerLocalPosition += forwardDirection * movementSpeed;
                }
                if (MyAPIGateway.Input.IsKeyPress(MyKeys.A)) // Move left
                {
                    playerLocalPosition -= rightDirection * movementSpeed;
                }
                if (MyAPIGateway.Input.IsKeyPress(MyKeys.D)) // Move right
                {
                    playerLocalPosition += rightDirection * movementSpeed;
                }
                if (MyAPIGateway.Input.IsKeyPress(MyKeys.Space)) // Move up
                {
                    playerLocalPosition -= upDirection * movementSpeed;
                }
                if (MyAPIGateway.Input.IsKeyPress(MyKeys.C)) // Move down
                {
                    playerLocalPosition += upDirection * movementSpeed;
                }
                */

                // Update the stored position
                PlayerPos[player] = playerLocalPosition;

                Vector3D newPlayerWorldPosition = Vector3D.Transform(playerLocalPosition, Grid.WorldMatrix);

                // Update the player's position
                var newPlayerMatrix = player.Character.WorldMatrix;
                newPlayerMatrix.Translation = newPlayerWorldPosition;
                player.Character.Teleport(newPlayerMatrix);

                //player.Character.Teleport(Grid.WorldMatrix);
            }
        }

        private void GetPlayers()
        {
            //MyAPIGateway.Utilities.ShowMessage("TRADE LANES CGL GetPlayers", "Getting players.");
            Players.Clear();
            PlayerPos.Clear();
            PlayerSeat.Clear();
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            foreach (var player in players)
            {
                if (player?.Character == null)
                    continue;

                // Check if the player is seated
                var parentEntity = player.Character.Parent as IMyCockpit;
                if (parentEntity != null)
                {
                    // Player is seated in a cockpit or seat
                    PlayerSeat[player] = parentEntity;
                    //MyAPIGateway.Utilities.ShowMessage("TRADE LANES", $"{player.DisplayName} is seated in {parentEntity.DisplayName}.");
                }
                else
                {
                    // Player is not seated
                    //MyAPIGateway.Utilities.ShowMessage("TRADE LANES", $"{player.DisplayName} is not seated.");
                }

                // Add the player to the list if they are inside the grid
                if (IsPlayerInsideGrid(player))
                {
                    Players.Add(player);
                    if (!PlayerPos.ContainsKey(player))
                        PlayerPos[player] = Vector3D.Transform(player.Character.WorldMatrix.Translation, Grid.PositionComp.WorldMatrixInvScaled);
                }
            }

            if (Players.Count == 0)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES CustomGridLogic", "No players found in trade lane.");
            }
        }

        private void GetPlayersOLD()
        {
            MyAPIGateway.Utilities.ShowMessage("TRADE LANES CGL GetPlayers", "Getting players.");

            Players.Clear();

            var seats = Grid.OccupiedBlocks;

            foreach (var seat in seats)
            {
                var player = MyAPIGateway.Players.GetPlayerControllingEntity(seat.Pilot);
                if (player != null && !Players.Contains(player))
                    Players.Add(player);
            }

            if (Players.Count == 0)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES CustomGridLogic", "No players found in trade lane.");
            }
        }

        private void GetPlayersOLDOLD()
        {
            MyAPIGateway.Utilities.ShowMessage("TRADE LANES CGL GetPlayers", "Getting players.");

            Players.Clear();

            /*
            List<MyEntity> intersectingEntities = new List<MyEntity>();
            MyGamePruningStructure.GetAllEntitiesInOBB(ref GridBB, intersectingEntities);
            //return intersectingEntities.OfType<MyCubeGrid>().FirstOrDefault();
            //Players = intersectingEntities.OfType<IMyPlayer>() as List<IMyPlayer>;

            foreach (MyEntity entity in intersectingEntities)
            {
                var player = entity as IMyPlayer;

                if (player != null)
                {
                    Players.Add(player);
                }
            }

            var shipControllers = new List<IMyShipController>();
            var grid = Grid as IMyCubeGrid;
            // Ensure the grid is valid  
            if (grid == null)
                return;

            // Iterate through all blocks on the grid  
            var fatBlocks = grid.GetFatBlocks<IMyCubeBlock>();
            foreach (var block in fatBlocks)
            {
                IMyShipController controller = block as IMyShipController;
                // Check if the block is a ship controller
                if (controller != null)
                {
                    // Check if a player is seated in the controller
                    if (controller.Pilot != null)
                    {
                        var player = MyAPIGateway.Players.GetPlayerControllingEntity(controller.Pilot);
                        if (player != null && !Players.Contains(player))
                        {
                            Players.Add(player);
                        }
                    }
                }
            }
            */

            var seats = Grid.OccupiedBlocks;

            foreach (var seat in seats)
            {
                var player = MyAPIGateway.Players.GetPlayerControllingEntity(seat.Pilot);
                if (player != null && !Players.Contains(player))
                    Players.Add(player);
            }

            if (Players.Count == 0)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES CustomGridLogic", "No players found in trade lane.");
            }
        }

        private bool IsPlayerInsideGrid(IMyPlayer player)
        {
            if (player?.Character == null || Grid == null)
                return false;

            // Get the player's position in world space
            Vector3D playerWorldPosition = player.Character.WorldAABB.Center;

            // Transform the player's position to the grid's local space
            Vector3D playerLocalPosition = Vector3D.Transform(playerWorldPosition, Grid.PositionComp.WorldMatrixInvScaled);

            // Get the grid's local bounding box
            BoundingBoxD gridLocalBoundingBox = Grid.PositionComp.LocalAABB;

            // Check if the player's local position is inside the grid's bounding box
            return gridLocalBoundingBox.Contains(playerLocalPosition) == ContainmentType.Contains;
        }

        public static void DrawOBB(MyOrientedBoundingBoxD obb, Color color, MySimpleObjectRasterizer raster = MySimpleObjectRasterizer.Wireframe, float thickness = 0.01f)
        {
            var material = MyStringId.GetOrCompute("Square");
            var box = new BoundingBoxD(-obb.HalfExtent, obb.HalfExtent);
            var wm = MatrixD.CreateFromQuaternion(obb.Orientation);
            wm.Translation = obb.Center;
            MySimpleObjectDraw.DrawTransparentBox(ref wm, ref box, ref color, raster, 1, thickness, material, material);
        }

        // Runs on the server and, for the sounds and particles, on every client
        public void ExecShipSystems(ShipSystem state)
        {
            var grid = Grid as IMyCubeGrid;
            if (grid == null)
                return;

            bool server = MyAPIGateway.Session.IsServer;
            bool effects = !MyAPIGateway.Utilities.IsDedicated;

            switch (state)
            {
                case ShipSystem.Docking:
                    if (server)
                        GetPlayers();
                    if (effects)
                        PlaySound(ShipSoundEmitter, TradeLaneDockingSound, ref ActiveSound);
                    break;
                case ShipSystem.CancelDocking:
                    Players.Clear();
                    PlayerPos.Clear();
                    PlayerSeat.Clear();
                    if (effects)
                    {
                        StopSoundEmitter(ShipSoundEmitter, true);
                        PlaySound(ShipSoundEmitter, TradeLaneDisengageSound, ref ActiveSound, 2f);
                    }
                    break;
                case ShipSystem.Jump:
                    InTransit = server;
                    //GetPlayers();
                    if (effects)
                        PlaySound(ShipSoundEmitter, TradeLaneJumpSound, ref ActiveSound);
                    break;
                case ShipSystem.TradeLaneDisengage:
                    InTransit = false;
                    Players.Clear();
                    PlayerPos.Clear();
                    PlayerSeat.Clear();
                    if (!effects)
                        break;
                    PlaySound(ShipSoundEmitter, TradeLaneDisengageSound, ref ActiveSound, 2f);
                    RemoveParticleEffects(ref ShipParticles);
                    RemoveParticleEffects(ref PathPartciles);

                    if (Grid?.Physics != null && grid.ControlSystem != null)
                    {
                        var controller = grid.ControlSystem.CurrentShipController as IMyShipController;
                        if (controller != null && controller.IsUnderControl)
                        {
                            var pointOffset = WarpPoint(Grid, controller.WorldMatrix.Backward, false, 0f);
                            ShipParticles = SpawnParticleEffects(Grid as MyEntity, "RingsDisengage", MatrixD.Identity, pointOffset);
                        }
                    }
                    break;
                case ShipSystem.TradeLaneBreakOff:
                    InTransit = false;
                    Players.Clear();
                    PlayerPos.Clear();
                    PlayerSeat.Clear();
                    if (!effects)
                        break;
                    StopSoundEmitter(ShipSoundEmitter, true);
                    PlaySound(ShipSoundEmitter, TradeLaneBreakOffSound, ref ActiveSound, 2f);
                    RemoveParticleEffects(ref ShipParticles);
                    RemoveParticleEffects(ref PathPartciles);

                    if (Grid?.Physics != null && grid.ControlSystem != null)
                    {
                        var controller = grid.ControlSystem.CurrentShipController as IMyShipController;
                        if (controller != null && controller.IsUnderControl)
                        {
                            MatrixD flippedMatrix = controller.LocalMatrix;
                            var pointOffset = WarpPoint(Grid, controller.WorldMatrix.Backward, false, 0f);
                            ShipParticles = SpawnParticleEffects(Grid as MyEntity, "RingsDisengage", flippedMatrix, pointOffset);
                        }
                    }
                    break;
                case ShipSystem.TradeLaneFlight:
                    InTransit = server;
                    if (!effects)
                        break;
                    PlaySound(ShipSoundEmitter, TradeLaneFlightSound, ref ActiveSound, 4f);

                    if (Grid?.Physics != null && grid.ControlSystem != null)
                    {
                        var controller = grid.ControlSystem.CurrentShipController as IMyShipController;
                        if (controller != null && controller.IsUnderControl)
                        {
                            MatrixD flippedMatrix = controller.LocalMatrix;
                            flippedMatrix.Forward = -flippedMatrix.Forward; // Flip the forward direction

                            var pointOffset = WarpPoint(Grid, controller.WorldMatrix.Forward, true, 0f);
                            ShipParticles = SpawnParticleEffects(Grid as MyEntity, ShipParticlesName, flippedMatrix, pointOffset);
                            //pointOffset = WarpPoint(Grid, controller.WorldMatrix.Forward, false, 210f);
                            pointOffset = WarpPoint(Grid, controller.WorldMatrix.Forward, false, 0f);
                            PathPartciles = SpawnParticleEffects(Grid as MyEntity, PathParticlesName, flippedMatrix, pointOffset);
                        }
                    }

                    //ShipParticles = SpawnParticleEffect(Grid, ShipParticlesName, MatrixD.Identity);
                    //PathPartciles = SpawnParticleEffect(Grid, PathParticlesName, MatrixD.Identity);
                    break;
            }
        }

        #region SOUND EFFECTS

        private MySoundPair GetSoundPair(string soundId)
        {
            MySoundPair soundPair = new MySoundPair(soundId);

            return soundPair;
        }

        public void PlaySound(MyEntity3DSoundEmitter emitter, MySoundPair sound, ref MySoundPair activeSound, float volume = 1f, bool forceStop = false)
        {
            //if (emitter == null || volume == 0 || emitter.VolumeMultiplier == volume)
            if (emitter == null || volume == 0)
                return;

            emitter.VolumeMultiplier = volume;
            emitter.Update();

            if (sound == null || activeSound == sound)
                return;

            emitter.StopSound(forceStop);
            emitter.PlaySound(sound, skipIntro: false, forcePlaySound: true);
            activeSound = sound;
            //emitter.PlaySound(activeSound, alwaysHearOnRealistic: true, forcePlaySound: true);
        }

        public MyEntity3DSoundEmitter SpawnSoundEmitter(MyEntity parent)
        {
            MyEntity3DSoundEmitter soundEmitter = null;
            soundEmitter = new MyEntity3DSoundEmitter(parent);
            var grid = parent as MyCubeGrid;
            /*
            if (grid != null)
                soundEmitter.SetPosition(grid.Physics.CenterOfMassWorld);
            */
            //soundEmitter.VolumeMultiplier = volume;
            //soundEmitter.CustomMaxDistance = distance;
            //soundEmitter.PlaySound(soundPair, alwaysHearOnRealistic: true, forcePlaySound: true);
            //soundEmitter.StopSound(true);
            //soundEmitter.PlaySound(soundPair, forcePlaySound: true);
            soundEmitter.Update();
            //activeSound = soundPair;
            return soundEmitter;
        }

        public MyEntity3DSoundEmitter SpawnSoundEmitter(MyEntity parent, MySoundPair soundPair, ref MySoundPair activeSound, float volume = 1, float distance = 100)
        {
            MyEntity3DSoundEmitter soundEmitter = null;
            soundEmitter = new MyEntity3DSoundEmitter(parent);
            soundEmitter.VolumeMultiplier = volume;
            soundEmitter.CustomMaxDistance = distance;
            //soundEmitter.PlaySound(soundPair, alwaysHearOnRealistic: true, forcePlaySound: true);
            //soundEmitter.StopSound(true);
            //soundEmitter.PlaySound(soundPair, forcePlaySound: true);
            soundEmitter.Update();
            //activeSound = soundPair;
            return soundEmitter;
        }

        public MyEntity3DSoundEmitter SpawnSoundEmitter(MyEntity parent, string soundId, float volume = 1, float distance = 100)
        {
            MyEntity3DSoundEmitter soundEmitter = null;
            soundEmitter = new MyEntity3DSoundEmitter(parent);
            MySoundPair soundPair = new MySoundPair(soundId);
            soundEmitter.VolumeMultiplier = volume;
            soundEmitter.CustomMaxDistance = distance;
            //soundEmitter.Force3D = true;
            //soundEmitter.SetPosition(parent.WorldMatrix.Translation);
            soundEmitter.PlaySound(soundPair, alwaysHearOnRealistic: true, forcePlaySound: true);
            //soundEmitter.Update();
            //soundEmitter.PlaySound(soundPair);
            //soundEmitter.PlaySingleSound(soundPair);

            /*
            bool stopPrevious = true;
            bool skipIntro = true;
            bool force2D = false;
            bool alwaysHearOnRealistic = true;
            bool skipToEnd = false;
            bool force3D = true;
            bool forcePlaySound = true;
            soundEmitter.PlaySound(soundPair, stopPrevious, skipIntro, force2D, alwaysHearOnRealistic, skipToEnd, force3D, forcePlaySound);
            */
            return soundEmitter;
        }

        public bool SpawnSoundEmitter(MyEntity parent, string soundId, ref MyEntity3DSoundEmitter emitter, float volume = 1, float distance = 100)
        {
            //MyEntity3DSoundEmitter soundEmitter = null;
            //soundEmitter = new MyEntity3DSoundEmitter(parent as MyEntity);
            //MySoundPair soundPair = new MySoundPair(soundId);
            //soundEmitter.VolumeMultiplier = volume;
            //soundEmitter.PlaySound(soundPair);

            if (emitter == null)
                emitter = new MyEntity3DSoundEmitter(parent);

            if (emitter == null)
                return false;

            MySoundPair soundPair = new MySoundPair(soundId);

            emitter.VolumeMultiplier = volume;
            emitter.CustomMaxDistance = distance;

            emitter.PlaySound(soundPair);
            return true;
        }

        public void StopSoundEmitter(MyEntity3DSoundEmitter emitter, bool force = false)
        {
            if (emitter == null)
                return;
            emitter.StopSound(force);
        }

        public void RemoveSoundEmitter(ref MyEntity3DSoundEmitter emitter)
        {
            if (emitter == null)
                return;
            emitter.StopSound(true);
            emitter = null;
        }

        public MyEntity3DSoundEmitter SpawnSoundEmitter(IMyEntity parent, string soundId, float volume = 1)
        {
            MyEntity3DSoundEmitter soundEmitter = null;
            soundEmitter = new MyEntity3DSoundEmitter(parent as MyEntity);
            MySoundPair soundPair = new MySoundPair(soundId);
            soundEmitter.VolumeMultiplier = volume;
            soundEmitter.PlaySound(soundPair);
            return soundEmitter;
        }

        public void SpawnSoundEmitter(IMyEntity parent, string soundId, ref MyEntity3DSoundEmitter emitter, float volume = 1)
        {
            //MyEntity3DSoundEmitter soundEmitter = null;
            //soundEmitter = new MyEntity3DSoundEmitter(parent as MyEntity);
            //MySoundPair soundPair = new MySoundPair(soundId);
            //soundEmitter.VolumeMultiplier = volume;
            //soundEmitter.PlaySound(soundPair);

            if (emitter == null)
                emitter = new MyEntity3DSoundEmitter(parent as MyEntity);

            MySoundPair soundPair = new MySoundPair(soundId);
            emitter.VolumeMultiplier = volume;
            emitter.PlaySound(soundPair);
        }

        #endregion

        #region PARTICLE EFFECTS

        private Vector3D WarpPoint(MyCubeGrid grid, Vector3D direction, bool forwardOffset = true, double offset = 0)
        {
            if (grid == null || grid.Physics == null)
                return Vector3D.Zero;

            // Normalize the supplied direction
            Vector3D normalizedDirection = Vector3D.Normalize(direction);

            // Get the grid's bounding box in world coordinates
            BoundingBoxD gridBoundingBox = grid.PositionComp.WorldAABB;

            // Calculate the half-extent in the supplied direction
            Vector3D halfExtent = gridBoundingBox.HalfExtents;
            double halfExtentInDirection = Math.Abs(Vector3D.Dot(normalizedDirection, Vector3D.Right) * halfExtent.X) +
                                           Math.Abs(Vector3D.Dot(normalizedDirection, Vector3D.Up) * halfExtent.Y) +
                                           Math.Abs(Vector3D.Dot(normalizedDirection, Vector3D.Forward) * halfExtent.Z);

            /*
            // Calculate the warp point at the start of the bounding box in the supplied direction
            Vector3D warpPoint = gridBoundingBox.Center - (normalizedDirection * halfExtentInDirection) + (normalizedDirection * offset);
            */

            // Determine the warp point based on the 'back' parameter
            Vector3D warpPoint;
            if (forwardOffset)
            {
                // Back of the bounding box
                warpPoint = gridBoundingBox.Center - (normalizedDirection * halfExtentInDirection) + (normalizedDirection * offset);
            }
            else
            {
                // Front of the bounding box with inverted forward half-extent offset
                warpPoint = gridBoundingBox.Center + (normalizedDirection * halfExtentInDirection) + (normalizedDirection * offset);
            }

            return warpPoint;
        }

        private MyParticleEffect SpawnParticleEffectsXXX(MyCubeGrid grid, string subtypeId, MatrixD localMatrix, uint parentId = uint.MaxValue)
        {
            /*
                if (grid == null)
                    return null;

                // Get the grid's bounding box
                BoundingBoxD gridBoundingBox = grid.PositionComp.WorldAABB;

                // Calculate the offset between the grid's bounding box center and its current position
                Vector3D boundingBoxCenter = gridBoundingBox.Center;
                Vector3D offset = grid.WorldMatrix.Translation - boundingBoxCenter;

                // Adjust the world position for the particle effect
                Vector3D worldPos = grid.WorldMatrix.Translation + offset;

                // Create the particle effect
                MyParticleEffect effect;
                //uint parentId = uint.MaxValue; // No specific parent entity
                if (!MyParticlesManager.TryCreateParticleEffect(subtypeId, ref localMatrix, ref worldPos, parentId != uint.MaxValue ? grid.Render.GetRenderObjectID() : parentId, out effect))
                    return null;

                return effect;
            */
            return null;
        }

        private MyParticleEffect SpawnParticleEffect(MyEntity parent, string subtypeId, MatrixD localMatrix)
        {
            //MyAPIGateway.Utilities.ShowNotification("TL: Spawn Particles 2");
            //MatrixD worldMatrix = localMatrix * parent.WorldMatrix;
            MyParticleEffect effect;
            //Vector3D worldPos = parent.GetPosition();
            Vector3D worldPos = parent.WorldMatrix.Translation;
            uint parentId = parent.Render.GetRenderObjectID();
            //uint parentId = uint.MaxValue;

            if (parent != null)
            {
                // Get the parent's bounding box in world coordinates
                BoundingBoxD parentBoundingBox = parent.PositionComp.WorldAABB;

                // Calculate the center of the bounding box
                Vector3D boundingBoxCenter = parentBoundingBox.Center;

                // Transform the center to the parent's local space
                Vector3D localBoundingBoxCenter = Vector3D.Transform(boundingBoxCenter, parent.PositionComp.WorldMatrixInvScaled);

                // Set the localMatrix.Translation to the local bounding box center
                localMatrix.Translation = localBoundingBoxCenter;
            }

            if (!MyParticlesManager.TryCreateParticleEffect(subtypeId, ref localMatrix, ref worldPos, parentId, out effect))
                return null;
            //effect.UserScale = 0.1f;
            return effect;
        }

        private MyParticleEffect SpawnParticleEffects(MyEntity parent, string subtypeId, MatrixD localMatrix, Vector3D offset)
        {
            //MyAPIGateway.Utilities.ShowNotification("TL: Spawn Particles 2");
            //MatrixD worldMatrix = localMatrix * parent.WorldMatrix;
            MyParticleEffect effect;
            //Vector3D worldPos = parent.GetPosition();
            Vector3D worldPos = parent.WorldMatrix.Translation;
            uint parentId = parent.Render.GetRenderObjectID();
            //uint parentId = uint.MaxValue;

            if (parent != null)
            {
                // Get the parent's bounding box in world coordinates
                BoundingBoxD parentBoundingBox = parent.PositionComp.WorldAABB;

                // Calculate the center of the bounding box
                Vector3D boundingBoxCenter = parentBoundingBox.Center;

                // Transform the center to the parent's local space
                Vector3D localBoundingBoxCenter = Vector3D.Transform(offset, parent.PositionComp.WorldMatrixInvScaled);

                // Set the localMatrix.Translation to the local bounding box center
                localMatrix.Translation = localBoundingBoxCenter;
            }

            if (!MyParticlesManager.TryCreateParticleEffect(subtypeId, ref localMatrix, ref worldPos, parentId, out effect))
                return null;
            //effect.UserScale = 0.1f;
            return effect;
        }

        private MyParticleEffect SpawnParticleEffectsXXX(MyEntity parent, string subtypeId, MatrixD localMatrix)
        {
            MyAPIGateway.Utilities.ShowMessage("TL", "Spawn Particles 1");
            //MatrixD worldMatrix = localMatrix * parent.WorldMatrix;
            MyParticleEffect effect;
            //Vector3D worldPos = parent.GetPosition();
            Vector3D worldPos = parent.WorldMatrix.Translation;
            //uint parentId = parent.Render.GetRenderObjectID();
            uint parentId = uint.MaxValue;
            if (!MyParticlesManager.TryCreateParticleEffect(subtypeId, ref localMatrix, ref worldPos, parentId, out effect))
                return null;
            //effect.UserScale = 0.1f;
            return effect;
        }

        private void StopParticleEffects(MyParticleEffect effect)
        {
            if (effect != null)
            {
                effect.StopEmitting();
            }
        }

        private void PauseParticleEffects(MyParticleEffect effect)
        {
            if (effect != null)
            {
                effect.Pause();
            }
        }

        private void PlayParticleEffects(MyParticleEffect effect)
        {
            if (effect != null)
            {
                effect.Play();
            }
        }

        private void ResetParticleEffects(MyParticleEffect effect)
        {
            if (effect != null)
            {
                effect.StopEmitting();
                effect.Play();
                effect.Update();
            }
        }

        public void RemoveParticleEffects(ref MyParticleEffect effect)
        {
            if (effect != null)
            {
                MyParticlesManager.RemoveParticleEffect(effect);
                effect.Stop();
                effect.Close();
                effect = null;
                //MyVisualScriptLogicProvider.StopSound("ParticleElectrical", true);
            }
        }
        #endregion

        /*
        if(grid.Speed > 100)
        {
            grid.Physics.SetSpeeds(Vector3D.ClampToSphere(grid.Physics.LinearVelocity, 100), grid.Physics.AngularVelocity);
        }
        */

        /*
        
         var grid = someEntity as MyCubeGrid;
         if (grid != null)
         {
             var logic = grid.GameLogic.GetAs<CustomGridLogic>();
             if (logic != null)
             {
                 // Interact with the custom logic
             }
         }
        
        */

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            // Check if the message starts with the "/starengine" prefix
            if (messageText.ToLower().StartsWith("/starengine"))
            {
                // Prevent the message from being sent to other players
                sendToOthers = false;

                // Parse the command
                string[] args = messageText.Split(' ');
                if (args.Length > 2)
                {
                    string command = args[1].ToLower();
                    string value = args[2];

                    // Handle specific commands
                    switch (command)
                    {
                        case "Command":
                            /*
                            MyAPIGateway.Utilities.ShowMessage("Star Engine", $"Commands:" +
                                $"\ninfinitepower: {InfinitePower}" +
                                $"\ncustompowerloadformula: {CustomPowerLoadFormula}" +
                                $"\nreactivesun: {ReactiveSun}" +
                                $"\nsunparticlesize: {SunParticleSize}" +
                                $"\nsununderloadscalar: {SunUnderLoadScalar}" +
                                $"\nmagnetparticlesize: {MagnetParticleSize}" +
                                $"\nspeedmultiplier: {SpeedMultiplier}" +
                                $"\nonspeed: {OnSpeed}" +
                                $"\nidlespeed: {IdleSpeed}" +
                                $"\noffspeed: {OffSpeed}" +
                                $"\nspeed: {Speed}");
                            */
                            break;

                        default:
                            MyAPIGateway.Utilities.ShowMessage("Star Engine", $"Unknown command: {command}");
                            break;
                    }
                }
                else
                {
                    MyAPIGateway.Utilities.ShowMessage("Star Engine", "Usage: /starengine <setting> <value>");
                }
            }
        }
    }
}