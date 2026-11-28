using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniFootball
{
    // Records a short position and skeletal pose buffer. Replay actors have only render
    // components and never take part in possession, collision, animation events or scoring.
    public sealed class BroadcastCamera
    {
        const float SamplePeriod = 1f / 15f;
        const int Samples = 34;
        const float PlaybackSpeed = .78f;
        const float CelebrationTime = 1.55f;
        readonly Camera camera;
        readonly Transform ball;
        readonly Transform[] players;
        readonly PoseTrack[] tracks;
        readonly GameObject replay;
        Vector3 focus, focusRate, previousBall, ballVelocity;
        float sampleClock, liveZoomRate, goalTime, replayLength;
        int written, next, scorer;
        bool showingGoal, playingReplay;
        public bool IsReplaying => playingReplay;

        sealed class PoseTrack
        {
            readonly Transform source;
            readonly Transform[] nodes;
            readonly Renderer[] originals;
            readonly bool[] wasHidden;
            readonly Vector3[] positions;
            readonly Quaternion[] rotations;
            readonly float[][] shapeWeights;
            Transform[] copies;
            GameObject root;

            public PoseTrack(Transform actor)
            {
                source = actor;
                nodes = source.GetComponentsInChildren<Transform>(true);
                originals = source.GetComponentsInChildren<Renderer>(true);
                wasHidden = new bool[originals.Length];
                positions = new Vector3[Samples * nodes.Length];
                rotations = new Quaternion[positions.Length];
                shapeWeights = new float[originals.Length][];
                for (int i = 0; i < originals.Length; i++)
                    if (originals[i] is SkinnedMeshRenderer skin && skin.sharedMesh != null && skin.sharedMesh.blendShapeCount > 0)
                    {
                        shapeWeights[i] = new float[skin.sharedMesh.blendShapeCount];
                        for (int j = 0; j < shapeWeights[i].Length; j++) shapeWeights[i][j] = skin.GetBlendShapeWeight(j);
                    }
            }

            public void Capture(int frame)
            {
                int offset = frame * nodes.Length;
                positions[offset] = source.position;
                rotations[offset] = source.rotation;
                for (int i = 1; i < nodes.Length; i++)
                {
                    positions[offset + i] = nodes[i].localPosition;
                    rotations[offset + i] = nodes[i].localRotation;
                }
            }

            public Vector3 Position(int frame) => positions[frame * nodes.Length];

            public void Build(Transform container)
            {
                if (root != null) return;
                copies = new Transform[nodes.Length];
                var map = new Dictionary<Transform, Transform>(nodes.Length);
                for (int i = 0; i < nodes.Length; i++)
                {
                    Transform original = nodes[i];
                    Transform copy = new GameObject(original.name).transform;
                    copies[i] = copy;
                    copy.SetParent(i == 0 ? container : map[original.parent], false);
                    copy.localScale = i == 0 ? original.lossyScale : original.localScale;
                    map.Add(original, copy);
                }
                root = copies[0].gameObject;
                // A single mesh tier suffices in the goal replay. Respect optional appearance pieces.
                var excluded = new HashSet<Renderer>();
                foreach (LODGroup group in source.GetComponentsInChildren<LODGroup>(true))
                {
                    LOD[] levels = group.GetLODs();
                    for (int i = 1; i < levels.Length; i++)
                        foreach (Renderer renderer in levels[i].renderers) excluded.Add(renderer);
                    if (levels.Length > 0)
                        foreach (Renderer renderer in levels[0].renderers) excluded.Remove(renderer);
                }
                var block = new MaterialPropertyBlock();
                for (int i = 0; i < originals.Length; i++)
                {
                    Renderer original = originals[i];
                    if (excluded.Contains(original) || !original.gameObject.activeInHierarchy ||
                        original.name == "Selection Marker" || original is TrailRenderer) continue;
                    GameObject target = map[original.transform].gameObject;
                    Renderer copy;
                    if (original is SkinnedMeshRenderer skin)
                    {
                        var clone = target.AddComponent<SkinnedMeshRenderer>();
                        clone.sharedMesh = skin.sharedMesh;
                        clone.localBounds = skin.localBounds;
                        clone.updateWhenOffscreen = true;
                        if (skin.rootBone != null) clone.rootBone = map[skin.rootBone];
                        Transform[] bones = skin.bones;
                        for (int j = 0; j < bones.Length; j++) bones[j] = bones[j] != null ? map[bones[j]] : null;
                        clone.bones = bones;
                        if (shapeWeights[i] != null)
                            for (int j = 0; j < shapeWeights[i].Length; j++) clone.SetBlendShapeWeight(j, shapeWeights[i][j]);
                        copy = clone;
                    }
                    else if (original is MeshRenderer && original.TryGetComponent(out MeshFilter filter))
                    {
                        target.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                        copy = target.AddComponent<MeshRenderer>();
                    }
                    else continue;
                    var materials = original.sharedMaterials;
                    copy.sharedMaterials = materials;
                    copy.shadowCastingMode = original.shadowCastingMode;
                    copy.receiveShadows = original.receiveShadows;
                    original.GetPropertyBlock(block);
                    copy.SetPropertyBlock(block);
                    for (int j = 0; j < materials.Length; j++)
                    {
                        original.GetPropertyBlock(block, j);
                        if (!block.isEmpty) copy.SetPropertyBlock(block, j);
                    }
                }
            }

            public void Show(bool visible)
            {
                if (root == null) return;
                root.SetActive(visible);
                for (int i = 0; i < originals.Length; i++)
                {
                    if (originals[i] == null) continue;
                    if (visible) wasHidden[i] = originals[i].forceRenderingOff;
                    originals[i].forceRenderingOff = visible || wasHidden[i];
                }
            }

            public void Interpolate(int from, int to, float fraction)
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    int a = from * nodes.Length + i, b = to * nodes.Length + i;
                    copies[i].SetLocalPositionAndRotation(Vector3.Lerp(positions[a], positions[b], fraction),
                        Quaternion.Slerp(rotations[a], rotations[b], fraction));
                }
            }

            public void Release()
            {
                if (root != null) Object.Destroy(root);
            }
        }

        public BroadcastCamera(Camera camera, Transform ball, Transform[] players)
        {
            this.camera = camera;
            this.ball = ball;
            this.players = players;
            replay = new GameObject("Goal Replay");
            replay.SetActive(false);
            tracks = new PoseTrack[players.Length + 1];
            tracks[0] = new PoseTrack(ball);
            for (int i = 0; i < players.Length; i++) tracks[i + 1] = new PoseTrack(players[i]);
            previousBall = ball.position;
        }

        public void SetPlayer(int index, Transform actor)
        {
            ResetSequence();
            tracks[index + 1].Release();
            players[index] = actor;
            tracks[index + 1] = new PoseTrack(actor);
        }

        public void Record(float dt)
        {
            if (showingGoal) return;
            if (!MatchPresentation.Replays)
            {
                written = next = 0;
                sampleClock = 0;
                return;
            }
            sampleClock += dt;
            if (sampleClock < SamplePeriod) return;
            sampleClock %= SamplePeriod;
            Capture();
        }

        void Capture()
        {
            foreach (PoseTrack track in tracks) track.Capture(next);
            next = (next + 1) % Samples;
            written = Mathf.Min(written + 1, Samples);
        }

        public float Goal(int scorerIndex)
        {
            if (MatchPresentation.Replays) Capture();
            showingGoal = true;
            scorer = Mathf.Clamp(scorerIndex, 0, players.Length - 1);
            goalTime = 0;
            replayLength = MatchPresentation.Replays && written >= 5 ? (written - 1) * SamplePeriod / PlaybackSpeed : 0;
            return CelebrationTime + replayLength;
        }

        public void ResetSequence()
        {
            if (playingReplay)
            {
                foreach (PoseTrack track in tracks) track.Show(false);
                replay.SetActive(false);
            }
            showingGoal = false;
            playingReplay = false;
            written = next = 0;
            sampleClock = 0;
            previousBall = ball.position;
            ballVelocity = Vector3.zero;
        }

        public void Dispose()
        {
            ResetSequence();
            Object.Destroy(replay);
        }

        public void Track(int selectedIndex, bool immediate = false)
        {
            float dt = Mathf.Clamp(Time.deltaTime, 0, .06f);
            if (showingGoal)
            {
                // Match stoppage uses scaled game time. Limit only camera smoothing
                // after a slow frame so playback stays in step with the restart.
                goalTime += Time.deltaTime;
                if (goalTime >= CelebrationTime && replayLength > 0)
                {
                    ReplayShot(goalTime - CelebrationTime);
                    return;
                }
                Vector3 center = players[scorer].position + Vector3.up * .95f;
                var offset = new Vector3(-6.8f, 2.8f, -Mathf.Sign(center.z) * 5.2f);
                Frame(center, offset, 37f, dt, immediate);
                return;
            }

            Vector3 current = ball.position;
            if (dt > .0001f)
            {
                Vector3 measured = Vector3.ClampMagnitude((current - previousBall) / dt, 24);
                ballVelocity = Vector3.Lerp(ballVelocity, measured, 1 - Mathf.Exp(-5 * dt));
            }
            previousBall = current;
            Vector3 player = players[Mathf.Clamp(selectedIndex, 0, players.Length - 1)].position;
            Vector3 target = Vector3.Lerp(player, current, .68f) + Vector3.ClampMagnitude(ballVelocity * .16f, 2.6f);
            target = new Vector3(Mathf.Clamp(target.x * .73f, -9, 9), .65f, Mathf.Clamp(target.z, -16.5f, 16.5f));
            float spread = Mathf.Clamp01((Vector3.Distance(current, player) + ballVelocity.magnitude * .3f) / 17f);
            float fov = Mathf.Lerp(42, 54, spread);
            if (camera.aspect < 1.45f) fov += 6;
            Frame(target, new Vector3(-18.3f, MatchPresentation.CameraHeight, -2.8f), fov, dt, immediate);
        }

        void Frame(Vector3 target, Vector3 offset, float fov, float dt, bool cut)
        {
            focus = cut ? target : Vector3.SmoothDamp(focus, target, ref focusRate, .28f, 35, dt);
            Vector3 position = focus + offset;
            camera.transform.position = cut ? position : Vector3.Lerp(camera.transform.position, position, 1 - Mathf.Exp(-8 * dt));
            Quaternion rotation = Quaternion.LookRotation(focus - camera.transform.position);
            camera.transform.rotation = cut ? rotation : Quaternion.Slerp(camera.transform.rotation, rotation, 1 - Mathf.Exp(-9 * dt));
            camera.fieldOfView = cut ? fov : Mathf.SmoothDamp(camera.fieldOfView, fov, ref liveZoomRate, .36f, 50, dt);
            if (cut) { focusRate = Vector3.zero; liveZoomRate = 0; }
        }

        void ReplayShot(float elapsed)
        {
            bool cut = !playingReplay;
            if (cut)
            {
                foreach (PoseTrack track in tracks)
                {
                    track.Build(replay.transform);
                    track.Show(true);
                }
                replay.SetActive(true);
                playingReplay = true;
            }
            float frame = Mathf.Clamp(elapsed * PlaybackSpeed / SamplePeriod, 0, written - 1);
            int index = Mathf.FloorToInt(frame), oldest = (next - written + Samples) % Samples;
            int from = (oldest + index) % Samples, to = (oldest + Mathf.Min(index + 1, written - 1)) % Samples;
            float blend = frame - index;
            foreach (PoseTrack track in tracks) track.Interpolate(from, to, blend);
            Vector3 ballTarget = Vector3.Lerp(tracks[0].Position(from), tracks[0].Position(to), blend);
            Vector3 center = Vector3.Lerp(players[scorer].position, ballTarget, .85f);
            float sign = Mathf.Sign(tracks[0].Position((next + Samples - 1) % Samples).z);
            Frame(center, new Vector3(-8, 5.5f, sign * 7.8f), 46, Mathf.Clamp(Time.deltaTime, 0, .06f), cut);
        }
    }
}
