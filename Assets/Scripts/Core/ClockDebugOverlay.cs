using UnityEngine;

namespace RhythmPlayer.Core
{
    /// 阶段 1 验证界面：显示歌曲时间 / 拍数 / 时钟与音频的最大偏差。
    /// 空格 = 暂停继续，R = 从头重开。
    [RequireComponent(typeof(SongClock))]
    public sealed class ClockDebugOverlay : MonoBehaviour
    {
        [SerializeField] SongClock clock;

        float maxDriftMs;
        GUIStyle style;

        void Awake()
        {
            if (clock == null) clock = GetComponent<SongClock>();
        }

        void Update()
        {
            if (clock == null) return;
            if (Input.GetKeyDown(KeyCode.Space)) clock.TogglePlayPause();
            if (Input.GetKeyDown(KeyCode.R)) clock.PlayFrom(0.0);
            if (clock.IsRunning && clock.SourceTime > 0.1)
            {
                var drift = Mathf.Abs((float)(clock.SongTime - clock.SourceTime)) * 1000f;
                if (drift < 500f) maxDriftMs = Mathf.Max(maxDriftMs, drift);
            }
        }

        void OnGUI()
        {
            if (clock == null) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 20 };
            var state = clock.IsRunning ? "播放中" : "已暂停";
            GUI.Label(new Rect(16, 16, 900, 30), $"【阶段1 · 音游时钟】{state}    空格=暂停/继续   R=重开", style);
            GUI.Label(new Rect(16, 52, 900, 30), $"歌曲时间 {clock.SongTime:F3} s    拍数 {clock.Beat:F3}    BPM {clock.Bpm:F0}", style);
            GUI.Label(new Rect(16, 88, 900, 30), $"时钟与音频位置最大偏差 {maxDriftMs:F1} ms", style);
        }
    }
}
