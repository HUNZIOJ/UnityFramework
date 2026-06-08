using System;
using UnityEngine.SceneManagement;

namespace Frame.Scenes
{
    [Serializable]
    public sealed class SceneLoadArgs
    {
        public string SceneName;
        public LoadSceneMode Mode = LoadSceneMode.Single;
        public bool ActivateOnLoad = true;
        public Action<float> Progress;
        public Action<Scene> Completed;
    }
}
