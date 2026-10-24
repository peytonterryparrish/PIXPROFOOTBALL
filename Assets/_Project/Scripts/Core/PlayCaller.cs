using System;
using System.Collections.Generic;

namespace PixFootball.Core
{
    /// <summary>Offensive philosophy of a coach (mirrors League.OffenseScheme without depending on the League assembly).</summary>
    public enum PlayStyle { Balanced, AirRaid, WestCoast, PowerRun, SpreadOption }

    /// <summary>The coach preferences that shape CPU play calling and the "Ask Coach" suggestions.</summary>
    public struct CoachProfile
    {
        /// <summary>0-100: share of calls that are passes in neutral situations (50 = balanced).</summary>
        public int PassTendency;
        /// <summary>0-100: blitz frequency, fourth-down boldness.</summary>
        public int Aggression;
        public PlayStyle Style;
        /// <summary>QA-621: the defensive coordinator's base front. -1 = 4-3 team, 0 = no preference, +1 = 3-4 team (shifts the base-front choice by up to 0.35). Not used for the offensive staff.</summary>
        public float FrontBias;
        /// <summary>0 = no preference, 1 = a nickel-first coordinator (adds up to 0.4 to the chance of the nickel against two-back personnel).</summary>
        public float NickelBias;
        /// <summary>v0.8.0 staff tendencies (from the coordinators, see StaffManager.Tendencies), relative to the league-average staff = 0: BlitzBias and ManBias are added to the chance of a blitz call and of a man-coverage call, the weight of deep passes is multiplied by 1 + DeepShotBias, and NoHuddleBias (extra share of snaps without a huddle) shortens the clock runoff between snaps (PhysicalGameSim.ApplyTempo).</summary>
        public float BlitzBias, ManBias, DeepShotBias, NoHuddleBias;

        public static CoachProfile Neutral { get { return new CoachProfile { PassTendency = 50, Aggression = 50, Style = PlayStyle.Balanced }; } }
        /// <summary>Symmetric bands around the neutral 50 (QA-500): 57 and up reads PASS HEAVY, 43 and down reads RUN HEAVY.</summary>
        public const int PassHeavyAt = 57, RunHeavyAt = 43;
        public string Label { get { return PassTendency >= PassHeavyAt ? "PASS HEAVY" : (PassTendency <= RunHeavyAt ? "RUN HEAVY" : "BALANCED"); } }
    }

    public struct CoachPick
    {
        public Play Play;
        public string Reason;
        public float Score;
    }

    /// <summary>
    /// Situational play calling from the full playbook for the CPU (and the "Ask Coach" button): down, distance, field zone, score and time,
    /// the coach's tendencies, hurry-up football in the two-minute drill, and repetition avoidance. Pure and deterministic given the Rng.
    /// </summary>
    public sealed class PlayCaller
    {
        public readonly Playbook Book;
        readonly Rng _rng;
        readonly List<Play> _offense = new List<Play>(), _defense = new List<Play>();

        public PlayCaller(Playbook book, Rng rng)
        {
            Book = book; _rng = rng;
            foreach (Play p in book.AllPlays)
            {
                if (p.Side == PlaySide.Offense && p.Category != PlayCategory.Kneel && p.Category != PlayCategory.Spike && p.Name != "Hail Mary") _offense.Add(p);
                else if (p.Side == PlaySide.Defense) _defense.Add(p);
            }
        }

        // ================================================================================================ helpers
        public static string Concept(string name)
        {
            if (name.EndsWith(" Right")) return name.Substring(0, name.Length - 6);
            if (name.EndsWith(" Left")) return name.Substring(0, name.Length - 5);
            return name;
        }

        public static bool IsHurryUp(GameState s)
        {
            if (!(s.Quarter == 2 || s.Quarter == 4)) return false;
            int diff = s.PossessionScoreDiff;
            if (s.Quarter == 4) return s.NormClock <= 240f && diff < 0 || s.NormClock <= 100f && diff <= 0;   // QA-501: the same windows the QuickSim CPU uses (trailing from ~4:00, level from ~1:40)
            return s.NormClock <= 100f;
        }

