using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MiniFootball.Editor
{
    public static class StadiumAssets
    {
        public static void Prepare(GameObject stadium, string root)
        {
            string meshFolder = root + "/StadiumMeshes";
            if (!AssetDatabase.IsValidFolder(meshFolder))
                AssetDatabase.CreateFolder(root, "StadiumMeshes");

            // Save the batched meshes separately so prefab instances and reopened scenes
            // refer to persistent geometry. Preserve each mesh GUID across rebuilds.
            foreach (var filter in stadium.GetComponentsInChildren<MeshFilter>())
            {
                Mesh generated = filter.sharedMesh;
                if (AssetDatabase.Contains(generated)) continue;
                string path = meshFolder + "/" + generated.name + ".asset";
                Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing == null)
                    AssetDatabase.CreateAsset(generated, path);
                else
                {
                    EditorUtility.CopySerialized(generated, existing);
                    filter.sharedMesh = existing;
                    EditorUtility.SetDirty(existing);
                    Object.DestroyImmediate(generated);
                }
            }

            const int w = 256, h = 512;
            Color32[] grass = new Color32[w * h];
            var random = new System.Random(107);
            for (int y = 0; y < h; y++)
            {
                float worldZ = Mathf.Lerp(-24f, 24f, (y + .5f) / h);
                float band = (float)System.Math.Tanh(7f * Mathf.Sin(worldZ * Mathf.PI / 4f));
                for (int x = 0; x < w; x++)
                {
                    float blades = (float)random.NextDouble() * 8f - 4f;
                    float grain = 2.2f * Mathf.Sin(x * 3.7f + y * .74f);
                    float shade = band * 7f + blades + grain;
                    grass[y * w + x] = new Color32((byte)(54 + .56f * shade),
                        (byte)(127 + shade), (byte)(76 + .65f * shade), 255);
                }
            }
            var turf = stadium.transform.Find("Turf").GetComponent<MeshRenderer>();
            turf.sharedMaterial = TexturedMaterial(root, "Mown grass", w, h, grass, .12f);
            stadium.GetComponent<BoxCollider>().sharedMaterial = ContactMaterial(root, "Grass contact", .38f, .32f);
        }

        public static void PrepareBall(GameObject ball, string root)
        {
            const int w = 256, h = 128;
            var panels = PanelCenters();
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float latitude = ((y + .5f) / h - .5f) * Mathf.PI;
                for (int x = 0; x < w; x++)
                {
                    float longitude = 2f * Mathf.PI * (x + .5f) / w;
                    Vector3 normal = new Vector3(Mathf.Cos(longitude) * Mathf.Cos(latitude),
                        Mathf.Sin(latitude), Mathf.Sin(longitude) * Mathf.Cos(latitude));
                    float best = -2f, second = -2f;
                    int closest = 0;
                    for (int p = 0; p < panels.Count; p++)
                    {
                        float score = Vector3.Dot(normal, panels[p]);
                        if (score > best) { second = best; best = score; closest = p; }
                        else if (score > second) second = score;
                    }
                    pixels[y * w + x] = best - second < .004f ? new Color32(83, 95, 101, 255) :
                        closest < 12 ? new Color32(23, 48, 68, 255) : new Color32(243, 245, 237, 255);
                }
            }
            ball.transform.Find("Shell").GetComponent<MeshRenderer>().sharedMaterial =
                TexturedMaterial(root, "Match ball panels", w, h, pixels, .34f);
            ball.GetComponent<SphereCollider>().sharedMaterial = ContactMaterial(root, "Ball contact", .34f, .55f);
        }

        // Twelve icosahedral directions and their twenty triangular face centres.
        static List<Vector3> PanelCenters()
        {
            var centers = new List<Vector3>(32);
            float phi = (1f + Mathf.Sqrt(5f)) * .5f;
            for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
            {
                centers.Add(new Vector3(0, a, b * phi).normalized);
                centers.Add(new Vector3(a, b * phi, 0).normalized);
                centers.Add(new Vector3(a * phi, 0, b).normalized);
            }
            for (int a = 0; a < 12; a++)
            for (int b = a + 1; b < 12; b++)
            for (int c = b + 1; c < 12; c++)
            {
                if (Vector3.Dot(centers[a], centers[b]) > .4f &&
                    Vector3.Dot(centers[a], centers[c]) > .4f &&
                    Vector3.Dot(centers[b], centers[c]) > .4f)
                    centers.Add((centers[a] + centers[b] + centers[c]).normalized);
            }
            return centers;
        }

        static Material TexturedMaterial(string root, string name, int w, int h, Color32[] pixels, float smoothness)
        {
            string path = root + "/Materials/" + name;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path + ".asset");
            if (!texture)
            {
                texture = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                AssetDatabase.CreateAsset(texture, path + ".asset");
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            EditorUtility.SetDirty(texture);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path + ".mat");
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path + ".mat");
            }
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        static PhysicsMaterial ContactMaterial(string root, string name, float friction, float bounce)
        {
            string path = root + "/Materials/" + name + ".asset";
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (!material)
            {
                material = new PhysicsMaterial(name);
                AssetDatabase.CreateAsset(material, path);
            }
            material.dynamicFriction = friction;
            material.staticFriction = friction;
            material.bounciness = bounce;
            material.bounceCombine = PhysicsMaterialCombine.Average;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
