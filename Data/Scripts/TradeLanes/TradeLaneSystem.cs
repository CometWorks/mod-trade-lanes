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
using System.Collections;
using ProtoBuf.Meta;
using VRage;
using VRage.Input;

// NOT ALL FEATRUES ARE IMPLEMENTED YET OR FULLY FLESHED OUT

namespace Psycho.TradeLanes
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class TradeLaneSystem : MySessionComponentBase
    {
        public static TradeLaneSystem Instance;

        public enum MovementState
        {
            Normal,
            Booster,
            Cruising,
            Superluminal,
            FTL,
            Warp,
            Jump,
            PhaseShift,
            FrameShift,
            Plaid
        }

        string SubtypeId = "TradeLaneComputer";

        //Whale Oil | Dark Matter
        //Dark Matter Engine and Dark Matter Accelerator

        public readonly string Plaid = "Ludicrous Mode for acceleration beyond its Insane Mode > Plaid Overtop Ludicrous Speed";

        public readonly float NormalSpeed = 80;
        public readonly float BoosterMaxSpeed = 300;
        public readonly float CruisingSpeed = 300;
        public readonly string FTL = "LOL";
        public readonly string Warp = "LMAO";
        public readonly string Jump = "ROFL";
        public readonly string PhaseShift = "Huh?";
        public readonly string FrameShift = "HEH?";
        public readonly string DMA = "HEH?";
        public readonly float SuperluminalSpeed = 10000;
        public readonly float LargeGridMaxSpeed = 25000;
        public readonly float SmallGridMaxSpeed = 25000;

        // DEFAULT
        public float LargeShipMaxSpeed = 100f;
        public float SmallShipMaxSpeed = 100f;

        int updateAtFrame = 0;

        private class GpsWithDistance
        {
            public IMyGps Gps { get; set; }
            public double Distance { get; set; }
        }

        public override void LoadData()
        {
            //MyAPIGateway.Utilities.ShowMessage("TradeLanes|MOD", "Loading...");
            /*
            if (!MyAPIGateway.Session.IsServer || MyAPIGateway.Utilities.IsDedicated)
                return;
            */
            //base.LoadData();

            Instance = this;

            var environmentDefinition = MyDefinitionManager.Static.EnvironmentDefinition;

            if (environmentDefinition != null)
            {
                //MyAPIGateway.Utilities.ShowMessage("TradeLanes|MOD", "Setting up max ship speeds");
                // READ FROM DEFINITION
                LargeShipMaxSpeed = environmentDefinition.LargeShipMaxSpeed;
                SmallShipMaxSpeed = environmentDefinition.SmallShipMaxSpeed;

                // SET OUR OWN VALUES AND USE OUR LOGIC TO RESTRICT SPEEDS
                environmentDefinition.LargeShipMaxSpeed = LargeGridMaxSpeed;
                environmentDefinition.SmallShipMaxSpeed = SmallGridMaxSpeed;

                // DEBUG
                //environmentDefinition.LargeShipMaxSpeed = 300;
                //environmentDefinition.SmallShipMaxSpeed = 300;
            }
            //MyAPIGateway.Utilities.ShowMessage("TradeLanes|MOD", "No, that's literally the only thing, moving on!");

            //MyAPIGateway.Utilities.ShowMessage("TradeLanes|MOD", "Started");
            //MyAPIGateway.Utilities.ShowMessage("TradeLanes|MOD", "Running...");

            SetUpdateOrder(MyUpdateOrder.AfterSimulation);

            TradeLaneNetwork.Load();
            LaneRegistry.Load();

            // crahses!!
            /*
            var existingGps = MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId);
            if (existingGps != null)
            {
                foreach (var gps in existingGps)
                {
                    if (gps.Name.Contains("Trade Lane"))
                    {
                        if (gps.Description.Contains("Trade Lane Network") || gps.Description.Contains("Trade Lane Information"))
                            MyAPIGateway.Session.GPS.RemoveGps(MyAPIGateway.Session.Player.IdentityId, gps);
                    }
                }
            }
            */
        }

        public override void BeforeStart()
        {
            if (TradeLaneNetwork.IsServer)
                CustomGridLogic.RegisterDamageHandler();
        }

        protected override void UnloadData()
        {
            //base.UnloadData();
            TradeLaneNetwork.Unload();
            LaneRegistry.Unload();
            WarheadLogic.Unload();
            CustomGridLogic.Unload();
            Instance = null;
        }

        bool DoOnce = false;

        public override void UpdateAfterSimulation()
        {
            TradeLaneNetwork.Update();
            if (TradeLaneNetwork.IsServer)
            {
                LaneRegistry.Update();
                CustomGridLogic.UpdateSettling();
            }

            if (!DoOnce)
            {
                
            }

            if (++updateAtFrame >= 300)
            {
                if (MyAPIGateway.Session?.Player?.Character == null)
                    return;

                var gpsList = MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId);

                var gpsWithDistances = new List<GpsWithDistance>();

                foreach (var gps in gpsList)
                {
                    if (gps.Name.Contains("Trade Lane") && gps.Description.Contains("Trade Lane Network"))
                    {
                        Vector3D characterLocation = MyAPIGateway.Session.Player.Character.WorldMatrix.Translation;
                        double distance = Vector3D.Distance(characterLocation, gps.Coords);
                        gpsWithDistances.Add(new GpsWithDistance { Gps = gps, Distance = distance });
                    }
                }

                gpsWithDistances.Sort((a, b) => a.Distance.CompareTo(b.Distance));

                for (int i = 0; i < gpsWithDistances.Count; i++)
                {
                    var gpsWithDistance = gpsWithDistances[i];
                    if (gpsWithDistance.Gps.Name.Contains(". Trade Lane"))
                    {
                        string result = (i + 1) + ". " + gpsWithDistance.Gps.Name.Substring(gpsWithDistance.Gps.Name.IndexOf('.') + 1).Trim();
                        if (gpsWithDistance.Gps.Name != result)
                        {
                            gpsWithDistance.Gps.Name = result;
                            MyAPIGateway.Session.GPS.ModifyGps(MyAPIGateway.Session.Player.IdentityId, gpsWithDistance.Gps);
                        }
                    }
                    else
                    {
                        string result = (i + 1) + ". " + gpsWithDistance.Gps.Name;
                        if (gpsWithDistance.Gps.Name != result)
                        {
                            gpsWithDistance.Gps.Name = result;
                            MyAPIGateway.Session.GPS.ModifyGps(MyAPIGateway.Session.Player.IdentityId, gpsWithDistance.Gps);
                        }
                    }
                }

                /*
                var matchingBlocks = new List<IMyTerminalBlock>();

                // Get all entities in the game world
                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities);

                foreach (var entity in entities)
                {
                    // Check if the entity is a grid
                    if (entity is IMyCubeGrid grid)
                    {
                        // Get all blocks on the grid
                        var blocks = new List<IMySlimBlock>();
                        grid.GetBlocks(blocks);

                        foreach (var block in blocks)
                        {
                            // Check if the block is a terminal block and matches the subtype ID
                            if (block.FatBlock is IMyTerminalBlock terminalBlock && terminalBlock.BlockDefinition.SubtypeId == SubtypeId)
                            {
                                matchingBlocks.Add(terminalBlock);
                            }
                        }
                    }
                }
                */

                updateAtFrame = 0;
            }
        }
    }
}

