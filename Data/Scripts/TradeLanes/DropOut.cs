using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

// A ship in a lane is teleported every frame, and the game does not expect that
// of a ship that changes. A queued explosion goes off where the ship was, a split
// off piece or a detached subgrid stays behind. So the ship drops out of the lane
// first, the same way it arrives at the gate, and the change happens in regular
// space:
//
// - before a block of the ship's Physical group is destroyed or ground off
//   (damage handler), or removed some other way (OnBlockRemoved),
// - on a grid split (OnGridSplit),
// - when a grid leaves the Physical group: a rotor, hinge or piston head detached,
//   a connector or landing gear unlocked (checked every frame before the teleport),
// - before an armed warhead goes off: on the Detonate action, when a countdown is
//   about to run out, and on any damage to it.
//
// A programmable block calling IMyWarhead.Detonate() goes around all of this.
//
// A warhead set off right after a teleport misses too. MyWarhead.GetExplosionSphere
// looks up the warheads around it in the game's pruning structure, which has the
// ship where it was until the next simulation step. It finds none and centers the
// explosion on the world origin. So a warhead of a ship that was in a lane goes
// off a frame after the drop-out (DropOutThen).

namespace Psycho.TradeLanes
{
    public partial class CustomGridLogic
    {
        // Every grid of a ship in a lane, with the ship that drives the trip
        static readonly Dictionary<IMyCubeGrid, CustomGridLogic> LaneGrids =
            new Dictionary<IMyCubeGrid, CustomGridLogic>();

        // A warhead counting down drops out this many seconds before it would go off
        const float CountdownMargin = 2 * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS;

        // Late in the queue, so that damage other mods take away does not count
        const int DamageHandlerPriority = 1000000;

        // Grids that dropped out of a lane in this frame, by frame, and the
        // detonations waiting for the game to catch up with them
        static readonly Dictionary<IMyCubeGrid, int> Settling = new Dictionary<IMyCubeGrid, int>();
        static readonly List<MyTuple<int, Action>> Later = new List<MyTuple<int, Action>>();
        static readonly List<Action> Due = new List<Action>();

        static int Frame => MyAPIGateway.Session.GameplayFrameCounter;

        readonly HashSet<IMyCubeGrid> Watched = new HashSet<IMyCubeGrid>();
        readonly List<IMyWarhead> Warheads = new List<IMyWarhead>();
        readonly List<IMyCubeGrid> GroupGrids = new List<IMyCubeGrid>();
        readonly List<IMyCubeGrid> Gone = new List<IMyCubeGrid>();

        // Server: the ship leaves the lane where it is, like at the gate
        public void DropOut(string reason)
        {
            if (Transit == null)
                return;
            MyLog.Default.WriteLine(
                $"TradeLanes: {Grid.DisplayName} ({Grid.EntityId}) dropped out of the lane: {reason}"
            );
            foreach (var grid in Watched)
                Settling[grid] = Frame;
            Disengage(LaneDirection(), "Dropped out of the lane.");
        }

        // Server: drops out the ship the grid belongs to, if it is in a lane
        static void DropOut(IMyCubeGrid grid, string reason)
        {
            CustomGridLogic ship;
            if (grid != null && LaneGrids.TryGetValue(grid, out ship))
                ship.DropOut(reason);
        }

        // In a lane or just out of it: the pruning structure may be a teleport behind
        static bool Teleported(IMyCubeGrid grid)
        {
            return grid != null && (LaneGrids.ContainsKey(grid) || Settling.ContainsKey(grid));
        }

        // Server: sets off a warhead, a frame after the drop-out if its ship is in a lane
        public static void DropOutThen(IMyCubeGrid grid, string reason, Action detonate)
        {
            if (!Teleported(grid))
            {
                detonate();
                return;
            }
            DropOut(grid, reason);
            Later.Add(MyTuple.Create(Frame, detonate));
        }

