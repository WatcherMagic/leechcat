#define DEVELOPMENT_BUILD

using System;
using System.Runtime.CompilerServices;
using BepInEx;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using UnityEngine;

namespace SlugTemplate
{
    [BepInPlugin(MOD_ID, "Leechcat", "0.1.0")]
    class leechcat : BaseUnityPlugin
    {
        private const string MOD_ID = "leechcat";

        private int _drainKeyHeldCounter = 0;
        private const int DRAIN_KEY_HELD_THRESHOLD = 20;
        
        public ConditionalWeakTable<Creature, CustomAirBreatherCreatureData> creatureBeingDrainedTable = new ();
        
        public void OnEnable()
        {
            On.Player.LungUpdate += LeechCatLungs;
            On.Player.Grabability += LeechCatGrabability;
            On.Player.IsCreatureLegalToHoldWithoutStun += LeechCatCreatureHoldWithoutStun;
            On.Player.GrabUpdate += LeechCatGrabUpdate;
            On.Player.Grabbed += LeechCatEscapeGrab;

            On.AirBreatherCreature.Update += LeechCatAirBreatherUpdate;
            IL.AirBreatherCreature.Update += LeechCatAirBreatherILUpdate;
            
            On.Leech.Attached += LeechLetGoOfLeechCat;
        }

        private void LeechCatLungs(On.Player.orig_LungUpdate orig, Player self)
        {
            if (self.slugcatStats.name.value == MOD_ID && self.submerged)
            {
                self.airInLungs = 1f;
            }
            else
            {
                orig(self);
            }
        }

        private Player.ObjectGrabability LeechCatGrabability(On.Player.orig_Grabability orig, Player self, PhysicalObject obj)
        {   
            if (self.SlugCatClass.value == MOD_ID)
            {
                if (obj is Creature && !(obj as Creature).Template.smallCreature)
                {
                    if (obj.GetType() == typeof(Player))
                    {
                        return orig(self, obj);
                    }

                    Player.ObjectGrabability checkForTwoHandCreature = orig(self, obj);
                    if (checkForTwoHandCreature != Player.ObjectGrabability.TwoHands)
                    {
                        return Player.ObjectGrabability.Drag;
                    }

                    return checkForTwoHandCreature;
                }
                
                return orig(self, obj);
                
                //add ability to grab leeches and eat them
            }
            
            return orig(self, obj);
        }
        
        private bool LeechCatCreatureHoldWithoutStun(On.Player.orig_IsCreatureLegalToHoldWithoutStun orig, Player self, Creature grabCheck)
        {
            if (self.slugcatStats.name.value == MOD_ID)
            {
                return true;
            }

            return orig(self, grabCheck);
        }

        private bool loggedDrain = false;
        private void LeechCatGrabUpdate(On.Player.orig_GrabUpdate orig, Player self, bool eu)
        {
            if (self.SlugCatClass.value == MOD_ID)
            {
                if (self.grasps[0] != null && self.grasps[0].grabbed != null 
                                           && self.grasps[0].grabbed is Creature
                                           && !(self.grasps[0].grabbed as Creature).dead)
                {
                    Creature grabbedCreature = self.grasps[0].grabbed as Creature;
                    CustomAirBreatherCreatureData customAirData = 
                        creatureBeingDrainedTable.GetOrCreateValue(grabbedCreature);
                    customAirData.beingDrained = false;
                    
                    if (self.input[0].pckp)
                    {
                        _drainKeyHeldCounter++;

                        if (_drainKeyHeldCounter >= DRAIN_KEY_HELD_THRESHOLD)
                        {
                            //drain creature's oxygen and give it to leechcat
                            
                            if (!loggedDrain)
                            {
                                Logger.LogInfo("Draining " + grabbedCreature.GetType());
                                UnityEngine.Debug.Log("LeechCat: Draining " + grabbedCreature.GetType());
                            }

                            if (grabbedCreature is AirBreatherCreature)
                            {
                                //Logger.LogInfo("Detected air breather creature, setting drained to true");
                                //UnityEngine.Debug.Log("LeechCat: Detected air breather creature, setting drained to true");
                                customAirData.beingDrained = true;

                                //     if (!loggedDrain)
                                //     {
                                //         Logger.LogInfo("Passed air breather creature check!");
                                //     }
                                //     
                                //     AirBreatherCreature grabbedCreatureLungs = grabbedCreature as AirBreatherCreature;
                                //     //took the maths from AirBreatherCreature.Update
                                //     grabbedCreatureLungs.lungs =
                                //         Mathf.Max(0f, grabbedCreatureLungs.lungs - 1f / grabbedCreatureLungs.Template.lungCapacity);
                                //     Logger.LogInfo("Draining creature's air! Creature lungs is " +
                                //                    grabbedCreatureLungs.lungs);
                            }
                            else
                            {
                                DrainNonAirBreatherCreature(grabbedCreature);
                            }
                            //grabbedCreature.Template.lungCapacity
                            
                            
                            loggedDrain = true;
                        }
                    }
                    else if (!self.input[0].pckp && self.input[1].pckp)
                    {
                        loggedDrain = false;
                        _drainKeyHeldCounter = 0;
                        customAirData.beingDrained = false;
                    }
                }
            
                orig(self, eu);
            }
            else
            {
                orig(self, eu);
            }
        }

