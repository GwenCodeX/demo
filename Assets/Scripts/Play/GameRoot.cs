using System.Collections;
using System.Collections.Generic;
using System.IO;
using RhythmPlayer.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace RhythmPlayer.Play
{
    /// <summary>
    /// 游戏入口：负责"选曲界面 ⇄ 游玩"的状态切换。
    /// - 启动时扫描歌曲目录（Songs/），列出所有已导入的歌曲
    /// - 选曲：数字键 1-9 / 鼠标或触摸点击列表项
    /// - 选曲后从磁盘加载音频（UnityWebRequest，支持玩家自己放歌进来），再让 Playfield 解析谱面
    /// - 游玩中按 Esc（或整曲播完）自动回到选曲界面
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        /// <summary>界面状态</summary>
        enum State
        {
            SongSelect, // 选曲界面
            Loading,    // 正在加载歌曲（音频解码）
            Playing,    // 游玩中
        }

        [Header("引用")]
        [SerializeField] Playfield playfield;
        [SerializeField] SongClock clock;

        readonly List<SongInfo> songs = new List<SongInfo>();
        State state = State.SongSelect;
        string loadingText = "";
        GUIStyle titleStyle;
        GUIStyle rowStyle;
        GUIStyle hintStyle;

        void Start()
        {
            // 引用缺省时自动在场景里找
            if (playfield == null) playfield = FindObjectOfType<Playfield>();
            if (clock == null) clock = FindObjectOfType<SongClock>();

            RefreshSongs();
            EnterSongSelect();
        }

        /// <summary>重新扫描歌曲目录（歌曲包放进 Songs/ 后重启即可出现）</summary>
        void RefreshSongs()
        {
            songs.Clear();
            songs.AddRange(SongRepository.Scan());
            Debug.Log($"[选曲] 已导入 {songs.Count} 首歌曲（目录:{SongRepository.Root}）");
        }

        /// <summary>回到选曲界面：停止播放并清空音符</summary>
        void EnterSongSelect()
        {
            state = State.SongSelect;
            loadingText = "";
            if (clock != null) clock.Stop();
            if (playfield != null) playfield.ClearForSelect();
        }

        void Update()
        {
            if (state == State.SongSelect)
            {
                // 数字键 1-9 直接选歌（列表最多显示 9 项）
                for (var i = 0; i < songs.Count && i < 9; i++)
                {
                    if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)))
                    {
                        StartSong(i);
                        return;
                    }
                }
            }
            else if (state == State.Playing)
            {
                // Esc 返回选曲；整曲播完也自动回选曲
                if (Input.GetKeyDown(KeyCode.Escape)) EnterSongSelect();
                else if (clock != null && clock.IsFinished) EnterSongSelect();
            }
        }

        /// <summary>开始播放第 index 首歌（鼠标/触摸点击与数字键都会走到这里）</summary>
        public void StartSong(int index)
        {
            if (state == State.Loading || index < 0 || index >= songs.Count) return;
            StartCoroutine(LoadAndPlay(songs[index]));
        }

        /// <summary>异步加载音频并开始游玩</summary>
        IEnumerator LoadAndPlay(SongInfo song)
        {
            state = State.Loading;
            loadingText = $"正在加载「{song.Name}」……";
            if (clock != null) clock.Stop();
            if (playfield != null) playfield.ClearForSelect();

            // UnityWebRequest 读取本地文件需要 file:/// 前缀，并且路径用正斜杠
            var url = "file:///" + song.AudioPath.Replace('\\', '/');
            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, GuessAudioType(song.AudioPath)))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    loadingText = $"音频加载失败：{request.error}";
                    Debug.LogError("[选曲] " + loadingText);
                    state = State.SongSelect;
                    yield break;
                }

                var clip = DownloadHandlerAudioClip.GetContent(request);
                loadingText = "";
                clock.LoadClip(clip, song.Bpm, song.FirstTime); // 换音频 + BPM + 拍偏移
                playfield.LoadSong(song);                        // 解析谱面、换背景、清场
                clock.PlayFrom(0.0);                             // 从头开始播放
                state = State.Playing;
            }
        }

        /// <summary>按扩展名猜测音频编码类型</summary>
        static AudioType GuessAudioType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".ogg": return AudioType.OGGVORBIS;
                case ".wav": return AudioType.WAV;
                default: return AudioType.MPEG;
            }
        }

        void OnGUI()
        {
            // 游玩中有自己的 HUD，这里只在选曲/加载时画界面
            if (state == State.Playing) return;

            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 44,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.95f) },
            };
            rowStyle ??= new GUIStyle(GUI.skin.button)
            {
                fontSize = 24,
                alignment = TextAnchor.MiddleLeft,
            };
            hintStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.65f) },
            };

            const float rowWidth = 860f;
            const float rowHeight = 52f;
            const float rowGap = 12f;
            var left = (Screen.width - rowWidth) * 0.5f;
            var top = Screen.height * 0.22f;

            GUI.Label(new Rect(0f, Screen.height * 0.08f, Screen.width, 60f), "音游播放器 · 选曲", titleStyle);

            // 固定展示至少 4 个槽位（1-4），没有歌曲的槽位给出导入提示
            var rows = Mathf.Clamp(Mathf.Max(4, songs.Count), 4, 9);
            for (var i = 0; i < rows; i++)
            {
                var rect = new Rect(left, top + i * (rowHeight + rowGap), rowWidth, rowHeight);
                if (i < songs.Count)
                {
                    var song = songs[i];
                    var artist = string.IsNullOrEmpty(song.Artist) ? "未知曲师" : song.Artist;
                    var text = $"{i + 1}.  {song.Name}      — {artist}      BPM {song.Bpm:0}";
                    if (GUI.Button(rect, "  " + text, rowStyle))
                    {
                        StartSong(i); // 鼠标点击 / 触摸点击
                    }
                }
                else
                {
                    GUI.Label(rect, $"{i + 1}.  （未导入 · 把歌曲文件夹放进 Songs 目录即可）", hintStyle);
                }
            }

            GUI.Label(new Rect(0f, Screen.height - 96f, Screen.width, 26f),
                "数字键 1-4（最多 9）选歌   ·   鼠标/触摸点击列表   ·   游玩中 Esc 返回选曲   ·   Tab 切换自动/手动",
                hintStyle);
            if (!string.IsNullOrEmpty(loadingText))
            {
                GUI.Label(new Rect(0f, Screen.height - 58f, Screen.width, 30f), loadingText, hintStyle);
            }
        }
    }
}
