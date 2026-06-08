using System.Collections;
using System.Collections.Generic;
using Frame.Core;
using UnityEngine;

namespace Frame.Audio
{
    public sealed class AudioService : GameModuleBase, IAudioService
    {
        private readonly Dictionary<AudioCategory, float> volumes = new Dictionary<AudioCategory, float>();
        private readonly List<AudioSource> sourcePool = new List<AudioSource>();
        private Transform audioRoot;
        private AudioSource musicSource;
        private Coroutine musicRoutine;
        private float musicBaseVolume = 1f;
        private bool muted;

        public override int Priority
        {
            get { return -300; }
        }

        protected override void OnInitialize()
        {
            GameObject root = new GameObject("Audio");
            root.transform.SetParent(Context.Root, false);
            audioRoot = root.transform;

            musicSource = CreateSource("Music");
            musicSource.loop = true;
            SetDefaultVolumes();
            Prewarm(Context.Settings.AudioSourcePoolSize);
            Context.Services.Register<IAudioService>(this);
            Context.Services.Register(this);
        }

        public void SetVolume(AudioCategory category, float volume)
        {
            volumes[category] = Mathf.Clamp01(volume);
            if (category == AudioCategory.Music || category == AudioCategory.Master)
            {
                ApplyMusicVolume();
            }
        }

        public float GetVolume(AudioCategory category)
        {
            float volume;
            return volumes.TryGetValue(category, out volume) ? volume : 1f;
        }

        public void SetMuted(bool muted)
        {
            this.muted = muted;
            if (musicSource != null)
            {
                musicSource.mute = muted;
            }

            for (int i = 0; i < sourcePool.Count; i++)
            {
                sourcePool[i].mute = muted;
            }
        }

        public void PlayMusic(AudioClip clip, float fadeSeconds = 0f, float volume = 1f)
        {
            if (clip == null)
            {
                return;
            }

            StopMusicRoutineIfRunning();
            musicRoutine = Context.Coroutines.Run(PlayMusicRoutine(clip, fadeSeconds, Mathf.Clamp01(volume)));
        }

        public void StopMusic(float fadeSeconds = 0f)
        {
            StopMusicRoutineIfRunning();
            musicRoutine = Context.Coroutines.Run(StopMusicRoutine(fadeSeconds));
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
                return musicSource;
            }

            return PlayOneShot(cue.Clip, cue.Category, cue.Volume, cue.Pitch, position);
        }

        public AudioSource PlayOneShot(AudioClip clip, AudioCategory category = AudioCategory.Sfx, float volume = 1f, float pitch = 1f, Vector3 position = default(Vector3))
        {
            if (clip == null)
            {
                return null;
            }

            AudioSource source = GetFreeSource();
            source.transform.position = position;
            source.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
            source.loop = false;
            source.mute = muted;
            source.volume = Mathf.Clamp01(volume) * ResolveVolume(category);
            source.PlayOneShot(clip);
            Context.Coroutines.Run(ReturnWhenFinished(source, clip.length / Mathf.Max(0.01f, source.pitch)));
            return source;
        }

        protected override void OnShutdown()
        {
            StopMusicRoutineIfRunning();
            if (musicSource != null)
            {
                musicSource.Stop();
            }

            for (int i = 0; i < sourcePool.Count; i++)
            {
                if (sourcePool[i] != null)
                {
                    sourcePool[i].Stop();
                }
            }

            sourcePool.Clear();
            volumes.Clear();
            if (audioRoot != null)
            {
                Object.Destroy(audioRoot.gameObject);
            }

            audioRoot = null;
            musicSource = null;
            musicBaseVolume = 1f;
        }

        private void SetDefaultVolumes()
        {
            volumes[AudioCategory.Master] = 1f;
            volumes[AudioCategory.Music] = 1f;
            volumes[AudioCategory.Sfx] = 1f;
            volumes[AudioCategory.UI] = 1f;
            volumes[AudioCategory.Ambient] = 1f;
        }

        private float ResolveVolume(AudioCategory category)
        {
            return GetVolume(AudioCategory.Master) * GetVolume(category);
        }

        private void ApplyMusicVolume()
        {
            if (musicSource != null)
            {
                musicSource.volume = musicBaseVolume * ResolveVolume(AudioCategory.Music);
            }
        }

        private void StopMusicRoutineIfRunning()
        {
            if (musicRoutine != null)
            {
                Context.Coroutines.Stop(musicRoutine);
                musicRoutine = null;
            }
        }

        private void Prewarm(int count)
        {
            for (int i = 0; i < count; i++)
            {
                AudioSource source = CreateSource("Sfx_" + i);
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

            AudioSource source = CreateSource("Sfx_Extra");
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
            source.mute = muted;
            return source;
        }

        private IEnumerator ReturnWhenFinished(AudioSource source, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (source != null)
            {
                source.Stop();
                source.clip = null;
                source.gameObject.SetActive(false);
            }
        }

        private IEnumerator PlayMusicRoutine(AudioClip clip, float fadeSeconds, float targetVolume)
        {
            if (fadeSeconds <= 0f)
            {
                musicSource.clip = clip;
                musicBaseVolume = targetVolume;
                ApplyMusicVolume();
                musicSource.Play();
                musicRoutine = null;
                yield break;
            }

            yield return FadeOutCurrentMusic(fadeSeconds * 0.5f);
            musicSource.clip = clip;
            musicBaseVolume = 0f;
            musicSource.volume = 0f;
            musicSource.Play();

            float elapsed = 0f;
            while (elapsed < fadeSeconds)
            {
                elapsed += UnityEngine.Time.unscaledDeltaTime;
                musicBaseVolume = Mathf.Lerp(0f, targetVolume, elapsed / fadeSeconds);
                ApplyMusicVolume();
                yield return null;
            }

            musicBaseVolume = targetVolume;
            ApplyMusicVolume();
            musicRoutine = null;
        }

        private IEnumerator FadeOutCurrentMusic(float fadeSeconds)
        {
            if (musicSource == null || !musicSource.isPlaying)
            {
                yield break;
            }

            if (fadeSeconds <= 0f)
            {
                musicSource.Stop();
                musicSource.clip = null;
                musicBaseVolume = 1f;
                yield break;
            }

            float startVolume = musicBaseVolume;
            float elapsed = 0f;
            while (elapsed < fadeSeconds)
            {
                elapsed += UnityEngine.Time.unscaledDeltaTime;
                musicBaseVolume = Mathf.Lerp(startVolume, 0f, elapsed / fadeSeconds);
                ApplyMusicVolume();
                yield return null;
            }

            musicSource.Stop();
            musicSource.clip = null;
        }

        private IEnumerator StopMusicRoutine(float fadeSeconds)
        {
            if (musicSource == null || !musicSource.isPlaying)
            {
                musicRoutine = null;
                yield break;
            }

            if (fadeSeconds <= 0f)
            {
                musicSource.Stop();
                musicSource.clip = null;
                musicBaseVolume = 1f;
                musicRoutine = null;
                yield break;
            }

            float startVolume = musicBaseVolume;
            float elapsed = 0f;
            while (elapsed < fadeSeconds)
            {
                elapsed += UnityEngine.Time.unscaledDeltaTime;
                musicBaseVolume = Mathf.Lerp(startVolume, 0f, elapsed / fadeSeconds);
                ApplyMusicVolume();
                yield return null;
            }

            musicSource.Stop();
            musicSource.clip = null;
            musicBaseVolume = 1f;
            musicRoutine = null;
        }
    }
}
