using PixFootball.Art;
using PixFootball.Core;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>What distinguishes one player's kit from the next: jersey number, number colours and how dirty he is.</summary>
    public struct JerseyLook
    {
        public int Number;
        public Color Fill, Trim;
        public DirtState Dirt;
        public bool HasNumber;
        /// <summary>Player surname for the back, team nickname for the chest, crest sprite key for the helmet side decal (v0.6).</summary>
        public string Name, Wordmark, CrestKey;

        /// <summary>Number colours for a kit: the number takes the kit's third colour with the second as outline (visitors: the team's accent), flipped to white / near black if it would vanish into the jersey.</summary>
        public static JerseyLook For(int number, UniformSet kit, bool home, DirtState dirt)
        {
            var j = new JerseyLook { Number = number, Dirt = dirt, HasNumber = number > 0 };   // QA-555: jersey 0 is the "unset" value of generated recruits: draw nothing
            Color jersey = kit.Uniform.Primary;
            Color fill = kit.Uniform.Tertiary;
            Color trim = home ? kit.Uniform.Secondary : kit.Helmet.Secondary;
            // v0.6.0: WCAG-style contrast ratio on linear luminance (the old absolute difference let navy / black kits keep a mid grey or dark gold number); lettering and numbers must read at broadcast distance
            float lj = Luma(jersey);
            if (Ratio(fill, jersey) < 3.4f) fill = lj > 0.3f ? new Color(0.02f, 0.02f, 0.03f) : new Color(0.93f, 0.93f, 0.93f);
            if (Ratio(trim, fill) < 2.0f || Ratio(trim, jersey) < 1.15f) trim = Luma(fill) > 0.3f ? new Color(0.01f, 0.01f, 0.02f) : new Color(0.9f, 0.9f, 0.9f);
            j.Fill = fill; j.Trim = trim; j.Wordmark = kit.Wordmark; j.CrestKey = kit.CrestKey;
            return j;
        }

        public static float Luma(Color linear) { return 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b; }

        /// <summary>Contrast ratio (1..21) between two linear colours.</summary>
        public static float Ratio(Color a, Color b) { float la = Luma(a) + 0.05f, lb = Luma(b) + 0.05f; return la > lb ? la / lb : lb / la; }
    }

    /// <summary>Shared state and helpers of the v0.6 look: shader property ids, the legacy (v0.5) switch used by the before / after gallery, global weather shader values.</summary>
    public static class PixLook
    {
        public static readonly int PixPrimary = Shader.PropertyToID("_PixPrimary"), PixSecondary = Shader.PropertyToID("_PixSecondary"), PixTertiary = Shader.PropertyToID("_PixTertiary");
        public static readonly int PixNumber = Shader.PropertyToID("_PixNumber"), PixNumColor = Shader.PropertyToID("_PixNumColor"), PixNumTrim = Shader.PropertyToID("_PixNumTrim");
        public static readonly int PixNumFront = Shader.PropertyToID("_PixNumFront"), PixNumBack = Shader.PropertyToID("_PixNumBack"), PixNumShoulderL = Shader.PropertyToID("_PixNumShoulderL"), PixNumShoulderR = Shader.PropertyToID("_PixNumShoulderR");
        public static readonly int PixDirtA = Shader.PropertyToID("_PixDirtA"), PixDirtB = Shader.PropertyToID("_PixDirtB"), PixDirtColor = Shader.PropertyToID("_PixDirtColor"), PixScuff = Shader.PropertyToID("_PixScuff");
        public static readonly int PixWeather = Shader.PropertyToID("_PixWeather"), PixWeather2 = Shader.PropertyToID("_PixWeather2");
        public static readonly int PixWearTex = Shader.PropertyToID("_PixWearTex"), PixWearRect = Shader.PropertyToID("_PixWearRect");
        public static readonly int PixTurf = Shader.PropertyToID("_PixTurf");

        public static readonly int PixLetters = Shader.PropertyToID("_PixLetters"), PixDecalTex = Shader.PropertyToID("_PixDecalTex");
        static readonly int[] NameIds = { Shader.PropertyToID("_PixName0"), Shader.PropertyToID("_PixName1"), Shader.PropertyToID("_PixName2"), Shader.PropertyToID("_PixName3") };
        static readonly int[] WordIds = { Shader.PropertyToID("_PixWord0"), Shader.PropertyToID("_PixWord1"), Shader.PropertyToID("_PixWord2"), Shader.PropertyToID("_PixWord3") };
        public static readonly int PixNamePlace = Shader.PropertyToID("_PixNamePlace"), PixWordPlace = Shader.PropertyToID("_PixWordPlace"), PixTextInfo = Shader.PropertyToID("_PixTextInfo");
        public static readonly int PixHelmetRight = Shader.PropertyToID("_PixHelmetRight"), PixHelmetUp = Shader.PropertyToID("_PixHelmetUp"), PixHelmetFwd = Shader.PropertyToID("_PixHelmetFwd"),
                                   PixHelmetCenter = Shader.PropertyToID("_PixHelmetCenter"), PixHelmetExt = Shader.PropertyToID("_PixHelmetExt"), PixHelmetDecal = Shader.PropertyToID("_PixHelmetDecal");
        static readonly float[] _slots = new float[TextAtlas.MaxChars];
        static readonly System.Collections.Generic.Dictionary<string, Texture> _crests = new System.Collections.Generic.Dictionary<string, Texture>();

        /// <summary>The surname of a player name ("Jordan Alder Jr." -> "ALDER").</summary>
        public static string Surname(string full)
        {
            if (string.IsNullOrEmpty(full)) return "";
            var parts = full.Trim().Split(' ');
            int i = parts.Length - 1;
            string last = parts[i].TrimEnd('.').ToUpperInvariant();
            if ((last == "JR" || last == "SR" || last == "II" || last == "III" || last == "IV") && i > 0) i--;
            return parts[i];
        }

        static void SetChars(Material m, int[] ids, string s, out int count)
        {
            count = TextAtlas.Pack(s, _slots);
            for (int i = 0; i < 4; i++) m.SetVector(ids[i], new Vector4(_slots[i * 4], _slots[i * 4 + 1], _slots[i * 4 + 2], _slots[i * 4 + 3]));
        }

        /// <summary>The crest texture of a team (the UI sprite sheet entry), null when unknown.</summary>
        public static Texture CrestTexture(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            Texture t;
            if (_crests.TryGetValue(key, out t)) return t;
            var rt = PixRuntimeAssets.Load();
            var sp = rt != null ? rt.Sprite(key) : null;
            t = sp != null ? sp.texture : null;
            _crests[key] = t;
            return t;
        }

        /// <summary>Helmet shell: team crest on both sides (the stripe comes from the colours).</summary>
        public static void ApplyHelmet(Material m, in JerseyLook look)
        {
            if (m == null) return;
            var crest = CrestTexture(look.CrestKey);
            if (crest != null) m.SetTexture(PixDecalTex, crest);
        }

        static bool _legacy;
        /// <summary>True renders the v0.5 look (original uniform / helmet shader graphs, plain turf, no weather surfaces) - the "before" of the gallery.</summary>
        public static bool Legacy
        {
            get { return _legacy; }
            set { if (_legacy == value) return; _legacy = value; if (LegacyChanged != null) LegacyChanged(); }
        }
        public static event System.Action LegacyChanged;

        /// <summary>Command line / autotest: -pixLegacyLook runs the v0.5 look.</summary>
        public static void ReadArgs()
        {
            foreach (string a in System.Environment.GetCommandLineArgs()) if (a == "-pixLegacyLook") _legacy = true;
        }

        public static PixLookAssets Assets { get { return PixLookAssets.Load(); } }
        public static bool HasV6Uniform { get { var a = Assets; return a != null && a.uniformMaterial != null; } }

        /// <summary>Writes the dirt state and number of a player into a V6 uniform material instance.</summary>
        public static void ApplyJersey(Material m, in JerseyLook look)
        {
            if (m == null) return;
            if (look.HasNumber && look.Number >= 0)
            {
                int tens, ones; NumberAtlas.Digits(look.Number, out tens, out ones);
                m.SetVector(PixNumber, new Vector4(tens < 0 ? 0 : tens, ones, tens < 0 ? 1 : 2, 1f));
                m.SetColor(PixNumColor, look.Fill); m.SetColor(PixNumTrim, look.Trim);
            }
            else m.SetVector(PixNumber, new Vector4(0, 0, 1, 0));
            int nc, wc;
            SetChars(m, NameIds, look.Name, out nc); SetChars(m, WordIds, look.Wordmark, out wc);
            var a = Assets; var mw = a != null ? a.textMaxWidth : new Vector2(0.24f, 0.13f);
            m.SetVector(PixTextInfo, new Vector4(nc, mw.x, wc, mw.y));
            float k, s, f, b; DirtModel.ShaderRegions(look.Dirt, out k, out s, out f, out b);
            m.SetVector(PixDirtA, new Vector4(k, s, f, b));
            m.SetVector(PixDirtB, new Vector4(look.Dirt.GrassFraction, look.Dirt.Mud, look.Dirt.Grime, 0f));
        }

        public const float NumberBackScale = 1.2f, NumberFrontScale = 1.25f;

        public static Color SoilColor()
        {
            var w = WeatherSystem.Instance;
            if (w == null || !w.Applied) return new Color(0.12f, 0.08f, 0.045f);
            if (w.ArtificialTurf) return new Color(0.055f, 0.055f, 0.05f);
            return Color.Lerp(new Color(0.14f, 0.10f, 0.055f), new Color(0.08f, 0.055f, 0.03f), w.Climate != null ? w.Climate.Wet : 0f);
        }

        public static void SetNumberPlacements(Material m, PixLookAssets a)
        {
            if (m == null || a == null) return;
            // v0.9 (GFX-412): the back number +20 percent and the front +25 percent (75 px per metre at the 17 m Classic camera: the numerals were 8-9 px tall strokes); the numerals scale about their centre
            var nf = a.numberFront; nf.z *= NumberFrontScale; var nb = a.numberBack; nb.z *= NumberBackScale;
            m.SetVector(PixNumFront, nf); m.SetVector(PixNumBack, nb);
            m.SetVector(PixNumShoulderL, a.numberShoulderL); m.SetVector(PixNumShoulderR, a.numberShoulderR);
            m.SetVector(PixNamePlace, a.namePlacement); m.SetVector(PixWordPlace, a.wordPlacement);
            if (a.letterAtlas != null) m.SetTexture(PixLetters, a.letterAtlas);
            // v0.9 (GFX-436): the soil colour follows the surface: brown soil on natural grass (darker when wet), black rubber infill grime on artificial turf; grass vs mud is decided per player
            m.SetColor(PixDirtColor, SoilColor());
        }
    }
}
