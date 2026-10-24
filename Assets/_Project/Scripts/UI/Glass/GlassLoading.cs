using System.Collections;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// The glass loading screen: a dark graphite stage with the wordmark, the matchup (two crests and names when a match loads), a progress bar and a control tip, shown over every scene change while the
    /// Glass theme is active (GameSession.LoadOverride). The overlay survives the scene change, waits for the new scene to wake up, then fades out. Classic keeps its plain synchronous load.
    /// </summary>
    public sealed class GlassLoading : MonoBehaviour
    {
        static GlassLoading _live;
        public static readonly string[] Tips =
        {
            "Tap a receiver button for a bullet pass, hold it for a lob.",
            "Lead your receiver with the right stick before you throw.",
            "Call a timeout with LT before the play clock runs out.",
            "The left stick scrambles the quarterback out of the pocket.",
            "Press View to change the camera at any time.",
            "The coach picks (ASK COACH tab) suggest a play for the down and distance."
        };

        CanvasGroup _group; GlassBar _bar; TextMeshProUGUI _title, _tip, _status;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() { GameSession.LoadOverride = Handle; }

        /// <summary>Pure: the title line for a scene name.</summary>
        public static string TitleFor(string scene)
        {
            if (scene == GameSession.MatchScene) return "KICKOFF";
            if (scene == GameSession.StadiumScene) return "FREE ROAM";
            return "MAIN MENU";
        }

        /// <summary>QA: builds the overlay for a scene change without loading anything (edit-mode renders).</summary>
        public static GameObject CreatePreview(string scene)
        {
            var go = new GameObject("GlassLoadingPreview");
            var gl = go.AddComponent<GlassLoading>();
            gl.Build(scene);
            gl._group.alpha = 1f; gl._bar.Set(0.62f);
            return go;
        }

        public static bool Handle(string scene)
        {
            if (!Application.isPlaying || !UiTheme.IsGlass || _live != null) return false;
            var go = new GameObject("GlassLoading");
            DontDestroyOnLoad(go);
            var gl = go.AddComponent<GlassLoading>();
            _live = gl;
            gl.Build(scene);
            gl.StartCoroutine(gl.Run(scene));
            return true;
        }

        void Build(string scene)
        {
            var t = GlassTheme.T;
            var canvas = UiKit.CreateCanvas("LoadingCanvas", 900, transform);
            canvas.GetComponent<GraphicRaycaster>().enabled = false;
            var root = (RectTransform)canvas.transform;
            _group = root.gameObject.AddComponent<CanvasGroup>(); _group.alpha = 0f;
            if (t.flat) GlassKit.TileBackdrop(root, "Stage");
            else { var bg = UiKit.Panel(root, "Stage", new Color(0.055f, 0.058f, 0.066f, 1f)); UiKit.Stretch(bg.rectTransform); }
            // a soft light wash so the glass has something to sit on
            if (!t.flat) { var wash = UiKit.Panel(root, "Wash", new Color(1f, 1f, 1f, 0.07f), GlassWidgets.GradientSprite()); UiKit.Stretch(wash.rectTransform); }
            GlassWidgets.TopBar(root, null);

            _title = GlassKit.Text(root, TitleFor(scene), GlassText.Hero, TextAlignmentOptions.Center, "Title");
            GlassKit.Style(_title, 96f, t.textPrimary, true, 8f);
            UiKit.Anchor(_title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 290f), new Vector2(1400f, 130f));

            // matchup (a match is loading): crests and names with VS between them
            var cfg = scene == GameSession.MatchScene ? MatchData.Current : null;
            if (cfg != null)
            {
                MakeTeam(root, cfg.HomeId, -420f); MakeTeam(root, cfg.AwayId, 420f);
                var vs = GlassKit.Text(root, "VS", GlassText.Title, TextAlignmentOptions.Center, "Vs"); GlassKit.Style(vs, 64f, t.accent, true, 6f);
                UiKit.Anchor(vs.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 110f), new Vector2(240f, 90f));
            }

            var card = GlassKit.Panel(root, "TipCard", 1.2f, true);
            UiKit.Anchor(card.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 170f), new Vector2(1100f, 150f));
            var eyebrow = GlassKit.Eyebrow(card.rectTransform, "TIP", TextAlignmentOptions.MidlineLeft, "TipLabel"); GlassKit.Style(eyebrow, 18f, t.accent, true, 4f);
            UiKit.Anchor(eyebrow.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -22f), new Vector2(200f, 28f));
            _tip = GlassKit.Text(card.rectTransform, Tips[Random.Range(0, Tips.Length)], GlassText.Body, TextAlignmentOptions.TopLeft, "Tip"); GlassKit.Style(_tip, 28f, t.textPrimary, false, 0f); _tip.textWrappingMode = TextWrappingModes.Normal;
            UiKit.Anchor(_tip.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -54f), new Vector2(1036f, 80f));

            _bar = GlassKit.ProgressBar(root, 0f, 1100f, 8f, true, "Progress");
            var br = (RectTransform)_bar.transform; UiKit.Anchor(br, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(1100f, 8f));
            _status = GlassKit.Eyebrow(root, "LOADING", TextAlignmentOptions.MidlineLeft, "Status"); GlassKit.Style(_status, 18f, t.textSecondary, true, 4f);
            UiKit.Anchor(_status.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-350f, 90f), new Vector2(400f, 28f));
        }

        static void MakeTeam(RectTransform root, string id, float x)
        {
            var t = GlassTheme.T;
            var team = TeamCatalog.ById(id); if (team == null) return;
            var sp = UiKit.Sprite("crest_" + team.Id);
            var crest = UiKit.Panel(root, "Crest", Color.white, sp); crest.preserveAspect = true; crest.enabled = sp != null;
            UiKit.Anchor(crest.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 100f), new Vector2(230f, 230f));
            var city = GlassKit.Eyebrow(root, team.City, TextAlignmentOptions.Center, "City"); GlassKit.Style(city, 22f, t.textSecondary, true, 4f);
            UiKit.Anchor(city.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, -42f), new Vector2(520f, 30f));
            var nick = GlassKit.Text(root, team.Nickname.ToUpperInvariant(), GlassText.Title, TextAlignmentOptions.Center, "Nick"); GlassKit.Style(nick, 50f, t.textPrimary, true, 3f);
            UiKit.Anchor(nick.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, -92f), new Vector2(560f, 66f)); UiKit.Fit(nick, 28f);
        }

        IEnumerator Run(string scene)
        {
            float t = 0f;
            while (t < 0.25f) { t += Time.unscaledDeltaTime; _group.alpha = Mathf.SmoothStep(0f, 1f, t / 0.25f); yield return null; }
            _group.alpha = 1f;
            yield return null;
            var op = SceneManager.LoadSceneAsync(scene);
            if (op == null) { Done(); yield break; }
            op.allowSceneActivation = false;
            float shown = 0f;
            while (op.progress < 0.9f || shown < 0.9f)
            {
                shown += Time.unscaledDeltaTime;
                _bar.Set(Mathf.Min(op.progress / 0.9f, shown / 0.9f) * 0.96f);
                yield return null;
            }
            _bar.Set(1f); _status.text = "READY";
            op.allowSceneActivation = true;
            while (!op.isDone) yield return null;
            for (int i = 0; i < 3; i++) yield return null;   // the new scene runs its Awake / Start
            t = 0f;
            while (t < 0.45f) { t += Time.unscaledDeltaTime; _group.alpha = 1f - Mathf.SmoothStep(0f, 1f, t / 0.45f); yield return null; }
            Done();
        }

        void Done() { _live = null; Destroy(gameObject); }
    }
}
