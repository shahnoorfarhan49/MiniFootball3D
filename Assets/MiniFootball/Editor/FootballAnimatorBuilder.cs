using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MiniFootball.Editor
{
    public static class FootballAnimatorBuilder
    {
        // Gaits are keyed by measured ground speed. Each is one heel-strike-to-heel-strike cycle.
        static readonly string[] Gaits = { "Walk", "Jog", "Run", "Sprint" };
        static readonly float[] Speeds = { 1.55f, 3.1f, 4.9f, 6.6f };
        static readonly float[] Periods = { 1.04f, .82f, .68f, .60f };
        static readonly HashSet<string> MuscleNames = new HashSet<string>(HumanTrait.MuscleName);

        public static void Install(GameObject character, string generatedFolder)
        {
            var animator = character.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("Football animation requires the imported footballer's valid humanoid Avatar.");

            // Humanoid clips store the avatar centre of mass in scale-normalized space.
            // Read it from this rig so newly authored clips keep its feet at pitch height.
            HumanPose reference = default;
            using (var poseHandler = new HumanPoseHandler(animator.avatar, animator.transform))
                poseHandler.GetHumanPose(ref reference);
            Vector3 bodyOrigin = Quaternion.Inverse(animator.transform.rotation) *
                (reference.bodyPosition - animator.transform.position / animator.humanScale);
            Quaternion bodyRotation = Quaternion.Inverse(animator.transform.rotation) * reference.bodyRotation;

            string directory = generatedFolder + "/Animations";
            if (!AssetDatabase.IsValidFolder(directory)) AssetDatabase.CreateFolder(generatedFolder, "Animations");
            string path = directory + "/FootballMotion.controller";
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            // Only this generated controller graph is owned by the builder. Clips are
            // separate assets and all externally referenced asset GUIDs survive a rebuild.
            controller.layers = Array.Empty<AnimatorControllerLayer>();
            foreach (UnityEngine.Object child in AssetDatabase.LoadAllAssetsAtPath(path))
                if (child != null && child != controller && AssetDatabase.IsSubAsset(child))
                    UnityEngine.Object.DestroyImmediate(child, true);
            controller.parameters = Array.Empty<AnimatorControllerParameter>();
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Carry", AnimatorControllerParameterType.Float);
            controller.AddParameter("Turn", AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter { name = "Tempo", type = AnimatorControllerParameterType.Float, defaultFloat = 1 });
            controller.AddLayer("Base Layer");
            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].defaultWeight = 1;
            layers[0].iKPass = true;
            controller.layers = layers;
            AnimatorStateMachine graph = layers[0].stateMachine;

            var idle = LocomotionClip(directory, bodyOrigin, bodyRotation, "Ready", 1.8f, -1, false, 0);
            BlendTree turns = NewTree(controller, "Pivot steps", "Turn");
            turns.AddChild(LocomotionClip(directory, bodyOrigin, bodyRotation, "PivotLeft", .67f, -1, false, -1), -1);
            turns.AddChild(idle, 0);
            turns.AddChild(LocomotionClip(directory, bodyOrigin, bodyRotation, "PivotRight", .67f, -1, false, 1), 1);
            BlendTree carry = NewTree(controller, "Possession", "Carry");
            for (int possession = 0; possession <= 1; possession++)
            {
                bool withBall = possession == 1;
                BlendTree pace = NewTree(controller, withBall ? "On ball" : "Off ball", "Speed");
                pace.AddChild(turns, 0);
                for (int gait = 0; gait < Gaits.Length; gait++)
                {
                    string name = withBall ? "Dribble" + Gaits[gait] : Gaits[gait];
                    pace.AddChild(LocomotionClip(directory, bodyOrigin, bodyRotation, name, Periods[gait], gait, withBall, 0), Speeds[gait]);
                }
                carry.AddChild(pace, possession);
            }

            AnimatorState locomotion = graph.AddState("Locomotion", new Vector3(220, 30));
            locomotion.motion = carry;
            locomotion.speedParameter = "Tempo";
            locomotion.speedParameterActive = true;
            graph.defaultState = locomotion;
            foreach (FootballAction action in Enum.GetValues(typeof(FootballAction)))
            {
                var state = graph.AddState(action.ToString(), new Vector3(500 + (int)action / 4 * 210, 30 + (int)action % 4 * 75));
                state.motion = ActionClip(directory, bodyOrigin, bodyRotation, action);
                var back = state.AddTransition(locomotion);
                back.hasExitTime = true;
                back.exitTime = .86f;
                back.hasFixedDuration = true;
                back.duration = .09f;
            }

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Fixed;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (character.GetComponent<FootballMotion>() == null) character.AddComponent<FootballMotion>();
            EditorUtility.SetDirty(controller);
        }

        static BlendTree NewTree(AnimatorController controller, string label, string parameter)
        {
            var tree = new BlendTree { name = label, blendType = BlendTreeType.Simple1D, blendParameter = parameter,
                useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, controller);
            return tree;
        }

        // A muscle pose is independent of the model's bind axes. Positive knee/elbow
        // stretch approaches extension. Numerical muscle channels use Unity's [-1,1] range.
        sealed class Pose : Dictionary<string, float>
        {
            public void Leg(string side, float swing, float extension, float ankle, float spread = .04f)
            {
                this[side + " Upper Leg Front-Back"] = swing;
                this[side + " Upper Leg In-Out"] = spread;
                this[side + " Lower Leg Stretch"] = extension;
                this[side + " Foot Up-Down"] = ankle;
            }

            public void Arm(string side, float elevation, float swing, float elbow, float twist = 0)
            {
                this[side + " Arm Down-Up"] = elevation;
                this[side + " Arm Front-Back"] = swing;
                this[side + " Arm Twist In-Out"] = twist;
                this[side + " Forearm Stretch"] = elbow;
            }

            public void Torso(float lean, float tilt, float twist, float gaze = 0)
            {
                this["Spine Front-Back"] = lean;
                this["Chest Front-Back"] = lean * .48f;
                this["Spine Left-Right"] = tilt;
                this["Chest Twist Left-Right"] = twist;
                this["Neck Nod Down-Up"] = gaze;
                this["Head Turn Left-Right"] = twist * -.27f;
            }
        }

        static Pose Ready()
        {
            var pose = new Pose();
            pose.Leg("Left", .09f, .79f, -.03f);
            pose.Leg("Right", .09f, .79f, -.03f);
            pose.Arm("Left", -.77f, .10f, .54f);
            pose.Arm("Right", -.77f, .10f, .54f);
            pose.Torso(.055f, 0, 0);
            return pose;
        }

        static AnimationClip LocomotionClip(string directory, Vector3 origin, Quaternion rotation, string name, float duration, int gait, bool carrying, int turning)
        {
            var curves = new Dictionary<string, AnimationCurve>();
            const int samples = 16;
            for (int frame = 0; frame <= samples; frame++)
            {
                float phase = frame / (float)samples;
                float cycle = phase * Mathf.PI * 2;
                var pose = Ready();
                if (gait < 0)
                {
                    pose.Torso(.055f + .018f * Mathf.Sin(cycle), turning * .12f, turning * .22f + .014f * Mathf.Sin(cycle));
                    if (turning != 0)
                    {
                        float step = Mathf.Cos(cycle);
                        pose.Leg("Left", .1f + step * .12f, .76f - Mathf.Max(0, step) * .37f, -.02f, .08f + turning * step * .09f);
                        pose.Leg("Right", .1f - step * .12f, .76f - Mathf.Max(0, -step) * .37f, -.02f, .08f - turning * step * .09f);
                    }
                }
                else
                {
                    float intensity = gait / 3f;
                    Stride(pose, "Left", phase, intensity, carrying);
                    Stride(pose, "Right", Mathf.Repeat(phase + .5f, 1), intensity, carrying);
                    pose.Torso(.06f + intensity * .15f + (carrying ? .04f : 0), .055f * Mathf.Sin(cycle),
                        (.10f + intensity * .08f) * Mathf.Cos(cycle), carrying ? -.08f : 0);
                }
                float rise = gait < 0 ? .002f * Mathf.Sin(cycle)
                    : (.019f + gait * .006f) * (1f - Mathf.Cos(cycle * 2)) * .5f;
                AddBodySample(curves, origin + Vector3.up * rise, rotation, phase * duration);
                AddSample(curves, pose, phase * duration);
            }
            return SaveClip(directory, name, curves, true);
        }

        static void Stride(Pose pose, string side, float phase, float intensity, bool carrying)
        {
            float angle = phase * Mathf.PI * 2;
            float reach = Mathf.Cos(angle);
            float swing = Mathf.Clamp01(-Mathf.Sin(angle));
            float loading = Mathf.Max(0, Mathf.Sin(angle * 2)) * (phase < .25f ? 1 : 0);
            float amplitude = Mathf.Lerp(.29f, .63f, intensity) * (carrying ? .81f : 1);
            float touch = carrying && side == "Right" ? .09f * Mathf.Pow(Mathf.Max(0, reach), 4) : 0;
            pose.Leg(side, .11f + reach * amplitude + touch,
                .87f - swing * (.57f + .62f * intensity) - loading * .18f,
                .13f * reach - .12f * Mathf.Sin(angle), carrying ? .06f : .025f);
            pose.Arm(side, -.78f + intensity * .08f, .06f - reach * (.18f + .37f * intensity),
                .64f - intensity * .67f + reach * .11f, -.09f * intensity);
        }

        static AnimationClip ActionClip(string directory, Vector3 origin, Quaternion rotation, FootballAction action)
        {
            Pose wind = Ready(), hit = Ready(), follow = Ready();
            switch (action)
            {
                case FootballAction.Pass:
                case FootballAction.Shoot:
                    bool drive = action == FootballAction.Shoot;
                    wind.Leg("Right", drive ? -.56f : -.28f, drive ? -.53f : .04f, -.28f, .12f);
                    wind.Leg("Left", .17f, .64f, .03f);
                    wind.Arm("Left", -.30f, .33f, .44f);
                    wind.Torso(.09f, -.12f, -.31f);
                    hit.Leg("Right", drive ? .65f : .46f, .95f, drive ? -.32f : .08f, .09f);
                    hit.Leg("Left", -.12f, .66f, .05f);
                    hit.Torso(.16f, -.11f, .24f);
                    hit.Arm("Left", -.36f, -.17f, .40f);
                    follow.Leg("Right", drive ? .81f : .51f, .47f, -.18f);
                    follow.Torso(.18f, -.03f, .30f);
                    break;
                case FootballAction.Tackle:
                    wind.Leg("Left", .18f, .24f, -.09f);
                    wind.Torso(.24f, -.06f, -.18f);
                    hit.Leg("Right", .74f, .94f, .16f, .13f);
                    hit.Leg("Left", -.22f, .29f, -.18f);
                    hit.Arm("Left", -.28f, -.22f, .49f);
                    hit.Torso(.31f, -.18f, .27f);
                    follow.Leg("Right", .44f, .67f, .02f);
                    follow.Torso(.21f, -.12f, .17f);
                    break;
                case FootballAction.Jump:
                case FootballAction.Celebrate:
                    bool cheering = action == FootballAction.Celebrate;
                    foreach (string side in new[] { "Left", "Right" })
                    {
                        wind.Leg(side, .28f, .11f, -.14f);
                        wind.Arm(side, -.62f, -.39f, .66f);
                        hit.Arm(side, cheering ? .88f : .16f, .38f, cheering ? .65f : -.12f);
                        hit.Leg(side, side == "Left" ? .38f : .15f, side == "Left" ? -.13f : .24f, -.24f);
                        follow.Leg(side, .22f, .22f, -.11f);
                        if (cheering) follow.Arm(side, .61f, .19f, .12f);
                    }
                    wind.Torso(.22f, 0, 0);
                    hit.Torso(-.16f, .04f, .08f, .15f);
                    break;
                case FootballAction.ThrowIn:
                    foreach (string side in new[] { "Left", "Right" })
                    {
                        wind.Arm(side, .69f, .04f, -.64f, -.18f);
                        hit.Arm(side, .37f, .88f, .92f, -.12f);
                        follow.Arm(side, -.22f, .68f, .70f);
                    }
                    wind.Torso(-.24f, 0, 0, .14f);
                    hit.Torso(.24f, 0, 0);
                    follow.Torso(.28f, 0, 0);
                    break;
                case FootballAction.SaveLeft:
                case FootballAction.SaveRight:
                    bool toLeft = action == FootballAction.SaveLeft;
                    string reach = toLeft ? "Left" : "Right", trail = toLeft ? "Right" : "Left";
                    float sideSign = toLeft ? -1 : 1;
                    wind.Leg(reach, .28f, .10f, -.10f, .17f);
                    wind.Leg(trail, .14f, .44f, .08f, .13f);
                    wind.Arm(reach, -.39f, .34f, .04f);
                    wind.Arm(trail, -.36f, .37f, -.08f);
                    hit.Arm(reach, .68f, .22f, .98f);
                    hit.Arm(trail, .68f, .66f, .84f, -.24f);
                    hit.Leg(trail, -.36f, .55f, -.21f, .13f);
                    hit.Leg(reach, .22f, -.14f, .03f, .11f);
                    hit.Torso(.025f, sideSign * .34f, -sideSign * .24f);
                    follow.Leg(reach, .38f, .25f, -.16f);
                    follow.Arm(reach, -.02f, .58f, .30f);
                    follow.Torso(.27f, sideSign * .23f, -.19f * sideSign);
                    break;
            }
            float t = MotionTiming.Duration(action), contact = MotionTiming.ContactPhase(action);
            var curves = new Dictionary<string, AnimationCurve>();
            AddBodySample(curves, origin, rotation, 0);
            AddBodySample(curves, origin, rotation, t);
            AddSample(curves, Ready(), 0);
            AddSample(curves, wind, t * contact * .53f);
            AddSample(curves, hit, t * contact);
            AddSample(curves, follow, t * Mathf.Lerp(contact, 1, .47f));
            AddSample(curves, Ready(), t);
            AnimationClip clip = SaveClip(directory, action.ToString(), curves, false);
            // Animation events arrive after pose evaluation. The next physics tick consumes
            // the recorded contact once, even if several ticks are needed in one display frame.
            if (action != FootballAction.Celebrate)
            {
                AnimationUtility.SetAnimationEvents(clip, new[] { new AnimationEvent {
                    functionName = nameof(FootballMotion.OnBallContact),
                    intParameter = (int)action,
                    time = t * contact
                } });
            }
            return clip;
        }

        static void AddBodySample(Dictionary<string, AnimationCurve> curves, Vector3 position, Quaternion rotation, float time)
        {
            AddKey(curves, "RootT.x", time, position.x);
            AddKey(curves, "RootT.y", time, position.y);
            AddKey(curves, "RootT.z", time, position.z);
            AddKey(curves, "RootQ.x", time, rotation.x);
            AddKey(curves, "RootQ.y", time, rotation.y);
            AddKey(curves, "RootQ.z", time, rotation.z);
            AddKey(curves, "RootQ.w", time, rotation.w);
        }

        static void AddKey(Dictionary<string, AnimationCurve> curves, string channel, float time, float value)
        {
            if (!curves.TryGetValue(channel, out AnimationCurve curve))
                curves[channel] = curve = new AnimationCurve();
            curve.AddKey(time, value);
        }

        static void AddSample(Dictionary<string, AnimationCurve> curves, Pose pose, float time)
        {
            foreach (var channel in pose)
            {
                if (!MuscleNames.Contains(channel.Key))
                    throw new InvalidOperationException("Unknown humanoid muscle channel " + channel.Key);
                AddKey(curves, channel.Key, time, Mathf.Clamp(channel.Value, -1f, 1f));
            }
        }

        static AnimationClip SaveClip(string directory, string name, Dictionary<string, AnimationCurve> curves, bool loop)
        {
            string path = directory + "/Humanoid" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = name, frameRate = 60 };
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            var bindings = new List<EditorCurveBinding>(curves.Count);
            var values = new List<AnimationCurve>(curves.Count);
            foreach (var channel in curves)
            {
                AnimationCurve curve = channel.Value;
                for (int k = 0; k < curve.length; k++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto);
                    AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto);
                }
                bindings.Add(EditorCurveBinding.FloatCurve("", typeof(Animator), channel.Key));
                values.Add(curve);
            }
            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), values.ToArray());
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            settings.loopBlendOrientation = true;
            settings.loopBlendPositionY = true;
            settings.loopBlendPositionXZ = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalPositionXZ = true;
            settings.keepOriginalOrientation = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }
    }
}
