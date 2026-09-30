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
