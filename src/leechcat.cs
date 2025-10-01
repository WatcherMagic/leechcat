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

        private PhysicalObject lastPotentialGrab = null;
        private PhysicalObject lastPickupCandidate = null;
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
        
        private void LeechCatGrabUpdate(On.Player.orig_GrabUpdate orig, Player self, bool eu)
        {

            if (self.SlugCatClass.value == MOD_ID)
            {
                if (self.grasps[0] != null && self.grasps[0].grabbed != null)
                {
                    string message = "Grabbed " + self.grasps[0].grabbed.GetType() + ", ";
                    
                    if (self.grasps[1] != null && self.grasps[1].grabbed != null)
                    {
                        message += self.grasps[1].grabbed.GetType();
                    }

                    Logger.LogInfo(message);
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