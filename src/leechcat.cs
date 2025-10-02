#define DEVELOPMENT_BUILD

using BepInEx;
using MonoMod.RuntimeDetour;
using MoreSlugcats;
using UnityEngine;

namespace SlugTemplate
{
    [BepInPlugin(MOD_ID, "Leechcat", "0.1.0")]
    class leechcat : BaseUnityPlugin
    {
        private const string MOD_ID = "leechcat";

        private int _drainKeyHeldCounter = 0;
        private const int DRAIN_KEY_HELD_THRESHOLD = 20;
        
        public void OnEnable()
        {
            LoadManualHooks();
            
            On.Player.LungUpdate += LeechCatLungs;
            On.Player.Grabability += LeechCatGrabability;
            On.Player.IsCreatureLegalToHoldWithoutStun += LeechCatCreatureHoldWithoutStun;
            On.Player.GrabUpdate += LeechCatGrabUpdate;
            On.Player.Grabbed += LeechCatEscapeGrab;

            On.AirBreatherCreature.Update += LeechCatAirBreatherDrainUpdate;
            
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
                    if (self.input[0].pckp)
                    {
                        _drainKeyHeldCounter++;

                        if (_drainKeyHeldCounter >= DRAIN_KEY_HELD_THRESHOLD)
                        {
                            //drain creature's oxygen and give it to leechcat
                            Creature grabbedCreature = self.grasps[0].grabbed as Creature;
                            if (!loggedDrain)
                            {
                                Logger.LogInfo("Draining " + grabbedCreature.GetType());
                                UnityEngine.Debug.Log("LeechCat: Draining " + grabbedCreature.GetType());
                            }

                            //this check doesn't work properly!
                            if (grabbedCreature.GetType() == typeof(AirBreatherCreature))
                            {
                                
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
            Logger.LogInfo("Entered DrainNonAirBreatherCreature()!");

            if (creatureToDrain.State is HealthState)
            {   
                /*I'm not entirely sure how this works cause CreatureState doesn't seem to
                 inherit from HealthState, but this is how they do it in Creature.Update() so
                 this is what we're doing*/
                HealthState creatureHealth = creatureToDrain.State as HealthState;
                Logger.LogInfo("Draining non air breather creature! Creature health is " + creatureHealth.health);
                UnityEngine.Debug.Log("LeechCat: Draining non air breather creature! Creature health is " + creatureHealth.health);
                creatureHealth.health -= 0.004f / creatureToDrain.Template.baseDamageResistance;
            }
            
            //yoinked from poison code in Creature.Update()
            // HealthState healthState = state;
            // healthState.health = healthState.health - (single2 - this.injectedPoison) / this.Template.baseDamageResistance;
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
            orig(self, eu);
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