        public static bool ShouldSpike(GameState s)
        {
            if (!IsHurryUp(s) || s.Phase != GamePhase.Scrimmage || s.Down >= 4) return false;
            if (!s.ClockRunning || s.PendingRunoff <= 0f) return false;   // nothing to save when the clock is already stopped (incompletion, out of bounds, timeout)
            if (s.NormClock > 40f) return false;
            int diff = s.PossessionScoreDiff;
            if (diff > 0 && s.Quarter == 4) return false;
            return s.TimeoutsOf(s.Possession) == 0 || s.NormClock <= 14f;
        }

        public static bool ShouldKneel(GameState s)
        {
            return s.Quarter == 4 && s.PossessionScoreDiff > 0 && s.Phase == GamePhase.Scrimmage && s.Down <= 3 && s.BallSpot >= 5   // never kneel the ball into your own end zone
                   && s.NormClock < 130f && s.TimeoutsOf(s.Possession.Opponent()) == 0;
        }

        /// <summary>The last snap of a half or game for a team that must score from beyond field-goal range: throw the Hail Mary.</summary>
        public static bool ShouldHailMary(GameState s)
        {
            if (s.Phase != GamePhase.Scrimmage || !(s.Quarter == 2 || s.Quarter == 4)) return false;
            if (s.NormClock > 9f || s.BallSpot < 40 || s.BallSpot > 62) return false;
            if (s.Quarter == 4 && s.PossessionScoreDiff >= 0) return false;
            if (s.Quarter == 4 && s.PossessionScoreDiff < -8) return false;
            return true;
        }

        /// <summary>
        /// CPU timeout decision (the same rule the live match director follows): the team that needs the clock stops it in the last
        /// two minutes of a half while the runoff is ticking; a team on offense waits until the clock is under 90 seconds.
        /// </summary>
        public static bool WantsTimeout(GameState s, Team t, Rng rng)
        {
            if (s.Phase != GamePhase.Scrimmage || !s.ClockRunning || s.PendingRunoff <= 0f) return false;
            if (!(s.Quarter == 2 || s.Quarter == 4) || s.NormClock > 130f) return false;
            if (s.TimeoutsOf(t) <= 0) return false;
            int diff = s.ScoreOf(t) - s.ScoreOf(t.Opponent());
            bool wantsClock = diff < 0 || (s.Quarter == 2 && diff <= 0 && s.Possession == t);
            if (!wantsClock) return false;
            if (s.Possession == t && s.NormClock > 90f) return false;
            return rng == null || rng.Chance(0.5f);
        }

        static bool SidelineRoute(Play p)
        {
            foreach (Assignment a in p.Assignments)
            {
                var r = a as RouteAssignment;
                if (r == null) continue;
                if (r.Route == RouteKind.Out || r.Route == RouteKind.Corner || r.Route == RouteKind.Comeback || r.Route == RouteKind.Fade || r.Route == RouteKind.Flat
                    || r.Route == RouteKind.Corner2 || r.Route == RouteKind.OutAndUp) return true;
            }
            return false;
        }

