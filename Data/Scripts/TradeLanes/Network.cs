using System;
using System.Collections.Generic;
using ProtoBuf;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

// Multiplayer support.
//
// The server (or the single player game) is authoritative: it pairs the Trade Lane
// Computers, runs the docking sequence and moves the ships. Clients only draw the
// rings and play the effects. What they cannot work out on their own comes from
// the server through this channel:
//
// - LaneInfo: where a lane starts and leads, and its Custom Data. A client draws
//   the rings of every lane from a stand-in built from it (ClientLanes), since
//   it has a computer only while it is within sync distance of it.
// - ShipSystem: a state change of a ship in a lane, for its sounds and particles.
// - GateEffect: particles on a ring when a ship requests docking or cancels it.
// - Notification: HUD text and chat lines for the players aboard the ship.
// - SeatLock: tells a client its USE control (F) is blocked while it travels.
// - SeatUnlockRequest: a client whose pilot is out of the seat anyway asks the
//   server to lift that block, only the server can.
// - Command: an admin's /tradelanes chat command, run on the server.
// - Detonate: a client ran a warhead's Detonate, the server drops the ship out
//   of its lane first if it is in one (DropOut.cs).
//
// On a cluster a node also sends the lanes it knows from the lane registry
// (Registry.cs), whose computers are loaded elsewhere or not at all.

namespace Psycho.TradeLanes
{
    public enum MessageKind
    {
        LaneInfo,
        LaneInfoRequest,
        ShipSystem,
        GateEffect,
        Notification,
        SeatLock,
        SeatUnlockRequest,
        Command,
        Detonate,
    }

    public enum GateEffectKind
    {
        RequestDocking,
        CancelDocking,
        StageChange,
    }

    [ProtoContract]
    public class TradeLaneMessage
    {
        [ProtoMember(1)]
        public MessageKind Kind;

        [ProtoMember(2)]
        public long EntityId;

        [ProtoMember(3)]
        public long OtherId;

        [ProtoMember(4)]
        public int Number;

        [ProtoMember(5)]
        public int Index;

        [ProtoMember(6)]
        public bool Flag;

        [ProtoMember(7)]
        public string Text;

        [ProtoMember(8)]
        public string Sender;

        [ProtoMember(9)]
        public double[] Vectors;
    }

    public static class TradeLaneNetwork
    {
        const ushort Channel = 34701;

        // A ship's status line repeats every frame, it goes out this often
        const int StatusInterval = 6;

        // and stays on the client's HUD this long after the last one
        const int StatusLifetimeMs = 300;

        // A client asks for every lane on these frames after the session starts
        static readonly int[] LaneRequestFrames = { 120, 1200 };

        const string UseControl = "USE";

        static readonly Dictionary<long, int> StatusSentAt = new Dictionary<long, int>();
        static readonly List<IMyPlayer> Players = new List<IMyPlayer>();

        // Client: the stand-ins drawing the lanes, by computer block id
        static readonly Dictionary<long, TradeLaneComputerBlockLogic> ClientLanes =
            new Dictionary<long, TradeLaneComputerBlockLogic>();

        static int Frame;
        static string StatusText;
        static int StatusUntil;
        static bool SeatLocked;

        public static bool IsServer => MyAPIGateway.Session.IsServer;

        static bool IsMultiplayer => MyAPIGateway.Multiplayer.MultiplayerActive;

        public static void Load()
        {
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(Channel, OnMessage);
            MyAPIGateway.Utilities.MessageEnteredSender += OnChat;
        }

        public static void Unload()
        {
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(Channel, OnMessage);
            MyAPIGateway.Utilities.MessageEnteredSender -= OnChat;
            foreach (var lane in ClientLanes.Values)
                lane.Close();
            ClientLanes.Clear();
            StatusSentAt.Clear();
            StatusText = null;

            // No need to lift the F block: MySession.Unload clears every input block
            SeatLocked = false;
        }

        // Called every frame from the session component
        public static void Update()
        {
            Frame++;
            if (StatusText != null)
            {
                if (Frame * 1000 / 60 < StatusUntil)
                    MyAPIGateway.Utilities.ShowNotification(StatusText, 16);
                else
                    StatusText = null;
            }

            if (SeatLocked && Frame % 60 == 0)
                CheckSeatLock();

            if (IsServer)
                return;

            if (Array.IndexOf(LaneRequestFrames, Frame) >= 0)
                RequestLaneInfo(0);

            foreach (var lane in ClientLanes.Values)
            {
                lane.UpdateAfterSimulation();
                if (Frame % 100 == 0)
                    lane.UpdateAfterSimulation100();
            }
        }

