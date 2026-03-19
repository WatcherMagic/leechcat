#define DEVELOPMENT_BUILD

using System;
using System.Runtime.CompilerServices;
using BepInEx;
using Mono.Cecil.Cil;
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
        private bool isDrainingCreature = false;
        private float maxLatchDistance = 100;
        private int canDelatchCounter = 0;
        private const int TIME_UNTIL_CAN_DELATCH = 20;
        // private float? latchOffsetX = null;
        // private float? latchOffsetY = null;
        private BodyChunk latchedChunk = null;
        private float? effectiveLatchRange = null;
        private bool spritesInFrontWhileLatched = false;
        private bool setInitialLatch = false;
        private float[] initialChunkMasses;
        
        private static leechcat _pluginInstance;
        public static BepInEx.Logging.ManualLogSource LeechcatLogger => _pluginInstance.Logger;
        
        public ConditionalWeakTable<Creature, CustomLeechCatVariables> CreatureBeingDrainedTable = new ();
        
        public void OnEnable()
        {
            _pluginInstance = this;
            LeechcatEnums.PlayerBodyModeIndex.RegisterValues();
            
            On.Player.LungUpdate += LeechCatLungs;
            On.Player.Update += LeechCatLatch;
            IL.Player.Update += LeechCatLatchIL;
            On.Player.Grabability += LeechCatGrabability;
            On.Player.IsCreatureLegalToHoldWithoutStun += LeechCatCreatureHoldWithoutStun;
            On.Player.IsCreatureImmuneToPlayerGrabStun += LeechCatDoesntStunCreatureOnGrab;
            On.Player.GrabUpdate += LeechCatGrabUpdate;
            On.Player.Grabbed += LeechCatEscapeGrab;

            On.AirBreatherCreature.Update += LeechCatAirBreatherUpdate;

            On.Leech.ConsiderOtherCreature += LeechIgnoreLeechcat;
        }

        private void OnDisable()
        {
            LeechcatEnums.PlayerBodyModeIndex.UnregisterValues();
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

        private bool loggedLatch = false;
        private void LeechCatLatch(On.Player.orig_Update orig, Player self, bool eu)
        {
            // if (self.slugcatStats.name.value == MOD_ID 
            //     && self.bodyMode != LeechcatEnums.PlayerBodyModeIndex.LeechcatLatched
            //     && self.input[0].pckp && self.input[0].jmp)
            // {
            //     UnityEngine.Debug.Log("Leechcat: Detected attempt to latch!");
            //     Logger.LogInfo("Detected attempt to latch!");
            //     Vector2 leechcatPos = self.bodyChunks[0].pos;
            //     float slugcatChunkRad = self.bodyChunks[0].rad;
            //     BodyChunk closestChunk = null;
            //     float closestDistance = 999999999999f;
            //     
            //     foreach (AbstractCreature crit in self.room.abstractRoom.creatures)
            //     {
            //         if (crit.realizedCreature != null)
            //         {
            //             foreach (BodyChunk chunk in crit.realizedCreature.bodyChunks)
            //             {
            //                 if (chunk.owner == self)
            //                 {
            //                     //Logger.LogInfo("Found own chunk: " + chunk.owner);
            //                     continue;
            //                 }
            //                 
            //                 float sizeFactor = Mathf.Clamp(chunk.rad / slugcatChunkRad, 0.8f, 1.5f);
            //                 float effectiveLatchRange = maxLatchDistance * sizeFactor;
            //                 float distance = (self.bodyChunks[0].pos - chunk.pos).magnitude;
            //                 
            //                 if (distance < closestDistance && distance <= effectiveLatchRange)
            //                 {
            //                     closestDistance = distance;
            //                     closestChunk = chunk;
            //                 }
            //             }
            //         }
            //     }
            //     
            //     if (closestChunk != null)
            //     {
            //         UnityEngine.Debug.Log("Leechcat: Found creature chunk in latching range! Chunk owner: " + closestChunk.owner);
            //         Logger.LogInfo("Found creature chunk in latching range! Chunk owner: " + closestChunk.owner);
            //         latchedChunk = closestChunk;
            //         canDelatchCounter = 0;
            //         self.bodyMode = LeechcatEnums.PlayerBodyModeIndex.LeechcatLatched;
            //         self.graphicsModule.BringSpritesToFront();
            //     }
            //     if (self.bodyMode != LeechcatEnums.PlayerBodyModeIndex.LeechcatLatched)
            //     {
            //         UnityEngine.Debug.Log("Leechcat: Couldn't find creature to latch onto!");
            //         Logger.LogInfo("Couldn't find creature to latch onto!");   
            //     }
            // }
            
            bool isLatched = self.bodyMode == LeechcatEnums.PlayerBodyModeIndex.LeechcatLatched;

            orig(self, eu);

            if (isLatched)
            {
                SetLatchedState(self);
            }
            // else if (latchedChunk != null && !CheckIsInsideCreatureChunk(self))
            // {
            //     latchedChunk = null;
            //     self.bodyChunks[0].collideWithObjects = true;
            //     self.bodyChunks[1].collideWithObjects = true;
            // }
        }

        private void SetLatchedState(Player self)
        {
            self.bodyMode = LeechcatEnums.PlayerBodyModeIndex.LeechcatLatched;
            self.bodyChunks[0].collideWithObjects = false;
            self.bodyChunks[1].collideWithObjects = false;

            if (spritesInFrontWhileLatched)
            {
                self.graphicsModule.BringSpritesToFront();
            }
        }

        private bool CheckIsInsideCreatureChunk(Player self)
        {
            foreach (AbstractCreature crit in self.room.abstractRoom.creatures)
            {
                if (crit.realizedCreature != null)
                {
                    foreach (BodyChunk chunk in crit.realizedCreature.bodyChunks)
                    {
                        float combinedSizeChunk1 = chunk.rad + self.bodyChunks[0].rad;
                        float combinedSizeChunk2 = chunk.rad + self.bodyChunks[1].rad;
                        float chunk1Distance = (chunk.pos - self.bodyChunks[0].pos).magnitude;
                        float chunk2Distance = (chunk.pos - self.bodyChunks[1].pos).magnitude;

                        if (chunk1Distance < combinedSizeChunk1 || chunk2Distance < combinedSizeChunk2)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        
        private void LeechCatLatchIL(ILContext il)
        {
            ILCursor c = new ILCursor(il);
            
            try
            {
                //set lastGroundY if leechcat latches onto something
                ILLabel label = null;
                c.GotoNext(MoveType.After,
                    x => x.MatchLdarg(0),
                    x => x.MatchLdfld(typeof(Player), nameof(Player.bodyMode)),
                    x => x.MatchLdsfld(typeof(Player.BodyModeIndex), nameof(Player.BodyModeIndex.Swimming)),
                    x => x.MatchCallOrCallvirt(out _),
                    x => x.MatchBrtrue(out _),
                    
                    x => x.MatchLdarg(0),
                    x => x.MatchLdfld(typeof(Player), nameof(Player.bodyMode)),
                    x => x.MatchLdsfld(typeof(Player.BodyModeIndex), nameof(Player.BodyModeIndex.ClimbingOnBeam)),
                    x => x.MatchCallOrCallvirt(out _),
                    x => x.MatchBrtrue(out _),
                    
                    x => x.MatchLdarg(0),
                    x => x.MatchLdfld(typeof(Player), nameof(Player.bodyMode)),
                    x => x.MatchLdsfld(typeof(Player.BodyModeIndex), nameof(Player.BodyModeIndex.ZeroG)),
                    x => x.MatchCallOrCallvirt(out _),
                    x => x.MatchBrtrue(out label));
                c.MoveAfterLabels();
                c.Emit(OpCodes.Ldarg_0);
                c.EmitDelegate<Func<Player, bool>>(player => player.bodyMode == LeechcatEnums.PlayerBodyModeIndex.LeechcatLatched);
                c.Emit(OpCodes.Brtrue, label);
            }
            catch (Exception e)
            {
                Logger.LogError("Encountered error while trying to emit IL to set lastGroundY on latch: " + e.Message);
            }
            
            try
            {
                //bypass clamp to ground while latched
                ILLabel label = null;
                c.GotoNext(MoveType.After,
                    x => x.MatchLdarg(0),
                    x => x.MatchLdfld(typeof(Player), nameof(Player.bodyMode)),
                    x => x.MatchLdsfld(typeof(Player.BodyModeIndex), nameof(Player.BodyModeIndex.Swimming)),
                    x => x.MatchCallOrCallvirt(out _),
                    x => x.MatchBrfalse(out label));
                c.MoveAfterLabels();
                c.Emit(OpCodes.Ldarg_0);
                c.EmitDelegate<Func<Player, bool>>(player =>
                    player.bodyMode != LeechcatEnums.PlayerBodyModeIndex.LeechcatLatched);
                c.Emit(OpCodes.Brfalse, label);
            }
            catch (Exception e)
            {
                Logger.LogError("Encountered error while trying to emit IL to bypass ground clamp while latched: " + e.Message);
            }
        }
        
        private Player.ObjectGrabability LeechCatGrabability(On.Player.orig_Grabability orig, Player self, PhysicalObject obj)
        {
            if (self.slugcatStats.name.value == MOD_ID)
            {
                if (obj is Creature && obj != self)
                {
                    //Logger.LogInfo("Can drag " + (obj as Creature).abstractCreature.creatureTemplate.name);
                    return Player.ObjectGrabability.Drag;
                }
                
                return Player.ObjectGrabability.CantGrab;
            }
            return orig(self, obj);
        }
        
        private bool LeechCatCreatureHoldWithoutStun(On.Player.orig_IsCreatureLegalToHoldWithoutStun orig, Player self, Creature grabCheck)
        {
            if (self.slugcatStats.name.value == MOD_ID)
            {
                //Logger.LogInfo(grabCheck.abstractCreature.creatureTemplate.name + " is legal to hold without stun!");
                return true;
            }

            return orig(self, grabCheck);
        }

        private bool LeechCatDoesntStunCreatureOnGrab(On.Player.orig_IsCreatureImmuneToPlayerGrabStun orig, Player self, Creature grabCheck)
        {
            if (self.slugcatStats.name.value == MOD_ID) 
            {
                return true;
            }
            
            return orig(self, grabCheck);
        }
        
        private void LeechCatGrabUpdate(On.Player.orig_GrabUpdate orig, Player self, bool eu)
        {
            if (self.SlugCatClass.value == MOD_ID)
            {
                if (self.grasps[0] != null && self.grasps[0].grabbed != null 
                                           && self.grasps[0].grabbed is Creature)
                {
                    Creature latched = self.grasps[0].grabbed as Creature;
                    if (!setInitialLatch)
                    {
                        Logger.LogInfo("Initial latch onto " + latched.Template.name);
                        UnityEngine.Debug.Log("Leechcat: Initial latch onto " + latched.Template.name);
                        if (latched is Fly)
                        {
                            spritesInFrontWhileLatched = false;
                        }
                        else
                        {
                            spritesInFrontWhileLatched = true;
                        }
                        SetLatchedState(self);
                        
                        
                        //find closest creature chunk
                        Vector2 leechcatPos = self.bodyChunks[0].pos;
                        float slugcatChunkRad = self.bodyChunks[0].rad;
                        BodyChunk closestChunk = null;
                        float closestDistance = 999999999999f;

                        if (latched.abstractCreature.realizedCreature == null)
                        {
                            Logger.LogInfo("Latch failed on trying to grab null creature!");
                            return;
                        }
                        foreach (BodyChunk chunk in latched.abstractCreature.realizedCreature.bodyChunks)
                        {
                            if (chunk.owner == self)
                            {
                                //Logger.LogInfo("Found own chunk: " + chunk.owner);
                                continue;
                            }
                            
                            float sizeFactor = Mathf.Clamp(chunk.rad / slugcatChunkRad, 0.8f, 1.5f);
                            effectiveLatchRange = maxLatchDistance * sizeFactor;
                            float distance = (self.bodyChunks[0].pos - chunk.pos).magnitude;
                            
                            if (distance < closestDistance && distance <= effectiveLatchRange)
                            {
                                closestDistance = distance;
                                closestChunk = chunk;
                            }
                        }

                        if (closestChunk == null)
                        {
                            Logger.LogInfo("Failed to find a chunk to latch onto!");
                            return;
                        }
                        
                        // float weightRatio = latched.TotalMass / self.TotalMass;
                        // float massMultiplier = Mathf.Pow(weightRatio * -0.5f, 2f) + 1.5f;
                        // Logger.LogInfo("Weight ratio: " + weightRatio);
                        // Logger.LogInfo("Mass multiplier: " + massMultiplier);
                        // initialChunkMasses = new float[self.bodyChunks.Length];
                        // for (int i = 0; i < initialChunkMasses.Length; i++)
                        // {
                        //     initialChunkMasses[i] = self.bodyChunks[i].mass;
                        //     self.bodyChunks[i].mass *= massMultiplier;
                        // }
                        // Logger.LogInfo("New total mass: " + self.TotalMass);
                        // UnityEngine.Debug.Log("Leechcat: weight ratio: " + weightRatio);
                        // UnityEngine.Debug.Log("Leechcat: mass multiplier: " + massMultiplier);
                        // UnityEngine.Debug.Log("Leechcat: new total mass: " + self.TotalMass);
                        
                        Logger.LogInfo("Grabbing " + latched.Template.name + " body chunk " + closestChunk.index);
                        UnityEngine.Debug.Log("Leechcat: Grabbing " + latched.Template.name + " body chunk " + closestChunk.index);
                        latchedChunk = closestChunk;
                        bool succeeded = self.Grab(latched, 0, latchedChunk.index,
                            Creature.Grasp.Shareability.CanOnlyShareWithNonExclusive, 0.5f, true, false);
                        Logger.LogInfo("Grab success: " + succeeded);
                        if (!succeeded)
                        {
                            return;
                        }
                        setInitialLatch = true;
                    }
                    else
                    {
                        if (self.input[0].pckp) //drain creature
                        {
                            _drainKeyHeldCounter++;

                            if (_drainKeyHeldCounter >= DRAIN_KEY_HELD_THRESHOLD)
                            {
                                if (!CreatureBeingDrainedTable.GetOrCreateValue(latched).beingDrained)
                                {
                                    Logger.LogInfo("Draining " + latched.Template.name);
                                    UnityEngine.Debug.Log("Leechcat: Draining " + latched.Template.name);
                                    CreatureBeingDrainedTable.GetOrCreateValue(latched).beingDrained = true;
                                }

                                if (latched is AirBreatherCreature)
                                {
                                    //StealAir(latched as AirBreatherCreature);
                                }
                                else
                                {
                                    DrainNonAirBreatherCreature(latched);
                                }
                            }
                            else if (self.input[0].jmp && _drainKeyHeldCounter < DRAIN_KEY_HELD_THRESHOLD)
                            {
                                //poison bite
                            }
                            
                            if (!self.input[0].pckp)
                            {
                                _drainKeyHeldCounter = 0;

                                if (CreatureBeingDrainedTable.GetOrCreateValue(latched).beingDrained)
                                {
                                    Logger.LogInfo("Stopped draining " + latched.Template.name);
                                    UnityEngine.Debug.Log("Leechcat: Stopped draining " + latched.Template.name);
                                    CreatureBeingDrainedTable.GetOrCreateValue(latched).beingDrained = false;
                                }
                            }
                        }
                        else if (self.input[0].jmp)
                        {
                            //stun bite
                        }
                    }
                }
                else
                {
                    self.bodyMode = Player.BodyModeIndex.Default;
                    // if (initialChunkMasses.Length == self.bodyChunks.Length)
                    // {
                    //     for (int i = 0; i < self.bodyChunks.Length; i++)
                    //     {
                    //         self.bodyChunks[i].mass = initialChunkMasses[i];
                    //     }
                    //     Logger.LogInfo("Reset mass on delatch");
                    //     UnityEngine.Debug.Log("Leechcat: reset mass on delatch");
                    // }
                    latchedChunk = null;
                    setInitialLatch = false;
                }
            }
            orig(self, eu);
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
            if (self.dead)
            {
                return;
            }
            if (self.lungs < 1f)
            {
                Logger.LogInfo(self.Template.name + "'s lungs: " + self.lungs);
                UnityEngine.Debug.Log("Leechcat: " + self.Template.name + "'s lungs: " + self.lungs);
            }

            if (CreatureBeingDrainedTable.GetOrCreateValue(self).beingDrained)
            {
                //above water creatures cannot die from drowning, but leechcat can still sap air
                //logic copied from AirBreatherCreature.Update
                if (self.lungs != 1f)
                {
                    self.lungs = Mathf.Max(-1f, self.lungs - 1f / self.Template.lungCapacity);
                }
                else if (Random.value < 0.016666668f) // drain approx once per second
                {
                    self.lungs = Mathf.Max(-1f, self.lungs - 1f / self.Template.lungCapacity);
                }
                if (self.lungs < 0.3f)
                {
                    if (Random.value < 0.025f)
                    {
                        self.LoseAllGrasps();
                    }
                    for (int i = 0; i < self.bodyChunks.Length; i++)
                    {
                        BodyChunk bodyChunk = self.bodyChunks[i];
                        bodyChunk.vel = bodyChunk.vel + ((((Custom.RNV() * self.bodyChunks[i].rad) * 0.4f) * Random.value) 
                                                         * Mathf.Sin(Mathf.InverseLerp(0.3f, -0.3f, self.lungs) * 3.1415927f)) 
                                                      + (((Custom.DegToVec(Mathf.Lerp(-30f, 30f, Random.value)) * Random.value) 
                                                          * (i == self.mainBodyChunkIndex ? 0.4f : 0.2f)) 
                                                         * Mathf.Pow(Mathf.Sin(Mathf.InverseLerp(0.3f, -0.3f, self.lungs) * 3.1415927f), 2f));
                    }
                    // if (self.lungs <= 0f && Random.value < 0.1f)
                    // {
                    //     self.Stun(Random.Range(0, 18));
                    // }
                }

                if (self.Submersion < 0.2f)
                {
                    self.lungs = Mathf.Max(self.lungs, -0.49f);
                }
            }
            else
            {
                orig(self, eu);
            }
        }
        
        private void StealAir(AirBreatherCreature target)
        {
            // Logger.LogInfo("Entered StealAir!");
            // UnityEngine.Debug.Log("Leechcat: Entered StealAir!");
            //
        }
        
        private void LeechIgnoreLeechcat(On.Leech.orig_ConsiderOtherCreature orig, Leech self, Creature crit)
        {
            if (crit != null && crit is Player && (crit as Player).slugcatStats.name.value == MOD_ID)
            {
                return;
            }

            orig(self, crit);
        }
    }
}