using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>Coaching-staff derived numbers the Sim's play caller can read (data only; the Sim is not changed in v0.8.0).</summary>
    public sealed class PlaycallTendencies
    {
        /// <summary>Share of offensive plays that are passes on neutral downs (0.35-0.70).</summary>
        public float PassRate;
        /// <summary>Willingness to go for it on fourth down (0-1).</summary>
        public float FourthDownAggression;
        /// <summary>Share of passing downs the defence sends extra rushers (0.10-0.45).</summary>
        public float BlitzRate;
        /// <summary>Share of passes thrown deep (0.06-0.25).</summary>
        public float DeepShotRate;
        /// <summary>Share of defensive snaps in man coverage (0.20-0.65).</summary>
        public float ManCoverageRate;
        /// <summary>Share of offensive snaps without a huddle (0.05-0.40).</summary>
        public float NoHuddleRate;
        public OffenseScheme OffScheme;
        public DefenseScheme DefScheme;
    }

    public sealed class MoraleFactor
    {
        public string Label;
        public int Value;
        public MoraleFactor(string label, int value) { Label = label; Value = value; }
    }

    public sealed class PlayerMoraleInfo
    {
        public int Morale;
        public int Target;
        public List<MoraleFactor> Factors = new List<MoraleFactor>();
        public string Mood;
    }

    /// <summary>Player and team morale: playing time, winning and the contract move it; it feeds re-signing, free agency and (as data) the Sim.</summary>
    public static class MoraleEngine
    {
        /// <summary>The team's regular-season win percentage this year (or last year's in the offseason), 0.5 when unknown.</summary>
        public static float WinPct(CareerSave s, int teamId)
        {
            Season season = s.Season;
            if (season != null && !season.IsEmpty && (s.Phase == CareerPhase.RegularSeason || s.Phase == CareerPhase.Playoffs || s.Phase == CareerPhase.PixBowl))
            {
                int w = season.WinsOf(teamId), l = season.LossesOf(teamId);
                return w + l == 0 ? 0.5f : (float)w / (w + l);
            }
            for (int i = s.TeamSeasons.Count - 1; i >= 0; i--)
            {
                TeamSeasonRecord r = s.TeamSeasons[i];
                if (r.TeamId == teamId) { int g = r.Wins + r.Losses + r.Ties; return g == 0 ? 0.5f : (r.Wins + 0.5f * r.Ties) / g; }
            }
            return 0.5f;
        }

        public static TeamMorale StateOf(CareerSave s, int teamId)
        {
            for (int i = 0; i < s.Morale.Count; i++) if (s.Morale[i].TeamId == teamId) return s.Morale[i];
            var m = new TeamMorale { TeamId = teamId };
            s.Morale.Add(m);
            return m;
        }

        public static int TeamMorale(CareerSave s, int teamId) { return StateOf(s, teamId).Value; }

        /// <summary>Why a player feels the way he does (the player card shows the factors).</summary>
        public static PlayerMoraleInfo Breakdown(CareerSave s, Team t, Player p)
        {
            var info = new PlayerMoraleInfo { Morale = p.Morale };
            int total = 62;
            float win = WinPct(s, t.Id);
            int success = (int)Math.Round((win - 0.5f) * 30f);
            info.Factors.Add(new MoraleFactor("Team success", success)); total += success;

            // playing time
            int role = 0; string roleLabel = "Role";
            bool starter = Offseason.IsStarter(t, p);
            if (t.PracticeSquad.Contains(p)) { role = -2; roleLabel = "Practice squad"; }
            else if (t.InjuredReserve.Contains(p)) { role = -2; roleLabel = "Injured reserve"; }
            else if (starter) { role = 6; roleLabel = "Starting role"; }
            else
            {
                float best = Offseason.BestOvr(t, p.Position);
                if (p.Overall >= 68 && p.Overall >= best - 2f) { role = -8; roleLabel = "Wants more playing time"; }
                else { role = 0; roleLabel = "Backup role"; }
            }
            info.Factors.Add(new MoraleFactor(roleLabel, role)); total += role;

            // contract
            if (p.Contract.YearsLeft > 0 || t.Roster.Contains(p))
            {
                long market = CapMath.Market(s, p, s.Year);
                long pay = p.Contract.YearsLeft > 0 ? p.Contract.Aav : 0L;
                int contract = 0; string cl = "Contract";
                if (pay > 0 && market > CapMath.MinSalary(s, s.Year) * 2)
                {
                    double ratio = (double)pay / market;
                    if (ratio < 0.6) { contract = -8; cl = "Underpaid"; }
                    else if (ratio < 0.85) { contract = -3; cl = "Slightly underpaid"; }
                    else if (ratio > 1.15) { contract = 3; cl = "Well paid"; }
                }
                if (p.Contract.YearsLeft <= 1 && p.Overall >= 70 && p.Bio.Age >= 25) { contract -= 2; cl += ", contract year"; }
                info.Factors.Add(new MoraleFactor(cl, contract)); total += contract;
            }

            Coach hc = t.HeadCoach;
            if (hc != null) { int c = (int)Math.Round((hc.Prestige - 60) * 0.1f); info.Factors.Add(new MoraleFactor("Head coach", c)); total += c; }
            int fit = (int)Math.Round(StaffManager.SchemeFit(t, p) * 3f);
            if (fit != 0) { info.Factors.Add(new MoraleFactor(fit > 0 ? "Scheme fit" : "Poor scheme fit", fit)); total += fit; }
            if (p.Injury.IsInjured) { info.Factors.Add(new MoraleFactor("Injured", -3)); total -= 3; }
            int lead = LeadersOnTeam(t);
            if (lead > 0 && !(p.Abilities.Count > 0 && AbilityCatalog.Has(p, "locker_room_leader"))) { info.Factors.Add(new MoraleFactor("Leadership", Math.Min(3, lead))); total += Math.Min(3, lead); }

            info.Target = Math.Max(15, Math.Min(100, total));
            info.Mood = p.Morale >= 80 ? "Thrilled" : (p.Morale >= 65 ? "Happy" : (p.Morale >= 50 ? "Content" : (p.Morale >= 35 ? "Frustrated" : "Wants out")));
            return info;
        }

        private static int LeadersOnTeam(Team t)
        {
            int n = 0;
            for (int i = 0; i < t.Roster.Count; i++)
            {
                PlayerAbility a = AbilityCatalog.Find(t.Roster[i], "locker_room_leader");
                if (a != null) n += a.Tier;
            }
            return n;
        }

        /// <summary>Weekly update for every team: players move a quarter of the way to their target; the team value follows the starters and the results.</summary>
        public static void UpdateWeek(CareerSave s, Rng rng)
        {
            for (int ti = 0; ti < s.Pro.Teams.Count; ti++)
            {
                Team t = s.Pro.Teams[ti];
                double sumStarters = 0; int nStarters = 0;
                for (int i = 0; i < t.Roster.Count; i++)
                {
                    Player p = t.Roster[i];
                    PlayerMoraleInfo info = Breakdown(s, t, p);
                    int delta = (int)Math.Round((info.Target - p.Morale) * 0.25f) + rng.Range(-1, 2);
                    p.Morale = Math.Max(5, Math.Min(100, p.Morale + delta));
                    if (Offseason.IsStarter(t, p)) { sumStarters += p.Morale; nStarters++; }
                }
                TeamMorale m = StateOf(s, t.Id);
                float starters = nStarters > 0 ? (float)(sumStarters / nStarters) : 65f;
                int value = (int)Math.Round(starters * 0.7f + (30f + WinPct(s, t.Id) * 70f) * 0.3f);
                m.Trend = value - m.Value;
                m.Value = Math.Max(0, Math.Min(100, value));
            }
        }

        /// <summary>Offseason reset: everybody drifts toward 65 and a title or a playoff run lifts the whole team.</summary>
        public static void NewYear(CareerSave s, Rng rng)
        {
            for (int ti = 0; ti < s.Pro.Teams.Count; ti++)
            {
                Team t = s.Pro.Teams[ti];
                int bonus = 0;
                for (int i = s.TeamSeasons.Count - 1; i >= 0; i--)
                    if (s.TeamSeasons[i].TeamId == t.Id && s.TeamSeasons[i].Year == s.Year)
                    {
                        PlayoffResult r = s.TeamSeasons[i].Result;
                        bonus = r == PlayoffResult.Champion ? 8 : (r >= PlayoffResult.Divisional ? 3 : (r == PlayoffResult.Missed ? -3 : 0));
                        break;
                    }
                foreach (Player p in CapManager.AllUnderContract(t))
                    p.Morale = Math.Max(20, Math.Min(100, (int)Math.Round(p.Morale * 0.6f + 65f * 0.4f) + bonus + rng.Range(-3, 4)));
            }
        }

        /// <summary>Rating points a player's mood adds or takes away (-2..+2): data the Sim can apply to every rating at kickoff.</summary>
        public static float RatingModifier(Player p)
        {
            float v = (p.Morale - 70) / 15f;
            return v < -2f ? -2f : (v > 2f ? 2f : v);
        }
    }

    /// <summary>Coaching staff: scheme fit, development, hiring and firing, salaries, play-calling tendencies.</summary>
    public static class StaffManager
    {
        // ---------------------------------------------------------------- scheme fit
        private static readonly Archetype[][] OffFit =
        {
            new Archetype[0],                                                                                        // Balanced
            new[] { Archetype.QbGunslinger, Archetype.WrDeepThreat, Archetype.WrSlot, Archetype.OlPassProtector },   // AirRaid
            new[] { Archetype.QbPocket, Archetype.WrPossession, Archetype.RbAllPurpose, Archetype.TeReceiver },      // WestCoast
            new[] { Archetype.RbPower, Archetype.OlRunMauler, Archetype.FbBlocker, Archetype.TeBlocker },            // PowerRun
            new[] { Archetype.QbScrambler, Archetype.WrSlot, Archetype.RbSpeed, Archetype.WrDeepThreat }             // SpreadOption
        };

        private static readonly Archetype[][] DefFit =
        {
            new[] { Archetype.DlBalanced, Archetype.LbRunStopper, Archetype.SBox },                                      // Base43
            new[] { Archetype.DlRunStuffer, Archetype.LbBlitzer, Archetype.LbRunStopper },                               // Base34
            new[] { Archetype.CbSlot, Archetype.LbCoverage, Archetype.SCenterfield },                                    // Nickel
            new[] { Archetype.DlPassRusher, Archetype.LbBlitzer, Archetype.CbManLockdown, Archetype.SBox },              // Aggressive
            new[] { Archetype.CbZone, Archetype.SCenterfield, Archetype.LbCoverage }                                     // Zone
        };

        private static Coach CoordinatorFor(Team t, Position pos)
        {
            if (PositionGroup.IsOffense(pos)) return t.OffensiveCoordinator ?? t.HeadCoach;
            if (PositionGroup.IsDefense(pos)) return t.DefensiveCoordinator ?? t.HeadCoach;
            return t.HeadCoach;
        }

        /// <summary>-1 (against the scheme), 0 (neutral) to +1 (made for it) for a player under his coordinator's scheme.</summary>
        public static float SchemeFit(Team t, Player p)
        {
            Coach c = CoordinatorFor(t, p.Position);
            if (c == null || PositionGroup.IsSpecial(p.Position) || p.Archetype == Archetype.None) return 0f;
            bool off = PositionGroup.IsOffense(p.Position);
            Archetype[] fav = off ? OffFit[(int)c.OffScheme] : DefFit[(int)c.DefScheme];
            for (int i = 0; i < fav.Length; i++) if (fav[i] == p.Archetype) return 1f;
            if (!off) return 0f;
            // a pass-first scheme dislikes pure blockers and a run-first scheme dislikes finesse passers
            if (c.OffScheme == OffenseScheme.AirRaid && (p.Archetype == Archetype.RbPower || p.Archetype == Archetype.OlRunMauler)) return -0.5f;
            if (c.OffScheme == OffenseScheme.PowerRun && (p.Archetype == Archetype.QbGunslinger || p.Archetype == Archetype.WrSlot)) return -0.5f;
            return 0f;
        }

        /// <summary>Average scheme fit of the starters, scaled 0-100 (50 = neutral) for the staff screen.</summary>
        public static int TeamSchemeFit(Team t, bool offense)
        {
            double sum = 0; int n = 0;
            for (int i = 0; i < LeagueRules.RosterOrder.Length; i++)
            {
                Position pos = LeagueRules.RosterOrder[i];
                if (PositionGroup.IsOffense(pos) != offense || PositionGroup.IsSpecial(pos)) continue;
                List<int> ids;
                if (!t.Depth.TryGet(pos, out ids)) continue;
                for (int k = 0; k < LeagueRules.Starters(pos) && k < ids.Count; k++)
                {
                    Player p = t.GetPlayer(ids[k]);
                    if (p == null) continue;
                    sum += SchemeFit(t, p); n++;
                }
            }
            return n == 0 ? 50 : (int)Math.Round(50 + 50 * (sum / n));
        }

        // ---------------------------------------------------------------- development
        /// <summary>The coaching figure passed to <see cref="Progression.Advance"/> for a player: head coach, his coordinator, the program, scheme fit and mood.</summary>
        public static int DevelopmentRating(Team t, Player p)
        {
            Coach hc = t.HeadCoach;
            Coach coord = CoordinatorFor(t, p.Position);
            float dev = hc != null ? hc.Development : 65f;
            if (coord != null && coord != hc) dev = dev * 0.55f + coord.Development * 0.45f;
            dev += (t.Prestige - 3) * 5f + SchemeFit(t, p) * 4f + MoraleEngine.RatingModifier(p);
            return (int)Math.Round(dev);
        }

        // ---------------------------------------------------------------- tendencies
        public static PlaycallTendencies Tendencies(Team t)
        {
            Coach oc = t.OffensiveCoordinator ?? t.HeadCoach, dc = t.DefensiveCoordinator ?? t.HeadCoach, hc = t.HeadCoach ?? oc;
            var r = new PlaycallTendencies { OffScheme = oc != null ? oc.OffScheme : OffenseScheme.Balanced, DefScheme = dc != null ? dc.DefScheme : DefenseScheme.Base43 };
            r.PassRate = Clamp(0.38f + (oc != null ? oc.PassTendency : 50) * 0.0034f, 0.35f, 0.70f);
            r.FourthDownAggression = hc != null ? hc.Aggression / 100f : 0.4f;
            switch (r.DefScheme)
            {
                case DefenseScheme.Aggressive: r.BlitzRate = 0.36f; r.ManCoverageRate = 0.55f; break;
                case DefenseScheme.Base34: r.BlitzRate = 0.30f; r.ManCoverageRate = 0.40f; break;
                case DefenseScheme.Nickel: r.BlitzRate = 0.20f; r.ManCoverageRate = 0.42f; break;
                case DefenseScheme.Zone: r.BlitzRate = 0.14f; r.ManCoverageRate = 0.22f; break;
                default: r.BlitzRate = 0.22f; r.ManCoverageRate = 0.38f; break;
            }
            r.BlitzRate = Clamp(r.BlitzRate + ((dc != null ? dc.Aggression : 50) - 50) * 0.002f, 0.10f, 0.45f);
            switch (r.OffScheme)
            {
                case OffenseScheme.AirRaid: r.DeepShotRate = 0.22f; r.NoHuddleRate = 0.30f; break;
                case OffenseScheme.WestCoast: r.DeepShotRate = 0.10f; r.NoHuddleRate = 0.12f; break;
                case OffenseScheme.PowerRun: r.DeepShotRate = 0.09f; r.NoHuddleRate = 0.05f; break;
                case OffenseScheme.SpreadOption: r.DeepShotRate = 0.15f; r.NoHuddleRate = 0.35f; break;
                default: r.DeepShotRate = 0.13f; r.NoHuddleRate = 0.12f; break;
            }
            return r;
        }

        private static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }

        // ---------------------------------------------------------------- salaries and contracts
        public static long SalaryFor(CareerSave s, CoachRole role, Coach c, int year)
        {
            double avg = (c.OffenseRating + c.DefenseRating + c.Development + c.Scouting) / 4.0;
            double hcBase = 2000000 + Math.Max(0, avg - 50) * 280000;
            double m = role == CoachRole.HeadCoach ? 1.0 : 0.38;
            return CapMath.Round(hcBase * m * CapMath.Scale(s, year));
        }

        public static long Budget(CareerSave s, Team t)
        {
            return CapMath.Round((24000000 + (t.Prestige - 3) * 2500000) * CapMath.Scale(s, s.Year));
        }

        public static StaffContract ContractOf(CareerSave s, int teamId, int coachId)
        {
            for (int i = 0; i < s.StaffContracts.Count; i++) if (s.StaffContracts[i].TeamId == teamId && s.StaffContracts[i].CoachId == coachId) return s.StaffContracts[i];
            return null;
        }

        public static long Payroll(CareerSave s, Team t)
        {
            long sum = 0;
            for (int i = 0; i < t.Staff.Count; i++) { StaffContract c = ContractOf(s, t.Id, t.Staff[i].Id); if (c != null) sum += c.Salary; }
            return sum;
        }

        /// <summary>Gives every existing coach a salary and contract (new career).</summary>
        public static void InitContracts(CareerSave s, Rng rng)
        {
            s.StaffContracts.Clear();
            for (int ti = 0; ti < s.Pro.Teams.Count; ti++)
            {
                Team t = s.Pro.Teams[ti];
                for (int i = 0; i < t.Staff.Count; i++)
                    s.StaffContracts.Add(new StaffContract { TeamId = t.Id, CoachId = t.Staff[i].Id, Salary = SalaryFor(s, t.Staff[i].Role, t.Staff[i], s.Year), YearsLeft = rng.Range(1, 5) });
            }
        }

        /// <summary>A fresh coaching market: eight candidates per role (quality follows a league-wide spread).</summary>
        public static void GeneratePool(CareerSave s, Rng rng)
        {
            // coaches who are still unemployed from last year stay, up to a few
            for (int i = s.CoachPool.Count - 1; i >= 0; i--) if (s.CoachPool[i].Coach.Age >= 66 || i < s.CoachPool.Count - 12) s.CoachPool.RemoveAt(i);
            var used = CareerFactory.UsedNames(s);
            for (int i = 0; i < s.CoachPool.Count; i++) used.Add(s.CoachPool[i].Coach.Name);
            CoachRole[] roles = { CoachRole.HeadCoach, CoachRole.OffensiveCoordinator, CoachRole.DefensiveCoordinator };
            for (int r = 0; r < roles.Length; r++)
            {
                int have = 0;
                for (int i = 0; i < s.CoachPool.Count; i++) if (s.CoachPool[i].Coach.Role == roles[r]) have++;
                for (int k = have; k < 8; k++)
                {
                    Coach c = LeagueFactory.CreateCoach(rng, used, roles[r], 1000000 + s.NextId(), rng.Gaussian(0f, 3f));
                    used.Add(c.Name);
                    s.CoachPool.Add(new CoachCandidate { Coach = c, AskSalary = SalaryFor(s, roles[r], c, s.Year + 1), Years = rng.Range(2, 5) });
                }
            }
        }

        public static CoachCandidate FindCandidate(CareerSave s, int coachId)
        {
            for (int i = 0; i < s.CoachPool.Count; i++) if (s.CoachPool[i].Coach.Id == coachId) return s.CoachPool[i];
            return null;
        }

        public static bool HasVacancy(Team t)
        {
            return t.HeadCoach == null || t.OffensiveCoordinator == null || t.DefensiveCoordinator == null;
        }

        public static List<CoachRole> Vacancies(Team t)
        {
            var l = new List<CoachRole>();
            if (t.HeadCoach == null) l.Add(CoachRole.HeadCoach);
            if (t.OffensiveCoordinator == null) l.Add(CoachRole.OffensiveCoordinator);
            if (t.DefensiveCoordinator == null) l.Add(CoachRole.DefensiveCoordinator);
            return l;
        }

        /// <summary>Fires the coach in a role: he goes to the market and the remaining contract is paid out (no cap effect). The role is vacant until a hire.</summary>
        public static OpResult Fire(CareerSave s, Team t, CoachRole role)
        {
            Coach c = t.FindCoach(role);
            if (c == null) return OpResult.Fail("That role is already vacant.");
            StaffContract sc = ContractOf(s, t.Id, c.Id);
            long buyout = sc != null ? sc.Salary * Math.Max(0, sc.YearsLeft) : 0L;
            t.Staff.Remove(c);
            if (sc != null) s.StaffContracts.Remove(sc);
            if (c.Age < 66) s.CoachPool.Add(new CoachCandidate { Coach = c, AskSalary = SalaryFor(s, role, c, s.Year + 1), Years = 3 });
            Ledger.Tx(s, TransactionKind.CoachFired, t.Id, null, buyout, c.Name + " (" + RoleName(role) + ") was fired by the " + t.Nickname + ".");
            NewsWriter.Post(s, NewsKind.Coach, t.Nickname + " fire " + RoleName(role) + " " + c.Name, c.Name + " is out after the " + t.Nickname + " decided to change direction.", role == CoachRole.HeadCoach ? 4 : 2, t.Id);
            return OpResult.Success(c.Name + " was fired (buyout " + CareerText.Money(buyout) + ").");
        }

        /// <summary>Hires a candidate into his role. An existing coach in that role is fired first.</summary>
        public static OpResult Hire(CareerSave s, Team t, int coachId, bool replaceExisting = true)
        {
            CoachCandidate cand = FindCandidate(s, coachId);
            if (cand == null) return OpResult.Fail("That coach is not on the market.");
            Coach existing = t.FindCoach(cand.Coach.Role);
            if (existing != null)
            {
                if (!replaceExisting) return OpResult.Fail("The " + RoleName(cand.Coach.Role) + " job is taken; fire him first.");
                long budgetAfter = Payroll(s, t) - (ContractOf(s, t.Id, existing.Id) != null ? ContractOf(s, t.Id, existing.Id).Salary : 0L) + cand.AskSalary;
                if (budgetAfter > Budget(s, t)) return OpResult.Fail("The staff budget (" + CareerText.Money(Budget(s, t)) + ") would be exceeded.");
                Fire(s, t, cand.Coach.Role);
            }
            else if (Payroll(s, t) + cand.AskSalary > Budget(s, t)) return OpResult.Fail("The staff budget (" + CareerText.Money(Budget(s, t)) + ") would be exceeded.");
            s.CoachPool.Remove(cand);
            t.Staff.Add(cand.Coach);
            s.StaffContracts.Add(new StaffContract { TeamId = t.Id, CoachId = cand.Coach.Id, Salary = cand.AskSalary, YearsLeft = cand.Years });
            Ledger.Tx(s, TransactionKind.CoachHired, t.Id, null, cand.AskSalary, cand.Coach.Name + " hired as " + RoleName(cand.Coach.Role) + " by the " + t.Nickname + ".");
            NewsWriter.Post(s, NewsKind.Coach, t.Nickname + " hire " + cand.Coach.Name + " as " + RoleName(cand.Coach.Role), cand.Coach.Name + " (" + cand.Coach.OffScheme + " / " + cand.Coach.DefScheme + ") signs " + cand.Years + " years at " + CareerText.Money(cand.AskSalary) + ".", cand.Coach.Role == CoachRole.HeadCoach ? 3 : 2, t.Id);
            return OpResult.Success(cand.Coach.Name + " hired.");
        }

        public static string RoleName(CoachRole r)
        {
            switch (r) { case CoachRole.HeadCoach: return "head coach"; case CoachRole.OffensiveCoordinator: return "offensive coordinator"; default: return "defensive coordinator"; }
        }

        /// <summary>How well a candidate suits a team's job: role-relevant ratings, development, and (for coordinators) scheme match with the roster.</summary>
        public static float CandidateValue(Coach c)
        {
            switch (c.Role)
            {
                case CoachRole.OffensiveCoordinator: return c.OffenseRating * 0.6f + c.Development * 0.4f;
                case CoachRole.DefensiveCoordinator: return c.DefenseRating * 0.6f + c.Development * 0.4f;
                default: return c.Prestige * 0.4f + c.Development * 0.35f + (c.OffenseRating + c.DefenseRating) * 0.125f;
            }
        }

        /// <summary>Hires the best affordable candidate for every vacant role of a team.</summary>
        public static void HireBest(CareerSave s, Team t)
        {
            var vacancies = Vacancies(t);
            for (int v = 0; v < vacancies.Count; v++)
            {
                CoachCandidate best = null; float bestV = -1f;
                for (int i = 0; i < s.CoachPool.Count; i++)
                {
                    CoachCandidate c = s.CoachPool[i];
                    if (c.Coach.Role != vacancies[v]) continue;
                    if (Payroll(s, t) + c.AskSalary > Budget(s, t)) continue;
                    float val = CandidateValue(c.Coach);
                    if (val > bestV) { bestV = val; best = c; }
                }
                if (best == null)
                {
                    // nobody affordable: promote a cheap internal hire (a fresh young coach on the minimum)
                    var used = CareerFactory.UsedNames(s);
                    Coach c = LeagueFactory.CreateCoach(s.NewRng(), used, vacancies[v], 1000000 + s.NextId(), -4f);
                    best = new CoachCandidate { Coach = c, AskSalary = CapMath.Round(SalaryFor(s, vacancies[v], c, s.Year + 1) * 0.6), Years = 2 };
                    s.CoachPool.Add(best);
                }
                Hire(s, t, best.Coach.Id, false);
            }
        }

        /// <summary>
        /// New-year staff business for every team (aging, rating drift, contract expiry, retirements, CPU firings and hires). The user's staff
        /// ages but is never fired or replaced automatically: vacancies block the franchise until the user hires.
        /// </summary>
        public static void NewYear(CareerSave s, Rng rng)
        {
            for (int ti = 0; ti < s.Pro.Teams.Count; ti++)
            {
                Team t = s.Pro.Teams[ti];
                bool user = t.Id == s.UserTeamId;
                for (int i = t.Staff.Count - 1; i >= 0; i--)
                {
                    Coach c = t.Staff[i];
                    c.Age++;
                    c.OffenseRating = Ratings.Clamp(c.OffenseRating + rng.Range(-2, 3));
                    c.DefenseRating = Ratings.Clamp(c.DefenseRating + rng.Range(-2, 3));
                    c.Development = Ratings.Clamp(c.Development + rng.Range(-2, 3));
                    c.Scouting = Ratings.Clamp(c.Scouting + rng.Range(-2, 3));
                    c.Prestige = (c.OffenseRating + c.DefenseRating) / 2;
                    StaffContract sc = ContractOf(s, t.Id, c.Id);
                    if (sc != null) sc.YearsLeft--;
                    bool retire = c.Age >= 68 && rng.Chance(0.5f);
                    bool expired = sc == null || sc.YearsLeft <= 0;
                    if (retire)
                    {
                        t.Staff.RemoveAt(i);
                        if (sc != null) s.StaffContracts.Remove(sc);
                        continue;
                    }
                    if (expired)
                    {
                        if (user) { if (sc != null) { sc.YearsLeft = 1; sc.Salary = SalaryFor(s, c.Role, c, s.Year + 1); } continue; }   // the user's staff is renewed for a year, he can change it any time
                        bool keep = rng.Chance(0.75f);
                        if (keep) { if (sc != null) { sc.YearsLeft = rng.Range(2, 5); sc.Salary = SalaryFor(s, c.Role, c, s.Year + 1); } else s.StaffContracts.Add(new StaffContract { TeamId = t.Id, CoachId = c.Id, Salary = SalaryFor(s, c.Role, c, s.Year + 1), YearsLeft = 3 }); }
                        else
                        {
                            t.Staff.RemoveAt(i);
                            if (sc != null) s.StaffContracts.Remove(sc);
                            s.CoachPool.Add(new CoachCandidate { Coach = c, AskSalary = SalaryFor(s, c.Role, c, s.Year + 1), Years = 3 });
                        }
                    }
                }
            }
            GeneratePool(s, rng);
            for (int ti = 0; ti < s.Pro.Teams.Count; ti++)
            {
                Team t = s.Pro.Teams[ti];
                if (t.Id == s.UserTeamId) continue;
                // fire a head coach after a bad year
                Coach hc = t.HeadCoach;
                if (hc != null)
                {
                    float win = MoraleEngine.WinPct(s, t.Id);
                    float p = win <= 0.25f ? 0.5f : (win <= 0.35f ? 0.25f : 0.02f);
                    if (rng.Chance(p)) Fire(s, t, CoachRole.HeadCoach);
                }
                HireBest(s, t);
            }
        }
    }
}
