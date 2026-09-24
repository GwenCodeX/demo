using System.Collections;
using System.Collections.Generic;
using System.IO;
using RhythmPlayer.Core;
using RhythmPlayer.UI;
using UnityEngine;
using UnityEngine.Networking;

namespace RhythmPlayer.Play
{
    /// <summary>
    /// 游戏入口：负责"选曲界面 → 游玩 → 结算"的状态切换。
    /// - 启动时扫描歌曲目录（Songs/），列出所有已导入的歌曲（含 .zip / .mcz 自动解压导入）
    /// - 选曲：数字键 1-9 / 鼠标或触摸点击；-= 调音量，[ ] 调下落速度（即时保存）
    /// - 选曲后从磁盘加载音频（UnityWebRequest，支持玩家自己放歌进来），再让 Playfield 解析谱面
    /// - 整曲播完进入结算面板（Enter / 点击返回）；游玩中按 Esc 直接返回选曲
    /// 界面为简洁科技风：背景渐变 + 细网格 + 圆角面板（全部由 UiTheme 用代码绘制）。
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        /// <summary>界面状态</summary>
        enum State
        {
            SongSelect, // 选曲界面
            Loading,    // 正在加载歌曲（音频解码）
            Playing,    // 游玩中
            Result,     // 结算面板
        }

        /// <summary>结算数据快照（进结算时从面板抄下来，之后清场会重置统计）</summary>
        sealed class ResultData
        {
            public string SongName;
            public int Score;
            public float Accuracy;
            public int MaxCombo;
            public int Best;
            public int Cool;
            public int Good;
            public int Miss;
            public int Total;
        }

        // 设置项的 PlayerPrefs 键（重启后仍然生效）
        const string VolumeKey = "RhythmPlayer.Volume";
        const string SpeedKey = "RhythmPlayer.Speed";

        [Header("引用")]
        [SerializeField] Playfield playfield;
        [SerializeField] SongClock clock;

        readonly List<SongInfo> songs = new List<SongInfo>();
        State state = State.SongSelect;
        SongInfo currentSong;
        ResultData result;
        string loadingText = "";
        float volume = 1f;       // 音量（0-1）
        float noteSpeed = 2.4f;  // 下落速度（每拍距离）
        float resultTimer;       // 结算界面的防误触计时

        GUIStyle titleStyle;
        GUIStyle rowStyle;
        GUIStyle emptyRowStyle;
        GUIStyle hintStyle;
        GUIStyle loadingStyle;
        GUIStyle resultPanelStyle;
        GUIStyle resultTitleStyle;
        GUIStyle resultRankStyle;
        GUIStyle resultRowStyle;

        void Start()
        {
            // 引用缺省时自动在场景里找
            if (playfield == null) playfield = FindObjectOfType<Playfield>();
            if (clock == null) clock = FindObjectOfType<SongClock>();

            // 读取并应用设置（音量 / 下落速度）
            volume = PlayerPrefs.GetFloat(VolumeKey, 1f);
            noteSpeed = PlayerPrefs.GetFloat(SpeedKey, playfield != null ? playfield.NoteSpeed : 2.4f);
            ApplySettings();

            RefreshSongs();
            EnterSongSelect();
        }

        // ===== 设置 =====

        /// <summary>应用设置：音量走 AudioListener（全局生效），下落速度给面板</summary>
        void ApplySettings()
        {
            AudioListener.volume = Mathf.Clamp01(volume);
            if (playfield != null) playfield.SetNoteSpeed(noteSpeed);
        }

        /// <summary>保存设置到本机（PlayerPrefs）</summary>
        void SaveSettings()
        {
            PlayerPrefs.SetFloat(VolumeKey, volume);
            PlayerPrefs.SetFloat(SpeedKey, noteSpeed);
            PlayerPrefs.Save();
        }

