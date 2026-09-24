using UnityEngine;

namespace RhythmPlayer.Core
{
    /// <summary>
    /// 节拍器：每个整拍发出一次"嗒"声，用来核对音符与音乐是否同步。
    /// 使用"前瞻调度"——提前把未来一小段时间内的声音排进音频硬件队列，
    /// 这样发出的声音是采样级精准的（以后打击音效也会用同样的思路）。
    /// </summary>
    [RequireComponent(typeof(SongClock))]
    public sealed class Metronome : MonoBehaviour
    {
        [SerializeField] SongClock clock;
        [SerializeField] bool enableTicks = true;
        [Range(0f, 1f)]
        [SerializeField] float volume = 0.3f;

        const double LookaheadSeconds = 0.2;  // 提前调度的时间窗（秒）
        AudioSource tickSource;               // 专门播放滴答声的音源
        double nextBeat;                      // 下一个要调度的整拍

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
                // 没在播放：下一个整拍从当前位置重新计算（暂停/跳转后不补拍）
                nextBeat = System.Math.Floor(clock.Beat) + 1.0;
                return;
            }

            // 把"未来 0.2 秒内"的整拍依次排进音频队列
            while (clock.DspTimeAt(clock.BeatToSeconds(nextBeat)) < AudioSettings.dspTime + LookaheadSeconds)
            {
                var target = clock.DspTimeAt(clock.BeatToSeconds(nextBeat));
                if (target >= AudioSettings.dspTime)
                {
                    tickSource.PlayScheduled(target); // 过期的跳转拍直接跳过
                }
                nextBeat += 1.0;
            }
        }

        /// <summary>运行时生成一个短促的滴答音（25ms 正弦 + 快速衰减），不依赖任何素材文件</summary>
        static AudioClip CreateTickClip()
        {
            const int sampleRate = 48000;
            const float frequency = 1046.5f; // C6
            var samples = new float[sampleRate / 40];
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
