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
using VRage.Game.Utils;
using System.Reflection;
using Sandbox.Engine.Physics;
using System.Globalization;
//using static VRageRender.Utils.MyWingedEdgeMesh;
//using Microsoft.Xml.Serialization.GeneratedAssembly;
//using static System.Collections.Specialized.BitVector32;

// REDO PARTICLES AND SOUND FX'S

// Emissive - MAIN
// Emissive0 - SECONDARY
// Emissive3 - RED
// Emissive4 - GREEN

namespace Psycho.TradeLanes
{
    //[MyEntityComponentDescriptor(typeof(MyObjectBuilder_TerminalBlock), false, "TradeLaneComputer")]
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_BatteryBlock), false, "TradeLaneComputer")]
    public class TradeLaneComputerBlockLogic : MyGameLogicComponent
    {
        // ================================================================================================================================================================================
        // CONFIGURE YOUR TRADE LANE HERE! (still early work in progress)
        // ================================================================================================================================================================================

        bool SafeZoneEnabled = true;
        bool RenderRings = true;

        float ParticleDistance = 5000f;
        string RingMergeParticleName = "RingsMerge";
        string CancelRingMergeParticleName = "CancelRingMerge";
        string RingActivationAndIdleParticleName = "RingsActivateAndIdle";

        string RingIdleSoundName = "ShipLargeRunLoop";
        float RingIdleSoundVolume = 0.2f;
        float RingIdleSoundDistance = 500f;

        // TEXTURES
        // SafeZone_Texture_Disabled
        // SafeZone_Texture_KeenSWH
        // SafeZone_Texture_Restricted
        // SafeZone_Texture_Voronoi
        // SafeZone_Texture_Clang
        // SafeZone_Texture_Gloura
        // SafeZone_Texture_Digital
        // SafeZone_Texture_Lines
        // SafeZone_Texture_Hexagon
        // SafeZone_Texture_Noise
        // SafeZone_Texture_Disco
        // SafeZone_Texture_Dots
        // SafeZone_Texture_Rain
        // SafeZone_Texture_Organic
        // SafeZone_Texture_Aura
        // SafeZone_Texture_Default
        string SafeZoneTexture = "SafeZone_Texture_Gloura";
        float SafeZoneRadius = 0f;
        MySafeZoneShape SafeZoneShape = MySafeZoneShape.Sphere;

        string TradeLaneIdKeyword = "TLID";
        string TradeLaneSeparator = ":";
        string TradeLaneHomeKeyword = "From";
        string TradeLaneTargetKeyword = "To";
        string TradeLaneSpeedKeyword = "Speed";
        string TradeLaneRingScaleKeyword = "RingSize";
        string TradeLaneComputerSafeZoneRadiusKeyword = "SafeZoneRadius";
        string TradeLaneRingInvisibleModelsKeyword = "RenderRings";

        float AntennaRange = 550f;
        string AntennaHudText = "Trade Lane Id:";

        double TradeLaneHeadingMarkerDistance = 550;

        // 25000f
        // Lanes used to cross at half the configured speed, 100000 gave about 50 km/s
        const float DefaultSuperluminalSpeed = 50000f;
        float SuperluminalSpeed = DefaultSuperluminalSpeed;   // 1000

        string TradeLaneRingIntervalKeyword = "RingInterval";
        const float DefaultRingInterval = 10000f; // m
        float RingInterval = DefaultRingInterval;
        bool UseTeleport = true;

        //float RingRotationSpeed = 5;
        float RingRotationSpeed = 2;

        // ================================================================================================================================================================================
        // CORE VARIABLES DO | NOT EDIT!
        // ================================================================================================================================================================================

        // DEBUG
        bool DisableParticleEffects = false;
        bool DisableSoundEffects = false;
        bool DisableSafeZone = false;

        // VARIABLES
        #region VARS
        public static HashSet<IMyTerminalBlock> TradeLanes = new HashSet<IMyTerminalBlock>();
        //public static Dictionary<IMyTerminalBlock, IMyTerminalBlock> TradeLanesPairs = new Dictionary<IMyTerminalBlock, IMyTerminalBlock>();
        //public static Dictionary<long, Dictionary<IMyTerminalBlock, IMyTerminalBlock>> TradeLanesChains = new Dictionary<long, Dictionary<IMyTerminalBlock, IMyTerminalBlock>>();
        public static long chainID1 = 0;
        public static long chainID2 = 0;

        MyEntity This;
        IMyTerminalBlock Block;
        MyBatteryBlock Battery;
        IMyTerminalBlock TargetBlock;
        IMyTerminalBlock SourceBlock;
        bool InhertiRotation = false;

        // Where the lane leads. The server takes it from TargetBlock, a client
        // from the server, since the far computer is rarely streamed in.
        bool HasTarget = false;
        Vector3D TargetPosition;
        Vector3D TargetGridPosition;
        Vector3D TargetUp;

        // The server keeps where the paired computer was last seen in this
        // computer's ModStorage, and pairs from that while the other one is not
        // loaded: on a cluster it is usually on another node, or offline.
        static readonly Guid StoredTargetKey = new Guid("1e3f46c5-17fe-4f8b-89a1-4cd572165cba");
        string StoredTargetXml;

        public class StoredTarget
        {
            // Only valid for the lane id it was seen with
            public string Tlid;
            public SerializableVector3D Position;
            public SerializableVector3D GridPosition;
            public SerializableVector3D Up;
            public bool InheritRotation;
            // The partner's block id, 0 in targets stored before the lane registry
            public long PartnerId;
        }

        // Where the target came from, for the lane registry (Registry.cs)
        bool TargetFromGps;
        bool TargetFromRegistry;

        // Ground down or cut from its grid, not closed with the grid
        bool RemovedFromGrid;

        // The target source last logged
        string TargetSource;
        int LaneInfoRequestCountdown = 0;
        string LaneInfoSent;

        // On a client the rings of a lane are drawn by a stand-in: an instance with
        // no block, made from the server's lane info and updated by TradeLaneNetwork.
        // The computer itself is only there while it is within sync distance, and
        // closing it would take the rings with it. A cluster node docks ships at the
        // rings of the lanes loaded elsewhere through a stand-in too, see LaneRegistry.
        bool IsStandIn = false;
        MatrixD StandInMatrix;
        long StandInGridId;
        long StandInComputerId;

        // A node's stand-in looks for a ship at a free ring every this many frames
        // only, it watches every lane of the cluster
        const int StandInDetectFrames = 6;
        int StandInFrame;

        MatrixD LaneMatrix => IsStandIn ? StandInMatrix : Block.WorldMatrix;
        long LaneGridId => IsStandIn ? StandInGridId : Block.CubeGrid.EntityId;

        public string Location = "Uncharted";

        string TradeLane_DummyName =        "lane_1";
        string TradeLane_Detect_DummyName = "lane_detect_1";
        string TradeLane_Prep_DummyName =   "lane_prep_1";
        string TradeLane_Dock_DummyName =   "lane_dock_1";

        string TradeLane_Model =                "\\Models\\Psycho\\Large\\Trade_Lane.mwm";
        string TradeLane_Arrow_Model =          "\\Models\\Psycho\\Large\\Trade_Lane_Arrow.mwm";
        string TradeLane_Lights_Front_Model =   "\\Models\\Psycho\\Large\\Trade_Lane_Lights_Front.mwm";
        string TradeLane_Lights_Back_Model =    "\\Models\\Psycho\\Large\\Trade_Lane_Lights_Back.mwm";
        string TradeLane_Ring_Model =           "\\Models\\Psycho\\Large\\Trade_Lane_Ring.mwm";

        string LightDummyName = "ring_light";

        public IMyModelDummy TradeLane1_Dummy;
        public IMyModelDummy TradeLane1_Detect_Dummy;
        public IMyModelDummy TradeLane1_Prep_Dummy;
        public IMyModelDummy TradeLane1_Dock_Dummy;

        public IMyModelDummy TradeLane2_Dummy;
        public IMyModelDummy TradeLane2_Detect_Dummy;
        public IMyModelDummy TradeLane2_Prep_Dummy;
        public IMyModelDummy TradeLane2_Dock_Dummy;

        private MyOrientedBoundingBoxD box = new MyOrientedBoundingBoxD();
        private MyOrientedBoundingBoxD TradeLane1_DummyBox = new MyOrientedBoundingBoxD();
        private MyOrientedBoundingBoxD TradeLane1_Detect_DummyBox = new MyOrientedBoundingBoxD();
        private MyOrientedBoundingBoxD TradeLane1_Prep_DummyBox = new MyOrientedBoundingBoxD();
        private MyOrientedBoundingBoxD TradeLane1_Dock_DummyBox = new MyOrientedBoundingBoxD();
        private MyOrientedBoundingBoxD TradeLane2_DummyBox = new MyOrientedBoundingBoxD();
        private MyOrientedBoundingBoxD TradeLane2_Detect_DummyBox = new MyOrientedBoundingBoxD();
        private MyOrientedBoundingBoxD TradeLane2_Prep_DummyBox = new MyOrientedBoundingBoxD();
        private MyOrientedBoundingBoxD TradeLane2_Dock_DummyBox = new MyOrientedBoundingBoxD();

        public IMyModelDummy TargetLane1_Dummy;
        public MyEntity TargetGate;
        public IMyModelDummy TargetLane2_Dummy;
        public MyEntity TargetLane2;

        int dockWaitFrames = 0;
        int dockPrepFrames = 0;
        int dockDetectFrames = 0;
        int dockFrames1 = 0;
        int prepFrames1 = 0;

        int dockFrames2 = 0;
        int prepFrames2 = 0;

        int RingLightBlinkInterval = 100;
        int RingLightBlinkLength = 20;
        int RingLightBlinkFrame = 2;

        MyEntity SourceGate;
        MyEntity Lane2;

        List<MyEntity> LaneGates = new List<MyEntity>();
        List<MyEntity> LaneGatesParts1 = new List<MyEntity>();

        // Client log of what this stand-in draws (TradeLaneNetwork counts them)
        public int RingCount => LaneGates.Count;

        // A ring entity closed from outside is never rebuilt by UpdateRings: drop them all so it builds anew
        bool HasClosedRing()
        {
            foreach (var ent in LaneGates)
                if (ent == null || ent.Closed || ent.MarkedForClose)
                    return true;
            foreach (var ent in LaneGatesParts1)
                if (ent == null || ent.Closed || ent.MarkedForClose)
                    return true;
            return false;
        }
        List<MyEntity> LaneGatesParts2 = new List<MyEntity>();
        List<MyEntity> LaneGatesParts3 = new List<MyEntity>();
        //HashSet<Vector3D> LaneGatePoints = new HashSet<Vector3D>();
        List<Vector3D> LaneGatePoints = new List<Vector3D>();
        List<MyEntity> LaneGates2 = new List<MyEntity>();
        List<Vector3D> LaneGatesLocations2 = new List<Vector3D>();

        Dictionary<MyEntity, MyEntity3DSoundEmitter> LaneRingSounds = new Dictionary<MyEntity, MyEntity3DSoundEmitter>();

        MyEntity newLane1;
        MyEntity newLane2;

        //HashSet<MyEntity> Lanes = new HashSet<MyEntity>();

        bool LockGrid = false;
        int GateCount = 0;
        int CurrentGateCount = 0;

        IMyCubeGrid GridWaiting;

        List<MyCubeGrid> GridsMerging = new List<MyCubeGrid>();

        List<long> PlayersInTransit = new List<long>();

        // Ships in transit, docking ignores them. Kept by their CustomGridLogic.
        public static List<MyCubeGrid> GridIgnore = new List<MyCubeGrid>();

        //float DisengageDistance = 125f;     // 25
        float DisengageDistance = 0.01f;     // 25
        float DisengageVelocityMult = 0.5f; // 0.2

        /*
        
        // REFERENCE EMPTY NAMES IN MODEL

        dummy_lane_1
        dummy_lane_prep_1
        dummy_lane_dock_1
        dummy_lane_detect_1
         
        */

        public MyEntity TargetLaneEntity;
        public IMyModelDummy TargetLaneDummy;
        public Vector3D TargetLaneDummyLocation;
        private MyOrientedBoundingBoxD TargetLaneGateBox = new MyOrientedBoundingBoxD();

        private Vector3D DetectDummyLocalMatrix;
        private Vector3D DockDummyLocalMatrix;
        private Vector3D PrepDummyLocalMatrix;
        private Vector3D GateDummyLocalMatrix;

        public MyOrientedBoundingBoxD DetectBoundingBox;
        public MyOrientedBoundingBoxD GateBoundingBox;

        private bool DoOnce = false;

        Dictionary<MyEntity, LaneData> LaneDataDict = new Dictionary<MyEntity, LaneData>();

        MySafeZone SafeZone;

        class LaneData
        {
            //public MyEntity Lane;
            public MyCubeGrid GridInWaitingLine;
            public MyShipController ShipController;
            public int GridMergeStage;
            public int GridDockFrame;
            public int GridPrepFrame;

            /*
            public Vector3D DetectDummyLoc;
            public Vector3D DockDummyLoc;
            public Vector3D PrepDummyLoc;
            public Vector3D GateDummyLoc;
            public MyOrientedBoundingBoxD DetectBox;
            public MyOrientedBoundingBoxD GateBox;
            */
        }

        MyCubeGrid FirstGrid1 = null;
        int LaneStage1 = 0;

        MyCubeGrid FirstGrid2 = null;
        int LaneStage2 = 0;

        private Random Random = new Random();
        private double shakeFrequency = 10.0; // Frequency of the shake
        private double shakeAmplitude = 0.1; // Amplitude of the shake
        private double driftSpeed = 0.01; // Speed of the drift
        private Vector3D driftDirection = Vector3D.Zero; // Current drift direction

        private int ShakeInterval = 10;
        private int ShakeFrame = 2;

        private Vector3D GateLockedInLocation;

        //private IMy3DSoundEmitter soundEmitter;
        private MyEntity3DSoundEmitter ShipSoundEmitter = null;
        private MyEntity3DSoundEmitter TradeLaneSoundEmitter = null;

        float RingOffsetDefault = 160f + 60f;
        float RingOffset = 160f + 60f;

        public MyParticleEffect ShipParticles = null;
        public MyParticleEffect PathPartciles = null;
        public MyParticleEffect EnterRingPartciles = null;
        public MyParticleEffect RingParticles = null;

        //List<MyParticleEffect> GateParticles = new List<MyParticleEffect>();
        Dictionary<MyEntity, MyParticleEffect> GateParticles = new Dictionary<MyEntity, MyParticleEffect>();
        Dictionary<MyEntity, MyEntity3DSoundEmitter> GateSounds = new Dictionary<MyEntity, MyEntity3DSoundEmitter>();
        Dictionary<MyEntity, MyParticleEffect> GateActivationParticles = new Dictionary<MyEntity, MyParticleEffect>();
        Dictionary<MyCubeGrid, MyParticleEffect> GateDisengageParticles = new Dictionary<MyCubeGrid, MyParticleEffect>();

        //public static List<long> LinkId = new List<long>();

        MyCubeGrid GridDockingRequest = null;

        //private Dictionary<string, MyLight> lights = new Dictionary<string, MyLight>();
        private List<MyLight> Lights = new List<MyLight>();
        private bool LightState = false;
        //void CreateLight(IMyEntity entity, string dummyName, Matrix dummyMatrix)

        float rotationSpeed = 3f;
        float currentRotation = 0f;

        Vector3D TargetAcceleration = Vector3D.Zero;

        //bool GateChanged = false;

        private Vector3D _previousPosition = Vector3D.Zero;
        private double _lastUpdateTime = 0;

        bool EmissiveSwitch = false;

        private int GpsRefreshInterval = 100;
        private int GpsRefreshFrame = 2;

        IMyShipController ShipController = null;

        float RingScaleMult = 1f;

        string CustomData = "";

        //public static List<string> GpsMarkers = new List<string>();

        int GateUpdateFrame = 0;

        bool SpawnBlock = false;

        int DistanceUpdateInterval = 10;
        int DistanceUpdateFrame = 0;

        int DockPositionThreshold = 50;

        #endregion

        #region OVERRIDES

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            //MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "Init");
            //Block.CustomDataChanged += Block_CustomDataChanged;

            //NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            //MyAPIGateway.Utilities.ShowMessage("TL", $"INIT");
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void MarkForClose()
        {
            // The grid sets IsBeingRemoved only while it removes the block itself
            RemovedFromGrid = (Entity as MyCubeBlock)?.IsBeingRemoved == true && !MyEntities.IsClosingAll;
            base.MarkForClose();
        }

        public override void Close()
        {
            NeedsUpdate |= MyEntityUpdateEnum.NONE;
            try
            {
                if (Block != null)
                {
                    Block.CustomDataChanged -= Block_CustomDataChanged;
                    //Block.PropertiesChanged -= Block_CustomDataChanged;
                    Block.CubeGridChanged -= Block_CubeGridChanged;

                    if (TradeLaneNetwork.IsServer)
                    {
                        if (RemovedFromGrid)
                            LaneRegistry.ComputerRemoved(Block.EntityId);
                        else
                            LaneRegistry.Unloaded(Block.EntityId);

                        // The clients drop their stand-in of this lane. On a cluster a
                        // close is usually a handover or an offline partition, and the
                        // lane stays in the registry.
                        if (RemovedFromGrid || !LaneRegistry.Active)
                        {
                            HasTarget = false;
                            SendLaneInfo();
                        }
                    }
                }

                bool ownsGps = IsStandIn || (TradeLaneNetwork.IsServer && Block?.CubeGrid != null);
                if (ownsGps && MyAPIGateway.Session.Player != null)
                {
                    var globalLaneIdent = $"Trade Lane Network\nID:{LaneGridId}";
                    // Check if a GPS marker with the same name already exists
                    var existingGps = MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId).FirstOrDefault(gps => gps.Description.Contains(globalLaneIdent));
                    if (existingGps != null)
                        MyAPIGateway.Session.GPS.RemoveLocalGps(existingGps);
                }
                if (ownsGps && MyAPIGateway.Session.Player != null)
                {
                    var localLaneIdent = $"Trade Lane Information\nID:{LaneGridId}";
                    // Check if a GPS marker with the same name already exists
                    var existingGps = MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId).FirstOrDefault(gps => gps.Description.Contains(localLaneIdent));
                    if (existingGps != null)
                        MyAPIGateway.Session.GPS.RemoveLocalGps(existingGps);
                }

                if (SourceGate != null)
                {
                    MyEntities.Remove(SourceGate);
                    SourceGate.Close();
                    SourceGate = null;
                }
                if (Lane2 != null)
                {
                    MyEntities.Remove(Lane2);
                    Lane2.Close();
                    Lane2 = null;
                }

                DeleteEntities(ref LaneGates);
                DeleteEntities(ref LaneGatesParts1);
                DeleteEntities(ref LaneGatesParts2);
                DeleteEntities(ref LaneGatesParts3);

                DeleteLights();

                RemoveSoundEmitter(ref ShipSoundEmitter);

                foreach (var sound in LaneRingSounds.Values)
                {
                    RemoveSoundEmitter(sound);
                }
                LaneRingSounds.Clear();

                LaneGates.Remove(This);

                if (SafeZone != null)
                {
                    SafeZone.Close();
                    SafeZone = null;
                }

                //RemoveParticleEffects(ref ShipParticles);
                //RemoveParticleEffects(ref PathPartciles);
                RemoveParticleEffects(ref EnterRingPartciles);
                RemoveParticleEffects(ref RingParticles);
                foreach (var particle in GateParticles.Values)
                {
                    if (particle != null)
                    {
                        if (particle != null)
                        {
                            MyParticlesManager.RemoveParticleEffect(particle);
                            particle.Stop();
                            particle.Close();
                        }
                    }
                }

                if (GateSounds.Count > 0)
                {
                    foreach (var sound in GateSounds.Values)
                    {
                        if (sound != null)
                        {
                            if (sound != null)
                            {
                                StopSoundEmitter(sound);
                            }
                        }
                    }
                    GateSounds.Clear();
                }

                if (TradeLanes.Contains(Block))
                {
                    TradeLanes.Remove(Block);
                }

                if (Block != null)
                {
                    Block = null;
                }
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES Close", e.Message);
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            //MyAPIGateway.Utilities.ShowMessage("TL", $"UOBF");
            //MyAPIGateway.Utilities.ShowMessage("TL", $"Check 1");
            //MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "Prep");
            This = Entity as MyEntity;
            Block = This as IMyTerminalBlock;
            Battery = This as MyBatteryBlock;

            /*
            var builtById = (Block as MyCubeBlock)?.BuiltBy;

            if (builtById.HasValue)
            {
                var playerIdentity = MyAPIGateway.Players.TryGetIdentityId(builtById.Value);

                if (playerIdentity != null)
                {
                    //string playerName = playerIdentity.DisplayName;

                    if (playerIdentity.PromoteLevel < MyPromoteLevel.Admin)
                    {
                        MyAPIGateway.Utilities.ShowNotification($"Non-admin placement, feature disbaled.", 2000, "Red");
                        Block.Close();
                        return;
                    }
                }
            }
            */

            /*
            if (MyAPIGateway.Session?.Player?.PromoteLevel < MyPromoteLevel.Admin)
            {
                // Remove the block if placed by a non-admin player
                MyAPIGateway.Utilities.ShowNotification("This block is restricted to admins only.", 2000, "Red");
                Block.Close(); // Remove the block
            }
            */

            if (Block == null || Battery == null)
            {
                //.Utilities.ShowMessage("TradeLaneComputer", "Block is null or not a battery!");
                return;
            }
            //MyAPIGateway.Utilities.ShowMessage("TL", $"Check 1");

            //MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "Load Defs");
            var blockDefinition = MyDefinitionManager.Static.GetCubeBlockDefinition(Block.BlockDefinition);

            if (blockDefinition != null)
            {
                var modContext = blockDefinition.Context;

                string modpath = modContext.ModPath;
                //string sbcpath = modContext.CurrentFile;
                //string modname = modContext.ModName;
                //string moddata = modContext.ModPathData;

                /*
                TradeLane_Model = modpath + "\\Models\\Psycho\\Large\\Trade_Lane.mwm";
                TradeLane_Arrow_Model = modpath + "\\Models\\Psycho\\Large\\Trade_Lane_Arrow.mwm";
                */
                TradeLane_Model = modpath + TradeLane_Model;
                TradeLane_Lights_Front_Model = modpath + TradeLane_Lights_Front_Model;
                TradeLane_Lights_Back_Model = modpath + TradeLane_Lights_Back_Model;
                TradeLane_Ring_Model = modpath + TradeLane_Ring_Model;
                //MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "Models set:\n" + TradeLane_Model);
            }


            /*
            Lane1 = CreateEntity(TradeLane_Model);
            Lane2 = CreateEntity(TradeLane_Model);
            MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "Create Ent");

            if (Lane1 == null || Lane2 == null)
            {
                MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "Lane1 and Lane2 failed!");
                return;
            }

            if (GetDummies(Lane1, ref TradeLane1_Dummy, ref TradeLane1_Detect_Dummy, ref TradeLane1_Prep_Dummy, ref TradeLane1_Dock_Dummy) == false)
            {
                MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "Lane1 failed to get dummies!");
                return;
            }

            if (GetDummies(Lane2, ref TradeLane2_Dummy, ref TradeLane2_Detect_Dummy, ref TradeLane2_Prep_Dummy, ref TradeLane2_Dock_Dummy) == false)
            {
                MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "Lane2 failed to get dummies!");
                return;
            }
            */

            //CheckBlockCustomData();

            //MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "Finished Prep, Run Logic!");

            /*
            if (Block.CustomName.ToLower().Contains("tradelanecomputerB:"))
            {
            }
            */

            /*
            var gridInterface = Block.CubeGrid as IMyCubeGrid;
            if (gridInterface == null)
                return;

            gridInterface.DestructibleBlocks = false;
            */

            // To try and make placement easier.
            // TradeLanes will hold their ID's in their names.
            // Simple e.g. TradeLaneComputer:1 with two pairs of trade lanes.
            // This code should try and append numbers to just place or copy them in paired sequence.
            bool autoIncrement = false;
            if (autoIncrement)
            {
                if (!Block.CustomName.Contains("TradeLaneComputer:"))
                {
                    if (chainID1 == chainID2)
                    {
                        chainID1++;
                        Block.CustomName = "TradeLaneComputer:" + chainID1.ToString();
                    }
                    else if (chainID1 > chainID2)
                    {
                        var dif = chainID2 - chainID1;
                        if (dif == 1)
                        {
                            //chainID2 = chainID1;
                            //Block.CustomName = "TradeLaneComputer:" + chainID2.ToString();
                        }
                        chainID2 = chainID1;
                        Block.CustomName = "TradeLaneComputer:" + chainID2.ToString();
                    }
                }
                else
                {
                    var count = 0;
                    int.TryParse(Block.CustomName.Substring(Block.CustomName.LastIndexOf(':') + 1), out count);
                    if (count > chainID1)
                    {
                        chainID1 = count;
                    }
                    else
                    {
                        if (count > chainID2)
                        {
                            chainID2 = count;
                        }
                    }

                    /*
                    if (count > chainID1)
                    {
                        chainID1 = count;
                    }
                    else
                    {
                        if (count > chainID2)
                        {
                            chainID2 = count;
                        }
                    }
                    */
                }
            }
            else
            {
                //Block.CustomName = "TradeLaneComputer";
            }

            TradeLanes.Add(Block);

            /*
            var safeZoneBlock = Block as IMySafeZoneBlock;
            if (safeZoneBlock != null)
            {
                EnableSafeZoneWithoutCredits(safeZoneBlock);
            }
            */

            // DO NOT CALL THIS, CHANGED BLOCK FROM SAFEZONE TO BATTERY!
            /*
            var zone = Block as IMySafeZoneBlock;
            if (zone == null)
                return;
            zone.EnableSafeZone(false);
            */

            MonitorAndForceStatic(Block.CubeGrid);

            CustomData = Block.CustomData;
            SetupSystems();
            UpdateSystems();

            /*
            if (SafeZoneEnabled && SafeZoneRadius > 0)
            {
                if (SafeZone == null)
                    SafeZone = SpawnSafeZone(Block.WorldMatrix.Translation, SafeZoneRadius, SafeZoneTexture, SafeZoneShape);
            }
            */

            Block.CustomDataChanged += Block_CustomDataChanged;
            //Block.PropertiesChanged += Block_CustomDataChanged;
            Block.CubeGridChanged += Block_CubeGridChanged;

            NeedsUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME | MyEntityUpdateEnum.EACH_FRAME;
        }

        private void Block_CubeGridChanged(IMyCubeGrid obj)
        {
            try
            {
                SetupSystems();
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES Block_CubeGridChanged", e.Message);
            }
        }

        public override void UpdateAfterSimulation100()
        {
            try
            {
                if (IsStandIn)
                {
                    if (HasClosedRing())
                    {
                        DeleteEntities(ref LaneGates);
                        DeleteEntities(ref LaneGatesParts1);
                    }
                    if (GateUpdateFrame > 0 && --GateUpdateFrame <= 0)
                        UpdateGates();
                    if (HasTarget)
                        UpdateRings();
                    return;
                }

                if (Block == null)
                    return;

                //if (MyAPIGateway.Session.HasCreativeRights)
                if (MyAPIGateway.Session.EnableCopyPaste || MyAPIGateway.Session.CreativeMode)
                {
                    /*
                    if (MyAPIGateway.Session.EnableCopyPaste && !MyAPIGateway.Session.CreativeMode)
                    {
                        if (SafeZone == null)
                            SafeZone = SpawnSafeZone(Block.WorldMatrix.Translation, 50f, SafeZoneTexture, SafeZoneShape);
                    }
                    */

                    if (!Block.Render.Visible)
                    {
                        Block.Render.Visible = true;
                        //Block.Physics.Enabled = true;
                        //Block.Physics.Activate();
                    }
                    if (!Block.CubeGrid.Physics.Enabled)
                    {
                        Block.CubeGrid.Physics.Enabled = true;
                        Block.CubeGrid.Physics.Activate();
                    }
                }
                else
                {
                    if (Block.Render.Visible)
                    {
                        Block.Render.Visible = false;
                        //Block.Physics.Enabled = false;
                        //Block.Physics.Deactivate();

                    }
                    if (Block.CubeGrid.Physics.Enabled)
                    {
                        Block.CubeGrid.Physics.Enabled = false;
                        Block.CubeGrid.Physics.Deactivate();
                    }

                    //ForceUpdate();
                }

                if (CustomData != Block.CustomData)
                {
                    CustomData = Block.CustomData;
                    UpdateSystems();
                }

                if (GateUpdateFrame > 0 && --GateUpdateFrame <= 0)
                {
                    UpdateGates();
                    /*
                    if (SpawnBlock)
                    {
                        if (LaneGates.Count > 0)
                        {
                            var gate = LaneGates[0];
                            if (LaneGates[0] != null)
                                CheckAndSpawnBlock(gate.WorldMatrix.Translation, gate.WorldMatrix.GetOrientation());
                        }
                        SpawnBlock = false;
                    }
                    */
                }

                //MyAPIGateway.Utilities.ShowMessage("TL", $"UAS100");
                if (Battery != null)
                {
                    Battery.CurrentStoredPower = Battery.MaxStoredPower;
                    var battery = Battery as IMyBatteryBlock;
                    if (TradeLaneNetwork.IsServer)
                    {
                        battery.Enabled = true;
                        battery.ChargeMode = Sandbox.ModAPI.Ingame.ChargeMode.Discharge;
                    }
                }

                //MyAPIGateway.Utilities.ShowNotification("Check100");

                /*
                //NebulaParticleTest = SpawnParticleEffects(Block, "ExhaustSmokeReactor", MatrixD.Zero);
                if (NebulaParticleTest == null)
                {
                    //NebulaParticleTest = SpawnParticleEffects(Block, "ParticleElectrical", MatrixD.Zero);
                    //NebulaParticleTest = SpawnParticleEffects(Block, "MyExhaustSmokeReactor", MatrixD.Identity, MyAPIGateway.Session.Player.Character.WorldMatrix.Translation);
                    //NebulaParticleTest = SpawnParticleEffects(Block as MyEntity, "TradeLaneWarp", MatrixD.Identity);
                    //NebulaParticleTest = SpawnParticleEffects(Block as MyEntity, "Circular_effect_psycho", MatrixD.Identity);
                }
                //NebulaParticleTest.UserScale = 50f;
                //NebulaParticleTest.Play();
                if (NebulaParticleTest != null)
                {
                    //MyAPIGateway.Utilities.ShowMessage("YEY", "Particle spawned");
                    //NebulaParticleTest.UserScale = 50f;
                    //NebulaParticleTest.UserVelocityMultiplier = 0.1f;
                }
                else
                {
                    //MyAPIGateway.Utilities.ShowMessage("AWW", "Particle NOT spawned");
                }
                */

                if (Block.CubeGrid?.Physics == null)
                {
                    /*
                    if (Block.CustomName.ToLower().Contains("tradelanecomputer:"))
                        Block.CustomName.Replace(":", "");
                    */
                    /*
                    if (LaneGates.Count > 0)
                    {
                        foreach (var ent in LaneGates)
                        {
                            MyEntities.Remove(ent);
                            ent.Close();
                        }
                        LaneGates.Clear();
                    }
                    */
                    /*
                    DeleteEntities(ref LaneGates);
                    DeleteEntities(ref LaneGatesParts1);
                    DeleteEntities(ref LaneGatesParts2);
                    DeleteEntities(ref LaneGatesParts3);
                    DeleteLights();
                    DoOnce = false;
                    return;
                    */
                }

                var tlid = ReadCustomData(Block.CustomData, TradeLaneIdKeyword, TradeLaneSeparator);

                //MyAPIGateway.Utilities.ShowMessage("tlid data", tlid);

                var laneId = 0;
                if (!Block.CustomName.ToLower().Contains("tradelanecomputer:") && !int.TryParse(Block.CustomName.Substring(Block.CustomName.LastIndexOf(':') + 1), out laneId) && string.IsNullOrEmpty(tlid))
                    return;

                if (!TradeLaneNetwork.IsServer)
                {
                    // Ask until the server answers, it also sends updates on its own
                    if (!HasTarget && --LaneInfoRequestCountdown <= 0)
                    {
                        TradeLaneNetwork.RequestLaneInfo(Block.EntityId);
                        LaneInfoRequestCountdown = 3;
                    }
                }
                else if (TradeLanes.Count > 0 && TargetBlock == null)
                {
                    tlid = "TLID:" + tlid;
                    //MyAPIGateway.Utilities.ShowMessage("tlid data", tlid);
                    //if (!int.TryParse(Block.CustomName.Substring(Block.CustomName.LastIndexOf(':') + 1), out laneId))
                    //return;

                    bool rotationSource = false;
                    foreach (var block in TradeLanes)
                    {
                        if (block == Block)
                        {
                            rotationSource = true;
                            continue;
                        }

                        if (block.CustomName.ToLower().Contains("tradelanecomputer:" + laneId) || block.CustomData.ToLower().Contains(tlid.ToLower()))
                        {
                            TargetBlock = block;
                            InhertiRotation = rotationSource;

                            break;
                        }
                    }
                }

                /*
                */

                if (TargetBlock != null && TargetBlock.CustomName != Block.CustomName && TargetBlock.CustomData.ToLower().Contains(tlid.ToLower()))
                {
                    if (TradeLaneNetwork.IsServer)
                        ReportToRegistry();
                    return;
                }

                if (TradeLaneNetwork.IsServer)
                    UpdateTarget();

                if (HasTarget)
                {
                    //if (TargetBlock.CustomName != Block.CustomName)
                    //    return;
                    //RotateGridTowardsTarget(Block, TargetBlock, InhertiRotation);
                    //RotateGridTowardsTargetByBlockOrientationRefence(Block, TargetBlock, InhertiRotation);
                    RotateGridTowardsTargetByBlockOrientationRefence(Block, TargetPosition, TargetGridPosition, TargetUp, InhertiRotation);
                }

                if (TradeLaneNetwork.IsServer)
                {
                    PublishLaneInfo();
                    ReportToRegistry();
                }

                /*
                if (TargetBlock != null && TargetLane1_Dummy == null)
                {
                    var logic = TargetBlock.GameLogic.GetAs<ElectricTetherBlockLogic>();
                    if (logic != null)
                    {
                        if (TargetLane1_Dummy == null && logic.TradeLane1_Dummy != null)
                        {
                            TargetLane1_Dummy = logic.TradeLane1_Dummy;
                            TargetGate = logic.SourceGate;
                        }
                        if (TargetLane2_Dummy == null && logic.TradeLane2_Dummy != null)
                        {
                            TargetLane2_Dummy = logic.TradeLane2_Dummy;
                            TargetLane2 = logic.Lane2;
                        }
                    }
                    //TargetLane1_Dummy = Block.
                    //TargetLane2_Dummy = Lane2.Model.GetDummy(TradeLane_DummyName);
                }
                */

                //Block.CustomData = Block.CustomData = InhertiRotation.ToString();

                // Handles the return and entity cleanup.
                if (!HasTarget)
                {
                    TargetBlock = null;
                    /*
                    if (LaneGates.Count > 0)
                    {
                        foreach (var ent in LaneGates)
                        {
                            MyEntities.Remove(ent);
                            ent.Close();
                        }
                        LaneGates.Clear();
                    }
                    */
                    DeleteEntities(ref LaneGates);
                    DeleteEntities(ref LaneGatesParts1);
                    DeleteEntities(ref LaneGatesParts2);
                    DeleteEntities(ref LaneGatesParts3);
                    LaneGatePoints.Clear();
                    DeleteLights();

                    //RemoveSoundEmitter(ref TradeLaneSoundEmitter);
                    //Block.CustomDataChanged -= Block_CustomDataChanged;

                    //LaneGates.Remove(This);

                    /*
                    if (SafeZone != null)
                    {
                        SafeZone.Close();
                        SafeZone = null;
                    }
                    */

                    RemoveParticleEffects(ref ShipParticles);
                    RemoveParticleEffects(ref PathPartciles);
                    RemoveParticleEffects(ref RingParticles);

                    if (GateParticles.Count > 0)
                    {
                        foreach (var particle in GateParticles.Values)
                        {
                            if (particle != null)
                            {
                                if (particle != null)
                                {
                                    MyParticlesManager.RemoveParticleEffect(particle);
                                    particle.Stop();
                                    particle.Close();
                                }
                            }
                        }
                    }

                    if (GateSounds.Count > 0)
                    {
                        foreach (var sound in GateSounds.Values)
                        {
                            if (sound != null)
                            {
                                if (sound != null)
                                {
                                    StopSoundEmitter(sound);
                                }
                            }
                        }
                        GateSounds.Clear();
                    }

                    LaneDataDict.Clear();
                    DoOnce = false;
                    return;
                }
                
                // A client's rings are drawn by the lane's stand-in, see TradeLaneNetwork
                if (!TradeLaneNetwork.IsServer)
                    return;

                UpdateRings();

                /*
                if (Lights != null && Lights.Count > 0)
                {
                    LightState = !LightState;
                    SetLightsEnabled(LightState);
                }
                */

                return;

                if (InhertiRotation)
                {
                    if (TargetGate != null)
                    {
                        //AddPointsBetweenGates(Block, TargetBlock, 1500);

                                /*
                        if (LaneGatePoints.Count > 0)
                        {
                            if (LaneGates1.Count < LaneGatePoints.Count)
                            {
                                foreach (var loc in LaneGatePoints)
                                {
                                    var ent = CreateEntity(TradeLane_Model);
                                    if (ent != null)
                                    {
                                        //ent.WorldMatrix = Lane1.WorldMatrix;
                                        var WorldMatrix = MatrixD.CreateWorld(loc, SourceGate.WorldMatrix.Forward, SourceGate.WorldMatrix.Up);
                                        ent.Teleport(WorldMatrix);
                                        //ent.WorldMatrix = MatrixD.CreateWorld(loc);
                                        ent.WorldMatrix = WorldMatrix;
                                        LaneGates1.Add(ent);
                                    }
                                }

                                //for (int i = 0; i < (LaneGatesLocations.Count - LaneGates.Count); i++)
                                //{
                                //    var ent = CreateEntity(TradeLane_Model);
                                //    LaneGates.Add(ent);
                                //}
                            }
                        }
                                */
                        //var ent = CreateEntity(TradeLane_Model);
                    }
                }
                else
                {
                    if (TargetLane2 != null)
                    {
                        //AddPointsBetweenGates(Block, TargetBlock, 2500);
                        //AddPointsBetweenGates(Lane2, TargetLane2, ref LaneGatesLocations2, (div / 2));

                        if (LaneGatesLocations2.Count > 0)
                        {
                            if (LaneGates2.Count < LaneGatesLocations2.Count)
                            {
                                foreach (var loc in LaneGatesLocations2)
                                {
                                    var ent = CreateEntity(TradeLane_Model);
                                    if (ent != null)
                                    {
                                        ent.WorldMatrix = Lane2.WorldMatrix;
                                        //var WorldMatrix = MatrixD.CreateWorld(loc);
                                        var WorldMatrix = MatrixD.CreateWorld(loc, Lane2.WorldMatrix.Forward, Lane2.WorldMatrix.Up);
                                        //ent.Teleport(WorldMatrix);
                                        //ent.WorldMatrix = MatrixD.CreateWorld(loc);
                                        ent.WorldMatrix = WorldMatrix;
                                        LaneGates2.Add(ent);
                                    }
                                }

                                /*
                                for (int i = 0; i < (LaneGatesLocations.Count - LaneGates.Count); i++)
                                {
                                    var ent = CreateEntity(TradeLane_Model);
                                    LaneGates.Add(ent);
                                }
                                */
                            }
                        }
                        //var ent = CreateEntity(TradeLane_Model);
                    }
                }



                    /*
                int index = 0;
                foreach (var ent in LaneGates)
                {
                    if (index < LaneGatesLocations.Count)
                    {
                        ent.WorldMatrix = MatrixD.CreateWorld(LaneGatesLocations[index]);
                    }
                    else
                    {
                        MyEntities.Remove(ent);
                        LaneGates.Remove(ent);
                        break;
                    }
                    ent.WorldMatrix = MatrixD.CreateWorld(LaneGatesLocations[index]);
                    index++;
                }
                    */

                /*
                */

                //LaneGates
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES UAS100", e.Message);
            }
        }

        // Places the rings along the lane, creating or removing them as needed
        void UpdateRings()
        {
            try
            {
                    // Get distance between first and last trade lane computers in meters.
                    var distnace = Vector3D.Distance(LaneMatrix.Translation, TargetPosition);

                    var kilometers = distnace / 1000f;
                    float ringEveryKm = RingInterval / 1000f; // Roughly set a TL ring every X kilometers.

                    if (distnace >= ringEveryKm)
                    {
                        //MyAPIGateway.Utilities.ShowMessage("ff", "Distance: " + distnace.ToString() + "m");
                        //MyAPIGateway.Utilities.ShowMessage("ff", "calc: " + (MathHelper.RoundToInt(kilometers / ringEveryKm)).ToString());
                        // Create location points for placing gates.
                        //int ringEveryKilometer = 50; // Roughly set a TL ring every 10km.
                        //var middleGateCount = MathHelper.RoundToInt(distnace / ringEveryKilometer);

                        var ringCount = MathHelper.Clamp(MathHelper.RoundToInt(kilometers / ringEveryKm), 1, int.MaxValue);
                        AddPointsBetweenGates(LaneMatrix.Translation, TargetPosition, ref LaneGatePoints, ringCount);
                    }
                    else
                        AddPointsBetweenGates(LaneMatrix.Translation, TargetPosition, ref LaneGatePoints, 1);

                    GateCountHandler(ref LaneGates, ref LaneGatePoints);
                    GateCountHandler(ref LaneGatesParts1, ref LaneGatePoints, 1);

                    //GateCountHandler(ref LaneGatesParts2, ref LaneGatePoints, 2);
                    //GateCountHandler(ref LaneGatesParts3, ref LaneGatePoints, 3);

                    //return;
                    // Place the entities (gates) on the location points with offset.
                    // Currently I'm choosing the blocks Right offset since europe and we drive on right lane side.
                    // Although there is no side or orientation in space, there is if there is a reference point.
                    // Two lanes should be a reference points. Seems like in Freelancer <3 the gates are randomly either up or down,
                    // which always bugged me a little. Why in some systems/sectors the gates leading outwards are top ones and on some other systems it's the bottom gate??
                    // No explanation found online regarding that, so for my personal consistency sake between gates,
                    // outbound gates are on the RIGHT SIDE and inbound are LEFT.
                    if (LaneGates.Count > 0 && LaneGates.Count == LaneGatePoints.Count)
                    {
                        if (!DoOnce)
                        {
                            MyEntity entity = LaneGates[0];
                            if (entity == null)
                            {
                                MyAPIGateway.Utilities.ShowMessage("TRADE LANES UAS100", "entity was faulty");
                                return;
                            }
                            var dummy = GetDummyByName(entity, "dummy_lane_detect_1");
                            DetectDummyLocalMatrix = dummy.Matrix.Translation;
                            DetectBoundingBox = new MyOrientedBoundingBoxD();
                            CreateOBB(entity, dummy, out DetectBoundingBox);
                            dummy = GetDummyByName(entity, "dummy_lane_dock_1");
                            DockDummyLocalMatrix = dummy.Matrix.Translation;
                            dummy = GetDummyByName(entity, "dummy_lane_prep_1");
                            PrepDummyLocalMatrix = dummy.Matrix.Translation;
                            dummy = GetDummyByName(entity, "dummy_lane_1");
                            GateDummyLocalMatrix = dummy.Matrix.Translation;
                            GateBoundingBox = new MyOrientedBoundingBoxD();
                            CreateOBB(entity, dummy, out GateBoundingBox);

                            DoOnce = true;
                            //return;
                        }
                        //return;

                        for (int i = 0; i < LaneGatePoints.Count; i++)
                        {
                            Vector3D loc = LaneGatePoints[i];
                            //Vector3D loc = LaneGatePoints.ElementAt(i);
                            if (loc == null || loc == Vector3D.Zero)
                            {
                                MyAPIGateway.Utilities.ShowMessage("TRADE LANES UAS100", "loc was faulty");
                                break;
                            }

                            MyEntity ent = LaneGates[i];

                            MyEntity ent1 = null;
                            MyEntity ent2 = null;
                            MyEntity ent3 = null;
                            if (LaneGatesParts1.Count == LaneGatePoints.Count)
                                ent1 = LaneGatesParts1[i];
                            if (LaneGatesParts2.Count == LaneGatePoints.Count)
                                ent2 = LaneGatesParts2[i];
                            if (LaneGatesParts3.Count == LaneGatePoints.Count)
                                ent3 = LaneGatesParts3[i];

                            if (ent == null)
                            {
                                MyAPIGateway.Utilities.ShowMessage("TRADE LANES UAS100", "ent was faulty");
                                break;
                            }

                            ent.WorldMatrix = MatrixD.CreateWorld(loc + (LaneMatrix.Right * (RingOffset * RingScaleMult)), LaneMatrix.Forward, LaneMatrix.Up);
                            if (ent1 != null)
                                ent1.WorldMatrix = MatrixD.CreateWorld(loc + (LaneMatrix.Right * (RingOffset * RingScaleMult)), LaneMatrix.Forward, LaneMatrix.Up);
                            if (ent2 != null)
                                ent2.WorldMatrix = MatrixD.CreateWorld(loc + (LaneMatrix.Right * (RingOffset * RingScaleMult)), LaneMatrix.Forward, LaneMatrix.Up);
                            if (ent3 != null)
                                ent3.WorldMatrix = MatrixD.CreateWorld(loc + (LaneMatrix.Right * (RingOffset * RingScaleMult)), LaneMatrix.Forward, LaneMatrix.Up);

                            //var detectDummy = GetDummyByName(TargetLaneEntity, "dummy_lane_detect_1");
                            //var dockDummy = GetDummyByName(TargetLaneEntity, "dummy_lane_dock_1");
                            //var prepDummy = GetDummyByName(TargetLaneEntity, "dummy_lane_prep_1");
                            //var gateDummy = GetDummyByName(TargetLaneEntity, "dummy_lane_1");

                            if (!LaneDataDict.ContainsKey(ent))
                                LaneDataDict[ent] = new LaneData();

                            /*
                            LaneDataDict[ent].DetectDummyLoc = Vector3D.Transform(detectDummy.Matrix.Translation, ent.WorldMatrix);
                            LaneDataDict[ent].DockDummyLoc = Vector3D.Transform(dockDummy.Matrix.Translation, ent.WorldMatrix);
                            LaneDataDict[ent].PrepDummyLoc = Vector3D.Transform(prepDummy.Matrix.Translation, ent.WorldMatrix);
                            LaneDataDict[ent].GateDummyLoc = Vector3D.Transform(gateDummy.Matrix.Translation, ent.WorldMatrix);

                            LaneDataDict[ent].DetectBox = new MyOrientedBoundingBoxD();
                            CreateOBB(ent, detectDummy, out LaneDataDict[ent].DetectBox);
                            CreateOBB(ent, gateDummy, out LaneDataDict[ent].GateBox);
                            */
                        }

                        TargetLaneEntity = LaneGates[LaneGates.Count - 1];
                        GateBoundingBox.Center = TargetLaneEntity.WorldMatrix.Translation;
                        /*
                        TargetLaneDummy = GetDummyByName(TargetLaneEntity, "dummy_lane_1");
                        if (TargetLaneDummy != null)
                        {
                            TargetLaneDummyLocation = Vector3D.Transform(TargetLaneDummy.Matrix.Translation, TargetLaneEntity.WorldMatrix);
                            CreateOBB(TargetLaneEntity, TargetLaneDummy, out TargetLaneGateBox);
                        }
                        */

                        /*
                        foreach (var gate in LaneGates)
                        {
                            if (Vector3D.DistanceSquared(MyAPIGateway.Session.Camera.Position, gate.WorldMatrix.Translation) > 10 * 10)
                            //if (Vector3D.Distance(MyAPIGateway.Session.Player.Character.WorldMatrix.Translation, gate.WorldMatrix.Translation) > 10)
                            {
                                if (GateParticles.ContainsKey(gate))
                                    StopParticleEffects(GateParticles[gate]);
                                else
                                    ResumeParticleEffects(GateParticles[gate]);
                            }
                        }
                        */

                        /*
                        for (int i = LaneGates.Count - 1; i >= 0; i--)
                        {
                            var gate = LaneGates[i];
                            if (gate != null)
                            {
                                if (GateParticles.ContainsKey(gate))
                                {
                                    if (MyAPIGateway.Session?.Player?.Character != null && Vector3D.Distance(MyAPIGateway.Session.Player.Character.WorldMatrix.Translation, gate.WorldMatrix.Translation) > 10)
                                    {
                                    }
                                        RemoveParticleEffects(GateParticles[gate], true);
                                        GateParticles.Remove(gate);
                                }
                            }
                        }
                        */

                        /*
                        if (ent != null)
                        {
                            if (!GateParticles.ContainsKey(ent))
                            {
                                //RingParticles = SpawnParticleEffectsOnto(ent, RingActivationAndIdleParticleName, MatrixD.Identity);
                                GateParticles[ent] = SpawnParticleEffectsOnto(ent, RingActivationAndIdleParticleName, MatrixD.Identity);
                            }
                            //GateParticles.Add(RingParticles);

                            //var sound = SpawnSoundEmitter(ent as IMyEntity, "ArcDroneLoopSmall", 2f);
                            //GateSounds[ent] = sound;
                        }
                        */

                        if (MyAPIGateway.Session?.Player?.Character != null && MyAPIGateway.Session?.Camera != null)
                        {
                            foreach (var gate in LaneGates)
                            {
                                if (gate != null)
                                {
                                    //if (Vector3D.Distance(MyAPIGateway.Session.Player.Character.WorldMatrix.Translation, gate.WorldMatrix.Translation) > ParticleDistance)
                                    if (Vector3D.DistanceSquared(MyAPIGateway.Session.Camera.Position, gate.WorldMatrix.Translation) > ParticleDistance * ParticleDistance)
                                    {
                                        if (GateParticles.ContainsKey(gate))
                                        {
                                            RemoveParticleEffects(GateParticles[gate], true);
                                            GateParticles.Remove(gate);
                                        }
                                    }
                                    else
                                    {
                                        if (!GateParticles.ContainsKey(gate))
                                        {
                                            //RingParticles = SpawnParticleEffectsOnto(ent, RingActivationAndIdleParticleName, MatrixD.Identity);
                                            if (MyAPIGateway.Utilities.IsDedicated == false)
                                                GateParticles[gate] = SpawnParticleEffectsOnto(gate, RingActivationAndIdleParticleName, MatrixD.Identity);
                                        }
                                    }
                                }
                            }
                        }

                        foreach (var gate in LaneGates)
                        {
                            //ArcDroneLoopSmall
                            //ArcDroneLoopMedium
                            //ArcDroneLoopLarge
                            //ShipSmallRunSlow
                            //ShipSmallRunMedium
                            //ShipSmallEngine
                            //ShipLargeIdle
                            //ShipLargeRunLoop
                            //ShipLargeEngine
                            //ArcPlayJet
                            //ArcPlayJetRun
                            //RealPlayJet
                            //ShipSmallRunSlow
                            //var sound = SpawnSoundFX(gate, "ArcDroneLoopLarge", 0.8f, 500f);
                            //var sound = SpawnSoundFX(gate, RingIdleSoundName, RingIdleSoundVolume, RingIdleSoundDistance);
                            if (gate == null)
                                continue;

                            if (!LaneRingSounds.ContainsKey(gate))
                            {
                                if (MyAPIGateway.Utilities.IsDedicated == false)
                                    LaneRingSounds[gate] = SpawnSoundFX(gate, RingIdleSoundName, RingIdleSoundVolume, RingIdleSoundDistance);
                            }
                        }
                    }
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES UpdateRings", e.Message);
            }
        }

        public override void UpdateBeforeSimulation()
        {
            /*
            try
            {
                foreach (var ring in LaneGatesParts1)
                {
                    RotateRing(ring, Vector3D.Forward, MathHelper.ToRadians(5));
                }

                if (TargetBlock == null)
                    return;

                // HANDLE GRIDS IN TRANSIT
                TransitHandler();
                TriggerActivators();
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES UAS", e.Message);
            }
            */
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                foreach (var ring in LaneGatesParts1)
                {
                    if (MyAPIGateway.Session?.Player?.Character != null && Vector3D.Distance(ring.WorldMatrix.Translation, MyAPIGateway.Session.Player.Character.WorldMatrix.Translation) < 3000)
                        RotateRing2(ring, Vector3D.Forward, MathHelper.ToRadians(RingRotationSpeed));
                }

                if (!HasTarget)
                    return;

                StandInFrame++;

                // A ship in transit drives itself, see Transit.cs
                TriggerActivators();

                if (LaneGates.Count > 0)
                {
                    if (RingLightBlinkFrame > 0 && --RingLightBlinkFrame <= 0)
                    {
                        EmissiveSwitch = !EmissiveSwitch;

                        float emissiveStrength = EmissiveSwitch ? 10f : 0f;

                        foreach (var gate in LaneGates)
                        {
                            //UpdateEmissive(gate, Color.DeepSkyBlue, emissiveStrength);
                            //UpdateEmissive(gate, Color.DodgerBlue, emissiveStrength, "Emissive0");
                            //UpdateEmissive(gate, Color.OrangeRed, emissiveStrength, "Emissive1"); ;
                            UpdateEmissive(gate, Color.Red, emissiveStrength, "Emissive3");
                            UpdateEmissive(gate, Color.Green, emissiveStrength, "Emissive4");
                        }

                        if (!EmissiveSwitch)
                            RingLightBlinkFrame = RingLightBlinkLength;
                        else
                            RingLightBlinkFrame = RingLightBlinkInterval;
                    }
                }
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES UAS", e.Message);
            }
        }

        #endregion

        #region EVENTS

        private void Block_CustomDataChanged(IMyTerminalBlock block)
        {
            // DOESN'T SEEM TO WORK
            //MyAPIGateway.Utilities.ShowMessage("TL", $"Custom data changed");
            CustomData = block.CustomData;
            SetupSystems();
        }

        #endregion

        #region CORE EXEC

        private void RescaleEntity(MyEntity entity, float scale)
        {
            if (entity == null)
                return;

            // Get the current world matrix
            MatrixD worldMatrix = entity.WorldMatrix;

            // Apply scaling to the matrix
            MatrixD scalingMatrix = MatrixD.CreateScale(scale);
            worldMatrix = scalingMatrix * worldMatrix;

            // Update the entity's world matrix
            entity.WorldMatrix = worldMatrix;
        }

        void UpdateSystems()
        {
            try
            {
                var id = ReadCustomData(CustomData, TradeLaneIdKeyword, TradeLaneSeparator);
                var from = ReadCustomData(CustomData, TradeLaneHomeKeyword, TradeLaneSeparator);
                var to = ReadCustomData(CustomData, TradeLaneTargetKeyword, TradeLaneSeparator);

                if (string.IsNullOrEmpty(id) == false && string.IsNullOrEmpty(from) == false && string.IsNullOrEmpty(to) == false)
                {
                    Block.CubeGrid.CustomName = "TL " + id + " " + from + " > " + to;
                }

                ReadSpeed();
                ReadRingInterval();

                if (SafeZoneEnabled)
                    UpdateSafeZone();

                UpdateGates();
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES SetupSystems (5)", e.Message);
            }
        }

        void UpdateSafeZone()
        {
            try
            {
                /*
                var id = ReadCustomData(CustomData, TradeLaneIdKeyword, TradeLaneSeparator);
                var from = ReadCustomData(CustomData, TradeLaneHomeKeyword, TradeLaneSeparator);
                var to = ReadCustomData(CustomData, TradeLaneTargetKeyword, TradeLaneSeparator);
                */
                /*
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                {
                    return;
                }
                */

                //Block.CubeGrid.CustomName = "TL " + id + " " + from + " > " + to;

                float radius = SafeZoneRadius;
                var data = ReadCustomData(CustomData, TradeLaneComputerSafeZoneRadiusKeyword, TradeLaneSeparator);

                if (float.TryParse(data, out radius))
                {
                    if (radius == 0)
                    {
                        if (SafeZone != null)
                        {
                            //SafeZoneRadius = radius;
                            //SafeZone.Radius = SafeZoneRadius;
                            SafeZone.Close();
                            SafeZone = null;
                        }
                        //SafeZone = SpawnSafeZone(Block.WorldMatrix.Translation, SafeZoneRadius, SafeZoneTexture, SafeZoneShape);
                    }
                    else
                    {
                        var newRadius = MathHelper.Clamp(radius, 10, 500); // the game seems to clamp it already to those values

                        if (SafeZone != null)
                        {
                            if (SafeZone.Radius != newRadius)
                            {
                                // just changing the radius does not seem to update the safe zone so we're recreating it instead

                                //SafeZone.Radius = newRadius;
                                //SafeZone.Render.UpdateRenderObject(true);

                                SafeZone.Close();
                                SafeZone = null;
                                SafeZone = SpawnSafeZone(Block.WorldMatrix.Translation, newRadius, SafeZoneTexture, SafeZoneShape);
                            }
                        }
                        else
                        {
                            SafeZone = SpawnSafeZone(Block.WorldMatrix.Translation, newRadius, SafeZoneTexture, SafeZoneShape);
                        }
                    }
                }
                else
                {
                    if (SafeZoneRadius == 0)
                    {
                        if (SafeZone != null)
                        {
                            SafeZone.Close();
                            SafeZone = null;
                        }
                    }
                    else
                    {
                        SafeZone = SpawnSafeZone(Block.WorldMatrix.Translation, SafeZoneRadius, SafeZoneTexture, SafeZoneShape);
                    }
                }
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES UpdateSafeZone", e.Message);
            }
        }

        void UpdateGates()
        {
            try
            {
                var scaleData = ReadCustomData(CustomData, TradeLaneRingScaleKeyword, TradeLaneSeparator);
                var renderData = ReadCustomData(CustomData, TradeLaneRingInvisibleModelsKeyword, TradeLaneSeparator);
                float scale = RingScaleMult;
                float newScaleMult = RingScaleMult;
                bool visibleRings = RenderRings;
                bool renderVisible = RenderRings;
                if (string.IsNullOrEmpty(scaleData) == false)
                {
                    if (float.TryParse(scaleData, out scale))
                        newScaleMult = MathHelper.Clamp(scale, 0.1f, 10f); // 0.5 is a bit low lol, should probably keep it at 1f or larger but whatever xD
                }
                if (string.IsNullOrEmpty(renderData) == false)
                {
                    if (bool.TryParse(renderData, out visibleRings))
                        renderVisible = visibleRings;
                }

                //RingOffset = 160f + (60f * RingScaleMult);
                //RingOffset = (160f + 60f) * RingScaleMult;
                //RingOffset = RingOffsetDefault * newScaleMult;

                if (LaneGates.Count > 0)
                {
                    foreach (var gate in LaneGates)
                    {
                        gate.PositionComp.Scale = newScaleMult;

                        gate.Render.Visible = renderVisible;
                        gate.Render.UpdateRenderObject(true);
                        gate.Render.UpdateRenderObjectLocal(gate.PositionComp.LocalMatrixRef);

                        /*
                        if (GateParticles.ContainsKey(gate))
                        {
                            if (GateParticles[gate] != null)
                            {
                                GateParticles[gate].UserScale = RingScaleMult;
                            }
                        }
                        */
                    }

                    foreach (var ring in LaneGatesParts1)
                    {
                        ring.PositionComp.Scale = newScaleMult;

                        ring.Render.Visible = renderVisible;
                        ring.Render.UpdateRenderObject(true);
                        ring.Render.UpdateRenderObjectLocal(ring.PositionComp.LocalMatrixRef);
                    }
                }

                /*
                if (LaneGates.Count > 0)
                {
                    foreach (MyEntity ring in LaneGates)
                    {
                        RescaleEntity(ring, RingScaleMult);
                    }
                }
                */
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES UpdateGates", e.Message);
            }
        }

        /*
        private MyCubeGrid CheckAndSpawnBlock(Vector3D location, MatrixD gateOrientation, string blockSubtypeId = "LargeBlockArmorBlock")
        {
            // Define a small bounding box around the location to check for existing entities
            BoundingBoxD checkBox = new BoundingBoxD(location - new Vector3D(0.5, 0.5, 0.5), location + new Vector3D(0.5, 0.5, 0.5));
            List<MyEntity> intersectingEntities = new List<MyEntity>();

            // Check for existing entities at the location
            MyGamePruningStructure.GetAllEntitiesInBox(ref checkBox, intersectingEntities);

            // If any entities are found, return null (no block will be spawned)
            if (intersectingEntities.Count > 0)
            {
                return null;
            }

            // Create an object builder for the armor block
            var blockBuilder = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeBlock>();
            blockBuilder.SubtypeName = blockSubtypeId; // Set the block subtype (e.g., "LargeBlockArmorBlock")
            blockBuilder.Min = Vector3I.Zero; // Position within the grid
            blockBuilder.BlockOrientation = new MyBlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up);
            blockBuilder.EntityId = MyEntityIdentifier.AllocateId(); // Assign a unique entity ID

            // Create an object builder for the grid
            var gridBuilder = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeGrid>();
            gridBuilder.GridSizeEnum = MyCubeSize.Large; // Set grid size (Large or Small)
            gridBuilder.IsStatic = true; // Set the grid to static
            gridBuilder.PositionAndOrientation = new MyPositionAndOrientation(location, gateOrientation.Forward, gateOrientation.Up);
            gridBuilder.CubeBlocks.Add(blockBuilder); // Add the block to the grid

            // Create the grid entity from the object builder
            var gridEntity = MyEntities.CreateFromObjectBuilder(gridBuilder, false) as MyCubeGrid;
            if (gridEntity == null)
            {
                MyAPIGateway.Utilities.ShowMessage("Trade Lane Error", "Failed to create grid.");
                return null;
            }

            // Add the grid to the game world
            MyEntities.Add(gridEntity);

            return gridEntity;
        }
        */

        void SetupSystems()
        {
            // it's now a singular block that doesn't allow block placements anymore so no need to check or setup antennas
            return;

            // Cuz holly fuk man....
            if (Block == null || Block.CubeGrid == null || string.IsNullOrEmpty(Block.CustomData))
                return;

            var speed = ReadCustomData(CustomData, TradeLaneSpeedKeyword, TradeLaneSeparator);
            if (string.IsNullOrEmpty(speed) == false)
            {
                float superluminalSpeed = 0;
                if (float.TryParse(speed, out superluminalSpeed))
                    SuperluminalSpeed = MathHelper.Clamp(superluminalSpeed, 500f, 500000f);
            }

            string antennaData = "";
            IMyRadioAntenna rightAntenna = null;
            IMyRadioAntenna leftAntenna = null;
            IMyRadioAntenna antenna = null;

            try
            {
                //MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "CustomDataChanged");
                //CheckBlockCustomData();
                //asdasd
                rightAntenna = GetAntennaInDirection(Block.CubeGrid, Block, Block.WorldMatrix.Right);
                leftAntenna = GetAntennaInDirection(Block.CubeGrid, Block, Block.WorldMatrix.Left);
                antenna = GetAntenna(Block.CubeGrid, rightAntenna, leftAntenna);
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES SetupSystems (1)", e.Message);
            }

            try
            {
                if (antenna != null)
                {
                    /*
                    int laneId = 0;
                    if (!int.TryParse(Block.CustomName.Substring(Block.CustomName.LastIndexOf(':') + 1), out laneId))
                        antenna.CustomName = "Trade Lane " + laneId.ToString();
                    */
                    antennaData = ReadCustomData(CustomData, TradeLaneIdKeyword, TradeLaneSeparator);

                    if (!string.IsNullOrEmpty(antennaData))
                    {
                        antenna.Radius = AntennaRange;
                        antenna.HudText = $"{AntennaHudText} {antennaData}";
                    }
                }
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES SetupSystems (2)", e.Message);
            }

            try
            {
                if (rightAntenna != null)
                {
                    antennaData = ReadCustomData(CustomData, TradeLaneTargetKeyword, TradeLaneSeparator);

                    if (!string.IsNullOrEmpty(antennaData))
                    {
                        rightAntenna.Radius = 550f;
                        rightAntenna.HudText = $"To: {antennaData}";
                    }
                }
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES SetupSystems (3)", e.Message);
            }

            try
            {
                if (leftAntenna != null)
                {
                    antennaData = ReadCustomData(CustomData, TradeLaneHomeKeyword, TradeLaneSeparator);

                    if (!string.IsNullOrEmpty(antennaData))
                    {
                        leftAntenna.Radius = 550f;
                        leftAntenna.HudText = $"From: {antennaData}";
                    }
                }
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES SetupSystems (4)", e.Message);
            }
            return;

            // the rest of the setup happens elsewhere

            try
            {
                if (SafeZoneEnabled)
                {
                    var zoneSize = ReadCustomData(CustomData, TradeLaneComputerSafeZoneRadiusKeyword, TradeLaneSeparator);
                    if (string.IsNullOrEmpty(zoneSize) == false)
                    {
                        float radius = SafeZoneRadius;
                        if (float.TryParse(zoneSize, out radius))
                        {
                            if (radius == 0)
                            {
                                if (SafeZone != null)
                                {
                                    //SafeZoneRadius = radius;
                                    //SafeZone.Radius = SafeZoneRadius;
                                    SafeZone.Close();
                                    SafeZone = null;
                                }
                                //SafeZone = SpawnSafeZone(Block.WorldMatrix.Translation, SafeZoneRadius, SafeZoneTexture, SafeZoneShape);
                            }
                            else
                            {
                                if (SafeZone != null)
                                {
                                    SafeZoneRadius = MathHelper.Clamp(radius, 1f, 750f); // why 750? dunno, but there you have it lol
                                    SafeZone.Radius = SafeZoneRadius;
                                }
                                else
                                {
                                    SafeZone = SpawnSafeZone(Block.WorldMatrix.Translation, SafeZoneRadius, SafeZoneTexture, SafeZoneShape);
                                }
                            }
                        }
                        //SafeZoneRadius = MathHelper.Clamp(radius, 1f, 600f);
                    }
                    //SafeZone.Radius = SafeZoneRadius;
                }

                var scale = ReadCustomData(CustomData, TradeLaneRingScaleKeyword, TradeLaneSeparator);
                if (string.IsNullOrEmpty(scale) == false)
                {
                    float scaleMult = RingScaleMult;
                    if (float.TryParse(scale, out scaleMult))
                        RingScaleMult = MathHelper.Clamp(scaleMult, 0.5f, 5f); // 0.5 is a bit low lol, should probably keep it at 1f or larger but whatever xD
                }

                // TODO: if 'PositionComp.Scale' doens't work, needs to recreate the rings, so deleting them and re-spawing them to a new scale,
                // otherwise the matrix rescale needs to be applied each update (not good for performance).
                // currently the problem is code handles spawning and despawning of the rings live through code.
                // however, new scale should be applied when cut-pasting the trade lane computer again.
                if (LaneGates.Count > 0)
                {
                    foreach(var ring in LaneGates)
                    {
                        ring.PositionComp.Scale = RingScaleMult;
                    }
                }
                /*
                if (LaneGates.Count > 0)
                {
                    foreach (MyEntity ring in LaneGates)
                    {
                        RescaleEntity(ring, RingScaleMult);
                    }
                }
                */
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES SetupSystems (5)", e.Message);
            }
        }

        private MySafeZone SpawnSafeZone(Vector3D position, float radius, string texture = "SafeZone_Texture_Default", MySafeZoneShape shape = MySafeZoneShape.Sphere)
        {
            // Create an object builder for the safe zone
            var safeZoneBuilder = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_SafeZone>();
            safeZoneBuilder.PositionAndOrientation = new MyPositionAndOrientation(position, Vector3D.Forward, Vector3D.Up);
            safeZoneBuilder.Radius = radius;
            safeZoneBuilder.Shape = shape;
            //safeZoneBuilder.AllowedActions = 0;
            //safeZoneBuilder.DisplayName = "Trade Lane Safe Zone";
            safeZoneBuilder.DisplayName = Block.CubeGrid.CustomName;
            //safeZoneBuilder.Texture = "Textures\\Models\\Environment\\SafeZone\\SafeZoneShield_Disabled_alphamask.dds"; // Set the texture for the safe zone
            //safeZoneBuilder.Texture = "E:\\SteamLibrary\\steamapps\\common\\SpaceEngineers\\Content\\Textures\\Models\\Environment\\SafeZone\\SafeZoneShield_Hexagon_alphamask.dds"; // Set the texture for the safe zone
            safeZoneBuilder.Texture = SafeZoneTexture; // Set the texture for the safe zone
            safeZoneBuilder.Enabled = true;

            // SafeZone_Texture_Disabled
            // SafeZone_Texture_KeenSWH
            // SafeZone_Texture_Restricted
            // SafeZone_Texture_Voronoi
            // SafeZone_Texture_Clang
            // SafeZone_Texture_Gloura
            // SafeZone_Texture_Digital
            // SafeZone_Texture_Lines
            // SafeZone_Texture_Hexagon
            // SafeZone_Texture_Noise
            // SafeZone_Texture_Disco
            // SafeZone_Texture_Dots
            // SafeZone_Texture_Rain
            // SafeZone_Texture_Organic
            // SafeZone_Texture_Aura
            // SafeZone_Texture_Default

            // Set allowed actions to prevent damage
            // MySafeZoneAction PROHIBITED
            //safeZoneBuilder.AllowedActions = MySafeZoneAction.Shooting | MySafeZoneAction.Grinding | MySafeZoneAction.Drilling | MySafeZoneAction.Welding;
            //int Shooting = 2;
            //int Grinding = 16;
            //int Drilling = 4;
            //int Welding = 8;
            //safeZoneBuilder.AllowedActions = Shooting | Grinding | Drilling | Welding;

            // Create the safe zone entity from the object builder
            var safeZone = MyEntities.CreateFromObjectBuilder(safeZoneBuilder, false) as MySafeZone;

            if (safeZone == null)
            {
                MyAPIGateway.Utilities.ShowMessage("SpawnSafeZone", "Failed to create safe zone.");
                return null;
            }

            safeZone.AllowedActions = 0;
            safeZone.Save = false; // Prevent saving the safe zone in the world

            // Configure the safe zone properties
            //safeZone.AccessTypePlayers = MySafeZoneAccess.Whitelist; // Allow only whitelisted players
            //safeZone.AccessTypeFactions = MySafeZoneAccess.Blacklist; // Block specific factions
            safeZone.AccessTypePlayers = MySafeZoneAccess.Blacklist; // Allow all players
            safeZone.AccessTypeFactions = MySafeZoneAccess.Blacklist; // Block specific factions
            safeZone.AccessTypeGrids = MySafeZoneAccess.Blacklist; // Block specific factions
            safeZone.AccessTypeFloatingObjects = MySafeZoneAccess.Blacklist; // Block specific factions

            // Add the safe zone to the game world
            MyEntities.Add(safeZone);

            return safeZone;
        }

        private void RotateRing(MyEntity ringEntity, Vector3D rotationAxis, double rotation, bool continuous = false)
        {
            /*
            if (MyAPIGateway.Utilities.IsDedicated == true)
                return;
            */

            if (ringEntity == null)
                return;

            // Get the current world matrix of the ring
            MatrixD currentWorldMatrix = ringEntity.WorldMatrix;

            MatrixD rotationMatrix;
            // Create a rotation matrix around the specified axis
            if (continuous)
                rotationMatrix = MatrixD.CreateFromAxisAngle(rotationAxis, rotation * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS);
            else
                rotationMatrix = MatrixD.CreateFromAxisAngle(rotationAxis, rotation);

            /*
            if (direction == 1)
            {
                // Keep rotation within 0 to 2π for cleaner values
                rotation += (rotator * speedMultiplier);
                if (rotation > MathHelper.TwoPi)
                    rotation -= MathHelper.TwoPi;
            }
            else
            {
                rotation -= (rotator * speedMultiplier);
                if (rotation < 0)
                    rotation = MathHelper.TwoPi;
            }
            */

            // Apply the rotation to the ring's world matrix
            MatrixD newWorldMatrix = rotationMatrix * currentWorldMatrix;

            // Preserve the ring's current position
            newWorldMatrix.Translation = currentWorldMatrix.Translation;

            // Update the ring's world matrix
            ringEntity.WorldMatrix = newWorldMatrix;
        }

        double Rotator = 0;
        private void RotateRing2(MyEntity ringEntity, Vector3D rotationAxis, double rotation, bool continuous = false)
        {
            /*
            if (MyAPIGateway.Utilities.IsDedicated == true)
                return;
            */

            //Rotator = rotation;

            if (ringEntity == null || rotation == 0 || ringEntity.Render.Visible == false)
                return;

            // Get the current world matrix of the ring
            MatrixD currentWorldMatrix = ringEntity.WorldMatrix;

            MatrixD rotationMatrix;

            /*
            if (continuous)
                rotationMatrix = MatrixD.CreateFromAxisAngle(rotationAxis, rotation * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS);
            else
                rotationMatrix = MatrixD.CreateFromAxisAngle(rotationAxis, Rotator);
            */

            float scalar = 0.1f;

            if (rotation > 0)
            {
                // Keep rotation within 0 to 2π for cleaner values
                Rotator += MathHelper.ToRadians(rotation * scalar);
                if (Rotator > MathHelper.TwoPi)
                    Rotator -= MathHelper.TwoPi;
            }
            else
            {
                Rotator -= MathHelper.ToRadians(rotation * scalar);
                if (Rotator < 0)
                    Rotator = MathHelper.TwoPi;
            }

            // Create a rotation matrix around the specified axis
            rotationMatrix = MatrixD.CreateFromAxisAngle(rotationAxis, Rotator);

            // Apply the rotation to the ring's world matrix
            MatrixD newWorldMatrix = rotationMatrix * currentWorldMatrix;

            // Preserve the ring's current position
            newWorldMatrix.Translation = currentWorldMatrix.Translation;

            // Update the ring's world matrix
            ringEntity.WorldMatrix = newWorldMatrix;
        }

        private void MonitorAndForceStatic(IMyCubeGrid grid)
        {
            if (grid == null)
                return;

            // Continuously monitor the grid's state
            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            {
                if (!grid.IsStatic)
                {
                    grid.IsStatic = true; // Force it back to static
                }
            });
        }

        private void TriggerActivators()
        {
            // @@ GET GRIDS INTO TRANSIT
            if (LaneGates.Count > 0)
            {
                //MyAPIGateway.Utilities.ShowNotification("waiting...", 16);

                //Vector4 color = Color.Green.ToVector4() * 12;

                bool playerInGateRange = false;
                bool updateGps = false;
                Vector3D gateLocation = Vector3D.Zero;
                double dist = 0f;

                for (int i = 0; i < LaneGates.Count; i++)
                {
                    var gate = LaneGates[i];
                    if (gate == null)
                        continue;

                    // for some reason had to move it outside in a new loop
                    /*
                    if (GpsRefreshFrame > 0 && --GpsRefreshFrame <= 0)
                    {
                        if (MyAPIGateway.Session?.Player?.Character != null)
                        {
                            var distance = Vector3D.Distance(MyAPIGateway.Session.Player.Character.WorldMatrix.Translation, gate.WorldMatrix.Translation);
                            if (MathHelper.Floor(distance) <= TradeLaneHeadingMarkerDistance)
                            {
                                dist = distance;
                                playerInGateRange = true;
                                gateLocation = gate.WorldMatrix.Translation;
                            }
                            updateGps = true;
                        }

                        GpsRefreshFrame = GpsRefreshInterval;
                    }
                    */
                    /*
                    else
                    {
                        MyAPIGateway.Utilities.ShowNotification("waiting...", 16);
                    }
                    */

                    /*
                    if (i != (LaneGates.Count - 1))
                    {
                        MyAPIGateway.Utilities.ShowNotification($"i: {i} LangeGates: {LaneGates.Count} Poitns: {LaneGatePoints.Count}", 16);

                        DetectBoundingBox.Center = Vector3D.Transform(DetectDummyLocalMatrix, gate.WorldMatrix);
                        var dummyMatrix = MatrixD.CreateWorld(Vector3D.Transform(DetectDummyLocalMatrix, gate.WorldMatrix));
                        var laneMatrix = gate.WorldMatrix;
                        Quaternion.CreateFromRotationMatrix(ref laneMatrix, out DetectBoundingBox.Orientation);
                        DrawOBB(DetectBoundingBox, color);
                    }
                    */
                    //MyAPIGateway.Utilities.ShowNotification($"i: {i} LaneGates: {LaneGates.Count} LocPoitns: {LaneGatePoints.Count}", 16);

                    if (i == (LaneGates.Count - 1))
                    {
                        GateBoundingBox.Center = Vector3D.Transform(GateDummyLocalMatrix, gate.WorldMatrix);
                        var dummyMatrix = MatrixD.CreateWorld(Vector3D.Transform(GateDummyLocalMatrix, gate.WorldMatrix));
                        var laneMatrix = gate.WorldMatrix;
                        Quaternion.CreateFromRotationMatrix(ref laneMatrix, out GateBoundingBox.Orientation);
                        //DrawOBB(GateBoundingBox, color);
                    }
                    else
                    {
                        DetectBoundingBox.Center = Vector3D.Transform(DetectDummyLocalMatrix, gate.WorldMatrix);
                        var dummyMatrix = MatrixD.CreateWorld(Vector3D.Transform(DetectDummyLocalMatrix, gate.WorldMatrix));
                        var laneMatrix = gate.WorldMatrix;
                        Quaternion.CreateFromRotationMatrix(ref laneMatrix, out DetectBoundingBox.Orientation);
                        //DrawOBB(DetectBoundingBox, color);
                        if (TradeLaneNetwork.IsServer && (!IsStandIn || StandInDue(gate, i)))
                            DockingHandler(gate);
                    }
                }

                if (IsStandIn || Block.CubeGrid.Physics != null)
                {
                    if (GpsRefreshFrame > 0 && --GpsRefreshFrame <= 0)
                    {
                        foreach (var gate in LaneGates)
                        {
                            if (MyAPIGateway.Session?.Player?.Character != null)
                            {
                                var distance = Vector3D.Distance(MyAPIGateway.Session.Player.Character.WorldMatrix.Translation, gate.WorldMatrix.Translation);
                                if (MathHelper.Floor(distance) <= TradeLaneHeadingMarkerDistance)
                                {
                                    dist = distance;
                                    playerInGateRange = true;
                                    gateLocation = gate.WorldMatrix.Translation;
                                }
                                updateGps = true;
                            }

                            GpsRefreshFrame = GpsRefreshInterval;
                        }
                    }
                }

                if (updateGps)
                {
                    //var customData = Block.CustomData;
                    var customData = CustomData;
                    var id = ReadCustomData(customData, TradeLaneIdKeyword, TradeLaneSeparator);
                    var from = ReadCustomData(customData, TradeLaneHomeKeyword, TradeLaneSeparator);
                    var to = ReadCustomData(customData, TradeLaneTargetKeyword, TradeLaneSeparator);

                    //MyAPIGateway.Utilities.ShowMessage($"update gps", $"distance: {dist}");

                    if (playerInGateRange)
                    {
                        //MyAPIGateway.Utilities.ShowMessage($"add gps", $"id: {Block.CubeGrid.EntityId}");
                        AddGpsMarker(gateLocation, id, from, to);
                    }
                    else
                    {
                        //MyAPIGateway.Utilities.ShowMessage($"remove gps", $"id: {Block.CubeGrid.EntityId}");
                        RemoveGpsMarker();
                    }

                    GpsMarkers(LaneMatrix.Translation, id, from, to);
                    //UpdateGpsDistancesIndex(id, from, to);
                    /*
                    var existingGps = MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId);
                    int index = 0;
                    if (existingGps != null && existingGps.Count > 0)
                    {
                        foreach (var gps in existingGps)
                        {
                            var distance = Vector3D.Distance(MyAPIGateway.Session.Player.Character.WorldMatrix.Translation, gps.Coords);

                            if (gps.Description.Contains($"Trade Lane Network\nID:{Block.CubeGrid.EntityId}"))
                            {
                                //MyAPIGateway.Utilities.ShowMessage($"gps", $"id: {gps.Name}");
                                //MyAPIGateway.Utilities.ShowMessage($"gps", $"id: {gps.Description}");
                                index++;
                            }
                        }
                    }
                    */
                }
                //return;
            }
        }

        private class GpsWithDistance
        {
            public IMyGps Gps { get; set; }
            public double Distance { get; set; }
        }

        private void UpdateGpsDistancesIndex(string id, string from, string to)
        {
            if (MyAPIGateway.Session?.Player?.Character == null)
                return;

            var globalGps = $"Trade Lane {id} | {from}-{to}";
            var globalLaneIdent = $"Trade Lane Network\nID:{LaneGridId}";

            Vector3D characterLocation = MyAPIGateway.Session.Player.Character.WorldMatrix.Translation;

            var gpsList = MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId);

            if (gpsList == null || gpsList.Count == 0)
                return;

            var gpsWithDistances = new List<GpsWithDistance>();

            foreach (var gps in gpsList)
            {
                if (gps.Description.Contains(globalLaneIdent))
                {
                    double distance = Vector3D.Distance(characterLocation, gps.Coords);
                    gpsWithDistances.Add(new GpsWithDistance { Gps = gps, Distance = distance });
                }
            }

            gpsWithDistances.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            for (int i = 0; i < gpsWithDistances.Count; i++)
            {
                var gpsWithDistance = gpsWithDistances[i];
                var gpsName = $"{i + 1} {globalGps}";
                if (gpsWithDistance.Gps.Name.Contains(globalGps))
                {
                    if (gpsWithDistance.Gps.Name != gpsName)
                    {
                        gpsWithDistance.Gps.Name = gpsName;
                        MyAPIGateway.Session.GPS.ModifyGps(MyAPIGateway.Session.Player.IdentityId, gpsWithDistance.Gps);
                    }
                }
            }
        }

        private void AddGpsMarker(Vector3D translation, string id, string from, string to)
        {
            if ((!IsStandIn && (Block.CubeGrid == null || Block.CubeGrid.Physics == null)) || MyAPIGateway.Session?.Player == null)
                return;

            //var localGpsName = $"Trade Lane {id}\nFrom {from}\nTo {to}";
            var localGpsName = $"TL{id} | {from} > {to}";
            var localLaneIdent = $"Trade Lane Information\nID:{LaneGridId}";

            var existingGps = MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId)
            .FirstOrDefault(gps => gps.Description.Contains(localLaneIdent));
            //.FirstOrDefault(gps => gps.Name == localGpsName);

            if (existingGps == null)
            {
                //MyAPIGateway.Utilities.ShowNotification("create");
                var gps = MyAPIGateway.Session.GPS.Create(localGpsName, localLaneIdent, translation, true, false);
                gps.GPSColor = Color.Gold.ToVector4();
                MyAPIGateway.Session.GPS.AddLocalGps(gps);
            }
            else
            {
                if (existingGps.Name != localGpsName || existingGps.Coords != translation)
                {
                    //MyAPIGateway.Utilities.ShowNotification("change");
                    existingGps.Name = localGpsName;
                    existingGps.Coords = translation;
                    MyAPIGateway.Session.GPS.ModifyGps(MyAPIGateway.Session.Player.IdentityId, existingGps);
                    /*
                    MyAPIGateway.Session.GPS.RemoveLocalGps(existingGps);
                    var gps = MyAPIGateway.Session.GPS.Create(localGpsName, localLaneIdent, translation, false, false);
                    MyAPIGateway.Session.GPS.AddLocalGps(gps);
                    */
                }
            }
        }

        private void RemoveGpsMarker()
        {
            //return;
            if ((!IsStandIn && Block.CubeGrid == null) || MyAPIGateway.Session?.Player == null)
                return;
            //MyAPIGateway.Utilities.ShowNotification("remove");
            var localLaneIdent = $"Trade Lane Information\nID:{LaneGridId}";
            var existingGps = MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId)
            .FirstOrDefault(gps => gps.Description.Contains(localLaneIdent));
            if (existingGps != null)
            {
                MyAPIGateway.Session.GPS.RemoveLocalGps(existingGps);
            }
        }

        private void GpsMarkers(Vector3D translation, string id, string from, string to)
        {
            if (MyAPIGateway.Session?.Player?.Character == null)
                return;

            var globalGps = $"Trade Lane {id} | {from}-{to}";
            var localGps = $"Trade Lane {id}\nFrom {from}\nTo {to}";

            var dist = MathHelper.RoundOn2((float)Vector3D.Distance(MyAPIGateway.Session.Player.Character.WorldMatrix.Translation, LaneMatrix.Translation));

            //GpsMarkers.Clear();

            if (string.IsNullOrEmpty(id) == false && string.IsNullOrEmpty(from) == false && string.IsNullOrEmpty(to) == false && (IsStandIn || Block.CubeGrid != null))
            {
                var gridId = LaneGridId;

                var globalLaneIdent = $"Trade Lane Network\nID:{LaneGridId}";
                var localLaneIdent = $"Trade Lane Information\nID:{LaneGridId}";

                /*
                if (GpsMarkers.Contains(globalGps) == false)
                    GpsMarkers.Add(globalGps);
                if (GpsMarkers.Contains(gpsName) == false)
                    GpsMarkers.Add(gpsName);
                */

                if (string.IsNullOrEmpty(from) == false && string.IsNullOrEmpty(to) == false)
                {
                    var existingGps = MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId)
                        .FirstOrDefault(gps => gps.Description.Contains(globalLaneIdent));
                    //.FirstOrDefault(gps => gps.Name == globalGps);
                    if (string.IsNullOrEmpty(globalGps) == false)
                    {
                        if (existingGps == null)
                        {
                            var gps = MyAPIGateway.Session.GPS.Create(globalGps, $"{globalLaneIdent}\nFrom: {from}\nTo: {to}", translation, false, false);
                            //gps.GPSColor = Color.Gold.ToVector4();
                            gps.GPSColor = new Vector4(255, 190, 0 ,255);
                            MyAPIGateway.Session.GPS.AddLocalGps(gps);
                        }
                        else
                        {
                            if (existingGps.Name.Contains(globalGps) == false)
                            {
                                MyAPIGateway.Session.GPS.RemoveLocalGps(existingGps);
                                var gps = MyAPIGateway.Session.GPS.Create(globalGps, $"{globalLaneIdent}\nFrom: {from}\nTo: {to}", translation, false, false);
                                MyAPIGateway.Session.GPS.AddLocalGps(gps);
                            }
                        }
                    }
                }

                return;

                if (MyAPIGateway.Session?.Player?.Character != null)
                {
                    if (PlayersInTransit.Contains(MyAPIGateway.Session.Player.IdentityId) == false)
                    {
                        if (string.IsNullOrEmpty(localGps) == false)
                        {
                            var distance = Vector3D.Distance(MyAPIGateway.Session.Player.Character.WorldMatrix.Translation, translation);

                            if (MathHelper.Floor(distance) <= TradeLaneHeadingMarkerDistance)
                            {
                                var existingGps = MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId)
                                    .FirstOrDefault(gps => gps.Description.Contains(localLaneIdent));
                                //.FirstOrDefault(gps => gps.Name == localGps);

                                if (existingGps == null)
                                {
                                    var gps = MyAPIGateway.Session.GPS.Create(localGps, localLaneIdent, translation, true, false);
                                    gps.GPSColor = Color.Gold.ToVector4();
                                    MyAPIGateway.Session.GPS.AddLocalGps(gps);
                                }
                                else
                                {
                                    if (existingGps.Name != localGps)
                                    {
                                        MyAPIGateway.Session.GPS.RemoveLocalGps(existingGps);
                                        var gps = MyAPIGateway.Session.GPS.Create(localGps, $"{localLaneIdent}", translation, false, false);
                                        MyAPIGateway.Session.GPS.AddLocalGps(gps);
                                    }
                                }
                            }
                            else
                            {
                                var existingGps = MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId)
                                    .FirstOrDefault(gps => gps.Name == localGps);

                                if (existingGps != null)
                                {
                                    MyAPIGateway.Session.GPS.RemoveLocalGps(existingGps);
                                }
                            }
                        }
                    }
                    else
                    {
                        var existingGps = MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId)
                            .FirstOrDefault(gps => gps.Name == localGps);
                        //.FirstOrDefault(gps => (gps.Description.Contains(localLaneIdent)));
                        if (existingGps != null)
                        {
                            MyAPIGateway.Session.GPS.RemoveLocalGps(existingGps);
                        }
                    }
                }
            }
            else
            {
                var existingGps = MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId)
                    .FirstOrDefault(gps => gps.Name == localGps);
                //.FirstOrDefault(gps => (gps.Name.Contains("Trade Lane") && gps.Name.Contains("Gate") && gps.Name.Contains("From") && gps.Name.Contains("To")));
                if (existingGps != null)
                {
                    MyAPIGateway.Session.GPS.RemoveLocalGps(existingGps);
                }
            }
        }

        float TargetDistanceDisplay = 0;
        float DockStageDistanceDisplay = 0;
        private void DockingHandler(MyEntity gate)
        {
            //foreach (var gate in LaneGates)
            //{
            if (!LaneDataDict.ContainsKey(gate))
                return;

            //var gateData = LaneDataDict[gate];

            //DrawOBB(gateData.DetectBox, color);

            if (LaneDataDict[gate].GridInWaitingLine != null)
            {
                var grid = LaneDataDict[gate].GridInWaitingLine;
                //var mergeStage = LaneDataDict[gate].GridMergeStage;
                //var gridDockingFrame = LaneDataDict[gate].GridDockFrame;
                //var gridPrepFrame = LaneDataDict[gate].GridPrepFrame;

                // Gone, or another lane launched it: docking it on would drag it
                // back here and pin it short of that lane's exit
                if (grid.MarkedForClose || grid.GameLogic.GetAs<CustomGridLogic>()?.IsInTransit == true)
                {
                    LaneDataDict[gate].GridInWaitingLine = null;
                    LaneDataDict[gate].GridMergeStage = 0;
                    LaneDataDict[gate].GridDockFrame = 0;
                    LaneDataDict[gate].GridPrepFrame = 0;
                    return;
                }

                if (grid.DampenersEnabled)
                {
                    OverrideThrusters(LaneDataDict[gate].GridInWaitingLine, Vector3D.Zero, true);
                    LaneDataDict[gate].GridInWaitingLine = null;
                    LaneDataDict[gate].GridMergeStage = 0;
                    LaneDataDict[gate].GridDockFrame = 0;
                    LaneDataDict[gate].GridPrepFrame = 0;

                    /*
                    */
                    //StopSoundEmitter(TradeLaneSoundEmitter);
                    //SpawnSoundEmitter(gate, "ShipJumpDriveJumpOut", ref ShipSoundEmitter, 4f);
                    PlayGateEffect(gate, grid, GateEffectKind.CancelDocking);
                    //LaneDataDict[gate] = gateData;
                    InstructShipSystems(grid, CustomGridLogic.ShipSystem.CancelDocking);
                }
                else
                {
                    /*
                    BoundingBoxD gridBoundingBox = grid.PositionComp.WorldAABB;

                    // Calculate the offset between the grid's bounding box center and its current position
                    Vector3D boundingBoxCenter = grid.PositionComp.WorldAABB.Center;
                    Vector3D offset = currentWorldMatrix.Translation - grid.PositionComp.WorldAABB.Center;

                    // Adjust the target position to account for the offset
                    Vector3D adjustedTargetPosition = targetPosition + currentWorldMatrix.Translation - grid.PositionComp.WorldAABB.Center;
                    */

                    //var currentWorldMatrix = grid.WorldMatrix;
                    var currentWorldMatrix = grid.WorldMatrix;

                    // Get the grid's bounding box
                    BoundingBoxD gridBoundingBox = grid.PositionComp.WorldAABB;

                    // Calculate the offset between the grid's bounding box center and its current position
                    Vector3D boundingBoxCenter = gridBoundingBox.Center;
                    Vector3D offset = currentWorldMatrix.Translation - boundingBoxCenter;

                    // Adjust the currentWorldMatrix to account for the bounding box
                    //currentWorldMatrix.Translation += offset;

                    //var targetWorldMatrix = MatrixD.CreateWorld(TargetLaneDummyLocation);
                    var targetWorldMatrix = LaneGates[LaneGates.Count - 1].WorldMatrix;
                    var waitWorldMatrix = MatrixD.CreateWorld(Vector3D.Transform(DockDummyLocalMatrix, gate.WorldMatrix));
                    Vector3D adjustedTargetPosition = targetWorldMatrix.Translation + offset;
                    var distance = Vector3D.Distance(currentWorldMatrix.Translation, adjustedTargetPosition);
                    var distanceToDockStagePos = Vector3D.Distance(currentWorldMatrix.Translation, waitWorldMatrix.Translation);
                    switch (LaneDataDict[gate].GridMergeStage)
                    {
                        case 1:
                            //targetWorldMatrix = MatrixD.CreateWorld(Vector3D.Transform(TradeLane2_Dock_Dummy.Matrix.Translation, Lane2.WorldMatrix), Lane2.WorldMatrix.Forward, Lane2.WorldMatrix.Up);
                            //SetGridDirection(firstGrid, Vector3D.Transform(TradeLane2_Dummy.Matrix.Translation, Lane2.WorldMatrix), 0.8);
                            targetWorldMatrix = MatrixD.CreateWorld(Vector3D.Transform(DockDummyLocalMatrix, gate.WorldMatrix));
                            adjustedTargetPosition = targetWorldMatrix.Translation + offset;
                            distanceToDockStagePos = Vector3D.Distance(currentWorldMatrix.Translation, adjustedTargetPosition);

                            if (distanceToDockStagePos < DockPositionThreshold) // 125
                            {
                                if (LaneDataDict[gate].GridDockFrame > 0 && --LaneDataDict[gate].GridDockFrame <= 0)
                                {
                                    TradeLaneNetwork.Notify(grid, $"{grid.DisplayName} | Request Granted", sender: "Trade Lane");
                                    LaneDataDict[gate].GridMergeStage = 2;
                                    LaneDataDict[gate].GridPrepFrame = 300;
                                    //LaneDataDict[gate] = gateData;
                                    //TradeLaneSoundEmitter = SpawnSoundEffects(MyAPIGateway.Session.Player.Character as IMyEntity, "ShipJumpDriveCharging", 5f);
                                    //TradeLaneSoundEmitter = SpawnSoundEffects(MyAPIGateway.Session.Player.Character as IMyEntity, "ShipPrototechJumpDriveJumpIn", 1f);
                                    //TradeLaneSoundEmitter = SpawnSoundEmitter(MyAPIGateway.Session.Player.Character as IMyEntity, "ShipJumpDriveCharging", 1f);
                                    //SpawnSoundEmitter(gate, "ShipJumpDriveCharging", ref ShipSoundEmitter, 1f);
                                    PlayGateEffect(gate, grid, GateEffectKind.StageChange);
                                    InstructShipSystems(grid, CustomGridLogic.ShipSystem.Docking);
                                }
                            }
                            break;
                        case 2:
                            //return;
                            //targetWorldMatrix = MatrixD.CreateWorld(Vector3D.Transform(TradeLane2_Prep_Dummy.Matrix.Translation, Lane2.WorldMatrix), Lane2.WorldMatrix.Forward, Lane2.WorldMatrix.Up);
                            //SetGridDirection(firstGrid, adjustedTargetPosition, 0.3);
                            //SetGridDirection(firstGrid, Vector3D.Transform(TradeLane2_Dummy.Matrix.Translation, Lane2.WorldMatrix), 0.8);
                            //targetWorldMatrix = MatrixD.CreateWorld(Vector3D.Transform(PrepDummyLocalMatrix, gate.WorldMatrix));
                            targetWorldMatrix = MatrixD.CreateWorld(Vector3D.Transform(PrepDummyLocalMatrix, gate.WorldMatrix));
                            adjustedTargetPosition = targetWorldMatrix.Translation + offset;
                            distanceToDockStagePos = Vector3D.Distance(currentWorldMatrix.Translation, adjustedTargetPosition);

                            if (distanceToDockStagePos < DockPositionThreshold)
                            {
                                if (LaneDataDict[gate].GridPrepFrame > 0 && --LaneDataDict[gate].GridPrepFrame <= 0)
                                {
                                    LaneDataDict[gate].GridMergeStage = 3;
                                    LaneDataDict[gate].GridPrepFrame = 300;
                                    //LaneDataDict[gate] = gateData;
                                    //TradeLaneSoundEmitter = SpawnSoundEmitter(MyAPIGateway.Session.Player.Character as IMyEntity, "ShipPrototechJumpDriveJumpIn", 1f);
                                    //SpawnSoundEmitter(gate, "ShipPrototechJumpDriveJumpIn", ref ShipSoundEmitter, 0.02f);
                                    PlayGateEffect(gate, grid, GateEffectKind.StageChange);
                                    OverrideThrusters(grid, Vector3D.Zero, true);
                                    InstructShipSystems(grid, CustomGridLogic.ShipSystem.TradeLaneMergeOnto);
                                    //TradeLaneSoundEmitter = SpawnSoundEffects(MyAPIGateway.Session.Player.Character as IMyEntity, "ShipJumpDriveCharging", 1f);
                                    //InstructShipSystems(grid, CustomGridLogic.ShipSystem.TradeLaneMergeOnto);
                                }
                            }
                            break;
                        case 3:
                            //return;
                            var gridLogic = grid.GameLogic.GetAs<CustomGridLogic>();
                            if (gridLogic == null || gridLogic.IsInTransit)
                                break;

                            // The ship takes the trip from here
                            var exitGate = LaneGates[LaneGates.Count - 1];
                            var arrivalBox = GateBoundingBox;
                            arrivalBox.Center = Vector3D.Transform(GateDummyLocalMatrix, exitGate.WorldMatrix);
                            gridLogic.EnterLane(exitGate.WorldMatrix, arrivalBox, SuperluminalSpeed);
                            /*
                            var pointOffset = WarpPoint(grid, gate.WorldMatrix.Forward, 0f);
                            ShipParticles = SpawnParticleEffectsOnto(grid as MyEntity, "WarpY", flippedMatrix, pointOffset);
                            pointOffset = WarpPoint(grid, gate.WorldMatrix.Backward, 0f);
                            PathPartciles = SpawnParticleEffectsOnto(grid as MyEntity, "MyWarpY", flippedMatrix, pointOffset);
                            */
                            OverrideThrusters(grid, Vector3D.Zero, true);

                            LaneDataDict[gate].GridInWaitingLine = null;
                            LaneDataDict[gate].GridMergeStage = 0;
                            //TradeLaneSoundEmitter = SpawnSoundEffects(MyAPIGateway.Session.Player.Character as IMyEntity, "TradeLane_Flight", 5f);
                            //TradeLaneSoundEmitter = SpawnSoundEffects(MyAPIGateway.Session.Player.Character as IMyEntity, "ShipPrototechJumpDriveJumpIn", 5f);
                            //TradeLaneSoundEmitter = SpawnSoundEmitter(MyAPIGateway.Session.Player.Character as IMyEntity, "ShipJumpDriveRecharge", 8f);
                            StopSoundEmitter(ShipSoundEmitter);
                            //SpawnSoundEmitter(gate, "ShipJumpDriveRecharge", ref ShipSoundEmitter, 8f);
                            //TradeLaneSoundEmitter = SpawnSoundEmitter(MyAPIGateway.Session.Player.Character as IMyEntity, "TradeLane_Flight", 2f);

                            //ShipParticles = SpawnParticleEffects(grid as MyEntity, "WarpY", MatrixD.Identity);
                            //PathPartciles = SpawnParticleEffects(grid as MyEntity, "MyWarpY", MatrixD.Identity);

                            //LaneDataDict[gate] = gateData;
                            /*
                            //SetGridDirection(firstGrid, firstGrid.WorldMatrix.Forward, 0.3);
                            //SetGridDirection(firstGrid, Vector3D.Transform(TradeLane2_Dummy.Matrix.Translation, Lane2.WorldMatrix), 0.8);
                            //var travelDirection = Vector3D.Normalize(Vector3D.Transform(TradeLane2_Dock_Dummy.Matrix.Translation, Lane2.WorldMatrix) - firstGrid.WorldMatrix.Translation);
                            var targetMatrix = MatrixD.CreateWorld(Vector3D.Transform(TargetLane2_Dummy.Matrix.Translation, TargetLane2.WorldMatrix));
                            var travelDirection = Vector3D.Normalize(targetMatrix.Translation - FirstGrid2.WorldMatrix.Translation);
                            distance = Vector3D.Distance(currentWorldMatrix.Translation, Vector3D.Transform(TargetLane2_Dummy.Matrix.Translation, TargetLane2.WorldMatrix));
                            if (distance > 25)
                            {
                                SetGridDirection(FirstGrid2, travelDirection, 0.1);
                                SetGridVelocity(FirstGrid2, (travelDirection * SuperluminalSpeed), true);
                            }
                            else
                                FirstGrid2 = null;
                            MyAPIGateway.Utilities.ShowNotification("dist: " + distance.ToString(), 16);
                            */
                            break;
                    }

                    if (LaneDataDict[gate].GridMergeStage > 0)
                    {
                        var travelDirection = Vector3D.Normalize(LaneGates[LaneGates.Count - 1].WorldMatrix.Translation - currentWorldMatrix.Translation);
                        //var DockWorldMatrix = gate.WorldMatrix;
                        SetGridAngle(grid, gate, adjustedTargetPosition, 0.5);
                        //EaseIntoPosition(grid, vesselWorldMatrix.Translation, adjustedTargetPosition, MathHelper.Clamp((1 * (distance/100)), 0.005f, 0.01f)); // Adjust easing factor as needed
                        EaseIntoPosition(grid, currentWorldMatrix.Translation, adjustedTargetPosition, MathHelper.Clamp((1 * (distance/100)), 0.005f, 0.01f)); // Adjust easing factor as needed

                        //MyAPIGateway.Utilities.ShowNotification($"Stage: {LaneDataDict[gate].GridMergeStage} | Distance: {MathHelper.RoundOn2((float)distance)}", 16);

                        // avoid jerky text text display
                        if (DistanceUpdateFrame > 0 && --DistanceUpdateFrame <= 0)
                        {
                            TargetDistanceDisplay = MathHelper.RoundOn2((float)distance);
                            DockStageDistanceDisplay = MathHelper.RoundOn2((float)distanceToDockStagePos);
                            DistanceUpdateFrame = DistanceUpdateInterval;
                        }

                        TradeLaneNetwork.Notify(grid, $"Docking Stage: {LaneDataDict[gate].GridMergeStage} ({DockStageDistanceDisplay}) | Distance: {TargetDistanceDisplay}", 16);
                    }
                }
            }
            else
            {
                if (gate == LaneGates[LaneGates.Count - 1])
                    return;

                //var grid = DetectBox(ref gateData.DetectBox);
                DetectBoundingBox.Center = Vector3D.Transform(DetectDummyLocalMatrix, gate.WorldMatrix);
                var dummyMatrix = MatrixD.CreateWorld(Vector3D.Transform(DetectDummyLocalMatrix, gate.WorldMatrix));
                var laneMatrix = gate.WorldMatrix;
                Quaternion.CreateFromRotationMatrix(ref laneMatrix, out DetectBoundingBox.Orientation);

                var grid = GetGridInTriggerBox(ref DetectBoundingBox, GridIgnore);
                /*
                if (GridIgnore.Contains(grid))
                    return;
                */

                /*
                if (grid == null)
                    return;

                if (grid.GameLogic != null)
                {
                    var gridLogic = grid.GameLogic.GetAs<CustomGridLogic>();
                    if (gridLogic != null)
                    {
                        if (gridLogic.Dock)
                        {

                        }
                    }
                }
                */

                /*
                if (grid != null)
                    MyAPIGateway.Utilities.ShowNotification("grid ok", 1000);
                */

                if (GridDockingRequest != null)
                {
                    //grid = GetDriftingGridTriggerBox(ref DetectBoundingBox, GridDockingRequest);
                }

                //GridDockingRequest = null;
                //@@@@@@

                if (grid != null && LaneDataDict[gate].GridInWaitingLine == null)
                {
                    if (GridDockingRequest != grid)
                    {
                        //MyAPIGateway.Utilities.ShowNotification("GridDockingRequest != grid", 1000);
                        //return;
                    }

                    if (grid.GameLogic.GetAs<CustomGridLogic>()?.IsInTransit == true)
                        return;

                    // Lane ends close together: the first computer docks it
                    if (IsDockingAnywhere(grid))
                        return;

                    TradeLaneNetwork.Notify(grid, "Trade Lane | Request Docking", sender: grid.DisplayName);
                    TargetDistanceDisplay = 0;
                    DockStageDistanceDisplay = 0;
                    DistanceUpdateFrame = DistanceUpdateInterval;

                    LaneDataDict[gate].GridInWaitingLine = grid;
                    LaneDataDict[gate].GridDockFrame = 300;
                    LaneDataDict[gate].GridMergeStage = 1;
                    //LaneDataDict[gate] = gateData;
                    //TradeLaneSoundEmitter = SpawnSoundEmitter(MyAPIGateway.Session.Player.Character as IMyEntity, "ShipJumpDriveCharging", 1f);
                    //SpawnSoundEmitter(MyAPIGateway.Session.Player.Character as IMyEntity, "BlockGravityGen", ref TradeLaneSoundEmitter, 5f);
                    //SpawnSoundEmitter(gate, "BlockSafeZone", ref ShipSoundEmitter, 1f);
                    PlayGateEffect(gate, grid, GateEffectKind.RequestDocking);
                    InstructShipSystems(LaneDataDict[gate].GridInWaitingLine, CustomGridLogic.ShipSystem.RequestDocking);
                    //ResetParticleEffects(GateParticles[gate]);
                    //StopSoundEmitter(TradeLaneSoundEmitter);
                    if (ShipParticles == null)
                    {
                        //NebulaParticleTest = SpawnParticleEffects(Block as MyEntity, "TradeLaneWarp", MatrixD.Identity);
                        //NebulaParticleTest = SpawnParticleEffects(grid as MyEntity, "TradeLaneWarp", MatrixD.Identity);
                    }
                }
                GridDockingRequest = null;
            }

            /*
            if (grid != null && !GridsMerging.Contains(grid) && !GridsInTransit.Contains(grid))
            {
                GridsMerging.Add(grid);
            }
            */
            //}
        }

        #endregion

        #region CORE FUNCTIONS

        private void DeleteEntities(ref List<MyEntity> entityList)
        {
            if (entityList == null)
                return;

            if (entityList.Count > 0)
            {
                foreach (var ent in entityList)
                {
                    if (ent == null)
                        continue;

                    MyEntities.Remove(ent);
                    ent.Close();
                }
                entityList.Clear();
            }
        }

        private string ReadCustomData(string data, string key, string separator = "=")
        {
            string result = "ERROR";
            if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(key))
                return result;
            string input = data;

            key = key.ToLower().Trim();

            string[] lines = input.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                /*
                if (line.Substring(0) == ";")
                    continue;
                */
                //if (key.ToLower().Contains("tlid"))
                    //MyAPIGateway.Utilities.ShowMessage("TL", $"ReadCustomData: {key} = {line}");

                //if (line.ToLower().Contains(key) && (line.Contains(":") || line.Contains("=")))
                if (line.ToLower().Contains(key) && line.Contains(separator))
                {
                    var value = line.Substring(line.LastIndexOf(separator) + 1);
                    if (!string.IsNullOrEmpty(value))
                        result = value.Trim();
                    break;
                }
            }

            return result;
        }

        public static void ChangeSafeZoneTexture(MySafeZone safeZone, string materialName)
        {
            if (safeZone == null || safeZone.Render == null)
                return;

            // Set the material for the safe zone
            //safeZone.Render.SetTexture(materialName);

            // Optionally, adjust the color or other visual properties
            //safeZone.Render.Color = Color.Blue; // Example: Change the color to blue
        }

        private void WriteCustomData(ref string data, string key, string value, string separator = "=")
        {
            //data = key.Trim() + separator.Trim() + value.Trim() + "\n\n" + data;
            data = key.Trim() + separator.Trim() + value.Trim() + "\n";
        }

        private void GateCountHandler(ref List<MyEntity> gates, ref List<Vector3D> points, int gatePart = 0)
        {
            // Create or delete entities (gates) for the amount of gate points as needed.
            if (gates.Count < points.Count)
            {
                int segmentsToAdd = points.Count - gates.Count;

                //for (int i = 0; i < points.Count; i++)
                for (int i = 0; i < segmentsToAdd; i++)
                {
                    MyEntity ent = null;

                    switch (gatePart)
                    {
                        case 0:
                            ent = CreateEntity(TradeLane_Model);

                            if (ent != null)
                            {
                                if (!GateParticles.ContainsKey(ent))
                                {
                                    //RingParticles = SpawnParticleEffectsOnto(ent, RingActivationAndIdleParticleName, MatrixD.Identity);
                                    if (MyAPIGateway.Utilities.IsDedicated == false)
                                        GateParticles[ent] = SpawnParticleEffectsOnto(ent, RingActivationAndIdleParticleName, MatrixD.Identity);
                                }
                                //GateParticles.Add(RingParticles);

                                //var sound = SpawnSoundEmitter(ent as IMyEntity, "ArcDroneLoopSmall", 2f);
                                //GateSounds[ent] = sound;
                            }

                            var dummies = GetDummiesByName(ent, LightDummyName);
                            //MyAPIGateway.Utilities.ShowNotification("dummies: " + dummies.Count.ToString(), 1000);
                            if (dummies.Count > 0)
                                CreateLights(ent, dummies);
                            //UpdateEmissive(ent, Color.CadetBlue, 5f);
                            UpdateEmissive(ent, Color.DeepSkyBlue, 10f);
                            UpdateEmissive(ent, Color.DodgerBlue, 10f, "Emissive0");
                            UpdateEmissive(ent, Color.OrangeRed, 10f, "Emissive1");
                            UpdateEmissive(ent, Color.Red, 10f, "Emissive3");
                            UpdateEmissive(ent, Color.Green, 10f, "Emissive4");

                            //var sound = SpawnSoundFX(ent, "ArcDroneLoopSmall", 2f);
                            //var sound2 = SpawnSound(ent, "ArcDroneLoopSmall", 2f);
                            //MyEntity3DSoundEmitter sound = null;
                            //SpawnSoundEmitter(ent, "ArcDroneLoopSmall", ref sound, 2f);
                            //GateSounds[ent] = sound;
                            break;
                        case 1:
                            ent = CreateEntity(TradeLane_Ring_Model);
                            //UpdateEmissive(ent, Color.CornflowerBlue, 5f);
                            UpdateEmissive(ent, Color.DodgerBlue, 10f);
                            //ParentEntity(ent1, ent);
                            break;
                        case 2:
                            ent = CreateEntity(TradeLane_Lights_Front_Model);
                            UpdateEmissive(ent, Color.Red, 10f);
                            //ParentEntity(ent2, ent);
                            break;
                        case 3:
                            ent = CreateEntity(TradeLane_Lights_Back_Model);
                            UpdateEmissive(ent, Color.Green, 10f);
                            //ParentEntity(ent3, ent);
                            break;
                    }

                    if (ent == null)
                        return;
                    gates.Add(ent);
                    //ent.WorldMatrix = MatrixD.CreateWorld(Block.WorldMatrix.Translation + (Block.WorldMatrix.Right * 160f), Block.WorldMatrix.Forward, Block. WorldMatrix.Up);
                    //ent.SetEmissiveParts("Emissive", Color.Orange, 1f);
                }
                GateUpdateFrame = 3;
            }
            else if (gates.Count > points.Count)
            {
                var ent = gates[gates.Count - 1];
                if (GateParticles.ContainsKey(ent))
                {
                    RemoveParticleEffects(GateParticles[ent]);
                    GateParticles.Remove(ent);
                }
                gates.RemoveAt(gates.Count - 1);
                MyEntities.Remove(ent);
                ent.Close();
                GateUpdateFrame = 3;
            }
        }

        void UpdateEmissive(MyEntity ent, Color color, float strength = 1f, string emissive = "Emissive")
        {
            //Color color = Color.Red;
            //float strength = 1f;
            ent.SetEmissiveParts(emissive, color, strength);
        }

        private MyEntity3DSoundEmitter SpawnSound(IMyEntity parent, string soundId, float volumeMult = 1)
        {
            MyEntity3DSoundEmitter sound = null;
            if (sound == null)
                sound = new MyEntity3DSoundEmitter(parent as MyEntity);
            MySoundPair soundPair = new MySoundPair(soundId);

            sound.VolumeMultiplier = volumeMult;

            sound.PlaySound(soundPair, true, true);

            /*
            //MyAPIGateway.Utilities.ShowNotification("particles3");
            MyVisualScriptLogicProvider.CreateSoundEmitterAtEntity(parent.EntityId.ToString(), "ConnectionSound");
            MyVisualScriptLogicProvider
            //MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("ParticleElectrical", worldPos);
            MyVisualScriptLogicProvider.StopSound("ParticleElectrical", true);
            */
            return sound;
        }

        private void CheckBlockCustomData()
        {
            return;
            if (Block.CustomData.Contains("TradeLaneRings:"))
            {
                //Block.CustomData = Block.CustomData.Replace("TradeLaneRings:", "");
                var count = int.Parse(Block.CustomData.Substring(Block.CustomData.LastIndexOf(':') + 1));
                if (count > 0)
                {
                    GateCount = count;
                }
            }
        }

        private void AddPointsBetweenGates(Vector3D startPosition, Vector3D endPosition, ref List<Vector3D> points, int segmentCount)
        {
            if (points == null)
                return;

            // Clear the existing points to avoid duplicates
            points.Clear();

            // Calculate the direction vector and the step size
            Vector3D direction = Vector3D.Normalize(endPosition - startPosition);
            double stepSize = Vector3D.Distance(startPosition, endPosition) / segmentCount;

            // Generate points along the line
            for (int i = 0; i <= segmentCount; i++)
            {
                Vector3D position = startPosition + (direction * (i * stepSize));
                points.Add(position);
            }
        }

        //private void AddPointsBetweenGates(IMyTerminalBlock block, IMyTerminalBlock targetBlock, double interval = 50.0)
        //private void AddPointsBetweenGates(MyEntity block, MyEntity targetBlock, ref List<Vector3D> points, double interval = 50.0)
        private void AddPointsBetweenGates(Vector3D startPosition, Vector3D endPosition, ref List<Vector3D> points, double interval = 50.0)
        {
            if (points == null)
                return;

            // Get the positions of the two blocks
            //Vector3D startPosition = block.WorldMatrix.Translation;
            //Vector3D endPosition = targetBlock.WorldMatrix.Translation;

            // Calculate the direction and distance
            Vector3D direction = Vector3D.Normalize(endPosition - startPosition);
            double totalDistance = Vector3D.Distance(startPosition, endPosition);

            // Place entities every 50 meters
            //double interval = 50.0;
            int entityCount = (int)(totalDistance / interval);

            if (entityCount == points.Count)
                return;

            for (int i = 1; i <= entityCount; i++)
            {
                // Calculate the position for the current entity
                Vector3D position = startPosition + (direction * (i * interval));

                points.Add(position);
                /*
                // Create and place the entity
                MyEntity entity = CreateEntity(entityModelPath);
                if (entity != null)
                {
                    entity.WorldMatrix = MatrixD.CreateWorld(position);
                    LaneGatesLocations.Add(position);
                    LaneGates.Add(entity);
                }
                */
            }
            //points[0] = startPosition;
            if (points.Count > 0)
            {
                points.Remove(points.First());
                points.Add(startPosition);
            }

            //points[points.Count - 1] = endPosition;
            if (points.Count > 0)
            {
                // Use LINQ to find the last element in the HashSet and replace it with the endPosition
                Vector3D lastPoint = points.Last();
                points.Remove(lastPoint);
                points.Add(endPosition);
            }
        }

        private void CreateOBB(MyEntity gate, IMyModelDummy dummy, out MyOrientedBoundingBoxD obb)
        {
            obb = new MyOrientedBoundingBoxD();

            if (dummy == null)
                return;

            var dummyMatrix = MatrixD.CreateWorld(Vector3D.Transform(dummy.Matrix.Translation, gate.WorldMatrix));
            var laneMatrix = gate.WorldMatrix;
            Quaternion.CreateFromRotationMatrix(ref laneMatrix, out obb.Orientation);
            obb.Center = dummyMatrix.Translation;
            obb.HalfExtent = dummy.Matrix.Scale / 2;
        }

        internal static List<IMyShipController> GetShipControllersFromGrid(IMyCubeGrid grid)
        {
            var shipControllers = new List<IMyShipController>();

            // Ensure the grid is valid  
            if (grid == null)
                return shipControllers;

            // Iterate through all blocks on the grid  
            var fatBlocks = grid.GetFatBlocks<IMyCubeBlock>();
            foreach (var block in fatBlocks)
            {
                IMyShipController controller = block as IMyShipController;
                // Check if the block is a ship controller
                if (controller != null)
                {
                    shipControllers.Add(controller);
                }
            }

            return shipControllers;
        }

        private IMyShipController GetMainOrFirstCockpit(IMyCubeGrid grid)
        {
            if (grid == null)
                return null;

            // Get all ship controllers on the grid
            var shipControllers = GetShipControllersFromGrid(grid);

            // Check for the main cockpit
            foreach (var controller in shipControllers)
            {
                if (controller.IsMainCockpit)
                {
                    return controller; // Return the main cockpit if found
                }
            }

            // Fallback: Return the first available controller
            return shipControllers.FirstOrDefault();
        }

        private IMyShipController GetActiveController(IMyCubeGrid grid)
        {
            if (grid == null)
                return null;

            // Get all ship controllers on the grid
            var shipControllers = GetShipControllersFromGrid(grid);

            // Check for a controller currently being controlled by a player or autopilot
            foreach (var controller in shipControllers)
            {
                //if (controller.IsUnderControl || controller.IsAutopilotEnabled)
                if (controller.IsUnderControl)
                {
                    return controller; // Return the active controller
                }
            }

            // Fallback: Return null if no active controller is found
            return null;
        }

        internal static IMyShipController GetController(IMyCubeGrid grid)
        {
            if (grid == null)
                return null;

            /*
            */
            if (grid.ControlSystem?.CurrentShipController != null)
            {
                var shipController = grid.ControlSystem.CurrentShipController as IMyShipController;
                if (shipController != null)
                {
                    return shipController;
                }
            }

            // Get all ship controllers on the grid
            var shipControllers = GetShipControllersFromGrid(grid);

            // Check for the main cockpit
            if (shipControllers.Count > 1)
            {
                foreach (var controller in shipControllers)
                {
                    if (controller.IsMainCockpit || controller.IsUnderControl || controller.IsAutopilotControlled)
                    {
                        return controller; // Return the main cockpit if found
                    }
                }
            }
            else if(shipControllers.Count == 1)
            {
                return shipControllers.FirstOrDefault();
            }

            /*
            // Check for a controller currently being controlled by a player or autopilot
            foreach (var controller in shipControllers)
            {
                //if (controller.IsUnderControl || controller.IsAutopilotEnabled)
                if (controller.IsUnderControl)
                {
                    return controller; // Return the active controller
                }
            }

            // Check for occupied cockpit
            foreach (var controller in shipControllers)
            {
                if (controller.Pilot != null)
                {
                    return controller; // Return the first available cockpit if no main cockpit is found
                }
            }
            */

            // Fallback: Return null if no active controller is found
            return null;
            // Fallback: Return the first available controller
            return shipControllers.FirstOrDefault();
        }


        private bool HasValidController(IMyCubeGrid grid)
        {
            if (grid == null)
                return false;

            /*
            */
            if (grid.ControlSystem?.CurrentShipController != null)
            {
                var shipController = grid.ControlSystem.CurrentShipController as IMyShipController;
                if (shipController != null)
                {
                    return true;
                }
            }

            // Get all ship controllers on the grid
            var shipControllers = GetShipControllersFromGrid(grid);

            // Check for the main cockpit
            if (shipControllers.Count > 1)
            {
                foreach (var controller in shipControllers)
                {
                    if (controller.IsMainCockpit || controller.IsUnderControl || controller.IsAutopilotControlled)
                    {
                        return true; // Return the main cockpit if found
                    }
                }
            }
            else if (shipControllers.Count == 1)
            {
                return true;
            }

            /*
            // Check for a controller currently being controlled by a player or autopilot
            foreach (var controller in shipControllers)
            {
                //if (controller.IsUnderControl || controller.IsAutopilotEnabled)
                if (controller.IsUnderControl)
                {
                    return controller; // Return the active controller
                }
            }

            // Check for occupied cockpit
            foreach (var controller in shipControllers)
            {
                if (controller.Pilot != null)
                {
                    return controller; // Return the first available cockpit if no main cockpit is found
                }
            }
            */

            return false;
        }


        private bool CheckController(IMyCubeGrid grid)
        {
            if (grid == null)
                return false;

            if (grid.ControlSystem?.CurrentShipController != null)
            {
                var shipController = grid.ControlSystem.CurrentShipController as IMyShipController;
                if (shipController != null)
                {
                    return true;
                }
            }

            // Get all ship controllers on the grid
            var shipControllers = GetShipControllersFromGrid(grid);

            // Check for the main cockpit
            if (shipControllers.Count > 1)
            {
                foreach (var controller in shipControllers)
                {
                    if (controller.IsMainCockpit)
                    {
                        return true; // Return the main cockpit if found
                    }
                }
            }
            else if (shipControllers.Count == 1)
            {
                return true;
            }

            return false;
        }

        private IMyModelDummy GetDummyByName(MyEntity ent, string dummyName)
        {
            dummyName = dummyName.Replace("dummy_", "");

            if (ent == null) return null;
            var model = ent.Model as IMyModel;
            if (model == null) return null;
            // Create a dictionary to store the dummies
            IDictionary<string, IMyModelDummy> dummies = new Dictionary<string, IMyModelDummy>();

            // Populate the dictionary with the dummies from the block's model
            model.GetDummies(dummies);

            // Check if the dummy with the specified name exists
            IMyModelDummy dummy;
            if (dummies.TryGetValue(dummyName, out dummy))
            {
                return dummy; // Return the dummy if found
            }

            return null; // Return null if the dummy is not found
        }

        private List<IMyModelDummy> GetDummiesByName(MyEntity ent, string dummyName)
        {
            dummyName = dummyName.Replace("dummy_", "");

            if (ent == null) return null;
            var model = ent.Model as IMyModel;
            if (model == null) return null;
            // Create a dictionary to store the dummies
            IDictionary<string, IMyModelDummy> dummies = new Dictionary<string, IMyModelDummy>();

            // Populate the dictionary with the dummies from the block's model
            model.GetDummies(dummies);

            List<IMyModelDummy> list = new List<IMyModelDummy>();

            foreach (var dummy in dummies)
            {
                if (dummy.Key.ToLower().Contains(dummyName))
                {
                    list.Add(dummy.Value);
                }
            }

            if (list.Count > 0)
            {
                return list; // Return the list of dummies if found
            }

            return null; // Return null if the dummy is not found
        }

        public bool GetDummies(MyEntity entity, ref IMyModelDummy detect, ref IMyModelDummy dock, ref IMyModelDummy prep, ref IMyModelDummy gate)
        {
            try
            {
                MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "Getting dummies...");

                var model = entity.Model as IMyModel;
                if (model == null)
                {
                    MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "No model found!");
                    return false;
                }

                Dictionary<string, IMyModelDummy> dummies = new Dictionary<string, IMyModelDummy>();

                model.GetDummies(dummies);
                if (dummies.Count == 0)
                {
                    MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "No dummies found!");
                    return false;
                }

                if (dummies.ContainsKey(TradeLane_DummyName))
                    gate = dummies[TradeLane_DummyName];

                if (dummies.ContainsKey(TradeLane_Detect_DummyName))
                    detect = dummies[TradeLane_Detect_DummyName];

                if (dummies.ContainsKey(TradeLane_Prep_DummyName))
                    prep = dummies[TradeLane_Prep_DummyName];

                if (dummies.ContainsKey(TradeLane_Dock_DummyName))
                    dock = dummies[TradeLane_Dock_DummyName];

                if (gate == null || detect == null || prep == null || dock == null)
                {
                    MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "Defect!");
                    if (gate == null)
                        MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "LANE");
                    if (detect == null)
                        MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "detect");
                    if (prep == null)
                        MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "prep");
                    if (dock == null)
                        MyAPIGateway.Utilities.ShowMessage("TradeLaneComputer", "dock");

                    return false;
                }

                return true;
            }
            catch (Exception e)
            {

                MyAPIGateway.Utilities.ShowMessage("BL.cs PrepSockets(): ", e.Message);
            }

            return true;
        }

        private MyEntity CreateEntity(string path)
        {
            try
            {
                var ent = new MyEntity();
                ent.Init(null, path, null, null, null);
                ent.Render.CastShadows = true;
                ent.IsPreview = true;
                ent.Save = false;
                ent.SyncFlag = false;
                ent.NeedsWorldMatrix = false;
                ent.Flags |= EntityFlags.IsNotGamePrunningStructureObject;
                //MatrixD scalingMatrix = MatrixD.CreateScale(RingScaleMult);
                //ent.WorldMatrix = scalingMatrix * ent.WorldMatrix;
                ent.PositionComp.Scale = RingScaleMult;
                ent.Render.Visible = true;
                MyEntities.Add(ent, true);

                /*
                if (ent != null)
                {
                    if (!GateParticles.ContainsKey(ent))
                    {
                        //RingParticles = SpawnParticleEffectsOnto(ent, RingActivationAndIdleParticleName, MatrixD.Identity);
                        GateParticles[ent] = SpawnParticleEffectsOnto(ent, RingActivationAndIdleParticleName, MatrixD.Identity);
                    }
                    //GateParticles.Add(RingParticles);

                    //var sound = SpawnSoundEmitter(ent as IMyEntity, "ArcDroneLoopSmall", 2f);
                    //GateSounds[ent] = sound;
                }
                */

                return ent;
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("CreateEntity()", e.Message);
            }
            return null;
        }

        private MyEntity CreateEntity(string path, MyEntity parent)
        {
            try
            {
                var ent = new MyEntity();
                ent.Init(null, path, parent, null, null);
                ent.Render.CastShadows = true; //Maybe true?
                ent.IsPreview = true;
                ent.Save = false;
                ent.SyncFlag = false;
                ent.NeedsWorldMatrix = false;
                ent.Flags |= EntityFlags.IsNotGamePrunningStructureObject;
                MyEntities.Add(ent, true);
                if (parent != null)
                {
                    /*
                    // Fix for CS0206: A non ref-returning property or indexer may not be used as an out or ref value
                    // The issue occurs because `parent.WorldMatrix` is a property and cannot be passed as a `ref` parameter.
                    // Instead, we can use a local variable to hold the value of `parent.WorldMatrix` and pass that variable by reference.

                    MatrixD parentWorldMatrix = parent.WorldMatrix; // Store the value of the parent's WorldMatrix in a local variable.

                    // Fix for CS1503: Argument 1: cannot convert from 'ref VRageMath.MatrixD' to 'ref VRageMath.Matrix'  
                    // The issue occurs because the method `SetLocalMatrix` expects a `ref Matrix` parameter, but `parentWorldMatrix` is of type `MatrixD`.  
                    // To fix this, we need to convert `MatrixD` to `Matrix` before passing it to the method.  

                    Matrix parentWorldMatrixAsMatrix = (Matrix)parentWorldMatrix; // Convert MatrixD to Matrix  
                    ent.PositionComp.SetLocalMatrix(ref parentWorldMatrixAsMatrix); // Pass the converted variable by reference  

                    MatrixD parentWorldMatrixForSetWorld = parent.WorldMatrix; // Store the value of the parent's WorldMatrix in another local variable.
                    ent.PositionComp.SetWorldMatrix(ref parentWorldMatrixForSetWorld); // Pass the local variable by reference.

                    // Fix for CS0029: Cannot implicitly convert type 'VRage.Game.Entity.MyEntity' to 'VRage.Game.Components.MyHierarchyComponentBase'
                    // The issue occurs because `ent.Hierarchy.Parent` expects a `MyHierarchyComponentBase` type, but `parent` is of type `MyEntity`.
                    // To fix this, we need to access the `Hierarchy` property of `parent` and assign it to `ent.Hierarchy.Parent`.
                    */

                    ent.Hierarchy.Parent = parent.Hierarchy; // Assign the Hierarchy component of the parent entity.
                }
                return ent;
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("CreateEntity()", e.Message);
            }
            return null;
        }

        private void ParentEntity(MyEntity child, MyEntity parent)
        {
            if (child == null || parent == null)
            {
                //MyAPIGateway.Utilities.ShowMessage("Error", "Child or Parent entity is null.");
                return;
            }

            // Convert MatrixD to Matrix for compatibility with SetLocalMatrix  
            Matrix localMatrix = (Matrix)child.WorldMatrix * Matrix.Invert((Matrix)parent.WorldMatrix);

            // Set the child's local matrix to maintain its position and orientation relative to the parent  
            child.PositionComp.SetLocalMatrix(ref localMatrix, parent.PositionComp.WorldMatrixRef);

            //MyAPIGateway.Utilities.ShowMessage("Parenting", $"Entity {child.DisplayName} is now parented to {parent.DisplayName}.");
        }

        private IMyRadioAntenna GetAntenna(IMyCubeGrid grid, IMyRadioAntenna antenna1 = null, IMyRadioAntenna antenna2 = null)
        {
            try
            {
                var antennas = new List<IMyRadioAntenna>();

                if (grid == null)
                    return null;

                var fatBlocks = grid.GetFatBlocks<IMyCubeBlock>();

                if (!fatBlocks.Any())
                    return null;

                foreach (var block in fatBlocks)
                {
                    IMyRadioAntenna antenna = block as IMyRadioAntenna;
                    if (antenna != null)
                    {
                        if (antenna1 != null && antenna2 != null)
                        {
                            if (antenna != antenna1 && antenna != antenna2)
                                antennas.Add(antenna);
                        }
                        else
                            antennas.Add(antenna);
                    }
                }

                return antennas.FirstOrDefault();
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES GetAntenna", e.Message);
            }
            return null;
        }

        private IMyRadioAntenna GetAntennaInDirection(IMyCubeGrid grid, IMyTerminalBlock referenceBlock, Vector3D direction, double maxDistance = 100.0)
        {
            try
            {
                if (grid == null || referenceBlock == null)
                    return null;

                var antennas = new List<IMyRadioAntenna>();

                // Normalize the direction vector
                direction = Vector3D.Normalize(direction);

                // Iterate through all blocks on the grid
                var fatBlocks = grid.GetFatBlocks<IMyCubeBlock>();

                if (!fatBlocks.Any())
                    return null;

                foreach (var block in fatBlocks)
                {
                    IMyRadioAntenna antenna = block as IMyRadioAntenna;
                    if (antenna != null)
                    {
                        // Calculate the vector from the reference block to the antenna
                        Vector3D antennaPosition = antenna.WorldMatrix.Translation;
                        Vector3D referencePosition = referenceBlock.WorldMatrix.Translation;
                        Vector3D toAntenna = Vector3D.Normalize(antennaPosition - referencePosition);

                        // Check if the antenna is in the specified direction and within the max distance
                        if (Vector3D.Dot(toAntenna, direction) > 0.99 && Vector3D.Distance(referencePosition, antennaPosition) <= maxDistance)
                        {
                            antennas.Add(antenna);
                        }
                    }
                }

                // Return the first antenna that meets the criteria
                return antennas.FirstOrDefault();
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("TRADE LANES GetAntennaInDirection", e.Message);
            }
            return null;
        }

        #endregion

        #region HELPERS

        private double GetSpeed(MyCubeGrid grid)
        {
            Vector3D currentPosition = grid.WorldMatrix.Translation;
            double currentTime = MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds;

            // Calculate time elapsed
            double timeElapsed = currentTime - _lastUpdateTime;

            // Calculate speed
            double speed = CalculateSpeed(currentPosition, _previousPosition, timeElapsed);

            // Debug: Display the speed
            //MyAPIGateway.Utilities.ShowNotification($"Speed: {speed:F2} m/s", 16);

            // Update previous position and time
            _previousPosition = currentPosition;
            _lastUpdateTime = currentTime;

            return speed;
        }

        private double CalculateSpeed(Vector3D currentPosition, Vector3D previousPosition, double timeElapsed)
        {
            if (timeElapsed <= 0)
                return 0;

            // Calculate the distance traveled
            double distanceTraveled = Vector3D.Distance(currentPosition, previousPosition);

            // Calculate the speed (distance / time)
            return distanceTraveled / timeElapsed;
        }

        private Vector3D GridsBoundingBoxCenterWorldPosition(MyCubeGrid grid)
        {
            if (grid == null)
                return Vector3D.Zero;

            // Get the grid's bounding box
            BoundingBoxD gridBoundingBox = grid.PositionComp.WorldAABB;

            // Calculate the offset between the grid's bounding box center and its current position
            Vector3D boundingBoxCenter = gridBoundingBox.Center;
            Vector3D offset = grid.WorldMatrix.Translation - boundingBoxCenter;

            // Adjust the world position for the particle effect
            Vector3D worldPos = grid.WorldMatrix.Translation + offset;

            return boundingBoxCenter;
        }

        private Vector3D WarpPointXX(MyCubeGrid grid, double offset = 0)
        {
            if (grid == null || grid.Physics == null)
                return Vector3D.Zero;

            // Get the grid's bounding box center in world coordinates
            BoundingBoxD gridBoundingBox = grid.PositionComp.WorldAABB;
            Vector3D boundingBoxCenter = gridBoundingBox.Center;

            // Get the grid's velocity and calculate the forward direction
            Vector3D velocity = grid.Physics.LinearVelocity;
            Vector3D forwardDirection = Vector3D.Normalize(velocity);

            // Add the offset in the forward direction
            Vector3D warpPoint = boundingBoxCenter + (forwardDirection * offset);

            return warpPoint;
        }

        private Vector3D WarpPoint(MyCubeGrid grid, double offset = 0)
        {
            if (grid == null || grid.Physics == null)
                return Vector3D.Zero;

            // Get the grid's bounding box in world coordinates
            BoundingBoxD gridBoundingBox = grid.PositionComp.WorldAABB;

            // Get the grid's velocity and calculate the forward direction
            Vector3D velocity = grid.Physics.LinearVelocity;
            if (velocity.LengthSquared() == 0)
                return gridBoundingBox.Center; // If no velocity, return the center of the bounding box

            Vector3D forwardDirection = Vector3D.Normalize(velocity);

            // Calculate the start of the bounding box in the direction of the velocity
            Vector3D warpPoint = gridBoundingBox.Center - (forwardDirection * gridBoundingBox.HalfExtents.Length()) + (forwardDirection * offset);

            return warpPoint;
        }

        private Vector3D WarpPointFF(MyCubeGrid grid, Vector3D direction, double offset = 0)
        {
            if (grid == null || grid.Physics == null)
                return Vector3D.Zero;

            // Get the grid's bounding box in world coordinates
            BoundingBoxD gridBoundingBox = grid.PositionComp.WorldAABB;

            // Get the grid's velocity and calculate the forward direction
            Vector3D velocity = grid.Physics.LinearVelocity;
            if (velocity.LengthSquared() == 0)
                return gridBoundingBox.Center; // If no velocity, return the center of the bounding box

            Vector3D forwardDirection = direction;

            // Calculate the start of the bounding box in the direction of the velocity
            Vector3D warpPoint = gridBoundingBox.Center - (forwardDirection * gridBoundingBox.HalfExtents.Length()) + (forwardDirection * offset);

            return warpPoint;
        }

        private Vector3D WarpPoint(MyCubeGrid grid, Vector3D direction, double offset = 0)
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

            // Calculate the warp point at the start of the bounding box in the supplied direction
            Vector3D warpPoint = gridBoundingBox.Center - (normalizedDirection * halfExtentInDirection) + (normalizedDirection * offset);

            return warpPoint;
        }

        #endregion

        #region PARTICLE EFFECTS
        
        public MyParticleEffect SpawnParticleEffects(MyCubeGrid grid, string subtypeId, MatrixD localMatrix, uint parentId = uint.MaxValue)
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

        public MyParticleEffect SpawnParticleEffectsOnto(MyEntity parent, string subtypeId, MatrixD localMatrix)
        {
            if (DisableParticleEffects)
                return null;

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
                return effect;
            //effect.UserScale = 0.1f;
            return effect;
        }

        public MyParticleEffect SpawnParticleEffects(MyEntity parent, string subtypeId, MatrixD localMatrix, Vector3D worldTranslation)
        {
            if (DisableParticleEffects)
                return null;

            //MyAPIGateway.Utilities.ShowNotification("TL: Spawn Particles 2");
            //MatrixD worldMatrix = localMatrix * parent.WorldMatrix;
            MyParticleEffect effect;
            //Vector3D worldPos = parent.GetPosition();
            Vector3D worldPos = worldTranslation;
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
                return effect;
            //effect.UserScale = 0.1f;
            return effect;
        }

        public MyParticleEffect SpawnParticleEffectsOnto(MyEntity parent, string subtypeId, MatrixD localMatrix, Vector3D offset)
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

        public MyParticleEffect SpawnParticleEffects(MyEntity parent, string subtypeId, MatrixD localMatrix)
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

        public void StopParticleEffects(MyParticleEffect effect)
        {
            if (effect != null)
            {
                effect.StopEmitting();
            }
        }

        public void PauseParticleEffects(MyParticleEffect effect)
        {
            if (effect != null)
            {
                effect.Pause();
            }
        }

        public void PlayParticleEffects(MyParticleEffect effect)
        {
            if (effect != null)
            {
                effect.Play();
                //effect.Update();
            }
        }

        public void ResumeParticleEffects(MyParticleEffect effect)
        {
            if (effect != null)
            {
                if (effect.IsEmittingStopped)
                    effect.Play();
                //effect.Update();
            }
        }

        public void ToggleParticleEffects(MyParticleEffect effect)
        {
            if (effect != null)
            {
                if (effect.IsEmittingStopped)
                    effect.Play();
                else
                    effect.StopEmitting();

                //effect.Update();
            }
        }

        public void ResetParticleEffects(MyParticleEffect effect)
        {
            return;
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

        public void RemoveParticleEffects(MyParticleEffect effect, bool instant = false)
        {
            if (effect != null)
            {
                //MyAPIGateway.Utilities.ShowMessage("TL", "Remove Particle Effects");
                MyParticlesManager.RemoveParticleEffect(effect);
                effect.Stop(instant);
                effect.Close();
            }
        }

        #endregion

        #region SOUND EMITTERS

        public MyEntity3DSoundEmitter SpawnSoundFX(MyEntity parent, string soundId, float volume = 1, float distance = 500)
        {
            if (DisableSoundEffects)
                return null;
            //MyEntity3DSoundEmitter emitter = null;
            var emitter = new MyEntity3DSoundEmitter(parent as MyEntity);
            //emitter = new MyEntity3DSoundEmitter(null);
            MySoundPair sound = new MySoundPair(soundId);
            emitter.VolumeMultiplier = volume;
            emitter.CustomMaxDistance = distance;
            // Should be spawned at entities center so....
            //emitter.SetPosition(position: parent.GetPosition());
            //soundEmitter.PlaySound(sound);
            emitter.PlaySound(sound, skipIntro: true, force3D: true, forcePlaySound: true);
            emitter.Update();
            return emitter;
        }

        public void SpawnSoundEmitter(MyEntity parent, string soundId, ref MyEntity3DSoundEmitter emitter, float volume = 1)
        {
            //MyEntity3DSoundEmitter soundEmitter = null;
            //soundEmitter = new MyEntity3DSoundEmitter(parent as MyEntity);
            //MySoundPair soundPair = new MySoundPair(soundId);
            //soundEmitter.VolumeMultiplier = volume;
            //soundEmitter.PlaySound(soundPair);

            if (emitter == null)
                emitter = new MyEntity3DSoundEmitter(parent);

            MySoundPair soundPair = new MySoundPair(soundId);
            emitter.VolumeMultiplier = volume;
            emitter.PlaySound(soundPair);
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
            emitter.Cleanup();
            emitter = null;
        }

        public void RemoveSoundEmitter(MyEntity3DSoundEmitter emitter)
        {
            if (emitter == null)
                return;
            emitter.StopSound(true);
            emitter.Cleanup();
            emitter = null;
        }

        #endregion

        #region TRIGGER BOX

        internal static bool CheckIfGridIsInTriggerBox(ref MyOrientedBoundingBoxD box, MyCubeGrid grid)
        {
            List<MyEntity> intersectingEntities = new List<MyEntity>();

            MyGamePruningStructure.GetAllEntitiesInOBB(ref box, intersectingEntities);

            //return intersectingEntities.OfType<MyCubeGrid>().FirstOrDefault();

            var list = intersectingEntities.OfType<MyCubeGrid>();

            foreach (var g in list)
            {
                //var grid = entity as MyCubeGrid;

                if (g != null && g == grid)
                {
                    //grid.Physics.LinearVelocity = Vector3.Zero;
                    return true;
                }
            }

            return false;
        }

        // A node's stand-in: a ring with a ship in its waiting line every frame, a free one
        // every StandInDetectFrames, the rings in turn
        bool StandInDue(MyEntity gate, int index)
        {
            LaneData data;
            return (LaneDataDict.TryGetValue(gate, out data) && data.GridInWaitingLine != null)
                || (StandInFrame + index) % StandInDetectFrames == 0;
        }

        // Server: true if a lane computer, or a node's stand-in, has this grid in its waiting line
        static bool IsDockingAnywhere(MyCubeGrid grid)
        {
            foreach (var block in TradeLanes)
            {
                var logic = block.GameLogic?.GetAs<TradeLaneComputerBlockLogic>();
                if (logic != null && logic.IsDocking(grid))
                    return true;
            }
            foreach (var lane in LaneRegistry.StandIns)
                if (lane.IsDocking(grid))
                    return true;
            return false;
        }

        bool IsDocking(MyCubeGrid grid)
        {
            foreach (var data in LaneDataDict.Values)
                if (data.GridInWaitingLine == grid)
                    return true;
            return false;
        }

        private MyCubeGrid GetGridInTriggerBox(ref MyOrientedBoundingBoxD box, List<MyCubeGrid> ignoreList)
        {
            List<MyEntity> intersectingEntities = new List<MyEntity>();

            MyGamePruningStructure.GetAllEntitiesInOBB(ref box, intersectingEntities);

            //return intersectingEntities.OfType<MyCubeGrid>().FirstOrDefault();

            var list = intersectingEntities.OfType<MyCubeGrid>();

            foreach (var grid in list)
            {
                //var grid = entity as MyCubeGrid;

                //if (grid != null && !grid.DampenersEnabled && CheckController(grid))
                if (grid != null && !grid.DampenersEnabled && HasValidController(grid))
                {
                    if (ignoreList.Contains(grid))
                        continue;

                    //grid.Physics.LinearVelocity = Vector3.Zero;

                    return grid;
                }
            }

            return null;
        }

        private MyCubeGrid GetGridInTriggerBox(ref MyOrientedBoundingBoxD box)
        {
            List<MyEntity> intersectingEntities = new List<MyEntity>();

            MyGamePruningStructure.GetAllEntitiesInOBB(ref box, intersectingEntities);

            //return intersectingEntities.OfType<MyCubeGrid>().FirstOrDefault();

            var list = intersectingEntities.OfType<MyCubeGrid>();

            foreach (var grid in list)
            {
                //var grid = entity as MyCubeGrid;

                if (grid != null && !grid.DampenersEnabled && CheckController(grid))
                {
                    //grid.Physics.LinearVelocity = Vector3.Zero;
                    return grid;
                }
            }

            return null;
        }

        private bool IsGridIntersectingDummy(IMyCubeGrid grid, IMyModelDummy dummy, MatrixD parentWorldMatrix)
        {
            if (grid == null || dummy == null)
                return false;

            // Define the size of the bounding box around the dummy
            Vector3D boundingBoxSize = new Vector3D(2.0, 2.0, 2.0); // Adjust size as needed

            // Get the dummy's world position
            Vector3D dummyWorldPosition = Vector3D.Transform(dummy.Matrix.Translation, parentWorldMatrix);

            // Create a bounding box around the dummy's world position
            BoundingBoxD dummyBoundingBox = new BoundingBoxD(
                dummyWorldPosition - boundingBoxSize * 0.5,
                dummyWorldPosition + boundingBoxSize * 0.5
            );

            // Get the grid's bounding box in world space
            BoundingBoxD gridBoundingBox = grid.WorldAABB;

            // Check for intersection
            return dummyBoundingBox.Intersects(gridBoundingBox);
        }

        private bool CheckTriggerBox(IMyModelDummy dummy, MyEntity lane, ref MyOrientedBoundingBoxD box, MyCubeGrid grid)
        {
            var dummyMatrix = MatrixD.CreateWorld(Vector3D.Transform(dummy.Matrix.Translation, lane.WorldMatrix));
            var laneMatrix = lane.WorldMatrix;

            Quaternion.CreateFromRotationMatrix(ref laneMatrix, out box.Orientation);

            box.Center = dummyMatrix.Translation;
            //TradeLane1_DummyBox.HalfExtent = new Vector3D(11, 11, 11);
            box.HalfExtent = dummy.Matrix.Scale / 2;

            //TradeLane1_DummyBox.HalfExtent.X = 55;
            //TradeLane1_DummyBox.HalfExtent.Y = 55;
            //TradeLane1_DummyBox.HalfExtent.Z = 55;

            Vector4 color = Color.Yellow.ToVector4() * 12;
            DrawOBB(box, color);

            List<MyEntity> intersectingEntities = new List<MyEntity>();

            MyGamePruningStructure.GetAllEntitiesInOBB(ref box, intersectingEntities);

            //return intersectingEntities.OfType<MyCubeGrid>().FirstOrDefault();

            foreach (var entity in intersectingEntities)
            {
                var g = entity as MyCubeGrid;

                if (g != null && !g.DampenersEnabled && g == grid)
                {
                    //grid.Physics.LinearVelocity = Vector3.Zero;
                    return true;
                }
            }

            return false;
        }

        private MyCubeGrid GetDriftingGridTriggerBox(ref MyOrientedBoundingBoxD box, MyCubeGrid target)
        {
            List<MyEntity> intersectingEntities = new List<MyEntity>();

            MyGamePruningStructure.GetAllEntitiesInOBB(ref box, intersectingEntities);

            //return intersectingEntities.OfType<MyCubeGrid>().FirstOrDefault();

            var list = intersectingEntities.OfType<MyCubeGrid>();

            foreach (var grid in list)
            {
                //var grid = entity as MyCubeGrid;

                if (grid != null && grid == target && CheckController(grid))
                {
                    //grid.Physics.LinearVelocity = Vector3.Zero;
                    return grid;
                }
            }

            return null;
        }

        private MyCubeGrid TriggerBox(IMyModelDummy dummy, MyEntity lane, ref MyOrientedBoundingBoxD box, MyCubeGrid grid)
        {
            var dummyMatrix = MatrixD.CreateWorld(Vector3D.Transform(dummy.Matrix.Translation, lane.WorldMatrix));
            var laneMatrix = lane.WorldMatrix;

            Quaternion.CreateFromRotationMatrix(ref laneMatrix, out box.Orientation);

            box.Center = dummyMatrix.Translation;
            //TradeLane1_DummyBox.HalfExtent = new Vector3D(11, 11, 11);
            box.HalfExtent = dummy.Matrix.Scale / 2;

            //TradeLane1_DummyBox.HalfExtent.X = 55;
            //TradeLane1_DummyBox.HalfExtent.Y = 55;
            //TradeLane1_DummyBox.HalfExtent.Z = 55;

            Vector4 color = Color.Yellow.ToVector4() * 12;
            DrawOBB(box, color);

            List<MyEntity> intersectingEntities = new List<MyEntity>();

            MyGamePruningStructure.GetAllEntitiesInOBB(ref box, intersectingEntities);

            //return intersectingEntities.OfType<MyCubeGrid>().FirstOrDefault();

            foreach (var entity in intersectingEntities)
            {
                var g = entity as MyCubeGrid;

                if (g != null && !g.DampenersEnabled && g != grid)
                {
                    //grid.Physics.LinearVelocity = Vector3.Zero;
                    return g;
                }
            }

            return null;
        }

        private MyCubeGrid TriggerBox(IMyModelDummy dummy, MyEntity lane, ref MyOrientedBoundingBoxD box)
        {
            var dummyMatrix = MatrixD.CreateWorld(Vector3D.Transform(dummy.Matrix.Translation, lane.WorldMatrix));
            var laneMatrix = lane.WorldMatrix;

            Quaternion.CreateFromRotationMatrix(ref laneMatrix, out box.Orientation);

            box.Center = dummyMatrix.Translation;
            //TradeLane1_DummyBox.HalfExtent = new Vector3D(11, 11, 11);
            box.HalfExtent = dummy.Matrix.Scale / 2;

            //TradeLane1_DummyBox.HalfExtent.X = 55;
            //TradeLane1_DummyBox.HalfExtent.Y = 55;
            //TradeLane1_DummyBox.HalfExtent.Z = 55;

            Vector4 color = Color.Yellow.ToVector4() * 12;
            DrawOBB(box, color);

            List<MyEntity> intersectingEntities = new List<MyEntity>();

            MyGamePruningStructure.GetAllEntitiesInOBB(ref box, intersectingEntities);

            //return intersectingEntities.OfType<MyCubeGrid>().FirstOrDefault();

            foreach (var entity in intersectingEntities)
            {
                var grid = entity as MyCubeGrid;

                if (grid != null && !grid.DampenersEnabled)
                {
                    //grid.Physics.LinearVelocity = Vector3.Zero;
                    return grid;
                }
            }

            return null;
        }

        #endregion

        #region GAME WORLD INTERACTIONS

        private void RotateGridTowardsTarget(IMyTerminalBlock block, IMyTerminalBlock targetBlock, bool inheritOrientation = false, double easingFactor = 0.1)
        {
            if (block == null)
            {
                MyAPIGateway.Utilities.ShowNotification("block is null", 16);
                return;
            }
            if (targetBlock == null)
            {
                MyAPIGateway.Utilities.ShowNotification("targetBlock is null", 16);
                return;
            }
            MyCubeGrid grid = block.CubeGrid as MyCubeGrid;
            Vector3D targetPosition = targetBlock.WorldMatrix.Translation;
            if (grid == null)
            {
                MyAPIGateway.Utilities.ShowNotification("Grid is null", 16);
                return;
            }

            // Get the grid's current position
            Vector3D gridPosition = grid.WorldMatrix.Translation;

            // Calculate the direction from the target to the grid (for the back to face the target)
            Vector3D targetToGridDirection = Vector3D.Normalize(gridPosition - targetPosition);

            // Get the target's up direction (assume the target is another block or entity)
            Vector3D targetUpDirection = TargetBlock?.WorldMatrix.Up ?? Vector3D.Up; // Default to world up if no target block

            // If inheritOrientation is true, align the back and up directions
            Vector3D forwardDirection = inheritOrientation ? -targetToGridDirection : targetToGridDirection;
            Vector3D upDirection = inheritOrientation ? targetUpDirection : grid.WorldMatrix.Up;

            // Calculate the right direction using the cross product
            Vector3D rightDirection = Vector3D.Cross(forwardDirection, upDirection);
            rightDirection = Vector3D.Normalize(rightDirection);

            // Recalculate the up direction to ensure orthogonality
            upDirection = Vector3D.Cross(rightDirection, forwardDirection);

            // Create the new rotation matrix
            MatrixD rotationMatrix = MatrixD.Identity;
            rotationMatrix.Forward = forwardDirection;
            rotationMatrix.Right = rightDirection;
            rotationMatrix.Up = upDirection;

            // Preserve the grid's current position
            rotationMatrix.Translation = gridPosition;

            // Apply the new rotation matrix to the grid
            grid.WorldMatrix = rotationMatrix;
        }

        private void RotateGridTowardsTargetByBlockOrientationRefence(IMyTerminalBlock block, Vector3D targetBlockPosition, Vector3D targetGridPosition, Vector3D targetGridUp, bool inheritOrientation = false)
        {
            if (block == null)
            {
                MyAPIGateway.Utilities.ShowNotification("Block is null", 16);
                return;
            }

            MyCubeGrid blockGrid = block.CubeGrid as MyCubeGrid;

            if (blockGrid == null)
            {
                MyAPIGateway.Utilities.ShowNotification("BlockGrid is null", 16);
                return;
            }

            if (inheritOrientation)
            {
                /*
                // Get the block's world orientation
                MatrixD blockWorldMatrix = block.LocalMatrix * blockGrid.WorldMatrix;

                // Get the targetBlock's world orientation
                MatrixD targetBlockWorldMatrix = targetBlock.LocalMatrix * targetGrid.WorldMatrix;

                // Calculate the rotation matrix to align block with targetBlock
                MatrixD rotationMatrix = targetBlockWorldMatrix.GetOrientation() * MatrixD.Transpose(blockWorldMatrix.GetOrientation());

                // Get the grid's current world matrix
                MatrixD gridWorldMatrix = blockGrid.WorldMatrix;

                // Apply the rotation to the grid
                MatrixD newGridWorldMatrix = rotationMatrix * gridWorldMatrix;

                // Preserve the grid's position
                newGridWorldMatrix.Translation = gridWorldMatrix.Translation;

                // Set the new world matrix for the grid
                blockGrid.WorldMatrix = newGridWorldMatrix;
                */
                /*
                // Get the targetBlock's grid world orientation
                MatrixD targetGridWorldMatrix = targetGrid.WorldMatrix;

                // Get the block's grid current world matrix
                MatrixD blockGridWorldMatrix = blockGrid.WorldMatrix;

                // Copy the orientation (rotation part) from the target grid to the block's grid
                blockGridWorldMatrix.Forward = targetGridWorldMatrix.Forward;
                blockGridWorldMatrix.Up = targetGridWorldMatrix.Up;
                blockGridWorldMatrix.Right = targetGridWorldMatrix.Right;

                // Preserve the block grid's current position
                blockGridWorldMatrix.Translation = blockGrid.WorldMatrix.Translation;

                // Apply the new orientation to the block's grid
                blockGrid.WorldMatrix = blockGridWorldMatrix;
                */


                // Get the block's grid current world matrix
                MatrixD blockGridWorldMatrix = blockGrid.WorldMatrix;

                // Calculate the direction vector from the block's grid to the targetBlock
                Vector3D directionToTarget = Vector3D.Normalize(targetGridPosition - blockGridWorldMatrix.Translation);

                // Set the Forward direction of the block's grid to face the targetBlock
                blockGridWorldMatrix.Forward = directionToTarget;

                // Align the Up direction of the block's grid with the targetBlock's Up direction
                blockGridWorldMatrix.Up = targetGridUp;

                // Recalculate the Right direction to ensure orthogonality
                blockGridWorldMatrix.Right = Vector3D.Cross(blockGridWorldMatrix.Forward, blockGridWorldMatrix.Up);
                blockGridWorldMatrix.Right = Vector3D.Normalize(blockGridWorldMatrix.Right);

                // Recalculate the Up direction to ensure orthogonality
                blockGridWorldMatrix.Up = Vector3D.Cross(blockGridWorldMatrix.Right, blockGridWorldMatrix.Forward);

                // Preserve the block grid's current position
                blockGridWorldMatrix.Translation = blockGrid.WorldMatrix.Translation;

                // Apply the new orientation to the block's grid
                blockGrid.WorldMatrix = blockGridWorldMatrix;
            }
            else
            {
                MyCubeGrid grid = block.CubeGrid as MyCubeGrid;
                var targetPosition = targetBlockPosition;

                // Get the grid's current position
                Vector3D gridPosition = grid.WorldMatrix.Translation;

                // Calculate the direction from the target to the grid (for the back to face the target)
                Vector3D targetToGridDirection = Vector3D.Normalize(gridPosition - targetPosition);

                // Get the target's up direction (assume the target is another block or entity)
                Vector3D targetUpDirection = targetGridUp;

                // If inheritOrientation is true, align the back and up directions
                Vector3D forwardDirection = inheritOrientation ? targetToGridDirection : -targetToGridDirection;
                Vector3D upDirection = inheritOrientation ? targetUpDirection : grid.WorldMatrix.Up;

                // Calculate the right direction using the cross product
                Vector3D rightDirection = Vector3D.Cross(forwardDirection, upDirection);
                rightDirection = Vector3D.Normalize(rightDirection);

                // Recalculate the up direction to ensure orthogonality
                upDirection = Vector3D.Cross(rightDirection, forwardDirection);

                // Create the new rotation matrix
                MatrixD rotationMatrix = MatrixD.Identity;
                rotationMatrix.Forward = forwardDirection;
                rotationMatrix.Right = rightDirection;
                rotationMatrix.Up = upDirection;

                // Preserve the grid's current position
                rotationMatrix.Translation = gridPosition;

                // Apply the new rotation matrix to the grid
                grid.WorldMatrix = rotationMatrix;
            }
        }

        private void SetGridDirection(MyCubeGrid grid, Vector3D direction)
        {
            if (grid == null)
                return;

            // Normalize the direction vector
            Vector3D forwardDirection = Vector3D.Normalize(direction);

            // Get the grid's current up direction
            Vector3D upDirection = grid.WorldMatrix.Up;

            // Calculate the right direction using the cross product
            Vector3D rightDirection = Vector3D.Cross(forwardDirection, upDirection);
            rightDirection = Vector3D.Normalize(rightDirection);

            // Recalculate the up direction to ensure orthogonality
            upDirection = Vector3D.Cross(rightDirection, forwardDirection);

            // Create the new rotation matrix
            MatrixD newWorldMatrix = grid.WorldMatrix;
            newWorldMatrix.Forward = forwardDirection;
            newWorldMatrix.Right = rightDirection;
            newWorldMatrix.Up = upDirection;

            // Preserve the grid's current position
            newWorldMatrix.Translation = grid.WorldMatrix.Translation;

            // Apply the new rotation matrix to the grid
            //grid.Teleport(newWorldMatrix);
            grid.WorldMatrix = newWorldMatrix;
        }

        internal static void SetGridDirectionzzz(MyCubeGrid grid, MatrixD targetMatrix, float easingFactor = 0.1f)
        {
            if (grid == null || grid.Physics == null || !grid.Physics.Enabled)
                return;

            var controller = GetController(grid as IMyCubeGrid);
            if (controller == null)
                return;

            // Normalize the target direction
            //targetDirection = Vector3D.Normalize(targetDirection);
            var targetDirection = targetMatrix.Forward;

            // Get the current forward direction from the controller
            Vector3D currentForward = controller.WorldMatrix.Forward;

            // Calculate the axis of rotation (cross product of current and target directions)
            Vector3D rotationAxis = Vector3D.Cross(currentForward, targetDirection);

            // If the rotation axis is near zero, the directions are parallel or anti-parallel
            if (rotationAxis.LengthSquared() < 1e-6)
            {
                grid.Physics.AngularVelocity = Vector3.Zero;
                return;
            }

            // Normalize the rotation axis
            rotationAxis = Vector3D.Normalize(rotationAxis);

            // Calculate the angle between the current and target directions
            double angle = Math.Acos(MathHelper.Clamp(Vector3D.Dot(currentForward, targetDirection), -1.0, 1.0));

            // If the angle is very small, stop angular velocity
            if (angle < 0.002)
            {
                grid.Physics.AngularVelocity = Vector3.Zero;
                return;
            }

            // Calculate the angular velocity needed to rotate towards the target direction
            Vector3D angularVelocity = rotationAxis * angle * easingFactor;

            // Apply roll from the controller's roll indicator
            float rollIndicator = controller.RollIndicator; // Get the roll input from the controller
            if (Math.Abs(rollIndicator) > 0.01f) // Apply roll only if the input is significant
            {
                Vector3D rollAxis = controller.WorldMatrix.Forward; // Roll around the forward axis
                angularVelocity += rollAxis * (rollIndicator * 10) * easingFactor;
            }

            // Apply the angular velocity to the grid
            grid.Physics.AngularVelocity = angularVelocity;
        }

        private void SetGridDirectionLASTWORKING(MyCubeGrid grid, Vector3D targetDirection, Vector2 offset, double easingFactor = 0.1)
        {
            if (grid == null)
                return;

            //SetGridDirection3(grid, targetDirection, offset, easingFactor);
            //return;

            var controller = GetController(grid as IMyCubeGrid);
            //controller = null;

            // Normalize the target direction
            Vector3D normalizedTargetDirection = Vector3D.Normalize(targetDirection);

            // Get the grid's current forward direction
            Vector3D currentForwardDirection = grid.WorldMatrix.Forward;
            if (controller != null)
                currentForwardDirection = controller.WorldMatrix.Forward;

            // Interpolate between the current forward direction and the target direction
            Vector3D easedDirection = Vector3D.Lerp(currentForwardDirection, normalizedTargetDirection, easingFactor);
            easedDirection = Vector3D.Normalize(easedDirection); // Ensure the result is normalized

            // Get the grid's current up direction
            Vector3D upDirection = grid.WorldMatrix.Up;
            if (controller != null)
                upDirection = controller.WorldMatrix.Up;

            // Calculate the right direction using the cross product
            Vector3D rightDirection = Vector3D.Cross(easedDirection, upDirection);
            rightDirection = Vector3D.Normalize(rightDirection);

            // Recalculate the up direction to ensure orthogonality
            upDirection = Vector3D.Cross(rightDirection, easedDirection);

            // Create the new rotation matrix
            MatrixD newWorldMatrix = grid.WorldMatrix;
            newWorldMatrix.Forward = easedDirection;
            newWorldMatrix.Right = rightDirection;
            newWorldMatrix.Up = upDirection;

            // Preserve the grid's current position
            newWorldMatrix.Translation = grid.WorldMatrix.Translation;

            /*
            // Apply the offset to the grid's position
            Vector3D offsetPosition = grid.WorldMatrix.Translation
                + (rightDirection * offset.X) // Offset to the right
                + (upDirection * offset.Y);  // Offset upward

            // Apply the offset position
            newWorldMatrix.Translation = offsetPosition;
            */

            // Apply the new rotation matrix to the grid
            grid.WorldMatrix = newWorldMatrix;
            //grid.Teleport(newWorldMatrix);
        }

        private void SetGridDirection3(MyCubeGrid grid, Vector3D targetDirection, Vector2 offset, double easingFactor = 0.1)
        {
            if (grid == null)
                return;

            var controller = GetController(grid as IMyCubeGrid);
            if (controller == null)
                return;

            // Normalize the target direction
            Vector3D normalizedTargetDirection = Vector3D.Normalize(targetDirection);

            // Get the grid's current forward direction
            Vector3D currentForwardDirection = controller.WorldMatrix.Forward;

            // Check if the grid is already aligned with the target direction
            if (Vector3D.Dot(currentForwardDirection, normalizedTargetDirection) > 0.99)
                return; // Already aligned

            // Interpolate between the current forward direction and the target direction
            Vector3D easedDirection = Vector3D.Lerp(currentForwardDirection, normalizedTargetDirection, easingFactor);
            easedDirection = Vector3D.Normalize(easedDirection); // Ensure the result is normalized

            // Get the grid's current up direction
            Vector3D upDirection = controller.WorldMatrix.Up;

            // Calculate the right direction using the cross product
            Vector3D rightDirection = Vector3D.Cross(easedDirection, upDirection);
            rightDirection = Vector3D.Normalize(rightDirection);

            // Recalculate the up direction to ensure orthogonality
            upDirection = Vector3D.Cross(rightDirection, easedDirection);

            // Create the new rotation matrix
            MatrixD newWorldMatrix = grid.WorldMatrix;
            newWorldMatrix.Forward = easedDirection;
            newWorldMatrix.Right = rightDirection;
            newWorldMatrix.Up = upDirection;

            // Apply the offset to the grid's position
            Vector3D offsetPosition = grid.WorldMatrix.Translation
                + (rightDirection * offset.X) // Offset to the right
                + (upDirection * offset.Y);  // Offset upward

            // Apply the offset position
            newWorldMatrix.Translation = offsetPosition;

            // Apply the new rotation matrix to the grid
            grid.WorldMatrix = newWorldMatrix;
        }

        private void SetGridDirection2(MyEntity gate, MyCubeGrid grid, Vector3D targetDirection, double easingFactor = 0.1)
        {
            if (grid == null || gate == null)
                return;

            // Normalize the target direction
            Vector3D normalizedTargetDirection = Vector3D.Normalize(targetDirection);

            // Get the gate's "Up" direction
            Vector3D gateUpDirection = gate.WorldMatrix.Up;

            // Get the grid's current forward direction
            Vector3D currentForwardDirection = grid.WorldMatrix.Forward;

            // Interpolate between the current forward direction and the target direction
            Vector3D easedDirection = Vector3D.Lerp(currentForwardDirection, normalizedTargetDirection, easingFactor);
            easedDirection = Vector3D.Normalize(easedDirection); // Ensure the result is normalized

            // Calculate the right direction using the cross product of the forward and up directions
            Vector3D rightDirection = Vector3D.Cross(easedDirection, gateUpDirection);
            rightDirection = Vector3D.Normalize(rightDirection);

            // Recalculate the up direction to ensure orthogonality
            Vector3D upDirection = Vector3D.Cross(rightDirection, easedDirection);

            // Create the new rotation matrix
            MatrixD newWorldMatrix = grid.WorldMatrix;
            newWorldMatrix.Forward = easedDirection;
            newWorldMatrix.Right = rightDirection;
            newWorldMatrix.Up = upDirection;

            // Preserve the grid's current position
            newWorldMatrix.Translation = grid.WorldMatrix.Translation;

            // Apply the new rotation matrix to the grid
            grid.WorldMatrix = newWorldMatrix;
        }

        private void SetGridDirection2(MyCubeGrid grid, Vector3D targetDirection, double easingFactor = 0.1)
        {
            if (grid == null)
            {
                MyAPIGateway.Utilities.ShowNotification("Grid is null", 16);
                return;
            }

            // Normalize the target direction
            Vector3D normalizedTargetDirection = Vector3D.Normalize(targetDirection);

            // Get the grid's current forward direction
            Vector3D currentForwardDirection = grid.WorldMatrix.Forward;

            // Interpolate between the current forward direction and the target direction
            Vector3D easedDirection = Vector3D.Lerp(currentForwardDirection, normalizedTargetDirection, easingFactor);
            easedDirection = Vector3D.Normalize(easedDirection); // Ensure the result is normalized

            // Get the grid's current up direction
            Vector3D upDirection = grid.WorldMatrix.Up;

            // Ensure upDirection is not parallel to easedDirection
            if (Vector3D.IsZero(Vector3D.Cross(easedDirection, upDirection)))
            {
                // Adjust upDirection to avoid parallelism
                upDirection = Vector3D.Up; // Default to world up
            }

            // Calculate the right direction using the cross product
            Vector3D rightDirection = Vector3D.Cross(easedDirection, upDirection);
            rightDirection = Vector3D.Normalize(rightDirection);

            // Recalculate the up direction to ensure orthogonality
            upDirection = Vector3D.Cross(rightDirection, easedDirection);

            // Create the new rotation matrix
            MatrixD newWorldMatrix = grid.WorldMatrix;
            newWorldMatrix.Forward = easedDirection;
            newWorldMatrix.Right = rightDirection;
            newWorldMatrix.Up = upDirection;

            // Preserve the grid's current position
            newWorldMatrix.Translation = grid.WorldMatrix.Translation;

            // Apply the new rotation matrix to the grid
            grid.WorldMatrix = newWorldMatrix;

            // Debug: Log the new orientation
            MyAPIGateway.Utilities.ShowNotification($"New Forward: {newWorldMatrix.Forward}, Up: {newWorldMatrix.Up}", 16);
        }

        private void SetGridVelocity(MyCubeGrid grid, Vector3D velocity, bool teleport = false, bool shipVelocity = true)
        {
            if (grid == null)
                return;

            /*
            if (grid?.Physics == null)
                return;
            */
            if (grid.Physics.Enabled)
            {
                //grid.Physics.Enabled = false;
                //grid.Physics.Deactivate();
            }

            // Set the grid's linear velocity
            if (shipVelocity)
                grid.Physics.LinearVelocity = velocity;
            if (teleport)
            {
                //TeleportGridForward(grid, SuperluminalSpeed);
                //TeleportGridForwardOLD(grid, velocity);
            }
        }

        private void SetGridVelocity2(MyCubeGrid grid, Vector3D targetVelocity, Vector2 offset, bool teleport = false, bool shipVelocity = true, double rampFactor = 0.1)
        {
            if (grid == null || grid.Physics == null || !grid.Physics.Enabled)
                return;

            var controller = GetController(grid as IMyCubeGrid);
            //controller = null;

            // Gradually ramp up the velocity
            if (shipVelocity)
            {
                // Get the current velocity
                Vector3D currentVelocity = grid.Physics.LinearVelocity;

                // Interpolate towards the target velocity
                Vector3D newVelocity = Vector3D.Lerp(currentVelocity, targetVelocity, rampFactor);

                // Set the new velocity
                grid.Physics.LinearVelocity = (rampFactor == 0 ? targetVelocity : newVelocity);
            }

            if (teleport)
            {
                // Handle teleport logic if needed
                // TeleportGridForward(grid, SuperluminalSpeed);
                TeleportGrid(grid, targetVelocity, offset);
                //TeleportGridForwardNew(grid, targetVelocity, rampFactor);
            }
        }

        private void EaseIntoPosition(MyCubeGrid grid, Vector3D targetPosition, double easingFactor = 0.1)
        {
            if (grid == null || grid.Physics == null || !grid.Physics.Enabled)
                return;

            //var controller = ControllersInTransit[grid];
            //var controller = GetController(grid);

            // Get the current WorldMatrix of the grid
            MatrixD currentWorldMatrix = grid.WorldMatrix;

            // Get the grid's bounding box
            BoundingBoxD gridBoundingBox = grid.PositionComp.WorldAABB;

            // Calculate the offset between the grid's bounding box center and its current position
            Vector3D boundingBoxCenter = gridBoundingBox.Center;
            Vector3D offset = currentWorldMatrix.Translation - boundingBoxCenter;

            // Adjust the target position to account for the offset
            Vector3D adjustedTargetPosition = targetPosition + offset;

            // Calculate the velocity needed to move towards the target position
            Vector3D currentPosition = currentWorldMatrix.Translation;
            Vector3D velocity = (adjustedTargetPosition - currentPosition) * easingFactor;

            // Use SetGridVelocity2 to move the grid
            //SetGridVelocity2(grid, velocity, teleport: false, shipVelocity: true, rampFactor: easingFactor);
            if (velocity.LengthSquared() < 0.02)
            {
                velocity = Vector3D.Normalize(adjustedTargetPosition - currentPosition) * 0.1; // Minimum velocity
            }
            grid.Physics.LinearVelocity = velocity * 50;

            /*
            // Calculate the target orientation
            Vector3D forwardDirection = Vector3D.Normalize(adjustedTargetPosition - currentPosition);
            if (forwardDirection.IsValid())
            {
                // Use setGridAngle to orient the grid
                SetGridAngle(grid, forwardDirection, rampFactor: easingFactor);
            }
            */
        }

        private void EaseIntoPosition(MyCubeGrid grid, Vector3D currentPosition, Vector3D targetPosition, double easingFactor = 0.1)
        {
            if (grid == null || grid.Physics == null || !grid.Physics.Enabled)
                return;

            //var controller = ControllersInTransit[grid];
            //var controller = GetController(grid);

            // Get the current WorldMatrix of the grid
            //MatrixD currentWorldMatrix = grid.WorldMatrix;

            // Get the grid's bounding box
            BoundingBoxD gridBoundingBox = grid.PositionComp.WorldAABB;

            // Calculate the offset between the grid's bounding box center and its current position
            Vector3D boundingBoxCenter = gridBoundingBox.Center;
            //Vector3D offset = currentWorldMatrix.Translation - boundingBoxCenter;

            // Adjust the target position to account for the offset
            //Vector3D adjustedTargetPosition = targetPosition + offset;

            // Calculate the velocity needed to move towards the target position
            //Vector3D currentPosition = currentWorldMatrix.Translation;
            Vector3D velocity = (targetPosition - currentPosition) * easingFactor;

            // Use SetGridVelocity2 to move the grid
            //SetGridVelocity2(grid, velocity, teleport: false, shipVelocity: true, rampFactor: easingFactor);
            if (velocity.LengthSquared() < 0.02)
            {
                velocity = Vector3D.Normalize(targetPosition - currentPosition) * 0.1; // Minimum velocity
            }

            grid.Physics.LinearVelocity = velocity * 30;

            /*
            // Calculate the target orientation
            Vector3D forwardDirection = Vector3D.Normalize(adjustedTargetPosition - currentPosition);
            if (forwardDirection.IsValid())
            {
                // Use setGridAngle to orient the grid
                SetGridAngle(grid, forwardDirection, rampFactor: easingFactor);
            }
            */
        }

        private void SetGridAngle(MyCubeGrid grid, MyEntity ring, Vector3D targetDirection, double rampFactor = 0.1)
        {
            if (grid == null || grid.Physics == null || !grid.Physics.Enabled)
                return;

            var controller = GetController(grid as IMyCubeGrid);
            if (controller == null)
                return;

            // Normalize the target direction
            //targetDirection = Vector3D.Normalize(targetDirection);
            targetDirection = ring.WorldMatrix.Forward;

            // Get the current forward direction from the controller
            Vector3D currentForward = controller.WorldMatrix.Forward;

            // Calculate the axis of rotation (cross product of current and target directions)
            Vector3D rotationAxis = Vector3D.Cross(currentForward, targetDirection);

            // If the rotation axis is near zero, the directions are parallel or anti-parallel
            if (rotationAxis.LengthSquared() < 1e-6)
            {
                grid.Physics.AngularVelocity = Vector3.Zero;
                return;
            }

            // Normalize the rotation axis
            rotationAxis = Vector3D.Normalize(rotationAxis);

            // Calculate the angle between the current and target directions
            double angle = Math.Acos(MathHelper.Clamp(Vector3D.Dot(currentForward, targetDirection), -1.0, 1.0));

            // If the angle is very small, stop angular velocity
            if (angle < 0.002)
            {
                grid.Physics.AngularVelocity = Vector3.Zero;
                return;
            }

            // Calculate the angular velocity needed to rotate towards the target direction
            Vector3D angularVelocity = rotationAxis * angle * rampFactor;

            // Apply the angular velocity to the grid
            grid.Physics.AngularVelocity = angularVelocity;
        }

        private void TeleportGrid(MyCubeGrid grid, Vector3D velocity, Vector2 offset)
        {
            if (grid == null)
                return;

            //TeleportGridForward2(grid, velocity, offset);
            //return;

            // Get the grid's current position and forward direction
            Vector3D currentPosition = grid.WorldMatrix.Translation;

            // Calculate the new position
            Vector3D newPosition = currentPosition + (velocity * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS);

            // Update the grid's WorldMatrix with the new position
            MatrixD newWorldMatrix = grid.WorldMatrix;
            newWorldMatrix.Translation = newPosition;

            // Apply the new WorldMatrix to the grid
            grid.Teleport(newWorldMatrix);
        }

        private void TeleportGridForward2(MyCubeGrid grid, Vector3D velocity, Vector2 offset)
        {
            if (grid == null)
                return;

            var controller = GetController(grid as IMyCubeGrid);

            // Get the grid's bounding box center
            BoundingBoxD gridBoundingBox = grid.PositionComp.WorldAABB;
            Vector3D boundingBoxCenter = gridBoundingBox.Center;

            // Get the controller's forward direction
            Vector3D forwardDirection = controller.WorldMatrix.Forward;

            // Calculate the new position based on the controller's forward direction
            Vector3D newPosition = boundingBoxCenter + (forwardDirection * velocity * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS);

            // Apply the offset to the new position
            newPosition += (controller.WorldMatrix.Right * offset.X) + (controller.WorldMatrix.Up * offset.Y);

            // Update the grid's WorldMatrix with the new position
            MatrixD newWorldMatrix = grid.WorldMatrix;
            newWorldMatrix.Translation = newPosition;

            // Apply the updated WorldMatrix to the grid
            grid.Teleport(newWorldMatrix);
        }

        //private void TeleportGridForward(MyCubeGrid grid, double speed)
        private void TeleportGridForwardOLD(MyCubeGrid grid, Vector3D velocity, Vector2 offset)
        {
            if (grid == null)
                return;

            // Get the grid's current position and forward direction
            Vector3D currentPosition = grid.WorldMatrix.Translation;
            Vector3D forwardDirection = grid.WorldMatrix.Forward;

            // Calculate the new position
            Vector3D newPosition = currentPosition + (velocity * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS);

            // Update the grid's WorldMatrix with the new position
            MatrixD newWorldMatrix = grid.WorldMatrix;
            newWorldMatrix.Translation = newPosition;

            // Apply the new WorldMatrix to the grid
            grid.Teleport(newWorldMatrix);
        }

        private void TeleportGridForwardNew(MyCubeGrid grid, Vector3D targetVelocity, double rampFactor = 0.1)
        {
            if (grid == null)
                return;

            // Get the grid's current position and forward direction
            Vector3D currentPosition = grid.WorldMatrix.Translation;
            Vector3D currentVelocity = grid.Physics?.LinearVelocity ?? Vector3D.Zero;

            // Interpolate the velocity towards the target velocity
            Vector3D newVelocity = Vector3D.Lerp(currentVelocity, targetVelocity, rampFactor);

            // Calculate the new position based on the interpolated velocity
            Vector3D newPosition = currentPosition + (newVelocity * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS);

            // Update the grid's WorldMatrix with the new position
            MatrixD newWorldMatrix = grid.WorldMatrix;
            newWorldMatrix.Translation = newPosition;

            // Apply the new WorldMatrix to the grid
            grid.Teleport(newWorldMatrix);

            // Optionally, update the grid's velocity for the next frame
            if (grid.Physics != null)
            {
                grid.Physics.LinearVelocity = newVelocity;
            }
        }

        private void TeleportGridForward(MyCubeGrid grid, Vector3D velocity)
        {
            if (grid == null)
                return;

            // Get the grid's current WorldMatrix
            MatrixD currentWorldMatrix = grid.WorldMatrix;

            // Get the grid's bounding box
            BoundingBoxD gridBoundingBox = grid.PositionComp.WorldAABB;

            // Calculate the offset between the grid's bounding box center and its current position
            Vector3D boundingBoxCenter = gridBoundingBox.Center;
            Vector3D offset = currentWorldMatrix.Translation - boundingBoxCenter;

            // Calculate the new position based on velocity
            Vector3D newPosition = currentWorldMatrix.Translation + (velocity * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS);

            // Adjust the new position to account for the bounding box offset
            newPosition += offset;

            // Update the grid's WorldMatrix with the new position
            MatrixD newWorldMatrix = currentWorldMatrix;
            newWorldMatrix.Translation = newPosition;

            // Apply the updated WorldMatrix to the grid
            grid.Teleport(newWorldMatrix);
        }

        private void EaseIntoTeleport(MyCubeGrid grid, MatrixD targetWorldMatrix, float easingFactor = 0.1f)
        {
            if (grid == null)
                return;

            // Get the current WorldMatrix of the grid
            MatrixD currentWorldMatrix = grid.WorldMatrix;

            // Interpolate the position
            Vector3D interpolatedPosition = Vector3D.Lerp(currentWorldMatrix.Translation, targetWorldMatrix.Translation, easingFactor);

            // Interpolate the rotation using SLERP
            Quaternion currentRotation = Quaternion.CreateFromRotationMatrix(currentWorldMatrix);
            Quaternion targetRotation = Quaternion.CreateFromRotationMatrix(targetWorldMatrix);
            Quaternion interpolatedRotation = Quaternion.Slerp(currentRotation, targetRotation, easingFactor);

            // Create the new WorldMatrix
            MatrixD interpolatedWorldMatrix = MatrixD.CreateFromQuaternion(interpolatedRotation);
            interpolatedWorldMatrix.Translation = interpolatedPosition;

            // Apply the interpolated WorldMatrix to the grid
            grid.Teleport(interpolatedWorldMatrix);
        }

        private void EaseIntoPositionOld(MyCubeGrid grid, Vector3D targetPosition, double easingFactor = 0.1)
        {
            if (grid == null)
                return;

            // Get the current WorldMatrix of the grid
            MatrixD currentWorldMatrix = grid.WorldMatrix;

            // Interpolate the position
            Vector3D interpolatedPosition = Vector3D.Lerp(currentWorldMatrix.Translation, targetPosition, easingFactor);

            // Update only the position in the WorldMatrix
            currentWorldMatrix.Translation = interpolatedPosition;

            grid.Physics.LinearVelocity = Vector3.Zero;

            // Apply the updated WorldMatrix to the grid
            grid.Teleport(currentWorldMatrix);
        }

        private void EaseIntoPositionOrig(MyCubeGrid grid, Vector3D targetPosition, double easingFactor = 0.1)
        {
            if (grid == null)
                return;

            // Get the current WorldMatrix of the grid
            MatrixD currentWorldMatrix = grid.WorldMatrix;

            // Get the grid's bounding box
            BoundingBoxD gridBoundingBox = grid.PositionComp.WorldAABB;

            // Calculate the offset between the grid's bounding box center and its current position
            Vector3D boundingBoxCenter = gridBoundingBox.Center;
            Vector3D offset = currentWorldMatrix.Translation - boundingBoxCenter;

            // Adjust the target position to account for the offset
            Vector3D adjustedTargetPosition = targetPosition + offset;

            // Interpolate the position
            Vector3D interpolatedPosition = Vector3D.Lerp(currentWorldMatrix.Translation, adjustedTargetPosition, easingFactor);

            // Update only the position in the WorldMatrix
            currentWorldMatrix.Translation = interpolatedPosition;

            // Stop the grid's velocity to ensure smooth movement
            if (grid.Physics != null)
            {
                grid.Physics.LinearVelocity = currentWorldMatrix.Translation - grid.WorldMatrix.Translation;
                OverrideThrusters(grid, currentWorldMatrix.Translation - grid.WorldMatrix.Translation);
            }
                //grid.Physics.LinearVelocity = GetRelativeLinearVelocity(grid, );
                //grid.Physics.LinearVelocity = Vector3.Zero;

            // Apply the updated WorldMatrix to the grid
            grid.Teleport(currentWorldMatrix);
        }

        private void EaseIntoPosition2(MyCubeGrid grid, Vector3D targetPosition, double easingFactor = 0.1)
        {
            if (grid == null || grid.Physics == null || !grid.Physics.Enabled)
                return;

            //var controller = ControllersInTransit[grid];
            var controller = GetController(grid);

            // Get the current WorldMatrix of the grid
            MatrixD currentWorldMatrix = grid.WorldMatrix;

            // Get the grid's bounding box
            BoundingBoxD gridBoundingBox = grid.PositionComp.WorldAABB;

            // Calculate the offset between the grid's bounding box center and its current position
            Vector3D boundingBoxCenter = gridBoundingBox.Center;
            Vector3D offset = currentWorldMatrix.Translation - boundingBoxCenter;

            // Adjust the target position to account for the offset
            Vector3D adjustedTargetPosition = targetPosition + offset;

            // Calculate the velocity needed to move towards the target position
            Vector3D currentPosition = currentWorldMatrix.Translation;
            Vector3D velocity = (adjustedTargetPosition - currentPosition) * easingFactor;

            // Use SetGridVelocity2 to move the grid
            //SetGridVelocity2(grid, velocity, teleport: false, shipVelocity: true, rampFactor: easingFactor);
            if (velocity.LengthSquared() < 0.01)
            {
                velocity = Vector3D.Normalize(adjustedTargetPosition - currentPosition) * 0.1; // Minimum velocity
            }
            grid.Physics.LinearVelocity = velocity * 50;

            // Calculate the target orientation
            Vector3D forwardDirection = Vector3D.Normalize(adjustedTargetPosition - currentPosition);
            if (forwardDirection.IsValid())
            {
                // Use setGridAngle to orient the grid
                //SetGridAngle(grid, forwardDirection, rampFactor: easingFactor);
            }
        }

        private void EaseIntoPositionOLD(MyCubeGrid grid, Vector3D targetPosition, double easingFactor = 0.1)
        {
            if (grid == null)
                return;

            // Get the current WorldMatrix of the grid
            MatrixD currentWorldMatrix = grid.WorldMatrix;

            // Get the grid's bounding box
            BoundingBoxD gridBoundingBox = grid.PositionComp.WorldAABB;

            // Calculate the offset between the grid's bounding box center and its current position
            Vector3D boundingBoxCenter = gridBoundingBox.Center;
            Vector3D offset = currentWorldMatrix.Translation - boundingBoxCenter;

            // Adjust the target position to account for the offset
            Vector3D adjustedTargetPosition = targetPosition + offset;

            // Interpolate the position
            Vector3D interpolatedPosition = Vector3D.Lerp(currentWorldMatrix.Translation, adjustedTargetPosition, easingFactor);

            // Update only the position in the WorldMatrix
            currentWorldMatrix.Translation = interpolatedPosition;

            // Stop the grid's velocity to ensure smooth movement
            if (grid.Physics != null)
            {
                grid.Physics.LinearVelocity = currentWorldMatrix.Translation - grid.WorldMatrix.Translation;
                OverrideThrusters(grid, currentWorldMatrix.Translation - grid.WorldMatrix.Translation);
            }
            //grid.Physics.LinearVelocity = GetRelativeLinearVelocity(grid, );
            //grid.Physics.LinearVelocity = Vector3.Zero;

            // Apply the updated WorldMatrix to the grid
            grid.Teleport(currentWorldMatrix);
        }

        private Vector3D GetRelativeLinearVelocity(MyCubeGrid grid, MatrixD currentWorldMatrix, Vector3D referenceVelocity)
        {
            if (grid == null || grid.Physics == null)
                return Vector3D.Zero;

            // Get the grid's linear velocity in world space
            Vector3D gridVelocity = grid.Physics.LinearVelocity;

            // Calculate the relative velocity
            Vector3D relativeVelocity = gridVelocity - referenceVelocity;

            return relativeVelocity;
        }

        void MoveGridByBoundingBox(MyCubeGrid grid, Vector3D offset)
        {
            if (grid == null)
                return;

            // Get the grid's bounding box
            BoundingBoxD gridBoundingBox = grid.PositionComp.WorldAABB;

            // Calculate the new center position
            Vector3D newCenter = gridBoundingBox.Center + offset;

            // Update the grid's position
            MatrixD newWorldMatrix = grid.WorldMatrix;
            newWorldMatrix.Translation = newCenter;
            grid.Teleport(newWorldMatrix);
        }

        private void OverrideThrusters(IMyCubeGrid grid, Vector3D desiredAcceleration, bool force = false)
        {
                return;
            if (grid == null)
                return;

            if (!force)
                if (TargetAcceleration == desiredAcceleration)
                    return;

            TargetAcceleration = desiredAcceleration;

            // Get the grid's mass
            float gridMass = grid.Physics.Mass;

            // Calculate the required force for the desired acceleration
            Vector3D requiredForce = desiredAcceleration * gridMass;

            // Iterate through all thrusters on the grid
            //var thrusters = new List<IMyThrust>();
            //grid.GetFatBlocks(thrusters);
            //var fatBlocks = grid.GetFatBlocks<IMyCubeBlock>();
            var thrusters = grid.GetFatBlocks<IMyThrust>();

            foreach (var block in thrusters)
            {
                var thruster = block as IMyThrust;
                if (thruster == null)
                    continue;

                if (force)
                {
                    thruster.ThrustOverridePercentage = 0;
                    continue;
                }

                // Get the thrust direction in world space
                Vector3D thrustDirection = thruster.WorldMatrix.Backward;

                // Project the required force onto the thrust direction
                double forceInDirection = Vector3D.Dot(requiredForce, thrustDirection);

                // Convert force to thrust override (clamp to max thrust)
                float thrustOverride = (float)Math.Max(0, Math.Min(forceInDirection, thruster.MaxEffectiveThrust));

                // Apply the thrust override
                //thruster.ThrustOverride = thrustOverride;

                var thrustPercent = thrustOverride / thruster.MaxEffectiveThrust;

                //if (thrustOverride > 0.2)
                if (thrustPercent > 0.02)
                    thruster.ThrustOverridePercentage = thrustPercent < 0.5 ? 0.5f : thrustPercent;
                else
                    thruster.ThrustOverridePercentage = 0;

                    //MyAPIGateway.Utilities.ShowNotification("thruster doing thigns", 100);
            }
        }

        private Vector2 ApplyShake(float multiplier = 1)
        {
            Vector2 offset = Vector2.Zero;

            if (ShakeFrame > 0 && --ShakeFrame <= 0)
            {
                var randX = Random.Next(-1, 2);
                var randY = Random.Next(-1, 2);

                offset.X = randX * multiplier;
                offset.Y = randY * multiplier;

                ShakeFrame = ShakeInterval;
            }

            return offset;
        }

        private void ApplyShake(MyCubeGrid grid)
        {
            if (ShakeFrame > 0 && --ShakeFrame <= 0)
            {
                Vector3D counterForce = Vector3D.Zero;
                Vector3D force = Vector3D.Zero;
                var randDir = Random.Next(1, 5);

                switch (randDir)
                {
                    case 1:
                        force = (grid.WorldMatrix.Up) * grid.Physics.Mass * 1.2;
                        break;
                    case 2:
                        force = (grid.WorldMatrix.Down) * grid.Physics.Mass * 1.2;
                        break;
                    case 3:
                        force = (grid.WorldMatrix.Left) * grid.Physics.Mass * 1.2;
                        break;
                    case 4:
                        force = (grid.WorldMatrix.Right) * grid.Physics.Mass * 1.2;
                        break;
                }

                if (force == Vector3D.Zero)
                    counterForce = force;
                else
                    counterForce = -force * 0.5;

                // Apply random torque for rotational shake
                var torque = new Vector3D(
                    Random.NextDouble() * 2 - 1, // Random value between -1 and 1
                    Random.NextDouble() * 2 - 1,
                    Random.NextDouble() * 2 - 1
                ) * grid.Physics.Mass * 0.05; // Scale torque by grid mass and intensity

                grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, counterForce * 2, grid.Physics.CenterOfMassWorld, force);

                ShakeFrame = ShakeInterval;
            }
            /*
            */

            return;

            // Calculate shake offset using sine wave
            double time = MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds;
            double shakeOffsetX = Math.Sin(time * shakeFrequency) * shakeAmplitude;
            double shakeOffsetY = Math.Cos(time * shakeFrequency) * shakeAmplitude;
            double shakeOffsetZ = Math.Sin(time * shakeFrequency * 0.5) * shakeAmplitude;

            //Vector3D shakeOffset = new Vector3D(shakeOffsetX, shakeOffsetY, shakeOffsetZ);
            Vector3D shakeOffset = new Vector3D(shakeOffsetX, shakeOffsetY, shakeOffsetZ);
            MatrixD newWorldMatrix = grid.WorldMatrix;
            newWorldMatrix.Translation += shakeOffset;

            grid.Teleport(newWorldMatrix);
        }

        private void ApplyDrift(MyCubeGrid grid)
        {
            // If no drift direction is set, generate a random one
            if (driftDirection == Vector3D.Zero)
            {
                driftDirection = new Vector3D(
                    Random.NextDouble() - 0.5,
                    Random.NextDouble() - 0.5,
                    Random.NextDouble() - 0.5
                );
                driftDirection = Vector3D.Normalize(driftDirection) * driftSpeed;
            }

            // Apply the drift to the grid's position
            MatrixD newWorldMatrix = grid.WorldMatrix;
            newWorldMatrix.Translation += driftDirection;

            grid.Teleport(newWorldMatrix);
        }

        private void InstructShipSystems(MyCubeGrid grid, CustomGridLogic.ShipSystem state)
        {
            grid.GameLogic.GetAs<CustomGridLogic>()?.Instruct(state);
        }

        // The documented Speed setting, SetupSystems used to read it but returns early
        void ReadSpeed()
        {
            float superluminalSpeed;
            var speed = ReadCustomData(CustomData, TradeLaneSpeedKeyword, TradeLaneSeparator);
            SuperluminalSpeed = float.TryParse(speed, out superluminalSpeed)
                ? MathHelper.Clamp(superluminalSpeed, 500f, 500000f)
                : DefaultSuperluminalSpeed;
        }

        void ReadRingInterval()
        {
            float ringInterval;
            var interval = ReadCustomData(CustomData, TradeLaneRingIntervalKeyword, TradeLaneSeparator);
            RingInterval = float.TryParse(interval, out ringInterval)
                ? MathHelper.Clamp(ringInterval, 1000f, 1000000f)
                : DefaultRingInterval;
        }

        // Server side: where the lane leads. A Target: GPS in the Custom Data wins,
        // then the paired computer if it is loaded, then the partner the cluster's
        // lane registry gives, then where the partner was last seen.
        private void UpdateTarget()
        {
            if (TargetBlock != null && (TargetBlock.MarkedForClose || TargetBlock.CubeGrid == null))
                TargetBlock = null;

            TargetFromGps = TargetFromRegistry = false;
            Vector3D gps;
            if (ReadTargetGps(CustomData, out gps))
            {
                TargetFromGps = true;
                HasTarget = true;
                TargetPosition = gps;
                TargetGridPosition = gps;
                TargetUp = Block.CubeGrid.WorldMatrix.Up;
                InhertiRotation = false;
                return;
            }

            var tlid = ReadCustomData(CustomData, TradeLaneIdKeyword, TradeLaneSeparator);
            if (TargetBlock != null)
            {
                HasTarget = true;
                TargetPosition = TargetBlock.WorldMatrix.Translation;
                TargetGridPosition = TargetBlock.CubeGrid.WorldMatrix.Translation;
                TargetUp = TargetBlock.CubeGrid.WorldMatrix.Up;
                StoreTarget(tlid, TargetBlock.EntityId);
                return;
            }

            bool inherit;
            var partner = LaneRegistry.Partner(Block.EntityId, tlid, out inherit);
            if (partner != null)
            {
                HasTarget = TargetFromRegistry = true;
                TargetPosition = partner.Vector(0);
                TargetGridPosition = partner.Vector(3);
                TargetUp = partner.Vector(4);
                InhertiRotation = inherit;
                StoreTarget(tlid, partner.Id);
                return;
            }

            var stored = ModStore.Load<StoredTarget>(Block, StoredTargetKey);
            if (stored != null && LaneRegistry.IsRemoved(stored.PartnerId))
            {
                // The partner was removed from its grid somewhere on the cluster
                MyLog.Default.WriteLine(
                    $"TradeLanes: computer {Block.EntityId} dropped its stored target, {stored.PartnerId} was removed"
                );
                ModStore.Save<StoredTarget>(Block, StoredTargetKey, null);
                StoredTargetXml = null;
                stored = null;
            }
            HasTarget = stored != null && stored.Tlid == tlid;
            if (!HasTarget)
                return;
            TargetPosition = stored.Position;
            TargetGridPosition = stored.GridPosition;
            TargetUp = stored.Up;
            InhertiRotation = stored.InheritRotation;
        }

        void StoreTarget(string tlid, long partnerId)
        {
            var stored = new StoredTarget
            {
                Tlid = tlid,
                Position = TargetPosition,
                GridPosition = TargetGridPosition,
                Up = TargetUp,
                InheritRotation = InhertiRotation,
                PartnerId = partnerId,
            };
            var xml = MyAPIGateway.Utilities.SerializeToXML(stored);
            if (xml == StoredTargetXml)
                return;
            StoredTargetXml = xml;
            ModStore.Save(Block, StoredTargetKey, stored);
        }

        // Target:GPS:name:x:y:z:... in the Custom Data, for a lane whose far end is
        // not loaded with this one. Best put last, the other keys are found by
        // substring.
        public static bool ReadTargetGps(string customData, out Vector3D position)
        {
            position = Vector3D.Zero;
            const string prefix = "Target:";
            foreach (var line in (customData ?? "").Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var parts = trimmed.Substring(prefix.Length).Trim().Split(':');
                double x, y, z;
                if (parts.Length < 5 || !parts[0].Equals("GPS", StringComparison.OrdinalIgnoreCase)
                    || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                    || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                    || !double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                    return false;

                position = new Vector3D(x, y, z);
                return true;
            }
            return false;
        }

        // Server side: tells the cluster's lane registry about this computer. On a
        // single server nothing listens.
        void ReportToRegistry()
        {
            var source = !HasTarget ? "none"
                : TargetFromGps ? "Target GPS"
                : TargetFromRegistry ? "lane registry"
                : TargetBlock != null ? "loaded partner"
                : "stored target";
            if (source != TargetSource)
            {
                TargetSource = source;
                MyLog.Default.WriteLine($"TradeLanes: computer {Block.EntityId} target from {source}");
            }

            var tlid = ReadCustomData(CustomData, TradeLaneIdKeyword, TradeLaneSeparator);
            if (tlid == "ERROR" || Block.CubeGrid == null)
                return;
            LaneRegistry.Report(
                LaneRegistry.Entry(
                    Block.EntityId,
                    Block.CubeGrid.EntityId,
                    tlid,
                    Block.WorldMatrix,
                    Block.CubeGrid.WorldMatrix,
                    CustomData,
                    HasTarget && !TargetFromGps && !TargetFromRegistry,
                    InhertiRotation
                )
            );
        }

        TradeLaneMessage LaneInfoMessage()
        {
            var m = Block.WorldMatrix;
            return new TradeLaneMessage
            {
                Kind = MessageKind.LaneInfo,
                EntityId = Block.EntityId,
                OtherId = Block.CubeGrid?.EntityId ?? 0,
                Flag = HasTarget,
                Number = InhertiRotation ? 1 : 0,
                Text = CustomData,
                Vectors = new[]
                {
                    TargetPosition.X, TargetPosition.Y, TargetPosition.Z,
                    TargetGridPosition.X, TargetGridPosition.Y, TargetGridPosition.Z,
                    TargetUp.X, TargetUp.Y, TargetUp.Z,
                    m.Translation.X, m.Translation.Y, m.Translation.Z,
                    m.Forward.X, m.Forward.Y, m.Forward.Z,
                    m.Up.X, m.Up.Y, m.Up.Z,
                },
            };
        }

        // Server side: tells the clients when anything about the lane changed
        private void PublishLaneInfo()
        {
            var m = Block.WorldMatrix;
            var info = $"{HasTarget}|{InhertiRotation}|{TargetPosition}|{TargetGridPosition}|{TargetUp}|{m.Translation}|{m.Forward}|{m.Up}|{CustomData}";
            if (info == LaneInfoSent)
                return;
            LaneInfoSent = info;
            SendLaneInfo();
        }

        public void SendLaneInfo(ulong to = 0)
        {
            if (Block != null)
                TradeLaneNetwork.SendLaneInfo(LaneInfoMessage(), to);
        }

        // Makes this a stand-in: on a client it draws the rings of a lane, on a cluster
        // node it docks the ships that node has at them
        public void InitStandIn()
        {
            IsStandIn = true;
            var modPath = TradeLaneSystem.Instance.ModContext.ModPath;
            TradeLane_Model = modPath + TradeLane_Model;
            TradeLane_Lights_Front_Model = modPath + TradeLane_Lights_Front_Model;
            TradeLane_Lights_Back_Model = modPath + TradeLane_Lights_Back_Model;
            TradeLane_Ring_Model = modPath + TradeLane_Ring_Model;
        }

        // Where the lane starts and leads: on a client from the server, on a cluster
        // node's stand-in from the lane registry
        public void ApplyLaneInfo(TradeLaneMessage message)
        {
            var v = message.Vectors;
            if (v == null || v.Length < 18)
                return;

            HasTarget = message.Flag;
            InhertiRotation = message.Number != 0;
            TargetPosition = new Vector3D(v[0], v[1], v[2]);
            TargetGridPosition = new Vector3D(v[3], v[4], v[5]);
            TargetUp = new Vector3D(v[6], v[7], v[8]);

            if (!IsStandIn)
                return;

            StandInMatrix = MatrixD.CreateWorld(new Vector3D(v[9], v[10], v[11]), new Vector3D(v[12], v[13], v[14]), new Vector3D(v[15], v[16], v[17]));
            StandInGridId = message.OtherId;
            StandInComputerId = message.EntityId;
            if (message.Text != CustomData)
            {
                CustomData = message.Text ?? "";
                ReadSpeed();
                ReadRingInterval();
                UpdateGates();
            }
        }

        // Plays a ring effect here, unless this is a dedicated server, and on every client
        private void PlayGateEffect(MyEntity gate, MyCubeGrid grid, GateEffectKind kind)
        {
            int index = LaneGates.IndexOf(gate);
            GateEffect(index, grid, kind);
            var computerId = Block?.EntityId ?? StandInComputerId;
            if (computerId != 0)
                TradeLaneNetwork.SendGateEffect(computerId, index, grid, kind);
        }

        public void GateEffect(int gateIndex, MyCubeGrid grid, GateEffectKind kind)
        {
            if (MyAPIGateway.Utilities.IsDedicated || gateIndex < 0 || gateIndex >= LaneGates.Count)
                return;

            var gate = LaneGates[gateIndex];
            MyParticleEffect particles;
            switch (kind)
            {
                case GateEffectKind.RequestDocking:
                    RemoveParticleEffects(ref EnterRingPartciles);
                    if (grid != null)
                        EnterRingPartciles = SpawnParticleEffects(gate, RingMergeParticleName, MatrixD.Identity, grid.PositionComp.WorldAABB.Center);
                    break;
                case GateEffectKind.CancelDocking:
                    StopSoundEmitter(ShipSoundEmitter, true);
                    RemoveParticleEffects(ref EnterRingPartciles);
                    SpawnParticleEffectsOnto(gate, CancelRingMergeParticleName, MatrixD.Identity);
                    if (grid != null)
                        SpawnParticleEffects(gate, RingMergeParticleName, MatrixD.Identity, grid.PositionComp.WorldAABB.Center);
                    break;
                case GateEffectKind.StageChange:
                    StopSoundEmitter(ShipSoundEmitter);
                    if (GateParticles.TryGetValue(gate, out particles))
                        ResetParticleEffects(particles);
                    break;
            }
        }

        #endregion

        #region DEBUG

        public static void DrawOBB(MyOrientedBoundingBoxD obb, Color color, MySimpleObjectRasterizer raster = MySimpleObjectRasterizer.Wireframe, float thickness = 0.01f)
        {
            var material = MyStringId.GetOrCompute("Square");
            var box = new BoundingBoxD(-obb.HalfExtent, obb.HalfExtent);
            var wm = MatrixD.CreateFromQuaternion(obb.Orientation);
            wm.Translation = obb.Center;
            MySimpleObjectDraw.DrawTransparentBox(ref wm, ref box, ref color, raster, 1, thickness, material, material);
        }

        #endregion

        #region UNUSED

        public void LinkGrids(long linkId, IMyTerminalBlock sourceBlock, IMyTerminalBlock targetBlock)
        {
            if (sourceBlock.IsInSameLogicalGroupAs(targetBlock))
                return;
            MyCubeGrid.CreateGridGroupLink(GridLinkTypeEnum.Electrical, linkId, (MyCubeGrid)sourceBlock.CubeGrid, (MyCubeGrid)targetBlock.CubeGrid);
            MyCubeGrid.CreateGridGroupLink(GridLinkTypeEnum.Logical, linkId, (MyCubeGrid)sourceBlock.CubeGrid, (MyCubeGrid)targetBlock.CubeGrid);
        }
        
        public void UnlinkGrids(long linkId, IMyTerminalBlock sourceBlock, IMyTerminalBlock targetBlock)
        {
            if (!sourceBlock.IsInSameLogicalGroupAs(targetBlock))
                return;
            MyCubeGrid.BreakGridGroupLink(GridLinkTypeEnum.Electrical, linkId, (MyCubeGrid)sourceBlock.CubeGrid, (MyCubeGrid)targetBlock.CubeGrid);
            MyCubeGrid.BreakGridGroupLink(GridLinkTypeEnum.Logical, linkId, (MyCubeGrid)sourceBlock.CubeGrid, (MyCubeGrid)targetBlock.CubeGrid);
        }

        #endregion

        #region FAILED, BUGGY OR INCOMPLETE

        private void SpawnGridCopy()
        {
            try
            {
                // Step 1: Get the original grid
                IMyCubeGrid originalGrid = Block.CubeGrid;
                if (originalGrid == null)
                    return;

                // Step 2: Serialize the grid
                MyObjectBuilder_CubeGrid gridBuilder = (MyObjectBuilder_CubeGrid)originalGrid.GetObjectBuilder();

                // Step 3: Clone the grid
                MyObjectBuilder_CubeGrid newGridBuilder = MyAPIGateway.Utilities.SerializeFromBinary<MyObjectBuilder_CubeGrid>(
                    MyAPIGateway.Utilities.SerializeToBinary(gridBuilder));

                // Step 4: Offset the new grid's position
                foreach (var block in newGridBuilder.CubeBlocks)
                {
                    if (block.EntityId == originalGrid.EntityId)
                    {
                        //continue; // Skip the original grid block
                        IMyTerminalBlock laneComp = block as IMyTerminalBlock;
                        if (laneComp != null)
                        {
                            laneComp.CustomName = "";
                        }
                    }
                    /*
                    */

                    block.Min += new Vector3I(0, 0, 10); // Offset by 10 blocks in the X direction
                }

                // Step 5: Add the new grid to the game world
                MyAPIGateway.Entities.CreateFromObjectBuilderParallel(newGridBuilder, true, entity =>
                {
                    MyAPIGateway.Entities.AddEntity(entity);
                });
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("Error", e.Message);
            }
        }
        private void SpawnAntenna(IMySafeZoneBlock safeZoneBlock, string antennaName)
        {
            /*
            if (safeZoneBlock == null)
                return;

            var antennaBuilder = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_RadioAntenna>();
            antennaBuilder.SubtypeName = "LargeBlockRadioAntenna"; // Use the appropriate subtype for the antenna  
            antennaBuilder.CustomName = antennaName;
            antennaBuilder.Enabled = true;

            // Use the correct namespace for the object builder
            var antennaEntity = MyEntities.CreateFromObjectBuilder(antennaBuilder, false) as MyRadioAntenna;

            if (antennaEntity == null)
            {
                MyAPIGateway.Utilities.ShowMessage("AttachAntenna", "Failed to create antenna.");
                return;
            }

            // Add the antenna to the game world  
            MyEntities.Add(antennaEntity);

            // Parent the antenna to the safe zone block  
            ParentEntity(antennaEntity, safeZoneBlock as MyEntity);

            MyAPIGateway.Utilities.ShowMessage("AttachAntenna", $"Antenna '{antennaName}' attached to safe zone block '{safeZoneBlock.CustomName}'.");
            */
        }
        void CreateNormalLight(IMyEntity entity, string lightName, Vector3D position, Color color, float range = 10f, float intensity = 1f)
        {
            if (entity == null)
                return;

            // Create a new light
            var light = MyLights.AddLight();
            if (light == null)
            {
                MyAPIGateway.Utilities.ShowNotification("Failed to create light.", 2000);
                return;
            }

            // Configure the light properties
            light.Start(lightName);
            light.Color = color;
            light.Range = range;
            light.Intensity = intensity;
            light.Falloff = 1f;
            light.LightType = MyLightType.DEFAULT; // Set it as a normal point light
            light.Position = Vector3D.Transform(position, entity.WorldMatrix); // Position relative to the entity

            light.LightOn = true;

            var l = light as IMyLightingBlock;
            l.BlinkIntervalSeconds = 1f;
            l.BlinkLength = 0.3f;
            l.BlinkOffset = 0.1f;

            // Update the light
            light.UpdateLight();
        }
        void AttachLightToEntity(IMyEntity parentEntity, string lightName, Vector3D localPosition, Color color, float range = 10f, float intensity = 1f)
        {
            if (parentEntity == null)
                return;

            // Create a new light
            var light = MyLights.AddLight();
            if (light == null)
            {
                MyAPIGateway.Utilities.ShowNotification("Failed to create light.", 2000);
                return;
            }

            // Configure the light properties
            light.Start(lightName);
            light.Color = color;
            light.Range = range;
            light.Intensity = intensity;
            light.Falloff = 1f;
            light.LightType = MyLightType.SPOTLIGHT; // Set it as a normal point light

            light.LightOn = true;

            // Set the light's position relative to the parent entity
            //light.Position = Vector3D.Transform(localPosition, parentEntity.WorldMatrix);
            light.Position = Vector3D.Transform(Vector3D.Transform(localPosition, Block.WorldMatrix), Block.CubeGrid.WorldMatrixInvScaled);

            // Attach the light to the parent entity
            light.ParentID = parentEntity.Render.GetRenderObjectID();
            if (parentEntity.Render == null || parentEntity.Render.GetRenderObjectID() == uint.MaxValue)
            {
                MyAPIGateway.Utilities.ShowMessage("TL","Invalid parent entity render object ID.");
                return;
            }
            //MyAPIGateway.Utilities.ShowMessage("TL", $"Light position: {light.Position}");


            // Point light properties
            light.LightOn = true;
            light.Color = color;
            light.Range = 5f;
            light.Falloff = 1f;
            light.Intensity = 5f;
            light.PointLightOffset = 0f; // offset light source towards block forward(?), I don't think it moves the glare too.
            //light.DiffuseFactor = 1f; // not sure what numbers do in this

            /*
            // Spotlight properties
            light.LightType = MyLightType.SPOTLIGHT;
            light.ReflectorOn = true;
            light.ReflectorColor = new Color(255, 155, 0); // RGB
            light.ReflectorIntensity = 10f;
            light.ReflectorRange = 100; // how far the projected light goes
            light.ReflectorConeDegrees = 90; // projected light angle in degrees, max 179.
            light.ReflectorTexture = @"Textures\Lights\reflector_large.dds"; // NOTE: for textures inside your mod you need to use: Utils.GetModTextureFullPath(@"Textures\someFile.dds");
            light.CastShadows = true;
            //light.ReflectorGlossFactor = <num>f; // affects gloss in some way

            // Glare properties... which don't seem to work...
            light.GlareOn = true;
            light.GlareSize = new Vector2(1, 1); // glare size in X and Y.
            light.GlareIntensity = 2;
            light.GlareMaxDistance = 50;
            light.SubGlares = GetFlareDefinition("InteriorLight").SubGlares; // subtype name from flares.sbc
            light.GlareType = MyGlareTypeEnum.Normal; // usable values: MyGlareTypeEnum.Normal, MyGlareTypeEnum.Distant, MyGlareTypeEnum.Directional
            light.GlareQuerySize = 0.5f; // glare "box" size, affects occlusion and fade occlussion
            light.GlareQueryShift = 1f; // no idea
            */

            // Update the light
            light.UpdateLight();
            Lights.Add(light);

            var l = light as IMyInteriorLight;
            if (l != null)
            {
                MyAPIGateway.Utilities.ShowMessage("TL", "IMyInteriorLight");
                l.BlinkIntervalSeconds = 1f;
                l.BlinkLength = 0.3f;
                l.BlinkOffset = 0.1f;
            }
        }
        public static MyFlareDefinition GetFlareDefinition(string flareSubtypeId)
        {
            if (string.IsNullOrEmpty(flareSubtypeId))
                throw new ArgumentException("flareSubtypeId must not be null or empty!");

            var flareDefId = new MyDefinitionId(typeof(MyObjectBuilder_FlareDefinition), flareSubtypeId);
            var flareDef = MyDefinitionManager.Static.GetDefinition(flareDefId) as MyFlareDefinition;

            if (flareDef == null)
                throw new Exception($"Couldn't find flare subtype {flareSubtypeId}");

            return flareDef;
        }
        private void DisableGridDamage(IMyCubeGrid grid)
        {
            /*
            if (grid == null)
                return;

            grid.OnBlockDamaged += (block, damage) =>
            {
                // Cancel the damage
                damage.Amount = 0;
            };
            */
        }
        private void EnableSafeZoneWithoutChip(IMySafeZoneBlock safeZoneBlock) // REQUIRES SAFEZONE BLOCK!
        {
            if (safeZoneBlock == null)
                return;

            safeZoneBlock.EnableSafeZone(true);

            MyDefinitionId DefinitionId;
            MyCubeBlockDefinition BlockDefinition = null;
            if (!MyDefinitionId.TryParse("SafeZoneBlock/TradeLaneComputer", out DefinitionId))
                return;

            BlockDefinition = MyDefinitionManager.Static.GetCubeBlockDefinition(DefinitionId);

            if (BlockDefinition == null)
                return;
            if (BlockDefinition as MyThrustDefinition == null)
                return;

            var sz = BlockDefinition as IMySafeZoneBlock;
            //sz.

            // Because setting the power consumption multiplier crashes the game, i'm leaving the vanilla consumption.
            //sz.MaxPowerConsumption = PowerConsumption;
            /*
            // Check if the block matches your custom definition
            var blockDefinition = safeZoneBlock.BlockDefinition as MyCubeBlockDefinition;
            if (blockDefinition != null && blockDefinition.Id.SubtypeName == "CustomSafeZoneBlock")
            {
                // Enable the safe zone without requiring chips
                safeZoneBlock.EnableSafeZone(true);

                // Optionally configure additional safe zone properties
                var safeZone = safeZoneBlock.SafeZoneEntity as MySafeZone;
                if (safeZone != null)
                {
                    safeZone.AccessTypePlayers = MySafeZoneAccess.Whitelist; // Example: Allow only whitelisted players
                    safeZone.AccessTypeFactions = MySafeZoneAccess.Blacklist; // Example: Block specific factions
                    safeZone.Radius = 100f; // Example: Set radius
                }
            }
            */
        }
        private void EnableSafeZoneWithoutCredits(IMySafeZoneBlock safeZoneBlock)
        {
            if (safeZoneBlock == null)
                return;

            // Fix for CS0234: The type or namespace name 'MyObjectBuilder_EntityBase' does not exist in the namespace 'VRage.Game'
            // Removed the reference to 'MyObjectBuilder_EntityBase' as it does not exist in the provided type signatures.
            // Instead, we directly enable the safe zone using the available method.

            safeZoneBlock.EnableSafeZone(true);

            // Optionally configure the safe zone settings if additional properties or methods are available.
            // Example: Uncomment and adjust the following lines if the IMySafeZoneBlock interface provides these properties.
            /*
            safeZoneBlock.AccessTypePlayers = MySafeZoneAccess.Whitelist; // Example: Set access type
            safeZoneBlock.AccessTypeFactions = MySafeZoneAccess.Blacklist; // Example: Set faction access
            safeZoneBlock.Shape = MySafeZoneShape.Sphere; // Example: Set shape
            safeZoneBlock.Radius = 100f; // Example: Set radius
            */
        }
        void LightsWorkingChanged(IMyCubeBlock block)
        {
            try
            {
                /*
                if (!inViewRange)
                    return;
                */

                //Session.UpdateOnce.Add(this); // update next frame
                SetLightsEnabled(block.IsWorking);
            }
            catch (Exception e)
            {
                //SimpleLog.Error(this, e);
            }
        }
        void CreateLights(MyEntity ent, List<IMyModelDummy> dummies)
        {
            if (ent == null || dummies.Count == 0)
                return;

            foreach (var dummy in dummies)
            {
                if (dummy == null)
                    continue;

                //MyAPIGateway.Utilities.ShowNotification("Create Light", 1000);
                //AttachLightToEntity(ent, "lightGreen", (dummy.Matrix.Translation + dummy.Matrix.Backward), Color.Green, 5f);
                //AttachLightToEntity(ent, "lightRed", (dummy.Matrix.Translation + dummy.Matrix.Forward), Color.Red, 5f);
                //CreateLight(ent, dummy.Name, dummy.Matrix, Color.Red);
                //CreateLight(ent, dummy.Name, dummy.Matrix, Color.Green, true);
                //LightState = true;
            }
        }
        void CreateLight(IMyEntity entity, string dummyName, Matrix dummyMatrix, Color color, bool flipForward = false, float Offset = 1f)
        {
            var light = MyLights.AddLight();

            if (light == null)
                MyAPIGateway.Utilities.ShowNotification("Light is null", 1000);

            light.Start(dummyName);
            //light.Color = Color.White;
            light.Color = color;
            light.Range = Block.CubeGrid.GridSize;
            light.Falloff = 1f;
            light.Intensity = 2f;
            light.ParentID = Block.CubeGrid.Render.GetRenderObjectID();
            if (flipForward)
                light.Position = Vector3D.Transform(Vector3D.Transform(dummyMatrix.Translation + dummyMatrix.Backward, Block.WorldMatrix), Block.CubeGrid.WorldMatrixInvScaled);
            else
                light.Position = Vector3D.Transform(Vector3D.Transform(dummyMatrix.Translation + dummyMatrix.Forward, Block.WorldMatrix), Block.CubeGrid.WorldMatrixInvScaled);
            if (flipForward)
                light.ReflectorDirection = Vector3D.TransformNormal(Vector3D.TransformNormal(dummyMatrix.Backward, Block.WorldMatrix), Block.CubeGrid.WorldMatrixInvScaled);
            else
                light.ReflectorDirection = Vector3D.TransformNormal(Vector3D.TransformNormal(dummyMatrix.Forward, Block.WorldMatrix), Block.CubeGrid.WorldMatrixInvScaled);
            light.ReflectorUp = Vector3D.TransformNormal(Vector3D.TransformNormal(dummyMatrix.Up, Block.WorldMatrix), Block.CubeGrid.WorldMatrixInvScaled);

            light.LightOn = true;
            var l = light as IMyReflectorLight;
            if (l == null)
            {
                MyAPIGateway.Utilities.ShowNotification("Light is null", 1000);
                return;
            }
            else
            {
                l.BlinkIntervalSeconds = 1f;
                l.BlinkLength = 0.1f;
                l.BlinkOffset = 0.1f;
            }

            //lights.Add(dummyName, light);
            Lights.Add(light);

            //configurator(dummyName, light, this);

            light.UpdateLight();
        }
        public void DeleteLights()
        {
            try
            {
                if (Lights != null && Lights.Count > 0)
                {
                    foreach (var light in Lights)
                    {
                        MyLights.RemoveLight(light);
                    }
                    Lights.Clear();
                }
                /*
                if (lights != null)
                {
                    foreach (var light in lights.Values)
                    {
                        MyLights.RemoveLight(light);
                    }

                    lights.Clear();
                }
                */
            }
            catch (Exception e)
            {
                //SimpleLog.Error(this, e);
            }
        }
        void SetLightsEnabled(bool state)
        {
            if (Lights != null)
            {
                foreach (var light in Lights)
                {
                    light.LightOn = state;
                    light.GlareOn = state;

                    if (light.LightType == MyLightType.SPOTLIGHT)
                        light.ReflectorOn = state;

                    light.UpdateLight();
                }
            }
        }

        #endregion




        #region OTHER / MISC


        /*
        private void ParentEntity(MyEntity child, MyEntity parent)
        {
            if (child == null || parent == null)
            {
                MyAPIGateway.Utilities.ShowMessage("Error", "Child or Parent entity is null.");
                return;
            }

            // Set the parent of the child entity
            child.Parent = parent;

            // Update the child's local matrix to maintain its current position relative to the parent
            child.PositionComp.SetLocalMatrix(child.WorldMatrix * MatrixD.Invert(parent.WorldMatrix));

            MyAPIGateway.Utilities.ShowMessage("Parenting", $"Entity {child.DisplayName} is now parented to {parent.DisplayName}.");
        }
        */

        // ----------------------------------------------------------------------------------------------------------------

        /*
        void DrawSmallestOBB(IMyCubeGrid mainGrid)
        {
            IMyGridGroupData group = MyAPIGateway.GridGroups.GetGridGroup(GridLinkTypeEnum.Physical, mainGrid);

            List<IMyCubeGrid> grids = new List<IMyCubeGrid>();
            group.GetGrids(grids);

            float smallestVolume = float.MaxValue;
            MyOrientedBoundingBoxD smallestOBB = default(MyOrientedBoundingBoxD);

            Vector3[] corners = new Vector3[8];

            for (int a = 0; a < grids.Count; a++)
            {
                IMyCubeGrid grid = grids[a];

                BoundingBox localBB = grid.LocalAABB;
                MatrixD toGridLocal = grid.WorldMatrixInvScaled;

                for (int b = 0; b < grids.Count; b++)
                {
                    if (a == b)
                        continue;

                    IMyCubeGrid otherGrid = grids[b];

                    otherGrid.LocalAABB.GetCorners(corners);
                    MatrixD wm = otherGrid.WorldMatrix;

                    for (int c = 0; c < corners.Length; c++)
                    {
                        Vector3D cornerWorld = Vector3D.Transform(corners[c], ref wm);
                        Vector3 cornerGridLocal = (Vector3)Vector3D.Transform(cornerWorld, ref toGridLocal);
                        localBB = localBB.Include(ref cornerGridLocal);
                    }
                }

                var obb = new MyOrientedBoundingBoxD(localBB, grid.WorldMatrix);

                DebugDraw.DrawOBB(obb, Utils.GetIndexColor(a, grids.Count) * 0.3f, MySimpleObjectRasterizer.Solid, BlendTypeEnum.PostPP, extraSeeThrough: false);

                float volume = localBB.Volume();

                if (smallestVolume > volume)
                {
                    smallestVolume = volume;
                    smallestOBB = obb;
                }
            }

            DrawOBB(smallestOBB, Color.Lime, MySimpleObjectRasterizer.Wireframe, BlendTypeEnum.PostPP, extraSeeThrough: false);
        }
        */


        /*
        public static void DrawOBB(MyOrientedBoundingBoxD obb, Color color, MySimpleObjectRasterizer draw = MySimpleObjectRasterizer.SolidAndWireframe, BlendTypeEnum blend = BlendTypeEnum.PostPP, bool extraSeeThrough = true)
        {
            MatrixD wm = MatrixD.CreateFromQuaternion(obb.Orientation);
            wm.Translation = obb.Center;

            BoundingBoxD localBB = new BoundingBoxD(-obb.HalfExtent, obb.HalfExtent);

            MySimpleObjectDraw.DrawTransparentBox(ref wm, ref localBB, ref color, draw, 1, faceMaterial: MaterialSquare, lineMaterial: MaterialSquare, blendType: blend);

            if (extraSeeThrough)
                DrawOBB(obb, color, draw, BlendTypeEnum.AdditiveTop, extraSeeThrough: false);
        }
        */

        /*
            why is it extended that way?
            if you want to manually extend it towards cockpit forward then simply Include() a point forward of said cockpit 😄
            no need to care which axis is cockpit towards, the transforms will do that
            Vector3D offsetWorld = cockpit.WorldMatrix.Translation + cockpit.WorldMatrix.Forward * 100;
            Vector3 offsetGridLocal = (Vector3)Vector3D.Transform(cornerWorld, ref toGridLocal);
            localBB = localBB.Include(ref offsetGridLocal);
            only really needed at the very end to modify the smallestOBB though
            probably easier to re-create it like: 
            var bb = new BoundingBoxD(-obb.HalfExtent, obb.HalfExtent);

            //bb.Include(...); // the snippet from above here

            obb = new MyOrientedBoundingBoxD(obb.Center, bb.HalfExtents, obb.Orientation); 
        */

        /*
        
            MyPhysicsHelper.InitModelPhysics(ent.chunk, RigidBodyFlag.RBF_STATIC, 15);
            ent.chunk.Physics.Enabled = true;
            ent.chunk.Physics.Activate();
         
        */


        #endregion



    }
}