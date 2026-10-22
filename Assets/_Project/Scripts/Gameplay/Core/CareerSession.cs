using System;
using System.Collections.Generic;
using System.IO;
using PixFootball.Core;
using PixFootball.League;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>User preferences of one franchise that the engine save does not carry (match difficulty and quarter length). Stored next to the save in the slot meta file.</summary>
    [Serializable]
    public sealed class CareerPrefs
    {
        public int Difficulty = (int)Gameplay.Difficulty.Pro;
        public int QuarterMinutes = 5;
    }

    /// <summary>The small file next to a save: what the load screen shows without reading the (about 7 MB) save itself.</summary>
    [Serializable]
    public sealed class CareerSlotMeta
    {
        public int Slot;
        public bool Valid;
        public string Name = "", TeamKey = "", TeamName = "", Phase = "", Record = "", SavedAt = "";
        public int Year, Seasons;
        public long Bytes;
        public CareerPrefs Prefs = new CareerPrefs();
    }

    /// <summary>
    /// The franchise that is open (one at a time) and the bridge between the franchise UI and the Unity match. The match scene reads <see cref="MatchLeague"/> through
    /// <see cref="MatchData"/>, plays the user's scheduled game and hands the <see cref="GameResult"/> back through <see cref="CompleteMatch"/>; the main menu then reopens the franchise hub
    /// (<see cref="ReturnToFranchise"/>). Save slots are JsonUtility files of the engine's <see cref="CareerSave"/> under persistentDataPath/Career.
    /// </summary>
    public static class CareerSession
    {
        public const int SlotCount = 5;

        public static CareerSave Save { get; private set; }
        public static CareerPrefs Prefs { get; private set; }
        public static int Slot { get; private set; }
        public static bool Active { get { return Save != null; } }

        /// <summary>The league the match scene plays with while a franchise game is running (null = the standalone league of <see cref="MatchData"/>).</summary>
        public static ProLeague MatchLeague { get; private set; }
        /// <summary>The scheduled game that was launched and has not come back yet (-1 = none).</summary>
        public static int PendingGameId = -1;
        /// <summary>The main menu opens the franchise hub (instead of the tile hub) the next time it loads.</summary>
        public static bool ReturnToFranchise;
        /// <summary>One line about the last played game ("Result saved." or why it could not be posted), shown by the hub.</summary>
        public static string LastMessage = "";
        /// <summary>Tests and tools: replaces the folder the slots live in.</summary>
        public static string DirOverride;

        // ================================================================== slots
        public static string Dir
        {
            get
            {
                string d = DirOverride ?? Path.Combine(Application.persistentDataPath, "Career");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        static string SavePath(int slot) { return Path.Combine(Dir, "slot" + slot + ".json"); }
        static string MetaPath(int slot) { return Path.Combine(Dir, "slot" + slot + ".meta.json"); }

        public static CareerSlotMeta MetaOf(int slot)
        {
            var m = new CareerSlotMeta { Slot = slot };
            try
            {
                if (File.Exists(MetaPath(slot)) && File.Exists(SavePath(slot)))
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(MetaPath(slot)), m);
                    m.Slot = slot; m.Valid = true;
                }
            }
            catch (Exception e) { Debug.LogWarning("[PIX] career slot " + slot + " meta: " + e.Message); m.Valid = false; }
            return m;
        }

        public static List<CareerSlotMeta> Slots()
        {
            var l = new List<CareerSlotMeta>();
            for (int i = 1; i <= SlotCount; i++) l.Add(MetaOf(i));
            return l;
        }

        /// <summary>The most recently saved slot, or null when there is none.</summary>
        public static CareerSlotMeta Latest()
        {
            CareerSlotMeta best = null;
            foreach (var m in Slots()) if (m.Valid && (best == null || string.CompareOrdinal(m.SavedAt, best.SavedAt) > 0)) best = m;
            return best;
        }

        public static int FirstFreeSlot()
        {
            for (int i = 1; i <= SlotCount; i++) if (!MetaOf(i).Valid) return i;
            return 0;
        }

        public static void DeleteSlot(int slot)
        {
            try { if (File.Exists(SavePath(slot))) File.Delete(SavePath(slot)); if (File.Exists(MetaPath(slot))) File.Delete(MetaPath(slot)); }
            catch (Exception e) { Debug.LogWarning("[PIX] career delete: " + e.Message); }
        }

        // ================================================================== open / close
        /// <summary>Starts a new franchise with the given team (index into the league's team list) in a slot and saves it right away.</summary>
        public static CareerSave StartNew(int userTeamId, int seed, CareerPrefs prefs, int slot, string name = null, bool useLeagueFiles = true)
        {
            var o = new CareerOptions { Seed = seed, UserTeamId = userTeamId, Name = name ?? "PIX Football Career" };
            if (useLeagueFiles)
            {
                var defs = MatchData.Defs;
                if (defs != null && defs.Count == 32) o.Teams = defs;
            }
            var s = CareerFlow.NewCareer(o);
            Open(s, prefs ?? new CareerPrefs(), slot);
            if (slot > 0) SaveNow();
            return s;
        }

        public static void Open(CareerSave save, CareerPrefs prefs, int slot)
        {
            Save = save; Prefs = prefs ?? new CareerPrefs(); Slot = slot; PendingGameId = -1; MatchLeague = null; LastMessage = "";
        }

        public static void Close() { Save = null; Prefs = null; Slot = 0; PendingGameId = -1; MatchLeague = null; ReturnToFranchise = false; }

        public static bool Load(int slot, out string error)
        {
            error = "";
            try
            {
                var meta = MetaOf(slot);
                if (!meta.Valid) { error = "That slot is empty."; return false; }
                string json = File.ReadAllText(SavePath(slot));
                var s = JsonUtility.FromJson<CareerSave>(json);
                if (s == null || s.League == null || s.League.Pro == null) { error = "The save file is damaged."; return false; }
                s.Bind();
                Open(s, meta.Prefs, slot);
                return true;
            }
            catch (Exception e) { error = "Could not load: " + e.Message; Debug.LogWarning("[PIX] career load: " + e); return false; }
        }

        /// <summary>Writes the open franchise to its slot (temp file, then replace). Returns false with the reason when it fails.</summary>
        public static bool SaveNow(out string error)
        {
            error = "";
            if (Save == null) { error = "No franchise is open."; return false; }
            if (Slot <= 0) { error = "This franchise has no save slot."; return false; }
            try
            {
                string json = JsonUtility.ToJson(Save);
                string path = SavePath(Slot), tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                var m = MetaFor(Save, Prefs, Slot);
                m.Bytes = json.Length;
                File.WriteAllText(MetaPath(Slot), JsonUtility.ToJson(m));
                return true;
            }
            catch (Exception e) { error = "Could not save: " + e.Message; Debug.LogWarning("[PIX] career save: " + e); return false; }
        }

        public static bool SaveNow() { string e; return SaveNow(out e); }

        public static CareerSlotMeta MetaFor(CareerSave s, CareerPrefs prefs, int slot)
        {
            var t = s.UserTeam;
            return new CareerSlotMeta
            {
                Slot = slot, Valid = true, Name = s.Name, TeamKey = t.Key, TeamName = t.FullName, Phase = CareerText.PhaseLabel(s), Year = s.Year, Seasons = Math.Max(1, s.Year - s.StartYear + 1),
                Record = s.Season != null && !s.Season.IsEmpty ? s.Season.WinsOf(t.Id) + "-" + s.Season.LossesOf(t.Id) : "0-0",
                SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Prefs = prefs ?? new CareerPrefs()
            };
        }

        // ================================================================== the match
        /// <summary>The match config for a scheduled game: both career teams (their depth charts come from the career league), the user's side, the pro rules, weather and time by week and stadium.</summary>
        public static MatchConfig BuildMatchConfig(GameSetup setup, CareerPrefs prefs, CareerSave save)
        {
            prefs = prefs ?? new CareerPrefs();
            var cfg = new MatchConfig
            {
                HomeId = setup.Home.Key, AwayId = setup.Away.Key, QuarterMinutes = Mathf.Clamp(prefs.QuarterMinutes, 1, 15), CollegeRules = false,
                Difficulty = (Difficulty)Mathf.Clamp(prefs.Difficulty, 0, 2), UserTeam = setup.UserIsHome ? PixFootball.Core.Team.Home : PixFootball.Core.Team.Away,
                Seed = setup.WeatherSeed == 0 ? 1 : setup.WeatherSeed, CpuVsCpu = false
            };
            WeatherChoice w; TimeChoice t;
            PickConditions(setup.WeatherSeed, setup.Home, setup.Game != null ? setup.Game.Week : 1, out w, out t);
            cfg.Weather = w; cfg.Time = t;
            return cfg;
        }

        /// <summary>Weather and kickoff time from the game's seed, the week and the stadium: domes are always clear, snow only in the second half of the season.</summary>
        public static void PickConditions(int seed, PixFootball.League.Team home, int week, out WeatherChoice weather, out TimeChoice time)
        {
            uint h = unchecked((uint)seed * 2654435761u);
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
            int a = (int)(h % 100u), b = (int)((h >> 8) % 100u);
            time = b < 55 ? TimeChoice.Day : b < 70 ? TimeChoice.Dusk : TimeChoice.Night;
            if (home != null && home.IsDome) { weather = WeatherChoice.Clear; return; }
            if (a < 52) weather = WeatherChoice.Clear;
            else if (a < 72) weather = WeatherChoice.Overcast;
            else if (a < 85) weather = WeatherChoice.Rain;
            else if (a < 91) weather = WeatherChoice.Fog;
            else weather = week >= 11 ? WeatherChoice.Snow : WeatherChoice.Clear;
        }

        /// <summary>Launches the user's game of the current week in the match scene. False (with a reason) when there is no playable game.</summary>
        public static bool LaunchUserGame(out string error)
        {
            error = "";
            if (Save == null) { error = "No franchise is open."; return false; }
            var setup = CareerFlow.SetupUserGame(Save);
            if (setup == null) { error = "There is no game to play this week."; return false; }
            var cfg = BuildMatchConfig(setup, Prefs, Save);
            BeginPending(setup.Game.Id);
            SaveNow();
            GameSession.StartMatch(cfg);
            return true;
        }

        /// <summary>Marks a scheduled game as the one the match scene is about to play (the match then plays with the career league).</summary>
        public static void BeginPending(int gameId) { PendingGameId = gameId; MatchLeague = Save != null ? Save.Pro : null; ReturnToFranchise = true; LastMessage = ""; }

        public static bool Pending { get { return Save != null && PendingGameId >= 0 && MatchLeague != null; } }

        /// <summary>Called by the match when the game is over: posts the result to the franchise and saves. Returns the message for the hub.</summary>
        public static string CompleteMatch(GameResult r)
        {
            if (!Pending || r == null) return "";
            ScheduledGame g = CareerFlow.FindGame(Save, PendingGameId);
            if (g == null || g.Played) { PendingGameId = -1; return LastMessage = "That game was already played."; }
            // a playoff game that ended level cannot be posted: the home team takes it with a point (rare, the pro overtime is sudden death)
            if (g.Kind != GameKind.Regular && r.HomeScore == r.AwayScore) { r.HomeScore += 1; r.Home.Score = r.HomeScore; r.Overtime = true; }
            var res = CareerFlow.SubmitGame(Save, PendingGameId, r);
            PendingGameId = -1;
            if (res.Ok)
            {
                if (Save.UserBoxes != null) { Save.UserBoxes.Add(new StoredBox { Year = Save.Year, Week = g.Week, Result = r }); while (Save.UserBoxes.Count > CareerSave.MaxStoredBoxes) Save.UserBoxes.RemoveAt(0); }   // the played game's box score is saved with the career
                SaveNow();
            }
            if (res.Ok) { LastMessage = "Final: " + CareerText.TeamNick(Save, r.AwayTeamId) + " " + r.AwayScore + ", " + CareerText.TeamNick(Save, r.HomeTeamId) + " " + r.HomeScore + ". The result is in the books."; }
            else LastMessage = res.Message;
            return LastMessage;
        }

        /// <summary>The menu scene loaded: the match league override ends (the exhibition uses the standalone league again).</summary>
        public static void OnMenuLoaded() { MatchLeague = null; }
    }
}
