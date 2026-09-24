using UnityEngine;

namespace RhythmPlayer.Core
{
    /// <summary>
    /// 音游时钟：以 AudioSettings.dspTime 为唯一时间基准，与音频硬件采样对齐，不受帧率影响。
    /// 注意：dspTime 是按音频缓冲"跳着"更新的（约几毫秒一步），逐帧直接读取会让画面一顿一顿；
    /// 所以这里用帧时钟逐帧推进、再缓慢向 dspTime 回正的方式输出平滑时间。
    /// 换曲由 LoadClip 完成（音频、BPM、拍偏移一起换）。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class SongClock : MonoBehaviour
    {
        const double SnapThreshold = 0.08;   // 与真实音频偏差超过该值（跳转等）直接对齐
        const double CorrectionRate = 10.0;  // 平时每秒向真实音频收敛的速率

        [Header("歌曲参数")]
        [SerializeField] AudioSource source;
        [Tooltip("BPM（换曲时由 LoadClip 覆盖）")]
        [SerializeField] float bpm = 182f;
        [Tooltip("第 0 拍对应的歌曲时间（秒）")]
        [SerializeField] float firstBeatOffsetSeconds;

        double anchorDsp;       // 歌曲时间 0 对应的 dspTime 锚点
        double pauseTime;       // 暂停时的歌曲时间
        double smoothingTime;   // 逐帧平滑后的歌曲时间
        bool started;           // 是否装载过歌曲（允许 Resume）
        bool running;           // 是否正在播放

        public AudioSource Source => source;
        public bool IsRunning => running;

        /// <summary>是否已装入音频（选曲界面判断能否直接开始）</summary>
        public bool HasClip => source != null && source.clip != null;
        public float Bpm => bpm;
        public float SecondsPerBeat => 60f / bpm;

        /// <summary>当前歌曲时间（秒，已平滑，逐帧连续）</summary>
        public double SongTime => running ? smoothingTime : pauseTime;

        /// <summary>AudioSource 自身报告的播放位置，用于校验时钟偏差</summary>
        public double SourceTime => source != null && source.clip != null ? source.time : 0.0;

        /// <summary>当前拍数（含小数，可为负）</summary>
        public double Beat => SecondsToBeat(SongTime);

        /// <summary>歌曲是否已播完</summary>
        public bool IsFinished => started && running && source != null && source.clip != null && !source.isPlaying;

        /// <summary>拍 → 秒（含拍偏移）</summary>
        public double BeatToSeconds(double beat) => firstBeatOffsetSeconds + beat * SecondsPerBeat;

        /// <summary>秒 → 拍（含拍偏移）</summary>
        public double SecondsToBeat(double seconds) => (seconds - firstBeatOffsetSeconds) / SecondsPerBeat;

        /// <summary>把歌曲时间换算为 dspTime（用于 PlayScheduled 前瞻调度，保持采样级精度）</summary>
        public double DspTimeAt(double songSeconds) => anchorDsp + songSeconds;

        void Awake()
        {
            if (source == null) source = GetComponent<AudioSource>();
        }

        void Update()
        {
            if (!running)
            {
                smoothingTime = pauseTime;
                return;
            }

            var raw = AudioSettings.dspTime - anchorDsp;   // 音频硬件的真实时间（有台阶）
            var delta = Time.unscaledDeltaTime;
            smoothingTime += delta;                        // 帧时钟推进（连续）
            var error = raw - smoothingTime;
            smoothingTime = System.Math.Abs(error) > SnapThreshold
                ? raw                                      // 偏差太大（跳转/重开）→ 直接对齐
                : smoothingTime + error * System.Math.Min(1.0, delta * CorrectionRate); // 缓慢回正
        }

        /// <summary>装载新曲目：换音频、BPM 与拍偏移（选曲后由 GameRoot 调用）</summary>
        public void LoadClip(AudioClip clip, float newBpm, float beatOffsetSeconds)
        {
            source.Stop();
            source.clip = clip;
            bpm = newBpm;
            firstBeatOffsetSeconds = beatOffsetSeconds;
            started = false;
            running = false;
            pauseTime = 0.0;
            smoothingTime = 0.0;
        }

        /// <summary>停止播放并回到初始状态（选曲界面用）</summary>
        public void Stop()
        {
            if (source != null) source.Stop();
            started = false;
            running = false;
            pauseTime = 0.0;
            smoothingTime = 0.0;
        }

        /// <summary>从指定歌曲时间开始播放（提前 0.1 秒预约给音频线程，保证起播干净）</summary>
        public void PlayFrom(double songSeconds = 0.0)
        {
            if (source == null || source.clip == null) return;
            var t = Mathf.Clamp((float)songSeconds, 0f, Mathf.Max(0f, source.clip.length - 0.01f));
            source.Stop();
            source.time = t;
            var startDsp = AudioSettings.dspTime + 0.1;
            source.PlayScheduled(startDsp);
            anchorDsp = startDsp - t;
            pauseTime = t;
            smoothingTime = AudioSettings.dspTime - anchorDsp;
            started = true;
            running = true;
        }

        /// <summary>暂停（记录当前歌曲时间）</summary>
        public void Pause()
        {
            if (!running) return;
            pauseTime = SongTime;
            source.Pause();
            running = false;
        }

        /// <summary>继续播放</summary>
        public void Resume()
        {
            if (running || !started) return;
            source.UnPause();
            anchorDsp = AudioSettings.dspTime - pauseTime;
            smoothingTime = AudioSettings.dspTime - anchorDsp;
            running = true;
        }

        /// <summary>空格键常用：暂停 / 继续（没开始时则从头播）</summary>
        public void TogglePlayPause()
        {
            if (!started) PlayFrom(0.0);
            else if (running) Pause();
            else Resume();
        }

        /// <summary>跳转到指定歌曲时间（播放中即时生效）</summary>
        public void Seek(double songSeconds)
        {
            if (source == null || source.clip == null) return;
            var t = Mathf.Clamp((float)songSeconds, 0f, Mathf.Max(0f, source.clip.length - 0.01f));
            source.time = t;
            pauseTime = t;
            if (running)
            {
                anchorDsp = AudioSettings.dspTime - t;
                smoothingTime = AudioSettings.dspTime - anchorDsp;
            }
        }
    }
}
