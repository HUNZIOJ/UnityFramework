using UnityEngine;

namespace Frame.Audio
{
    public interface IAudioService
    {
        void SetVolume(AudioCategory category, float volume);

        float GetVolume(AudioCategory category);

        void SetMuted(bool muted);

        void PlayMusic(AudioClip clip, float fadeSeconds = 0f, float volume = 1f);

        void StopMusic(float fadeSeconds = 0f);

        AudioSource PlayCue(AudioCue cue, Vector3 position = default(Vector3));

        AudioSource PlayOneShot(AudioClip clip, AudioCategory category = AudioCategory.Sfx, float volume = 1f, float pitch = 1f, Vector3 position = default(Vector3));
    }
}
