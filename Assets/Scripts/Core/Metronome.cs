using UnityEngine;

namespace RhythmPlayer.Core
{
    /// 节拍器：演示音游核心技巧——前瞻调度：提前把未来一小段时间内的声音排进音频队列，
    /// 之后音符生成也会用同样的思路。
    [RequireComponent(typeof(SongClock))]
    public sealed class Metronome : MonoBehaviour
    {
        [SerializeField] SongClock clock;
        [SerializeField] bool enableTicks = true;
        [Range(0f, 1f)]
        [SerializeField] float volume = 0.3f;

        const double LookaheadSeconds = 0.2;
        AudioSource tickSource;
        double nextBeat;

        void Awake()
        {
            if (clock == null) clock = GetComponent<SongClock>();
            tickSource = gameObject.AddComponent<AudioSource>();
            tickSource.playOnAwake = false;
            tickSource.volume = volume;
            tickSource.clip = CreateTickClip();
        }

        void Update()
        {
            if (clock == null || tickSource == null) return;
            if (!enableTicks || !clock.IsRunning)
            {
                nextBeat = System.Math.Floor(clock.Beat) + 1.0;
                return;
            }

            while (clock.DspTimeAt(clock.BeatToSeconds(nextBeat)) < AudioSettings.dspTime + LookaheadSeconds)
            {
                var target = clock.DspTimeAt(clock.BeatToSeconds(nextBeat));
                if (target >= AudioSettings.dspTime)
                {
                    tickSource.PlayScheduled(target);
                }
                nextBeat += 1.0;
            }
        }

        static AudioClip CreateTickClip()
        {
            const int sampleRate = 48000;
            const float frequency = 1046.5f; // C6
            var samples = new float[sampleRate / 40]; // 25ms
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)sampleRate;
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * Mathf.Exp(-t * 120f) * 0.6f;
            }
            var clip = AudioClip.Create("MetronomeTick", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
