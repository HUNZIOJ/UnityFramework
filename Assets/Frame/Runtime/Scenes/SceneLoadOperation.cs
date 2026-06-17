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

        public string SceneName
        {
            get { return sceneName; }
        }

        public override bool keepWaiting
        {
            get { return operation != null && !operation.isDone; }
        }

        public float Progress
        {
            get { return operation == null ? 1f : operation.progress; }
        }

        public float NormalizedProgress
        {
            get
            {
                if (operation == null)
                {
                    return 1f;
                }

                return operation.isDone ? 1f : Mathf.Clamp01(operation.progress / 0.9f);
            }
        }

        public bool IsDone
        {
            get { return operation == null || operation.isDone; }
        }

        public bool IsReadyToActivate
        {
            get { return operation != null && !operation.allowSceneActivation && operation.progress >= 0.9f; }
        }

        public bool AllowSceneActivation
        {
            get { return operation == null || operation.allowSceneActivation; }
            set
            {
                if (operation != null)
                {
                    operation.allowSceneActivation = value;
                }
            }
        }

        public Scene LoadedScene
        {
            get { return SceneManager.GetSceneByName(sceneName); }
        }

        public AsyncOperation Operation
        {
            get { return operation; }
        }

        public void Activate()
        {
            AllowSceneActivation = true;
        }
    }
}
