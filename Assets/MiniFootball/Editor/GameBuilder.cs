using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MiniFootball.Editor
{
    public static class GameBuilder
    {
        const string Root = "Assets/MiniFootballGenerated";
        const string Marker = Root + "/GeneratedByMiniFootball.txt";
        const string MarkerText = "MiniFootball3D generated assets v1";
        const string GameScenePath = Root + "/MiniFootball.unity";

        [MenuItem("MiniFootball/Build Complete Game")]
        public static void BuildCompleteGame()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before generating MiniFootball.");

            Scene existingScene = SceneManager.GetSceneByPath(GameScenePath);
            if (existingScene.IsValid() && existingScene.isLoaded && existingScene.isDirty)
                throw new InvalidOperationException("Save or close the open generated scene before rebuilding.");

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Wait for the existing URP package to finish importing.");

            PrepareFolder();
            var cache = new Dictionary<Color32, Material>();
            Material Paint(Color color)
            {
                Color32 key = color;
                if (cache.TryGetValue(key, out Material saved)) return saved;
                string path = Root + "/Materials/" + ColorUtility.ToHtmlStringRGBA(key) + ".mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = shader;
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Smoothness", .22f);
                EditorUtility.SetDirty(material);
                cache.Add(key, material);
                return material;
            }

            AnimationClip idle = SaveClip("Idle"), run = SaveClip("Run"), kick = SaveClip("Kick");
            Scene previous = SceneManager.GetActiveScene();
            Scene generated = existingScene;
            if (generated.IsValid() && generated.isLoaded)
            {
                if (SceneManager.loadedSceneCount == 1)
                {
                    previous = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    SceneManager.SetActiveScene(previous);
                }
                else if (previous == generated)
                {
                    previous = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                        .First(scene => scene.isLoaded && scene != generated);
                    SceneManager.SetActiveScene(previous);
                }
                EditorSceneManager.CloseScene(generated, true);
            }
            Scene building = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(building);
                GameObject athlete = FootballArt.Character(Paint);
                FootballArt.AddAnimation(athlete, idle, run, kick);
                GameObject characterPrefab = SavePrefab(athlete, "Footballer");
                GameObject ballPrefab = SavePrefab(FootballArt.Ball(Paint), "Ball");
                GameObject stadiumPrefab = SavePrefab(FootballArt.Stadium(Paint), "Stadium");
                PrefabUtility.InstantiatePrefab(stadiumPrefab, building);

                var cameraObject = new GameObject("Match Camera");
                cameraObject.tag = "MainCamera";
                var camera = cameraObject.AddComponent<Camera>();
                camera.backgroundColor = new Color32(116, 157, 191, 255);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.nearClipPlane = .15f;
                camera.farClipPlane = 155;
                cameraObject.transform.SetPositionAndRotation(new Vector3(-21, 18, -23), Quaternion.LookRotation(new Vector3(21, -18, 26)));
                cameraObject.AddComponent<AudioListener>();

                var sunlight = new GameObject("Sun").AddComponent<Light>();
                sunlight.type = LightType.Directional;
                sunlight.intensity = 1.2f;
                sunlight.color = new Color32(255, 240, 223, 255);
                sunlight.shadows = LightShadows.Soft;
                sunlight.transform.rotation = Quaternion.Euler(49, -35, 0);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color32(145, 154, 173, 255);
                RenderSettings.sun = sunlight;

                var game = new GameObject("MiniFootball Game").AddComponent<FootballGame>();
                game.MatchCamera = camera;
                game.CharacterPrefab = characterPrefab;
                game.BallPrefab = ballPrefab;
                game.HighlightMaterial = Paint(new Color32(130, 251, 184, 255));

                AssetDatabase.SaveAssets();
                if (!EditorSceneManager.SaveScene(building, GameScenePath))
                    throw new IOException("Could not save the generated MiniFootball scene.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(building, true);
            }

            // Prepend the game so it boots in Windows builds. Preserve other scene entries.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(GameScenePath, true) }
                .Concat(EditorBuildSettings.scenes.Where(scene => scene.path != GameScenePath)).ToArray();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath);
            EditorGUIUtility.PingObject(Selection.activeObject);
            Debug.Log("MiniFootball generated. Open " + GameScenePath + " and enter Play Mode.");
        }

        static void PrepareFolder()
        {
            if (AssetDatabase.IsValidFolder(Root))
            {
                TextAsset marker = AssetDatabase.LoadAssetAtPath<TextAsset>(Marker);
                if (marker == null || marker.text != MarkerText)
                    throw new IOException("Generation stopped because " + Root + " already exists without the MiniFootball ownership marker.");
            }
            else
            {
                AssetDatabase.CreateFolder("Assets", "MiniFootballGenerated");
                File.WriteAllText(Marker, MarkerText);
                AssetDatabase.ImportAsset(Marker, ImportAssetOptions.ForceSynchronousImport);
            }
            foreach (string name in new[] { "Materials", "Animations", "Prefabs" })
                if (!AssetDatabase.IsValidFolder(Root + "/" + name)) AssetDatabase.CreateFolder(Root, name);
        }

        static AnimationClip SaveClip(string name)
        {
            string path = Root + "/Animations/" + name + ".anim";
            AnimationClip clip = FootballArt.Motion(name);
            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(clip, path);
                return clip;
            }
            EditorUtility.CopySerialized(clip, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(clip);
            return existing;
        }

        static GameObject SavePrefab(GameObject source, string name)
        {
            try
            {
                return PrefabUtility.SaveAsPrefabAsset(source, Root + "/Prefabs/" + name + ".prefab")
                    ?? throw new IOException("Could not save " + name + " prefab.");
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }
    }
}
