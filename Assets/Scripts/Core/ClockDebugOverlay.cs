using UnityEngine;
using RhythmPlayer.UI;

namespace RhythmPlayer.Core
{
    /// <summary>
    /// 左上角调试面板（简洁科技风）：显示歌曲时间、拍数、时钟与音频位置的最大偏差（越小越同步）。
    /// 快捷键：空格 = 暂停/继续；R = 从头重开；Esc = 返回选曲（GameRoot 处理）。
    /// </summary>
    [RequireComponent(typeof(SongClock))]
    public sealed class ClockDebugOverlay : MonoBehaviour
    {
        [SerializeField] SongClock clock;

        float maxDriftMs;   // 时钟与音频位置的最大偏差（毫秒）
        GUIStyle panelStyle;
        GUIStyle textStyle;

        void Awake()
        {
            if (clock == null) clock = GetComponent<SongClock>();
        }

        void Update()
        {
            if (clock == null) return;

            if (Input.GetKeyDown(KeyCode.Space)) clock.TogglePlayPause();
            if (Input.GetKeyDown(KeyCode.R)) clock.PlayFrom(0.0);

            // 播放稳定后再统计偏差（忽略起播瞬间的无效读数）
            if (clock.IsRunning && clock.SourceTime > 0.1)
            {
                var drift = Mathf.Abs((float)(clock.SongTime - clock.SourceTime)) * 1000f;
                if (drift < 500f) maxDriftMs = Mathf.Max(maxDriftMs, drift);
            }
        }

        void OnGUI()
        {
            if (clock == null) return;

            panelStyle ??= UiTheme.PanelStyle();
            textStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                normal = { textColor = UiTheme.TextDim },
            };

            // 圆角半透明面板 + 三行信息
            GUI.Box(new Rect(14f, 14f, 470f, 92f), GUIContent.none, panelStyle);
            var state = clock.IsRunning ? "播放中" : "已暂停";
            GUI.Label(new Rect(30f, 22f, 440f, 22f), $"时钟 {state}     空格 暂停/继续     R 重开     Esc 选曲", textStyle);
            GUI.Label(new Rect(30f, 46f, 440f, 22f), $"歌曲时间 {clock.SongTime:F3} s     拍数 {clock.Beat:F2}     BPM {clock.Bpm:F0}", textStyle);
            GUI.Label(new Rect(30f, 70f, 440f, 22f), $"时钟偏差 {maxDriftMs:F1} ms", textStyle);
        }
    }
}
