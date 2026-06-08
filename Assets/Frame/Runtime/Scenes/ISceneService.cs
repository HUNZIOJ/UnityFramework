using UnityEngine;
using UnityEngine.SceneManagement;

namespace Frame.Scenes
{
    public interface ISceneService
    {
        Scene ActiveScene { get; }

        void Load(string sceneName, LoadSceneMode mode = LoadSceneMode.Single);

        SceneLoadOperation LoadAsync(SceneLoadArgs args);

        AsyncOperation UnloadAsync(string sceneName);
    }
}
