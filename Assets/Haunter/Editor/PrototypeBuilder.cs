using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Haunter.Editor
{
    public static class PrototypeBuilder
    {
        const string ScenePath = "Assets/Haunter/Scenes/HaunterInTheDark.unity";
        const string MaterialPath = "Assets/Haunter/Materials/Echo.mat";

        [MenuItem("Haunter/Rebuild prototype scene")]
        public static void CreateScene()
        {
            ConfigureProject();
            var surface = Shader.Find("Haunter/EchoSurface");
            var outline = Shader.Find("Hidden/Haunter/OutlineComposite");
            if (!surface || !outline) throw new Exception("Echo shaders were not imported.");
            if (ShaderUtil.ShaderHasError(surface) || ShaderUtil.ShaderHasError(outline))
                throw new Exception("Echo shader compilation failed.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(surface) { name = "Echo — monochrome masks" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PrototypeWorld.Build(material);
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<EchoRenderer>(FindObjectsSortMode.None))
                renderer.OutlineShader = outline;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("HAUNTER_SCENE_READY " + ScenePath);
        }

        static void ConfigureProject()
        {
            EnsureProceduralAudio();
            PlayerSettings.companyName = "Haunter Prototype";
            PlayerSettings.productName = "Haunter in the Dark";
            PlayerSettings.bundleVersion = "0.7.0";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            QualitySettings.antiAliasing = 0;
            QualitySettings.vSyncCount = 1;
            QualitySettings.shadows = ShadowQuality.Disable;
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers");
            layers.GetArrayElementAtIndex(8).stringValue = "Throwable";
            layers.GetArrayElementAtIndex(9).stringValue = "Player";
            layers.GetArrayElementAtIndex(10).stringValue = "Creature";
            tags.ApplyModifiedProperties();
        }

        static void EnsureProceduralAudio()
        {
            const string folder = "Assets/Haunter/Resources/HaunterSounds";
            const int sampleRate = 22050;
            bool created = false;
            foreach (NoiseKind kind in Enum.GetValues(typeof(NoiseKind)))
            {
                string path = Path.Combine(folder, kind + ".wav");
                if (File.Exists(path)) continue;
                Directory.CreateDirectory(folder);
                WriteWave(path, SoundSamples(kind, sampleRate), sampleRate);
                created = true;
            }
            string[] atmosphere = { "Archive", "Courtyard", "Ballroom", "Maze", "Suspicion", "Investigation", "Pursuit", "Release" };
            for (int i = 0; i < atmosphere.Length; i++)
            {
                string path = Path.Combine(folder, atmosphere[i] + ".wav");
                if (File.Exists(path)) continue;
                Directory.CreateDirectory(folder);
                WriteWave(path, AtmosphereSamples(i, sampleRate), sampleRate);
                created = true;
            }
            if (created) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static void WriteWave(string path, float[] samples, int sampleRate)
        {
            const int sampleBytes = sizeof(short);
            int byteCount = samples.Length * sampleBytes;
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            writer.Write(36 + byteCount);
            writer.Write(new byte[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
            writer.Write(new byte[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            writer.Write(16);                 // PCM format block size
            writer.Write((short)1);           // PCM encoding
            writer.Write((short)1);           // Mono
            writer.Write(sampleRate);
            writer.Write(sampleRate * sampleBytes);
            writer.Write((short)sampleBytes);
            writer.Write((short)(sampleBytes * 8));
            writer.Write(new byte[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            writer.Write(byteCount);
            foreach (float sample in samples) writer.Write((short)Mathf.RoundToInt(sample * short.MaxValue));
        }

        // Original procedural sound palette: no downloaded assets or licensing dependencies.
        static float[] SoundSamples(NoiseKind kind, int sampleRate)
        {
            float length = kind == NoiseKind.Collapse ? 1.6f : kind == NoiseKind.Impact ? .65f : kind == NoiseKind.Intro ? 1f : .32f;
            var samples = new float[(int)(sampleRate * length)];
            var random = new System.Random(714 + (int)kind);
            float lowNoise = 0;
            for (int i = 0; i < samples.Length; ++i)
            {
                float t = (float)i / sampleRate;
                float noise = (float)random.NextDouble() * 2 - 1;
                lowNoise = Mathf.Lerp(lowNoise, noise, .12f);
                float sound;
                switch (kind)
                {
                    case NoiseKind.Cane:
                        sound = (Mathf.Sin(t * 1250 * Mathf.PI * 2) * .45f + noise * .45f) * Mathf.Exp(-t * 38)
                            + Mathf.Sin(t * 410 * Mathf.PI * 2) * Mathf.Exp(-t * 16) * .15f; break;
                    case NoiseKind.Impact:
                        sound = (Mathf.Sin(t * 790 * Mathf.PI * 2) * .22f + Mathf.Sin(t * 1263 * Mathf.PI * 2) * .17f) * Mathf.Exp(-t * 9)
                            + noise * Mathf.Exp(-t * 43) * .5f; break;
                    case NoiseKind.Creature:
                        sound = Mathf.Sin(t * (62 - t * 32) * Mathf.PI * 2) * Mathf.Exp(-t * 19) * .7f
                            + lowNoise * Mathf.Exp(-t * 15) * .65f; break;
                    case NoiseKind.Intro:
                        sound = Mathf.Sin(t * (330 - t * 140) * Mathf.PI * 2) * Mathf.Exp(-t * 5) * .16f; break;
                    case NoiseKind.Collapse:
                        float debris = Mathf.Exp(-Mathf.Abs(t - .19f) * 22f) + .48f * Mathf.Exp(-Mathf.Abs(t - .37f) * 17f);
                        sound = .63f * Mathf.Sin(t * (49f - t * 8f) * Mathf.PI * 2) * Mathf.Exp(-t * 4.5f)
                            + lowNoise * 1.35f * Mathf.Exp(-t * 2.9f) + noise * (.35f * Mathf.Exp(-t * 13f) + .24f * debris);
                        break;
                    default:
                        sound = lowNoise * Mathf.Exp(-t * 22) * 1.5f + Mathf.Sin(t * 105 * Mathf.PI * 2) * Mathf.Exp(-t * 28) * .45f; break;
                }
                samples[i] = Mathf.Clamp(sound * Mathf.Min(t * 1200, 1), -.9f, .9f);
            }
            return samples;
        }

        static float[] AtmosphereSamples(int track, int sampleRate)
        {
            float seconds = track < 4 ? 8f : 4f;
            var output = new float[(int)(sampleRate * seconds)];
            var rng = new System.Random(824 + track);
            float texture = 0f;
            for (int i = 0; i < output.Length; i++)
            {
                float time = (float)i / sampleRate;
                float grain = (float)rng.NextDouble() * 2f - 1f;
                texture = Mathf.Lerp(texture, grain, .028f);
                float swell = .62f + .38f * Tone(time, .25f);
                float pulse = Mathf.Exp(-Mathf.Repeat(time, track == 6 ? .5f : 1f) * 11f);
                float value;
                switch (track)
                {
                    case 0: value = Tone(time, 55f) * .12f + Tone(time, 110f) * .035f + texture * .54f; break;
                    case 1: value = texture * .9f * swell + Tone(time, 36f) * .065f + grain * .028f; break;
                    case 2: value = swell * (Tone(time, 73.5f) * .17f + Tone(time, 74f) * .13f + Tone(time, 156.5f) * .07f) + texture * .2f; break;
                    case 3: value = (.10f + .12f * pulse) * Tone(time, 41f) + Tone(time, 61.5f) * .10f + texture * .38f; break;
                    case 4: value = swell * (Tone(time, 58f) * .21f + Tone(time, 87f) * .14f); break;
                    case 5: value = (Tone(time, 65f) * .27f + texture * .6f) * pulse + Tone(time, 98f) * .07f; break;
                    case 6: value = (Tone(time, 49f) * .41f + Tone(time, 113f) * .14f + texture * .42f) * pulse + Tone(time, 55f) * .12f; break;
                    default: value = (.46f + .54f * Mathf.Exp(-time * .7f)) * (Tone(time, 65.5f) * .26f + Tone(time, 98.25f) * .16f); break;
                }
                float edge = Mathf.Clamp01(Mathf.Min(time, seconds - time) * 13f);
                output[i] = Mathf.Clamp(value, -.9f, .9f) * edge;
            }
            return output;
        }

        static float Tone(float seconds, float frequency) => Mathf.Sin(seconds * frequency * Mathf.PI * 2f);

        [MenuItem("Haunter/Build Windows prototype")]
        public static void BuildWindows()
        {
            ConfigureProject();
            if (!File.Exists(ScenePath)) CreateScene();
            EditorSceneManager.SaveOpenScenes();
            string directory = Path.GetFullPath("Builds/Windows");
            Directory.CreateDirectory(directory);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(directory, "HaunterInTheDark.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Windows build failed: " + report.summary.result);
            Debug.Log("HAUNTER_BUILD_OK " + report.summary.totalSize + " bytes");
        }

        [MenuItem("Haunter/Build browser prototype")]
        public static void BuildWebGL()
        {
            ConfigureProject();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                throw new Exception("Install WebGL Build Support for Unity 2023.1.14f1 before building the browser player.");
            if (!File.Exists(ScenePath)) CreateScene();
            EditorSceneManager.SaveOpenScenes();
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, Il2CppCodeGeneration.OptimizeSize);
            PlayerSettings.WebGL.template = "PROJECT:Haunter";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            // GitHub Pages cannot add the Content-Encoding headers required by native decompression.
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.threadsSupport = false;
            PlayerSettings.WebGL.initialMemorySize = 128;
            PlayerSettings.WebGL.maximumMemorySize = 1024;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithoutStacktrace;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            PlayerSettings.WebGL.showDiagnostics = false;
            AssetDatabase.SaveAssets();
            string directory = Path.GetFullPath("Builds/WebGL");
            Directory.CreateDirectory(directory);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = directory,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Browser build failed: " + report.summary.result);
            File.WriteAllText(Path.Combine(directory, ".nojekyll"), string.Empty);
            Debug.Log("HAUNTER_WEBGL_BUILD_OK " + report.summary.totalSize + " bytes");
        }

        public static void RebuildAndBuildWindows()
        {
            CreateScene();
            BuildWindows();
        }

        // Explicit migration of the saved scene. Ordinary BuildWindows still preserves user tuning.
        [MenuItem("Haunter/Apply distraction balance and build")]
        public static void UpdateGuardianAndBuildWindows()
        {
            if (!File.Exists(ScenePath)) CreateScene();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var session = UnityEngine.Object.FindFirstObjectByType<GameSession>();
            if (!session || !session.Creature || !session.Exit) throw new Exception("Missing guardian scene references.");
            var serialized = new SerializedObject(session.Creature);
            serialized.FindProperty("movementSpeed").floatValue = 5.8f;
            serialized.FindProperty("returnSpeed").floatValue = 4.8f;
            serialized.FindProperty("pawInterval").floatValue = .22f;
            serialized.FindProperty("movementHearingScale").floatValue = 80f;
            serialized.FindProperty("caneHearingMultiplier").floatValue = 14f;
            serialized.FindProperty("impactHearingMultiplier").floatValue = 6f;
            serialized.FindProperty("objectListenSeconds").floatValue = 5f;
            serialized.FindProperty("guardOffset").vector3Value = new Vector3(0, 0, -2.2f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            session.Creature.transform.SetPositionAndRotation(session.Exit.position + new Vector3(0, .05f, -2.2f), Quaternion.Euler(0, 180, 0));
            EditorUtility.SetDirty(session.Creature);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            BuildWindows();
        }
    }
}