        // Server, every frame, after the simulation step
        public static void UpdateSettling()
        {
            if (Settling.Count == 0 && Later.Count == 0)
                return;
            var frame = Frame;
            foreach (var item in Later)
                if (item.Item1 < frame)
                    Due.Add(item.Item2);
            Later.RemoveAll(item => item.Item1 < frame);
            foreach (var grid in new List<IMyCubeGrid>(Settling.Keys))
                if (Settling[grid] < frame)
                    Settling.Remove(grid);
            foreach (var action in Due)
                action();
            Due.Clear();
        }

        public static void RegisterDamageHandler()
        {
            MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(
                DamageHandlerPriority,
                BeforeDamage
            );
        }

        public static void Unload()
        {
            LaneGrids.Clear();
            Settling.Clear();
            Later.Clear();
        }

        static void BeforeDamage(object target, ref MyDamageInformation info)
        {
            if (
                LaneGrids.Count == 0 && Settling.Count == 0
                || info.Amount <= 0
                || info.IsDeformation
            )
                return;

            // The game asks an armed warhead first, any damage sets it off. The
            // damage comes again a frame later, see DropOutThen.
            var warhead = target as IMyWarhead;
            if (warhead != null)
            {
                if (!Teleported(warhead.CubeGrid))
                    return;
                var amount = info.Amount;
                var type = info.Type;
                var attacker = info.AttackerId;
                info.Amount = 0;
                DropOutThen(
                    warhead.CubeGrid,
                    "an armed warhead was hit",
                    () =>
                    {
                        if (!warhead.Closed)
                            warhead.SlimBlock.DoDamage(amount, type, true, null, attacker);
                    }
                );
                return;
            }

            var block = target as IMySlimBlock;
            CustomGridLogic ship;
            if (
                block != null
                && LaneGrids.TryGetValue(block.CubeGrid, out ship)
                && Destroys(block, ref info)
            )
                ship.DropOut($"a block is about to be destroyed ({info.Type})");
        }

        // The amount is already scaled by the block's and the grid's damage modifiers
        static bool Destroys(IMySlimBlock block, ref MyDamageInformation info)
        {
            if (info.Type != MyDamageType.Grind)
                return info.Amount + block.AccumulatedDamage >= block.Integrity;

            // A grinder's amount becomes integrity in MySlimBlock.DecreaseMountLevel
            var definition = (MyCubeBlockDefinition)block.BlockDefinition;
            var ratio = block.FatBlock?.DisassembleRatio ?? definition.DisassembleRatio;
            return info.Amount / ratio * definition.IntegrityPointsPerSec >= block.Integrity;
        }

        // Server: keeps track of the grids of the ship's Physical group while it is
        // in a lane. Returns false when a grid left the group, that is a disconnect.
        bool WatchGroup()
        {
            GroupGrids.Clear();
            MyAPIGateway
                .GridGroups.GetGridGroup(GridLinkTypeEnum.Physical, Grid)
                ?.GetGrids(GroupGrids);
            if (GroupGrids.Count == 0)
                GroupGrids.Add(Grid);

            // Grids join as their mechanical links form after a load, that is fine
            foreach (var grid in GroupGrids)
                if (!Watched.Contains(grid))
                    Watch(grid);

            bool intact = true;
            Gone.Clear();
            foreach (var grid in Watched)
                if (!GroupGrids.Contains(grid))
                    Gone.Add(grid);
            // A grid that closes is deleted or unloads, nothing to fix in space. One
            // that came off stays watched until the drop-out, which gives it the
            // ship's speed.
            foreach (var grid in Gone)
                if (grid.MarkedForClose)
                    Unwatch(grid);
                else
                    intact = false;
            return intact;
        }

