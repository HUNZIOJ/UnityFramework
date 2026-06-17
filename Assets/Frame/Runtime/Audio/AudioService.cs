using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Frame.Core;
using UnityEngine;

namespace Frame.Audio
{
    public sealed class AudioService : GameModuleBase, IAudioService
    {
        private readonly Dictionary<AudioCategory, float> mixerVolumes = new Dictionary<AudioCategory, float>();
        private readonly List<AudioSource> sourcePool = new List<AudioSource>();
        private readonly Dictionary<AudioSource, ActivePlayback> activePlaybacks = new Dictionary<AudioSource, ActivePlayback>();
        private Transform audioRoot;
        private AudioPlaybackHandle musicHandle;
        private CancellationTokenSource musicFadeCancellation;
        private bool muted;

        private sealed class ActivePlayback
        {
            public AudioPlaybackHandle Handle;
            public CancellationTokenSource ReturnCancellation;
        }

        public override int Priority
        {
            get { return -300; }
        }

        public AudioPlaybackHandle CurrentMusic
        {
            get { return musicHandle; }
        }

        protected override void OnInitialize()
        {
            GameObject root = new GameObject("Audio");
            root.transform.SetParent(Context.Root, false);
            audioRoot = root.transform;

            SetDefaultVolumes();
            ApplyAllMixerVolumes();
            Prewarm(Context.Settings.AudioSourcePoolSize);
            Context.Services.Register<IAudioService>(this);
            Context.Services.Register(this);
        }

        public void SetVolume(AudioCategory category, float volume)
        {
            mixerVolumes[category] = Mathf.Clamp01(volume);
            ApplyMixerVolume(category);
        }

        public float GetVolume(AudioCategory category)
        {
            float volume;
            return mixerVolumes.TryGetValue(category, out volume) ? volume : 1f;
        }

        public void SetMuted(bool muted)
        {
            this.muted = muted;
            ApplyMixerVolume(AudioCategory.Master);
        }

        public void PlayMusic(AudioClip clip, float fadeSeconds = 0f, float volume = 1f)
        {
            if (clip == null)
            {
                return;
            }

            StopMusic();
            musicHandle = PlayClipHandle(clip, AudioCategory.Music, volume, 1f, default(Vector3), true);
            if (musicHandle != null && fadeSeconds > 0f)
            {
                musicHandle.Volume = 0f;
                StartMusicFade(musicHandle, Mathf.Clamp01(volume), fadeSeconds, false);
            }
        }

        public void StopMusic(float fadeSeconds = 0f)
        {
            if (musicHandle != null)
            {
                AudioPlaybackHandle handle = musicHandle;
                if (fadeSeconds > 0f && handle.IsValid)
                {
                    StartMusicFade(handle, 0f, fadeSeconds, true);
                    return;
                }

                CancelMusicFade();
                handle.Stop();
                musicHandle = null;
            }
        }

        public AudioSource PlayCue(AudioCue cue, Vector3 position = default(Vector3))
        {
            if (cue == null || cue.Clip == null)
            {
                return null;
            }

            if (cue.Category == AudioCategory.Music)
            {
                PlayMusic(cue.Clip, 0f, cue.Volume);
                return musicHandle != null ? musicHandle.Source : null;
            }

            return PlayOneShot(cue.Clip, cue.Category, cue.Volume, cue.Pitch, position);
        }

        public AudioSource PlayOneShot(AudioClip clip, AudioCategory category = AudioCategory.Sfx, float volume = 1f, float pitch = 1f, Vector3 position = default(Vector3))
        {
            AudioPlaybackHandle handle = PlayOneShotHandle(clip, category, volume, pitch, position);
            return handle != null ? handle.Source : null;
        }

        public AudioPlaybackHandle PlayCueHandle(AudioCue cue, Vector3 position = default(Vector3))
        {
            if (cue == null || cue.Clip == null)
            {
                return null;
            }

            if (cue.Category == AudioCategory.Music)
            {
                StopMusic();
                musicHandle = PlayClipHandle(cue.Clip, AudioCategory.Music, cue.Volume, cue.Pitch, position, cue.Loop);
                return musicHandle;
            }

            return PlayOneShotHandle(cue.Clip, cue.Category, cue.Volume, cue.Pitch, position);
        }

