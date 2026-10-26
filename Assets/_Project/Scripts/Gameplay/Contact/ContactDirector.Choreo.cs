using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 CHOREO: the contact director for takes the simulation's choreographer booked. The simulation has already chosen the pair, placed it on the field and moves both bodies along the
    /// take's root motion; the director only starts the same clips at the same clip time, draws the bodies where the simulation has them (no warp-in, no correction back, no catch-up walk) and lets
    /// each actor go when the booking hands him back.
    /// </summary>
    public sealed partial class ContactDirector
    {
        // ---- booked single takes: a late tackler who piles on (the simulation moves him along the take)
        readonly System.Collections.Generic.Dictionary<int, ChoreoBooking> _bookedSolo = new System.Collections.Generic.Dictionary<int, ChoreoBooking>();

        void ResetBooked()
        {
            foreach (var kv in _bookedSolo) { var v = View(kv.Key); if (v != null) { v.ContactDriven = false; if (v.Rig != null) v.Rig.SetScrub(false); } }
            _bookedSolo.Clear();
        }

        void OnPileOnBooked(in PlayEvent e)
        {
            var sim = _stage.Sim; int joiner = e.AgentA, carrier = e.AgentB;
            if (sim.Choreo == null || SimAgentView.DebugNoChoreo || _table == null || InSession(joiner) || _bookedSolo.ContainsKey(joiner)) return;
            ChoreoBooking bk = null;
            var books = sim.Choreo.Bookings;
            for (int i = books.Count - 1; i >= 0; i--) { var b = books[i]; if (b.Joiner && b.B == joiner && b.A == carrier && !b.ViewStarted) { bk = b; break; } }
            if (bk == null) return;
            var v = View(joiner); var lib = ArtLibrary.Load();
            if (v == null || v.Rig == null || !v.Rig.Valid || lib == null) return;
            var clip = lib.ClipByName(bk.ClipB.Name); if (clip == null) return;
            float now = bk.ClipTime(sim.Choreo.Clock + sim.StepAlpha * PlaySimulation.Dt);
            bk.ViewStarted = true; v.ContactDriven = true; _bookedSolo[joiner] = bk;
            v.Rig.Play(clip, Mathf.Clamp(now, 0f, clip.length - 0.1f), Mathf.Max(Mathf.Min(clip.length, bk.ExitTB + 0.35f), now + 0.2f), bk.Rate, 0.14f, 0.35f, false, false, false, 0f, AnimKit.ImpactFloor);
        }

        void TickBooked(float dt)
        {
            if (_bookedSolo.Count == 0) return;
            System.Collections.Generic.List<int> done = null;
            foreach (var kv in _bookedSolo)
            {
                var v = View(kv.Key); var bk = kv.Value;
                if (v == null) { (done ?? (done = new System.Collections.Generic.List<int>())).Add(kv.Key); continue; }
                if (!bk.OwnsB) { v.ContactDriven = false; if (v.Rig != null) v.Rig.Stop(0.25f); (done ?? (done = new System.Collections.Generic.List<int>())).Add(kv.Key); continue; }
                v.SetContactPose(v.transform.position, BookedYaw(bk));
            }
            if (done != null) foreach (int id in done) _bookedSolo.Remove(id);
        }

        static ContactKind KindOf(PairKind k)
        {
            switch (k)
            {
                case PairKind.Wrap: return ContactKind.Wrap;
                case PairKind.Dive: return ContactKind.Dive;
                case PairKind.Broken: return ContactKind.Broken;
                case PairKind.Truck: return ContactKind.Truck;
                case PairKind.BigHit: return ContactKind.BigHit;
                case PairKind.BigHitShrug: return ContactKind.BigHitShrug;
                case PairKind.Sack: return ContactKind.Sack;
                case PairKind.SackBroken: return ContactKind.SackBroken;
                case PairKind.Collision: return ContactKind.Collision;
                default: return ContactKind.Contested;
            }
        }

        /// <summary>The view yaw (degrees, Unity frame) of a booking's placement: the rotation of the authored frame.</summary>
        float BookedYaw(ChoreoBooking bk) { return bk.Align.Yaw + (_stage.Frame.TowardsPlusZ ? 0f : 180f); }

        bool HasUnstartedBooking(int carrier, int tackler)
        {
            var sim = _stage.Sim; if (sim.Choreo == null || SimAgentView.DebugNoChoreo) return false;
            var books = sim.Choreo.Bookings;
            for (int i = books.Count - 1; i >= 0; i--) { var b = books[i]; if (b.Kind == ChoreoKind.Tackle && !b.Joiner && b.Phase == ChoreoPhase.Playing && !b.ViewStarted && b.A == carrier && b.B == tackler) return true; }
            return false;
        }

        bool TryStartBooked(int tackler, int carrier, in PlayEvent e)
        {
            var sim = _stage.Sim;
            if (sim.Choreo == null || SimAgentView.DebugNoChoreo || _table == null) return false;
            ChoreoBooking bk = null;
            var books = sim.Choreo.Bookings;
            for (int i = books.Count - 1; i >= 0; i--)
            {
                var b = books[i];
                if (b.Kind == ChoreoKind.Tackle && b.Phase == ChoreoPhase.Playing && !b.ViewStarted && b.A == carrier && b.B == tackler) { bk = b; break; }
            }
            if (bk == null || bk.Pair == null) return false;
            var vc = View(carrier); var vt = View(tackler);
            if (vc == null || vt == null || vc.Rig == null || vt.Rig == null || !vc.Rig.Valid || !vt.Rig.Valid) return false;
            var pair = _table.ByClipA(bk.Pair.A.Name);
            var lib = ArtLibrary.Load();
            if (pair == null || lib == null) return false;
            var clipA = lib.ClipByName(pair.clipA); var clipB = lib.ClipByName(pair.clipB);
            if (clipA == null || clipB == null) return false;
            ContactKind kind = KindOf(bk.PairKind);
            float now = bk.ClipTime(sim.Choreo.Clock + sim.StepAlpha * PlaySimulation.Dt);
            var s = ContactSession.CreateBooked(pair, kind, carrier, tackler, bk, now);
            s.ClipA = clipA; s.ClipB = clipB;
            _sessions.Add(s);
            bk.ViewStarted = true;
            vc.ContactDriven = true; vt.ContactDriven = true;
            float rate = bk.Rate;
            float endA = Mathf.Min(Mathf.Min(pair.lengthA, s.MaxPlay), bk.ExitTA + 0.35f), endB = Mathf.Min(Mathf.Min(pair.lengthB, s.MaxPlay), bk.ExitTB + 0.35f);
            vc.Rig.Play(clipA, s.Start, Mathf.Max(endA, s.Start + 0.2f), rate, 0.14f, 0.35f, false, false, false, 0f, AnimKit.ImpactFloor);
            vt.Rig.Play(clipB, s.Start, Mathf.Max(endB, s.Start + 0.2f), rate, 0.14f, 0.35f, false, false, false, 0f, AnimKit.ImpactFloor);
            _recent.Add(pair.clipA); if (_recent.Count > 40) _recent.RemoveAt(0);
            LastTake = "booked " + kind + " " + pair.clipA + " clip t=" + now.ToString("0.00");
            Vector3 pc = vc.transform.position;
            if (e.Tackle == TackleType.BigHit || kind == ContactKind.BigHit) Fire(pc, ImpactOf(e), true, carrier, tackler, false, s);
            else if (kind != ContactKind.Contested) Fire(pc, ImpactOf(e) * 0.7f, false, carrier, tackler, kind == ContactKind.Sack, s);
            return true;
        }
    }
}
