using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MiniFootball
{
    public static class MatchPresentation
    {
        const string Key = "MiniFootball.Presentation.";
        static Camera camera;
        static Light sun;
        static LightShadows originalShadows;
        static RenderPipelineAsset originalOverride;
        static UniversalRenderPipelineAsset pipeline;
        static float previousLod;
        static int previousVsync, previousFps;
        static bool previousHdr, initialized;
        static int preset, frameRate;
        static float cameraHeight;
        static bool replays;

        public static int Preset
        {
            get => preset;
            set { preset = Mathf.Clamp(value, 0, 2); Store("Preset", preset); Apply(); }
        }

        public static int FrameRate
        {
            get => frameRate;
            set { frameRate = value == 30 ? 30 : 60; Store("Fps", frameRate); Apply(); }
        }

        public static float CameraHeight
        {
            get => cameraHeight;
            set { cameraHeight = Mathf.Clamp(value, 10, 22); PlayerPrefs.SetFloat(Key + "Height", cameraHeight); PlayerPrefs.Save(); }
        }

        public static bool Replays
        {
            get => replays;
            set { replays = value; Store("Replays", value ? 1 : 0); }
        }

        static void Store(string name, int value)
        {
            PlayerPrefs.SetInt(Key + name, value);
            PlayerPrefs.Save();
        }

        public static void Initialize(Camera matchCamera)
        {
            Shutdown();
            camera = matchCamera;
            sun = RenderSettings.sun;
            previousLod = QualitySettings.lodBias;
            previousVsync = QualitySettings.vSyncCount;
            previousFps = Application.targetFrameRate;
            previousHdr = camera.allowHDR;
            if (sun != null) originalShadows = sun.shadows;
            originalOverride = QualitySettings.renderPipeline;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset source)
            {
                pipeline = Object.Instantiate(source);
                pipeline.name = "MiniFootball runtime URP";
                pipeline.hideFlags = HideFlags.DontSave;
                QualitySettings.renderPipeline = pipeline;
            }

            preset = Mathf.Clamp(PlayerPrefs.GetInt(Key + "Preset", 1), 0, 2);
            frameRate = PlayerPrefs.GetInt(Key + "Fps", 30) == 60 ? 60 : 30;
            cameraHeight = Mathf.Clamp(PlayerPrefs.GetFloat(Key + "Height", 15), 10, 22);
            replays = PlayerPrefs.GetInt(Key + "Replays", 1) == 1;
            initialized = true;
            Apply();
        }

        static void Apply()
        {
            if (!initialized) return;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = frameRate;
            QualitySettings.lodBias = preset == 0 ? .65f : preset == 1 ? 1 : 1.45f;
            if (pipeline != null)
            {
                pipeline.renderScale = preset == 0 ? .72f : preset == 1 ? .90f : 1f;
                pipeline.shadowDistance = preset == 0 ? 0 : preset == 1 ? 34 : 50;
                pipeline.shadowCascadeCount = preset == 2 ? 2 : 1;
                pipeline.msaaSampleCount = preset == 2 ? 2 : 1;
            }
            if (camera != null) camera.allowHDR = preset == 2 && previousHdr;
            if (sun != null) sun.shadows = preset == 0 ? LightShadows.None : preset == 1 ? LightShadows.Hard : LightShadows.Soft;
        }

        public static void Shutdown()
        {
            if (!initialized) return;
            initialized = false;
            if (pipeline != null)
            {
                if (QualitySettings.renderPipeline == pipeline) QualitySettings.renderPipeline = originalOverride;
                Object.Destroy(pipeline);
            }
            if (sun != null) sun.shadows = originalShadows;
            if (camera != null) camera.allowHDR = previousHdr;
            QualitySettings.lodBias = previousLod;
            QualitySettings.vSyncCount = previousVsync;
            Application.targetFrameRate = previousFps;
            camera = null;
            sun = null;
            pipeline = null;
            originalOverride = null;
        }
    }
}
