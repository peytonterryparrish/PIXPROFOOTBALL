using System;
using System.Collections.Generic;
using System.Linq;
using PixFootball.Gameplay;
using PixFootball.League;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// "Play Now" setup: pick the home and away teams from the 32 pro teams (crest, colours, overall rating), quarter length, rules, CPU difficulty
    /// and which side you play, then kick off. Two live preview cards on the right, option rows on the left (full gamepad navigation).
    /// </summary>
    public class TeamSelectPanel : MonoBehaviour, ICancelHandler
    {
        public Action OnBack;
        public Selectable First;

        sealed class Card { public Image Wash, Crest; public TextMeshProUGUI Role, City, Nick, Overall, OvrLabel; public Image[] Chips = new Image[3]; public Image Bar; }
        Card _home, _away;
        OptionSelector _homeSel, _awaySel, _quarterSel, _rulesSel, _diffSel, _sideSel;
        List<TeamInfo> _teams;
        MatchConfig _cfg;
        TextMeshProUGUI _summary;

        public static TeamSelectPanel Build(Transform parent, Action onBack)
        {
            var root = UiKit.Rect(parent, "TeamSelectPanel");
            var p = root.gameObject.AddComponent<TeamSelectPanel>();
            p.OnBack = onBack;
            UiKit.Stretch(root);
            p.Make(root);
            return p;
        }

        static string Ovr(string key) { var t = MatchData.TeamByKey(key); return MatchData.Overall(t).ToString(); }

        void Make(RectTransform root)
        {
            var back = UiKit.Panel(root, "Backdrop", new Color(0.05f, 0.06f, 0.1f, 0.97f)); UiKit.Stretch(back.rectTransform);
            _teams = TeamCatalog.Teams.ToList();
            _cfg = MatchData.Current.Clone();
            if (TeamCatalog.ById(_cfg.HomeId) == null || _cfg.HomeId == _cfg.AwayId) { _cfg.HomeId = TeamCatalog.SavedTeamId; _cfg.AwayId = _teams[(TeamCatalog.IndexOf(_cfg.HomeId) + 1) % _teams.Count].Id; }

            var title = UiKit.Label(root, "TEAM SELECT", 64, UiColors.TextPrimary, true, TextAlignmentOptions.BottomLeft, "Title");
            UiKit.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(110, -50), new Vector2(760, 84));
            var rule = UiKit.Panel(root, "TitleRule", UiColors.Gold); UiKit.Anchor(rule.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(116, -138), new Vector2(200, 6));

            var col = UiKit.Rect(root, "Column");
            UiKit.Anchor(col, new Vector2(0, 1), new Vector2(0, 1), new Vector2(110, -168), new Vector2(800, 760));
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = 8; v.childAlignment = TextAnchor.UpperLeft; v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false; v.childForceExpandHeight = false;

            string[] names = _teams.Select(t => t.Name).ToArray();
            _homeSel = UiControls.Selector(col, "Home team", names, TeamCatalog.IndexOf(_cfg.HomeId), i => { _cfg.HomeId = _teams[i].Id; Refresh(); }, 800, 66);
            _awaySel = UiControls.Selector(col, "Away team", names, TeamCatalog.IndexOf(_cfg.AwayId), i => { _cfg.AwayId = _teams[i].Id; Refresh(); }, 800, 66);
            int qi = Array.IndexOf(MatchConfig.QuarterChoices, _cfg.QuarterMinutes); if (qi < 0) qi = 2;
            _quarterSel = UiControls.Selector(col, "Quarter length", MatchConfig.QuarterChoices.Select(m => m + " min").ToArray(), qi, i => { _cfg.QuarterMinutes = MatchConfig.QuarterChoices[i]; Refresh(); }, 800, 66);
            _rulesSel = UiControls.Selector(col, "Rules", new[] { "Pro", "College" }, _cfg.CollegeRules ? 1 : 0, i => { _cfg.CollegeRules = i == 1; Refresh(); }, 800, 66);
            _diffSel = UiControls.Selector(col, "Difficulty", new[] { "Rookie", "Pro", "All-Pro" }, (int)_cfg.Difficulty, i => { _cfg.Difficulty = (Difficulty)i; Refresh(); }, 800, 66);
            _sideSel = UiControls.Selector(col, "You play as", new[] { "Home team", "Away team" }, _cfg.UserTeam == PixFootball.Core.Team.Home ? 0 : 1, i => { _cfg.UserTeam = i == 0 ? PixFootball.Core.Team.Home : PixFootball.Core.Team.Away; Refresh(); }, 800, 66);
            var gap = UiKit.Rect(col, "Gap"); gap.sizeDelta = new Vector2(10, 14); gap.gameObject.AddComponent<LayoutElement>().preferredHeight = 14;
            var start = UiKit.MenuButton(col, "KICK OFF", KickOff, 800, 84, 40);
            UiKit.MenuButton(col, "BACK", () => { if (OnBack != null) OnBack(); }, 800, 66, 30);
            First = _homeSel;

            _summary = UiKit.Label(root, "", 24, UiColors.TextSecondary, false, TextAlignmentOptions.TopLeft, "Summary");
            UiKit.Anchor(_summary.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(116, 100), new Vector2(780, 60));

            _away = MakeCard(root, "AWAY", new Vector2(-340, 20));
            _home = MakeCard(root, "HOME", new Vector2(340, 20));
            var vs = UiKit.Label(root, "VS", 54, UiColors.Gold, true, TextAlignmentOptions.Center, "VS");
            UiKit.Anchor(vs.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(120, 80));
            // cards sit in the right 55% of the screen
            foreach (var r in new[] { _away.Wash.transform.parent, _home.Wash.transform.parent, vs.transform })
            {
                var rt = (RectTransform)r; rt.anchorMin = rt.anchorMax = new Vector2(0.77f, 0.5f);
            }
            Refresh();
        }

        Card MakeCard(RectTransform root, string role, Vector2 offset)
        {
            var c = new Card();
            var r = UiKit.Rect(root, role + "Card");
            UiKit.Anchor(r, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(offset.x * 0.66f, offset.y), new Vector2(380, 640));
            var bg = UiKit.Panel(r, "Bg", UiColors.BgPanel); UiKit.Stretch(bg.rectTransform);
            c.Wash = UiKit.Panel(r, "Wash", new Color(1, 1, 1, 0.55f)); UiKit.Stretch(c.Wash.rectTransform);
            c.Bar = UiKit.Panel(r, "Bar", UiColors.Gold); UiKit.Anchor(c.Bar.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(380, 8)); c.Bar.rectTransform.anchorMax = new Vector2(1, 1); c.Bar.rectTransform.sizeDelta = new Vector2(0, 8);
            c.Role = UiKit.Label(r, role, 28, UiColors.TextSecondary, true, TextAlignmentOptions.Center, "Role");
            UiKit.Anchor(c.Role.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(340, 40)); c.Role.characterSpacing = 6f;
            c.Crest = UiKit.Panel(r, "Crest", Color.white); c.Crest.preserveAspect = true;
            UiKit.Anchor(c.Crest.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -78), new Vector2(240, 240));
            c.City = UiKit.Label(r, "", 30, UiColors.TextSecondary, true, TextAlignmentOptions.Center, "City");
            UiKit.Anchor(c.City.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -336), new Vector2(360, 40)); c.City.characterSpacing = 3f;
            c.Nick = UiKit.Label(r, "", 46, Color.white, true, TextAlignmentOptions.Center, "Nick");
            UiKit.Anchor(c.Nick.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -376), new Vector2(370, 64)); c.Nick.characterSpacing = 2f;
            for (int i = 0; i < 3; i++)
            {
                c.Chips[i] = UiKit.Panel(r, "Chip" + i, Color.white);
                UiKit.Anchor(c.Chips[i].rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-56 + i * 56, -464), new Vector2(46, 22));
            }
            c.OvrLabel = UiKit.Label(r, "OVERALL", 22, UiColors.TextMuted, true, TextAlignmentOptions.Center, "OvrLabel");
            UiKit.Anchor(c.OvrLabel.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -506), new Vector2(300, 30)); c.OvrLabel.characterSpacing = 4f;
            c.Overall = UiKit.Label(r, "80", 88, UiColors.Gold, true, TextAlignmentOptions.Center, "Overall");
            UiKit.Anchor(c.Overall.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -534), new Vector2(300, 100));
            return c;
        }

        void Fill(Card c, string id)
        {
            var t = TeamCatalog.ById(id);
            if (t == null) return;
            var sp = UiKit.Sprite("crest_" + t.Id); c.Crest.sprite = sp; c.Crest.enabled = sp != null;
            c.City.text = t.City.ToUpperInvariant(); c.Nick.text = t.Nickname.ToUpperInvariant();
            Color p = t.PrimaryUi, s = t.SecondaryUi; Color tt; ColorUtility.TryParseHtmlString(t.TertiaryHex, out tt);
            c.Wash.color = new Color(p.r, p.g, p.b, 0.42f);
            c.Bar.color = p;
            c.Chips[0].color = p; c.Chips[1].color = s; c.Chips[2].color = tt;
            c.Overall.text = Ovr(id);
        }

        void Refresh()
        {
            if (_home == null) return;
            Fill(_home, _cfg.HomeId); Fill(_away, _cfg.AwayId);
            var user = _cfg.UserTeam == PixFootball.Core.Team.Home ? _home : _away;
            _home.Role.text = _cfg.UserTeam == PixFootball.Core.Team.Home ? "HOME  -  YOU" : "HOME  -  CPU";
            _away.Role.text = _cfg.UserTeam == PixFootball.Core.Team.Away ? "AWAY  -  YOU" : "AWAY  -  CPU";
            _home.Role.color = _cfg.UserTeam == PixFootball.Core.Team.Home ? UiColors.Gold : UiColors.TextSecondary;
            _away.Role.color = _cfg.UserTeam == PixFootball.Core.Team.Away ? UiColors.Gold : UiColors.TextSecondary;
            _summary.text = _cfg.QuarterMinutes + " minute quarters  /  " + (_cfg.CollegeRules ? "college" : "pro") + " rules  /  CPU " + (_cfg.Difficulty == Difficulty.AllPro ? "All-Pro" : _cfg.Difficulty.ToString())
                            + (_cfg.HomeId == _cfg.AwayId ? "   -   pick two different teams" : "");
        }

        void KickOff()
        {
            if (_cfg.HomeId == _cfg.AwayId)
            {
                _cfg.AwayId = _teams[(TeamCatalog.IndexOf(_cfg.HomeId) + 1) % _teams.Count].Id;
                _awaySel.SetOptions(_awaySel.Options, TeamCatalog.IndexOf(_cfg.AwayId));
                Refresh();
            }
            _cfg.Seed = 0; _cfg.CpuVsCpu = false;
            TeamCatalog.SavedTeamId = _cfg.UserTeam == PixFootball.Core.Team.Home ? _cfg.HomeId : _cfg.AwayId;
            PlayerPrefs.SetString("pix.match.home", _cfg.HomeId); PlayerPrefs.SetString("pix.match.away", _cfg.AwayId);
            PlayerPrefs.SetInt("pix.match.q", _cfg.QuarterMinutes); PlayerPrefs.Save();
            MatchData.Current = _cfg;
            GameSession.StartMatch(_cfg);
        }

        public void OnCancel(BaseEventData e) { if (OnBack != null) OnBack(); }
    }
}
