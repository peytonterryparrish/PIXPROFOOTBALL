using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.UI.Glass
{
    /// <summary>How a hub tile is dressed (direction 16 "Tile Hub Modern").</summary>
    public enum PlanLook
    {
        /// <summary>Hero / art tile: full-bleed art area (crests over a team-colour wash), shade from the bottom, title and sub on the shade.</summary>
        Art,
        /// <summary>Flat icon tile: charcoal body, a line icon in the middle, title and sub at the bottom.</summary>
        Flat,
        /// <summary>Wide flat tile: icon at the left, title and sub on the right (the low 162 high tiles).</summary>
        Wide,
        /// <summary>Information panel (not selectable): a few crests and a caption.</summary>
        Info
    }

    /// <summary>One tile of a hub page: what it says, where it sits (block units, top-left origin, y down) and what it does.</summary>
    public sealed class PlanTile
    {
        public string Id, Title, Sub, Badge, Icon;
        public PlanLook Look;
        public float X, Y, W, H;
        public bool Locked, Hero;
        public bool Selectable { get { return !Locked && Look != PlanLook.Info; } }
        public Vector2 Center(float blockH) { return new Vector2(X + W * 0.5f, blockH - (Y + H * 0.5f)); }
    }

    /// <summary>What the main menu knows when it lays the pages out (the live data behind the captions).</summary>
    public sealed class PlanInput
    {
        public bool WatchMode = true;
        public string CareerLine, ResumeBadge;
        public string RematchLine = "";
        public bool HasRematch = true;
        public string WatchLine = "Controller input: Off (watch)";
    }

    /// <summary>
    /// The page plans of the tile hub (pure, EditMode tested): positions follow the mock of direction 16 (hero 780 x 470, standard 380 x 292, wide 480 x 290 / 480 x 162, half 230 x 288 / 240 x 238, large 500 x 250,
    /// 3 columns at x 0 / 800 / 1300 of a 1800 x 784 block, 20 gutters). Nothing for the college / Road to Heisman mode is planned (owner, 2026-10-23: left out of v0.11.0); the tiles of features that do not exist
    /// yet (Online, Superstar, Mini Games, Trophy Room) are shown locked "COMING SOON", the way the spec asks, and only those four.
    /// </summary>
    public static class MenuHubPlan
    {
        public static readonly Vector2 BlockSize = new Vector2(1800f, 784f);
        public static readonly string[] TabNames = { "HOME", "PLAY", "SETTINGS" };
        public const int Home = 0, Play = 1, Settings = 2;

        static PlanTile T(string id, string title, string sub, string icon, PlanLook look, float x, float y, float w, float h, bool locked = false, string badge = null, bool hero = false)
        {
            return new PlanTile { Id = id, Title = title, Sub = sub, Icon = icon, Look = look, X = x, Y = y, W = w, H = h, Locked = locked, Badge = badge, Hero = hero };
        }

        static string PlaySub(PlanInput p) { return p.WatchMode ? "Pick two teams, sit back and watch the game" : "Pick two teams, the rules and the weather"; }

        public static List<PlanTile> Page(int page, PlanInput p)
        {
            if (p == null) p = new PlanInput();
            var l = new List<PlanTile>();
            switch (page)
            {
                case Home:
                    l.Add(T("play", "PLAY NOW", PlaySub(p), "fb_football_laces", PlanLook.Art, 0, 0, 780, 470, false, "FEATURED", true));
                    l.Add(T("franchise", "FRANCHISE", p.CareerLine ?? "Build a dynasty: 32 teams, endless seasons", "fb_franchise_hub", PlanLook.Art, 0, 492, 380, 292, false, p.ResumeBadge));
                    if (p.HasRematch) l.Add(T("rematch", "QUICK REMATCH", p.RematchLine, "fb_football_laces", PlanLook.Art, 400, 492, 380, 292, false, "LAST GAME"));
                    else l.Add(T("rematch", "QUICK REMATCH", "Play a game first", "fb_football_laces", PlanLook.Flat, 400, 492, 380, 292, true));
                    l.Add(T("online", "ONLINE HEAD TO HEAD", "", "fb_radar_scan", PlanLook.Flat, 800, 0, 480, 290, true));
                    l.Add(T("superstar", "SUPERSTAR", "", "fb_roster_jersey", PlanLook.Flat, 800, 312, 230, 288, true));
                    l.Add(T("trophy", "TROPHY ROOM", "", "ui_trophy", PlanLook.Flat, 1050, 312, 230, 288, true));
                    l.Add(T("minigames", "MINI GAMES", "", "sym_goalpost", PlanLook.Wide, 800, 622, 480, 162, true));
                    l.Add(T("freeroam", "FREE ROAM", "Explore the empty stadium", "fb_field_compass", PlanLook.Art, 1300, 0, 500, 250));
                    l.Add(T("settings", "SETTINGS", "All options", "ui_settings", PlanLook.Flat, 1300, 272, 240, 238));
                    l.Add(T("quit", "QUIT", "Back to desktop", "ui_close", PlanLook.Flat, 1560, 272, 240, 238));
                    l.Add(T("rankings", "POWER RANKINGS", "The top four teams of the league", null, PlanLook.Info, 1300, 532, 500, 252));
                    break;
                case Play:
                    l.Add(T("play", "PLAY NOW", PlaySub(p), "fb_football_laces", PlanLook.Art, 0, 0, 880, 380, false, "EXHIBITION", true));
                    l.Add(T("franchise", "FRANCHISE", p.CareerLine ?? "Build a dynasty: 32 teams, endless seasons", "fb_franchise_hub", PlanLook.Art, 900, 0, 900, 380, false, p.ResumeBadge));
                    if (p.HasRematch) l.Add(T("rematch", "QUICK REMATCH", p.RematchLine, "fb_football_laces", PlanLook.Art, 0, 400, 590, 384, false, "LAST GAME"));
                    else l.Add(T("rematch", "QUICK REMATCH", "Play a game first", "fb_football_laces", PlanLook.Flat, 0, 400, 590, 384, true));
                    l.Add(T("freeroam", "FREE ROAM", "Explore the empty stadium", "fb_field_compass", PlanLook.Art, 610, 400, 590, 384));
                    l.Add(T("online", "ONLINE", "", "fb_radar_scan", PlanLook.Wide, 1220, 400, 580, 114, true));
                    l.Add(T("superstar", "SUPERSTAR", "", "fb_roster_jersey", PlanLook.Wide, 1220, 534, 580, 114, true));
                    l.Add(T("minigames", "MINI GAMES", "", "sym_goalpost", PlanLook.Wide, 1220, 668, 580, 116, true));
                    break;
                default:
                    l.Add(T("allsettings", "ALL SETTINGS", p.WatchLine, "fb_tuning_sliders", PlanLook.Flat, 0, 0, 780, 784, false, "OPTIONS", true));
                    l.Add(T("video", "VIDEO", "Display and quality", "ui_eye", PlanLook.Flat, 800, 0, 480, 250));
                    l.Add(T("gameplay", "GAMEPLAY", "Watch mode, helper bar, speed", "ui_controller", PlanLook.Flat, 1300, 0, 500, 250));
                    l.Add(T("camera", "CAMERA", "Views and shake", "fb_film_reel", PlanLook.Flat, 800, 270, 480, 250));
                    l.Add(T("broadcast", "BROADCAST", "Overlays and shows", "fb_video_review", PlanLook.Flat, 1300, 270, 500, 250));
                    l.Add(T("audio", "AUDIO", "Music, crowd, effects", "fb_audio_levels", PlanLook.Flat, 800, 540, 480, 244));
                    l.Add(T("quit", "QUIT", "Back to desktop", "ui_close", PlanLook.Flat, 1300, 540, 500, 244));
                    break;
            }
            return l;
        }

        /// <summary>The settings page a tile opens (0 video .. 4 audio), -1 for tiles that are not settings pages.</summary>
        public static int SettingsPageOf(string id)
        {
            switch (id) { case "video": case "allsettings": return 0; case "gameplay": return 1; case "camera": return 2; case "broadcast": return 3; case "audio": return 4; default: return -1; }
        }

        /// <summary>Pure check used by the tests: true when two tiles overlap.</summary>
        public static bool Overlap(PlanTile a, PlanTile b)
        {
            return a.X < b.X + b.W - 0.5f && b.X < a.X + a.W - 0.5f && a.Y < b.Y + b.H - 0.5f && b.Y < a.Y + a.H - 0.5f;
        }

        /// <summary>Ticker, chip and hub captions: pure text helpers.</summary>
        public static string ChipName(string team) { return string.IsNullOrEmpty(team) ? "COACH" : "COACH " + team.ToUpperInvariant(); }
    }
}
