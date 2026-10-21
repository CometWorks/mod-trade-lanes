using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Utils;
using VRageMath;

// Lane registry on a server cluster's World Authority.
//
// On a cluster the two computers of a lane are loaded on different nodes, or
// not at all. Each node reports the computers it loads to the World Authority
// over the cluster plugin's mod channel. The World Authority pairs them by
// TLID, keeps the registry in its world storage and tells every node about
// changes. A node pairs a computer from the registry when its partner is not
// loaded there, and gives its clients every lane, not only the ones it has.
//
// Only the World Authority receives requests on the channel, so the process
// that gets one is the World Authority. Nothing here is active before it has
// answered. On a single server or in single player nothing answers, and the
// computers pair on their own as before.
//
// Deliveries can get lost (a node's handler is not registered yet, a node or
// the World Authority restarts), so every node reports all its computers and
// asks for the whole registry again every RefreshFrames.
//
// The rings of a lane are RingInterval (10 km) apart, far outside the station's
// partition, so a ship at a ring is in a partition of its own that the cluster
// can place on any node, and a node sees only the grids it loads. So every node
// docks the ships it has at the rings of each paired lane whose computer is not
// loaded there, through a stand-in built from the registry (StandIns).

namespace Psycho.TradeLanes
{
    [ProtoContract]
    public class LaneEntry
    {
        [ProtoMember(1)]
        public long Id;

        [ProtoMember(2)]
        public long GridId;

        [ProtoMember(3)]
        public string Tlid;

        // Block position, forward and up, grid position and up
        [ProtoMember(4)]
        public double[] Vectors;

        [ProtoMember(5)]
        public string CustomData;

        // Reported by the node: the computer is paired without the registry (its
        // partner is loaded there, or a stored target), with this rotation flag
        [ProtoMember(6)]
        public bool Paired;

        [ProtoMember(7)]
        public bool Inherit;

        // Set by the World Authority
        [ProtoMember(8)]
        public long Partner;

        [ProtoMember(9)]
        public bool InheritRotation;

        [ProtoMember(10)]
        public long Order;

        public Vector3D Vector(int index)
        {
            return new Vector3D(Vectors[index * 3], Vectors[index * 3 + 1], Vectors[index * 3 + 2]);
        }

        // What the node reported, rounded so float noise is not a change
        public string ReportKey()
        {
            var text = string.Join(
                ",",
                Vectors.Select(v => v.ToString("0.00", CultureInfo.InvariantCulture))
            );
            return $"{GridId}|{Tlid}|{text}|{Paired}|{Inherit}|{CustomData}";
        }
    }

    [ProtoContract]
    public class RegistryMessage
    {
        [ProtoMember(1)]
        public List<LaneEntry> Entries = new List<LaneEntry>();

        // Computers removed from their grid, or forgotten by an admin
        [ProtoMember(2)]
        public List<long> Removed = new List<long>();

        // A request asking for the whole registry in response
        [ProtoMember(3)]
        public bool Full;

        // A request to drop every computer of this TLID (admin command)
        [ProtoMember(4)]
        public string Forget;
    }

    public class RegistryStore
    {
        public List<LaneEntry> Entries = new List<LaneEntry>();
        public List<long> Removed = new List<long>();
        public long NextOrder;
    }

    public static class LaneRegistry
    {
        // The cluster plugin's mod channel ids ("CLUSMODS" and "CLUSMODR")
        const long SendId = 0x434C55534D4F4453;
        const long ReceiveId = 0x434C55534D4F4452;
        const string Channel = "TradeLanes";
        const string StoreFile = "LaneRegistry.xml";

        // The channel takes 16 KiB string payloads, this much protobuf fits in base64
        const int PayloadBytes = 12000;
        const int CustomDataLimit = 4000;
        const int RemovedLimit = 500;

        const int FlushFrames = 60;
        const int FirstRequestFrame = 300;
        const int SearchFrames = 600;
        const int RefreshFrames = 1800;

