using UnityEngine;

namespace Frame.Audio
{
    [CreateAssetMenu(menuName = "Frame/Audio Cue", fileName = "AudioCue")]
    public sealed class AudioCue : ScriptableObject
    {
        [SerializeField] private AudioClip clip = null;
        [SerializeField] private AudioCategory category = AudioCategory.Sfx;
        [SerializeField] private float volume = 1f;
        [SerializeField] private float pitch = 1f;
        [SerializeField] private bool loop = false;

        public AudioClip Clip
        {
            get { return clip; }
        }

        public AudioCategory Category
        {
            get { return category; }
        }

        public float Volume
        {
            get { return Mathf.Clamp01(volume); }
        }

        public float Pitch
        {
            get { return Mathf.Clamp(pitch, 0.1f, 3f); }
        }

        public bool Loop
        {
            get { return loop; }
        }
    }
}
