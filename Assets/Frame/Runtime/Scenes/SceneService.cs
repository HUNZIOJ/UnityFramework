using Cysharp.Threading.Tasks;
using Frame.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Frame.Scenes
{
    public sealed class SceneService : GameModuleBase, ISceneService
    {
        public override int Priority
        {
            get { return -500; }
        }

        public Scene ActiveScene
        {
            get { return SceneManager.GetActiveScene(); }
        }

        protected override void OnInitialize()
        {
            Context.Services.Register<ISceneService>(this);
            Context.Services.Register(this);
        }

        public void Load(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new FrameException("Scene name is empty.");
            }

            SceneManager.LoadScene(sceneName, mode);
        }

        public SceneLoadOperation LoadAsync(SceneLoadArgs args)
        {
            if (args == null || string.IsNullOrWhiteSpace(args.SceneName))
            {
                throw new FrameException("SceneLoadArgs.SceneName is required.");
            }

            AsyncOperation operation = SceneManager.LoadSceneAsync(args.SceneName, args.Mode);
            if (operation == null)
            {
                throw new FrameException("Failed to start scene load: " + args.SceneName);
            }

            operation.allowSceneActivation = args.ActivateOnLoad;
            SceneLoadOperation wrapped = new SceneLoadOperation(args.SceneName, operation);
            TrackLoadAsync(args, operation).Forget();
            return wrapped;
        }

        public AsyncOperation UnloadAsync(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return null;
            }

            return SceneManager.UnloadSceneAsync(sceneName);
        }

        private async UniTaskVoid TrackLoadAsync(SceneLoadArgs args, AsyncOperation operation)
        {
            while (!operation.isDone)
            {
                if (args.Progress != null)
                {
                    args.Progress(operation.progress);
                }

                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            if (args.Progress != null)
            {
                args.Progress(1f);
            }

            if (args.Completed != null)
            {
                args.Completed(SceneManager.GetSceneByName(args.SceneName));
            }
        }
    }
}
