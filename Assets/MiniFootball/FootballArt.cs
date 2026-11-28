using System.Collections.Generic;
using UnityEngine;

namespace MiniFootball
{
    public static class FootballArt
    {
        public const float HalfWidth = 13f, HalfLength = 20f, GoalHalf = 3.2f, GoalHeight = 2.15f, BallRadius = .112f;
        public static readonly Color Ink = new Color32(18, 26, 43, 255);
        static readonly Color White = new Color32(231, 243, 227, 255);
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

        // Pack each section by material. Seats, fans, markings and net strands add
        // vertices to shared meshes without creating a GameObject per detail.
        sealed class Geometry
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> indices = new List<int>();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int first = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                uv.Add(Vector2.zero); uv.Add(Vector2.up); uv.Add(Vector2.one); uv.Add(Vector2.right);
                indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
                indices.Add(first); indices.Add(first + 2); indices.Add(first + 3);
            }

            public void Box(Vector3 center, Vector3 dimensions)
            {
                Vector3 h = dimensions * .5f, lo = center - h, hi = center + h;
                var a = new Vector3(lo.x, lo.y, lo.z);
                var b = new Vector3(lo.x, hi.y, lo.z);
                var c = new Vector3(hi.x, hi.y, lo.z);
                var d = new Vector3(hi.x, lo.y, lo.z);
                var e = new Vector3(lo.x, lo.y, hi.z);
                var f = new Vector3(lo.x, hi.y, hi.z);
                var g = new Vector3(hi.x, hi.y, hi.z);
                var h2 = new Vector3(hi.x, lo.y, hi.z);
                Quad(a, b, c, d); Quad(h2, g, f, e);
                Quad(e, f, b, a); Quad(d, c, g, h2);
                Quad(b, f, g, c); Quad(e, a, d, h2);
            }

            public void Tube(Vector3 start, Vector3 end, float radius, int sides)
            {
                Vector3 axis = (end - start).normalized;
                Vector3 right = Vector3.Cross(axis, Mathf.Abs(axis.y) < .85f ? Vector3.up : Vector3.forward).normalized * radius;
                Vector3 up = Vector3.Cross(axis, right);
                for (int i = 0; i < sides; i++)
                {
                    float a = 2 * Mathf.PI * i / sides, b = 2 * Mathf.PI * (i + 1) / sides;
                    Vector3 u = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                    Vector3 v = right * Mathf.Cos(b) + up * Mathf.Sin(b);
                    Quad(start + u, start + v, end + v, end + u);
                }
            }

            public void Ribbon(Vector3 start, Vector3 end, float width)
            {
                Vector3 offset = Vector3.Cross(Vector3.up, end - start).normalized * (.5f * width);
                Quad(start - offset, end - offset, end + offset, start + offset);
            }