        // Node side. Active once the World Authority answered.
        public static bool Active;
        static readonly Dictionary<long, LaneEntry> Local = new Dictionary<long, LaneEntry>();
        static readonly Dictionary<long, string> Reported = new Dictionary<long, string>();
        static readonly HashSet<long> Dirty = new HashSet<long>();
        static readonly HashSet<long> PendingRemoved = new HashSet<long>();
        static readonly Dictionary<long, LaneEntry> Known = new Dictionary<long, LaneEntry>();
        static readonly HashSet<long> Removed = new HashSet<long>();
        static readonly Dictionary<long, string> ClientSent = new Dictionary<long, string>();
        static string PendingForget;

        // Node side: the stand-ins docking ships here, by computer id, and the lane info they have
        static readonly Dictionary<long, TradeLaneComputerBlockLogic> StandInLanes =
            new Dictionary<long, TradeLaneComputerBlockLogic>();
        static readonly Dictionary<long, string> StandInApplied = new Dictionary<long, string>();

        public static IEnumerable<TradeLaneComputerBlockLogic> StandIns => StandInLanes.Values;

        // World Authority side
        static bool IsAuthority;
        static RegistryStore Store = new RegistryStore();
        static readonly Dictionary<long, LaneEntry> Registry = new Dictionary<long, LaneEntry>();
        static readonly HashSet<long> Changed = new HashSet<long>();
        static readonly HashSet<long> NewlyRemoved = new HashSet<long>();
        static bool StoreDirty;

        static int Frame;
        static int NextRequestFrame = FirstRequestFrame;
        static long Correlation;

        public static void Load()
        {
            MyAPIGateway.Utilities.RegisterMessageHandler(ReceiveId, OnMessage);
        }

        public static void Unload()
        {
            MyAPIGateway.Utilities.UnregisterMessageHandler(ReceiveId, OnMessage);
            Active = IsAuthority = false;
            foreach (var set in new[] { Local, Known, Registry })
                set.Clear();
            Reported.Clear();
            ClientSent.Clear();
            foreach (var lane in StandInLanes.Values)
                lane.Close();
            StandInLanes.Clear();
            StandInApplied.Clear();
            Dirty.Clear();
            PendingRemoved.Clear();
            Removed.Clear();
            Changed.Clear();
            NewlyRemoved.Clear();
            Store = new RegistryStore();
        }

        // Called every frame on the server
        public static void Update()
        {
            Frame++;
            if (!IsAuthority && Frame >= NextRequestFrame)
            {
                NextRequestFrame = Frame + (Active ? RefreshFrames : SearchFrames);
                Request(Local.Keys.ToList(), true);
            }

            if (Active && !IsAuthority)
                UpdateStandIns();

            if (Frame % FlushFrames != 0)
                return;

            if (IsAuthority)
                FlushAuthority();
            else if (
                Active && (Dirty.Count > 0 || PendingRemoved.Count > 0 || PendingForget != null)
            )
                Request(Dirty.ToList(), false);
        }

        #region Node side

        // A computer loaded here, as it is now
        public static void Report(LaneEntry entry)
        {
            Local[entry.Id] = entry;
            DropStandIn(entry.Id);
            string key;
            if (!Reported.TryGetValue(entry.Id, out key) || key != entry.ReportKey())
                Dirty.Add(entry.Id);
        }

        // Closed with its grid: unloaded, handed to another node or deleted, which
        // cannot be told apart. The registry keeps it.
        public static void Unloaded(long id)
        {
            Local.Remove(id);
            Reported.Remove(id);
            Dirty.Remove(id);
        }

        // Removed from a grid that stays: the lane is gone
        public static void ComputerRemoved(long id)
        {
            Unloaded(id);
            PendingRemoved.Add(id);
            if (!Active)
                return;
            Known.Remove(id);
            Removed.Add(id);
        }

        // The admin command. Returns the answer for the admin.
        public static string Forget(string tlid)
        {
            if (!Active)
                return "No lane registry answered, this is not a cluster. Nothing to forget.";
            PendingForget = Normalize(tlid);
            return $"Asked the World Authority to forget every computer of TLID:{PendingForget}. Those that still exist register again within a minute.";
        }

