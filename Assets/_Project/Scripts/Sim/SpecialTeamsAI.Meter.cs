using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>What a kick meter (timing + aim) does to a kick: how much of the leg's power is delivered, how wide the error spread is and where the kick is aimed.</summary>
    internal struct KickMods
    {
        public float Power;      // multiplier on distance / launch speed
        public float Spread;     // multiplier on the random error
        public float AimYards;   // lateral aim offset for kickoffs and punts (yards, field frame)
        public float AimAngle;   // aim angle offset for field goals (radians, +X)
        public bool User;
    }

    internal sealed partial class SpecialTeamsAI
    {
        private bool _userKickPress;
        private ControlInput _userKickInput;
        private float _userReadyAt = -1f;

        /// <summary>Longest a user kicker may take before the kick is struck on a default (mediocre) meter reading.</summary>
        public const float UserKickTimeout = 2.6f;
        /// <summary>Kickoffs have no rush: the kicker may wait longer for the meter.</summary>
        public const float UserKickoffTimeout = 6f;

        private bool UserControlsKicker { get { return _kicker != null && _kicker.ControlledByUser; } }

        /// <summary>True while the user is the kicker and the ball is ready: the kick meter should be running and waiting for <see cref="ControlInput.KickPress"/>.</summary>
        public bool AwaitingUserKick
        {
            get
            {
                if (_kicked || _approachAt >= 0f || !UserControlsKicker || S.Phase != PlayPhase.Live || S.Kind == PlayKind.Scrimmage) return false;
                if (S.Kind == PlayKind.Kickoff) return S.Time >= 0.2f;
                return _ballInHands;
            }
        }

        public bool IsKicker(SimAgent a) { return _kicker != null && a == _kicker && S.Kind != PlayKind.Scrimmage; }

        public void UserKick(ControlInput input)
        {
            if (_kicked || !input.KickPress || _userKickPress || !AwaitingUserKick) return;   // (v0.7: a press only counts while the meter is live, and only the first one)
            _userKickPress = true;
            _userKickInput = input;
            UserTrigger();
        }

        /// <summary>The kick goes when the CPU is kicking, or once the user's press (or the timeout) has started the approach and the contact time has come.</summary>
        private bool KickGate(float earliest)
        {
            if (!UserControlsKicker) return true;
            UserTrigger();
            return _approachAt >= 0f;
        }

        private KickMods Mods()
        {
            var m = new KickMods { Power = 1f, Spread = 1f, AimYards = 0f, AimAngle = 0f, User = false };
            if (!UserControlsKicker) return m;
            m.User = true;
            ControlInput i = _userKickInput;
            float power = _userKickPress ? SimMath.Clamp01(i.KickPower) : 0.7f;
            float timing = _userKickPress ? SimMath.Clamp01(i.KickTiming) : 0.45f;
            float aim = _userKickPress ? SimMath.Clamp(i.KickAim, -1f, 1f) : 0f;
            m.Power = KickMeter.PowerMultiplier(power, timing);
            m.Spread = KickMeter.SpreadMultiplier(timing);
            m.AimYards = aim * KickMeter.AimYardsAtFullDeflection;
            m.AimAngle = aim * KickMeter.AimAngleAtFullDeflection;
            return m;
        }

        /// <summary>Lateral position (X) where a kick launched at this speed and elevation crosses the goal-post plane, with the current wind acting.</summary>
        private static float FlightPlaneX(Vec3 from, Vec3 aim, float theta, float speed)
        {
            Vec3 v = LaunchAtAngle(from, aim, theta, speed);
            Vec3 p = from;
            for (int i = 0; i < 1500; i++)
            {
                Vec3 pp = p;
                BallPhysics.Advance(ref p, ref v, BallPhysics.Dt);
                if (p.Y >= GoalPostY)
                {
                    float f = (GoalPostY - pp.Y) / Math.Max(1e-5f, p.Y - pp.Y);
                    return pp.X + (p.X - pp.X) * f;
                }
                if (p.Z < -3f) break;
            }
            return p.X;
        }
    }

    /// <summary>
    /// The kick meter model shared by the simulation and the HUD (2K-style "TIMING Perfect / AIM Centered" panel): the timing reading
    /// (1 = perfect) scales power and accuracy, the aim reading (-1..1) steers the kick, the power fill sets how much of the leg is used.
    /// </summary>
    public static class KickMeter
    {
        /// <summary>Lateral aim offset (yards at the kicker's target) at full left / right deflection of a kickoff or punt.</summary>
        public const float AimYardsAtFullDeflection = 10f;
        /// <summary>Aim angle (radians) of a field goal at full deflection: 0.06 rad is about 3.4 degrees, 2.8 yd wide at 47 yd.</summary>
        public const float AimAngleAtFullDeflection = 0.06f;

        /// <summary>Delivered fraction of the kicker's power for a power fill and a timing reading: a full, well-timed kick gives 1.0, a half-hearted slap about 0.6.</summary>
        public static float PowerMultiplier(float power01, float timing01)
        {
            float p = 0.62f + 0.38f * SimMath.Clamp01(power01);
            return p * (0.90f + 0.10f * SimMath.Clamp01(timing01));
        }

        /// <summary>Error spread multiplier: 1.0 for a perfect reading growing to about 4.1 for a shanked one.</summary>
        public static float SpreadMultiplier(float timing01)
        {
            float e = 1f - SimMath.Clamp01(timing01);
            return 1f + 3.1f * e * e;
        }

        /// <summary>Timing reading from a swinging meter: the distance of the stopped cursor (0..1 along the bar) from the sweet spot, 1 = dead on.</summary>
        public static float TimingFromCursor(float cursor01, float sweetSpot01 = 0.5f)
        {
            float d = Math.Abs(cursor01 - sweetSpot01) / 0.5f;
            return SimMath.Clamp01(1f - d);
        }

        /// <summary>Position (0..1) of a cursor that ping-pongs along the bar with the given period (seconds) at time t.</summary>
        public static float PingPong(float t, float period)
        {
            if (period <= 0.0001f) return 0f;
            float ph = (t / period) % 1f;
            if (ph < 0f) ph += 1f;
            return ph < 0.5f ? ph * 2f : 2f - ph * 2f;
        }

        /// <summary>Aim cursor (-1 left .. +1 right) swinging as a sine with the given period.</summary>
        public static float AimSwing(float t, float period)
        {
            if (period <= 0.0001f) return 0f;
            return (float)Math.Sin(t / period * 2.0 * Math.PI);
        }

        public static string TimingLabel(float timing01)
        {
            float t = SimMath.Clamp01(timing01);
            if (t >= 0.92f) return "Perfect";
            if (t >= 0.78f) return "Great";
            if (t >= 0.58f) return "Good";
            if (t >= 0.35f) return "Fair";
            return "Poor";
        }

        public static string AimLabel(float aim)
        {
            float a = Math.Abs(aim);
            if (a < 0.10f) return "Centered";
            string side = aim < 0f ? "Left" : "Right";
            if (a < 0.35f) return "Slightly " + side;
            if (a < 0.7f) return side;
            return "Way " + side;
        }
    }
}
