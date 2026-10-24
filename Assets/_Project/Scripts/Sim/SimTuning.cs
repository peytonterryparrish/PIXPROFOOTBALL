namespace PixFootball.Sim
{
    /// <summary>
    /// Calibration knobs (v0.10.0). The shipped values are the defaults below; the headless lab overrides them to explore (never at runtime in the game).
    /// </summary>
    internal static class SimTuning
    {
        /// <summary>Added to the openness a quarterback wants before he throws (start of the progression). 0 = the v0.9.0 thresholds.</summary>
        public static float QbPatience = 0f;
        public static float QbMinThreshold = 0.45f;
        /// <summary>
        /// Extra seconds before the passer feels a rusher (a blind-side edge, tunnel vision on the read): a real quarterback is sacked by a rusher he did not see, not by one he saw and could not beat.
        /// With ScanPressure it takes the sack rate from 1.7 to 6-7 percent of dropbacks (NFL 6.5) without more throwaways (3.5 percent) or scrambles (4 percent).
        /// </summary>
        public static float SenseDelay = 0.30f;
        /// <summary>Pocket pressure at which the passer stops working his progression one read at a time and scans for the checkdown / the throwaway (it was 0.30: he dumped the ball as soon as a rusher was 0.7 s away).</summary>
        public static float ScanPressure = 0.60f;
        /// <summary>Openness a window needs for a passer with nothing better to take it at the end of the hold (plus 0.08 x awareness); under a hit he throws at anything not dead covered.</summary>
        public static float TakeMin = 0.16f;
        /// <summary>Openness a deep (17+ yd) target needs for the passer to take the shot at the end of the hold.</summary>
        public static float ShotMin = 0.0f;
        public static float ShotDepth = 11f;
        /// <summary>SenseDelay multipliers by drop (quick, five-step, seven-step): the deeper the drop the longer the passer has stood in the pocket seeing the rush.</summary>
        public static float SenseQuick = 1.15f, SenseFive = 0.8f, SenseSeven = 0.4f;
        /// <summary>Seconds a quarterback with nobody open sits on the ball past the normal hold (0.20 for a sharp passer, 0.75 for a green one: Base + 0.55 x (1 - awareness)).</summary>
        public static float CoverHold = 0.20f;
        /// <summary>Added to the pass-protection leverage drift (negative = the rush wins more). 0 = v0.9.0.</summary>
        /// <summary>v0.11.0 SIM-FLOW: how much of a rating gap between a blocker and his man reaches the lock-up (1 = v0.10). At 1 a 15-point gap is a pancake and a 6-point better team won by 35 (Miami 46, Buffalo 10; 9.4 yards a carry); the NFL margin is about 12 for typical gaps.</summary>
        public static float TalentGapScale = 1f;
        /// <summary>v0.11.0 SIM-FLOW: how much of the tackler-versus-carrier skill gap reaches the tackle contest (1 = v0.10). At 1 an elite back broke every tackle (Miami 11.7 yards a carry, three teams over 8, the league range 3.2 to 11.7) and a weak team could not score (Buffalo 10 points a game).</summary>
        public static float TackleGapScale = 0.55f;
        /// <summary>The contest offset that goes with <see cref="TackleGapScale"/> so the league average stays where the calibration has it.</summary>
        public static float TackleGapOffset = 0f;
        public static float PassBias = 0.06f;      // v0.11.0 SIM-FLOW (B-036): an even OL/DL pair no longer sheds after the 0.9 s commit window every time (sheds per pass play 1.79 -> 1.56, sacks per team 2.9 -> 2.5 against 2.3 in the NFL)
        /// <summary>Tackle-contest points in the passer's favour when a rusher gets hold of him before the release.</summary>
        public static float PasserBonus = 7f;
        /// <summary>Yards of open lane (speed-scaled) the quarterback needs outside the crashed end to keep the ball on a read option.</summary>
        public static float OptionKeepOpen = 7.5f;
        /// <summary>Earliest hand-off of an outside zone (seconds after the snap), shotgun and under centre.</summary>
        public static float OutsideZoneHandoffSg = 0.78f, OutsideZoneHandoffUc = 0.70f;
        /// <summary>Extra seconds a drawn-in linebacker, safety or spy needs to see the hand-off and turn back downhill.</summary>
        public static float DrawHoldExtra = 0.18f;
    }
}