        // ================================================================================================ offense
        /// <summary>Share of passes the situation calls for (0-1), before repetition effects.</summary>
        public static float PassBias(GameState s, CoachProfile c)
        {
            int toGoal = 100 - s.BallSpot;
            float p;
            if (s.Down == 1) p = 0.46f;
            else if (s.Down == 2) p = s.Distance <= 3 ? 0.34f : (s.Distance <= 7 ? 0.50f : 0.66f);
            else if (s.Down == 3) p = s.Distance <= 2 ? 0.34f : (s.Distance <= 6 ? 0.64f : 0.86f);
            else p = s.Distance <= 2 ? 0.30f : 0.80f;
            p += (c.PassTendency - 50) * 0.004f;
            if (c.Style == PlayStyle.AirRaid) p += 0.07f; else if (c.Style == PlayStyle.PowerRun) p -= 0.08f; else if (c.Style == PlayStyle.WestCoast) p += 0.02f;
            if (s.BallSpot <= 8) p -= 0.10f;                          // backed up: safe calls
            if (toGoal <= 3) p = Math.Min(p, 0.42f) + (s.Down >= 3 ? 0.1f : 0f);
            else if (toGoal <= 20) p -= 0.04f;
            int diff = s.PossessionScoreDiff;
            if (s.Quarter >= 4 && diff < -7 && s.NormClock < 600f) p = Math.Max(p, 0.82f);
            if (s.Quarter >= 4 && diff > 7 && s.NormClock < 600f) p = Math.Min(p, 0.26f);
            if (s.Quarter >= 4 && diff > 0 && s.NormClock < 240f) p = Math.Min(p, 0.22f);
            if (IsHurryUp(s) && diff <= 0) p = Math.Max(p, 0.90f);
            return Math.Max(0.05f, Math.Min(0.95f, p));
        }

        float Weight(Play p, GameState s, CoachProfile c, bool wantPass, List<Play> recent, bool hurry)
        {
            float w = 1f;
            int toGoal = 100 - s.BallSpot;
            Formation form = Book.FindFormation(p.FormationName);
            if (form == null) return 0f;   // QA-499: a custom play whose formation is missing from the book is never picked (it used to throw)
            // personnel / formation fit
            bool passing = wantPass;
            string fam = form.Family;
            if (passing)
            {
                if (fam == DefaultPlaybooks.ShotgunF || fam == DefaultPlaybooks.Trips || fam == DefaultPlaybooks.Empty) w *= 1.5f;
                if (fam == DefaultPlaybooks.IForm || fam == DefaultPlaybooks.GoalLine) w *= 0.45f;
                if (fam == DefaultPlaybooks.Empty && s.Distance < 7) w *= 0.5f;
            }
            else
            {
                if (fam == DefaultPlaybooks.IForm || fam == DefaultPlaybooks.Singleback || fam == DefaultPlaybooks.Pistol) w *= 1.35f;
                if (fam == DefaultPlaybooks.Empty || fam == DefaultPlaybooks.Trips) w *= 0.2f;
            }
            if (toGoal <= 3) { if (fam == DefaultPlaybooks.GoalLine) w *= 36f; else w *= 0.4f; }   // (v0.10.0: 36, was 14: the book has 22 formations now, the goal-line family is one of them)
            else if (fam == DefaultPlaybooks.GoalLine) w *= 0.15f;
            if (c.Style == PlayStyle.PowerRun && (fam == DefaultPlaybooks.IForm || fam == DefaultPlaybooks.GoalLine)) w *= 1.4f;
            if (c.Style == PlayStyle.AirRaid && (fam == DefaultPlaybooks.ShotgunF || fam == DefaultPlaybooks.Empty)) w *= 1.3f;
            if (c.Style == PlayStyle.SpreadOption && (fam == DefaultPlaybooks.ShotgunF || fam == DefaultPlaybooks.Pistol)) w *= 1.3f;

            switch (p.Category)
            {
                case PlayCategory.InsideRun:
                    w *= s.Distance <= 3 ? 2.4f : (s.Distance >= 8 ? 0.45f : 1f);
                    if (p.Name.StartsWith("Dive") || p.Name.StartsWith("Iso") || p.Name.StartsWith("Power")) w *= s.Distance <= 3 || toGoal <= 5 ? 1.7f : 0.7f;
                    if (p.Name.StartsWith("Counter") || p.Name.StartsWith("Trap")) w *= 0.7f;
                    break;
                case PlayCategory.OutsideRun: w *= s.Distance >= 4 ? 1.2f : 0.7f; break;
                case PlayCategory.Draw: w *= (s.Down >= 2 && s.Distance >= 7) ? 1.6f : 0.35f; break;
                case PlayCategory.QbRun:
                    if (p.Name == "QB Sneak") w *= (s.Distance <= 1 && (toGoal <= 4 || s.Down >= 3)) ? 5f : 0f;
                    else w *= 0.4f;
                    break;
                case PlayCategory.ShortPass:
                    w *= s.Distance <= 5 ? 1.8f : (s.Distance >= 11 ? 0.5f : 1f);
                    if (c.Style == PlayStyle.WestCoast) w *= 1.4f;
                    break;
                case PlayCategory.MediumPass: w *= s.Distance >= 6 ? 1.5f : 0.8f; break;
                case PlayCategory.DeepPass:
                    w *= s.Distance >= 9 ? 1.2f : 0.45f;
                    if (c.DeepShotBias != 0f) w *= Math.Max(0.3f, 1f + c.DeepShotBias);
                    if (c.Style == PlayStyle.AirRaid) w *= 1.4f;
                    if (toGoal < 12 && p.Name != "Fade") w *= 0.3f;
                    if (p.Name == "Fade") w *= toGoal <= 8 ? 3f : 0.4f;
                    break;
                case PlayCategory.PlayAction:
                    w *= (s.Down <= 2 && s.Distance <= 7) ? 1.8f : 0.4f;
                    break;
                case PlayCategory.Screen: w *= (s.Down >= 2 && s.Distance >= 8) ? 1.2f : 0.25f; break;
            }
            if (toGoal <= 12 && p.IsPass && (p.Category == PlayCategory.MediumPass || p.Category == PlayCategory.ShortPass)) w *= 1.2f;
            if (hurry && p.IsPass)
            {
                if (SidelineRoute(p)) w *= 8f;
                if (p.Category == PlayCategory.Screen || p.Category == PlayCategory.PlayAction) w *= 0.3f;
            }
            if (hurry && p.IsRun) w *= 0.3f;
            // avoid repeating
            int rep = 0;
            for (int i = 0; i < recent.Count; i++)
            {
                if (recent[i] == null) continue;
                if (recent[i].Name == p.Name) rep += 3;
                else if (Concept(recent[i].Name) == Concept(p.Name)) rep += 2;
                else if (recent[i].Category == p.Category && i < 2) rep += 1;
            }
            w *= (float)Math.Pow(0.5, rep);
            return w;
        }

