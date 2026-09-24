using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RhythmPlayer.Core
{
    /// 谱面音符：文本行格式 `{首位}{L/R}{角度}-{开始拍}[-{结束拍}]`
    /// 经数据分析确认：首位 0 = 单点（无时长），1-6 = 长条（有起止）。
    public readonly struct ChartNote
    {
        public readonly int Style;      // 首位数字：0 = 单点，1-6 = 长条（具体语义待确认，先原样保留）
        public readonly char Side;      // L / R
        public readonly int AngleDeg;   // 0 / 60 / ... / 360（每 60° 一档）
        public readonly double StartBeat;
        public readonly double EndBeat; // 单点时与 StartBeat 相同

        public ChartNote(int style, char side, int angleDeg, double startBeat, double endBeat)
        {
            Style = style;
            Side = side;
            AngleDeg = angleDeg;
            StartBeat = startBeat;
            EndBeat = endBeat;
        }

        public bool IsHold => EndBeat > StartBeat;

        /// 轨道 = 角度 / 60（0° 与 360° 同轨），共 6 轨。语义暂定，后续可换映射。
        public int Lane => (AngleDeg / 60) % 6;
    }

    public sealed class ChartData
    {
        public readonly List<ChartNote> Notes = new List<ChartNote>();
        public readonly List<string> Errors = new List<string>();

        public int HoldCount
        {
            get
            {
                var count = 0;
                foreach (var note in Notes) if (note.IsHold) count++;
                return count;
            }
        }
    }

    public static class ChartParser
    {
        public static ChartData Parse(TextAsset asset) => asset == null ? null : ParseText(asset.text);

        public static ChartData ParseText(string text)
        {
            var data = new ChartData();
            if (string.IsNullOrEmpty(text)) return data;

            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;

                var parts = line.Split('-');
                if (parts.Length < 2 || parts[0].Length < 3)
                {
                    data.Errors.Add($"第 {i + 1} 行结构异常：{line}");
                    continue;
                }

                var head = parts[0];
                if (!int.TryParse(head.Substring(0, 1), out var style) ||
                    !int.TryParse(head.Substring(2), out var angle) ||
                    !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var start))
                {
                    data.Errors.Add($"第 {i + 1} 行数字格式错误：{line}");
                    continue;
                }

                var end = start;
                if (parts.Length >= 3 && !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out end))
                {
                    data.Errors.Add($"第 {i + 1} 行结束时间错误：{line}");
                    continue;
                }

                data.Notes.Add(new ChartNote(style, head[1], angle, start, end));
            }

            data.Notes.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat));
            return data;
        }
    }
}
