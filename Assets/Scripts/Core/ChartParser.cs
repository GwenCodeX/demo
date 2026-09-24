using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RhythmPlayer.Core
{
    /// <summary>
    /// 谱面音符。支持两种文本格式：
    /// 新格式（DCS 制谱器导出，如 C017.txt）：`{键位}-{开始拍}[-{结束拍}]`
    ///   键位 1-6 = 六个键；两位数如 24 = 双押（键 2+键 4 同时）。
    /// 旧格式（如 E119.txt）：`{首位}{L/R}{角度}-{开始拍}[-{结束拍}]`
    ///   首位 0 = 单点 / 1-6 = 长条，轨道暂按 角度/60 映射（语义未确认，可调）。
    /// </summary>
    public readonly struct ChartNote
    {
        public readonly int Style;        // 旧格式首位数字；新格式恒 0
        public readonly char Side;        // 旧格式 L/R；新格式 '-'
        public readonly int AngleDeg;     // 旧格式角度；新格式恒 0
        public readonly double StartBeat; // 开始拍（到达判定点的时间）
        public readonly double EndBeat;   // 结束拍；单点时与 StartBeat 相同
        public readonly int Lane;         // 0-5，六条轨道
        public readonly bool IsDouble;    // 是否双押
        public readonly int PartnerLane;  // 双押伙伴轨道；非双押为 -1

        public ChartNote(int style, char side, int angleDeg, double startBeat, double endBeat, int lane, bool isDouble, int partnerLane)
        {
            Style = style;
            Side = side;
            AngleDeg = angleDeg;
            StartBeat = startBeat;
            EndBeat = endBeat;
            Lane = lane;
            IsDouble = isDouble;
            PartnerLane = partnerLane;
        }

        /// <summary>是否是长条（有持续段）</summary>
        public bool IsHold => EndBeat > StartBeat;
    }

    /// <summary>一份解析完的谱面：音符列表 + 解析过程中的错误行</summary>
    public sealed class ChartData
    {
        public readonly List<ChartNote> Notes = new List<ChartNote>();
        public readonly List<string> Errors = new List<string>();

        /// <summary>长条数量</summary>
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

    /// <summary>
    /// 谱面解析器：把文本谱面转成音符列表（按拍点排序）。
    /// 两种格式自动识别：行首是 1-2 位纯数字 → 新格式；否则按旧格式解析。
    /// </summary>
    public static class ChartParser
    {
        static readonly char[] Dash = { '-' };

        /// <summary>从 TextAsset 解析（编辑器里直接引用资源时用）</summary>
        public static ChartData Parse(TextAsset asset) => asset == null ? null : ParseText(asset.text);

        /// <summary>从文本解析（运行时从磁盘读谱面时用）</summary>
        public static ChartData ParseText(string text)
        {
            var data = new ChartData();
            if (string.IsNullOrEmpty(text)) return data;

            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (!ParseLine(line, data)) data.Errors.Add($"第 {i + 1} 行无法解析：{line}");
            }

            // 按拍点排序：播放器按顺序激活音符
            data.Notes.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat));
            return data;
        }

        /// <summary>解析一行，失败返回 false（错误行会被收集起来展示）</summary>
        static bool ParseLine(string line, ChartData data)
        {
            var parts = line.Split(Dash);
            if (parts.Length < 2 || parts[0].Length == 0) return false;

            var head = parts[0];
            if (head.Length <= 2 && IsAllDigits(head)) return ParseKeyLine(head, parts, data);
            return ParseLegacyLine(head, parts, data);
        }

        /// <summary>新格式：`{键位}-{开始拍}[-{结束拍}]`；两位数键位 = 双押</summary>
        static bool ParseKeyLine(string keys, string[] parts, ChartData data)
        {
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var start)) return false;

            var end = start;
            if (parts.Length >= 3 && !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out end)) return false;

            // 把每个数字字符翻成轨道下标（键位 1-6 → 轨道 0-5）
            var lanes = new int[keys.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i] - '0';
                if (key < 1 || key > 6) return false;
                lanes[i] = key - 1;
            }

            var isDouble = lanes.Length == 2;
            for (var i = 0; i < lanes.Length; i++)
            {
                var partner = isDouble ? lanes[1 - i] : -1; // 双押时互相记录伙伴轨道（画连线用）
                data.Notes.Add(new ChartNote(0, '-', 0, start, end, lanes[i], isDouble, partner));
            }
            return true;
        }

        /// <summary>旧格式：`{首位}{L/R}{角度}-{开始拍}[-{结束拍}]`</summary>
        static bool ParseLegacyLine(string head, string[] parts, ChartData data)
        {
            if (head.Length < 3) return false;
            if (!int.TryParse(head.Substring(0, 1), out var style) ||
                !int.TryParse(head.Substring(2), out var angle) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var start)) return false;

            var end = start;
            if (parts.Length >= 3 && !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out end)) return false;

            data.Notes.Add(new ChartNote(style, head[1], angle, start, end, angle / 60 % 6, false, -1));
            return true;
        }

        /// <summary>整串是否全是数字（用来识别新格式的键位头）</summary>
        static bool IsAllDigits(string s)
        {
            foreach (var c in s)
            {
                if (c < '0' || c > '9') return false;
            }
            return true;
        }
    }
}