        /// <summary>Picks the offensive call for a scrimmage down (no kicking decisions: see <see cref="FourthDown"/>).</summary>
        public Play CallOffense(GameState s, CoachProfile c, IList<Play> recent)
        {
            var scored = Score(s, c, recent, false, false);
            return WeightedPick(scored);
        }

        public List<CoachPick> SuggestOffense(GameState s, CoachProfile c, IList<Play> recent, int count)
        {
            var list = Score(s, c, recent, true, true);
            list.Sort((a, b) => b.Score.CompareTo(a.Score));
            var picks = new List<CoachPick>();
            var seen = new HashSet<string>();
            foreach (CoachPick p in list)
            {
                if (!seen.Add(Concept(p.Play.Name))) continue;
                picks.Add(new CoachPick { Play = p.Play, Reason = ReasonFor(p.Play, s, c), Score = p.Score });
                if (picks.Count >= count) break;
            }
            return picks;
        }

        List<CoachPick> Score(GameState s, CoachProfile c, IList<Play> recent, bool forSuggest, bool useBothSides)
        {
            float pPass = PassBias(s, c);
            bool hurry = IsHurryUp(s);
            var rec = new List<Play>(recent ?? new List<Play>());
            var res = new List<CoachPick>(_offense.Count);
            foreach (Play p in _offense)
            {
                if (p.Category == PlayCategory.Kneel || p.Category == PlayCategory.Spike) continue;
                if (s.Phase == GamePhase.PointAfter && p.Category == PlayCategory.Screen) continue;
                float share = p.IsPass ? pPass : (p.IsRun ? 1f - pPass : 0.05f);
                if (p.Category == PlayCategory.QbRun && p.Name != "QB Sneak") share *= 0.4f;
                float w = share * Weight(p, s, c, p.IsPass, rec, hurry);
                // spread the pass / run mass over its plays so the tendency is respected whatever the playbook's size
                res.Add(new CoachPick { Play = p, Score = w });
            }
            // normalise by group so that 60 pass plays do not outweigh 12 run plays
            float sumP = 0f, sumR = 0f;
            foreach (CoachPick k in res) { if (k.Play.IsPass) sumP += k.Score; else sumR += k.Score; }
            for (int i = 0; i < res.Count; i++)
            {
                CoachPick k = res[i];
                float denom = k.Play.IsPass ? sumP : sumR;
                k.Score = denom > 0f ? k.Score / denom * (k.Play.IsPass ? pPass : 1f - pPass) : 0f;
                res[i] = k;
            }
            return res;
        }

