using UnityEngine;

namespace MiniFootball
{
    public enum FootballAction { Pass, Shoot, Tackle, Jump, SaveLeft, SaveRight, Celebrate, ThrowIn }

    // The generated clip uses these exact points for its wind-up, contact and follow-through.
    public static class MotionTiming
    {
        public static float Duration(FootballAction action) => action switch
        {
            FootballAction.Pass => .46f,
            FootballAction.Shoot => .65f,
            FootballAction.Tackle => .59f,
            FootballAction.Jump => .74f,
            FootballAction.SaveLeft or FootballAction.SaveRight => .93f,
            FootballAction.Celebrate => 1.48f,
            FootballAction.ThrowIn => .78f,
            _ => .5f
        };

        public static float ContactTime(FootballAction action) => Duration(action) * ContactPhase(action);

        public static float ContactPhase(FootballAction action) => action switch
        {
            FootballAction.Pass => .43f,
            FootballAction.Shoot => .47f,
            FootballAction.Tackle => .41f,
            FootballAction.Jump => .48f,
            FootballAction.SaveLeft or FootballAction.SaveRight => .46f,
            FootballAction.ThrowIn => .50f,
            _ => .50f
        };
    }

    [DisallowMultipleComponent, RequireComponent(typeof(Animator))]
    public sealed class FootballMotion : MonoBehaviour
    {
        static readonly int Speed = Animator.StringToHash("Speed");
        static readonly int Turn = Animator.StringToHash("Turn");
        static readonly int Carry = Animator.StringToHash("Carry");
        static readonly int Tempo = Animator.StringToHash("Tempo");
        static readonly int Locomotion = Animator.StringToHash("Base Layer.Locomotion");
        static readonly int[] Actions = {
            Animator.StringToHash("Base Layer.Pass"), Animator.StringToHash("Base Layer.Shoot"),
            Animator.StringToHash("Base Layer.Tackle"), Animator.StringToHash("Base Layer.Jump"),
            Animator.StringToHash("Base Layer.SaveLeft"), Animator.StringToHash("Base Layer.SaveRight"),
            Animator.StringToHash("Base Layer.Celebrate"), Animator.StringToHash("Base Layer.ThrowIn")
        };

        struct Plant
        {
            public bool Grounded;
            public Vector3 Anchor;
        }

        Animator rig;
        FootballAction action;
        bool playingAction, pendingContact, contactReached;
        float velocity, turn;
        Plant left, right;