        private void DrainNonAirBreatherCreature(Creature creatureToDrain)
        {
            if (creatureToDrain.State is HealthState)
            {   
                /*I'm not entirely sure how this works cause CreatureState doesn't seem to
                 inherit from HealthState, but this is how they do it in Creature.Update() so
                 this is what we're doing*/
                HealthState creatureHealth = creatureToDrain.State as HealthState;
                creatureHealth.health -= 0.0015f / creatureToDrain.Template.baseDamageResistance;
            }
        }
        
        private void LeechCatEscapeGrab(On.Player.orig_Grabbed orig, Player self, Creature.Grasp grasp)
        {
            orig(self, grasp);

            if (self.dangerGrasp != null)
            {
                for (int i = 0; i < grasp.grabber.grasps.Length; i++)
                {
                    if (grasp.grabber.grasps[i].grabbed == self && !self.dead)
                    {
                        grasp.grabber.ReleaseGrasp(i);
                    }
                }
            }
        }
        
        private void LeechCatAirBreatherUpdate(On.AirBreatherCreature.orig_Update orig, AirBreatherCreature self, bool eu)
        {
            if (creatureBeingDrainedTable.GetOrCreateValue(self).beingDrained)
            {
                Logger.LogInfo("Draining " + self.abstractCreature.GetType() + "'s lungs: " + self.lungs);
                UnityEngine.Debug.Log("LeechCat: Draining " + self.abstractCreature.GetType() + "'s lungs: " + self.lungs);
                //self.lungs = Mathf.Max(-1f, self.lungs - 1f / self.Template.lungCapacity);
            }
            else if (self.lungs != 1f)
            {
                Logger.LogInfo(self.abstractCreature.GetType() + "'s lungs: " + self.lungs);
                UnityEngine.Debug.Log(self.abstractCreature.GetType() + "'s lungs: " + self.lungs);
            }
            
            orig(self, eu);
        }
        