        #region Server side

        public static void SendLaneInfo(TradeLaneMessage laneInfo, ulong to = 0)
        {
            if (IsServer && IsMultiplayer)
                Send(laneInfo, to);
        }

        public static void SendShipSystem(IMyCubeGrid grid, CustomGridLogic.ShipSystem state)
        {
            if (!IsServer || !IsMultiplayer)
                return;

            Send(
                new TradeLaneMessage
                {
                    Kind = MessageKind.ShipSystem,
                    EntityId = grid.EntityId,
                    Number = (int)state,
                }
            );
        }

        public static void SendGateEffect(
            long computerId,
            int gateIndex,
            IMyCubeGrid grid,
            GateEffectKind kind
        )
        {
            if (!IsServer || !IsMultiplayer)
                return;

            Send(
                new TradeLaneMessage
                {
                    Kind = MessageKind.GateEffect,
                    EntityId = computerId,
                    OtherId = grid?.EntityId ?? 0,
                    Index = gateIndex,
                    Number = (int)kind,
                }
            );
        }

        // Shows a HUD notification, or a chat line when sender is given, to the
        // players aboard the grid. A 16 ms notification is a status line the caller
        // repeats every frame.
        public static void Notify(
            IMyCubeGrid grid,
            string text,
            int ms = 2000,
            string sender = null
        )
        {
            if (!IsMultiplayer)
            {
                // Single player: everything shows, as it always did
                ShowLocal(text, ms, sender);
                return;
            }

            if (grid == null)
                return;

            bool status = sender == null && ms <= 16;
            bool send = true;
            if (status)
            {
                int last;
                if (
                    StatusSentAt.TryGetValue(grid.EntityId, out last)
                    && Frame - last < StatusInterval
                )
                    send = false;
                else
                    StatusSentAt[grid.EntityId] = Frame;
            }

            Players.Clear();
            MyAPIGateway.Players.GetPlayers(Players, player => IsAboard(player, grid));
            foreach (var player in Players)
            {
                if (player == MyAPIGateway.Session.Player)
                {
                    ShowLocal(text, ms, sender);
                    continue;
                }

                if (!send)
                    continue;

                Send(
                    new TradeLaneMessage
                    {
                        Kind = MessageKind.Notification,
                        Text = text,
                        Number = status ? StatusLifetimeMs : ms,
                        Flag = status,
                        Sender = sender,
                    },
                    player.SteamUserId
                );
            }
            Players.Clear();
        }

        // Keeps the players seated on the grid in their seats: their USE control (F)
        // is blocked until the grid leaves the lane. Returns their identities, the
        // ship keeps them with its trip. Players who come aboard later are not locked.
        public static List<long> LockSeats(IMyCubeGrid grid)
        {
            var identities = new List<long>();
            Players.Clear();
            MyAPIGateway.Players.GetPlayers(Players, player => IsSeated(player, grid));
            foreach (var player in Players)
                identities.Add(player.IdentityId);
            Players.Clear();

            SetSeatLock(identities, true);
            return identities;
        }

        // Blocks or frees the USE control of these players. The game syncs the
        // block to the player's client, if the player is online.
        public static void SetSeatLock(List<long> identities, bool locked)
        {
            if (!IsServer || identities == null)
                return;

            MyLog.Default.WriteLine(
                $"TradeLanes: seat lock {(locked ? "on" : "off")} for [{string.Join(", ", identities)}]"
            );
            foreach (var identity in identities)
            {
                MyVisualScriptLogicProvider.SetPlayerInputBlacklistState(
                    UseControl,
                    identity,
                    !locked
                );

                // So the client knows its F is blocked
                Players.Clear();
                MyAPIGateway.Players.GetPlayers(Players, player => player.IdentityId == identity);
                foreach (var player in Players)
                {
                    if (player == MyAPIGateway.Session.Player)
                        SeatLocked = locked;
                    else if (IsMultiplayer)
                        Send(
                            new TradeLaneMessage { Kind = MessageKind.SeatLock, Flag = locked },
                            player.SteamUserId
                        );
                }
                Players.Clear();
            }
        }

        static bool IsAboard(IMyPlayer player, IMyCubeGrid grid)
        {
            var controlled = player.Controller?.ControlledEntity?.Entity as IMyCubeBlock;
            if (controlled?.CubeGrid == grid)
                return true;

            return IsSeated(player, grid);
        }

