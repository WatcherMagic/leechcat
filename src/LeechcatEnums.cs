namespace SlugTemplate;

public class LeechcatEnums
{
    public class PlayerBodyModeIndex
    {
        public static Player.BodyModeIndex LeechcatLatched;

        public static void RegisterValues()
        {
            LeechcatLatched = new Player.BodyModeIndex("LeechcatLatched", true);
        }

        public static void UnregisterValues()
        {
            if (LeechcatLatched != null)
            {
                LeechcatLatched.Unregister();
                LeechcatLatched = null;
            }
        }
    }
}