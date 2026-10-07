using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RhythmPlayer.Core
{
    /// <summary>编译后的运行时音符：秒为单位、纯数值结构，播放器每帧直接使用</summary>
    public struct RuntimeNote
    {
        public float StartSeconds;
        public float EndSeconds;
        public byte Lane;
        public bool IsHold;
        public bool IsDouble;
        public sbyte PartnerLane;
        public bool IsScroll;
        public byte ScrollType;
        public short ScrollAngle;
        public bool ScrollRight;
    }

    /// <summary>编译后的谱面：按开始时间排序的运行时音符 + 统计</summary>
    public sealed class CompiledChart
    {
        public RuntimeNote[] Notes;
        public int HoldCount;
        public int ScrollCount;
        public int TotalNotes => Notes != null ? Notes.Length : 0;
    }

    /// <summary>
    /// 谱面编译器 + 缓存：
    /// 谱面文件只在首次使用时解析一次，并"编译"成秒单位的运行时数组；
    /// 之后试听、切难度、重打全部命中缓存，不再读取和解析文件。
    /// 文件被替换（大小或修改时间变化）时会自动失效并重新编译。
    /// </summary>
    public static class ChartCompiler
    {
        sealed class CacheEntry
        {
            public CompiledChart Chart;
            public long Size;
            public long Ticks;
        }

        static readonly Dictionary<string, CacheEntry> cache = new Dictionary<string, CacheEntry>();

        /// <summary>取编译后的谱面（命中缓存则零开销）</summary>
        public static CompiledChart Get(string chartPath, float firstTimeOffset, float secondsPerBeat)
        {
            if (string.IsNullOrEmpty(chartPath) || !File.Exists(chartPath)) return null;

            var info = new FileInfo(chartPath);
            if (cache.TryGetValue(chartPath, out var entry) &&
                entry.Size == info.Length && entry.Ticks == info.LastWriteTimeUtc.Ticks)
            {
                return entry.Chart;
            }

            var data = ChartParser.LoadFile(chartPath);
            if (data == null) return null;

            var compiled = Compile(data, firstTimeOffset, secondsPerBeat);
            cache[chartPath] = new CacheEntry { Chart = compiled, Size = info.Length, Ticks = info.LastWriteTimeUtc.Ticks };
            return compiled;
        }

        /// <summary>清空缓存（删除歌曲 / 重新导入后调用）</summary>
        public static void Clear()
        {
            cache.Clear();
        }

        /// <summary>指定路径的缓存失效（同名文件重新导入等场景）</summary>
        public static void Invalidate(string chartPath)
        {
            if (!string.IsNullOrEmpty(chartPath)) cache.Remove(chartPath);
        }

        static CompiledChart Compile(ChartData data, float firstTimeOffset, float secondsPerBeat)
        {
            var notes = new RuntimeNote[data.Notes.Count];
            var holdCount = 0;
            var scrollCount = 0;

            for (var i = 0; i < data.Notes.Count; i++)
            {
                var note = data.Notes[i];
                if (note.IsHold) holdCount++;
                if (note.IsScroll) scrollCount++;
                notes[i] = new RuntimeNote
                {
                    StartSeconds = firstTimeOffset + (float)note.StartBeat * secondsPerBeat,
                    EndSeconds = firstTimeOffset + (float)note.EndBeat * secondsPerBeat,
                    Lane = (byte)Mathf.Clamp(note.Lane, 0, 5),
                    IsHold = note.IsHold,
                    IsDouble = note.IsDouble,
                    PartnerLane = (sbyte)Mathf.Clamp(note.PartnerLane, -1, 5),
                    IsScroll = note.IsScroll,
                    ScrollType = (byte)Mathf.Clamp(note.Style, 0, 6),
                    ScrollAngle = (short)note.AngleDeg,
                    ScrollRight = note.Side == 'R',
                };
            }

            return new CompiledChart { Notes = notes, HoldCount = holdCount, ScrollCount = scrollCount };
        }
    }
}