        void Watch(IMyCubeGrid grid)
        {
            Watched.Add(grid);
            LaneGrids[grid] = this;
            grid.OnBlockAdded += OnBlockAdded;
            grid.OnBlockRemoved += OnBlockRemoved;
            grid.OnGridSplit += OnGridSplit;
            // Collects nothing, only visits the blocks
            grid.GetBlocks(
                null,
                block =>
                {
                    OnBlockAdded(block);
                    return false;
                }
            );
        }

        void Unwatch(IMyCubeGrid grid)
        {
            Watched.Remove(grid);
            CustomGridLogic ship;
            if (LaneGrids.TryGetValue(grid, out ship) && ship == this)
                LaneGrids.Remove(grid);
            grid.OnBlockAdded -= OnBlockAdded;
            grid.OnBlockRemoved -= OnBlockRemoved;
            grid.OnGridSplit -= OnGridSplit;
            Warheads.RemoveAll(warhead => warhead.CubeGrid == grid);
        }

        void UnwatchAll()
        {
            Gone.Clear();
            Gone.AddRange(Watched);
            foreach (var grid in Gone)
                Unwatch(grid);
            Warheads.Clear();
        }

        void OnBlockAdded(IMySlimBlock block)
        {
            var warhead = block.FatBlock as IMyWarhead;
            if (warhead != null)
                Warheads.Add(warhead);
        }

        void OnBlockRemoved(IMySlimBlock block)
        {
            if (block.CubeGrid.MarkedForClose)
                return;
            // A connector or an advanced rotor coming apart reports the block on
            // the other side as removed, that one is still there
            if (block.CubeGrid.GetCubeBlock(block.Position) == block)
                DropOut("a grid disconnected");
            else
                DropOut(
                    $"a block was removed ({block.BlockDefinition.Id.SubtypeName} on {block.CubeGrid.DisplayName})"
                );
        }

        void OnGridSplit(IMyCubeGrid original, IMyCubeGrid split)
        {
            DropOut("the grid split");
        }

        // The game counts a warhead down before the grids update, so this is the
        // last frame to get out before it goes off
        bool WarheadAboutToGoOff()
        {
            foreach (var warhead in Warheads)
                if (
                    !warhead.Closed
                    && warhead.IsCountingDown
                    && warhead.DetonationTime <= CountdownMargin
                )
                    return true;
            return false;
        }
    }

    // Wraps the vanilla Detonate button and toolbar action of the warheads. The
    // terminal controls exist once the first warhead is created, so the first
    // warhead installs the wrapper. The game clears the controls on unload.
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Warhead), false)]
    public class WarheadLogic : MyGameLogicComponent
    {
        static bool Installed;
        static System.Action<IMyTerminalBlock> VanillaDetonate;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            if (Installed)
                return;
            Installed = true;

            List<IMyTerminalControl> controls;
            MyAPIGateway.TerminalControls.GetControls<IMyWarhead>(out controls);
            foreach (var control in controls)
            {
                var button = control as IMyTerminalControlButton;
                if (button?.Id != "Detonate")
                    continue;
                VanillaDetonate = button.Action;
                button.Action = Detonate;
            }

            List<IMyTerminalAction> actions;
            MyAPIGateway.TerminalControls.GetActions<IMyWarhead>(out actions);
            foreach (var action in actions)
                if (action.Id == "Detonate" && VanillaDetonate != null)
                    action.Action = Detonate;
        }

        public static void Unload()
        {
            Installed = false;
            VanillaDetonate = null;
        }

        // A client cannot tell whether the ship is in a lane, the server can. The
        // server drops it out and detonates it, the vanilla request would race
        // with the message.
        static void Detonate(IMyTerminalBlock block)
        {
            var warhead = block as IMyWarhead;
            if (warhead == null || !warhead.IsArmed)
                return;
            if (MyAPIGateway.Session.IsServer)
                CustomGridLogic.DropOutThen(
                    warhead.CubeGrid,
                    "a warhead was detonated",
                    () => VanillaDetonate(block)
                );
            else
                TradeLaneNetwork.RequestDetonate(warhead);
        }
    }
}
