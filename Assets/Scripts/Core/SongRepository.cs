using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RhythmPlayer.Core
{
    /// <summary>
    /// 一首歌的完整信息。一个歌曲文件夹 = 一份"歌曲包"。
    /// 歌曲包内需要的文件：音频（audio.mp3 / .ogg / .wav）+ 谱面（*.txt）+ 可选的 musicInfo.json、bg.jpg。
    /// </summary>
    public sealed class SongInfo
    {
        /// <summary>歌曲文件夹的绝对路径</summary>
        public string Folder;

        /// <summary>歌曲名（优先取 musicInfo.json 的 name，缺省用文件夹名）</summary>
        public string Name;

        /// <summary>曲师（musicInfo.json 的 autor）</summary>
        public string Artist;

        /// <summary>每分钟拍数（musicInfo.json 的 bpm，缺省 120）</summary>
        public float Bpm = 120f;

        /// <summary>第 0 拍对应的歌曲时间偏移（秒，musicInfo.json 的 firstTime）</summary>
        public float FirstTime;

        /// <summary>音频文件路径（.mp3 / .ogg / .wav）</summary>
        public string AudioPath;

        /// <summary>谱面文本路径（.txt）</summary>
        public string ChartPath;

        /// <summary>背景图路径（bg.jpg / bg.png，可为空）</summary>
        public string BackgroundPath;
    }

    /// <summary>
    /// 歌曲仓库：扫描歌曲根目录，把每个子文件夹当作一首歌"导入"进来。
    /// 根目录规则：
    ///   - 编辑器里：Assets/Songs（把歌曲文件夹放进这里）
    ///   - 打包后：exe 同级的 Songs 目录（把歌曲文件夹放进去即完成导入）
    /// </summary>
    public static class SongRepository
    {
        /// <summary>
        /// musicInfo.json 的数据结构。
        /// 只声明用得到的字段，JsonUtility 会自动忽略其余字段（uuid/bpmList 等）。
        /// 注意：格式里曲师字段的拼写就是 "autor"。
        /// </summary>
        [Serializable]
        class MusicInfoJson
        {
            public string name;
            public string autor;
            public float bpm;
            public float firstTime;
        }

        /// <summary>歌曲根目录（编辑器与打包后自动区分）</summary>
        public static string Root
        {
            get
            {
                // 编辑器：Application.dataPath 就是工程里的 Assets 目录
                var inAssets = Path.Combine(Application.dataPath, "Songs");
                if (Directory.Exists(inAssets)) return inAssets;

                // 打包后：Application.dataPath 是 xxx_Data，取它的上级（即 exe 所在目录）再拼 Songs
                var parent = Directory.GetParent(Application.dataPath);
                return Path.Combine(parent != null ? parent.FullName : Application.dataPath, "Songs");
            }
        }

        /// <summary>
        /// 扫描所有歌曲。按文件夹名排序，保证每次启动的列表顺序一致。
        /// 缺少音频或谱面的文件夹会被跳过并打印警告。
        /// </summary>
        public static List<SongInfo> Scan()
        {
            var songs = new List<SongInfo>();
            var root = Root;
            if (!Directory.Exists(root)) return songs;

            foreach (var folder in Directory.GetDirectories(root))
            {
                var song = LoadSong(folder);
                if (song != null) songs.Add(song);
            }

            songs.Sort((a, b) => string.Compare(
                Path.GetFileName(a.Folder), Path.GetFileName(b.Folder), StringComparison.Ordinal));
            return songs;
        }

        /// <summary>读取单个歌曲文件夹；缺少音频或谱面时返回 null 并给出警告</summary>
        static SongInfo LoadSong(string folder)
        {
            // 音频：优先固定名 audio.*，其次任意同后缀文件
            var audio = FindFirst(folder, new[] { "audio.mp3", "audio.ogg", "audio.wav" })
                        ?? FindByExtension(folder, new[] { ".mp3", ".ogg", ".wav" });
            if (audio == null)
            {
                Debug.LogWarning($"[歌曲] {folder} 没有音频文件，跳过");
                return null;
            }

            var chart = FindChart(folder);
            if (chart == null)
            {
                Debug.LogWarning($"[歌曲] {folder} 没有谱面 .txt，跳过");
                return null;
            }

            var song = new SongInfo
            {
                Folder = folder,
                Name = Path.GetFileName(folder),
                AudioPath = audio,
                ChartPath = chart,
            };

            // 读取 musicInfo.json（可选）：歌名 / 曲师 / BPM / 拍偏移
            var infoPath = Path.Combine(folder, "musicInfo.json");
            if (File.Exists(infoPath))
            {
                try
                {
                    var info = JsonUtility.FromJson<MusicInfoJson>(File.ReadAllText(infoPath));
                    if (info != null)
                    {
                        if (!string.IsNullOrEmpty(info.name)) song.Name = info.name;
                        if (!string.IsNullOrEmpty(info.autor)) song.Artist = info.autor;
                        if (info.bpm > 1f) song.Bpm = info.bpm;
                        song.FirstTime = info.firstTime;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[歌曲] {folder}/musicInfo.json 解析失败：{e.Message}");
                }
            }

            song.BackgroundPath = FindFirst(folder, new[] { "bg.jpg", "bg.png" });
            return song;
        }

        /// <summary>谱面文件：取文件夹里最大的 .txt（排除名字含"信息"的投稿说明文件）</summary>
        static string FindChart(string folder)
        {
            string best = null;
            foreach (var file in Directory.GetFiles(folder, "*.txt"))
            {
                if (Path.GetFileName(file).Contains("信息")) continue;
                if (best == null || new FileInfo(file).Length > new FileInfo(best).Length) best = file;
            }
            return best;
        }

        /// <summary>按候选文件名依次查找（如 audio.mp3 → audio.ogg → audio.wav）</summary>
        static string FindFirst(string folder, string[] names)
        {
            foreach (var name in names)
            {
                var path = Path.Combine(folder, name);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>按扩展名查找第一个文件</summary>
        static string FindByExtension(string folder, string[] extensions)
        {
            foreach (var extension in extensions)
            {
                var files = Directory.GetFiles(folder, "*" + extension);
                if (files.Length > 0) return files[0];
            }
            return null;
        }
    }
}