        void Awake()
        {
            rig = GetComponent<Animator>();
            rig.applyRootMotion = false;
            rig.fireEvents = true;
            rig.updateMode = AnimatorUpdateMode.Fixed;
            rig.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        public void SetLocomotion(float metresPerSecond, float signedAngularSpeedDegreesPerSecond, bool dribbling)
        {
            velocity = Mathf.Max(0, metresPerSecond);
            turn = Mathf.Clamp(signedAngularSpeedDegreesPerSecond / 180f, -1, 1);
            float dt = Time.fixedDeltaTime;
            rig.SetFloat(Speed, velocity, .065f, dt);
            rig.SetFloat(Turn, turn, .075f, dt);
            rig.SetFloat(Carry, dribbling ? 1 : 0, .12f, dt);
            // The blend tree covers walking through sprinting. Scale cadence at the extremes
            // and for athlete stature, where blending between sampled speeds cannot compensate.
            float tempo = 1f;
            if (velocity > .2f && velocity < 1.55f) tempo = Mathf.Lerp(.72f, 1f, velocity / 1.55f);
            if (velocity > 6.6f) tempo = Mathf.Min(1.24f, velocity / 6.6f);
            rig.SetFloat(Tempo, tempo / Mathf.Clamp(transform.lossyScale.y, .87f, 1.15f));
        }

        public void Play(FootballAction nextAction)
        {
            action = nextAction;
            contactReached = false;
            playingAction = true;
            pendingContact = action != FootballAction.Celebrate;
            left = right = default;
            rig.CrossFadeInFixedTime(Actions[(int)action], .065f, 0, 0);
        }

        // Invoked by the action clip on its authored contact key.
        public void OnBallContact(int actionIndex)
        {
            if (pendingContact && actionIndex == (int)action) contactReached = true;
        }

        public bool ConsumeContact()
        {
            if (!pendingContact || !contactReached) return false;
            pendingContact = false;
            return true;
        }

        public void ResetPose()
        {
            playingAction = pendingContact = contactReached = false;
            left = right = default;
            velocity = turn = 0;
            rig.SetFloat(Speed, 0);
            rig.SetFloat(Turn, 0);
            rig.SetFloat(Carry, 0);
            rig.SetFloat(Tempo, 1);
            rig.Play(Locomotion, 0, 0);
        }

        void OnAnimatorIK(int layer)
        {
            if (layer != 0 || rig == null || !rig.isHuman) return;
            rig.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0);
            rig.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0);
            AnimatorStateInfo state = rig.GetCurrentAnimatorStateInfo(0);
            if (playingAction)
            {
                int target = Actions[(int)action];
                AnimatorStateInfo gesture = state;
                if (rig.IsInTransition(0))
                {
                    var next = rig.GetNextAnimatorStateInfo(0);
                    if (next.fullPathHash == target) gesture = next;
                }
                if (gesture.fullPathHash == target && gesture.normalizedTime < 1)
                {
                    // Body offsets are visual and relative to the game-controlled feet/root.
                    float envelope = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(gesture.normalizedTime)), 2);
                    float size = rig.humanScale;
                    if (action == FootballAction.Jump || action == FootballAction.Celebrate)
                        rig.bodyPosition += Vector3.up * (size * envelope * (action == FootballAction.Jump ? .38f : .20f));
                    else if (action == FootballAction.SaveLeft || action == FootballAction.SaveRight)
                    {
                        float side = action == FootballAction.SaveLeft ? -1 : 1;
                        rig.bodyPosition += (transform.right * (side * .58f) - Vector3.up * .25f) * (size * envelope);
                        rig.bodyRotation = Quaternion.AngleAxis(-side * 48 * envelope, transform.forward) * rig.bodyRotation;
                    }
                    return;
                }
                // CrossFade may still be queued on the first fixed frame after Play().
                if (pendingContact && !contactReached) return;
                playingAction = false;
            }
            if (state.fullPathHash != Locomotion || velocity < .35f || Mathf.Abs(turn) > .88f)
            {
                left = right = default;
                return;
            }

            float cycle = Mathf.Repeat(state.normalizedTime, 1);
            FootPlant(AvatarIKGoal.LeftFoot, cycle, ref left);
            FootPlant(AvatarIKGoal.RightFoot, Mathf.Repeat(cycle + .5f, 1), ref right);
        }

        void FootPlant(AvatarIKGoal goal, float phase, ref Plant foot)
        {
            // The locomotion clips begin at heel strike. Anchor only the load-bearing
            // part of stance. Release before toe-off and when the root changes abruptly.
            const float lift = .37f;
            if (phase > lift)
            {
                foot.Grounded = false;
                return;
            }
            bool leftFoot = goal == AvatarIKGoal.LeftFoot;
            Transform bone = rig.GetBoneTransform(leftFoot ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            Vector3 ankle = bone.position;
            if (!foot.Grounded)
            {
                foot.Anchor = ankle;
                foot.Grounded = true;
            }
            Vector3 offset = foot.Anchor - ankle;
            float sole = leftFoot ? rig.leftFeetBottomHeight : rig.rightFeetBottomHeight;
            offset.y = Mathf.Clamp(transform.position.y + sole - ankle.y, -.09f, .09f);
            float fade = Mathf.Min(Mathf.Clamp01(phase / .075f), Mathf.Clamp01((lift - phase) / .075f));
            float reach = .24f * rig.humanScale;
            if (offset.magnitude > reach) foot.Anchor = ankle + Vector3.ClampMagnitude(offset, reach);
            rig.SetIKPositionWeight(goal, .83f * fade);
            rig.SetIKPosition(goal, ankle + Vector3.ClampMagnitude(offset, reach));
        }
    }
}
