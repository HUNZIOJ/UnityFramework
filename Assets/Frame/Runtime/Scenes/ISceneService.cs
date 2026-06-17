using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Frame.Scenes
{
    public interface ISceneService
    {
        event Action<SceneLoadOperation> LoadStarted;

        event Action<SceneLoadOperation, float> LoadProgress;

        event Action<SceneLoadOperation> LoadCompleted;

        Scene ActiveScene { get; }

        bool IsLoading { get; }

        SceneLoadOperation CurrentOperation { get; }

        void Load(string sceneName, LoadSceneMode mode = LoadSceneMode.Single, bool validateInBuildSettings = true);

        SceneLoadOperation LoadAsync(SceneLoadArgs args);

        AsyncOperation UnloadAsync(string sceneName);

        bool IsSceneLoaded(string sceneName);

        bool IsSceneInBuildSettings(string sceneName);

        bool SetActiveScene(string sceneName);
    }
}
