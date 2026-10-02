using System.Linq;
using HereToSlay.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HereToSlay.EditorTools
{
    /// <summary>
    /// Makes sure the sorting layers used by the layered 2D scene exist, and offers a menu to create a dedicated game scene.
    /// </summary>
    [InitializeOnLoad]
    public static class HereToSlaySetup
    {
        private const string ScenePath = "Assets/Scenes/HereToSlay.unity";

        static HereToSlaySetup()
        {
            EditorApplication.delayCall += EnsureSortingLayers;
            EditorApplication.delayCall += EnsurePlayerSettings;
            EditorApplication.update += PollBuildRequest;
        }

        private const string BuildRequestFile = "Builds/build.request";
        private static double nextPoll;

        /// <summary>
        /// Lets a build be requested without touching the editor: create Builds/build.request and the
        /// editor builds the Windows EXE on its next update (the request file is then removed).
        /// </summary>
        private static void PollBuildRequest()
        {
            if (EditorApplication.timeSinceStartup < nextPoll)
            {
                return;
            }

            nextPoll = EditorApplication.timeSinceStartup + 2.0;
            if (!System.IO.File.Exists(BuildRequestFile) || EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            {
                return;
            }

            System.IO.File.Delete(BuildRequestFile);
            BuildWindowsInternal(false);
        }

        /// <summary>Keep the game running (AI turns, animations) even when the window loses focus.</summary>
        [MenuItem("Here To Slay/Ensure Player Settings")]
        public static void EnsurePlayerSettings()
        {
            if (!PlayerSettings.runInBackground)
            {
                PlayerSettings.runInBackground = true;
                Debug.Log("[Here To Slay] Enabled Player Settings > Run In Background.");
            }

            if (PlayerSettings.productName != "Here to Slay")
            {
                PlayerSettings.productName = "Here to Slay";
            }
        }

        [MenuItem("Here To Slay/Ensure Sorting Layers")]
        public static void EnsureSortingLayers()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                return;
            }

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty sortingLayers = tagManager.FindProperty("m_SortingLayers");
            if (sortingLayers == null)
            {
                return;
            }

            bool changed = false;
            foreach (string layerName in Layers.All)
            {
                bool exists = false;
                for (int i = 0; i < sortingLayers.arraySize; i++)
                {
                    if (sortingLayers.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == layerName)
                    {
                        exists = true;
                        break;
                    }
                }

                if (exists)
                {
                    continue;
                }

                sortingLayers.InsertArrayElementAtIndex(sortingLayers.arraySize);
                SerializedProperty entry = sortingLayers.GetArrayElementAtIndex(sortingLayers.arraySize - 1);
                entry.FindPropertyRelative("name").stringValue = layerName;
                entry.FindPropertyRelative("uniqueID").longValue = (uint)(layerName.GetHashCode() & 0x7fffffff);
                SerializedProperty locked = entry.FindPropertyRelative("locked");
                if (locked != null)
                {
                    locked.boolValue = false;
                }

                changed = true;
            }

            if (changed)
            {
                tagManager.ApplyModifiedProperties();
                Debug.Log("[Here To Slay] Added sorting layers: " + string.Join(", ", Layers.All));
            }
        }

        public const string BuildFolder = "Builds/HereToSlay";

        /// <summary>Builds a stand-alone Windows game: Builds/HereToSlay/HereToSlay.exe (the Builds folder is git-ignored).</summary>
        [MenuItem("Here To Slay/Build Windows EXE")]
        public static void BuildWindows()
        {
            BuildWindowsInternal(true);
        }

        private static void BuildWindowsInternal(bool reveal)
        {
            EnsureSortingLayers();
            EnsurePlayerSettings();
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;

            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" };
            }

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = BuildFolder + "/HereToSlay.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
            UnityEditor.Build.Reporting.BuildSummary summary = report.summary;
            string message = $"[Here To Slay] Windows build {summary.result}: {summary.outputPath} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalErrors} errors)";
            System.IO.Directory.CreateDirectory("Builds");
            System.IO.File.WriteAllText("Builds/last_build.txt", message);
            if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log(message);
                if (reveal)
                {
                    EditorUtility.RevealInFinder(summary.outputPath);
                }
            }
            else
            {
                Debug.LogError(message);
            }
        }

        [MenuItem("Here To Slay/Create Game Scene")]
        public static void CreateGameScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("Here To Slay").AddComponent<HereToSlayGame>();
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
            if (existing.All(s => s.path != ScenePath))
            {
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }.Concat(existing).ToArray();
            }

            Debug.Log("[Here To Slay] Created " + ScenePath + ". Press Play!");
        }
    }
}