        // The partner the registry gave this computer, if its TLID still matches
        public static LaneEntry Partner(long id, string tlid, out bool inherit)
        {
            inherit = false;
            LaneEntry own,
                partner;
            if (
                !Active
                || !Known.TryGetValue(id, out own)
                || own.Tlid != Normalize(tlid)
                || own.Partner == 0
                || !Known.TryGetValue(own.Partner, out partner)
            )
                return null;
            inherit = own.InheritRotation;
            return partner;
        }

        public static bool IsRemoved(long id)
        {
            return id != 0 && Removed.Contains(id);
        }

        public static string Normalize(string tlid)
        {
            return (tlid ?? "").Trim().ToLowerInvariant();
        }

        public static LaneEntry Entry(
            long id,
            long gridId,
            string tlid,
            MatrixD block,
            MatrixD grid,
            string customData,
            bool paired,
            bool inherit
        )
        {
            if (customData != null && customData.Length > CustomDataLimit)
                customData = customData.Substring(0, CustomDataLimit);
            return new LaneEntry
            {
                Id = id,
                GridId = gridId,
                Tlid = Normalize(tlid),
                Vectors = new[]
                {
                    block.Translation.X,
                    block.Translation.Y,
                    block.Translation.Z,
                    block.Forward.X,
                    block.Forward.Y,
                    block.Forward.Z,
                    block.Up.X,
                    block.Up.Y,
                    block.Up.Z,
                    grid.Translation.X,
                    grid.Translation.Y,
                    grid.Translation.Z,
                    grid.Up.X,
                    grid.Up.Y,
                    grid.Up.Z,
                },
                CustomData = customData ?? "",
                Paired = paired,
                Inherit = inherit,
            };
        }

        static void UpdateStandIns()
        {
            if (Frame % FlushFrames == 0)
                SyncStandIns();

            foreach (var lane in StandInLanes.Values)
            {
                lane.UpdateAfterSimulation();
                if (Frame % 100 == 0)
                    lane.UpdateAfterSimulation100();
            }
        }

        // A stand-in for every paired lane whose computer is not loaded here
        static void SyncStandIns()
        {
            foreach (var entry in Known.Values)
            {
                var info = Local.ContainsKey(entry.Id) ? null : LaneInfo(entry);
                if (info == null || !info.Flag)
                {
                    DropStandIn(entry.Id);
                    continue;
                }

                var key = InfoKey(info);
                string applied;
                if (StandInApplied.TryGetValue(entry.Id, out applied) && applied == key)
                    continue;

                TradeLaneComputerBlockLogic lane;
                if (!StandInLanes.TryGetValue(entry.Id, out lane))
                {
                    lane = new TradeLaneComputerBlockLogic();
                    lane.InitStandIn();
                    StandInLanes[entry.Id] = lane;
                    MyLog.Default.WriteLine($"TradeLanes: docking at computer {entry.Id}'s rings here, it is loaded elsewhere");
                }
                lane.ApplyLaneInfo(info);
                StandInApplied[entry.Id] = key;
            }

            foreach (var id in StandInLanes.Keys.Where(id => !Known.ContainsKey(id)).ToList())
                DropStandIn(id);
        }

        static void DropStandIn(long id)
        {
            TradeLaneComputerBlockLogic lane;
            if (!StandInLanes.TryGetValue(id, out lane))
                return;
            lane.Close();
            StandInLanes.Remove(id);
            StandInApplied.Remove(id);
            MyLog.Default.WriteLine($"TradeLanes: no longer docking at computer {id}'s rings here");
        }

        // A client asked for every lane: the ones not loaded here
        public static void SendLaneInfos(ulong to)
        {
            if (!Active)
                return;
            foreach (var entry in Known.Values)
            {
                if (Local.ContainsKey(entry.Id))
                    continue;
                var info = LaneInfo(entry);
                if (info.Flag)
                    TradeLaneNetwork.SendLaneInfo(info, to);
            }
        }

