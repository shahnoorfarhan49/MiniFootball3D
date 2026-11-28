using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiniFootball.Editor
{
    // Scope the import settings to the original model shipped with MiniFootball.
    public sealed class FootballerImporter : AssetPostprocessor
    {
        internal const string ModelPath = "Assets/MiniFootball/Models/Footballer.fbx";

        public override uint GetVersion() => 1;

        void OnPreprocessModel()
        {
            if (assetPath != ModelPath) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
            importer.importBlendShapes = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importNormals = ModelImporterNormals.Import;

            // The Blender armature deliberately uses the HumanBodyBones enum names.
            // Explicit mapping avoids importer heuristics swapping the limbs or missing toes.
            var bones = new[] {
                HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head,
                HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
                HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes
            };
            var rig = importer.humanDescription;
            rig.human = bones.Select(bone => new HumanBone {
                humanName = HumanTrait.BoneName[(int)bone], boneName = bone.ToString(),
                limit = new HumanLimit { useDefaultValues = true }
            }).ToArray();
            rig.upperArmTwist = rig.lowerArmTwist = .5f;
            rig.upperLegTwist = rig.lowerLegTwist = .5f;
            rig.armStretch = rig.legStretch = .05f;
            importer.humanDescription = rig;
        }
    }

    public static class AthleteModel
    {
        public static GameObject Create(FootballArt.Paint paint)
        {
            string path = FootballerImporter.ModelPath;
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!model) throw new InvalidOperationException("Missing footballer model at " + path);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault(a => a.isValid && a.isHuman);
            if (!avatar)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault(a => a.isValid && a.isHuman);
            }
            if (!avatar) throw new InvalidOperationException("Check the Humanoid rig import messages for " + path);

            var actor = UnityEngine.Object.Instantiate(model);
            actor.name = "Footballer";
            var animator = actor.GetComponent<Animator>();
            if (!animator) animator = actor.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            var appearance = actor.AddComponent<AthleteAppearance>();
            var skins = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer Skin(string name) => skins.Single(s => s.name == name);
            appearance.Near = Skin("AthleteNear");
            appearance.Far = Skin("AthleteFar");
            appearance.Hair = Enumerable.Range(0, 4).Select(i => Skin("HairStyle" + i)).ToArray();

            Material kit = KitMaterial(paint(Color.white));
            foreach (var renderer in skins)
            {
                renderer.sharedMaterials = new[] { kit };
                renderer.quality = SkinQuality.Bone2;
                // Allow the animated arms and keeper dive to leave the rest-pose bounds.
                renderer.localBounds = new Bounds(renderer.localBounds.center, renderer.localBounds.size + Vector3.one * 1.5f);
            }
            for (int i = 0; i < appearance.Hair.Length; i++)
                appearance.Hair[i].gameObject.SetActive(i == 0);
            var lod = actor.AddComponent<LODGroup>();
            lod.SetLODs(new[] {
                new LOD(.11f, new Renderer[] { appearance.Near }),
                new LOD(0, new Renderer[] { appearance.Far })
            });
            // Tight bounds for screen-height LOD selection, independent of padded skinning bounds.
            lod.localReferencePoint = new Vector3(0, .9f, 0);
            lod.size = 1.82f;
            return actor;
        }

        static Material KitMaterial(Material template)
        {
            const string directory = "Assets/MiniFootballGenerated/Materials/";
            const string atlasPath = directory + "FootballKitAtlas.asset";
            const string materialPath = directory + "FootballKit.mat";
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            if (!atlas)
            {
                atlas = AthleteAppearance.NewAtlas();
                AssetDatabase.CreateAsset(atlas, atlasPath);
            }
            AthleteAppearance.FillAtlas(atlas, 0, "Club", 9, new Color32(39, 105, 130, 255), new Color32(27, 40, 55, 255));
            EditorUtility.SetDirty(atlas);
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material)
            {
                material = new Material(template) { name = "Football kit" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", atlas);
            material.SetFloat("_Smoothness", .21f);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
