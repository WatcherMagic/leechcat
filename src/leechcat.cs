#define DEVELOPMENT_BUILD

using BepInEx;
using MonoMod.RuntimeDetour;
using MoreSlugcats;

namespace SlugTemplate
{
    [BepInPlugin(MOD_ID, "Leechcat", "0.1.0")]
    class leechcat : BaseUnityPlugin
    {
        private const string MOD_ID = "leechcat";

        private int drainKeyHeldCounter = 0;
        private const int DRAIN_KEY_HELD_THRESHOLD = 20;
        
        public void OnEnable()
        {
            On.Player.LungUpdate += LeechCatLungs;
            On.Player.Grabability += LeechCatGrabability;
            On.Player.IsCreatureLegalToHoldWithoutStun += LeechCatCreatureHoldWithoutStun;
            On.Player.GrabUpdate += LeechCatGrabUpdate;
            On.Player.Grabbed += LeechCatEscapeGrab;
            
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
            Logger.LogInfo("Entered Leechcat creature legal to hold without stun hook");
            Debug.LogInfo("LeechCat: Entered Leechcat creature legal to hold without stun hook");
            
            if (self.slugcatStats.name.value == MOD_ID)
            {
                Logger.LogInfo("Player is Leechcat, returning true!");
                Debug.LogInfo("LeechCat: Player is Leechcat, returning true!");
                
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
                        drainKeyHeldCounter++;

                        if (drainKeyHeldCounter >= DRAIN_KEY_HELD_THRESHOLD)
                        {
                            //drain creature's oxygen and give it to leechcat
                            Creature grabbedCreature = self.grasps[0].grabbed as Creature;
                            if (!loggedDrain)
                            {
                                Logger.LogInfo("Draining " + grabbedCreature.GetType());
                                Debug.LogInfo("LeechCat: Draining " + grabbedCreature.GetType());
                            }
                            loggedDrain = true;
                            
                            //
                        }
                    }
                    else if (!self.input[0].pckp && self.input[1].pckp)
                    {
                        loggedDrain = false;
                        drainKeyHeldCounter = 0;
                    }
                }
            
                orig(self, eu);
            }
            else
            {
                orig(self, eu);
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
        
        private void LeechLetGoOfLeechCat(On.Leech.orig_Attached orig, Leech self)
        {
            BodyChunk grabbedChunk = self.grasps[0].grabbed.bodyChunks[self.grasps[0].chunkGrabbed];
            if (grabbedChunk.owner is Player && (grabbedChunk.owner as Player).SlugCatClass.value == MOD_ID)
            {
                self.LoseAllGrasps();
            }
            Debug.LogInfo("Leech let go of leechcat!");
            
            orig(self);
        }
    }
}