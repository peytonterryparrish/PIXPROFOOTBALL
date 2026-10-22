using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>
    /// Throws random (often illegal) management actions at the franchise for two seasons: the API must refuse bad moves with a message,
    /// never throw, and leave the league consistent.
    /// </summary>
    public class CareerFuzzTests
    {
        private static void RandomActions(CareerSave s, Rng rng, int count)
        {
            Team u = s.UserTeam;
            var used = Offseason.LeagueUsedNames(s.Pro);
            for (int n = 0; n < count; n++)
            {
                int action = rng.Range(0, 16);
                var all = CapManager.AllUnderContract(u).ToList();
                Player any = all.Count > 0 ? all[rng.Range(0, all.Count)] : null;
                Team cpu = s.Pro.Teams[rng.Range(0, s.Pro.Teams.Count)];
                switch (action)
                {
                    case 0: if (any != null && u.Roster.Count > 45) RosterManager.Release(s, u, any.Id); break;
                    case 1: if (any != null) RosterManager.ToPracticeSquad(s, u, any.Id); break;
                    case 2: if (u.PracticeSquad.Count > 0) RosterManager.Promote(s, u, u.PracticeSquad[rng.Range(0, u.PracticeSquad.Count)].Id); break;
                    case 3: if (any != null) RosterManager.PlaceOnIR(s, u, any.Id); break;
                    case 4: if (u.InjuredReserve.Count > 0) RosterManager.ActivateFromIR(s, u, u.InjuredReserve[0].Id); break;
                    case 5:
                        if (s.Pro.FreeAgents.Count > 0)
                        {
                            Player fa = s.Pro.FreeAgents[rng.Range(0, s.Pro.FreeAgents.Count)];
                            if (s.Phase == CareerPhase.FreeAgency) FreeAgency.PlaceOffer(s, fa.Id, FreeAgency.Ask(s, fa).Aav, 2, 0.1f);
                            else FreeAgency.SignNow(s, u, fa.Id, FreeAgency.Ask(s, fa).Aav, 1, rng.Chance(0.3f));
                        }
                        break;
                    case 6:
                        {
                            var tp = new TradeProposal { TeamA = u.Id, TeamB = cpu.Id };
                            if (any != null && rng.Chance(0.7f)) tp.FromA.Add(TradeAsset.OfPlayer(any.Id));
                            var picks = TradeEngine.PicksOf(s, u.Id);
                            if (picks.Count > 0 && rng.Chance(0.6f)) tp.FromA.Add(TradeAsset.OfPick(picks[rng.Range(0, picks.Count)].Id));
                            if (cpu.Roster.Count > 0 && rng.Chance(0.6f)) tp.FromB.Add(TradeAsset.OfPlayer(cpu.Roster[rng.Range(0, cpu.Roster.Count)].Id));
                            var cpuPicks = TradeEngine.PicksOf(s, cpu.Id);
                            if (cpuPicks.Count > 0 && rng.Chance(0.4f)) tp.FromB.Add(TradeAsset.OfPick(cpuPicks[rng.Range(0, cpuPicks.Count)].Id));
                            TradeEvaluation ev = TradeEngine.Evaluate(s, tp);
                            if (!ev.CpuAccepts) { TradeProposal c = TradeEngine.AskWhatItTakes(s, tp); if (c != null && rng.Chance(0.5f)) TradeEngine.Propose(s, c); }
                            else TradeEngine.Propose(s, tp);
                            break;
                        }
                    case 7: if (u.Depth.Get(Position.WR).Count > 2) DepthManager.Swap(s, u, Position.WR, rng.Range(0, 3), rng.Range(0, u.Depth.Get(Position.WR).Count)); break;
                    case 8: if (any != null) DepthManager.Insert(s, u, LeagueRules.RosterOrder[rng.Range(0, LeagueRules.RosterOrder.Length)], any.Id, rng.Range(0, 4)); break;
                    case 9: if (any != null) CapManager.Restructure(s, u, any.Id); break;
                    case 10:
                        if (s.Phase == CareerPhase.ReSign && any != null)
                        {
                            Player p = Negotiator.ExpiringPlayers(s, u).FirstOrDefault();
                            if (p != null)
                            {
                                if (rng.Chance(0.3f)) CapManager.FranchiseTag(s, u, p.Id);
                                else { ContractAsk a = Negotiator.Ask(s, u, p); Negotiator.Offer(s, u, p.Id, (long)(a.Aav * rng.Range(0.7f, 1.2f)), rng.Range(1, 6), 0.1f); }
                            }
                        }
                        break;
                    case 11:
                        if (rng.Chance(0.5f)) { var roles = new[] { CoachRole.HeadCoach, CoachRole.OffensiveCoordinator, CoachRole.DefensiveCoordinator }; StaffManager.Fire(s, u, roles[rng.Range(0, 3)]); }
                        else if (s.CoachPool.Count > 0) StaffManager.Hire(s, u, s.CoachPool[rng.Range(0, s.CoachPool.Count)].Coach.Id);
                        break;
                    case 12:
                        if (s.Phase == CareerPhase.Draft && CareerDraft.UserOnClock(s))
                        {
                            var board = CareerDraft.Board(s, "grade", true);
                            CareerDraft.UserPick(s, board[rng.Range(0, Math.Min(board.Count, 30))].Id);
                        }
                        else if (s.Phase == CareerPhase.Scouting && s.Pro.DraftClass.Count > 0) CareerDraft.Scout(s, s.Pro.DraftClass[rng.Range(0, s.Pro.DraftClass.Count)].Id, rng.Range(1, 40));
                        break;
                    case 13: if (s.Waivers.Count > 0) RosterManager.ClaimWaiver(s, s.Waivers[0].PlayerId); break;
                    case 14: { var offers = TradeEngine.PendingOffers(s); if (offers.Count > 0) { if (rng.Chance(0.6f)) TradeEngine.AcceptOffer(s, offers[0].Id); else TradeEngine.RejectOffer(s, offers[0].Id); } break; }
                    case 15:
                        if (CareerFlow.InSeason(s))
                        {
                            var games = CareerFlow.GamesThisWeek(s).Where(g => !g.Played).ToList();
                            if (games.Count > 0) CareerFlow.SimGame(s, games[rng.Range(0, games.Count)].Id);
                        }
                        break;
                }
            }
        }

        [TestCase(301)]
        [TestCase(302)]
        [TestCase(303)]
        [Category("Slow")]   // multi-season: excluded from the release run (-testCategory "!Slow")
        public void RandomManagementNeverBreaksTheFranchise(int seed)
        {
            CareerSave s = CareerTestUtil.Fresh(seed, seed % 32);
            var rng = new Rng(seed * 7);
            int guard = 0;
            while (!(s.Year == 2028 && s.Phase == CareerPhase.Preseason) && guard++ < 600)
            {
                RandomActions(s, rng, rng.Range(2, 9));
                List<string> problems = CareerValidator.Check(s, false);
                Assert.AreEqual(0, problems.Count, "after random actions in " + s.Phase + " " + s.Year + ": " + string.Join("; ", problems.Take(6).ToArray()));
                CareerFlow.AutoResolveUser(s);
                StepResult r = CareerFlow.Advance(s);
                Assert.IsTrue(r.Ok, "stuck in " + s.Phase + ": " + string.Join("; ", r.Blockers.ToArray()));
            }
            Assert.AreEqual(2028, s.Year);
            CareerFlow.AutoResolveUser(s);
            CareerFlow.Advance(s);
            CareerTestUtil.AssertClean(s, true);
        }
    }
}
