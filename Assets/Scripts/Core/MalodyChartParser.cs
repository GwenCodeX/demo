using System;
using System.IO;
using UnityEngine;

namespace RhythmPlayer.Core
{
    /// <summary>
    /// Malody 谱面（.mc，JSON 格式）解析器。
    /// 结构：meta（标题/曲师/背景/轨道数）+ time（BPM 表，取第一个）+ note（音符数组）。
    /// 音符：beat = [整数, 分子, 分母]，值直接是拍数（整数 + 分子/分母）；column = 0-5 列；
    ///       带 endbeat 字段的是长条。
    /// </summary>
    public static class MalodyChartParser
    {
        // ===== 与 .mc 的 JSON 结构一一对应的数据类（JsonUtility 只取用得到的字段） =====

        [Serializable] class McFile
        {
            public McMeta meta;
            public McTime[] time;
            public McNote[] note;
        }

        [Serializable] class McMeta
        {
            public McSong song;
            public McModeExt mode_ext;
            public string background;
            public string version;
            public int mode;
        }

        [Serializable] class McSong
        {
            public string title;
            public string artist;
        }

        [Serializable] class McModeExt
        {
            public int column;   // 轨道数（方块模式应为 6）
        }

        [Serializable] class McTime
        {
            public int[] beat;
            public float bpm;
        }

        [Serializable] class McNote
        {
            public int[] beat;
            public int[] endbeat;   // 长条才有
            public int column;
        }

        /// <summary>从 .mc 里读出来的歌曲信息（歌曲列表用）</summary>
        public sealed class MalodyMeta
        {
            public string Title;
            public string Artist;
            public string Version;          // 难度名（meta.version）
            public float Bpm = 120f;
            public string BackgroundFile;   // 相对谱面包目录的文件名
            public int Columns = 6;
            public int NoteCount;
        }

        /// <summary>只读歌曲信息（不解析音符，选曲列表用）</summary>
        public static MalodyMeta ReadMeta(string path)
        {
            var file = LoadJson(path);
            if (file?.meta == null) return null;

            var title = file.meta.song != null && !string.IsNullOrEmpty(file.meta.song.title)
                ? file.meta.song.title
                : Path.GetFileNameWithoutExtension(path);

            return new MalodyMeta
            {
                Title = title,
                Artist = file.meta.song?.artist,
                Version = file.meta.version,
                Bpm = ReadBpm(file),
                BackgroundFile = file.meta.background,
                Columns = file.meta.mode_ext != null && file.meta.mode_ext.column > 0 ? file.meta.mode_ext.column : 6,
                NoteCount = file.note?.Length ?? 0,
            };
        }

        /// <summary>解析成统一的谱面数据（可直接交给 Playfield 播放）</summary>
        public static ChartData ParseFile(string path)
        {
            var file = LoadJson(path);
            var data = new ChartData();
            if (file?.note == null) return data;

            foreach (var note in file.note)
            {
                var start = BeatValue(note.beat);
                var end = note.endbeat != null ? BeatValue(note.endbeat) : start;
                var lane = Mathf.Clamp(note.column, 0, 5);
                data.Notes.Add(new ChartNote(0, '-', 0, start, end, lane, false, -1));
            }

            // 按拍点排序，并把同一拍上的两个音符标记成双押
            data.Notes.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat));
            MarkDoubles(data);
            return data;
        }

        /// <summary>同一拍上的两个音符标记为双押（互为伙伴，用来画连线/换贴图）</summary>
        static void MarkDoubles(ChartData data)
        {
            for (var i = 1; i < data.Notes.Count; i++)
            {
                var previous = data.Notes[i - 1];
                var current = data.Notes[i];
                if (previous.IsDouble || current.IsDouble) continue;                       // 三连音等复杂情况不重复标记
                if (previous.Lane == current.Lane) continue;                               // 同轨不算双押
                if (Math.Abs(previous.StartBeat - current.StartBeat) > 0.001) continue;    // 拍点不同

                data.Notes[i - 1] = new ChartNote(previous.Style, previous.Side, previous.AngleDeg,
                    previous.StartBeat, previous.EndBeat, previous.Lane, true, current.Lane);
                data.Notes[i] = new ChartNote(current.Style, current.Side, current.AngleDeg,
                    current.StartBeat, current.EndBeat, current.Lane, true, previous.Lane);
            }
        }

        /// <summary>取 BPM（Malody 支持变速，这里只取第一段；多段时给出警告）</summary>
        static float ReadBpm(McFile file)
        {
            if (file.time == null || file.time.Length == 0) return 120f;
            if (file.time.Length > 1) Debug.LogWarning("[Malody] 谱面含变速，暂只使用第一个 BPM");
            var bpm = file.time[0].bpm;
            return bpm > 1f ? bpm : 120f;
        }

        /// <summary>读取并反序列化 .mc；失败返回 null</summary>
        static McFile LoadJson(string path)
        {
            try
            {
                return JsonUtility.FromJson<McFile>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogError($"[Malody] 读取 {path} 失败：{e.Message}");
                return null;
            }
        }

        /// <summary>beat = [整数, 分子, 分母] → 拍数（整数 + 分子/分母）</summary>
        static double BeatValue(int[] beat)
        {
            if (beat == null || beat.Length == 0) return 0.0;
            var value = (double)beat[0];
            if (beat.Length >= 3 && beat[2] != 0) value += (double)beat[1] / beat[2];
            return value;
        }
    }
}