        Play WeightedPick(List<CoachPick> picks)
        {
            float total = 0f;
            foreach (CoachPick p in picks) total += p.Score;
            if (total <= 0f) return picks.Count > 0 ? picks[0].Play : null;
            float r = _rng.NextFloat() * total;
            foreach (CoachPick p in picks) { r -= p.Score; if (r <= 0f) return p.Play; }
            return picks[picks.Count - 1].Play;
        }

        string ReasonFor(Play p, GameState s, CoachProfile c)
        {
            int toGoal = 100 - s.BallSpot;
            if (IsHurryUp(s) && p.IsPass) return "TWO-MINUTE: GET OUT OF BOUNDS";
            if (toGoal <= 3) return "GOAL LINE: GET IT IN";
            if (s.Down == 3 && s.Distance >= 7) return "3RD & LONG: ATTACK THE STICKS";
            if (s.Down >= 3 && s.Distance <= 2) return "SHORT YARDAGE: POWER";
            if (s.BallSpot <= 8) return "BACKED UP: PLAY IT SAFE";
            if (toGoal <= 20) return "RED ZONE: " + (p.IsPass ? "HIT A WINDOW" : "PUNCH IT");
            if (p.Category == PlayCategory.PlayAction) return "SELL THE RUN, SHOT DOWNFIELD";
            if (p.Category == PlayCategory.Screen) return "DEFENSE IS PRESSING: SCREEN";
            if (p.IsRun) return s.Down == 1 ? "1ST DOWN: SET UP THE PASS" : "KEEP THE CHAINS MOVING";
            if (p.Category == PlayCategory.DeepPass) return "TAKE A SHOT";
            return c.PassTendency >= 58 ? "COACH LIKES TO THROW" : "MOVE THE CHAINS";
        }

        // ================================================================================================ fourth down / special calls
        public enum FourthCall { Go, Punt, FieldGoal }

        /// <summary>The coach's fourth-down decision (the same rules the CPU follows).</summary>
        public FourthCall FourthDown(GameState s, CoachProfile c, int maxFgYards = 0)
        {
            int toGoal = 100 - s.BallSpot;
            int fgDist = toGoal + 17;
            bool college = s.Rules.CollegeStyle;
            bool fgRange = fgDist <= (maxFgYards > 0 ? maxFgYards : (college ? 50 : 54));   // the kicker's own range when the caller knows it
            int diff = s.PossessionScoreDiff;
            bool late = s.Quarter == 4 && s.NormClock < 240f;
            bool desperate = late && diff < 0;
            float boldness = (c.Aggression - 50) * 0.01f;
            if (toGoal <= 2 && s.Distance <= 1 && fgRange && !desperate && _rng.Chance(0.35f + boldness)) return FourthCall.Go;
            if (s.Distance <= 2 && toGoal <= 45 && toGoal > 3 && (_rng.Chance(0.40f + boldness * 0.6f) || desperate && toGoal > 20)) return FourthCall.Go;
            if (s.Distance <= 1 && toGoal <= 62 && toGoal > 45 && _rng.Chance(0.32f + boldness * 0.5f)) return FourthCall.Go;      // v0.11.0 SIM-FLOW (B-074): fourth and inches from the 40 to midfield is a go about a third of the time
            if (desperate && s.Distance > 3 && s.BallSpot > 35 && !fgRange) return FourthCall.Go;
            if (fgRange && !(desperate && diff < -3)) return FourthCall.FieldGoal;
            if (s.BallSpot >= 35 || !desperate) return FourthCall.Punt;
            return FourthCall.Go;
        }

