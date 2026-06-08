using System.IO;
using Frame.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Frame.Editor
{
    public static class FrameMenuItems
    {
        private const string SettingsAssetPath = "Assets/Frame/Resources/Frame/FrameSettings.asset";

        [MenuItem("Frame/Create Default Frame Settings")]
        public static void CreateDefaultSettings()
        {
            EnsureFolder("Assets/Frame");
            EnsureFolder("Assets/Frame/Resources");
            EnsureFolder("Assets/Frame/Resources/Frame");

            FrameSettings settings = AssetDatabase.LoadAssetAtPath<FrameSettings>(SettingsAssetPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<FrameSettings>();
                AssetDatabase.CreateAsset(settings, SettingsAssetPath);
                AssetDatabase.SaveAssets();
            }

            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        [MenuItem("Frame/Create GameEntry In Scene")]
        public static void CreateGameEntryInScene()
        {
            GameEntry existing = Object.FindAnyObjectByType<GameEntry>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            GameObject go = new GameObject("Frame", typeof(GameEntry));
            Undo.RegisterCreatedObjectUndo(go, "Create GameEntry");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        [MenuItem("Frame/Open README")]
        public static void OpenReadme()
        {
            Object readme = AssetDatabase.LoadAssetAtPath<Object>("Assets/Frame/README.md");
            if (readme != null)
            {
                AssetDatabase.OpenAsset(readme);
            }
        }

        [MenuItem("Frame/Validate Project")]
        public static void ValidateProject()
        {
            bool hasSettings = AssetDatabase.LoadAssetAtPath<FrameSettings>(SettingsAssetPath) != null;
            bool hasEntry = Object.FindAnyObjectByType<GameEntry>(FindObjectsInactive.Include) != null;

            if (!hasSettings)
            {
                Debug.LogWarning("[Frame] FrameSettings asset not found. Use Frame/Create Default Frame Settings.");
            }

            if (!hasEntry)
            {
                Debug.Log("[Frame] No GameEntry found in current scene. Auto bootstrap is enabled by default when no settings asset exists.");
            }

            if (hasSettings && hasEntry)
            {
                Debug.Log("[Frame] Project validation passed for current scene.");
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path);
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent))
            {
                parent = parent.Replace('\\', '/');
                EnsureFolder(parent);
                AssetDatabase.CreateFolder(parent, name);
            }
        }
    }
}
