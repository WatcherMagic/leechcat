#define DEVELOPMENT_BUILD

using System;
using System.Runtime.CompilerServices;
using BepInEx;
using JetBrains.Annotations;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using UnityEngine;

namespace SlugTemplate
{
    
    //leechcat ability plans:
    //custom poison bite, costs 1 food pip
        //slows prey, causes weakness, and rarely stuns; can be stacked
    //stun bite, costs 1/4 food pip
        //stuns prey for ~1 second; timing it with activation of CreatureSpasmer/thrashing can double stun duration
    //can't pick up objects; no arms
        //alternatively, can hold one object in mouth (i.e, for swallowing/tolls)
    //controls:
        //shift = latch onto creature/pick up thing in mouth
        //shift (held while latched) = drain oxygen, also drains food pips on exhausted creatures
        //x (while latched) = delatch and jump to new point on creature; use to avoid being thrown off
        //  sticky window/grace period when volunatrily delatched--auto target new latch point
        //z = stun bite, increases exhaustion
        //shift + z/c = poison bite, increases exhaustion
        //arrow keys = move around on creature while latched
            //cannot bite or drain oxygen while moving
        //optional: up arrow near pole while latched = hold onto pole
    //fight mechanics:
        //leechcat latches onto a creature above water, creature may or may not react
        //being latched prevents leechcat's oxygen from depleting
        //hold shift -> oxygen drain, creature panics and starts trying to attack/dislodge leechcat
        //leechcat's "grip strength" depletes while the creature struggles
        //press z -> jump to a different location on the body,
        //  with grip strength refilling rapidly depending on struggle strength
        //x/c can be used at cost of food pips to stun or poison the creature
        //timing stun bite with the start of a thrash is "super effective" and doubles stun duration
        //while oxygen is being depleted, creature's "exhaustion" is increasing
        //applied poison also increases exhaustion
        //exhaustion cannot decrease while the creature is stunned
        //repeat cycle of oxygen drain + stun + poison until creature is exhausted
        //leechcat receives drag assist on creatures in exhausted state, and can now drain food pips
        //player options: drag exhausted creature to water for quick kill + advantage, or feed
        //if feeding on land, exhaustion will gradually decrease;
        //  when it goes below 100%, player will lose access to food pips and creature will recover slightly
        //dragging creature will cause creature to start ineffectively struggling, preventing exhaustion recovery
        //underwater, exhaustion can be maxed out, the creature can drown,
        //  and leechcat can siphon food pips at maximum rate
        //creature dies once leechcat has siphoned all its food pips
    //feeding mechanics:
        //carcasses have a "decay timer" and are no longer edible after 1-2 minutes
        //this forces leechcat to rely on live prey, but allows eating from fresh kills and scavenging if lucky
        //leechcat food pip drain scales depending on how exhausted the prey is:
        //  100%-125%: 1/4 pip
        //  125%-150%: 1/2 pip
        //  175%-200%(max): 1 pip
    //AI adjustments:
        //creatures that have leechcat's poison will flee any threats,
        //  then try to stay still to heal it off
    
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
            IL.AirBreatherCreature.Update += LeechCatAirBreatherILUpdate;

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

