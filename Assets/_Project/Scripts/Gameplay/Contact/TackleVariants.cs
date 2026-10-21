using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>What kind of authored pair a contact needs.</summary>
    public enum ContactKind
    {
        /// <summary>Standing wrap-up: the carrier goes down, the tackler on top / beside him.</summary>
        Wrap,
        /// <summary>Low tackle at the legs (dive tackle, shoestring that brings him down).</summary>
        Dive,
        /// <summary>The tackler is beaten: he whiffs / falls while the carrier stumbles on.</summary>
        Broken,
        /// <summary>A big hit: both bodies take the collision (BoNES big-hit takes).</summary>
        BigHit,
        /// <summary>Quarterback taken down by a rusher.</summary>
        Sack,
        /// <summary>Quarterback escapes the rusher.</summary>
        SackBroken,
        /// <summary>Truck: the carrier runs through the defender.</summary>
        Truck,
        /// <summary>Plain shoulder to shoulder collision (blocker / hit reaction).</summary>
        Collision,
        /// <summary>A catch in traffic: receiver and defender both go up for the ball (A = receiver, B = defender).</summary>
        Contested
    }

    /// <summary>What the simulation says about one contact, in the units the table uses (metres, degrees).</summary>
    public struct ContactSituation
    {
        public ContactKind Kind;
        /// <summary>Where the tackler is seen from the carrier (0 = in front of him, +/-180 = behind, + = to the carrier's right).</summary>
        public float Bearing;
        /// <summary>Carrier ground speed, m/s.</summary>
        public float CarrierSpeed;
        /// <summary>Closing speed of the tackler on the carrier, m/s.</summary>
        public float Closing;
        public int Seed;
    }

    /// <summary>Picks the authored pair (a Draft Fever two-actor take) that best matches a simulated contact. Pure; unit tested.</summary>
    public static class TackleVariants
    {
        /// <summary>Contact kind for a simulation tackle outcome.</summary>
        public static ContactKind KindFor(TackleType type, bool sack)
        {
            if (sack) return ContactKind.Sack;
            switch (type)
            {
                case TackleType.BigHit: return ContactKind.BigHit;
                case TackleType.Shoestring: case TackleType.DiveTackle: return ContactKind.Dive;
                case TackleType.BrokenTackle: return ContactKind.Broken;
                default: return ContactKind.Wrap;   // WrapUp, GangTackle, Drag, PushOutOfBounds
            }
        }

        public static bool Has(string name, string token) { return name != null && name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0; }

        /// <summary>Does the take belong to the kind?</summary>
        public static bool Matches(ContactTable.Pair p, ContactKind kind)
        {
            string n = p.clipA ?? "";
            switch (kind)
            {
                case ContactKind.Wrap: return p.key == ArtKeys.ContactTackle && Has(n, "TackleWrap") && Has(n, "High");
                case ContactKind.Dive: return p.key == ArtKeys.ContactTackle && Has(n, "TackleWrap") && Has(n, "Low");
                case ContactKind.Broken: return p.key == ArtKeys.ContactTackle && Has(n, "TackleBroken") && Has(n, "Dmo");
                case ContactKind.Truck: return p.key == ArtKeys.ContactTackle && Has(n, "TackleBroken") && Has(n, "Omo");
                case ContactKind.BigHit: return p.key == ArtKeys.ContactBigHit && Has(n, "Success");
                case ContactKind.Sack: return p.key == ArtKeys.ContactSack && Has(n, "SackWrap");
                case ContactKind.SackBroken: return p.key == ArtKeys.ContactSack && Has(n, "SackBroken");
                case ContactKind.Collision: return p.key == ArtKeys.ContactCollision;
                case ContactKind.Contested: return p.key == ArtKeys.CatchContested && Has(n, "Success");
            }
            return false;
        }

        /// <summary>FNV-1a of a clip name: the same take is picked for the same seed on every run (replays rely on it).</summary>
        public static int StableHash(string s)
        {
            unchecked { uint h = 2166136261u; for (int i = 0; i < s.Length; i++) h = (h ^ s[i]) * 16777619u; return (int)h; }
        }

        /// <summary>Lower is worse; the best variant has the highest score.</summary>
        public static float Score(ContactTable.Pair p, ContactSituation s)
        {
            float ang = Mathf.Abs(Mathf.DeltaAngle(p.bearing, s.Bearing));
            float score = -ang / 25f;
            score -= Mathf.Abs(p.speedA - s.CarrierSpeed) * 0.18f;
            score -= Mathf.Abs(p.closingB - s.Closing) * 0.12f;
            // variety among near-equal takes, stable per seed
            int h = (s.Seed * 73856093) ^ (StableHash(p.clipA) * 19349663);
            score += ((h & 0xFFFF) / 65535f) * 0.45f;
            return score;
        }

        /// <summary>Best matching pair; null when the table has nothing of that kind (the caller falls back to the in-place clips).</summary>
        public static ContactTable.Pair Choose(IList<ContactTable.Pair> pairs, ContactSituation s)
        {
            ContactTable.Pair best = null; float bs = float.MinValue;
            for (int i = 0; i < pairs.Count; i++)
            {
                var p = pairs[i];
                if (p == null || p.CountA < 2 || p.CountB < 2 || !Matches(p, s.Kind)) continue;
                float sc = Score(p, s);
                if (sc > bs) { bs = sc; best = p; }
            }
            return best;
        }
    }
}
