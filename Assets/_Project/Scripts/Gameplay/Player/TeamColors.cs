using UnityEngine;

namespace PixFootball.Gameplay
{
    [System.Serializable]
    public struct TeamColors
    {
        public Color Primary, Secondary, Tertiary;
        public TeamColors(Color p, Color s, Color t) { Primary = p; Secondary = s; Tertiary = t; }

        // The uniform shader graph colour properties are linear; hex values are sRGB.
        static Color Hex(string h) { Color c; ColorUtility.TryParseHtmlString(h, out c); return c.linear; }
        public static TeamColors FromHex(string primary, string secondary, string tertiary) { return new TeamColors(Hex(primary), Hex(secondary), Hex(tertiary)); }
        public static TeamColors Home { get { return new TeamColors(Hex("#12306B"), Hex("#E9EDF2"), Hex("#FFC837")); } }
        public static TeamColors Away { get { return new TeamColors(Hex("#B01E2A"), Hex("#F2F2F2"), Hex("#1A1A1A")); } }
    }
}