        public static void SendLaneInfo(long id, ulong to)
        {
            LaneEntry entry;
            if (Active && !Local.ContainsKey(id) && Known.TryGetValue(id, out entry))
                TradeLaneNetwork.SendLaneInfo(LaneInfo(entry), to);
        }

        static void Request(List<long> ids, bool full)
        {
            var entries = new List<LaneEntry>();
            foreach (var id in ids)
            {
                LaneEntry entry;
                if (!Local.TryGetValue(id, out entry))
                    continue;
                entries.Add(entry);
                Reported[id] = entry.ReportKey();
            }
            Dirty.Clear();

            var first = true;
            foreach (var message in Chunks(entries, PendingRemoved.Take(RemovedLimit).ToList()))
            {
                message.Full = full && first;
                message.Forget = first ? PendingForget : null;
                first = false;
                Send(new object[] { "request", Channel, Encode(message), ++Correlation });
            }
            PendingForget = null;
        }

        // A response or a broadcast: only the World Authority sends either
        static void OnRegistry(string origin, RegistryMessage message)
        {
            if (!Active)
            {
                Active = true;
                NextRequestFrame = Frame + RefreshFrames;
                MyLog.Default.WriteLine($"TradeLanes: the lane registry on {origin} answered");
            }

            foreach (var id in message.Removed)
            {
                PendingRemoved.Remove(id);
                Known.Remove(id);
                Removed.Add(id);
            }
            foreach (var entry in message.Entries)
            {
                Known[entry.Id] = entry;
                Removed.Remove(entry.Id);
            }
            if (message.Entries.Count > 0 || message.Removed.Count > 0)
                MyLog.Default.WriteLine(
                    $"TradeLanes: registry from {origin}: {message.Entries.Count} computers, {message.Removed.Count} removed, {Known.Count} known"
                );

            PublishToClients();
        }

        // Tells this node's clients about every lane whose computer is not loaded
        // here and that changed, a target moves when its partner does
        static void PublishToClients()
        {
            foreach (var entry in Known.Values)
            {
                if (Local.ContainsKey(entry.Id))
                    continue;
                var info = LaneInfo(entry);
                var key = InfoKey(info);
                string sent;
                var known = ClientSent.TryGetValue(entry.Id, out sent);
                if (sent == key || (!known && !info.Flag))
                    continue;
                ClientSent[entry.Id] = key;
                TradeLaneNetwork.SendLaneInfo(info);
            }

            foreach (var id in ClientSent.Keys.Where(id => !Known.ContainsKey(id)).ToList())
            {
                ClientSent.Remove(id);
                if (!Local.ContainsKey(id))
                    TradeLaneNetwork.SendLaneInfo(
                        new TradeLaneMessage { Kind = MessageKind.LaneInfo, EntityId = id }
                    );
            }
        }

        // What a lane info says, rounded so float noise is not a change
        static string InfoKey(TradeLaneMessage info)
        {
            return string.Join(
                    ",",
                    info.Vectors.Select(v => v.ToString("0.00", CultureInfo.InvariantCulture))
                ) + $"|{info.Flag}|{info.Number}|{info.Text}";
        }

        // The lane info a computer would send, from the registry
        static TradeLaneMessage LaneInfo(LaneEntry entry)
        {
            var target = Vector3D.Zero;
            var targetGrid = Vector3D.Zero;
            var targetUp = Vector3D.Zero;
            var inherit = false;
            var paired = TradeLaneComputerBlockLogic.ReadTargetGps(entry.CustomData, out target);
            if (paired)
            {
                targetGrid = target;
                targetUp = entry.Vector(4);
            }
            else
            {
                LaneEntry partner = null;
                paired = entry.Partner != 0 && Known.TryGetValue(entry.Partner, out partner);
                if (paired)
                {
                    target = partner.Vector(0);
                    targetGrid = partner.Vector(3);
                    targetUp = partner.Vector(4);
                    inherit = entry.InheritRotation;
                }
            }

            var v = entry.Vectors;
            return new TradeLaneMessage
            {
                Kind = MessageKind.LaneInfo,
                EntityId = entry.Id,
                OtherId = entry.GridId,
                Flag = paired,
                Number = inherit ? 1 : 0,
                Text = entry.CustomData,
                Vectors = new[]
                {
                    target.X,
                    target.Y,
                    target.Z,
                    targetGrid.X,
                    targetGrid.Y,
                    targetGrid.Z,
                    targetUp.X,
                    targetUp.Y,
                    targetUp.Z,
                    v[0],
                    v[1],
                    v[2],
                    v[3],
                    v[4],
                    v[5],
                    v[6],
                    v[7],
                    v[8],
                },
            };
        }