        /// <summary>选曲界面的设置按键：-= 调音量，[ ] 调下落速度；改动即时保存</summary>
        void HandleSettingsKeys()
        {
            var changed = false;
            if (Input.GetKeyDown(KeyCode.Minus)) { volume = Mathf.Clamp01(volume - 0.05f); changed = true; }
            if (Input.GetKeyDown(KeyCode.Equals)) { volume = Mathf.Clamp01(volume + 0.05f); changed = true; }
            if (Input.GetKeyDown(KeyCode.LeftBracket)) { noteSpeed = Mathf.Clamp(noteSpeed - 0.1f, 0.6f, 6f); changed = true; }
            if (Input.GetKeyDown(KeyCode.RightBracket)) { noteSpeed = Mathf.Clamp(noteSpeed + 0.1f, 0.6f, 6f); changed = true; }
            if (!changed) return;

            ApplySettings();
            SaveSettings();
        }

        // ===== 歌曲列表与状态切换 =====

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
            result = null;
            if (clock != null) clock.Stop();
            if (playfield != null) playfield.ClearForSelect();
        }

        void Update()
        {
            if (state == State.SongSelect)
            {
                HandleSettingsKeys();

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
                // Esc 直接返回选曲；整曲播完进入结算
                if (Input.GetKeyDown(KeyCode.Escape)) EnterSongSelect();
                else if (clock != null && clock.IsFinished) ShowResult();
            }
            else if (state == State.Result)
            {
                // 防误触：面板出现 0.5 秒后才接受确认
                resultTimer += Time.unscaledDeltaTime;
                if (resultTimer > 0.5f && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                    || Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(0)))
                {
                    EnterSongSelect();
                }
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
            loadingText = $"正在加载「{song.Name}」";
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
                currentSong = song;
                clock.LoadClip(clip, song.Bpm, song.FirstTime); // 换音频 + BPM + 拍偏移
                playfield.LoadSong(song);                        // 解析谱面、换背景、清场
                clock.PlayFrom(0.0);                             // 从头开始播放
                state = State.Playing;
            }
        }

        /// <summary>进入结算界面：把面板统计抄进快照（之后清场会重置统计）</summary>
        void ShowResult()
        {
            result = new ResultData
            {
                SongName = currentSong != null ? currentSong.Name : "",
                Score = playfield != null ? playfield.Score : 0,
                Accuracy = playfield != null ? playfield.Accuracy : 1f,
                MaxCombo = playfield != null ? playfield.MaxCombo : 0,
                Best = playfield != null ? playfield.BestCount : 0,
                Cool = playfield != null ? playfield.CoolCount : 0,
                Good = playfield != null ? playfield.GoodCount : 0,
                Miss = playfield != null ? playfield.MissCount : 0,
                Total = playfield != null ? playfield.TotalNotes : 0,
            };
            resultTimer = 0f;
            if (clock != null) clock.Stop(); // 停止音频（画面保留最后一帧）
            state = State.Result;
        }

        /// <summary>评级：S ≥95%，A ≥90%，B ≥80%，C ≥70%，其余 D</summary>
        static string RankOf(float accuracy)
        {
            if (accuracy >= 0.95f) return "S";
            if (accuracy >= 0.90f) return "A";
            if (accuracy >= 0.80f) return "B";
            if (accuracy >= 0.70f) return "C";
            return "D";
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

        // ===== 界面 =====

        void OnGUI()
        {
            // 游玩中有自己的 HUD，这里不画
            if (state == State.Playing) return;

            UiTheme.DrawBackdrop(); // 渐变 + 网格背景

            if (state == State.Result)
            {
                DrawResult();
                return;
            }

            // ---- 选曲 / 加载界面 ----
            titleStyle ??= UiTheme.TitleStyle();
            rowStyle ??= UiTheme.RowStyle();
            emptyRowStyle ??= UiTheme.EmptyRowStyle();
            hintStyle ??= UiTheme.HintStyle();
            loadingStyle ??= UiTheme.LoadingStyle();

            // 标题区
            GUI.Label(new Rect(0f, Screen.height * 0.075f, Screen.width, 62f), "RHYTHM PLAYER", titleStyle);
            GUI.Label(new Rect(0f, Screen.height * 0.075f + 58f, Screen.width, 28f), "六边形音游播放器", hintStyle);
            GUI.DrawTexture(new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.075f + 100f, 360f, 2f), UiTheme.AccentLine());

            // 歌曲列表：固定展示至少 4 个槽位（1-4），没有歌曲的槽位给出导入提示
            const float rowWidth = 880f;
            const float rowHeight = 56f;
            const float rowGap = 14f;
            var left = (Screen.width - rowWidth) * 0.5f;
            var top = Screen.height * 0.26f;

            var rows = Mathf.Clamp(Mathf.Max(4, songs.Count), 4, 9);
            for (var i = 0; i < rows; i++)
            {
                var rect = new Rect(left, top + i * (rowHeight + rowGap), rowWidth, rowHeight);
                if (i < songs.Count)
                {
                    var song = songs[i];
                    var artist = string.IsNullOrEmpty(song.Artist) ? "未知曲师" : song.Artist;
                    var notes = song.NoteCount > 0 ? $"      {song.NoteCount} 音符" : "";
                    var text = $"{i + 1:00}    {song.Name}      —  {artist}      BPM {song.Bpm:0}{notes}";
                    if (GUI.Button(rect, text, rowStyle)) StartSong(i); // 鼠标点击 / 触摸点击
                }
                else
                {
                    GUI.Label(rect, $"{i + 1:00}    （未导入 · 把歌曲文件夹或 zip / mcz 放进 Songs 目录）", emptyRowStyle);
                }
            }

            // 底部提示 + 当前设置
            GUI.Label(new Rect(0f, Screen.height - 84f, Screen.width, 24f),
                "数字键 1-4（最多 9）选歌   ·   鼠标 / 触摸点击列表   ·   游玩中 Esc 返回选曲   ·   Tab 切换自动 / 手动",
                hintStyle);
            GUI.Label(new Rect(0f, Screen.height - 56f, Screen.width, 24f),
                $"音量 {volume * 100f:0}%（- = 调整）        下落速度 {noteSpeed:0.0}×（[ ] 调整）", hintStyle);

            // 加载中提示（带点动画）
            if (!string.IsNullOrEmpty(loadingText))
            {
                var dots = new string('.', 1 + (int)(Time.unscaledTime * 3f) % 3);
                GUI.Box(new Rect((Screen.width - 520f) * 0.5f, Screen.height - 200f, 520f, 64f), loadingText + dots, loadingStyle);
            }
        }

        /// <summary>结算面板：评级 / 分数 / 准确率 / 最大连击 / 判定统计</summary>
        void DrawResult()
        {
            if (result == null) return;

            resultPanelStyle ??= UiTheme.PanelStyle();
            resultTitleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UiTheme.TextMain },
            };
            resultRankStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 96,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = UiTheme.Accent },
            };
            resultRowStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UiTheme.TextMain },
            };

            var panelRect = new Rect((Screen.width - 640f) * 0.5f, (Screen.height - 520f) * 0.5f, 640f, 520f);
            GUI.Box(panelRect, GUIContent.none, resultPanelStyle);

            GUI.Label(new Rect(panelRect.x, panelRect.y + 26f, panelRect.width, 36f), "结算 · " + result.SongName, resultTitleStyle);
            GUI.Label(new Rect(panelRect.x, panelRect.y + 70f, panelRect.width, 120f), RankOf(result.Accuracy), resultRankStyle);
            GUI.Label(new Rect(panelRect.x, panelRect.y + 206f, panelRect.width, 30f), $"分数      {result.Score:N0}", resultRowStyle);
            GUI.Label(new Rect(panelRect.x, panelRect.y + 244f, panelRect.width, 30f), $"准确率    {result.Accuracy * 100f:0.00}%", resultRowStyle);
            GUI.Label(new Rect(panelRect.x, panelRect.y + 282f, panelRect.width, 30f), $"最大连击  {result.MaxCombo}", resultRowStyle);
            GUI.Label(new Rect(panelRect.x, panelRect.y + 336f, panelRect.width, 30f),
                $"Best {result.Best}     Cool {result.Cool}     Good {result.Good}     Miss {result.Miss}", resultRowStyle);
            GUI.Label(new Rect(panelRect.x, panelRect.y + 368f, panelRect.width, 30f), $"（共 {result.Total} 音符）", resultRowStyle);
            GUI.Label(new Rect(panelRect.x, panelRect.y + 448f, panelRect.width, 26f), "Enter / 点击任意处 返回选曲", hintStyle ?? UiTheme.HintStyle());
        }
    }
}
