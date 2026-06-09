using UnityEngine;

namespace Frame.Audio
{
    public sealed class AudioPlaybackHandle
    {
        private readonly System.Action<AudioPlaybackHandle> stopAction;
        private readonly System.Action<AudioPlaybackHandle> refreshAction;
        private AudioSource source;
        private float volume;
        private float pitch;
        private bool valid;

        internal AudioPlaybackHandle(AudioSource source, AudioCategory category, float volume, float pitch, System.Action<AudioPlaybackHandle> stopAction, System.Action<AudioPlaybackHandle> refreshAction)
        {
            this.source = source;
            Category = category;
            this.volume = Mathf.Clamp01(volume);
            this.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
            this.stopAction = stopAction;
            this.refreshAction = refreshAction;
            valid = source != null;
        }

        public AudioSource Source
        {
            get { return valid ? source : null; }
        }

        public AudioCategory Category
        {
            get;
            private set;
        }

        public float Volume
        {
            get { return volume; }
            set
            {
                volume = Mathf.Clamp01(value);
                Refresh();
            }
        }

        public float Pitch
        {
            get { return pitch; }
            set
            {
                pitch = Mathf.Clamp(value, 0.1f, 3f);
                if (valid && source != null)
                {
                    source.pitch = pitch;
                }
            }
        }

        public bool IsValid
        {
            get { return valid && source != null; }
        }

        public bool IsPlaying
        {
            get { return IsValid && source.isPlaying; }
        }

        public void Stop()
        {
            if (valid && stopAction != null)
            {
                stopAction(this);
            }
        }

        internal void Invalidate()
        {
            valid = false;
            source = null;
        }

        internal void Refresh()
        {
            if (valid && refreshAction != null)
            {
                refreshAction(this);
            }
        }
    }

    public interface IAudioService
    {
        void SetVolume(AudioCategory category, float volume);

        float GetVolume(AudioCategory category);

        void SetMuted(bool muted);

        void PlayMusic(AudioClip clip, float fadeSeconds = 0f, float volume = 1f);

        void StopMusic(float fadeSeconds = 0f);

        AudioSource PlayCue(AudioCue cue, Vector3 position = default(Vector3));

        AudioSource PlayOneShot(AudioClip clip, AudioCategory category = AudioCategory.Sfx, float volume = 1f, float pitch = 1f, Vector3 position = default(Vector3));

        AudioPlaybackHandle PlayCueHandle(AudioCue cue, Vector3 position = default(Vector3));

        AudioPlaybackHandle PlayOneShotHandle(AudioClip clip, AudioCategory category = AudioCategory.Sfx, float volume = 1f, float pitch = 1f, Vector3 position = default(Vector3));
    }
}