        // ================================================================================================ defense
        /// <summary>Defensive formation for the offense's personnel and the situation.</summary>
        public string PickDefensiveFormation(GameState s, CoachProfile c, string offUnit, string offFamily)
        {
            int toGoal = 100 - s.BallSpot;
            if (toGoal <= 3 && (offFamily == DefaultPlaybooks.GoalLine || offUnit == "22" || offUnit == "21" || _rng.Chance(0.5f))) return DefaultPlaybooks.GoalLineD;
            bool longYardage = s.Distance >= 7 && s.Down >= 2;
            string f = PickBaseFormation(s, c, offUnit, longYardage);
            // v0.10.0: the other fronts of the book: the 4-3 over front, the 3-3-5 against three receivers, the 46 against a heavy set
            if (f == DefaultPlaybooks.Base43) { if (_rng.Chance(0.06f) && (offUnit == "21" || offUnit == "22")) return DefaultPlaybooks.Bear46; return _rng.Chance(0.30f) ? DefaultPlaybooks.Over43 : f; }
            if (f == DefaultPlaybooks.Nickel && (offUnit == "11" || offUnit == "20") && !longYardage && _rng.Chance(0.10f)) return DefaultPlaybooks.Nickel335;
            return f;
        }

        string PickBaseFormation(GameState s, CoachProfile c, string offUnit, bool longYardage)
        {
            switch (offUnit)
            {
                case "01": return longYardage || _rng.Chance(0.45f) ? DefaultPlaybooks.Dime : DefaultPlaybooks.Nickel;
                case "10": return longYardage && _rng.Chance(0.5f) || _rng.Chance(0.25f) ? DefaultPlaybooks.Dime : DefaultPlaybooks.Nickel;
                case "20": case "11": return longYardage && s.Distance >= 10 && _rng.Chance(0.4f) ? DefaultPlaybooks.Dime : DefaultPlaybooks.Nickel;
                case "21": if (c.NickelBias > 0f && _rng.Chance(0.4f * c.NickelBias)) return DefaultPlaybooks.Nickel; return _rng.Chance(Math.Max(0.02f, Math.Min(0.95f, 0.35f + 0.35f * c.FrontBias))) ? DefaultPlaybooks.Base34 : DefaultPlaybooks.Base43;
                case "12": if (c.NickelBias > 0f && _rng.Chance(0.3f * c.NickelBias)) return DefaultPlaybooks.Nickel; return _rng.Chance(Math.Max(0.02f, Math.Min(0.95f, 0.25f + 0.35f * c.FrontBias))) ? DefaultPlaybooks.Base34 : DefaultPlaybooks.Base43;
                case "22": return _rng.Chance(Math.Max(0.02f, Math.Min(0.95f, 0.4f + 0.35f * c.FrontBias))) ? DefaultPlaybooks.Base34 : DefaultPlaybooks.Base43;
                default: return DefaultPlaybooks.Base43;
            }
        }

