using UnityEngine;
using UnityEngine.SceneManagement;

namespace Frame.Scenes
{
    public sealed class SceneLoadOperation : CustomYieldInstruction
    {
        private readonly AsyncOperation operation;
        private readonly string sceneName;

        public SceneLoadOperation(string sceneName, AsyncOperation operation)
        {
            this.sceneName = sceneName;
            this.operation = operation;
        }

        public override bool keepWaiting
        {
            get { return operation != null && !operation.isDone; }
        }

        public float Progress
        {
            get { return operation == null ? 1f : operation.progress; }
        }

        public bool IsDone
        {
            get { return operation == null || operation.isDone; }
        }

        public Scene LoadedScene
        {
            get { return SceneManager.GetSceneByName(sceneName); }
        }

        public AsyncOperation Operation
        {
            get { return operation; }
        }
    }
}
