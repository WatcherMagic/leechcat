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
        private bool isDrainingCreature = false;
        
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
            
            //On.Leech.Attached += LeechLetGoOfLeechCat;
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
                    CustomAirBreatherCreatureData customAirData = null;

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
                    
                    Logger.LogInfo(il.ToString());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Logger.LogError("Exception encountered in IL hook to AirBreatherCreature.Update: " 
                                + e.GetType() + ": " + e.Message + "\n" + e.StackTrace);
            }
        }

        private static void StealAir(AirBreatherCreature target)
        {
            if (target == null || target.dead)
            {
                return /*0f*/;
            }

            //future improvements:
            //creatures on land can refill their lungs, but it becomes less effective
                // the longer they are drained as they get weaker
            //leechcat gets an ability that can stun creatures for a moment while latched on
                // most useful for dragging prey underwater
                // costs a quarter food pip?
            //lungs drain slightly faster underwater when leechcat is draining
            //most creatures (read: not scavengers) can't attack leechcat while latched on
            //bigger creatures can drag leechcat around while attached if not stunned
            
            if (target.lungs > 0.3)
            {
                if (UnityEngine.Random.value >= 0.0166666675)
                {
                    target.lungs = Mathf.Max(-1f, target.lungs - 1f / target.Template.lungCapacity);
                }
                
                if (target.Submersion < 1.0f)
                {
                    const float LUNGS_FILL_RATE = 0.033333335f;
                    target.lungs -= LUNGS_FILL_RATE;
                }
            }
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