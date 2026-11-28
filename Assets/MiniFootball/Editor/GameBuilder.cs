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

            Scene previous = SceneManager.GetActiveScene();
            bool wasOpen = existingScene.IsValid() && existingScene.isLoaded;
            bool wasActive = wasOpen && previous == existingScene;
            bool replacedScene = false;
            bool savedScene = false;
            Scene building = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(building);
                GameObject athlete = AthleteModel.Create(Paint);
                FootballAnimatorBuilder.Install(athlete, Root);
                ValidateFootballer(athlete);
                GameObject characterPrefab = SavePrefab(athlete, "Footballer");
                GameObject ballObject = FootballArt.Ball(Paint);
                StadiumAssets.PrepareBall(ballObject, Root);
                GameObject ballPrefab = SavePrefab(ballObject, "Ball");
                GameObject stadium = FootballArt.Stadium(Paint);
                StadiumAssets.Prepare(stadium, Root);
                GameObject stadiumPrefab = SavePrefab(stadium, "Stadium");
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
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color32(160, 186, 210, 255);
                RenderSettings.ambientEquatorColor = new Color32(134, 149, 153, 255);
                RenderSettings.ambientGroundColor = new Color32(85, 103, 83, 255);
                RenderSettings.sun = sunlight;

                var game = new GameObject("MiniFootball Game").AddComponent<FootballGame>();
                game.MatchCamera = camera;
                game.CharacterPrefab = characterPrefab;
                game.BallPrefab = ballPrefab;
                game.HighlightMaterial = Paint(new Color32(130, 251, 184, 255));

                AssetDatabase.SaveAssets();
                if (wasOpen)
                {
                    if (!EditorSceneManager.CloseScene(existingScene, true))
                        throw new IOException("Could not close the previous generated scene.");
                    replacedScene = true;
                }
                savedScene = EditorSceneManager.SaveScene(building, GameScenePath);
                if (!savedScene)
                    throw new IOException("Could not save the generated MiniFootball scene.");
            }
            finally
            {
                // Keep a regenerated scene open in the same editing session, including on an upgrade from v1.
                Scene restored = building;
                if (replacedScene && !savedScene)
                    restored = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Additive);
                if (replacedScene && wasActive) SceneManager.SetActiveScene(restored);
                else if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (!savedScene || !wasOpen) EditorSceneManager.CloseScene(building, true);
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

        [MenuItem("MiniFootball/Validate Generated Game")]
        public static void ValidateGeneratedGame()
        {
            GameObject athlete = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Footballer.prefab");
            ValidateFootballer(athlete);
            GameObject ball = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Ball.prefab");
            if (ball == null || ball.GetComponent<Rigidbody>() == null || ball.GetComponent<SphereCollider>() == null)
                throw new InvalidOperationException("The generated ball needs a Rigidbody and a sphere collider.");
            var stadium = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Stadium.prefab");
            if (!stadium || !stadium.GetComponentInChildren<Collider>(true))
                throw new InvalidOperationException("The generated stadium needs a pitch collider.");
            var geometry = stadium.GetComponentsInChildren<MeshFilter>(true);
            if (geometry.Length == 0 || geometry.Any(filter => !filter.sharedMesh || !EditorUtility.IsPersistent(filter.sharedMesh)))
                throw new InvalidOperationException("One or more stadium meshes did not survive prefab serialization.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath) == null)
                throw new InvalidOperationException("Generate the MiniFootball scene before validating it.");

            Scene scene = SceneManager.GetSceneByPath(GameScenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Additive);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<FootballGame>(true)).Single();
                if (!game.MatchCamera || game.CharacterPrefab != athlete || game.BallPrefab != ball || !game.HighlightMaterial)
                    throw new InvalidOperationException("The generated scene has an incomplete match reference.");
                Debug.Log("MiniFootball scene, player rig, animation controller, materials and match references validated.");
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void ValidateFootballer(GameObject footballer)
        {
            if (!footballer) throw new InvalidOperationException("Generate the footballer prefab first.");
            Animator animator = footballer.GetComponentInChildren<Animator>(true);
            if (!animator || !animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman || !animator.runtimeAnimatorController)
                throw new InvalidOperationException("The generated footballer needs a valid humanoid avatar and controller.");
            var appearance = footballer.GetComponent<AthleteAppearance>();
            var lod = footballer.GetComponentInChildren<LODGroup>(true);
            if (!footballer.GetComponent<FootballMotion>() || !appearance || !appearance.Near || !appearance.Far ||
                appearance.Hair == null || appearance.Hair.Length == 0 || appearance.Hair.Any(hair => !hair) ||
                !lod || lod.lodCount < 2)
                throw new InvalidOperationException("The player needs its motion driver, two mesh tiers and hair references.");
            SkinnedMeshRenderer[] skins = footballer.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length == 0 || skins.Any(skin => !skin.sharedMesh || skin.bones.Length == 0 ||
                    skin.bones.Any(bone => !bone) || skin.sharedMaterials.Any(material => !material)))
                throw new InvalidOperationException("Verify the footballer mesh, skeleton bindings and materials.");
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
