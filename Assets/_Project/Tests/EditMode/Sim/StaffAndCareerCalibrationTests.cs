using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Sim;
using CoreTeam = PixFootball.Core.Team;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>
    /// v0.8.0: the coaching staff's own play-calling rates (blitz, man coverage, deep shots, no-huddle tempo; <c>StaffManager.Tendencies</c>) reach the CPU
    /// coach, and a whole league of players with the catalogue's abilities (and moods) still plays football inside the calibration bands.
    /// </summary>
    public class SimStaffTendencyTests
    {
        private static LeagueTeam TeamWith(ProLeague lg, int index, OffenseScheme off, DefenseScheme def)
        {
            LeagueTeam t = lg.Teams[index];
            t.OffensiveCoordinator.OffScheme = off; t.OffensiveCoordinator.PassTendency = 50;
            t.DefensiveCoordinator.DefScheme = def; t.DefensiveCoordinator.Aggression = 50;
            t.HeadCoach.Aggression = 50;
            return t;
        }

        [Test]
        public void TheLeagueAverageStaffIsNeutral()
        {
            ProLeague lg = LeagueFactory.CreatePro(7);
            double blitz = 0, man = 0, deep = 0, hud = 0;
            foreach (LeagueTeam t in lg.Teams)
            {
                CoachProfile p = PhysicalGameSim.ProfileOf(t);
                blitz += p.BlitzBias; man += p.ManBias; deep += p.DeepShotBias; hud += p.NoHuddleBias;
            }
            int n = lg.Teams.Count;
            TestContext.WriteLine("STAFF league mean bias: blitz " + (blitz / n).ToString("0.000") + " man " + (man / n).ToString("0.000") + " deep " + (deep / n).ToString("0.000") + " no-huddle " + (hud / n).ToString("0.000"));
            Assert.That(blitz / n, Is.InRange(-0.02, 0.02));
            Assert.That(man / n, Is.InRange(-0.03, 0.03));
            Assert.That(deep / n, Is.InRange(-0.15, 0.15));
            Assert.That(hud / n, Is.InRange(-0.03, 0.03));
        }

        private static double Share(Func<int, Play> call, int n, Func<Play, bool> test)
        {
            int hit = 0;
            for (int i = 0; i < n; i++) if (test(call(i))) hit++;
            return hit / (double)n;
        }

        [Test]
        public void AnAggressiveDefensiveStaffBlitzesAndPlaysMoreMan()
        {
            ProLeague lg = LeagueFactory.CreatePro(7);
            CoachProfile aggressive = PhysicalGameSim.ProfileOf(TeamWith(lg, 0, OffenseScheme.Balanced, DefenseScheme.Aggressive));
            CoachProfile zone = PhysicalGameSim.ProfileOf(TeamWith(lg, 1, OffenseScheme.Balanced, DefenseScheme.Zone));
            Assert.Greater(aggressive.BlitzBias, zone.BlitzBias);
            Assert.Greater(aggressive.ManBias, zone.ManBias);
            Playbook book = DefaultPlaybooks.Create();
            var gs = new GameState(RuleSet.Pro()) { Phase = GamePhase.Scrimmage, BallSpot = 45, Down = 2, Distance = 8, Possession = CoreTeam.Home, Quarter = 2, ClockSeconds = 700f };
            Formation shotgun = book.FindFormation("Shotgun");
            var callerA = new PlayCaller(book, new Rng(11));
            var callerZ = new PlayCaller(book, new Rng(11));
            double blitzA = Share(i => callerA.CallDefense(gs, aggressive, shotgun), 4000, p => p.Category == PlayCategory.Blitz);
            double blitzZ = Share(i => callerZ.CallDefense(gs, zone, shotgun), 4000, p => p.Category == PlayCategory.Blitz);
            var callerA2 = new PlayCaller(book, new Rng(12));
            var callerZ2 = new PlayCaller(book, new Rng(12));
            double manA = Share(i => callerA2.CallDefense(gs, aggressive, shotgun), 4000, p => p.Category == PlayCategory.ManCoverage);
            double manZ = Share(i => callerZ2.CallDefense(gs, zone, shotgun), 4000, p => p.Category == PlayCategory.ManCoverage);
            TestContext.WriteLine("STAFF blitz share: aggressive " + blitzA.ToString("0.000") + " zone " + blitzZ.ToString("0.000") + "; man share: aggressive " + manA.ToString("0.000") + " zone " + manZ.ToString("0.000"));
            Assert.Greater(blitzA, blitzZ + 0.08);
            Assert.Greater(manA, manZ + 0.12);
        }

        [Test]
        public void AnAirRaidStaffThrowsMoreDeepShotsThanAPowerRunStaff()
        {
            ProLeague lg = LeagueFactory.CreatePro(7);
            CoachProfile air = PhysicalGameSim.ProfileOf(TeamWith(lg, 0, OffenseScheme.AirRaid, DefenseScheme.Base43));
            CoachProfile power = PhysicalGameSim.ProfileOf(TeamWith(lg, 1, OffenseScheme.PowerRun, DefenseScheme.Base43));
            Playbook book = DefaultPlaybooks.Create();
            var gs = new GameState(RuleSet.Pro()) { Phase = GamePhase.Scrimmage, BallSpot = 40, Down = 2, Distance = 11, Possession = CoreTeam.Home, Quarter = 2, ClockSeconds = 700f };
            var recent = new List<Play>();
            double DeepShare(CoachProfile c, int seed)
            {
                var caller = new PlayCaller(book, new Rng(seed));
                int deep = 0, pass = 0;
                for (int i = 0; i < 6000; i++)
                {
                    Play p = caller.CallOffense(gs, c, recent);
                    if (!p.IsPass) continue;
                    pass++;
                    if (p.Category == PlayCategory.DeepPass) deep++;
                }
                return deep / (double)Math.Max(1, pass);
            }
            double a = DeepShare(air, 21), pw = DeepShare(power, 21);
            CoachProfile airFlat = air; airFlat.DeepShotBias = 0f;
            double aFlat = DeepShare(airFlat, 21);
            TestContext.WriteLine("STAFF deep-pass share of passes: air raid " + a.ToString("0.000") + " (without the staff rate " + aFlat.ToString("0.000") + ") power run " + pw.ToString("0.000"));
            Assert.Greater(a, pw + 0.04);
            Assert.Greater(a, aFlat + 0.01, "the deep-shot rate adds to the scheme's own preference");
        }

        [Test, Category("Slow")]
        public void AFastTempoStaffRunsMorePlaysAGame()
        {
            double Plays(OffenseScheme scheme, bool staff)
            {
                ProLeague lg = LeagueFactory.CreatePro(7);
                LeagueTeam a = TeamWith(lg, 0, scheme, DefenseScheme.Base43), b = TeamWith(lg, 1, scheme, DefenseScheme.Base43);
                var counts = new int[16];
                Parallel.For(0, counts.Length, g =>   // independent games over a read-only league
                {
                    var game = new PhysicalGameSim(a, b, RuleSet.Pro(), new Rng(900 + g)) { StaffTendencies = staff };
                    counts[g] = game.Run();
                });
                return counts.Average();
            }
            double spread = Plays(OffenseScheme.SpreadOption, true), power = Plays(OffenseScheme.PowerRun, true), spreadOff = Plays(OffenseScheme.SpreadOption, false);
            TestContext.WriteLine("STAFF plays per game: spread/no-huddle " + spread.ToString("0.0") + " (staff rates off " + spreadOff.ToString("0.0") + "), power run " + power.ToString("0.0"));
            Assert.Greater(spread, power + 3.0, "a no-huddle staff snaps the ball more often");
        }

        [Test]
        public void StaffTendenciesCanBeSwitchedOff()
        {
            ProLeague lg = LeagueFactory.CreatePro(7);
            TeamWith(lg, 0, OffenseScheme.AirRaid, DefenseScheme.Aggressive);
            var game = new PhysicalGameSim(lg.Teams[0], lg.Teams[1], RuleSet.Pro(), new Rng(5));
            Assert.AreNotEqual(0f, game.ProfileOf(CoreTeam.Home).BlitzBias);
            game.StaffTendencies = false;
            CoachProfile p = game.ProfileOf(CoreTeam.Home);
            Assert.AreEqual(0f, p.BlitzBias); Assert.AreEqual(0f, p.ManBias); Assert.AreEqual(0f, p.DeepShotBias); Assert.AreEqual(0f, p.NoHuddleBias);
        }

        // ------------------------------------------------------------------ the league as a whole

        private struct League
        {
            public double Pts, PassYds, RushYds, Cmp, Att, Sacks, Ints, FumLost, Plays;
        }

        private static League PlayLeague(bool abilities, int games)
        {
            ProLeague lg = LeagueFactory.CreatePro(7);
            var arng = new Rng(8080);
            int count = 0, players = 0;
            foreach (LeagueTeam t in lg.Teams) foreach (Player p in t.Roster) { AbilityCatalog.AssignInitial(p, arng); count += p.Abilities.Count; players++; if (!abilities) p.Abilities.Clear(); }
            if (abilities) TestContext.WriteLine("CAL " + count + " abilities on " + players + " players (" + (count / (double)players).ToString("0.00") + " each)");
            var pairs = new int[games][];
            var rng = new Rng(2025);
            for (int g = 0; g < games; g++)
            {
                int a = rng.Range(0, 32), b = rng.Range(0, 32);
                while (b == a) b = rng.Range(0, 32);
                pairs[g] = new[] { a, b };
            }
            var parts = new League[games];
            Parallel.For(0, games, g =>   // games are independent and read the league only
            {
                var game = new PhysicalGameSim(lg.Teams[pairs[g][0]], lg.Teams[pairs[g][1]], RuleSet.Pro(), new Rng(700 + g));
                game.Run();
                var p = new League { Pts = game.State.HomeScore + game.State.AwayScore };
                foreach (PlayRecord rec in game.Plays)
                {
                    PlayResult r = rec.Result;
                    if (r == null || (r.Penalty != null && rec.PenaltyAccepted)) continue;
                    switch (r.Type)
                    {
                        case PlayType.Run: p.Plays++; p.RushYds += r.Yards; break;
                        case PlayType.Pass: p.Plays++; p.Att++; p.Cmp++; p.PassYds += r.Yards; break;
                        case PlayType.Incomplete: p.Plays++; p.Att++; break;
                        case PlayType.Interception: p.Plays++; p.Att++; p.Ints++; break;
                        case PlayType.Sack: p.Plays++; p.Sacks++; break;
                        case PlayType.Fumble: p.Plays++; if (r.Turnover) p.FumLost++; break;
                    }
                }
                parts[g] = p;
            });
            var l = new League();
            foreach (League p in parts) { l.Pts += p.Pts; l.PassYds += p.PassYds; l.RushYds += p.RushYds; l.Cmp += p.Cmp; l.Att += p.Att; l.Sacks += p.Sacks; l.Ints += p.Ints; l.FumLost += p.FumLost; l.Plays += p.Plays; }
            double teams = games * 2.0;
            l.Pts /= teams; l.PassYds /= teams; l.RushYds /= teams; l.Sacks /= teams; l.Ints /= teams; l.FumLost /= teams; l.Plays /= teams;
            l.Cmp = 100 * l.Cmp / l.Att;
            return l;
        }

        private static string Line(League l)
        {
            return "pts/team " + l.Pts.ToString("0.0") + " plays " + l.Plays.ToString("0.0") + " rush " + l.RushYds.ToString("0") + " pass " + l.PassYds.ToString("0") + " comp% " + l.Cmp.ToString("0.0")
                + " sacks " + l.Sacks.ToString("0.00") + " int " + l.Ints.ToString("0.00") + " fumLost " + l.FumLost.ToString("0.00");
        }

        [Test, Category("Slow")]
        public void CpuLeagueWithTheCatalogueAbilitiesStillProducesNflLikeTeamStatistics()
        {
            const int games = 320;
            League with = PlayLeague(true, games), without = PlayLeague(false, games);
            TestContext.WriteLine("CAL with abilities:    " + Line(with));
            TestContext.WriteLine("CAL without abilities: " + Line(without));
            Assert.That(with.Pts, Is.InRange(17.0, 29.0), "points per team");
            Assert.That(with.PassYds, Is.InRange(185.0, 280.0), "gross passing yards per team");
            Assert.That(with.RushYds, Is.InRange(88.0, 150.0), "rushing yards per team");
            Assert.That(with.Cmp, Is.InRange(57.0, 71.0), "completion percentage");
            Assert.That(with.Sacks, Is.InRange(1.3, 3.8), "sacks per team");
            Assert.That(with.Ints, Is.InRange(0.45, 1.4), "interceptions per team");
            Assert.That(with.FumLost, Is.InRange(0.2, 1.0), "fumbles lost per team");
            Assert.That(with.Plays, Is.InRange(56.0, 70.0), "plays per team");
            // the same games on the same seeds: the abilities move the league by a few percent, not by a regime
            Assert.That(Math.Abs(with.Pts - without.Pts), Is.LessThan(2.2), "points per team against the same league without abilities");
            Assert.That(Math.Abs(with.PassYds - without.PassYds), Is.LessThan(20.0), "passing yards against the control");
            Assert.That(Math.Abs(with.RushYds - without.RushYds), Is.LessThan(14.0), "rushing yards against the control");
            Assert.That(Math.Abs(with.Plays - without.Plays), Is.LessThan(2.5), "plays against the control");
        }
    }
}