        public AudioPlaybackHandle PlayOneShotHandle(AudioClip clip, AudioCategory category = AudioCategory.Sfx, float volume = 1f, float pitch = 1f, Vector3 position = default(Vector3))
        {
            return PlayClipHandle(clip, category, volume, pitch, position, false);
        }

        private AudioPlaybackHandle PlayClipHandle(AudioClip clip, AudioCategory category, float volume, float pitch, Vector3 position, bool loop)
        {
            if (clip == null)
            {
                return null;
            }

            AudioSource source = GetFreeSource();
            source.transform.position = position;
            source.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
            source.loop = loop;
            source.clip = loop ? clip : null;
            ApplyMixerGroup(source, category);

            AudioPlaybackHandle handle = new AudioPlaybackHandle(source, category, volume, pitch, StopPlayback, RefreshPlayback);
            activePlaybacks[source] = new ActivePlayback
            {
                Handle = handle
            };

            RefreshPlayback(handle);
            if (loop)
            {
                source.Play();
            }
            else
            {
                source.PlayOneShot(clip);
                ActivePlayback playback = activePlaybacks[source];
                playback.ReturnCancellation = new CancellationTokenSource();
                float playbackSeconds = clip.length / Mathf.Max(0.01f, source.pitch);
                ReturnWhenFinishedAsync(source, playbackSeconds, handle, playback.ReturnCancellation.Token).Forget();
            }

            return handle;
        }

        protected override void OnShutdown()
        {
            CancelMusicFade();
            musicHandle = null;

            for (int i = 0; i < sourcePool.Count; i++)
            {
                if (sourcePool[i] != null)
                {
                    sourcePool[i].Stop();
                    sourcePool[i].clip = null;
                    sourcePool[i].gameObject.SetActive(false);
                }
            }

            foreach (KeyValuePair<AudioSource, ActivePlayback> playback in activePlaybacks)
            {
                if (playback.Value == null)
                {
                    continue;
                }

                if (playback.Value.ReturnCancellation != null)
                {
                    playback.Value.ReturnCancellation.Cancel();
                    playback.Value.ReturnCancellation.Dispose();
                    playback.Value.ReturnCancellation = null;
                }

                if (playback.Value.Handle != null)
                {
                    playback.Value.Handle.Invalidate();
                }
            }

            activePlaybacks.Clear();
            sourcePool.Clear();
            mixerVolumes.Clear();
            if (audioRoot != null)
            {
                Object.Destroy(audioRoot.gameObject);
            }

            audioRoot = null;
            musicHandle = null;
        }

        private void SetDefaultVolumes()
        {
            mixerVolumes[AudioCategory.Master] = 1f;
            mixerVolumes[AudioCategory.Music] = 1f;
            mixerVolumes[AudioCategory.Sfx] = 1f;
            mixerVolumes[AudioCategory.UI] = 1f;
            mixerVolumes[AudioCategory.Ambient] = 1f;
        }

        private void RefreshPlayback(AudioPlaybackHandle handle)
        {
            if (handle == null || !handle.IsValid)
            {
                return;
            }

            AudioSource source = handle.Source;
            if (source == null)
            {
                return;
            }

            source.pitch = handle.Pitch;
            source.volume = handle.Volume;
        }

        private void StopPlayback(AudioPlaybackHandle handle)
        {
            if (handle == null || !handle.IsValid)
            {
                return;
            }

            ReturnSource(handle.Source, true);
            if (handle == musicHandle)
            {
                CancelMusicFade();
                musicHandle = null;
            }
        }

        private void StartMusicFade(AudioPlaybackHandle handle, float targetVolume, float seconds, bool stopWhenComplete)
        {
            if (handle == null || !handle.IsValid)
            {
                return;
            }

            CancelMusicFade();
            CancellationTokenSource cancellation = new CancellationTokenSource();
            musicFadeCancellation = cancellation;
            FadeMusicAsync(handle, Mathf.Clamp01(targetVolume), seconds, stopWhenComplete, cancellation).Forget();
        }

        private async UniTaskVoid FadeMusicAsync(AudioPlaybackHandle handle, float targetVolume, float seconds, bool stopWhenComplete, CancellationTokenSource cancellation)
        {
            float duration = Mathf.Max(0.001f, seconds);
            float startVolume = handle == null ? 0f : handle.Volume;
            float elapsed = 0f;

            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellation.Token);

