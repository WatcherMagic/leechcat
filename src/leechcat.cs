#define DEVELOPMENT_BUILD

using System.Runtime.CompilerServices;
using BepInEx;
using MonoMod.Cil;
using RWCustom;
using UnityEngine;
using Random = UnityEngine.Random;

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
            LoadManualHooks();
            
            On.Player.LungUpdate += LeechCatLungs;
            On.Player.Grabability += LeechCatGrabability;
            On.Player.IsCreatureLegalToHoldWithoutStun += LeechCatCreatureHoldWithoutStun;
            On.Player.GrabUpdate += LeechCatGrabUpdate;
            On.Player.Grabbed += LeechCatEscapeGrab;

            On.AirBreatherCreature.Update += LeechCatAirBreatherDrainUpdate;
            //IL.AirBreatherCreature.Update += LeechCatAirBreatherILUpdate;
            
            On.Leech.Attached += LeechLetGoOfLeechCat;
        }

        private void LoadManualHooks()
        {
            //
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

                            //this check doesn't work properly!
                            if (grabbedCreature is AirBreatherCreature)
                            {
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
        
        private void LeechCatAirBreatherDrainUpdate(On.AirBreatherCreature.orig_Update orig, AirBreatherCreature self, bool eu)
        {
            Logger.LogInfo("Draining air breather creature! Lungs are at " + self.lungs);
            UnityEngine.Debug.Log("LeechCat: Draining air breather creature! Lungs: " + self.lungs);
            
            if (creatureBeingDrainedTable.GetOrCreateValue(self).beingDrained)
            {
                if (self.Submersion == 1f)
                {
                    self.lungs = Mathf.Max(-1f, self.lungs - 0.5f / self.Template.lungCapacity);
                }
                else
                {
                    //0.33333335f is the rate at which all creature's lungs refill regardless of capacity
                    float normalizedCapacity = 1f / self.Template.lungCapacity;
                    float fillRate = 0.33333335f; //found in AirBreatherCreature.Update
                    float netDrain = 0.5f;
                    float drainRate = fillRate + netDrain;
                    float scaledDrainRate = drainRate / normalizedCapacity;
                    self.lungs = Mathf.Max(-1f, self.lungs - scaledDrainRate);
                }
                
                //LOGS:
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 1
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 1
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 1
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 1
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 1
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 1
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 1
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 1
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 1
                // key pressed & held
                // [Info   :  Leechcat] Draining MoreSlugcats.Yeek
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 1
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0
                // ...
                // key released
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.03333334
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.06666667
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.1
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.1333333
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.1666667
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.2
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.2333333
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.2666667
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.3
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.3333333
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.3666667
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.4
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.4333333
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.4666667
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.5
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.5333334
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.5666667
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.6000001
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.6333334
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.6666668
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.7000002
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.7333335
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.7666669
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.8000003
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.8333336
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.866667
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.9000003
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.9333337
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.9666671
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 1
                // key pressed & held
                // [Info   :  Leechcat] Draining MoreSlugcats.Yeek
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 1
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0
                // ...
                // key released
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.03333334
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.06666667
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.1
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.1333333
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.1666667
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.2
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.2333333
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.2666667
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.3
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.3333333
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.3666667
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.4
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.4333333
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.4666667
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.5
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.5333334
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.5666667
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.6000001
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.6333334
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.6666668
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.7000002
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.7333335
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.7666669
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.8000003
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.8333336
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.866667
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.9000003
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.9333337
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 0.9666671
                // [Info   :  Leechcat] Draining air breather creature! Lungs are at 1
                // ...

                
            //     if (self.abstractCreature.realizedCreature != null)
            //     {
            //         Logger.LogInfo("Check for realized creature passed!");
            //         self.abstractCreature.realizedCreature.Update(eu);
            //     }
            //     else
            //     {
            //         Logger.LogInfo("Check for realised creature failed!");
            //         orig(self, eu);
            //     }
            //     
            //     Logger.LogInfo("Draining air breather creature! Lungs are at " + self.lungs);
            //     UnityEngine.Debug.Log("LeechCat: Draining air breather creature! Lungs: " + self.lungs);
            //     
            //     //copied from AirBreatherCreature.Update() with slight modifications
            //     self.lungs = Mathf.Max(-1f, self.lungs - 1f / self.Template.lungCapacity);
            //     if (self.lungs < 0.3f)
            //     {
            //         if (self.Submersion == 1f &&
            //             Random.value <
            //             Mathf.Sin(Mathf.InverseLerp(0.3f, -0.3f, self.lungs) * 3.1415927f) * 0.5f)
            //         {
            //             Logger.LogInfo("Bubbles!");
            //             UnityEngine.Debug.Log("LeechCat: Bubbles!");
            //             //self.room.AddObject(new Bubble(self.mainBodyChunk.pos, (Custom.RNV() * Random.value) * 6f, false, false));
            //         }
            //         if (Random.value < 0.025f)
            //         {
            //             self.LoseAllGrasps();
            //         }
            //         for (int i = 0; i < self.bodyChunks.Length; i++)
            //         {
            //             BodyChunk bodyChunk = self.bodyChunks[i];
            //             bodyChunk.vel = bodyChunk.vel 
            //                             + ((((Custom.RNV() * self.bodyChunks[i].rad) * 0.4f) * Random.value) 
            //                                * Mathf.Sin(Mathf.InverseLerp(0.3f, -0.3f, self.lungs) * 3.1415927f)) 
            //                             + (((Custom.DegToVec(Mathf.Lerp(-30f, 30f, Random.value)) * Random.value) 
            //                                 * (i == self.mainBodyChunkIndex ? 0.4f : 0.2f)) * Mathf.Pow(Mathf.Sin(Mathf.InverseLerp(0.3f, -0.3f, self.lungs) * 3.1415927f), 2f));
            //         }
            //         if (self.lungs < -0.5f && Random.value < 1f / Custom.LerpMap(self.lungs, -0.5f, -1f, 90f, 30f))
            //         {
            //             self.Die();
            //         }
            //     }
            //     
            }
            // else
            // {
            orig(self, eu);
            // }
        }
        
        private void LeechCatAirBreatherILUpdate(ILContext il)
        {
            // try
            // {
            //     ILCursor c = new ILCursor(il);
            //     c.GotoNext(MoveType.Before,
            //         c => c.MatchLdarg(0),
            //         c => c.MatchCallOrCallvirt<Room>(typeof(Room).GetField(nameof(UpdatableAndDeletable.room)).ToString())
            //     );
            //
            //
            //     c.EmitDelegate(() =>
            //     {
            //         if (creatureBeingDrainedTable.GetOrCreateValue())
            //         {
            //             return true;
            //         }
            //         return false;
            //     });
            // }
            // catch (Exception e)
            // {
            //     UnityEngine.Debug.LogException(e);
            // }
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