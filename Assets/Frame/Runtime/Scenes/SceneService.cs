using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using Frame.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Frame.Scenes
{
    public sealed class SceneService : GameModuleBase, ISceneService
    {
        private readonly List<SceneLoadOperation> activeOperations = new List<SceneLoadOperation>();

        public event Action<SceneLoadOperation> LoadStarted;

        public event Action<SceneLoadOperation, float> LoadProgress;

        public event Action<SceneLoadOperation> LoadCompleted;

        public override int Priority
        {
            get { return -500; }
        }

        public Scene ActiveScene
        {
            get { return SceneManager.GetActiveScene(); }
        }

        public bool IsLoading
        {
            get
            {
                RemoveCompletedOperations();
                return activeOperations.Count > 0;
            }
        }

        public SceneLoadOperation CurrentOperation
        {
            get;
            private set;
        }

        protected override void OnInitialize()
        {
            Context.Services.Register<ISceneService>(this);
            Context.Services.Register(this);
        }

        public void Load(string sceneName, LoadSceneMode mode = LoadSceneMode.Single, bool validateInBuildSettings = true)
        {
            ValidateSceneName(sceneName);
            ValidateSceneCanLoad(sceneName, validateInBuildSettings);

            SceneManager.LoadScene(sceneName, mode);
        }

        public SceneLoadOperation LoadAsync(SceneLoadArgs args)
        {
            if (args == null || string.IsNullOrWhiteSpace(args.SceneName))
            {
                throw new FrameException("SceneLoadArgs.SceneName is required.");
            }

            ValidateSceneCanLoad(args.SceneName, args.ValidateInBuildSettings);
            if (!args.AllowConcurrentLoads && IsLoading)
            {
                string currentName = CurrentOperation == null ? "unknown" : CurrentOperation.SceneName;
                throw new FrameException("A scene load is already running: " + currentName);
            }

            AsyncOperation operation = SceneManager.LoadSceneAsync(args.SceneName, args.Mode);
            if (operation == null)
            {
                throw new FrameException("Failed to start scene load: " + args.SceneName);
            }

            operation.allowSceneActivation = args.ActivateOnLoad;
            SceneLoadOperation wrapped = new SceneLoadOperation(args.SceneName, operation);
            activeOperations.Add(wrapped);
            CurrentOperation = wrapped;
            PublishLoadStarted(wrapped);
            TrackLoadAsync(args, wrapped).Forget();
            return wrapped;
        }

        public AsyncOperation UnloadAsync(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return null;
            }

            if (!IsSceneLoaded(sceneName))
            {
                return null;
            }

            return SceneManager.UnloadSceneAsync(sceneName);
        }

        public bool IsSceneLoaded(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return false;
            }

            string normalized = NormalizeScenePath(sceneName);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (SceneMatches(scene, sceneName, normalized) && scene.isLoaded)
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsSceneInBuildSettings(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return false;
            }

            string normalized = NormalizeScenePath(sceneName);
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = NormalizeScenePath(SceneUtility.GetScenePathByBuildIndex(i));
                if (string.Equals(path, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string name = Path.GetFileNameWithoutExtension(path);
                if (string.Equals(name, sceneName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public bool SetActiveScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return false;
            }

            string normalized = NormalizeScenePath(sceneName);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (SceneMatches(scene, sceneName, normalized) && scene.isLoaded)
                {
                    return SceneManager.SetActiveScene(scene);
                }
            }

            return false;
        }

        protected override void OnShutdown()
        {
            activeOperations.Clear();
            CurrentOperation = null;
            LoadStarted = null;
            LoadProgress = null;
            LoadCompleted = null;
        }

        private async UniTaskVoid TrackLoadAsync(SceneLoadArgs args, SceneLoadOperation operation)
        {
            try
            {
                while (!operation.IsDone)
                {
                    NotifyProgress(args, operation, operation.NormalizedProgress);
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }

                NotifyProgress(args, operation, 1f);
                Scene loadedScene = operation.LoadedScene;
                if (args.SetActiveOnComplete && loadedScene.IsValid() && loadedScene.isLoaded)
                {
                    SceneManager.SetActiveScene(loadedScene);
                }

                NotifyCompleted(args, operation, loadedScene);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
            }
            finally
            {
                activeOperations.Remove(operation);
                if (CurrentOperation == operation)
                {
                    CurrentOperation = activeOperations.Count == 0 ? null : activeOperations[activeOperations.Count - 1];
                }
            }
        }

        private void NotifyProgress(SceneLoadArgs args, SceneLoadOperation operation, float progress)
        {
            if (args.Progress != null)
            {
                try
                {
                    args.Progress(progress);
                }
                catch (Exception exception)
                {
                    FrameLog.Exception(exception);
                }
            }

            PublishLoadProgress(operation, progress);
        }

        private void NotifyCompleted(SceneLoadArgs args, SceneLoadOperation operation, Scene scene)
        {
            if (args.Completed != null)
            {
                try
                {
                    args.Completed(scene);
                }
                catch (Exception exception)
                {
                    FrameLog.Exception(exception);
                }
            }

            PublishLoadCompleted(operation);
        }

        private void PublishLoadStarted(SceneLoadOperation operation)
        {
            Action<SceneLoadOperation> handler = LoadStarted;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(operation);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
            }
        }

        private void PublishLoadProgress(SceneLoadOperation operation, float progress)
        {
            Action<SceneLoadOperation, float> handler = LoadProgress;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(operation, progress);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
            }
        }

        private void PublishLoadCompleted(SceneLoadOperation operation)
        {
            Action<SceneLoadOperation> handler = LoadCompleted;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(operation);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
            }
        }

        private void RemoveCompletedOperations()
        {
            for (int i = activeOperations.Count - 1; i >= 0; i--)
            {
                if (activeOperations[i] == null || activeOperations[i].IsDone)
                {
                    activeOperations.RemoveAt(i);
                }
            }
        }

        private void ValidateSceneCanLoad(string sceneName, bool validateInBuildSettings)
        {
            if (validateInBuildSettings && !IsSceneInBuildSettings(sceneName))
            {
                throw new FrameException("Scene is not included in Build Settings: " + sceneName);
            }
        }

        private static void ValidateSceneName(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new FrameException("Scene name is empty.");
            }
        }

        private static bool SceneMatches(Scene scene, string sceneName, string normalizedScenePath)
        {
            if (!scene.IsValid())
            {
                return false;
            }

            if (string.Equals(scene.name, sceneName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(NormalizeScenePath(scene.path), normalizedScenePath, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeScenePath(string sceneNameOrPath)
        {
            return string.IsNullOrWhiteSpace(sceneNameOrPath) ? string.Empty : sceneNameOrPath.Replace('\\', '/');
        }
    }
}