                while (handle != null && handle.IsValid && elapsed < duration)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    elapsed += Time.unscaledDeltaTime;
                    handle.Volume = Mathf.Lerp(startVolume, targetVolume, Mathf.Clamp01(elapsed / duration));
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellation.Token);
                }

                if (handle != null && handle.IsValid)
                {
                    handle.Volume = targetVolume;
                    if (stopWhenComplete)
                    {
                        handle.Stop();
                    }
                }
            }
            catch (System.OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(musicFadeCancellation, cancellation))
                {
                    musicFadeCancellation = null;
                    cancellation.Dispose();
                }
            }
        }

        private void CancelMusicFade()
        {
            CancellationTokenSource cancellation = musicFadeCancellation;
            if (cancellation == null)
            {
                return;
            }

            musicFadeCancellation = null;
            cancellation.Cancel();
            cancellation.Dispose();
        }

        private void ApplyMixerGroup(AudioSource source, AudioCategory category)
        {
            if (source != null && Context != null && Context.Settings != null)
            {
                source.outputAudioMixerGroup = Context.Settings.GetAudioMixerGroup(category);
            }
        }

        private void ApplyAllMixerVolumes()
        {
            ApplyMixerVolume(AudioCategory.Master);
            ApplyMixerVolume(AudioCategory.Music);
            ApplyMixerVolume(AudioCategory.Sfx);
            ApplyMixerVolume(AudioCategory.UI);
            ApplyMixerVolume(AudioCategory.Ambient);
        }

        private void ApplyMixerVolume(AudioCategory category)
        {
            if (Context == null || Context.Settings == null)
            {
                return;
            }

            UnityEngine.Audio.AudioMixerGroup group = Context.Settings.GetAssignedAudioMixerGroup(category);
            string parameter = Context.Settings.GetAudioMixerVolumeParameter(category);
            if (group == null || group.audioMixer == null || string.IsNullOrWhiteSpace(parameter))
            {
                return;
            }

            float volume = category == AudioCategory.Master && muted ? 0f : GetVolume(category);
            group.audioMixer.SetFloat(parameter, LinearToDecibels(volume));
        }

        private static float LinearToDecibels(float value)
        {
            if (value <= 0.0001f)
            {
                return -80f;
            }

            return Mathf.Log10(value) * 20f;
        }

        private void Prewarm(int count)
        {
            for (int i = 0; i < count; i++)
            {
                AudioSource source = CreateSource("Audio_" + i);
                source.gameObject.SetActive(false);
                sourcePool.Add(source);
            }
        }

        private AudioSource GetFreeSource()
        {
            for (int i = 0; i < sourcePool.Count; i++)
            {
                if (!sourcePool[i].gameObject.activeSelf)
                {
                    sourcePool[i].gameObject.SetActive(true);
                    return sourcePool[i];
                }
            }

            AudioSource source = CreateSource("Audio_Extra");
            sourcePool.Add(source);
            return source;
        }

        private AudioSource CreateSource(string name)
        {
            GameObject go = new GameObject(name, typeof(AudioSource));
            go.transform.SetParent(audioRoot == null ? Context.Root : audioRoot, false);
            AudioSource source = go.GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }

        private async UniTaskVoid ReturnWhenFinishedAsync(AudioSource source, float seconds, AudioPlaybackHandle handle, CancellationToken cancellationToken)
        {
            try
            {
                int milliseconds = Mathf.Max(0, Mathf.CeilToInt(seconds * 1000f));
                await UniTask.Delay(milliseconds, DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, cancellationToken);
            }
            catch (System.OperationCanceledException)
            {
                return;
            }

            ActivePlayback playback;
            if (source != null && activePlaybacks.TryGetValue(source, out playback) && playback.Handle == handle)
            {
                ReturnSource(source, false);
            }
        }

        private void ReturnSource(AudioSource source, bool stopReturnRoutine)
        {
            if (source == null)
            {
                return;
            }

            ActivePlayback playback;
            if (activePlaybacks.TryGetValue(source, out playback))
            {
                if (playback.ReturnCancellation != null)
                {
                    if (stopReturnRoutine)
                    {
                        playback.ReturnCancellation.Cancel();
                    }

                    playback.ReturnCancellation.Dispose();
                    playback.ReturnCancellation = null;
                }

                if (playback.Handle != null)
                {
                    playback.Handle.Invalidate();
                }

                activePlaybacks.Remove(source);
            }

            source.Stop();
            source.clip = null;
            source.loop = false;
            source.gameObject.SetActive(false);
        }
    }
}