        static bool IsSeated(IMyPlayer player, IMyCubeGrid grid)
        {
            var seat = player.Character?.Parent as IMyCubeBlock;
            return seat?.CubeGrid == grid;
        }

        static void Send(TradeLaneMessage message, ulong to = 0)
        {
            var data = MyAPIGateway.Utilities.SerializeToBinary(message);
            if (to == 0)
                MyAPIGateway.Multiplayer.SendMessageToOthers(Channel, data);
            else
                MyAPIGateway.Multiplayer.SendMessageTo(Channel, data, to);
        }

        #endregion

        #region Client side

        // Asks the server where a computer's lane leads, 0 asks for every lane.
        // The answer is a LaneInfo per lane.
        public static void RequestLaneInfo(long computerId)
        {
            if (IsServer)
                return;

            var data = MyAPIGateway.Utilities.SerializeToBinary(
                new TradeLaneMessage { Kind = MessageKind.LaneInfoRequest, EntityId = computerId }
            );
            MyAPIGateway.Multiplayer.SendMessageToServer(Channel, data);
        }

        // Instead of the vanilla DetonateRequest, see WarheadLogic
        public static void RequestDetonate(IMyTerminalBlock warhead)
        {
            var data = MyAPIGateway.Utilities.SerializeToBinary(
                new TradeLaneMessage { Kind = MessageKind.Detonate, EntityId = warhead.EntityId }
            );
            MyAPIGateway.Multiplayer.SendMessageToServer(Channel, data);
        }

        static void ApplyLaneInfo(TradeLaneMessage message)
        {
            // The computer itself, when it is streamed in, only turns its grid
            Computer(message.EntityId)?.ApplyLaneInfo(message);

            TradeLaneComputerBlockLogic lane;
            ClientLanes.TryGetValue(message.EntityId, out lane);
            if (!message.Flag)
            {
                if (lane != null)
                {
                    lane.Close();
                    ClientLanes.Remove(message.EntityId);
                }
                return;
            }

            if (lane == null)
            {
                lane = new TradeLaneComputerBlockLogic();
                lane.InitStandIn();
                ClientLanes[message.EntityId] = lane;
            }
            lane.ApplyLaneInfo(message);
        }

        // The ship lifts the block when it leaves the lane. A pilot who is out of
        // the seat anyway, because the ship was removed or the character died,
        // gets F back here.
        static void CheckSeatLock()
        {
            var player = MyAPIGateway.Session.Player;
            if (player == null || player.Character?.Parent is IMyCockpit)
                return;

            SeatLocked = false;
            MyLog.Default.WriteLine("TradeLanes: out of the seat, lifting the seat lock");
            if (IsServer)
                MyVisualScriptLogicProvider.SetPlayerInputBlacklistState(
                    UseControl,
                    player.IdentityId,
                    true
                );
            else
                MyAPIGateway.Multiplayer.SendMessageToServer(
                    Channel,
                    MyAPIGateway.Utilities.SerializeToBinary(
                        new TradeLaneMessage { Kind = MessageKind.SeatUnlockRequest }
                    )
                );
        }

        static void ShowLocal(string text, int ms, string sender)
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            if (sender != null)
                MyAPIGateway.Utilities.ShowMessage(sender, text);
            else
                MyAPIGateway.Utilities.ShowNotification(text, ms);
        }

        #endregion

