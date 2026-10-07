using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RhythmPlayer.Core
{
    /// <summary>
    /// 谱面音符。DCS（舞立方制谱器）文本谱面有两种行：
    ///   普通音符：`{键位}-{起拍}[-{止拍}]`；键位 1-6，多字符 = 双押（如 24），A-F 为另一族（语义未确认）
    ///   滑条音符：`{滑条类型 0-6}{L/R}{起始角度}-{起拍}[-{止拍}]`；L/R = 顺/逆，角度 = 起始方向
    /// Head 保留原始行首，写回时原样输出，保证读写往返一致。
    /// </summary>
    public readonly struct ChartNote
    {
        public readonly string Head;      // 原始行首（如 "24"、"A"、"1L120"）
        public readonly int Style;        // 滑条类型 0-6；普通音符恒 0
        public readonly char Side;        // 滑条 L/R；普通音符 '-'
        public readonly int AngleDeg;     // 滑条起始角度；普通音符恒 0
        public readonly double StartBeat; // 开始拍（到达判定点的时间）
        public readonly double EndBeat;   // 结束拍；单点时与 StartBeat 相同
        public readonly int Lane;         // 0-5，六条轨道
        public readonly bool IsDouble;    // 是否双押
        public readonly int PartnerLane;  // 双押伙伴轨道；非双押为 -1
        public readonly bool IsScroll;    // 是否滑条行
        public readonly bool HasEnd;      // 原始行是否写了结束拍
        public readonly int SourceIndex;  // 原始行序（写回时按它还原顺序）

        public ChartNote(string head, int style, char side, int angleDeg, double startBeat, double endBeat,
                         int lane, bool isDouble, int partnerLane, bool isScroll, bool hasEnd, int sourceIndex)
        {
            Head = head;
            Style = style;
            Side = side;
            AngleDeg = angleDeg;
            StartBeat = startBeat;
            EndBeat = endBeat;
            Lane = lane;
            IsDouble = isDouble;
            PartnerLane = partnerLane;
            IsScroll = isScroll;
            HasEnd = hasEnd;
            SourceIndex = sourceIndex;
        }

        /// <summary>是否是长条（有持续段）</summary>
        public bool IsHold => EndBeat > StartBeat;
    }

    /// <summary>一份解析完的谱面：音符列表 + 解析过程中的错误行</summary>
    public sealed class ChartData
    {
        public readonly List<ChartNote> Notes = new List<ChartNote>();
        public readonly List<string> Errors = new List<string>();

        /// <summary>长条数量（含滑条中带持续段的）</summary>
        public int HoldCount
        {
            get
            {
                var count = 0;
                foreach (var note in Notes) if (note.IsHold) count++;
                return count;
            }
        }

        /// <summary>滑条数量</summary>
        public int ScrollCount
        {
            get
            {
                var count = 0;
                foreach (var note in Notes) if (note.IsScroll) count++;
                return count;
            }
        }
    }

    /// <summary>
    /// 谱面解析 / 写回：与舞立方制谱器（DCS）的 .txt 格式一一对应。
    /// 识别规则：行首形如 `{0-6}{L|R}{纯数字}` → 滑条行；否则视为普通音符行。
    /// </summary>
    public static class ChartParser
    {
        static readonly char[] Dash = { '-' };

        /// <summary>六个外区（1-6）对应的方向角度，顺序与轨道一致（1=120°,2=180°,3=240°,4=60°,5=0°,6=300°）</summary>
        static readonly int[] ZoneAngles = { 120, 180, 240, 60, 0, 300 };
        const string BeatFormat = "0.#####";

        /// <summary>从 TextAsset 解析（编辑器里直接引用资源时用）</summary>
        public static ChartData Parse(TextAsset asset) => asset == null ? null : ParseText(asset.text);

        /// <summary>
        /// 按扩展名从磁盘装载谱面：.mc 走 Malody 解析器，其余按文本谱面解析。
        /// 读取失败时返回空谱面（并打错误日志），不会抛异常。
        /// </summary>
        public static ChartData LoadFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new ChartData();
            try
            {
                if (path.EndsWith(".mc", StringComparison.OrdinalIgnoreCase)) return MalodyChartParser.ParseFile(path);
                return ParseText(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogError($"[谱面] 读取 {path} 失败：{e.Message}");
                return new ChartData();
            }
        }

        /// <summary>从文本解析（运行时从磁盘读谱面时用）</summary>
        public static ChartData ParseText(string text)
        {
            var data = new ChartData();
            if (string.IsNullOrEmpty(text)) return data;

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var index = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (ParseLine(line, index, data)) index++;
                else data.Errors.Add($"第 {i + 1} 行无法解析：{line}");
            }

            // 按拍点排序：播放器按顺序激活音符；同拍按原始行序稳定排列
            data.Notes.Sort((a, b) =>
            {
                var byBeat = a.StartBeat.CompareTo(b.StartBeat);
                return byBeat != 0 ? byBeat : a.SourceIndex.CompareTo(b.SourceIndex);
            });
            return data;
        }

        /// <summary>
        /// 写回 DCS .txt：按原始行序输出，行首原样、数值用原始精度（最多 5 位小数）。
        /// 手工构造（无 SourceIndex）的音符排在最后。
        /// </summary>
        public static string ToText(ChartData data)
        {
            if (data == null || data.Notes.Count == 0) return string.Empty;

            var ordered = new List<ChartNote>(data.Notes);
            ordered.Sort((a, b) => a.SourceIndex.CompareTo(b.SourceIndex));

            var sb = new StringBuilder();
            var written = new HashSet<int>();
            foreach (var note in ordered)
            {
                if (!written.Add(note.SourceIndex)) continue; // 双押的两个音符共享同一行文本
                sb.Append(note.Head).Append('-').Append(Format(note.StartBeat));
                if (note.HasEnd) sb.Append('-').Append(Format(note.EndBeat));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        static string Format(double beat) => beat.ToString(BeatFormat, CultureInfo.InvariantCulture);

        /// <summary>解析一行，失败返回 false（错误行会被收集起来展示）</summary>
        static bool ParseLine(string line, int index, ChartData data)
        {
            var parts = line.Split(Dash);
            if (parts.Length < 2 || parts[0].Length == 0) return false;

            var head = parts[0];
            if (IsScrollHead(head)) return ParseScrollLine(head, parts, index, data);
            return ParseSampleLine(head, parts, index, data);
        }

        /// <summary>滑条行首：`{0-6}{L|R}{纯数字角度}`</summary>
        static bool IsScrollHead(string head)
        {
            if (head.Length < 3) return false;
            if (head[0] < '0' || head[0] > '6') return false;
            if (head[1] != 'L' && head[1] != 'R') return false;
            return IsAllDigits(head, 2, head.Length);
        }

        /// <summary>滑条行：`{类型 0-6}{L/R}{角度}-{起拍}[-{止拍}]`</summary>
        static bool ParseScrollLine(string head, string[] parts, int index, ChartData data)
        {
            if (!int.TryParse(head.Substring(0, 1), out var style)) return false;
            if (!int.TryParse(head.Substring(2), out var angle)) return false;
            if (!TryParseBeats(parts, out var start, out var end, out var hasEnd)) return false;

            data.Notes.Add(new ChartNote(head, style, head[1], angle, start, end,
                LandingLane(style, head[1], angle), false, -1, true, hasEnd, index));
            return true;
        }

        /// <summary>普通音符行：`{键位}-{起拍}[-{止拍}]`；键位 1-6（多字符 = 双押），A-F 为另一族</summary>
        static bool ParseSampleLine(string head, string[] parts, int index, ChartData data)
        {
            if (head.Length == 0) return false;
            if (!TryParseBeats(parts, out var start, out var end, out var hasEnd)) return false;

            var lanes = new int[head.Length];
            for (var i = 0; i < head.Length; i++)
            {
                var lane = LaneOf(head[i]);
                if (lane < 0) return false;
                lanes[i] = lane;
            }

            var isDouble = lanes.Length == 2;
            for (var i = 0; i < lanes.Length; i++)
            {
                var partner = isDouble ? lanes[1 - i] : -1; // 双押时互相记录伙伴轨道（画连线用）
                data.Notes.Add(new ChartNote(head, 0, '-', 0, start, end,
                    lanes[i], isDouble, partner, false, hasEnd, index));
            }
            return true;
        }

        /// <summary>
        /// 滑条落点区：`{起点区 1-6}{L 逆 / R 顺 / LO 直}{旋转量}`，
        /// 落点 = 起点区方向 ± 旋转量（L 逆时针增加、R 顺时针减小）；n0 视为"从中心出发"，角度即落点方向。
        /// </summary>
        static int LandingLane(int style, char side, int angleDeg)
        {
            if (style <= 0) return LaneOfAngle(angleDeg);

            var startAngle = ZoneAngles[Mathf.Clamp(style - 1, 0, 5)];
            var steps = angleDeg / 60;
            if (side == 'R') steps = -steps;
            else if (side != 'L') steps = 0; // LO = 直：不旋转
            return LaneOfAngle(startAngle + steps * 60);
        }

        /// <summary>方向角度 → 最近的区（0-5）</summary>
        static int LaneOfAngle(int angleDeg)
        {
            var best = 0;
            var bestDelta = float.MaxValue;
            for (var i = 0; i < ZoneAngles.Length; i++)
            {
                var delta = Mathf.Abs(Mathf.DeltaAngle(angleDeg, ZoneAngles[i]));
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>键位字符 → 轨道：1-6 → 0-5；A-F → 0-5（另一族，语义未确认）</summary>
        static int LaneOf(char c)
        {
            if (c >= '1' && c <= '6') return c - '1';
            if (c >= 'A' && c <= 'F') return c - 'A';
            return -1;
        }

        static bool TryParseBeats(string[] parts, out double start, out double end, out bool hasEnd)
        {
            end = 0.0;
            hasEnd = false;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out start)) return false;
            end = start;
            if (parts.Length >= 3)
            {
                if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out end)) return false;
                hasEnd = true;
            }
            return true;
        }

        /// <summary>指定区间是否全是数字</summary>
        static bool IsAllDigits(string s, int from, int to)
        {
            for (var i = from; i < to; i++)
            {
                if (s[i] < '0' || s[i] > '9') return false;
            }
            return true;
        }
    }
}