            // if (self.bodyMode == LeechcatEnums.PlayerBodyModeIndex.LeechcatLatched
            //     && latchedChunk != null)
            // {
            //     SetLatchedState(self);
            //
            //     //velocity approach
            //     float massRatio = latchedChunk.owner.TotalMass / self.TotalMass;
            //     float velocityMult = Mathf.Clamp(Mathf.Log((-massRatio + 2.16f) + 0.2f, 0.1f), 0f, 1f);
            //     if (!loggedLatch)
            //     {
            //         UnityEngine.Debug.Log("Leechcat: weight ratio: " + massRatio);
            //         UnityEngine.Debug.Log("Leechcat: velocity multiplier: " + velocityMult);
            //         Logger.LogInfo("Weight ratio: " + massRatio);
            //         Logger.LogInfo("Velocity multiplier: " + velocityMult);
            //         loggedLatch = true;
            //     }
            //     if (velocityMult < 1f)
            //     {
            //         foreach (BodyChunk chunk in latchedChunk.owner.bodyChunks)
            //         {
            //             float multiplierWeight = 1f - velocityMult;
            //             int closenessToLatched = latchedChunk.index - chunk.index;
            //             closenessToLatched = closenessToLatched < 0 ? closenessToLatched * -1 : closenessToLatched;
            //             if (closenessToLatched < 0)
            //             {
            //                 Logger.LogError("Below 0 value in latched chunk velocity calculations!");
            //                 UnityEngine.Debug.Log("Leechcat ERROR: Below 0 value in latched chunk velocity calculations!");
            //                 break;
            //             }
            //             switch (closenessToLatched)
            //             {
            //                 case 0:
            //                     chunk.vel *= velocityMult;
            //                     break;
            //                 case 1:
            //                     chunk.vel *= multiplierWeight * 0.1f;
            //                     break;
            //                 case 2:
            //                     chunk.vel *= multiplierWeight * 0.25f;
            //                     break;
            //                 case 3:
            //                     chunk.vel *= multiplierWeight * 0.5f;
            //                     break;
            //                 case 4:
            //                     chunk.vel *= multiplierWeight * 0.8f;
            //                     break;
            //                 default:
            //                     break;
            //             }
            //         }
            //     }
            //     
            //     //TODO: check if ground detection works
            //     if (latchedChunk.contactPoint.y < 0)
            //     {
            //         latchedChunk.vel.y -= self.gravity;
            //     }
            //     self.bodyChunks[0].pos = latchedChunk.pos;
            //     self.bodyChunks[0].vel = latchedChunk.vel;
            //     
            //     canDelatchCounter++;
            //
            //     if (self.input[0].jmp && !self.input[1].jmp || self.dangerGrasp != null)
            //     {
            //         if (canDelatchCounter >= TIME_UNTIL_CAN_DELATCH)
            //         {
            //             self.bodyMode = Player.BodyModeIndex.Default;
            //             loggedLatch = false;
            //         }
            //         else
            //         {
            //             UnityEngine.Debug.Log("Leechcat: Can't delatch yet! Delatch counter is at " + canDelatchCounter);
            //             Logger.LogInfo("Can't delatch yet! Delatch counter is at " + canDelatchCounter);
            //         }
            //     }
            // }
            //
            bool isLatched = self.bodyMode == LeechcatEnums.PlayerBodyModeIndex.LeechcatLatched;

            orig(self, eu);

