using System;
using UnityEngine;

namespace MiniFootball
{
    public static class FootballArt
    {
        public const float HalfWidth = 13f, HalfLength = 20f, GoalHalf = 3.2f, GoalHeight = 2.15f, BallRadius = .23f;
        public static readonly Color Ink = new Color32(18, 26, 43, 255);
        static readonly Color White = new Color32(231, 243, 227, 255);
        static readonly Color Grass = new Color32(45, 126, 76, 255);
        static readonly Color LightGrass = new Color32(52, 139, 83, 255);
        public delegate Material Paint(Color color);

        public static GameObject Part(string name, PrimitiveType shape, Transform parent, Vector3 position, Vector3 scale, Material material, bool solid = false)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            if (!solid)
            {
                Collider collider = part.GetComponent<Collider>();
                collider.enabled = false;
                if (Application.isPlaying) UnityEngine.Object.Destroy(collider);
                else UnityEngine.Object.DestroyImmediate(collider);
            }
            return part;
        }

        static Transform Pivot(string name, Transform parent, Vector3 position)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = position;
            return pivot;
        }

        public static GameObject Character(Paint paint)
        {
            Transform root = new GameObject("Footballer").transform;
            var skin = paint(new Color32(192, 133, 94, 255));
            var hair = paint(new Color32(38, 28, 35, 255));
            var dark = paint(Ink);
            var light = paint(White);
            Transform body = Pivot("Body", root, Vector3.zero);
            Part("Kit_Torso", PrimitiveType.Sphere, body, new Vector3(0, 1.15f, 0), new Vector3(.64f, .63f, .40f), light);
            Part("Trim_Shorts", PrimitiveType.Cube, body, new Vector3(0, .86f, 0), new Vector3(.47f, .26f, .34f), dark);
            Part("Trim_Collar", PrimitiveType.Cylinder, body, new Vector3(0, 1.42f, 0), new Vector3(.23f, .024f, .23f), dark);
            Part("Skin_Neck", PrimitiveType.Cylinder, body, new Vector3(0, 1.47f, 0), new Vector3(.18f, .07f, .18f), skin);
            Part("Skin_Head", PrimitiveType.Sphere, body, new Vector3(0, 1.68f, 0), new Vector3(.43f, .48f, .39f), skin);
            Part("Hair_Crown", PrimitiveType.Sphere, body, new Vector3(0, 1.84f, -.04f), new Vector3(.44f, .28f, .39f), hair);
            Part("Hair_Fringe", PrimitiveType.Sphere, body, new Vector3(-.065f, 1.82f, .13f), new Vector3(.22f, .15f, .18f), hair)
                .transform.localRotation = Quaternion.Euler(0, 0, -18);
            Part("Skin_Nose", PrimitiveType.Sphere, body, new Vector3(0, 1.66f, .19f), new Vector3(.065f, .08f, .075f), skin);
            Part("Smile", PrimitiveType.Sphere, body, new Vector3(0, 1.595f, .174f), new Vector3(.085f, .022f, .022f), hair);

            for (int side = -1; side <= 1; side += 2)
            {
                string sideName = side < 0 ? "Left" : "Right";
                Part("Skin_Ear" + side, PrimitiveType.Sphere, body, new Vector3(side * .207f, 1.67f, 0), new Vector3(.08f, .12f, .09f), skin);
                Part("EyeWhite" + side, PrimitiveType.Sphere, body, new Vector3(side * .082f, 1.715f, .173f), new Vector3(.094f, .06f, .061f), light);
                Part("Pupil" + side, PrimitiveType.Sphere, body, new Vector3(side * .076f, 1.712f, .201f), new Vector3(.038f, .045f, .021f), dark);
                Transform arm = Pivot(sideName + "Arm", body, new Vector3(side * .3f, 1.36f, 0));
                Part("Kit_Sleeve", PrimitiveType.Sphere, arm, new Vector3(side * .065f, -.08f, 0), new Vector3(.24f, .28f, .23f), light);
                Part("Skin_Arm", PrimitiveType.Sphere, arm, new Vector3(side * .125f, -.285f, .02f), new Vector3(.135f, .38f, .145f), skin);
                Part("Skin_Hand", PrimitiveType.Sphere, arm, new Vector3(side * .13f, -.48f, .045f), new Vector3(.145f, .16f, .14f), skin);
                Transform leg = Pivot(sideName + "Leg", body, new Vector3(side * .146f, .86f, 0));
                Part("Trim_Leg", PrimitiveType.Sphere, leg, new Vector3(0, -.15f, 0), new Vector3(.24f, .36f, .24f), dark);
                Part("Skin_Knee", PrimitiveType.Sphere, leg, new Vector3(0, -.34f, 0), new Vector3(.16f, .28f, .17f), skin);
                Part("Kit_Sock", PrimitiveType.Cylinder, leg, new Vector3(0, -.59f, 0), new Vector3(.154f, .19f, .154f), light);
                Part("Boot", PrimitiveType.Sphere, leg, new Vector3(0, -.76f, .088f), new Vector3(.194f, .177f, .36f), dark);
            }

            Part("Crest", PrimitiveType.Sphere, body, new Vector3(-.14f, 1.25f, .18f), new Vector3(.083f, .093f, .037f), dark);
            return root.gameObject;
        }

        public static AnimationClip Motion(string motion)
        {
            var clip = new AnimationClip { name = motion, legacy = true, wrapMode = motion == "Kick" ? WrapMode.Once : WrapMode.Loop };
            float duration = motion == "Run" ? .58f : motion == "Kick" ? .34f : 1.5f;
            void Curve(string path, string property, params float[] values)
            {
                var keys = new Keyframe[values.Length];
                for (int i = 0; i < values.Length; i++) keys[i] = new Keyframe(duration * i / (values.Length - 1), values[i]);
                clip.SetCurve(path, typeof(Transform), property, new AnimationCurve(keys));
            }

            if (motion == "Run")
            {
                Curve("Body", "localPosition.y", .02f, .12f, .02f, .12f, .02f);
                Curve("Body/LeftArm", "localEulerAnglesRaw.x", 30, 0, -30, 0, 30);
                Curve("Body/RightArm", "localEulerAnglesRaw.x", -30, 0, 30, 0, -30);
                Curve("Body/LeftLeg", "localEulerAnglesRaw.x", -37, 0, 37, 0, -37);
                Curve("Body/RightLeg", "localEulerAnglesRaw.x", 37, 0, -37, 0, 37);
            }
            else if (motion == "Kick")
            {
                Curve("Body", "localPosition.y", 0, .07f, 0);
                Curve("Body/LeftArm", "localEulerAnglesRaw.x", 0, -31, 0);
                Curve("Body/RightArm", "localEulerAnglesRaw.x", 0, 28, 0);
                Curve("Body/LeftLeg", "localEulerAnglesRaw.x", 0, 10, 0);
                Curve("Body/RightLeg", "localEulerAnglesRaw.x", 25, -63, 0);
            }
            else
            {
                Curve("Body", "localPosition.y", 0, .035f, 0);
                Curve("Body/LeftArm", "localEulerAnglesRaw.x", 0, 5, 0);
                Curve("Body/RightArm", "localEulerAnglesRaw.x", 0, -5, 0);
                Curve("Body/LeftLeg", "localEulerAnglesRaw.x", 0, 0, 0);
                Curve("Body/RightLeg", "localEulerAnglesRaw.x", 0, 0, 0);
            }
            return clip;
        }

        public static void AddAnimation(GameObject character, AnimationClip idle, AnimationClip run, AnimationClip kick)
        {
            var animation = character.AddComponent<Animation>();
            animation.AddClip(idle, "Idle"); animation.AddClip(run, "Run"); animation.AddClip(kick, "Kick");
            animation.clip = idle;
            animation.playAutomatically = false;
        }

        public static GameObject Stadium(Paint paint)
        {
            Transform root = new GameObject("Riverside Stadium").transform;
            Material ink = paint(Ink), white = paint(White), grass = paint(Grass), lightGrass = paint(LightGrass);
            void Box(string name, Vector3 position, Vector3 scale, Material material, bool solid = false)
                => Part(name, PrimitiveType.Cube, root, position, scale, material, solid);
            Box("Ground", new Vector3(0, -.36f, 0), new Vector3(88, .48f, 92), ink);
            Box("Pitch Collider", new Vector3(0, -.13f, 0), new Vector3(32, .26f, 48), grass, true);
            for (int i = 0; i < 10; i++)
                Box("Mown Stripe", new Vector3(0, .008f, -18 + i * 4), new Vector3(26, .016f, 4), i % 2 == 0 ? lightGrass : grass);
            void Line(Vector3 from, Vector3 to, float width, Material material)
            {
                Vector3 offset = to - from;
                GameObject line = Part("Line", PrimitiveType.Cube, root, (from + to) * .5f, new Vector3(width, width * .6f, offset.magnitude + width * .3f), material);
                line.transform.localRotation = Quaternion.LookRotation(offset);
            }
            Vector3 P(float x, float z) => new Vector3(x, .025f, z);
            void Mark(float ax, float az, float bx, float bz) => Line(P(ax, az), P(bx, bz), .075f, white);
            Mark(-13, -20, -13, 20); Mark(13, -20, 13, 20); Mark(-13, 0, 13, 0);
            const int segments = 40;
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.PI * 2 * i / segments, b = Mathf.PI * 2 * (i + 1) / segments;
                Mark(3 * Mathf.Cos(a), 3 * Mathf.Sin(a), 3 * Mathf.Cos(b), 3 * Mathf.Sin(b));
            }
            Box("Center Spot", P(0, 0), new Vector3(.22f, .025f, .22f), white);
            Material net = paint(new Color32(178, 203, 215, 255));
            Material seats = paint(new Color32(45, 77, 129, 255));
            Material seatsAccent = paint(new Color32(92, 124, 172, 255));
            for (int sign = -1; sign <= 1; sign += 2)
            {
                float end = sign * HalfLength, back = end + sign * 1.5f;
                Mark(-13, end, 13, end);
                Mark(-6, end, -6, sign * 15); Mark(6, end, 6, sign * 15); Mark(-6, sign * 15, 6, sign * 15);
                Box("Crossbar", new Vector3(0, GoalHeight, end), new Vector3(GoalHalf * 2 + .14f, .14f, .14f), white, true);
                foreach (int side in new[] { -1, 1 })
                {
                    float x = GoalHalf * side;
                    Box("Goalpost", new Vector3(x, GoalHeight / 2, end), new Vector3(.14f, GoalHeight, .14f), white, true);
                    Line(new Vector3(x, GoalHeight, end), new Vector3(x, 0, back), .085f, white);
                    for (int row = 1; row < 8; row++)
                    {
                        float height = row * GoalHeight / 8f;
                        Line(new Vector3(x, height, end), new Vector3(x, height * .66f, back), .022f, net);
                    }
                }
                for (int row = 1; row < 8; row++)
                {
                    float t = row / 8f;
                    float z = Mathf.Lerp(back, end, t), y = Mathf.Lerp(.04f, GoalHeight, t);
                    Line(new Vector3(-GoalHalf, y, z), new Vector3(GoalHalf, y, z), .021f, net);
                }
                for (int column = 0; column <= 16; column++)
                {
                    float x = Mathf.Lerp(-GoalHalf, GoalHalf, column / 16f);
                    Line(new Vector3(x, .04f, back), new Vector3(x, GoalHeight, end), .021f, net);
                }
                for (int tier = 0; tier < 6; tier++)
                {
                    float y = .32f + tier * .38f;
                    Material seat = tier % 3 == 0 ? seatsAccent : seats;
                    Box("Side Stand", new Vector3(sign * (17 + tier * .75f), y / 2, 0), new Vector3(.75f, y, 48), seat);
                    Box("End Stand", new Vector3(0, y / 2, sign * (25 + tier * .75f)), new Vector3(34, y, .75f), seat);
                }
                Box("Touchline Board", new Vector3(sign * 15, .4f, 0), new Vector3(.22f, .8f, 42), ink);
                Box("Board Trim", new Vector3(sign * 14.88f, .74f, 0), new Vector3(.06f, .1f, 42), seatsAccent);
            }
            return root.gameObject;
        }

        public static GameObject Ball(Paint paint)
        {
            var root = new GameObject("Match Ball");
            float diameter = BallRadius * 2;
            Part("Shell", PrimitiveType.Sphere, root.transform, Vector3.zero, Vector3.one * diameter, paint(White));
            Material dark = paint(Ink);
            foreach (Vector3 axis in new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
            {
                GameObject panel = Part("Panel", PrimitiveType.Sphere, root.transform, axis * (BallRadius - .01f),
                    new Vector3(.135f, .135f, .038f), dark);
                panel.transform.localRotation = Quaternion.LookRotation(axis, Mathf.Abs(axis.y) > .9f ? Vector3.forward : Vector3.up);
            }
            root.AddComponent<SphereCollider>().radius = BallRadius;
            var body = root.AddComponent<Rigidbody>();
            body.mass = .43f;
            body.linearDamping = .53f;
            body.angularDamping = .9f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            return root;
        }
    }
}
