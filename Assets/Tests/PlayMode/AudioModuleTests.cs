using System.Collections;
using System.Reflection;
using Frame.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Frame.Tests.PlayMode
{
    public sealed class AudioModuleTests
    {
        [UnityTest]
        public IEnumerator AudioService_VolumeMuteMusicOneShotAndCueWork()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                AudioService service = fixture.Initialize(new AudioService());
                AudioClip clip = AudioClip.Create("TestClip", 4410, 1, 44100, false);

                service.SetVolume(AudioCategory.Master, 0.5f);
                service.SetVolume(AudioCategory.Sfx, 0.25f);
                Assert.AreEqual(0.5f, service.GetVolume(AudioCategory.Master));
                Assert.AreEqual(0.25f, service.GetVolume(AudioCategory.Sfx));

                service.SetMuted(true);
                AudioSource oneShot = service.PlayOneShot(clip, AudioCategory.Sfx, volume: 1f, pitch: 1f);
                Assert.IsNotNull(oneShot);
                Assert.IsFalse(oneShot.mute);
                Assert.AreEqual(1f, oneShot.volume, 0.001f);

                service.SetVolume(AudioCategory.Sfx, 0.5f);
                Assert.AreEqual(1f, oneShot.volume, 0.001f);

                service.SetVolume(AudioCategory.UI, 0.2f);
                AudioPlaybackHandle handle = service.PlayOneShotHandle(clip, AudioCategory.UI, volume: 0.5f, pitch: 1.5f);
                Assert.IsNotNull(handle);
                Assert.IsTrue(handle.IsValid);
                Assert.IsNotNull(handle.Source);
                Assert.AreEqual(0.5f, handle.Source.volume, 0.001f);
                Assert.AreEqual(1.5f, handle.Source.pitch, 0.001f);

                handle.Volume = 0.8f;
                Assert.AreEqual(0.8f, handle.Source.volume, 0.001f);

                AudioSource handleSource = handle.Source;
                handle.Stop();
                Assert.IsFalse(handle.IsValid);
                Assert.IsFalse(handleSource.gameObject.activeSelf);

                service.SetMuted(false);
                service.PlayMusic(clip, fadeSeconds: 0f, volume: 0.8f);
                yield return null;

                AudioCue musicCue = CreateCue(clip, AudioCategory.Music, 0.6f, 1f, loop: true);
                AudioPlaybackHandle musicHandle = service.PlayCueHandle(musicCue);
                Assert.IsNotNull(musicHandle);
                Assert.AreEqual(AudioCategory.Music, musicHandle.Category);
                Assert.IsTrue(musicHandle.Source.loop);
                Assert.AreEqual(0.6f, musicHandle.Source.volume, 0.001f);

                AudioSource musicPlaybackSource = musicHandle.Source;
                service.StopMusic(0f);
                Assert.IsFalse(musicHandle.IsValid);
                Assert.IsFalse(musicPlaybackSource.gameObject.activeSelf);

                AudioCue cue = CreateCue(clip, AudioCategory.UI, 0.7f, 1.2f);
                AudioSource cueSource = service.PlayCue(cue);
                Assert.IsNotNull(cueSource);

                Object.Destroy(musicCue);
                Object.Destroy(cue);
                Object.Destroy(clip);
                service.Shutdown();
            }
        }

        private static AudioCue CreateCue(AudioClip clip, AudioCategory category, float volume, float pitch, bool loop = false)
        {
            AudioCue cue = ScriptableObject.CreateInstance<AudioCue>();
            SetField(cue, "clip", clip);
            SetField(cue, "category", category);
            SetField(cue, "volume", volume);
            SetField(cue, "pitch", pitch);
            SetField(cue, "loop", loop);
            return cue;
        }

        private static void SetField<T>(AudioCue cue, string fieldName, T value)
        {
            FieldInfo field = typeof(AudioCue).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(cue, value);
        }
    }
}