            if (isLatched)
            {
                SetLatchedState(self);
            }
            // else if (latchedChunk != null && !CheckInsideCreatureChunk(self))
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
        }

        private bool CheckInsideCreatureChunk(Player self)
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
            
            // if (self.SlugCatClass.value == MOD_ID)
            // {
            //     if (obj is Creature && !(obj as Creature).Template.smallCreature)
            //     {
            //         if (obj.GetType() == typeof(Player))
            //         {
            //             return orig(self, obj);
            //         }
            //
            //         Player.ObjectGrabability checkForTwoHandCreature = orig(self, obj);
            //         if (checkForTwoHandCreature != Player.ObjectGrabability.TwoHands)
            //         {
            //             return Player.ObjectGrabability.Drag;
            //         }
            //
            //         return checkForTwoHandCreature;
            //     }
            //     
            //     return orig(self, obj);
            //     
            //     //add ability to grab leeches and eat them
            // }
            //
            // return orig(self, obj);
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
                    self.bodyMode = LeechcatEnums.PlayerBodyModeIndex.LeechcatLatched;

                    // Creature grabbedCreature = self.grasps[0].grabbed as Creature;
                    // CustomLeechCatVariables customAirData = null;
                    //
                    // if (grabbedCreature is AirBreatherCreature)
                    // {
                    //     customAirData = CreatureBeingDrainedTable.GetOrCreateValue(grabbedCreature);
                    // }
                    //
                    // if (self.input[0].pckp)
                    // {
                    //     //Logger.LogInfo("Player pressed pickup!");
                    //     _drainKeyHeldCounter++;
                    // }
                    // else
                    // {
                    //     //Logger.LogInfo("Player is not pressing pickup!");
                    //     _drainKeyHeldCounter = 0;
                    //     
                    //     if (customAirData != null && customAirData.beingDrained)
                    //     {
                    //         customAirData.beingDrained = false;
                    //     }
                    //     if (isDrainingCreature)
                    //     {
                    //         isDrainingCreature = false;
                    //         Logger.LogInfo("Setting beingDrained to false!");
                    //         UnityEngine.Debug.Log("Leechcat: Stopped draining " + grabbedCreature.Template.name + "!");
                    //     }
                    // }
                    //
                    // //creature is being drained & pickup was not released, continue to other logic
                    // if (customAirData != null && customAirData.beingDrained)
                    // {
                    //     orig(self, eu);
                    //     return;
                    // }
                    //
                    // //creature is not being drained yet but conditions have been met to start
                    // if (_drainKeyHeldCounter >= DRAIN_KEY_HELD_THRESHOLD)
                    // {
                    //     isDrainingCreature = true;
                    //     Logger.LogInfo("Started draining " + grabbedCreature.Template.name + "!");
                    //     Debug.Log("Leechcat: Started draining " + grabbedCreature.Template.name + "!");
                    //     if (grabbedCreature is AirBreatherCreature && customAirData != null)
                    //     {
                    //         Logger.LogInfo("Detected air breather creature! Setting beingDrained to true");
                    //         customAirData.beingDrained = true;
                    //     }
                    //     else
                    //     {
                    //         DrainNonAirBreatherCreature(grabbedCreature);
                    //     }
                    // }
                }
                else
                {
                    self.bodyMode = Player.BodyModeIndex.Default;
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
            if (!self.dead && self.lungs < 1f)
            {
                Logger.LogInfo(self.GetType() + "'s lungs: " + self.lungs);
                UnityEngine.Debug.Log("Leechcat: " + self.GetType() + "'s lungs: " + self.lungs);
            }
            
            orig(self, eu);
        }
        
        private void LeechCatAirBreatherILUpdate(ILContext il)
        {
            try
            {
                ILCursor c = new ILCursor(il);
                
                    c.GotoNext(MoveType.After,
                        x => x.MatchLdarg(0),
                        x => x.MatchCallOrCallvirt(typeof(Creature).GetProperty(nameof(Creature.dead)).GetGetMethod()),
                        x => x.MatchBrtrue(out _));
                    c.MoveAfterLabels();
                    ILLabel passBeingDrainedCheck = c.DefineLabel();
                    c.Emit(OpCodes.Ldarg_0);
                    c.EmitDelegate<Func<AirBreatherCreature, bool>>(target => CreatureBeingDrainedTable.GetOrCreateValue(target).beingDrained);
                    c.Emit(OpCodes.Brfalse_S, passBeingDrainedCheck);
                    c.Emit(OpCodes.Ldarg_0);
                    c.EmitDelegate(StealAir);
                    
                    //force the game to treat being drained on land like drowning under water
                    ILLabel drowningLogic = c.DefineLabel();
                    c.Emit(OpCodes.Ldarg_0);
                    c.Emit(OpCodes.Ldfld, typeof(AirBreatherCreature).GetField(nameof(AirBreatherCreature.lungs)));
                    c.EmitDelegate<Func<float, bool>>(lungs => lungs <= 0.3);
                    c.Emit(OpCodes.Brtrue, drowningLogic);
                    c.MarkLabel(passBeingDrainedCheck);
                    
                    c.GotoNext(MoveType.Before,
                        x => x.MatchLdarg(0),
                        x => x.MatchLdcR4(-1f),
                        x => x.MatchLdarg(0));
                    c.MoveAfterLabels();
                    c.MarkLabel(drowningLogic);
                    
                    //Logger.LogInfo(il.ToString());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Logger.LogError("Exception encountered in IL hook to AirBreatherCreature.Update: " 
                                + e.GetType() + ": " + e.Message + "\n" + e.StackTrace);
            }
        }

        private void StealAir(AirBreatherCreature target)
        {
            if (target == null || target.dead)
            {
                return /*0f*/;
            }

            if (CreatureBeingDrainedTable.GetOrCreateValue(target).beingDrained)
            {
                if (target.Submersion < 1.0f)
                {
                    float baseDrain = 0.2f;
                    float sizeMultiplier = target.TotalMass;
                    const float VANILLA_REFILL = 0.033333335f;
                
                    float netDrain = baseDrain / target.Template.lungCapacity / (1f + sizeMultiplier) - VANILLA_REFILL;

                    target.lungs -= netDrain;
                    target.lungs = Mathf.Clamp(target.lungs, -0.49f, target.Template.lungCapacity);
                }
            }
            
            // if (target.lungs > 0.3f)
            // {
            //     if (UnityEngine.Random.value >= 0.0166666675)
            //     {
            //         target.lungs = Mathf.Max(-1f, target.lungs - 1f / target.Template.lungCapacity);
            //     }
            //     
            //     if (target.Submersion < 1.0f)
            //     {
            //         const float LUNGS_FILL_RATE = 0.033333335f;
            //         target.lungs -= LUNGS_FILL_RATE;
            //     }
            // }
            //
            // if (target.lungs < -0.49f && target.Submersion < 1.0f)
            // {
            //     target.lungs = -0.49f;
            // }
        }
        
        private void LeechIgnoreLeechcat(On.Leech.orig_ConsiderOtherCreature orig, Leech self, Creature crit)
        {
            if (crit != null && crit is Player && (crit as Player).slugcatStats.name.value == MOD_ID)
            {
                return;
            }

            orig(self, crit);
        }
        
        // private void LeechLetGoOfLeechCat(On.Leech.orig_Attached orig, Leech self)
        // {
        //     BodyChunk grabbedChunk = self.grasps[0].grabbed.bodyChunks[self.grasps[0].chunkGrabbed];
        //     if (grabbedChunk.owner is Player && (grabbedChunk.owner as Player).SlugCatClass.value == MOD_ID)
        //     {
        //         self.LoseAllGrasps();
        //     }
        //     UnityEngine.Debug.Log("Leech let go of leechcat!");
        //     
        //     orig(self);
        // }
    }
}