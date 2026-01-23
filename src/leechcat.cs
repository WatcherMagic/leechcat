#define DEVELOPMENT_BUILD

using System;
using System.Runtime.CompilerServices;
using BepInEx;
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
        //shift = pick up thing in mouth?
        //shift + z = latch onto creature
        //shift (held while latched) = drain oxygen, also drains food pips on exhausted creatures
        //z (while latched) = delatch and jump to new point on creature; use to avoid being thrown off
        //  sticky window/grace period when volunatrily delatched--auto target new latch point
        //x = stun bite, increases exhaustion
        //shift + x/c = poison bite, increases exhaustion
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
    
    [BepInPlugin(MOD_ID, "Leechcat", "0.1.0")]
    class leechcat : BaseUnityPlugin
    {
        private const string MOD_ID = "leechcat";

        private int _drainKeyHeldCounter = 0;
        private const int DRAIN_KEY_HELD_THRESHOLD = 20;
        private bool isDrainingCreature = false;
        private bool currentlyLatched = false;
        private float maxLatchDistance = 100;
        private int canDelatchCounter = 0;
        private const int TIME_UNTIL_CAN_DELATCH = 3;
        // private float? latchOffsetX = null;
        // private float? latchOffsetY = null;
        private BodyChunk latchedChunk = null;
        
        public ConditionalWeakTable<Creature, CustomLeechCatVariables> creatureBeingDrainedTable = new ();
        
        public void OnEnable()
        {
            LeechcatEnums.PlayerBodyModeIndex.RegisterValues();
            
            On.Player.LungUpdate += LeechCatLungs;
            On.Player.Update += LeechCatLatch;
            IL.Player.Update += LeechCatLatchIL;
            //On.Player.Grabability += LeechCatGrabability;
            //On.Player.IsCreatureLegalToHoldWithoutStun += LeechCatCreatureHoldWithoutStun;
            //On.Player.GrabUpdate += LeechCatGrabUpdate;
            On.Player.Grabbed += LeechCatEscapeGrab;

            On.AirBreatherCreature.Update += LeechCatAirBreatherUpdate;
            IL.AirBreatherCreature.Update += LeechCatAirBreatherILUpdate;

            On.Leech.ConsiderOtherCreature += LeechIgnoreLeechcat;
            
            // On.GraphicsModule.InitiateSprites += InitiateChunkDebugSprites;
            // On.GraphicsModule.DrawSprites += DrawChunkDebugSprites;
            // On.GraphicsModule.AddToContainer += AddChunkDebugSpritesToContainer;
            
            Logger.LogInfo("All player BodyModeIndex enums:");
            foreach (string index in Player.BodyModeIndex.values.entries)
            {
                Logger.LogInfo(index);
            }
        }

        private void OnDisable()
        {
            LeechcatEnums.PlayerBodyModeIndex.UnregisterValues();
        }

        private void LeechCatLatchIL(ILContext il)
        {
            try
            {
                ILCursor c = new ILCursor(il);
                
                
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Logger.LogError("Exception encountered in IL hook to Player.Update: " 
                                + e.GetType() + ": " + e.Message + "\n" + e.StackTrace);
            }
        }

        private void LeechCatLatch(On.Player.orig_Update orig, Player self, bool eu)
        {
            orig(self, eu);
            
            if (self.slugcatStats.name.value == MOD_ID && !currentlyLatched && self.input[0].pckp && self.input[0].jmp)
            {
                UnityEngine.Debug.Log("Leechcat: Detected attempt to latch!");
                Logger.LogInfo("Detected attempt to latch!");
                Vector2 leechcatPos = self.bodyChunks[0].pos;
                float slugcatChunkRad = self.bodyChunks[0].rad;
                
                foreach (AbstractCreature crit in self.room.abstractRoom.creatures)
                {
                    if (crit.realizedCreature != null)
                    {
                        foreach (BodyChunk chunk in crit.realizedCreature.bodyChunks)
                        {
                            if (chunk.owner == self)
                            {
                                //Logger.LogInfo("Found own chunk: " + chunk.owner);
                                continue;
                            }

                            Logger.LogInfo("slugcat chunk rad: " + self.bodyChunks[0].rad);
                            Logger.LogInfo("potential latch chunk rad: " + chunk.rad);
                            
                            float sizeFactor = Mathf.Clamp(chunk.rad / slugcatChunkRad, 0.8f, 1.5f);
                            Logger.LogInfo("size factor for chunk: " + sizeFactor);
                            Logger.LogInfo("max latch distance: " + maxLatchDistance);
                            
                            float effectiveLatchRange = maxLatchDistance * sizeFactor;
                            Logger.LogInfo("effective latch range: " + effectiveLatchRange);
                            Logger.LogInfo("slugcat chunk positon: " + self.bodyChunks[0].pos);
                            Logger.LogInfo("distance: " + (self.bodyChunks[0].pos - chunk.pos).magnitude);
                            
                            if ((leechcatPos - chunk.pos).magnitude <= effectiveLatchRange)
                            {
                                UnityEngine.Debug.Log("Leechcat: Found creature chunk in latching range! Chunk owner: " + chunk.owner);
                                Logger.LogInfo("Found creature chunk in latching range! Chunk owner: " + chunk.owner);
                                // latchOffsetX = chunk.pos.x - latchedPos.x;
                                // latchOffsetY = chunk.pos.y - latchedPos.y;
                                latchedChunk = chunk;
                                canDelatchCounter = 0;
                                currentlyLatched = true;
                                self.graphicsModule.BringSpritesToFront();
                                break;
                            }

                        }
                    }
                }

                UnityEngine.Debug.Log("Leechcat: Couldn't find creature to latch onto!");
                Logger.LogInfo("Couldn't find creature to latch onto!");
            }

            if (self.slugcatStats.name.value == MOD_ID && currentlyLatched && latchedChunk != null)
            {
                self.bodyChunks[0].collideWithObjects = false;
                self.bodyChunks[1].collideWithObjects = false;
                // Vector2 latchOffset = new Vector2(latchOffsetX.Value, latchOffsetY.Value);
                // Vector2 latchPos = latchedChunk.pos + latchOffset;
                self.bodyChunks[0].pos = latchedChunk.pos;
                UnityEngine.Debug.Log("Leechcat: Moved leechcat to latch point! " + self.bodyChunks[0].pos.ToString());
                Logger.LogInfo("Moved Leechcat to latch point!" + self.bodyChunks[0].pos);
                
                canDelatchCounter++;

                if (self.input[0].jmp)
                {
                    if (canDelatchCounter >= TIME_UNTIL_CAN_DELATCH)
                    {
                        currentlyLatched = false;
                        latchedChunk = null;
                    }
                    else
                    {
                        Debug.LogInfo("Leechcat: Can't delatch yet! Delatch counter is at " + canDelatchCounter);
                        Logger.LogInfo("Can't delatch yet! Delatch counter is at " + canDelatchCounter);
                    }
                }
            }
        }

        private void LeechIgnoreLeechcat(On.Leech.orig_ConsiderOtherCreature orig, Leech self, Creature crit)
        {
            if (crit != null && crit is Player && (crit as Player).slugcatStats.name.value == MOD_ID)
            {
                return;
            }

            orig(self, crit);
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

        private void LeechCatGrabUpdate(On.Player.orig_GrabUpdate orig, Player self, bool eu)
        {
            if (self.SlugCatClass.value == MOD_ID)
            {
                if (self.grasps[0] != null && self.grasps[0].grabbed != null 
                                           && self.grasps[0].grabbed is Creature
                                           && !(self.grasps[0].grabbed as Creature).dead)
                {
                    Creature grabbedCreature = self.grasps[0].grabbed as Creature;
                    CustomLeechCatVariables customAirData = null;

                    if (grabbedCreature is AirBreatherCreature)
                    {
                        customAirData = creatureBeingDrainedTable.GetOrCreateValue(grabbedCreature);
                    }
                    
                    if (self.input[0].pckp)
                    {
                        //Logger.LogInfo("Player pressed pickup!");
                        _drainKeyHeldCounter++;
                    }
                    else
                    {
                        //Logger.LogInfo("Player is not pressing pickup!");
                        _drainKeyHeldCounter = 0;
                        
                        if (customAirData != null && customAirData.beingDrained)
                        {
                            customAirData.beingDrained = false;
                        }
                        if (isDrainingCreature)
                        {
                            isDrainingCreature = false;
                            Logger.LogInfo("Setting beingDrained to false!");
                            UnityEngine.Debug.Log("Leechcat: Stopped draining " + grabbedCreature.Template.name + "!");
                        }
                    }

                    //creature is being drained & pickup was not released, continue to other logic
                    if (customAirData != null && customAirData.beingDrained)
                    {
                        orig(self, eu);
                        return;
                    }

                    //creature is not being drained yet but conditions have been met to start
                    if (_drainKeyHeldCounter >= DRAIN_KEY_HELD_THRESHOLD)
                    {
                        isDrainingCreature = true;
                        Logger.LogInfo("Started draining " + grabbedCreature.Template.name + "!");
                        Debug.Log("Leechcat: Started draining " + grabbedCreature.Template.name + "!");
                        if (grabbedCreature is AirBreatherCreature && customAirData != null)
                        {
                            Logger.LogInfo("Detected air breather creature! Setting beingDrained to true");
                            customAirData.beingDrained = true;
                        }
                        else
                        {
                            DrainNonAirBreatherCreature(grabbedCreature);
                        }
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
                    c.EmitDelegate<Func<AirBreatherCreature, bool>>(target => creatureBeingDrainedTable.GetOrCreateValue(target).beingDrained);
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

            if (creatureBeingDrainedTable.GetOrCreateValue(target).beingDrained)
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