        #endregion

        #region World Authority side

        static void OnRequest(string origin, RegistryMessage message, object correlation)
        {
            if (!IsAuthority)
                BecomeAuthority();

            foreach (var entry in message.Entries)
                Upsert(entry);
            foreach (var id in message.Removed)
                Remove(id);
            if (!string.IsNullOrEmpty(message.Forget))
            {
                var forget = Normalize(message.Forget);
                foreach (var entry in Registry.Values.Where(e => e.Tlid == forget).ToList())
                    Remove(entry.Id);
                MyLog.Default.WriteLine($"TradeLanes: {origin} asked to forget TLID:{forget}");
            }
            Pair();

            if (!message.Full)
                return;
            foreach (
                var chunk in Chunks(
                    Registry.Values.ToList(),
                    Store.Removed.Skip(Math.Max(0, Store.Removed.Count - RemovedLimit)).ToList()
                )
            )
                Send(new object[] { "respond", Channel, Encode(chunk), correlation, origin });
        }

        static void BecomeAuthority()
        {
            IsAuthority = true;
            try
            {
                if (
                    MyAPIGateway.Utilities.FileExistsInWorldStorage(StoreFile, typeof(LaneRegistry))
                )
                    using (
                        var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                            StoreFile,
                            typeof(LaneRegistry)
                        )
                    )
                        Store =
                            MyAPIGateway.Utilities.SerializeFromXML<RegistryStore>(
                                reader.ReadToEnd()
                            ) ?? new RegistryStore();
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine(
                    $"TradeLanes: unreadable {StoreFile}, starting empty: {e.Message}"
                );
                Store = new RegistryStore();
            }
            foreach (var entry in Store.Entries)
                Registry[entry.Id] = entry;
            MyLog.Default.WriteLine(
                $"TradeLanes: lane registry on this World Authority, {Registry.Count} computers from storage"
            );
        }

        static void Upsert(LaneEntry entry)
        {
            LaneEntry old;
            Registry.TryGetValue(entry.Id, out old);
            entry.Order = old != null ? old.Order : ++Store.NextOrder;
            entry.Partner = old != null ? old.Partner : 0;
            entry.InheritRotation = old != null && old.InheritRotation;
            if (old == null || old.ReportKey() != entry.ReportKey())
            {
                Registry[entry.Id] = entry;
                Changed.Add(entry.Id);
                StoreDirty = true;
            }
            if (Store.Removed.Remove(entry.Id))
                StoreDirty = true;
        }

        static void Remove(long id)
        {
            Registry.Remove(id);
            Changed.Remove(id);
            NewlyRemoved.Add(id);
            if (!Store.Removed.Contains(id))
                Store.Removed.Add(id);
            if (Store.Removed.Count > RemovedLimit)
                Store.Removed.RemoveAt(0);
            StoreDirty = true;
        }

        // The rule the computers use: each pairs with the first other computer of
        // its TLID, and the earlier one of a pair takes the other's up direction.
        // A computer that is paired on its node keeps its own rotation flag.
        static void Pair()
        {
            foreach (
                var group in Registry
                    .Values.Where(e => e.Tlid != "" && e.Tlid != "error")
                    .GroupBy(e => e.Tlid)
            )
            {
                var ordered = group.OrderBy(e => e.Order).ToList();
                foreach (var entry in ordered)
                {
                    var partner = ordered.FirstOrDefault(e => e != entry);
                    long partnerId = partner?.Id ?? 0;
                    bool inherit = false;
                    if (partner != null)
                        inherit =
                            entry.Paired ? entry.Inherit
                            : partner.Paired && partner.Partner == entry.Id ? !partner.Inherit
                            : entry.Order < partner.Order;
                    Assign(entry, partnerId, inherit);
                }
            }
            foreach (var entry in Registry.Values.Where(e => e.Tlid == "" || e.Tlid == "error"))
                Assign(entry, 0, false);
        }

