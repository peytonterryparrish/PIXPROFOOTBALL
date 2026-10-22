using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>A row of the user's draft board: the true prospect is hidden behind the team's scouting estimate.</summary>
    public sealed class ProspectRow
    {
        public Prospect Prospect;
        public int Id;
        public string Name;
        public Position Pos;
        public int Age;
        public string School;
        public int ConsensusRank;
        public bool Starred;
        public bool Drafted;
        /// <summary>The user's own estimate (sharpens with scouting points).</summary>
        public float EstOverall, EstPotential, EstGrade;
        /// <summary>Plus or minus rating points of uncertainty.</summary>
        public float Uncertainty;
        public int PointsSpent;
        public string ProjectedRound = "";
        public List<PlayerAbility> AbilityHints = new List<PlayerAbility>();
    }

    /// <summary>
    /// Combine / scouting, the draft itself (with pick ownership, trade-up and trade-down offers, live grades) and the rookie signing step.
    /// CPU teams draft by their own scouting board and positional need; picks the user owns wait for him on the clock.
    /// </summary>
    public static class CareerDraft
    {
        // ---------------------------------------------------------------- scouting
        public static void PrepareClass(CareerSave s, Rng rng)
        {
            Offseason.PrepareDraftClass(s.Pro, s.College, rng);
            var cls = s.Pro.DraftClass;
            // no prospect shares a name with anybody already in the league (practice squads and reserves included)
            var taken = CareerFactory.UsedNames(s, false);
            var nrng = rng.Fork();
            for (int i = 0; i < cls.Count; i++)
            {
                PlayerBio bio = cls[i].Player.Bio;
                if (taken.Add(bio.FullName)) continue;
                string first, last;
                NameGenerator.PersonName(nrng, taken, out first, out last);
                bio.FirstName = first; bio.LastName = last;
            }
            // a league without a college world still gets schools: the colleges its pros came from
            var schools = new List<string>();
            for (int t = 0; t < s.Pro.Teams.Count; t++)
                for (int i = 0; i < s.Pro.Teams[t].Roster.Count; i++)
                {
                    string c = s.Pro.Teams[t].Roster[i].Bio.College;
                    if (!string.IsNullOrEmpty(c) && !schools.Contains(c)) schools.Add(c);
                }
            var arng = rng.Fork();
            if (schools.Count > 0)
                for (int i = 0; i < cls.Count; i++)
                    if (string.IsNullOrEmpty(cls[i].Player.Bio.College)) { string c = schools[arng.Range(0, schools.Count)]; cls[i].Player.Bio.College = c; cls[i].SchoolName = c; }
            for (int i = 0; i < cls.Count; i++) AbilityCatalog.AssignInitial(cls[i].Player, arng);
            // the user starts with a blurry board and his whole scouting budget (the CPU reports were built with their budgets already spent)
            for (int i = 0; i < s.Pro.ScoutingReports.Count; i++)
            {
                ScoutingReport old = s.Pro.ScoutingReports[i];
                if (old.TeamId != s.UserTeamId) continue;
                s.Pro.ScoutingReports[i] = new ScoutingReport(old.TeamId, old.Year, old.Seed, old.PointsBudget);
            }
            s.Draft = new DraftState { Year = s.Year };
        }

        public static ScoutingReport UserReport(CareerSave s)
        {
            var reps = s.Pro.ScoutingReports;
            for (int i = 0; i < reps.Count; i++) if (reps[i].TeamId == s.UserTeamId) return reps[i];
            return null;
        }

        public static int PointsRemaining(CareerSave s) { ScoutingReport r = UserReport(s); return r == null ? 0 : r.PointsRemaining; }

        public static Prospect FindProspect(CareerSave s, int id)
        {
            var cls = s.Pro.DraftClass;
            for (int i = 0; i < cls.Count; i++) if (cls[i].Id == id) return cls[i];
            return null;
        }

        /// <summary>Spends scouting points on a prospect; his estimated ratings and ceiling get sharper.</summary>
        public static OpResult Scout(CareerSave s, int prospectId, int points)
        {
            if (s.Phase != CareerPhase.Scouting && s.Phase != CareerPhase.Draft) return OpResult.Fail("Scouting is only possible before the draft.");
            Prospect p = FindProspect(s, prospectId);
            if (p == null) return OpResult.Fail("Unknown prospect.");
            ScoutingReport r = UserReport(s);
            if (r == null) return OpResult.Fail("No scouting report.");
            if (points <= 0) return OpResult.Fail("Spend at least one point.");
            if (r.PointsRemaining <= 0) return OpResult.Fail("No scouting points left.");
            int before = r.PointsSpent;
            r.Reveal(p, points);
            return OpResult.Success("Spent " + (r.PointsSpent - before) + " points; " + r.PointsRemaining + " left.");
        }

        public static void SetStarred(CareerSave s, int prospectId, bool starred)
        {
            var list = s.Draft.BigBoard;
            list.Remove(prospectId);
            if (starred) list.Add(prospectId);
        }

        public static void MoveOnBoard(CareerSave s, int prospectId, int newIndex)
        {
            var list = s.Draft.BigBoard;
            if (!list.Remove(prospectId)) return;
            list.Insert(Math.Max(0, Math.Min(newIndex, list.Count)), prospectId);
        }

        private static string RoundLabel(float grade)
        {
            if (grade >= 84) return "Round 1 (top 10)";
            if (grade >= 78) return "Round 1";
            if (grade >= 73) return "Round 2";
            if (grade >= 69) return "Round 3";
            if (grade >= 66) return "Round 4";
            if (grade >= 63) return "Rounds 5-6";
            if (grade >= 58) return "Round 7";
            return "Undrafted";
        }

        /// <summary>The user's board: every prospect with his scouting estimate. Sort by "grade" (the user's estimate), "consensus" or "starred".</summary>
        public static List<ProspectRow> Board(CareerSave s, string sort = "grade", bool onlyUndrafted = false)
        {
            var rows = new List<ProspectRow>();
            ScoutingReport rep = UserReport(s);
            var cls = s.Pro.DraftClass;
            for (int i = 0; i < cls.Count; i++)
            {
                Prospect p = cls[i];
                if (onlyUndrafted && p.Drafted) continue;
                var row = new ProspectRow
                {
                    Prospect = p, Id = p.Id, Name = p.Player.Name, Pos = p.Player.Position, Age = p.Player.Age, School = p.SchoolName, ConsensusRank = p.ConsensusRank,
                    Starred = s.Draft.BigBoard.Contains(p.Id), Drafted = p.Drafted
                };
                if (rep != null)
                {
                    ScoutedProspect sc = rep.Get(p);
                    row.EstOverall = sc.Estimated.Overall(p.Player.Position);
                    row.EstPotential = sc.EstimatedPotential;
                    row.EstGrade = ScoutingReport.Grade(row.EstOverall, row.EstPotential, p.Player.Age);
                    row.Uncertainty = sc.Sd;
                    row.PointsSpent = sc.PointsSpent;
                    row.AbilityHints = AbilityCatalog.ScoutedHints(p.Player, sc.PointsSpent);
                }
                row.ProjectedRound = RoundLabel(row.EstGrade);
                rows.Add(row);
            }
            if (sort == "consensus") rows.Sort((a, b) => a.ConsensusRank.CompareTo(b.ConsensusRank));
            else if (sort == "starred") rows.Sort((a, b) => { int c = b.Starred.CompareTo(a.Starred); if (c != 0) return c; int ia = s.Draft.BigBoard.IndexOf(a.Id), ib = s.Draft.BigBoard.IndexOf(b.Id); c = ia.CompareTo(ib); return c != 0 ? c : b.EstGrade.CompareTo(a.EstGrade); });
            else rows.Sort((a, b) => b.EstGrade.CompareTo(a.EstGrade));
            return rows;
        }

        // ---------------------------------------------------------------- building the order
        /// <summary>Builds the pick list from pick ownership (call when the draft opens, and again if the order must be refreshed).</summary>
        public static void Build(CareerSave s)
        {
            TradeEngine.EnsurePicks(s);
            List<int> order = s.Season != null && s.Season.Games.Count > 0 ? s.Season.DraftOrder() : DraftEngine.DraftOrder(s.Pro);
            var d = new DraftState { Year = s.Year, Built = true };
            d.BigBoard = s.Draft != null ? s.Draft.BigBoard : new List<int>();
            for (int r = 1; r <= LeagueRules.DraftRounds; r++)
                for (int i = 0; i < order.Count; i++)
                {
                    int orig = order[i];
                    DraftPickAsset pick = null;
                    for (int k = 0; k < s.Picks.Count; k++)
                        if (s.Picks[k].Year == s.Year && s.Picks[k].Round == r && s.Picks[k].OriginalTeamId == orig) { pick = s.Picks[k]; break; }
                    if (pick == null) continue;
                    pick.Overall = (r - 1) * order.Count + i + 1;
                    d.Slots.Add(new DraftSlot { PickId = pick.Id, Overall = pick.Overall, Round = r, PickInRound = i + 1, OriginalTeamId = orig, TeamId = pick.OwnerTeamId });
                }
            d.Slots.Sort((a, b) => a.Overall.CompareTo(b.Overall));
            s.Draft = d;
            s.DraftBoards = null;
        }

        /// <summary>Picks may change hands while the draft runs (trades): the slots follow the pick book.</summary>
        public static void SyncSlots(CareerSave s)
        {
            for (int i = 0; i < s.Draft.Slots.Count; i++)
            {
                DraftSlot sl = s.Draft.Slots[i];
                if (sl.Made) continue;
                DraftPickAsset p = TradeEngine.FindPick(s, sl.PickId);
                if (p != null) sl.TeamId = p.OwnerTeamId;
            }
        }

        public static DraftSlot Current(CareerSave s)
        {
            if (!s.Draft.Built || s.Draft.Finished || s.Draft.Current >= s.Draft.Slots.Count) return null;
            return s.Draft.Slots[s.Draft.Current];
        }

        public static bool UserOnClock(CareerSave s)
        {
            DraftSlot sl = Current(s);
            return sl != null && sl.TeamId == s.UserTeamId;
        }

        // ---------------------------------------------------------------- picks
        private static List<Prospect> BoardOf(CareerSave s, int teamId)
        {
            int key = s.Pro.DraftClass.Count * 7919 + s.Year * 31;
            if (s.DraftBoards == null || s.DraftBoardsKey != key || s.DraftBoards.Count != s.Pro.Teams.Count)
            {
                s.DraftBoards = new List<List<Prospect>>();
                for (int t = 0; t < s.Pro.Teams.Count; t++) s.DraftBoards.Add(s.Pro.ScoutingReports[t].BigBoard(s.Pro.DraftClass));
                s.DraftBoardsKey = key;
            }
            return s.DraftBoards[teamId];
        }

        private static HashSet<int> TakenIds(CareerSave s)
        {
            var taken = new HashSet<int>();
            for (int i = 0; i < s.Draft.Slots.Count; i++) if (s.Draft.Slots[i].Made) taken.Add(s.Draft.Slots[i].PlayerId);
            return taken;
        }

        private static DraftSlot MakePick(CareerSave s, DraftSlot slot, Prospect choice, Rng rng)
        {
            Team team = s.Pro.GetTeam(slot.TeamId);
            Player p = choice.Player;
            int year = s.Year;
            choice.Drafted = true; choice.DraftedByTeamId = team.Id; choice.OverallPick = slot.Overall;
            p.DraftYear = year; p.DraftRound = slot.Round; p.DraftPick = slot.Overall; p.Experience = 0;
            if (choice.SchoolId >= 0) CollegeFactory.ApplyProTransition(p);
            p.Morale = 80; p.Class = ClassYear.Senior; p.IsRedshirting = false; p.Retired = false;
            bool user = team.Id == s.UserTeamId;
            p.Contract = new Contract();   // signed in the rookie signing step (CPU: right away at the end of the draft)
            Offseason.AssignJersey(team, p, rng);
            team.AddPlayer(p);
            slot.Made = true; slot.PlayerId = p.Id; slot.PlayerName = p.Name; slot.Pos = p.Position; slot.ConsensusRank = choice.ConsensusRank;
            slot.Grade = GradeOf(slot.Overall, choice.ConsensusRank);
            DraftPickAsset asset = TradeEngine.FindPick(s, slot.PickId);
            if (asset != null) { asset.Used = true; asset.PlayerId = p.Id; }
            s.RookieRights.Add(new RookieRight { PlayerId = p.Id, TeamId = team.Id, Overall = slot.Overall, Round = slot.Round, Year = year });
            s.Pro.LastDraft.Add(new DraftPickRecord
            {
                Year = year, Overall = slot.Overall, Round = slot.Round, PickInRound = slot.PickInRound, TeamId = team.Id, PlayerId = p.Id, PlayerName = p.Name, Position = p.Position,
                College = p.Bio.College, ConsensusRank = choice.ConsensusRank
            });
            if (slot.Overall <= 10 || user)
                NewsWriter.Post(s, NewsKind.Draft, team.Nickname + " select " + p.Name + " with pick #" + slot.Overall, CareerText.PlayerLabel(p) + (string.IsNullOrEmpty(choice.SchoolName) ? "" : " (" + choice.SchoolName + ")") + " goes " + CareerText.Ordinal(slot.Overall) + " overall to the " + team.Nickname + ".", slot.Overall <= 3 ? 4 : 3, team.Id, -1, p.Id);
            s.Draft.Current++;
            if (s.Draft.Current >= s.Draft.Slots.Count) s.Draft.Finished = true;
            return slot;
        }

        /// <summary>Live draft grade of a pick from where the public board had the player (positive: a value pick).</summary>
        public static string GradeOf(int overall, int consensusRank)
        {
            int diff = overall - consensusRank;
            if (diff >= 25) return "A+";
            if (diff >= 12) return "A";
            if (diff >= 4) return "B+";
            if (diff >= -4) return "B";
            if (diff >= -12) return "C";
            if (diff >= -25) return "D";
            return "F";
        }

        /// <summary>The user selects a prospect for the pick on the clock.</summary>
        public static OpResult UserPick(CareerSave s, int prospectId)
        {
            DraftSlot slot = Current(s);
            if (slot == null) return OpResult.Fail("The draft is over.");
            if (slot.TeamId != s.UserTeamId) return OpResult.Fail("You are not on the clock.");
            Prospect p = FindProspect(s, prospectId);
            if (p == null || p.Drafted) return OpResult.Fail("That prospect is not available.");
            MakePick(s, slot, p, s.NewRng());
            return OpResult.Success(p.Player.Name + " selected at #" + slot.Overall + " (" + slot.Grade + ").");
        }

        /// <summary>Makes the pick on the clock for whoever owns it. For the user it takes the best player on his own board that fits a need.</summary>
        public static DraftSlot AutoPick(CareerSave s, Rng rng)
        {
            DraftSlot slot = Current(s);
            if (slot == null) return null;
            Team team = s.Pro.GetTeam(slot.TeamId);
            var board = BoardOf(s, team.Id);
            var taken = TakenIds(s);
            Prospect choice = DraftEngine.ChoosePick(team, board, taken, slot.Round, rng);
            if (choice == null)
                for (int i = 0; i < board.Count; i++) if (!board[i].Drafted && !taken.Contains(board[i].Id)) { choice = board[i]; break; }
            if (choice == null) { s.Draft.Finished = true; return null; }
            return MakePick(s, slot, choice, rng);
        }

        /// <summary>Runs the draft until the user is on the clock (or it ends). Returns the picks made.</summary>
        public static List<DraftSlot> SimToUserPick(CareerSave s, Rng rng)
        {
            var made = new List<DraftSlot>();
            int guard = 0;
            while (!s.Draft.Finished && !UserOnClock(s) && guard++ < 400)
            {
                DraftSlot sl = AutoPick(s, rng);
                if (sl == null) break;
                made.Add(sl);
            }
            return made;
        }

        public static List<DraftSlot> SimToEnd(CareerSave s, Rng rng)
        {
            var made = new List<DraftSlot>();
            int guard = 0;
            while (!s.Draft.Finished && guard++ < 400)
            {
                DraftSlot sl = AutoPick(s, rng);
                if (sl == null) break;
                made.Add(sl);
            }
            return made;
        }

        /// <summary>Average pick grade of a team's draft (letter) from the value of each pick against the consensus board.</summary>
        public static string TeamGrade(CareerSave s, int teamId)
        {
            double sum = 0, w = 0;
            for (int i = 0; i < s.Draft.Slots.Count; i++)
            {
                DraftSlot sl = s.Draft.Slots[i];
                if (!sl.Made || sl.TeamId != teamId) continue;
                double weight = TradeEngine.PickChartValue(sl.Overall);
                sum += (sl.Overall - sl.ConsensusRank) * weight; w += weight;
            }
            if (w <= 0) return "-";
            return GradeOf(0, -(int)Math.Round(sum / w));   // diff = overall - rank, so a positive average maps to the same scale
        }

        // ---------------------------------------------------------------- draft day trade offers
        /// <summary>CPU teams further down the order offer to move up to the pick on the clock (only when the user owns it).</summary>
        public static List<TradeOffer> TradeUpOffers(CareerSave s, Rng rng, int max = 3)
        {
            var offers = new List<TradeOffer>();
            DraftSlot cur = Current(s);
            if (cur == null || cur.TeamId != s.UserTeamId) return offers;
            DraftPickAsset mine = TradeEngine.FindPick(s, cur.PickId);
            if (mine == null) return offers;
            float want = TradeEngine.PickValue(s, mine) * 1.08f;
            for (int i = s.Draft.Current + 3; i < s.Draft.Slots.Count && i < s.Draft.Current + 28 && offers.Count < max; i++)
            {
                DraftSlot sl = s.Draft.Slots[i];
                if (sl.Made || sl.TeamId == s.UserTeamId) continue;
                Team cpu = s.Pro.GetTeam(sl.TeamId);
                DraftPickAsset theirs = TradeEngine.FindPick(s, sl.PickId);
                if (theirs == null) continue;
                var tp = new TradeProposal { TeamA = cpu.Id, TeamB = s.UserTeamId };
                tp.FromA.Add(TradeAsset.OfPick(theirs.Id));
                float sum = TradeEngine.PickValue(s, theirs);
                var extras = TradeEngine.PicksOf(s, cpu.Id);
                extras.Sort((a, b) => TradeEngine.PickValue(s, b).CompareTo(TradeEngine.PickValue(s, a)));
                for (int k = 0; k < extras.Count && sum < want && tp.FromA.Count < 4; k++)
                {
                    if (extras[k].Id == theirs.Id || extras[k].Overall > 0 && extras[k].Overall < theirs.Overall) continue;
                    float v = TradeEngine.PickValue(s, extras[k]);
                    if (sum + v > want * 1.5f) continue;
                    tp.FromA.Add(TradeAsset.OfPick(extras[k].Id)); sum += v;
                }
                if (sum < want) continue;
                tp.FromB.Add(TradeAsset.OfPick(mine.Id));
                if (!TradeEngine.Validate(s, tp).Legal) continue;
                offers.Add(new TradeOffer
                {
                    Id = s.NextId(), FromTeamId = cpu.Id, ToTeamId = s.UserTeamId, Year = s.Year, ExpiresWeek = 99, Proposal = tp,
                    Message = "The " + cpu.Nickname + " want to move up from #" + sl.Overall + " to #" + cur.Overall + "."
                });
            }
            for (int i = 0; i < offers.Count; i++) s.Inbox.Add(offers[i]);
            return offers;
        }

        // ---------------------------------------------------------------- rookie signing
        public static List<RookieRight> UnsignedRights(CareerSave s, int teamId)
        {
            var l = new List<RookieRight>();
            for (int i = 0; i < s.RookieRights.Count; i++) if (s.RookieRights[i].TeamId == teamId && !s.RookieRights[i].Signed) l.Add(s.RookieRights[i]);
            return l;
        }

        public static Contract RookieDeal(CareerSave s, RookieRight r) { return CapMath.Rookie(s, r.Overall, s.Year + 1); }

        public static OpResult SignRookie(CareerSave s, int playerId)
        {
            RookieRight r = null;
            for (int i = 0; i < s.RookieRights.Count; i++) if (s.RookieRights[i].PlayerId == playerId && !s.RookieRights[i].Signed) r = s.RookieRights[i];
            if (r == null) return OpResult.Fail("No unsigned draft pick with that id.");
            Team t = s.Pro.GetTeam(r.TeamId);
            Player p = t.FindAnyPlayer(playerId);
            if (p == null) return OpResult.Fail("The player is not on the team.");
            Contract c = RookieDeal(s, r);
            if (t.CapUsed + c.CapHit > t.SalaryCap) return OpResult.Fail("Not enough cap space (" + CareerText.Money(c.CapHit) + " needed).");
            p.Contract = c;
            r.Signed = true;
            Ledger.Tx(s, TransactionKind.RookieSigned, t.Id, p, c.Aav, p.Name + " (#" + r.Overall + ") signed a " + c.Years + "-year rookie deal.");
            return OpResult.Success(p.Name + " signed for " + CareerText.Money(c.Aav) + " per year.");
        }

        public static int SignAllRookies(CareerSave s, int teamId)
        {
            int n = 0;
            var rights = UnsignedRights(s, teamId);
            rights.Sort((a, b) => a.Overall.CompareTo(b.Overall));
            for (int i = 0; i < rights.Count; i++)
            {
                if (SignRookie(s, rights[i].PlayerId).Ok) n++;
            }
            return n;
        }

        /// <summary>Wraps the draft up: CPU picks are signed, undrafted prospects hit the pool and the college world turns over.</summary>
        public static void FinishDraft(CareerSave s, Rng rng)
        {
            var cls = s.Pro.DraftClass;
            // CPU rookies sign at once (they may not fit under the cap: those few simply sign a minimum deal)
            for (int t = 0; t < s.Pro.Teams.Count; t++)
            {
                if (t == s.UserTeamId) continue;
                var rights = UnsignedRights(s, t);
                for (int i = 0; i < rights.Count; i++)
                {
                    if (SignRookie(s, rights[i].PlayerId).Ok) continue;
                    Team team = s.Pro.GetTeam(t);
                    Player p = team.FindAnyPlayer(rights[i].PlayerId);
                    if (p == null) continue;
                    p.Contract = CapMath.Minimum(s, 3, s.Year + 1);
                    p.Contract.Rookie = true;
                    rights[i].Signed = true;
                }
            }
            for (int t = 0; t < s.Pro.Teams.Count; t++)
            {
                if (t == s.UserTeamId) continue;
                Team team = s.Pro.Teams[t];
                int cg = 0;
                while (team.CapUsed > team.SalaryCap && cg++ < 8) CapManager.AutoFix(s, team);
            }
            var removed = new HashSet<int>();
            for (int i = 0; i < cls.Count; i++)
            {
                Prospect pr = cls[i];
                removed.Add(pr.Id);
                if (pr.Drafted) continue;
                Player p = pr.Player;
                int grade = (int)pr.TrueGrade;
                if (grade < 52) continue;
                p.DraftYear = s.Year; p.DraftRound = 0; p.DraftPick = 0; p.Experience = 0;
                if (pr.SchoolId >= 0) CollegeFactory.ApplyProTransition(p);
                p.TeamId = -1; p.Contract = new Contract(); p.Retired = false; p.IsRedshirting = false;
                s.Pro.FreeAgents.Add(p);
            }
            s.Draft.Finished = true;
            if (s.College != null)
            {
                if (s.College.Season != null && s.College.Season.Games.Count > 0) CollegeFactory.UpdatePrestige(s.College, s.College.Season);
                CollegeFactory.AdvanceYear(s.College, rng, removed);
            }
            s.Pro.DraftClass = new List<Prospect>();
            s.Pro.ScoutingReports = new List<ScoutingReport>();
            s.DraftBoards = null;
        }
    }
}