            public void Bake(string name, Transform parent, Material material, bool shadows = true)
            {
                if (vertices.Count == 0) return;
                var mesh = new Mesh { name = name };
                if (vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(indices, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                var obj = new GameObject(name);
                obj.transform.SetParent(parent, false);
                obj.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = obj.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                if (!shadows) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        public static GameObject Stadium(Paint paint)
        {
            Transform root = new GameObject("Riverside Stadium").transform;
            var turf = new Geometry();
            turf.Quad(new Vector3(-16, 0, -24), new Vector3(-16, 0, 24), new Vector3(16, 0, 24), new Vector3(16, 0, -24));
            turf.Bake("Turf", root, paint(Color.white), false);
            var floor = root.gameObject.AddComponent<BoxCollider>();
            floor.center = new Vector3(0, -.16f, 0);
            floor.size = new Vector3(32, .32f, 48);

            var ground = new Geometry();
            ground.Box(new Vector3(0, -.34f, 0), new Vector3(82, .32f, 82));
            ground.Bake("Concourse", root, paint(new Color32(66, 78, 84, 255)), false);
            BuildMarkings(root, paint(White));
            BuildGoals(root, paint);
            for (int end = 0; end < 4; end++) BuildStand(root, paint, end);
            return root.gameObject;
        }

        static void BuildMarkings(Transform parent, Material white)
        {
            var lines = new Geometry();
            Vector3 P(float x, float z) => new Vector3(x, .009f, z);
            void Mark(float ax, float az, float bx, float bz) => lines.Ribbon(P(ax, az), P(bx, bz), .085f);
            void Arc(float x, float z, float radius, float first, float last, int segments)
            {
                for (int i = 0; i < segments; i++)
                {
                    float a = Mathf.Lerp(first, last, (float)i / segments) * Mathf.Deg2Rad;
                    float b = Mathf.Lerp(first, last, (float)(i + 1) / segments) * Mathf.Deg2Rad;
                    Mark(x + radius * Mathf.Cos(a), z + radius * Mathf.Sin(a), x + radius * Mathf.Cos(b), z + radius * Mathf.Sin(b));
                }
            }

            Mark(-HalfWidth, -HalfLength, -HalfWidth, HalfLength);
            Mark(HalfWidth, -HalfLength, HalfWidth, HalfLength);
            Mark(-HalfWidth, 0, HalfWidth, 0);
            Arc(0, 0, 3.1f, 0, 360, 54);
            lines.Box(P(0, 0), new Vector3(.16f, .008f, .16f));
            for (int sign = -1; sign <= 1; sign += 2)
            {
                float line = HalfLength * sign;
                Mark(-HalfWidth, line, HalfWidth, line);
                foreach (Vector2 box in new[] { new Vector2(6.1f, 5.1f), new Vector2(3.9f, 1.85f) })
                {
                    float inner = (HalfLength - box.y) * sign;
                    Mark(-box.x, line, -box.x, inner); Mark(-box.x, inner, box.x, inner); Mark(box.x, inner, box.x, line);
                }
                lines.Box(P(0, sign * 15.9f), new Vector3(.13f, .008f, .13f));
                Arc(0, sign * 15.9f, 2.1f, sign > 0 ? 209 : 29, sign > 0 ? 331 : 151, 20);
                Arc(-HalfWidth, line, .55f, sign < 0 ? 0 : 270, sign < 0 ? 90 : 360, 7);
                Arc(HalfWidth, line, .55f, sign < 0 ? 90 : 180, sign < 0 ? 180 : 270, 7);
            }
            lines.Bake("Field paint", parent, white, false);
        }

        static void BuildGoals(Transform root, Paint paint)
        {
            var frame = new Geometry();
            var net = new Geometry();
            var flags = new Geometry();
            for (int sign = -1; sign <= 1; sign += 2)
            {
                float end = sign * HalfLength;
                Vector3 Front(float x, float y) => new Vector3(x, y, end);
                Vector3 Rear(float x, float y) => new Vector3(x, y, end + sign * Mathf.Lerp(1.72f, 1.16f, y / GoalHeight));
                void Post(Vector3 a, Vector3 b, float radius, bool solid)
                {
                    frame.Tube(a, b, radius, 10);
                    if (!solid) return;
                    var collider = new GameObject("Goal frame collider");
                    collider.transform.SetParent(root, false);
                    collider.transform.localPosition = (a + b) / 2;
                    collider.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
                    var capsule = collider.AddComponent<CapsuleCollider>();
                    capsule.radius = radius;
                    capsule.height = (b - a).magnitude + 2 * radius;
                }

                Post(Front(-GoalHalf, GoalHeight), Front(GoalHalf, GoalHeight), .065f, true);
                foreach (int side in new[] { -1, 1 })
                {
                    float x = side * GoalHalf;
                    Post(Front(x, 0), Front(x, GoalHeight), .065f, true);
                    Post(Front(x, GoalHeight), Rear(x, GoalHeight), .042f, false);
                    Post(Rear(x, GoalHeight), Rear(x, 0), .042f, false);
                    for (int row = 0; row <= 10; row++)
                    {
                        float y = GoalHeight * row / 10f;
                        net.Tube(Front(x, y), Rear(x, y), .010f, 3);
                    }
                    for (int column = 1; column < 7; column++)
                    {
                        float t = column / 7f;
                        net.Tube(Vector3.Lerp(Front(x, .015f), Rear(x, .015f), t),
                            Vector3.Lerp(Front(x, GoalHeight), Rear(x, GoalHeight), t), .010f, 3);
                    }

                    var corner = new Vector3(side * (HalfWidth + .12f), 0, sign * (HalfLength + .12f));
                    frame.Tube(corner, corner + Vector3.up * 1.05f, .019f, 5);
                    flags.Box(corner + new Vector3(side * .13f, .92f, 0), new Vector3(.26f, .19f, .02f));
                }
                for (int i = 0; i <= 32; i++)
                {
                    float x = Mathf.Lerp(-GoalHalf, GoalHalf, i / 32f);
                    net.Tube(Front(x, GoalHeight), Rear(x, GoalHeight), .010f, 3);
                    net.Tube(Rear(x, GoalHeight), Rear(x, .015f), .010f, 3);
                }
                for (int row = 0; row <= 10; row++)
                {
                    float y = Mathf.Max(.015f, row * GoalHeight / 10f);
                    net.Tube(Rear(-GoalHalf, y), Rear(GoalHalf, y), .010f, 3);
                }
                for (int r = 0; r < 6; r++)
                {
                    float t = r / 6f;
                    net.Tube(Vector3.Lerp(Front(-GoalHalf, GoalHeight), Rear(-GoalHalf, GoalHeight), t),
                        Vector3.Lerp(Front(GoalHalf, GoalHeight), Rear(GoalHalf, GoalHeight), t), .010f, 3);
                }
            }
            frame.Bake("Posts and flags", root, paint(White));
            net.Bake("Goal netting", root, paint(new Color32(199, 215, 211, 255)), false);
            flags.Bake("Corner pennants", root, paint(new Color32(255, 164, 59, 255)), false);
        }

        static void BuildStand(Transform root, Paint paint, int section)
        {
            bool sideline = section < 2;
            int sign = section % 2 == 0 ? 1 : -1;
            float edge = sideline ? 16.65f : 24.2f;
            int seats = sideline ? 52 : 32;
            float length = seats * .82f;
            string sectionName = new[] { "East", "West", "North", "South" }[section];
            var concrete = new Geometry();
            var seatsBlue = new Geometry();
            var seatsLight = new Geometry();
            var steel = new Geometry();
            var roof = new Geometry();
            var boards = new Geometry();
            var crowd = new[] { new Geometry(), new Geometry(), new Geometry(), new Geometry() };
            var heads = new Geometry();
            var random = new System.Random(701 + section);
            Vector3 Point(float along, float up, float depth) => sideline
                ? new Vector3(sign * (edge + depth), up, along) : new Vector3(along, up, sign * (edge + depth));
            Vector3 Size(float along, float up, float depth) => sideline ? new Vector3(depth, up, along) : new Vector3(along, up, depth);
            for (int row = 0; row < 7; row++)
            {
                float y = .64f + row * .37f;
                float d = row * .74f;
                concrete.Box(Point(0, y / 2, d), Size(length + 1.6f, y, .745f));
                for (int column = 0; column < seats; column++)
                {
                    if ((column + 1) % 12 < 2) continue;
                    float a = (column - (seats - 1) * .5f) * .82f;
                    Geometry seat = (column / 6 + row / 3) % 3 == 1 ? seatsLight : seatsBlue;
                    seat.Box(Point(a, y + .13f, d), Size(.48f, .10f, .39f));
                    seat.Box(Point(a, y + .27f, d + .17f), Size(.48f, .31f, .08f));
                    if (random.NextDouble() > .62) continue;
                    float height = .36f + .08f * (float)random.NextDouble();
                    float lean = .09f * ((float)random.NextDouble() - .5f);
                    // Tapered jackets and faceted heads give the terraces variation at broadcast distance.
                    Geometry jacket = crowd[random.Next(crowd.Length)];
                    jacket.Tube(Point(a, y + .20f, d), Point(a + lean, y + height + .17f, d - .045f), .148f, 5);
                    heads.Tube(Point(a + lean, y + height + .15f, d - .045f),
                        Point(a + lean, y + height + .35f, d - .064f), .098f, 6);
                }
            }

            boards.Box(Point(0, .32f, -.75f), Size(length, .64f, .09f));
            for (int i = -2; i <= 2; i++)
                seatsLight.Box(Point(i * length / 5, .36f, -.81f), Size(length / 8, .11f, .05f));
            if (section == 0)
            {
                // Put the cantilever on the far touchline, outside the camera's line of sight.
                float span = length + 1.6f;
                roof.Box(Point(0, 5.7f, 1.65f), Size(span, .19f, 6.8f));
                roof.Box(Point(0, 5.46f, -1.7f), Size(span, .47f, .16f));
                for (int i = -3; i <= 3; i++)
                {
                    float a = i * span / 6.35f;
                    steel.Tube(Point(a, 0, 4.6f), Point(a, 5.7f, 4.6f), .095f, 6);
                    steel.Tube(Point(a, 4.15f, 4.6f), Point(a, 5.66f, -1.2f), .075f, 6);
                }
            }

            concrete.Bake(sectionName + " terraces", root, paint(new Color32(104, 115, 120, 255)));
            seatsBlue.Bake(sectionName + " seats", root, paint(new Color32(39, 87, 121, 255)), false);
            seatsLight.Bake(sectionName + " accents", root, paint(new Color32(82, 157, 153, 255)), false);
            boards.Bake(sectionName + " barrier", root, paint(new Color32(29, 55, 70, 255)));
            roof.Bake(sectionName + " canopy", root, paint(new Color32(218, 225, 222, 255)));
            steel.Bake(sectionName + " trusses", root, paint(new Color32(87, 105, 111, 255)));
            Color32[] coats = { new Color32(67, 90, 104, 255), new Color32(154, 87, 78, 255),
                new Color32(178, 186, 170, 255), new Color32(78, 115, 135, 255) };
            for (int i = 0; i < crowd.Length; i++)
                crowd[i].Bake(sectionName + " crowd " + i, root, paint(coats[i]), false);
            heads.Bake(sectionName + " crowd faces", root, paint(new Color32(169, 131, 103, 255)), false);
        }

        public static GameObject Ball(Paint paint)
        {
            var root = new GameObject("Match Ball");
            Part("Shell", PrimitiveType.Sphere, root.transform, Vector3.zero, Vector3.one * (2 * BallRadius), paint(White));
            root.AddComponent<SphereCollider>().radius = BallRadius;
            var body = root.AddComponent<Rigidbody>();
            body.mass = .43f;
            // The engine default caps spin below the roll rate of even a slow ground pass.
            body.maxAngularVelocity = 180f;
            body.linearDamping = .115f;
            body.angularDamping = .28f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            return root;
        }
    }
}
