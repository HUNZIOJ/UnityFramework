using System;
using System.Collections.Generic;
using System.IO;
using Frame.Core;
using Frame.UI;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

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
            ValidationReport report = RunProjectValidation();
            LogValidationSummary(report);
        }

        public static ValidationReport RunProjectValidation(bool logDetails = true)
        {
            ValidationReport report = new ValidationReport(logDetails);
            ValidateSettings(report);
            ValidateGameEntry(report);
            ValidateBuildScenes(report);
            ValidateRuntimeDependencies(report);
            ValidateIntegrations(report);
            ValidateResources(report);

            return report;
        }

        public static void ValidateProjectForCI()
        {
            ValidationReport report = RunProjectValidation();
            LogValidationSummary(report);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(report.ExitCode);
                return;
            }

            if (!report.Passed)
            {
                throw new InvalidOperationException("[Frame] Project validation failed. Errors=" + report.Errors + " Warnings=" + report.Warnings);
            }
        }

        private static void LogValidationSummary(ValidationReport report)
        {
            if (report.Errors > 0)
            {
                Debug.LogError("[Frame] Project validation failed. Errors=" + report.Errors + " Warnings=" + report.Warnings);
                return;
            }

            if (report.Warnings > 0)
            {
                Debug.LogWarning("[Frame] Project validation finished with warnings. Warnings=" + report.Warnings);
                return;
            }

            Debug.Log("[Frame] Project validation passed.");
        }

        private static void ValidateSettings(ValidationReport report)
        {
            FrameSettings settings = AssetDatabase.LoadAssetAtPath<FrameSettings>(SettingsAssetPath);
            if (settings == null)
            {
                report.Warning("FrameSettings asset not found. Use Frame/Create Default Frame Settings.");
                return;
            }

            if (settings.UIReferenceResolution.x <= 0f || settings.UIReferenceResolution.y <= 0f)
            {
                report.Error("FrameSettings UI reference resolution is invalid.");
            }

            if (settings.AudioSourcePoolSize <= 0)
            {
                report.Error("FrameSettings audio source pool size is invalid.");
            }

            if (settings.DefaultGameObjectPoolMaxSize <= 0)
            {
                report.Error("FrameSettings default GameObject pool max size is invalid.");
            }
        }

        private static void ValidateGameEntry(ValidationReport report)
        {
#if UNITY_2023_1_OR_NEWER
            GameEntry[] entries = Object.FindObjectsByType<GameEntry>(FindObjectsInactive.Include);
#else
            GameEntry[] entries = Object.FindObjectsOfType<GameEntry>(true);
#endif
            if (entries.Length == 0)
            {
                report.Info("No GameEntry found in current scene. Auto bootstrap can create one before scene load.");
                return;
            }

            if (entries.Length > 1)
            {
                report.Error("Multiple GameEntry instances found in current scene: " + entries.Length);
            }
        }

        private static void ValidateBuildScenes(ValidationReport report)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes == null || scenes.Length == 0)
            {
                report.Warning("Build Settings has no scenes.");
                return;
            }

            bool hasEnabledScene = false;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].enabled)
                {
                    hasEnabledScene = true;
                    if (!File.Exists(scenes[i].path))
                    {
                        report.Error("Enabled build scene is missing: " + scenes[i].path);
                    }
                }
            }

            if (!hasEnabledScene)
            {
                report.Warning("Build Settings has scenes, but none are enabled.");
            }
        }

        private static void ValidateRuntimeDependencies(ValidationReport report)
        {
            ValidatePackage(report, "com.unity.nuget.newtonsoft-json", "Newtonsoft JSON serializer");
            ValidatePackage(report, "com.unity.inputsystem", "Input service asmdef reference");
            ValidatePackage(report, "com.cysharp.unitask", "async services");
            ValidatePackage(report, "com.tuyoogame.yooasset", "YooAsset asset service integration");

            string asmdef = ReadTextAsset("Assets/Frame/Frame.Runtime.asmdef");
            if (string.IsNullOrEmpty(asmdef))
            {
                report.Error("Frame.Runtime.asmdef is missing.");
                return;
            }

            ValidateAsmdefReference(report, asmdef, "UnityEngine.UI");
            ValidateAsmdefReference(report, asmdef, "Unity.InputSystem");
            ValidateAsmdefReference(report, asmdef, "UniTask");
        }

        private static void ValidateIntegrations(ValidationReport report)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>("Assets/ThirdParty/DOTween/DOTween.dll") == null)
            {
                report.Warning("DOTween.dll not found. Disable TweenService or import DOTween before using tween integration.");
            }

            if (AssetDatabase.LoadAssetAtPath<Object>("Assets/Frame/Integrations/DOTween/Frame.DOTween.asmdef") == null)
            {
                report.Warning("Frame DOTween integration asmdef not found.");
            }

            if (AssetDatabase.LoadAssetAtPath<Object>("Assets/Frame/Integrations/YooAsset/Frame.YooAsset.asmdef") == null)
            {
                report.Error("Frame YooAsset integration asmdef not found. YooAsset is the only supported asset backend.");
            }
        }

        private static void ValidateResources(ValidationReport report)
        {
            if (!Directory.Exists("Assets"))
            {
                report.Error("Assets folder is missing.");
                return;
            }

            Dictionary<string, string> resourcePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] files = Directory.GetFiles("Assets", "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string assetPath = files[i].Replace('\\', '/');
                if (ShouldSkipResourceValidation(assetPath))
                {
                    continue;
                }

                string resourcesKey;
                if (!TryGetResourcesKey(assetPath, out resourcesKey))
                {
                    continue;
                }

                string existing;
                if (resourcePaths.TryGetValue(resourcesKey, out existing))
                {
                    report.Warning("Duplicate Resources path '" + resourcesKey + "': " + existing + " and " + assetPath);
                }
                else
                {
                    resourcePaths[resourcesKey] = assetPath;
                }

                ValidateResourceAsset(report, assetPath, resourcesKey);
            }
        }

        private static void ValidateResourceAsset(ValidationReport report, string assetPath, string resourcesKey)
        {
            string extension = Path.GetExtension(assetPath);
            if (resourcesKey.StartsWith("UI/", StringComparison.OrdinalIgnoreCase) && string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase))
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null)
                {
                    report.Error("UI prefab could not be loaded: " + assetPath);
                    return;
                }

                if (prefab.GetComponentInChildren<UIPanelBase>(true) == null)
                {
                    report.Warning("Resources UI prefab has no UIPanelBase component: " + assetPath);
                }
            }

            if (resourcesKey.StartsWith("Configs/", StringComparison.OrdinalIgnoreCase) && string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    JToken.Parse(File.ReadAllText(assetPath));
                }
                catch (Exception exception)
                {
                    report.Error("Invalid JSON config: " + assetPath + " error=" + exception.Message);
                }
            }
        }

        private static void ValidatePackage(ValidationReport report, string packageName, string purpose)
        {
            if (PackageExists(packageName))
            {
                return;
            }

            report.Error("Required package missing for " + purpose + ": " + packageName);
        }

        private static bool PackageExists(string packageName)
        {
            string manifest = ReadTextAsset("Packages/manifest.json");
            if (!string.IsNullOrEmpty(manifest) && manifest.Contains("\"" + packageName + "\""))
            {
                return true;
            }

            if (Directory.Exists("Packages/" + packageName))
            {
                return true;
            }

            string packageCache = "Library/PackageCache";
            if (!Directory.Exists(packageCache))
            {
                return false;
            }

            string[] directories = Directory.GetDirectories(packageCache, packageName + "@*");
            return directories.Length > 0;
        }

        private static void ValidateAsmdefReference(ValidationReport report, string asmdef, string reference)
        {
            if (!asmdef.Contains("\"" + reference + "\""))
            {
                report.Error("Frame.Runtime.asmdef missing reference: " + reference);
            }
        }

        private static string ReadTextAsset(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        private static bool TryGetResourcesKey(string assetPath, out string key)
        {
            const string marker = "/Resources/";
            int index = assetPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                key = null;
                return false;
            }

            key = assetPath.Substring(index + marker.Length);
            key = key.Substring(0, key.Length - Path.GetExtension(key).Length);
            return !string.IsNullOrWhiteSpace(key);
        }

        private static bool ShouldSkipResourceValidation(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return true;
            }

            string extension = Path.GetExtension(assetPath);
            return string.Equals(extension, ".meta", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".asmdef", StringComparison.OrdinalIgnoreCase);
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

        public sealed class ValidationReport
        {
            private readonly bool logDetails;
            private readonly List<ValidationMessage> messages = new List<ValidationMessage>();

            public ValidationReport(bool logDetails = true)
            {
                this.logDetails = logDetails;
            }

            public IReadOnlyList<ValidationMessage> Messages
            {
                get { return messages; }
            }

            public int Errors { get; private set; }

            public int Warnings { get; private set; }

            public bool Passed
            {
                get { return Errors == 0; }
            }

            public int ExitCode
            {
                get { return Passed ? 0 : 1; }
            }

            public void Error(string message)
            {
                Errors++;
                Add(LogType.Error, message);
            }

            public void Warning(string message)
            {
                Warnings++;
                Add(LogType.Warning, message);
            }

            public void Info(string message)
            {
                Add(LogType.Log, message);
            }

            private void Add(LogType type, string message)
            {
                messages.Add(new ValidationMessage(type, message));

                if (!logDetails)
                {
                    return;
                }

                if (type == LogType.Error)
                {
                    Debug.LogError("[Frame] " + message);
                    return;
                }

                if (type == LogType.Warning)
                {
                    Debug.LogWarning("[Frame] " + message);
                    return;
                }

                Debug.Log("[Frame] " + message);
            }
        }

        public sealed class ValidationMessage
        {
            public ValidationMessage(LogType type, string message)
            {
                Type = type;
                Message = message;
            }

            public LogType Type { get; private set; }

            public string Message { get; private set; }
        }
    }
}
