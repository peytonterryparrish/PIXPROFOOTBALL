using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 QB (playtest note 9): eyes on the ball. While a pass is in the air the receiver it is thrown to (and the defender covering him) turn their head to it, limited like a real neck (75 degrees of yaw, 35 of pitch from the body),
    /// at a human rate; the stage feeds the ball's position every frame (<see cref="SetEyesOnBall"/>). A world-space delta on the head bone after the animation and the pose layers, so it works over every locomotion take.
    /// </summary>
    public partial class SimAgentView
    {
        Vector3 _eyesBall; float _eyesW, _eyesWT; Vector3 _eyesDir; bool _eyesInit;
        /// <summary>QA: how far the head is turned to the ball (0..1).</summary>
        public float EyesOnBallWeight { get { return _eyesW; } }

        /// <summary>Stage input, every frame: the ball's world position and how much this player's head should be on it (0 = not tracking).</summary>
        public void SetEyesOnBall(Vector3 ball, float weight) { _eyesBall = ball; _eyesWT = weight; }

        void EyesLate(float dt)
        {
            _eyesW = Mathf.MoveTowards(_eyesW, _eyesWT, dt / (_eyesWT > _eyesW ? 0.16f : 0.30f));
            if (_eyesW < 0.01f) { _eyesInit = false; return; }
            if (Avatar == null || Avatar.Head == null || (Ragdoll != null && Ragdoll.Active) || ContactDriven) return;
            Transform head = Avatar.Head;
            Vector3 body = Forward; body.y = 0f; if (body.sqrMagnitude < 1e-4f) return; body.Normalize();
            Vector3 to = _eyesBall - head.position; Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude < 0.04f) return;
            float yaw = Mathf.Clamp(Vector3.SignedAngle(body, flat, Vector3.up), -75f, 75f);
            float pitch = Mathf.Clamp(Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg, -35f, 35f);
            Vector3 want = Quaternion.AngleAxis(yaw, Vector3.up) * body;
            want = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, want)) * want;
            // human pace: the gaze swings to the ball at no more than 600 degrees per second
            if (!_eyesInit) { _eyesDir = body; _eyesInit = true; }
            _eyesDir = Vector3.RotateTowards(_eyesDir, want, 600f * Mathf.Deg2Rad * dt, 0f);
            // the head is assumed to look along the body (running); the delta takes it to the gaze, split over the neck and the head
            Quaternion delta = Quaternion.FromToRotation(body, _eyesDir);
            Quaternion w = Quaternion.Slerp(Quaternion.identity, delta, _eyesW);
            Transform neck = Avatar.Animator != null ? Avatar.Animator.GetBoneTransform(HumanBodyBones.Neck) : null;
            if (neck != null && neck != head) { neck.rotation = Quaternion.Slerp(Quaternion.identity, w, 0.35f) * neck.rotation; head.rotation = Quaternion.Slerp(Quaternion.identity, w, 0.65f) * head.rotation; }
            else head.rotation = w * head.rotation;
        }
    }
}