/*

namespace Psycho.TradeLanes
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class PlayerInteractionHandler : MySessionComponentBase
    {
        public static PlayerInteractionHandler Instance;
    }
}

*/

/*
namespace TugMod
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_JumpDrive), false, "DHI_TJD_LG", "DHI_TJD_SG", "DHI_CJD")]
    public class TJD : MyGameLogicComponent
    {
        IMyCubeBlock tugBlock = null;

        // CAN DELETE IF MY SOLUTION WORKS
        int RotationDirection = 0;
        int TranslationDirection = 0;
        int AnimationLoop = 0;

        // DO NOT TOUCH!
        public class BlocksData
        {
            public string subpartName = "";
            public string animAxis = "X";
            public MyEntitySubpart subpart = null;
            public float rotationSpeed = 0;
            public int rotationDir = 1;
        }

        List<BlocksData> blocksData = new List<BlocksData>();

        // I DUNNO WHAT THESE LETTERS DO! T.T
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            try
            {
                tugBlock = Entity as IMyCubeBlock;

                if (tugBlock == null)
                    return;

                NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            }
            catch (Exception e)
            {
                MyVisualScriptLogicProvider.ShowNotificationToAll("Init Error" + e, 10000, "Red");
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                if (tugBlock.CubeGrid?.Physics == null)
                    return;

                string subtypeId = tugBlock.BlockDefinition.SubtypeId;

                if (string.IsNullOrEmpty(subtypeId))
                    return;

                switch (subtypeId)
                {
                    case "DHI_TJD_SG":
                    case "DHI_TJD_LG":                  // Subtype ID of the block the settings appy to.
                        blocksData.Add(new BlocksData
                        {
                            subpartName = "Fan",        // Subpart name.
                            animAxis = "Z",             // Rotation Axis.
                            rotationSpeed = 0.1f,         // Rotation Speed.
                            rotationDir = -1            // Rotation Direction.
                        });
                        blocksData.Add(new BlocksData
                        {
                            subpartName = "Ring1",
                            animAxis = "Z",
                            rotationSpeed = -0.08f,
                            rotationDir = -1
                        });
                        blocksData.Add(new BlocksData
                        {
                            subpartName = "Ring2",
                            animAxis = "Y",
                            rotationSpeed = -0.08f,
                            rotationDir = -1
                        });
                        break;
                }

                foreach (var thing in blocksData)
                {
                    thing.subpart = tugBlock.GetSubpart(thing.subpartName);
                }

                NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
            }
            catch (Exception e)
            {
                MyVisualScriptLogicProvider.ShowNotificationToAll("Update Once" + e, 2500, "Red");
            }
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                if (tugBlock.IsWorking)
                {
                    Animate();

                    //MoveCube();
                }
            }
            catch (Exception e)
            {
                MyVisualScriptLogicProvider.ShowNotificationToAll("Update Error" + e, 2500, "Red");
            }
        }

        private void Animate()
        {
            try
            {
                foreach (var thing in blocksData)
                {
                    var FanRotation = thing.rotationSpeed;
                    var FanSubpart = thing.subpart;
                    var FanInitialMatrix = FanSubpart.PositionComp.LocalMatrix;

                    var FanRotationMatrix = tugBlock.WorldMatrix;

                    switch (thing.animAxis)
                    {
                        case "X":
                            FanRotationMatrix = (MatrixD.CreateRotationX(FanRotation * thing.rotationDir));
                            break;
                        case "Y":
                            FanRotationMatrix = (MatrixD.CreateRotationY(FanRotation * thing.rotationDir));
                            break;
                        case "Z":
                            FanRotationMatrix = (MatrixD.CreateRotationZ(FanRotation * thing.rotationDir));
                            break;
                    }

                    var FanAnimationMatrix = FanRotationMatrix * FanInitialMatrix;
                    // You spin my head round
                    // baby right round
                    // when you go super critical
                    // when you go critical, critical...
                    // (Get it?? It has spinny things? And is a reactor? with a core? That can go cri... ah nvm).
                    FanSubpart.PositionComp.LocalMatrix = FanAnimationMatrix;
                }
            }
            catch (Exception e)
            {
                MyVisualScriptLogicProvider.ShowNotificationToAll("Update Error" + e, 2500, "Red");
            }
        }

        // DELETE IF MY SOLUTION WORKS
        public void MoveCube()
        {
            try
            {
                if (AnimationLoop == 400)
                {
                    AnimationLoop = 0;
                }
                if (AnimationLoop == 0)
                {
                    RotationDirection = -1;
                    TranslationDirection = -1;
                }
                if (AnimationLoop == 200)
                {
                    RotationDirection = -1;
                    TranslationDirection = 1;
                }



                //Outer Shell
                var FanRotation = 0.1f; //Speed
                var FanSubpart = tugBlock.GetSubpart("Fan");
                var FanInitialMatrix = FanSubpart.PositionComp.LocalMatrix;
                var FanRotationMatrix = (MatrixD.CreateRotationZ(FanRotation * RotationDirection));
                var FanAnimationMatrix = FanRotationMatrix * FanInitialMatrix;
                FanSubpart.PositionComp.LocalMatrix = FanAnimationMatrix;



                //Outer Shell
                var outerShellRotation = -0.08f; //Speed
                var outerShellSubpart = tugBlock.GetSubpart("Ring1");
                var outerShellInitialMatrix = outerShellSubpart.PositionComp.LocalMatrix;
                var outerShellRotationMatrix = (MatrixD.CreateRotationZ(outerShellRotation * RotationDirection));
                var outerShellAnimationMatrix = outerShellRotationMatrix * outerShellInitialMatrix;
                outerShellSubpart.PositionComp.LocalMatrix = outerShellAnimationMatrix;

                //Inner Ball
                var innerBallRotation = -0.08;
                var innerBallSubpart = tugBlock.GetSubpart("Ring2");
                var innerBallInitialMatrix = innerBallSubpart.PositionComp.LocalMatrix;
                var innerBallRotationMatrix = (MatrixD.CreateRotationY(innerBallRotation * RotationDirection));
                var innerBallAnimationShellMatrix = innerBallRotationMatrix * innerBallInitialMatrix;

                innerBallSubpart.PositionComp.LocalMatrix = innerBallAnimationShellMatrix;
                AnimationLoop++;

                //all 3 axces
                //var rotationMatrix = (MatrixD.CreateRotationY(rotation * TranslationTimeHead) * MatrixD.CreateRotationX(rotation * TranslationTimeHead) * MatrixD.CreateRotationZ(rotation * TranslationTimeHead));
            }

            catch (Exception e)
            {
                MyVisualScriptLogicProvider.ShowNotificationToAll("Update Error" + e, 2500, "Red");
            }
        }

    }
}
*/