        private void LeechCatAirBreatherILUpdate(ILContext il)
        {
            try
            {
                ILCursor c = new ILCursor(il);
                
                //Confirmed
                c.GotoNext(MoveType.Before,
                    c => c.MatchLdarg(0),
                    c => c.MatchLdarg(0),
                    c => c.MatchLdfld(typeof(AirBreatherCreature).GetField(nameof(AirBreatherCreature.lungs))),
                    c => c.MatchLdcR4(0.033333335f),
                    c => c.MatchAdd());
                c.MoveAfterLabels();
                ILLabel matchBrFalse = c.MarkLabel();
                Logger.LogInfo("\nReached refill lungs point: " + c.ToString());
                c.EmitDelegate(() =>
                {
                    Logger.LogInfo("Reached refill lungs equation");
                });

                //Confirmed
                c.GotoNext(MoveType.Before,
                    c => c.MatchLdarg(0),
                    c => c.MatchLdcR4(-1),
                    c => c.MatchLdarg(0),
                    c => c.MatchLdfld<AirBreatherCreature>(nameof(AirBreatherCreature.lungs)));
                c.MoveAfterLabels();
                ILLabel drainingTrueJumpPoint = c.MarkLabel();
                Logger.LogInfo("\nReached draining jump point: " + c.ToString());

                ILLabel jumpPoint = null;
                c.GotoPrev(MoveType.Before,
                    c => c.MatchLdarg(0),
                    c => c.MatchLdfld<UpdatableAndDeletable>(nameof(UpdatableAndDeletable.room)),
                    c => c.MatchBrfalse(out jumpPoint));
                Logger.LogInfo("JumpPoint target: " + jumpPoint.Target);
                Logger.LogInfo("JumpPoint branch: " + jumpPoint.Branches);
                Logger.LogInfo("matchBrFalse target: " + matchBrFalse.Target);
                Logger.LogInfo("matchBrFalse branch: " + matchBrFalse.Branches);
                if (jumpPoint != null && jumpPoint.Target == matchBrFalse.Target)
                {
                    Logger.LogInfo("\nReached draining insertion point: " + c.ToString());
                    c.MoveAfterLabels();
                    c.Emit(OpCodes.Ldarg, 0); //load self (AirBreatherCreature)
                    c.EmitDelegate<Func<AirBreatherCreature, bool>>((AirBreatherCreature creature) =>
                    {
                        if (creatureBeingDrainedTable.GetOrCreateValue(creature).beingDrained)
                        {
                            Logger.LogInfo("IL beingDrained check returned true!");
                            return true;
                        }
                        Logger.LogInfo("IL beingDrained check returned false!");
                        return false;
                    });
                    c.Emit(OpCodes.Brtrue, drainingTrueJumpPoint);
                }
                else
                {
                    Logger.LogError("Found the wrong insertion point for skipping lung refill if being drained: " + c.ToString() + "Fill lungs skip will not be emitted!");
                }

                // c.GotoNext(MoveType.Before,
                //     c => c.MatchLdarg(0),
                //     c => c.MatchLdcR4(-1),
                //     c => c.MatchLdarg(0),
                //     c => c.MatchLdfld<AirBreatherCreature>(nameof(AirBreatherCreature.lungs)),
                //     c => c.MatchLdcR4(1)); //,
                // //     c => c.MatchLdarg(0),
                // //     c => c.MatchCallOrCallvirt<CreatureTemplate>(nameof(Creature.Template)),
                // //     c => c.MatchLdfld<CreatureTemplate>(nameof(CreatureTemplate.lungCapacity)),
                // //     c => c.MatchDiv(),
                // //     c => c.MatchSub(),
                // //     c => c.MatchCallOrCallvirt<Mathf>(nameof(Mathf.Max))); //,
                // //     // c => c.MatchStfld<AirBreatherCreature>(nameof(AirBreatherCreature.lungs)));
                // c.MoveAfterLabels();
                // ILLabel drowningLogicPoint = c.MarkLabel();
                // c.EmitDelegate(() =>
                // {
                //     Logger.LogInfo("Reached lung drain equation");
                // });
                //
                // c.GotoLabel(checkDrainInsertPoint);
                //  c.Emit(OpCodes.Ldarg, 0); //load self (AirBreatherCreature)
                //  c.EmitDelegate((AirBreatherCreature creature) =>
                //  {
                //      if (creatureBeingDrainedTable.GetOrCreateValue(creature).beingDrained)
                //      {
                //          Logger.LogInfo("IL beingDrained check returned true!");
                //          return true;
                //      }
                //      Logger.LogInfo("IL beingDrained check returned false!");
                //      return false;
                //  });
                // c.Emit(OpCodes.Brtrue, drowningLogicPoint);

                Logger.LogInfo(il.ToString());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Logger.LogError("Exception encountered in IL hook to AirBreatherCreature.Update: " 
                                + e.GetType() + ": " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        private void LeechLetGoOfLeechCat(On.Leech.orig_Attached orig, Leech self)
        {
            BodyChunk grabbedChunk = self.grasps[0].grabbed.bodyChunks[self.grasps[0].chunkGrabbed];
            if (grabbedChunk.owner is Player && (grabbedChunk.owner as Player).SlugCatClass.value == MOD_ID)
            {
                self.LoseAllGrasps();
            }
            UnityEngine.Debug.Log("Leech let go of leechcat!");
            
            orig(self);
        }
    }
}