        /// <summary>CPU defensive call: personnel matches the offense's formation, coverage by situation, blitz frequency by aggression.</summary>
        public Play CallDefense(GameState s, CoachProfile c, Formation offFormation)
        {
            string unit = offFormation != null ? offFormation.Unit : "11";
            string family = offFormation != null ? offFormation.Family : DefaultPlaybooks.ShotgunF;
            string formation = PickDefensiveFormation(s, c, unit, family);
            int toGoal = 100 - s.BallSpot;
            int diff = s.PossessionScoreDiff; // offense perspective
            bool late = s.Quarter == 4 && s.NormClock < 110f;

            var pool = new List<Play>();
            foreach (Play p in _defense) if (p.FormationName == formation) pool.Add(p);
            if (pool.Count == 0) { foreach (Play p in _defense) if (p.FormationName == DefaultPlaybooks.Base43) pool.Add(p); }
            if (toGoal <= 3)
            {
                var gl = new List<Play>(); foreach (Play p in pool) if (p.Category == PlayCategory.GoalLine) gl.Add(p);
                if (gl.Count > 0) return gl[_rng.Range(0, gl.Count)];
            }
            if (late && diff < 0 && s.BallSpot < 72)
            {
                var prev = new List<Play>(); foreach (Play p in _defense) if (p.Category == PlayCategory.Prevent) prev.Add(p);
                if (prev.Count > 0 && _rng.Chance(0.55f)) return prev[_rng.Range(0, prev.Count)];
            }
            float aggr = c.Aggression / 100f;
            float pBlitz = 0.07f + aggr * 0.26f;
            pBlitz += c.BlitzBias;
            if (s.Down == 3 && s.Distance >= 6) pBlitz += 0.08f;
            if (s.Distance <= 2) pBlitz += 0.05f;
            if (toGoal <= 12) pBlitz += 0.04f;
            if (late && diff > 0) pBlitz *= 0.4f;
            float pMan = 0.30f + (toGoal <= 12 ? 0.12f : 0f);
            pMan += c.ManBias;
            if (s.Down == 3 && s.Distance >= 8) pMan -= 0.1f;
            float r = _rng.NextFloat();
            PlayCategory want = r < pBlitz ? PlayCategory.Blitz : (r < pBlitz + pMan ? PlayCategory.ManCoverage : PlayCategory.ZoneCoverage);
            var cands = new List<Play>();
            foreach (Play p in pool) if (p.Category == want) cands.Add(p);
            if (cands.Count == 0) foreach (Play p in pool) if (p.Category == PlayCategory.ZoneCoverage) cands.Add(p);
            if (cands.Count == 0) cands = pool;
            if (s.Down == 3 && s.Distance >= 9 && want == PlayCategory.ZoneCoverage)
            {
                var deep = cands.FindAll(p => p.Scheme == CoverageScheme.Cover4 || p.Scheme == CoverageScheme.Cover2 || p.Scheme == CoverageScheme.Cover6);
                if (deep.Count > 0) cands = deep;
            }
            return cands[_rng.Range(0, cands.Count)];
        }

        public List<CoachPick> SuggestDefense(GameState s, CoachProfile c, int count)
        {
            // the coach assumes the offense lines up in its most likely personnel
            var res = new List<CoachPick>();
            int toGoal = 100 - s.BallSpot;
            string[] wanted;
            string why;
            if (toGoal <= 3) { wanted = new[] { "Goal Line Plug Cover 2", "Goal Line Gap Control", "Goal Line Cover 0" }; why = "GOAL LINE: STACK THE BOX"; }
            else if (s.Down >= 3 && s.Distance >= 8) { wanted = new[] { "Cover 4 Quarters", "Cover 2 Zone", "Cover 3 Sky", "Dime Quarters Spy" }; why = "LONG YARDAGE: KEEP EVERYTHING IN FRONT"; }
            else if (s.Distance <= 2) { wanted = new[] { "Mike Blitz Cover 1", "Cover 1 Robber", "Cover 3 Sky", "Cover 2 Man Under" }; why = "SHORT YARDAGE: DEFEND THE RUN"; }
            else if (s.Quarter >= 4 && s.NormClock < 110f && s.PossessionScoreDiff < 0) { wanted = new[] { "Prevent Shell", "Cover 4 Quarters", "Cover 2 Zone" }; why = "LATE LEAD: NO BIG PLAYS"; }
            else { wanted = new[] { "Cover 3 Sky", "Cover 2 Zone", "Cover 1 Robber", "Cover 4 Quarters", "Mike Blitz Cover 1" }; why = c.Aggression >= 60 ? "AGGRESSIVE COACH: PRESSURE" : "BALANCED LOOK"; }
            foreach (string n in wanted)
            {
                foreach (Play p in _defense)
                    if (p.Name == n) { res.Add(new CoachPick { Play = p, Reason = why, Score = 1f }); break; }
                if (res.Count >= count) break;
            }
            return res;
        }
    }
}

