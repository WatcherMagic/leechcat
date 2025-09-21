using BepInEx;

namespace SlugTemplate
{
    [BepInPlugin(MOD_ID, "Leechcat", "0.1.0")]
    class leechcat : BaseUnityPlugin
    {
        private const string MOD_ID = "leechcat";

        public void OnEnable()
        {
            On.Player.LungUpdate += LeechCatLungs;

            On.Player.Grabability += LeechCatGrab;

            On.Leech.Attached += LeechLetGoOfLeechCat;
        }
        
        private void LeechCatLungs(On.Player.orig_LungUpdate orig, Player self)
        {
            if (self.slugcatStats.name.value == MOD_ID)
            {
                self.airInLungs = 1f;
            }
            else
            {
                orig(self);
            }
        }
        
        private Player.ObjectGrabability LeechCatGrab(On.Player.orig_Grabability orig, Player self, PhysicalObject obj)
        {
            if (self.slugcatStats.name.value == MOD_ID)
            {
                Debug.Log("Leechcat is grabbing! Target: " + obj.GetType());
                
                if (obj is Creature)
                {
                    Debug.Log("Leechcat: Check for grab target is creature passed");
                    if (!(obj as Creature).Template.smallCreature )
                    {
                        Debug.Log("Leechcat: Grab target is not a small creature!");

                        Debug.Log("Leechcat: self.dontGrabStuff: " + self.dontGrabStuff);
                        if (self.dontGrabStuff < 1)
                        {
                            Debug.Log("Leechcat: self.dontGrabStuff < 1!");
                            Debug.Log("Returning grabbability.Drag!");
                            return Player.ObjectGrabability.Drag;
                        }
                    }
                }
                //add ability to grab leeches and eat them
            }
            
            return orig(self, obj);
        }
        
        private void LeechLetGoOfLeechCat(On.Leech.orig_Attached orig, Leech self)
        {
            BodyChunk grabbedChunk = self.grasps[0].grabbed.bodyChunks[self.grasps[0].chunkGrabbed];
            if (grabbedChunk.owner is Player && (grabbedChunk.owner as Player).slugcatStats.name.value == MOD_ID)
            {
                self.LoseAllGrasps();
            }
            Debug.Log("Leech let go of leechcat!");
            
            orig(self);
        }
    }
}