        static void OnMessage(ushort channel, byte[] data, ulong sender, bool fromServer)
        {
            try
            {
                var message = MyAPIGateway.Utilities.SerializeFromBinary<TradeLaneMessage>(data);
                if (message == null)
                    return;

                if (message.Kind == MessageKind.LaneInfoRequest)
                {
                    if (!IsServer)
                        return;
                    if (message.EntityId != 0)
                    {
                        var computer = Computer(message.EntityId);
                        if (computer != null)
                            computer.SendLaneInfo(sender);
                        else
                            LaneRegistry.SendLaneInfo(message.EntityId, sender);
                        return;
                    }
                    foreach (var block in TradeLaneComputerBlockLogic.TradeLanes)
                        block
                            ?.GameLogic?.GetAs<TradeLaneComputerBlockLogic>()
                            ?.SendLaneInfo(sender);
                    LaneRegistry.SendLaneInfos(sender);
                    return;
                }

                if (message.Kind == MessageKind.Command)
                {
                    if (IsServer)
                        RunCommand(sender, message.Text);
                    return;
                }

                if (message.Kind == MessageKind.Detonate)
                {
                    if (IsServer)
                        Detonate(message.EntityId, sender);
                    return;
                }

                if (message.Kind == MessageKind.SeatUnlockRequest)
                {
                    var identity = MyAPIGateway.Players.TryGetIdentityId(sender);
                    MyLog.Default.WriteLine(
                        $"TradeLanes: seat lock off for {identity}, asked by the client"
                    );
                    if (IsServer && identity != 0)
                        MyVisualScriptLogicProvider.SetPlayerInputBlacklistState(
                            UseControl,
                            identity,
                            true
                        );
                    return;
                }

                // Everything else is the server's word
                if (!fromServer || IsServer)
                    return;

                switch (message.Kind)
                {
                    case MessageKind.LaneInfo:
                        ApplyLaneInfo(message);
                        break;

                    case MessageKind.ShipSystem:
                        var grid = Entity(message.EntityId) as MyCubeGrid;
                        grid?.GameLogic?.GetAs<CustomGridLogic>()
                            ?.ExecShipSystems((CustomGridLogic.ShipSystem)message.Number);
                        break;

                    case MessageKind.GateEffect:
                        TradeLaneComputerBlockLogic lane;
                        if (ClientLanes.TryGetValue(message.EntityId, out lane))
                            lane.GateEffect(
                                message.Index,
                                Entity(message.OtherId) as MyCubeGrid,
                                (GateEffectKind)message.Number
                            );
                        break;

                    case MessageKind.Notification:
                        if (message.Flag)
                        {
                            StatusText = message.Text;
                            StatusUntil = Frame * 1000 / 60 + message.Number;
                        }
                        else
                        {
                            ShowLocal(message.Text, message.Number, message.Sender);
                        }
                        break;

                    case MessageKind.SeatLock:
                        MyLog.Default.WriteLine(
                            $"TradeLanes: the server set the seat lock {message.Flag}"
                        );
                        // The server already blocked the control, this only keeps track
                        SeatLocked = message.Flag;
                        break;
                }
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES Network", e.Message);
            }
        }

        // /tradelanes forget <TLID>: drops every computer of the lane from the
        // cluster's lane registry, for a computer whose whole grid was deleted.
        // The registry cannot tell that from a grid unloading on its node.
        static void OnChat(ulong sender, string text, ref bool sendToOthers)
        {
            if (!text.StartsWith("/tradelanes", StringComparison.OrdinalIgnoreCase))
                return;
            sendToOthers = false;
            if (IsServer)
                RunCommand(sender, text);
            else
                MyAPIGateway.Multiplayer.SendMessageToServer(
                    Channel,
                    MyAPIGateway.Utilities.SerializeToBinary(
                        new TradeLaneMessage { Kind = MessageKind.Command, Text = text }
                    )
                );
        }

        static void RunCommand(ulong sender, string text)
        {
            string answer;
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var local =
                MyAPIGateway.Session.Player != null
                && MyAPIGateway.Session.Player.SteamUserId == sender;
            if (!local && MyAPIGateway.Session.GetUserPromoteLevel(sender) < MyPromoteLevel.Admin)
                answer = "Only an admin can do that.";
            else if (
                words.Length == 3
                && words[1].Equals("forget", StringComparison.OrdinalIgnoreCase)
            )
                answer = LaneRegistry.Forget(words[2]);
            else
                answer = "Usage: /tradelanes forget <TLID>";

            MyLog.Default.WriteLine($"TradeLanes: {sender} ran '{text}': {answer}");
            if (local)
                ShowLocal(answer, 0, "Trade Lanes");
            else
                Send(
                    new TradeLaneMessage
                    {
                        Kind = MessageKind.Notification,
                        Text = answer,
                        Sender = "Trade Lanes",
                    },
                    sender
                );
        }

        // The checks of the vanilla DetonateRequest: access to the block, armed
        static void Detonate(long warheadId, ulong sender)
        {
            var warhead = Entity(warheadId) as IMyWarhead;
            var identity = MyAPIGateway.Players.TryGetIdentityId(sender);
            if (warhead == null || !warhead.IsArmed || !warhead.HasPlayerAccess(identity))
                return;
            CustomGridLogic.DropOutThen(
                warhead.CubeGrid,
                "a warhead was detonated",
                warhead.Detonate
            );
        }

        static IMyEntity Entity(long id)
        {
            IMyEntity entity;
            return id != 0 && MyAPIGateway.Entities.TryGetEntityById(id, out entity)
                ? entity
                : null;
        }

        static TradeLaneComputerBlockLogic Computer(long id)
        {
            return (
                Entity(id) as IMyTerminalBlock
            )?.GameLogic?.GetAs<TradeLaneComputerBlockLogic>();
        }
    }
}
