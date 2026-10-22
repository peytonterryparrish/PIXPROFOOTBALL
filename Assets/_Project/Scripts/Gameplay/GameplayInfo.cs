namespace PixFootball.Gameplay
{
    public static class GameplayInfo
    {
        /// <summary>The build version (Player Settings > Version): menus, logs and bug reports read it from here so they can never go stale.</summary>
        public static string Version { get { return UnityEngine.Application.version; } }
        public const string Title = "PIX FOOTBALL";
    }
}
