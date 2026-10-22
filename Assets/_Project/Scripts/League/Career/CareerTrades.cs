using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    public enum TradeWindow { Open, ClosedDeadline, ClosedPostseason }

    public sealed class TradeEvaluation
    {
        public bool Legal = true;
        public List<string> Problems = new List<string>();
        /// <summary>Value (trade points) of what each side receives, from the receiving side's point of view.</summary>
        public float AReceives, AGives, BReceives, BGives;
        /// <summary>The CPU side(s) would accept the deal as proposed.</summary>
        public bool CpuAccepts;
        /// <summary>Received value divided by given value for the CPU side that evaluated (>= 1.05 is needed).</summary>
        public float CpuRatio;
        public string Message = "";
    }

    /// <summary>
    /// Trades of players and draft picks (the current year's and the next two years'). Everything is priced in "trade points" on one scale
    /// where the first overall pick is worth 3000: players by overall, ceiling, age, position and contract surplus; picks by a draft value chart.
    /// </summary>
    public static class TradeEngine
    {
        public const float CpuMargin = 1.05f;
        public const int MaxPendingOffers = 3;

        // ---------------------------------------------------------------- the draft pick book
        /// <summary>Creates the draft picks for the current and the next two years that do not exist yet.</summary>
        public static void EnsurePicks(CareerSave s)
        {
            for (int y = s.Year; y <= s.Year + 2; y++)
            {
                bool have = false;
                for (int i = 0; i < s.Picks.Count; i++) if (s.Picks[i].Year == y) { have = true; break; }
                if (have) continue;
                for (int r = 1; r <= LeagueRules.DraftRounds; r++)
                    for (int t = 0; t < s.Pro.Teams.Count; t++)
                        s.Picks.Add(new DraftPickAsset { Id = s.NextId(), Year = y, Round = r, OriginalTeamId = s.Pro.Teams[t].Id, OwnerTeamId = s.Pro.Teams[t].Id });
            }
            // old, used picks are not needed any more
            for (int i = s.Picks.Count - 1; i >= 0; i--) if (s.Picks[i].Year < s.Year - 1) s.Picks.RemoveAt(i);
        }

        public static DraftPickAsset FindPick(CareerSave s, int pickId)
        {
            for (int i = 0; i < s.Picks.Count; i++) if (s.Picks[i].Id == pickId) return s.Picks[i];
            return null;
        }

        /// <summary>Picks a team owns that are still unused, soonest first.</summary>
        public static List<DraftPickAsset> PicksOf(CareerSave s, int teamId)
        {
            var l = new List<DraftPickAsset>();
            for (int i = 0; i < s.Picks.Count; i++)
                if (s.Picks[i].OwnerTeamId == teamId && !s.Picks[i].Used) l.Add(s.Picks[i]);
            l.Sort((a, b) =>
            {
                int c = a.Year.CompareTo(b.Year);
                if (c != 0) return c;
                c = a.Round.CompareTo(b.Round);
                return c != 0 ? c : a.OriginalTeamId.CompareTo(b.OriginalTeamId);
            });
            return l;
        }

        public static string PickLabel(CareerSave s, DraftPickAsset k)
        {
            string orig = k.OriginalTeamId == k.OwnerTeamId ? "" : " (from " + CareerText.TeamNick(s, k.OriginalTeamId) + ")";
            return k.Year + " round " + k.Round + (k.Overall > 0 ? " (#" + k.Overall + ")" : "") + orig;
        }

        // ---------------------------------------------------------------- values
        public static float PickChartValue(int overall)
        {
            if (overall < 1) overall = 1;
            return 3000f * (float)Math.Exp(-0.0245 * (overall - 1));
        }

        /// <summary>Expected overall pick number of a team's pick before the order is final.</summary>
        public static int EstimatedOverall(CareerSave s, DraftPickAsset pick)
        {
            if (pick.Overall > 0) return pick.Overall;
            int n = s.Pro.Teams.Count;
            var order = new List<Team>(s.Pro.Teams);
            order.Sort((a, b) => a.TeamOverall().CompareTo(b.TeamOverall()));
            int rank = order.FindIndex(t => t.Id == pick.OriginalTeamId) + 1;   // 1 = weakest team = earliest pick
            float shrink = pick.Year <= s.Year ? 0.35f : 0.6f;
            float slot = rank * (1f - shrink) + (n + 1) * 0.5f * shrink;
            return (int)Math.Round((pick.Round - 1) * n + slot);
        }

        public static float PickValue(CareerSave s, DraftPickAsset pick)
        {
            float v = PickChartValue(EstimatedOverall(s, pick));
            int ahead = pick.Year - s.Year;
            if (ahead > 0) v *= ahead == 1 ? 0.88f : 0.78f;
            return v;
        }

        private static float PositionMultiplier(Position p)
        {
            switch (p)
            {
                case Position.QB: return 1.5f;
                case Position.LE: case Position.RE: return 1.1f;
                case Position.LT: case Position.RT: return 1.0f;
                case Position.WR: case Position.CB: return 1.0f;
                case Position.DT: return 0.95f;
                case Position.LOLB: case Position.ROLB: return 0.85f;
                case Position.LG: case Position.RG: case Position.C: return 0.75f;
                case Position.TE: return 0.75f;
                case Position.MLB: return 0.7f;
                case Position.FS: case Position.SS: return 0.7f;
                case Position.RB: return 0.6f;
                case Position.K: return 0.25f;
                case Position.P: return 0.15f;
                case Position.FB: return 0.1f;
            }
            return 0.8f;
        }

        private static float AgeMultiplier(Player p)
        {
            int eff = p.Bio.Age + (30 - AgeCurve.DeclineStart(p.Position));   // quarterbacks age gracefully, running backs do not
            if (eff <= 23) return 1.15f;
            if (eff <= 26) return 1.05f;
            if (eff <= 28) return 0.95f;
            if (eff == 29) return 0.8f;
            if (eff == 30) return 0.62f;
            if (eff == 31) return 0.45f;
            if (eff == 32) return 0.3f;
            if (eff == 33) return 0.2f;
            return 0.1f;
        }

        public static float PlayerValue(CareerSave s, Player p)
        {
            float ovr = p.Ratings.Overall(p.Position);
            float adj = ovr;
            if (p.Bio.Age <= 25 && p.Potential > ovr) adj += (p.Potential - ovr) * (p.Bio.Age <= 23 ? 0.35f : 0.2f);
            float t = Math.Max(0f, Math.Min(1.4f, (adj - 55f) / 40f));
            float baseV = 4500f * t * t * t * PositionMultiplier(p.Position) * AgeMultiplier(p);
            float contract = 0f;
            if (p.Contract.YearsLeft > 0)
            {
                long market = CapMath.Market(s, p, s.Year);
                double surplus = (market - p.Contract.Aav) / 1000000.0 * Math.Min(p.Contract.YearsLeft, 4) * 22.0 / CapMath.Scale(s, s.Year);
                contract = (float)Math.Max(-0.6 * baseV, Math.Min(0.9 * baseV, surplus));
            }
            float v = baseV + contract;
            if (p.Injury.WeeksOut > 6) v *= 0.85f;
            return Math.Max(3f, v);
        }

        public static float AssetValue(CareerSave s, TradeAsset a)
        {
            if (a.IsPick) { DraftPickAsset k = FindPick(s, a.PickId); return k == null ? 0f : PickValue(s, k); }
            Player p = s.FindPlayer(a.PlayerId);
            return p == null ? 0f : PlayerValue(s, p);
        }

        // ---------------------------------------------------------------- window
        public static TradeWindow Window(CareerSave s)
        {
            switch (s.Phase)
            {
                case CareerPhase.Playoffs: case CareerPhase.PixBowl: return TradeWindow.ClosedPostseason;
                case CareerPhase.RegularSeason: return s.Season.Week > s.Settings.TradeDeadlineWeek ? TradeWindow.ClosedDeadline : TradeWindow.Open;
                default: return TradeWindow.Open;
            }
        }

        public static bool IsOpen(CareerSave s) { return Window(s) == TradeWindow.Open; }

        // ---------------------------------------------------------------- legality
        /// <summary>The contract a team takes over in a trade: the signing bonus stays with the team that paid it.</summary>
        public static Contract ReceivedContract(Contract c)
        {
            Contract n = c.Clone();
            if (c.Years > 0 && c.SigningBonus > 0)
            {
                long prorated = c.SigningBonus / c.Years;
                for (int i = 0; i < n.CapHits.Count; i++) n.CapHits[i] = Math.Max(Contract.MinSalary, n.CapHits[i] - prorated);
                n.Guaranteed = Math.Max(0L, c.Guaranteed - c.SigningBonus / c.Years * c.YearsLeft);
                n.SigningBonus = 0;
            }
            return n;
        }

        /// <summary>Dead money the sending team keeps: the unamortised signing bonus.</summary>
        public static void TradeDead(Contract c, out long now, out long next)
        {
            long dead = c.Years > 0 ? c.SigningBonus / c.Years * c.YearsLeft : 0L;
            now = c.YearsLeft > 1 ? dead / 2 : dead;
            next = dead - now;
        }

        private static bool Owns(CareerSave s, int teamId, TradeAsset a, out string problem)
        {
            problem = null;
            if (a.IsPick)
            {
                DraftPickAsset k = FindPick(s, a.PickId);
                if (k == null) { problem = "Unknown draft pick."; return false; }
                if (k.OwnerTeamId != teamId) { problem = PickLabel(s, k) + " is not owned by " + CareerText.TeamNick(s, teamId) + "."; return false; }
                if (k.Used) { problem = PickLabel(s, k) + " was already used."; return false; }
                return true;
            }
            Team t = s.Pro.GetTeam(teamId);
            Player p = t.GetPlayer(a.PlayerId);
            if (p == null) { problem = "That player is not on the active roster of the " + t.Nickname + " (practice squad and IR players cannot be traded)."; return false; }
            if (p.Contract.YearsLeft <= 0) { problem = p.Name + "'s contract has expired."; return false; }
            for (int i = 0; i < s.RookieRights.Count; i++) if (s.RookieRights[i].PlayerId == p.Id && !s.RookieRights[i].Signed) { problem = p.Name + " is an unsigned draft pick."; return false; }
            return true;
        }

        public static TradeEvaluation Validate(CareerSave s, TradeProposal tp)
        {
            var ev = new TradeEvaluation();
            if (!IsOpen(s)) { ev.Legal = false; ev.Problems.Add(Window(s) == TradeWindow.ClosedDeadline ? "The trade deadline has passed." : "Trades are closed during the playoffs."); }
            if (tp.TeamA == tp.TeamB) { ev.Legal = false; ev.Problems.Add("A team cannot trade with itself."); }
            if (tp.FromA.Count == 0 && tp.FromB.Count == 0) { ev.Legal = false; ev.Problems.Add("Nothing is on the table."); }
            Team a = s.Pro.GetTeam(tp.TeamA), b = s.Pro.GetTeam(tp.TeamB);
            if (a == null || b == null) { ev.Legal = false; ev.Problems.Add("Unknown team."); return ev; }
            string problem;
            var seen = new HashSet<string>();
            for (int i = 0; i < tp.FromA.Count; i++)
            {
                string key = tp.FromA[i].IsPick ? "k" + tp.FromA[i].PickId : "p" + tp.FromA[i].PlayerId;
                if (!seen.Add(key)) { ev.Legal = false; ev.Problems.Add("An asset appears twice."); }
                if (!Owns(s, a.Id, tp.FromA[i], out problem)) { ev.Legal = false; ev.Problems.Add(problem); }
            }
            for (int i = 0; i < tp.FromB.Count; i++)
            {
                string key = tp.FromB[i].IsPick ? "k" + tp.FromB[i].PickId : "p" + tp.FromB[i].PlayerId;
                if (!seen.Add(key)) { ev.Legal = false; ev.Problems.Add("An asset appears twice."); }
                if (!Owns(s, b.Id, tp.FromB[i], out problem)) { ev.Legal = false; ev.Problems.Add(problem); }
            }
            if (!ev.Legal) return ev;
            CheckSide(s, a, tp.FromA, tp.FromB, ev);
            CheckSide(s, b, tp.FromB, tp.FromA, ev);
            return ev;
        }

        private static void CheckSide(CareerSave s, Team t, List<TradeAsset> outgoing, List<TradeAsset> incoming, TradeEvaluation ev)
        {
            int outPlayers = 0, inPlayers = 0;
            long capDelta = 0;
            for (int i = 0; i < outgoing.Count; i++)
            {
                if (outgoing[i].IsPick) continue;
                Player p = t.GetPlayer(outgoing[i].PlayerId);
                if (p == null) continue;
                outPlayers++;
                long dn, dx; TradeDead(p.Contract, out dn, out dx);
                capDelta -= p.Contract.CapHit; capDelta += dn;
            }
            for (int i = 0; i < incoming.Count; i++)
            {
                if (incoming[i].IsPick) continue;
                Player p = s.FindPlayer(incoming[i].PlayerId);
                if (p == null) continue;
                inPlayers++;
                capDelta += ReceivedContract(p.Contract).CapHit;
            }
            int limit = RosterManager.ActiveLimitNow(s);
            if (t.Id == s.UserTeamId && t.Roster.Count - outPlayers + inPlayers > limit) { ev.Legal = false; ev.Problems.Add("The " + t.Nickname + " would exceed the roster limit (" + limit + ")."); }
            if (t.CapUsed + capDelta > t.SalaryCap && capDelta > 0) { ev.Legal = false; ev.Problems.Add("The " + t.Nickname + " would be over the cap (" + CareerText.Money(t.CapUsed + capDelta - t.SalaryCap) + ")."); }
        }

        // ---------------------------------------------------------------- evaluation
        private static float SideValue(CareerSave s, Team receiver, List<TradeAsset> assets, bool receiving)
        {
            float sum = 0f;
            for (int i = 0; i < assets.Count; i++)
            {
                float v = AssetValue(s, assets[i]);
                if (!assets[i].IsPick)
                {
                    Player p = s.FindPlayer(assets[i].PlayerId);
                    if (p != null) v *= NeedFactor(s, receiver, p, receiving);
                }
                sum += v;
            }
            return sum;
        }

        /// <summary>How a team feels about one player: a starter it cannot replace is worth more, a surplus body is worth less.</summary>
        private static float NeedFactor(CareerSave s, Team t, Player p, bool receiving)
        {
            Position pos = p.Position;
            int starters = LeagueRules.Starters(pos);
            int ovr = p.Ratings.Overall(pos);
            var mine = new List<int>();
            for (int i = 0; i < t.Roster.Count; i++)
                if (t.Roster[i].Position == pos && t.Roster[i].Id != p.Id) mine.Add(t.Roster[i].Ratings.Overall(pos));
            mine.Sort((a, b) => b.CompareTo(a));
            if (receiving)
            {
                float weakestStarter = mine.Count >= starters ? mine[starters - 1] : 0f;
                if (ovr > weakestStarter + 2) return 1.06f;                       // fills a need
                if (mine.Count >= LeagueRules.ProSlots(pos)) return 0.8f;          // surplus
                return 1f;
            }
            // giving up a player
            int rank = 0;
            for (int i = 0; i < mine.Count; i++) if (mine[i] > ovr) rank++;
            if (rank < starters)
            {
                float replacement = mine.Count > rank ? mine[Math.Min(mine.Count - 1, starters - 1)] : 0f;
                return replacement < ovr - 8 ? 1.15f : 1.06f;
            }
            return 1f;
        }

        public static TradeEvaluation Evaluate(CareerSave s, TradeProposal tp)
        {
            TradeEvaluation ev = Validate(s, tp);
            Team a = s.Pro.GetTeam(tp.TeamA), b = s.Pro.GetTeam(tp.TeamB);
            if (a == null || b == null) return ev;
            ev.AReceives = SideValue(s, a, tp.FromB, true);
            ev.AGives = SideValue(s, a, tp.FromA, false);
            ev.BReceives = SideValue(s, b, tp.FromA, true);
            ev.BGives = SideValue(s, b, tp.FromB, false);
            // the CPU side(s)
            bool aCpu = a.Id != s.UserTeamId, bCpu = b.Id != s.UserTeamId;
            float ratioB = ev.BGives <= 0.01f ? 99f : ev.BReceives / ev.BGives;
            float ratioA = ev.AGives <= 0.01f ? 99f : ev.AReceives / ev.AGives;
            ev.CpuRatio = bCpu ? ratioB : ratioA;
            bool ok = true;
            // a CPU team that answers the user wants a 5 percent edge; two CPU teams just need a fair deal each
            float needA = aCpu && bCpu ? 0.98f : CpuMargin, needB = aCpu && bCpu ? 0.98f : CpuMargin;
            if (bCpu && ratioB < needB) ok = false;
            if (aCpu && ratioA < needA) ok = false;
            if (bCpu && Untouchable(s, b, tp.FromB, ev.BReceives, ev.BGives)) ok = false;
            if (aCpu && Untouchable(s, a, tp.FromA, ev.AReceives, ev.AGives)) ok = false;
            ev.CpuAccepts = ev.Legal && ok;
            if (!ev.Legal) ev.Message = ev.Problems.Count > 0 ? ev.Problems[0] : "Illegal trade.";
            else if (ev.CpuAccepts) ev.Message = "They like this deal.";
            else ev.Message = ev.CpuRatio >= 0.85f ? "Close, but they want a little more." : "They are not interested.";
            return ev;
        }

        /// <summary>The CPU will not move its franchise player unless it is overpaid handsomely.</summary>
        private static bool Untouchable(CareerSave s, Team cpu, List<TradeAsset> giving, float received, float gave)
        {
            for (int i = 0; i < giving.Count; i++)
            {
                if (giving[i].IsPick) continue;
                Player p = cpu.GetPlayer(giving[i].PlayerId);
                if (p == null) continue;
                bool franchise = p.Position == Position.QB && Offseason.IsStarter(cpu, p) && p.Overall >= 80;
                if ((franchise || p.Overall >= 90) && received < gave * 1.5f) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- proposing and executing
        /// <summary>The user offers a trade to a CPU team; it is executed at once when the CPU accepts.</summary>
        public static OpResult Propose(CareerSave s, TradeProposal tp)
        {
            TradeEvaluation ev = Evaluate(s, tp);
            if (!ev.Legal) return OpResult.Fail(ev.Message);
            if (!ev.CpuAccepts) return OpResult.Fail(ev.Message);
            return Execute(s, tp);
        }

        public static OpResult Execute(CareerSave s, TradeProposal tp)
        {
            TradeEvaluation v = Validate(s, tp);
            if (!v.Legal) return OpResult.Fail(v.Problems.Count > 0 ? v.Problems[0] : "Illegal trade.");
            Team a = s.Pro.GetTeam(tp.TeamA), b = s.Pro.GetTeam(tp.TeamB);
            var rng = s.NewRng();
            var movedToB = new List<Player>(); var movedToA = new List<Player>();
            Player star = null;
            for (int i = 0; i < tp.FromA.Count; i++)
            {
                if (tp.FromA[i].IsPick) { FindPick(s, tp.FromA[i].PickId).OwnerTeamId = b.Id; continue; }
                Player p = a.GetPlayer(tp.FromA[i].PlayerId);
                MovePlayer(s, a, b, p, rng); movedToB.Add(p);
                if (star == null || p.Overall > star.Overall) star = p;
            }
            for (int i = 0; i < tp.FromB.Count; i++)
            {
                if (tp.FromB[i].IsPick) { FindPick(s, tp.FromB[i].PickId).OwnerTeamId = a.Id; continue; }
                Player p = b.GetPlayer(tp.FromB[i].PlayerId);
                MovePlayer(s, b, a, p, rng); movedToA.Add(p);
                if (star == null || p.Overall > star.Overall) star = p;
            }
            TrimCpu(s, a); TrimCpu(s, b);
            RosterManager.AfterMove(s, a); RosterManager.AfterMove(s, b);
            if (s.Draft != null && s.Draft.Built && !s.Draft.Finished) CareerDraft.SyncSlots(s);
            string sa = Describe(s, tp.FromA), sb = Describe(s, tp.FromB);
            Ledger.Tx(s, TransactionKind.Trade, a.Id, null, 0, a.Nickname + " send " + sa + " to the " + b.Nickname + " for " + sb + ".", b.Id);
            float total = 0f;
            for (int i = 0; i < tp.FromA.Count; i++) total += AssetValue(s, tp.FromA[i]);
            for (int i = 0; i < tp.FromB.Count; i++) total += AssetValue(s, tp.FromB[i]);
            int pr = total >= 3000f ? 4 : (total >= 1200f ? 3 : 2);
            if (a.Id == s.UserTeamId || b.Id == s.UserTeamId || pr >= 3)
                NewsWriter.Post(s, NewsKind.Trade, a.Nickname + " and " + b.Nickname + " agree to trade", a.Nickname + " receive " + sb + "; the " + b.Nickname + " receive " + sa + ".", pr, a.Id, b.Id, star != null ? star.Id : 0);
            return OpResult.Success("Trade completed: you get " + sb + ", they get " + sa + ".");
        }

        private static void TrimCpu(CareerSave s, Team t)
        {
            if (t.Id == s.UserTeamId) return;
            int limit = RosterManager.ActiveLimitNow(s);
            int guard = 0;
            while (t.Roster.Count > limit && guard++ < 10) RosterManager.DropWorstBackup(s, t, (Position)(-1));
        }

        private static string Describe(CareerSave s, List<TradeAsset> assets)
        {
            if (assets.Count == 0) return "nothing";
            var parts = new List<string>();
            for (int i = 0; i < assets.Count; i++)
            {
                if (assets[i].IsPick) { DraftPickAsset k = FindPick(s, assets[i].PickId); parts.Add(k != null ? k.Year + " round " + k.Round + " pick" : "a pick"); }
                else { Player p = s.FindPlayer(assets[i].PlayerId); parts.Add(p != null ? p.Name + " (" + p.Position + ")" : "a player"); }
            }
            return string.Join(", ", parts.ToArray());
        }

        private static void MovePlayer(CareerSave s, Team from, Team to, Player p, Rng rng)
        {
            long dn, dx; TradeDead(p.Contract, out dn, out dx);
            from.DeadCap += dn; from.DeadCapNext += dx;
            from.RemovePlayer(p);
            p.Contract = ReceivedContract(p.Contract);
            RosterManager.RememberPrior(s, p, from.Id);
            p.Morale = Math.Max(40, p.Morale - 4);
            Offseason.AssignJersey(to, p, rng);
            to.AddPlayer(p);
        }

        // ---------------------------------------------------------------- counter offers
        /// <summary>
        /// When the CPU declines, asks for more: adds the user's assets (smallest that closes the gap, up to three) until the CPU is satisfied.
        /// Returns null when nothing the user owns can make the deal work.
        /// </summary>
        public static TradeProposal AskWhatItTakes(CareerSave s, TradeProposal tp)
        {
            var copy = Copy(tp);
            Team user = s.Pro.GetTeam(copy.TeamA);
            bool userIsA = copy.TeamA == s.UserTeamId;
            if (!userIsA) return null;
            for (int round = 0; round < 3; round++)
            {
                TradeEvaluation ev = Evaluate(s, copy);
                if (ev.CpuAccepts) return copy;
                if (!ev.Legal) return null;
                float need = ev.BGives * CpuMargin - ev.BReceives;
                if (need <= 0f) need = 1f;
                TradeAsset best = null; float bestV = float.MaxValue; TradeAsset biggest = null; float biggestV = 0f;
                var taken = new HashSet<string>();
                for (int i = 0; i < copy.FromA.Count; i++) taken.Add(copy.FromA[i].IsPick ? "k" + copy.FromA[i].PickId : "p" + copy.FromA[i].PlayerId);
                var cands = new List<TradeAsset>();
                var picks = PicksOf(s, user.Id);
                for (int i = 0; i < picks.Count; i++) cands.Add(TradeAsset.OfPick(picks[i].Id));
                for (int i = 0; i < user.Roster.Count; i++)
                    if (user.Roster[i].Contract.YearsLeft > 0) cands.Add(TradeAsset.OfPlayer(user.Roster[i].Id));
                Team cpu = s.Pro.GetTeam(copy.TeamB);
                for (int i = 0; i < cands.Count; i++)
                {
                    TradeAsset c = cands[i];
                    string key = c.IsPick ? "k" + c.PickId : "p" + c.PlayerId;
                    if (taken.Contains(key)) continue;
                    var trial = Copy(copy); trial.FromA.Add(c);
                    if (!Validate(s, trial).Legal) continue;
                    float v = SideValue(s, cpu, new List<TradeAsset> { c }, true);
                    if (v >= need && v < bestV) { bestV = v; best = c; }
                    if (v > biggestV) { biggestV = v; biggest = c; }
                }
                TradeAsset add = best ?? biggest;
                if (add == null) return null;
                copy.FromA.Add(add);
            }
            return Evaluate(s, copy).CpuAccepts ? copy : null;
        }

        public static TradeProposal Copy(TradeProposal tp)
        {
            var c = new TradeProposal { TeamA = tp.TeamA, TeamB = tp.TeamB };
            for (int i = 0; i < tp.FromA.Count; i++) c.FromA.Add(new TradeAsset { IsPick = tp.FromA[i].IsPick, PlayerId = tp.FromA[i].PlayerId, PickId = tp.FromA[i].PickId });
            for (int i = 0; i < tp.FromB.Count; i++) c.FromB.Add(new TradeAsset { IsPick = tp.FromB[i].IsPick, PlayerId = tp.FromB[i].PlayerId, PickId = tp.FromB[i].PickId });
            return c;
        }

        // ---------------------------------------------------------------- CPU-initiated offers
        public static List<TradeOffer> PendingOffers(CareerSave s)
        {
            var l = new List<TradeOffer>();
            for (int i = 0; i < s.Inbox.Count; i++) if (s.Inbox[i].Status == TradeOfferStatus.Pending) l.Add(s.Inbox[i]);
            return l;
        }

        public static void ExpireOffers(CareerSave s, bool all = false)
        {
            int week = s.Season != null ? s.Season.Week : 0;
            for (int i = 0; i < s.Inbox.Count; i++)
            {
                TradeOffer o = s.Inbox[i];
                if (o.Status != TradeOfferStatus.Pending) continue;
                if (all || o.Year != s.Year || (s.Phase == CareerPhase.RegularSeason && week > o.ExpiresWeek)) o.Status = TradeOfferStatus.Expired;
            }
            if (s.Inbox.Count > 60) s.Inbox.RemoveRange(0, s.Inbox.Count - 40);
        }

        /// <summary>CPU teams approach the user with offers for his players (one or two a week at most).</summary>
        public static int GenerateOffers(CareerSave s, Rng rng)
        {
            if (!IsOpen(s)) return 0;
            ExpireOffers(s);
            int pending = PendingOffers(s).Count;
            if (pending >= MaxPendingOffers) return 0;
            Team user = s.UserTeam;
            int created = 0;
            var teams = new List<Team>(s.Pro.Teams);
            rng.Shuffle(teams);
            for (int ti = 0; ti < teams.Count && pending + created < MaxPendingOffers; ti++)
            {
                Team cpu = teams[ti];
                if (cpu.Id == user.Id || !rng.Chance(0.10f)) continue;
                // the position where the CPU team is weakest relative to what the user has
                Player target = null; float bestGain = 4f;
                for (int i = 0; i < user.Roster.Count; i++)
                {
                    Player p = user.Roster[i];
                    if (p.Contract.YearsLeft <= 0 || PlayerValue(s, p) < 120f) continue;
                    float starter = Offseason.BestOvr(cpu, p.Position);
                    float gain = p.Ratings.Overall(p.Position) - starter;
                    if (gain > bestGain && rng.Chance(0.35f)) { bestGain = gain; target = p; }
                }
                if (target == null) continue;
                float want = PlayerValue(s, target) * 1.12f;
                TradeProposal tp = BuildPayment(s, cpu, user, want, rng);
                if (tp == null) continue;
                tp.FromB.Add(TradeAsset.OfPlayer(target.Id));
                TradeEvaluation ev = Validate(s, tp);
                if (!ev.Legal) continue;
                var offer = new TradeOffer
                {
                    Id = s.NextId(), FromTeamId = cpu.Id, ToTeamId = user.Id, Year = s.Year, ExpiresWeek = (s.Season != null ? s.Season.Week : 0) + 1,
                    Message = "The " + cpu.Nickname + " want " + target.Name + " (" + target.Position + ", " + target.Overall + " OVR).", Proposal = tp
                };
                s.Inbox.Add(offer);
                created++;
            }
            return created;
        }

        /// <summary>Assets of the payer (picks first, then surplus players) worth about <paramref name="want"/> trade points; null when the payer cannot afford it.</summary>
        private static TradeProposal BuildPayment(CareerSave s, Team payer, Team other, float want, Rng rng, float maxValue = -1f)
        {
            if (maxValue < 0f) maxValue = want * 1.8f;
            var tp = new TradeProposal { TeamA = payer.Id, TeamB = other.Id };
            float sum = 0f;
            var picks = PicksOf(s, payer.Id);
            // best single pick that covers the price without overpaying by much
            DraftPickAsset single = null; float singleV = float.MaxValue;
            for (int i = 0; i < picks.Count; i++)
            {
                float v = PickValue(s, picks[i]);
                if (v >= want && v < singleV && v <= maxValue) { singleV = v; single = picks[i]; }
            }
            if (single != null) { tp.FromA.Add(TradeAsset.OfPick(single.Id)); return tp; }
            // otherwise stack picks (cheapest first, up to three) and top up with a backup player
            picks.Sort((x, y) => PickValue(s, y).CompareTo(PickValue(s, x)));
            for (int i = 0; i < picks.Count && tp.FromA.Count < 3 && sum < want; i++)
            {
                float v = PickValue(s, picks[i]);
                if (sum + v > maxValue) continue;
                tp.FromA.Add(TradeAsset.OfPick(picks[i].Id)); sum += v;
            }
            if (sum >= want) return tp;
            Player filler = null; float fillV = 0f;
            for (int i = 0; i < payer.Roster.Count; i++)
            {
                Player p = payer.Roster[i];
                if (p.Contract.YearsLeft <= 0 || Offseason.IsStarter(payer, p)) continue;
                float v = PlayerValue(s, p);
                if (sum + v >= want && sum + v <= maxValue && (filler == null || v < fillV)) { filler = p; fillV = v; }
            }
            if (filler == null) return null;
            tp.FromA.Add(TradeAsset.OfPlayer(filler.Id));
            return tp;
        }

        public static OpResult AcceptOffer(CareerSave s, int offerId)
        {
            TradeOffer o = null;
            for (int i = 0; i < s.Inbox.Count; i++) if (s.Inbox[i].Id == offerId) o = s.Inbox[i];
            if (o == null || o.Status != TradeOfferStatus.Pending) return OpResult.Fail("That offer is no longer available.");
            OpResult r = Execute(s, o.Proposal);
            o.Status = r.Ok ? TradeOfferStatus.Accepted : TradeOfferStatus.Expired;
            return r;
        }

        public static OpResult RejectOffer(CareerSave s, int offerId)
        {
            for (int i = 0; i < s.Inbox.Count; i++)
                if (s.Inbox[i].Id == offerId && s.Inbox[i].Status == TradeOfferStatus.Pending) { s.Inbox[i].Status = TradeOfferStatus.Rejected; return OpResult.Success("Offer declined."); }
            return OpResult.Fail("That offer is no longer available.");
        }

        // ---------------------------------------------------------------- CPU to CPU
        /// <summary>CPU teams trade among themselves: a team with a surplus at a position sells to a team that needs it, for picks. Returns the number of trades.</summary>
        public static int SimulateCpuTrades(CareerSave s, Rng rng, int maxTrades, int attempts = 40)
        {
            if (!IsOpen(s)) return 0;
            int done = 0;
            for (int n = 0; n < attempts && done < maxTrades; n++)
            {
                Team buyer = s.Pro.Teams[rng.Range(0, s.Pro.Teams.Count)];
                Team seller = s.Pro.Teams[rng.Range(0, s.Pro.Teams.Count)];
                if (buyer.Id == seller.Id || buyer.Id == s.UserTeamId || seller.Id == s.UserTeamId) continue;
                Player target = null; float bestGain = 5f;
                for (int i = 0; i < seller.Roster.Count; i++)
                {
                    Player p = seller.Roster[i];
                    if (p.Contract.YearsLeft <= 0) continue;
                    float v = PlayerValue(s, p);
                    if (v < 60f || v > 1500f) continue;
                    // the seller must have a surplus: the player is not one of its starters, or its starter group is deep
                    bool starter = Offseason.IsStarter(seller, p);
                    if (starter) continue;
                    float gain = p.Ratings.Overall(p.Position) - Offseason.BestOvr(buyer, p.Position);
                    if (gain > bestGain) { bestGain = gain; target = p; }
                }
                if (target == null) continue;
                float lo = SideValue(s, seller, new List<TradeAsset> { TradeAsset.OfPlayer(target.Id) }, false) * 1.0f;
                float hi = SideValue(s, buyer, new List<TradeAsset> { TradeAsset.OfPlayer(target.Id) }, true) * 0.99f;
                if (hi < lo) continue;
                TradeProposal tp = BuildPayment(s, buyer, seller, lo, rng, hi);
                if (tp == null) continue;
                tp.FromB.Add(TradeAsset.OfPlayer(target.Id));
                TradeEvaluation ev = Evaluate(s, tp);
                if (!ev.Legal || !ev.CpuAccepts) continue;
                if (Execute(s, tp).Ok) done++;
            }
            return done;
        }
    }
}