        static void Assign(LaneEntry entry, long partner, bool inherit)
        {
            if (entry.Partner == partner && entry.InheritRotation == inherit)
                return;
            entry.Partner = partner;
            entry.InheritRotation = inherit;
            Changed.Add(entry.Id);
            StoreDirty = true;
        }

        static void FlushAuthority()
        {
            if (Changed.Count > 0 || NewlyRemoved.Count > 0)
            {
                var entries = Changed
                    .Where(Registry.ContainsKey)
                    .Select(id => Registry[id])
                    .ToList();
                MyLog.Default.WriteLine(
                    $"TradeLanes: registry broadcast, {entries.Count} changed, {NewlyRemoved.Count} removed, {Registry.Count} computers"
                );
                foreach (var chunk in Chunks(entries, NewlyRemoved.ToList()))
                    Send(new object[] { "broadcast", Channel, Encode(chunk) });
                Changed.Clear();
                NewlyRemoved.Clear();
            }

            if (!StoreDirty)
                return;
            StoreDirty = false;
            Store.Entries = Registry.Values.OrderBy(e => e.Order).ToList();
            try
            {
                using (
                    var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(
                        StoreFile,
                        typeof(LaneRegistry)
                    )
                )
                    writer.Write(MyAPIGateway.Utilities.SerializeToXML(Store));
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine($"TradeLanes: cannot write {StoreFile}: {e.Message}");
            }
        }

        #endregion

        // Splits into messages that fit the payload limit, the first one carries
        // the removed ids. Always at least one message.
        static List<RegistryMessage> Chunks(List<LaneEntry> entries, List<long> removed)
        {
            var chunks = new List<RegistryMessage> { new RegistryMessage { Removed = removed } };
            var size = removed.Count * 10;
            foreach (var entry in entries)
            {
                var entrySize = MyAPIGateway.Utilities.SerializeToBinary(entry).Length + 8;
                if (size + entrySize > PayloadBytes && chunks[chunks.Count - 1].Entries.Count > 0)
                {
                    chunks.Add(new RegistryMessage());
                    size = 0;
                }
                chunks[chunks.Count - 1].Entries.Add(entry);
                size += entrySize;
            }
            return chunks;
        }

        static string Encode(RegistryMessage message)
        {
            return Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(message));
        }

        static void Send(object[] message)
        {
            MyAPIGateway.Utilities.SendModMessage(SendId, message);
        }

        // Plugin to mod: [kind, channel, origin, payload, correlation] or
        // ["fault", channel, error, correlation]
        static void OnMessage(object data)
        {
            try
            {
                var parts = data as object[];
                if (parts == null || parts.Length < 3 || parts[1] as string != Channel)
                    return;
                if (!MyAPIGateway.Session.IsServer)
                    return;

                var kind = parts[0] as string;
                if (kind == "fault")
                {
                    MyLog.Default.WriteLine($"TradeLanes: registry message failed: {parts[2]}");
                    return;
                }
                if (parts.Length < 4)
                    return;

                var origin = parts[2] as string;
                var message = MyAPIGateway.Utilities.SerializeFromBinary<RegistryMessage>(
                    Convert.FromBase64String(parts[3] as string ?? "")
                );
                if (message == null)
                    return;
                message.Entries = message.Entries ?? new List<LaneEntry>();
                message.Removed = message.Removed ?? new List<long>();

                if (kind == "request")
                    OnRequest(origin, message, parts.Length > 4 ? parts[4] : Correlation);
                else if (kind == "response" || kind == "broadcast")
                    OnRegistry(origin, message);
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine($"TradeLanes: bad registry message: {e}");
            }
        }
    }
}
