using UnityEngine;

namespace RhythmPlayer.Core
{
    /// 音游时钟：以 AudioSettings.dspTime 为唯一时间基准，与音频硬件采样对齐，不受帧率影响。
    /// 歌曲时间 0 秒 = 音频第 0 采样；拍数 = (歌曲时间 - offset) / 每拍秒数。
    [RequireComponent(typeof(AudioSource))]
    public sealed class SongClock : MonoBehaviour
    {
        [Header("歌曲参数")]
        [SerializeField] AudioSource source;
        [Tooltip("BPM（来自 musicInfo.json）")]
        [SerializeField] float bpm = 182f;
        [Tooltip("第 0 拍对应的歌曲时间（秒）")]
        [SerializeField] float firstBeatOffsetSeconds;

        double anchorDsp;
        double pauseTime;
        bool started;
        bool running;

        public AudioSource Source => source;
        public bool IsRunning => running;
        public float Bpm => bpm;
        public float SecondsPerBeat => 60f / bpm;

        /// 当前歌曲时间（秒）
        public double SongTime => running ? AudioSettings.dspTime - anchorDsp : pauseTime;

        /// AudioSource 自身报告的播放位置，用于校验时钟偏差
        public double SourceTime => source != null && source.clip != null ? source.time : 0.0;

        /// 当前拍数（含小数，可为负）
        public double Beat => SecondsToBeat(SongTime);

        public bool IsFinished => started && running && source != null && source.clip != null && !source.isPlaying;

        public double BeatToSeconds(double beat) => firstBeatOffsetSeconds + beat * SecondsPerBeat;
        public double SecondsToBeat(double seconds) => (seconds - firstBeatOffsetSeconds) / SecondsPerBeat;

        /// 把歌曲时间换算为 dspTime（用于 PlayScheduled 前瞻调度）
        public double DspTimeAt(double songSeconds) => anchorDsp + songSeconds;

        void Awake()
        {
            if (source == null) source = GetComponent<AudioSource>();
        }

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
            started = true;
            running = true;
        }

        public void Pause()
        {
            if (!running) return;
            pauseTime = SongTime;
            source.Pause();
            running = false;
        }

        public void Resume()
        {
            if (running || !started) return;
            source.UnPause();
            anchorDsp = AudioSettings.dspTime - pauseTime;
            running = true;
        }

        public void TogglePlayPause()
        {
            if (!started) PlayFrom(0.0);
            else if (running) Pause();
            else Resume();
        }

        public void Seek(double songSeconds)
        {
            if (source == null || source.clip == null) return;
            var t = Mathf.Clamp((float)songSeconds, 0f, Mathf.Max(0f, source.clip.length - 0.01f));
            source.time = t;
            pauseTime = t;
            if (running) anchorDsp = AudioSettings.dspTime - t;
        }
    }
}
