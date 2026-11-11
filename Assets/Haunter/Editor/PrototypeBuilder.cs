using System;
using System.IO;
using UnityEditor;
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
            PlayerSettings.companyName = "Haunter Prototype";
            PlayerSettings.productName = "Haunter in the Dark";
            PlayerSettings.bundleVersion = "0.3.0";
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
