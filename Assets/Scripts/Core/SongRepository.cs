using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace RhythmPlayer.Core
{
    /// <summary>
    /// 一首歌的完整信息。一个"谱面包"对应一首歌（Malody 包里的每个 .mc 难度各算一首）。
    /// </summary>
    public sealed class SongInfo
    {
        /// <summary>歌曲文件夹的绝对路径</summary>
        public string Folder;

        /// <summary>歌曲名（文本包取 musicInfo.json 的 name；Malody 包取 .mc 的标题）</summary>
        public string Name;

        /// <summary>曲师</summary>
        public string Artist;

        /// <summary>每分钟拍数</summary>
        public float Bpm = 120f;

        /// <summary>第 0 拍对应的歌曲时间偏移（秒）</summary>
        public float FirstTime;

        /// <summary>音频文件路径（.mp3 / .ogg / .wav）</summary>
        public string AudioPath;

        /// <summary>谱面文件路径（.txt 文本谱面 或 .mc Malody 谱面）</summary>
        public string ChartPath;

        /// <summary>背景图路径（可为空）</summary>
        public string BackgroundPath;

        /// <summary>音符总数（扫描时顺带解析得到；0 = 未知）</summary>
        public int NoteCount;
    }

    /// <summary>
    /// 歌曲仓库：扫描歌曲根目录，把每个可识别的谱面包"导入"进来。支持三种形态：
    ///   1. 普通歌曲文件夹：音频 + 文本谱面（*.txt）+ 可选 musicInfo.json / bg.jpg
    ///   2. Malody 谱面包文件夹：.mc（JSON 谱面）+ 音频（.ogg/.mp3/.wav）；一个文件夹里多个 .mc = 多个难度各算一首
    ///   3. 压缩包：.zip / .mcz（mcz 是 Malody 的打包格式，本质就是 zip），自动解压后按上面两种处理
    /// 根目录规则：编辑器里是 Assets/Songs；打包后是 exe 同级的 Songs 目录（把文件放进去即完成导入）。
    /// </summary>
    public static class SongRepository
    {
        const string UnpackedFolderName = "_unpacked"; // 打包版放解压缓存的子目录名

        /// <summary>musicInfo.json 的数据结构（只声明用得到的字段，JsonUtility 自动忽略其余）</summary>
        [Serializable]
        class MusicInfoJson
        {
            public string name;
            public string autor;    // 注意：格式里曲师字段的拼写就是 autor
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
        /// 解压缓存目录：
        ///   打包版放在 Songs/_unpacked（跟着游戏走，方便查看）
        ///   编辑器里放到 persistentDataPath（如果放在 Assets 下会污染工程）
        /// </summary>
        static string UnpackRoot
        {
            get
            {
                if (Application.isEditor) return Path.Combine(Application.persistentDataPath, "SongUnpacked");
                return Path.Combine(Root, UnpackedFolderName);
            }
        }

        /// <summary>扫描所有歌曲。顺序稳定：先按文件夹名、再按歌名排序。</summary>
        public static List<SongInfo> Scan()
        {
            var songs = new List<SongInfo>();
            var root = Root;
            if (!Directory.Exists(root)) return songs;

            // 1. 先把根目录下的 .zip / .mcz 压缩包解压到缓存目录
            foreach (var pattern in new[] { "*.zip", "*.mcz" })
            {
                foreach (var archive in Directory.GetFiles(root, pattern))
                {
                    UnpackArchive(archive);
                }
            }

            // 2. 扫描普通歌曲文件夹（跳过缓存目录）
            foreach (var folder in Directory.GetDirectories(root))
            {
                if (string.Equals(Path.GetFileName(folder), UnpackedFolderName, StringComparison.OrdinalIgnoreCase)) continue;
                CollectSongs(folder, songs, 0);
            }

            // 3. 扫描解压出来的内容
            var unpackRoot = UnpackRoot;
            if (Directory.Exists(unpackRoot))
            {
                foreach (var folder in Directory.GetDirectories(unpackRoot))
                {
                    CollectSongs(folder, songs, 0);
                }
            }

            songs.Sort((a, b) =>
            {
                var byFolder = string.Compare(Path.GetFileName(a.Folder), Path.GetFileName(b.Folder), StringComparison.Ordinal);
                return byFolder != 0 ? byFolder : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });
            return songs;
        }

        /// <summary>在一个文件夹里找歌；自身不是歌就看下一层（压缩包解出的外层目录 / 合集包）</summary>
        static void CollectSongs(string folder, List<SongInfo> songs, int depth)
        {
            // Malody 谱面包：文件夹里直接有 .mc
            var mcFiles = Directory.GetFiles(folder, "*.mc");
            if (mcFiles.Length > 0)
            {
                Array.Sort(mcFiles, StringComparer.Ordinal);
                foreach (var mc in mcFiles)
                {
                    var song = LoadMalodySong(folder, mc);
                    if (song != null) songs.Add(song);
                }
                return;
            }

            // 普通歌曲包
            var textSong = LoadTextSong(folder);
            if (textSong != null)
            {
                songs.Add(textSong);
                return;
            }

            // 都不是：再往下找一层
            if (depth >= 2) return;
            foreach (var sub in Directory.GetDirectories(folder))
            {
                CollectSongs(sub, songs, depth + 1);
            }
        }

        /// <summary>Malody 谱面包：标题/BPM/背景来自 .mc，音频优先找与 .mc 同名的文件</summary>
        static SongInfo LoadMalodySong(string folder, string mcPath)
        {
            var meta = MalodyChartParser.ReadMeta(mcPath);
            if (meta == null) return null;

            var baseName = Path.GetFileNameWithoutExtension(mcPath);
            var audio = FindFirst(folder, new[] { baseName + ".ogg", baseName + ".mp3", baseName + ".wav" })
                        ?? FindFirst(folder, new[] { "audio.ogg", "audio.mp3", "audio.wav" })
                        ?? FindByExtension(folder, new[] { ".ogg", ".mp3", ".wav" });
            if (audio == null)
            {
                Debug.LogWarning($"[歌曲] Malody 谱面 {mcPath} 缺少音频，跳过");
                return null;
            }

            var name = meta.Title;
            if (!string.IsNullOrEmpty(meta.Version)) name += " [" + meta.Version + "]";

            var song = new SongInfo
            {
                Folder = folder,
                Name = name,
                Artist = meta.Artist,
                Bpm = meta.Bpm,
                AudioPath = audio,
                ChartPath = mcPath,
                NoteCount = meta.NoteCount,
            };

            if (!string.IsNullOrEmpty(meta.BackgroundFile))
            {
                var background = Path.Combine(folder, meta.BackgroundFile);
                if (File.Exists(background)) song.BackgroundPath = background;
            }
            return song;
        }

        /// <summary>普通歌曲包：音频 + 最大的 .txt 谱面 + 可选 musicInfo.json</summary>
        static SongInfo LoadTextSong(string folder)
        {
            var audio = FindFirst(folder, new[] { "audio.mp3", "audio.ogg", "audio.wav" })
                        ?? FindByExtension(folder, new[] { ".mp3", ".ogg", ".wav" });
            if (audio == null)
            {
                // 连音频都没有 → 这多半只是个普通文件夹，安静跳过
                if (Directory.GetFiles(folder, "musicInfo.json").Length == 0) return null;
                Debug.LogWarning($"[歌曲] {folder} 有 musicInfo.json 但没有音频，跳过");
                return null;
            }

            var chart = FindChart(folder);
            if (chart == null)
            {
                Debug.LogWarning($"[歌曲] {folder} 没有谱面（.txt 或 .mc），跳过");
                return null;
            }

            var song = new SongInfo
            {
                Folder = folder,
                Name = Path.GetFileName(folder),
                AudioPath = audio,
                ChartPath = chart,
            };

            // musicInfo.json（可选）：歌名 / 曲师 / BPM / 拍偏移
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
            song.NoteCount = ChartParser.LoadFile(chart).Notes.Count; // 顺带统计音符数（选曲列表展示用）
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

        /// <summary>解压 zip/mcz 到缓存目录（已解压过则跳过；中文压缩包用 GBK 编码兜底）</summary>
        static void UnpackArchive(string archivePath)
        {
            var target = Path.Combine(UnpackRoot, Path.GetFileNameWithoutExtension(archivePath));
            if (Directory.Exists(target)) return; // 已缓存

            try
            {
                Directory.CreateDirectory(target);
                try
                {
                    ExtractZip(archivePath, target, null); // 先按 zip 标准（UTF-8）解
                }
                catch
                {
                    // 失败多半是中文（GBK）文件名 → 清掉重来，换 GBK 再解一遍
                    DeleteDirectory(target);
                    Directory.CreateDirectory(target);
                    ExtractZip(archivePath, target, Encoding.GetEncoding("GBK"));
                }
                Debug.Log($"[导入] 已解压 {Path.GetFileName(archivePath)} → {target}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[导入] 解压 {Path.GetFileName(archivePath)} 失败：{e.Message}");
                DeleteDirectory(target);
            }
        }

        /// <summary>逐条手动解压：便于控制文件名编码，同时防止"路径穿越"</summary>
        static void ExtractZip(string archivePath, string target, Encoding encoding)
        {
            var targetFull = Path.GetFullPath(target);
            using (var stream = File.OpenRead(archivePath))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, false, encoding))
            {
                foreach (var entry in zip.Entries)
                {
                    var destination = Path.GetFullPath(Path.Combine(targetFull, entry.FullName));
                    // 防路径穿越：解压目标必须仍在缓存目录内
                    if (!destination.StartsWith(targetFull, StringComparison.OrdinalIgnoreCase)) continue;

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destination); // 目录项
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? targetFull);
                    entry.ExtractToFile(destination, true);
                }
            }
        }

        /// <summary>尽力删除目录（失败忽略）</summary>
        static void DeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch
            {
                // 清理失败不影响主流程
            }
        }
    }
}
