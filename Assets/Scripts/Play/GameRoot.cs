using System.Collections;
using System.Collections.Generic;
using System.IO;
using RhythmPlayer.Core;
using RhythmPlayer.UI;
using UnityEngine;
using UnityEngine.Networking;

namespace RhythmPlayer.Play
{
    public sealed class GameRoot : MonoBehaviour
    {
        enum State
        {
            SongSelect,
            Loading,
            Playing,
            Paused,
            Settings,
            Result,
        }

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

        const string VolumeKey = "RhythmPlayer.Volume";
        const string SpeedKey = "RhythmPlayer.Speed";
        const string FpsKey = "RhythmPlayer.FpsIndex";
        const string PerfKey = "RhythmPlayer.PerformanceMode";
        const string KeyPrefix = "RhythmPlayer.Key";

        static readonly int[] FpsPresets = { 60, 90, 120, 144, 165, 240, 300 };

        [Header("引用")]
        [SerializeField] Playfield playfield;
        [SerializeField] SongClock clock;

        readonly List<SongInfo> songs = new List<SongInfo>();
        State state = State.SongSelect;
        State settingsReturn = State.SongSelect;
        SongInfo currentSong;
        ResultData result;
        string loadingText = "";
        float volume = 1f;
        float noteSpeed = 2.4f;
        int fpsIndex = FpsPresets.Length - 1;
        bool performanceMode = true;
        int rebindLane = -1;
        float resultTimer;
        float maxDriftMs;

        GUIStyle titleStyle;
        GUIStyle rowStyle;
        GUIStyle emptyRowStyle;
        GUIStyle hintStyle;
        GUIStyle loadingStyle;
        GUIStyle resultPanelStyle;
        GUIStyle resultTitleStyle;
        GUIStyle resultRankStyle;
        GUIStyle resultRowStyle;
        GUIStyle smallButtonStyle;
        GUIStyle menuButtonStyle;
        GUIStyle menuTitleStyle;
        GUIStyle settingsLabelStyle;
        GUIStyle settingsValueStyle;
        GUIStyle rebindHintStyle;

        static bool IsTouchPlatform =>
            Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer;

        void Start()
        {
            if (playfield == null) playfield = FindObjectOfType<Playfield>();
            if (clock == null) clock = FindObjectOfType<SongClock>();

            volume = PlayerPrefs.GetFloat(VolumeKey, 1f);
            noteSpeed = PlayerPrefs.GetFloat(SpeedKey, playfield != null ? playfield.NoteSpeed : 2.4f);
            fpsIndex = Mathf.Clamp(PlayerPrefs.GetInt(FpsKey, FpsPresets.Length - 1), 0, FpsPresets.Length - 1);
            performanceMode = PlayerPrefs.GetInt(PerfKey, 1) != 0;
            LoadKeyBindings();

            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            ApplySettings();

            StartCoroutine(StartupRoutine());
        }

        void LoadKeyBindings()
        {
            if (playfield == null) return;
            for (var i = 0; i < 6; i++)
            {
                var stored = PlayerPrefs.GetInt(KeyPrefix + i, (int)playfield.GetJudgeKey(i));
                if (stored != 0) playfield.SetJudgeKey(i, (KeyCode)stored);
            }
        }

        IEnumerator StartupRoutine()
        {
            loadingText = "正在准备歌曲资源";
            yield return SongRepository.PrepareRoutine();
            loadingText = "";
            RefreshSongs();
            EnterSongSelect();
        }

        void ApplySettings()
        {
            AudioListener.volume = Mathf.Clamp01(volume);
            if (playfield != null) playfield.SetNoteSpeed(noteSpeed);
            ApplyFrameRate();
            ApplyPerformanceMode();
        }

        void ApplyFrameRate()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = FpsPresets[fpsIndex];
        }

        void ApplyPerformanceMode()
        {
            if (performanceMode)
            {
                QualitySettings.shadows = ShadowQuality.Disable;
                QualitySettings.antiAliasing = 0;
                QualitySettings.pixelLightCount = 0;
                QualitySettings.particleRaycastBudget = 0;
                QualitySettings.softParticles = false;
            }
            else
            {
                QualitySettings.shadows = ShadowQuality.HardOnly;
                QualitySettings.antiAliasing = 2;
                QualitySettings.pixelLightCount = 4;
                QualitySettings.particleRaycastBudget = 256;
                QualitySettings.softParticles = true;
            }
        }

        void SaveSettings()
        {
            PlayerPrefs.SetFloat(VolumeKey, volume);
            PlayerPrefs.SetFloat(SpeedKey, noteSpeed);
            PlayerPrefs.SetInt(FpsKey, fpsIndex);
            PlayerPrefs.SetInt(PerfKey, performanceMode ? 1 : 0);
            PlayerPrefs.Save();
        }

        void AdjustVolume(float delta)
        {
            volume = Mathf.Clamp01(volume + delta);
            ApplySettings();
            SaveSettings();
        }

        void AdjustSpeed(float delta)
        {
            noteSpeed = Mathf.Clamp(noteSpeed + delta, 0.6f, 6f);
            ApplySettings();
            SaveSettings();
        }

        void AdjustFps(int delta)
        {
            fpsIndex = (fpsIndex + delta + FpsPresets.Length) % FpsPresets.Length;
            ApplyFrameRate();
            SaveSettings();
        }

        void TogglePerformanceMode()
        {
            performanceMode = !performanceMode;
            ApplyPerformanceMode();
            SaveSettings();
        }

        void AssignKey(int lane, KeyCode key)
        {
            if (playfield == null)
            {
                rebindLane = -1;
                return;
            }

            var previous = playfield.GetJudgeKey(lane);
            for (var i = 0; i < 6; i++)
            {
                if (i != lane && playfield.GetJudgeKey(i) == key)
                {
                    playfield.SetJudgeKey(i, previous);
                    PlayerPrefs.SetInt(KeyPrefix + i, (int)previous);
                }
            }

            playfield.SetJudgeKey(lane, key);
            PlayerPrefs.SetInt(KeyPrefix + lane, (int)key);
            PlayerPrefs.Save();
            rebindLane = -1;
        }

        void HandleSettingsKeys()
        {
            if (Input.GetKeyDown(KeyCode.Minus)) AdjustVolume(-0.05f);
            if (Input.GetKeyDown(KeyCode.Equals)) AdjustVolume(0.05f);
            if (Input.GetKeyDown(KeyCode.LeftBracket)) AdjustSpeed(-0.1f);
            if (Input.GetKeyDown(KeyCode.RightBracket)) AdjustSpeed(0.1f);
        }

        void RefreshSongs()
        {
            songs.Clear();
            songs.AddRange(SongRepository.Scan());
            Debug.Log($"[选曲] 已导入 {songs.Count} 首歌曲（目录:{SongRepository.Root}）");
        }

        void EnterSongSelect()
        {
            state = State.SongSelect;
            loadingText = "";
            result = null;
            rebindLane = -1;
            if (clock != null) clock.Stop();
            if (playfield != null) playfield.ClearForSelect();
        }

        void PauseGame()
        {
            if (state != State.Playing || clock == null) return;
            clock.Pause();
            state = State.Paused;
        }

        void ResumeGame()
        {
            if (clock != null) clock.Resume();
            state = State.Playing;
        }

        void RestartSong()
        {
            maxDriftMs = 0f;
            if (clock != null) clock.PlayFrom(0.0);
            state = State.Playing;
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && state == State.Playing) PauseGame();
        }

        void Update()
        {
            switch (state)
            {
                case State.SongSelect:
                    HandleSettingsKeys();
                    for (var i = 0; i < songs.Count && i < 9; i++)
                    {
                        if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)))
                        {
                            StartSong(i);
                            return;
                        }
                    }
                    break;

                case State.Playing:
                    TrackDrift();
                    if (Input.GetKeyDown(KeyCode.Space)) { PauseGame(); break; }
                    if (Input.GetKeyDown(KeyCode.R)) { RestartSong(); break; }
                    if (Input.GetKeyDown(KeyCode.Escape)) { PauseGame(); break; }
                    if (clock != null && clock.IsFinished) ShowResult();
                    break;

                case State.Paused:
                    if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape)) ResumeGame();
                    break;

                case State.Result:
                    resultTimer += Time.unscaledDeltaTime;
                    if (resultTimer > 0.5f && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                        || Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(0)))
                    {
                        EnterSongSelect();
                    }
                    break;
            }
        }

        void TrackDrift()
        {
            if (clock == null || !clock.IsRunning || clock.SourceTime <= 0.1) return;
            var drift = Mathf.Abs((float)(clock.SongTime - clock.SourceTime)) * 1000f;
            if (drift < 500f) maxDriftMs = Mathf.Max(maxDriftMs, drift);
        }

        public void StartSong(int index)
        {
            if (state == State.Loading || index < 0 || index >= songs.Count) return;
            StartCoroutine(LoadAndPlay(songs[index]));
        }

        IEnumerator LoadAndPlay(SongInfo song)
        {
            state = State.Loading;
            loadingText = $"正在加载「{song.Name}」";
            if (clock != null) clock.Stop();
            if (playfield != null) playfield.ClearForSelect();

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
                maxDriftMs = 0f;
                clock.LoadClip(clip, song.Bpm, song.FirstTime);
                playfield.LoadSong(song);
                clock.PlayFrom(0.0);
                state = State.Playing;
            }
        }

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
            if (clock != null) clock.Stop();
            state = State.Result;
        }

        static string RankOf(float accuracy)
        {
            if (accuracy >= 0.95f) return "S";
            if (accuracy >= 0.90f) return "A";
            if (accuracy >= 0.80f) return "B";
            if (accuracy >= 0.70f) return "C";
            return "D";
        }

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
            if (state == State.Result)
            {
                UiTheme.DrawBackdrop();
                DrawResult();
                return;
            }

            if (state == State.Settings)
            {
                UiTheme.DrawBackdrop();
                DrawSettings();
                return;
            }

            if (state == State.SongSelect || state == State.Loading)
            {
                UiTheme.DrawBackdrop();
                DrawSongSelect();
                return;
            }

            DrawPauseButton();
            if (state == State.Paused) DrawPauseMenu();
        }

        void DrawPauseButton()
        {
            if (state != State.Playing) return;
            smallButtonStyle ??= UiTheme.SmallButtonStyle();
            if (GUI.Button(new Rect(16f, 16f, 120f, 54f), "暂停", smallButtonStyle)) PauseGame();
        }

        void DrawPauseMenu()
        {
            resultPanelStyle ??= UiTheme.PanelStyle();
            menuButtonStyle ??= UiTheme.MenuButtonStyle();
            menuTitleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 34,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = UiTheme.TextMain },
            };

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var panel = new Rect((Screen.width - 440f) * 0.5f, (Screen.height - 560f) * 0.5f, 440f, 560f);
            GUI.Box(panel, GUIContent.none, resultPanelStyle);
            GUI.Label(new Rect(panel.x, panel.y + 34f, panel.width, 44f), "已暂停", menuTitleStyle);

            const float buttonWidth = 360f;
            const float buttonHeight = 76f;
            const float buttonGap = 18f;
            var left = panel.x + (panel.width - buttonWidth) * 0.5f;
            var top = panel.y + 120f;

            if (GUI.Button(new Rect(left, top, buttonWidth, buttonHeight), "继续", menuButtonStyle)) ResumeGame();
            if (GUI.Button(new Rect(left, top + (buttonHeight + buttonGap), buttonWidth, buttonHeight), "重开", menuButtonStyle)) RestartSong();
            if (GUI.Button(new Rect(left, top + 2f * (buttonHeight + buttonGap), buttonWidth, buttonHeight), "设置", menuButtonStyle))
            {
                settingsReturn = State.Paused;
                state = State.Settings;
            }
            if (GUI.Button(new Rect(left, top + 3f * (buttonHeight + buttonGap), buttonWidth, buttonHeight), "退出（返回选曲）", menuButtonStyle)) EnterSongSelect();
        }

        void DrawSettings()
        {
            resultPanelStyle ??= UiTheme.PanelStyle();
            menuButtonStyle ??= UiTheme.MenuButtonStyle();
            menuTitleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 34,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = UiTheme.TextMain },
            };
            settingsLabelStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = UiTheme.TextMain },
            };
            settingsValueStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UiTheme.Accent },
            };
            rebindHintStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UiTheme.Accent },
            };
            hintStyle ??= UiTheme.HintStyle();

            if (rebindLane >= 0)
            {
                var e = Event.current;
                if (e != null && e.type == EventType.KeyDown)
                {
                    if (e.keyCode == KeyCode.Escape) rebindLane = -1;
                    else if (e.keyCode != KeyCode.None && e.keyCode != KeyCode.Tab) AssignKey(rebindLane, e.keyCode);
                    e.Use();
                }
            }

            var panelHeight = Mathf.Min(Screen.height - 20f, 700f);
            var panel = new Rect((Screen.width - 760f) * 0.5f, (Screen.height - panelHeight) * 0.5f, 760f, panelHeight);
            GUI.Box(panel, GUIContent.none, resultPanelStyle);
            GUI.Label(new Rect(panel.x, panel.y + 14f, panel.width, 44f), "设置", menuTitleStyle);

            var left = panel.x + 50f;
            var rowY = panel.y + 64f;
            const float rowHeight = 54f;

            GUI.Label(new Rect(left, rowY, 220f, rowHeight), "音量", settingsLabelStyle);
            if (GUI.Button(new Rect(left + 240f, rowY, 72f, 50f), "－", menuButtonStyle)) AdjustVolume(-0.05f);
            GUI.Label(new Rect(left + 322f, rowY, 150f, rowHeight), $"{volume * 100f:0}%", settingsValueStyle);
            if (GUI.Button(new Rect(left + 482f, rowY, 72f, 50f), "＋", menuButtonStyle)) AdjustVolume(0.05f);

            rowY += 60f;
            GUI.Label(new Rect(left, rowY, 220f, rowHeight), "下落速度", settingsLabelStyle);
            if (GUI.Button(new Rect(left + 240f, rowY, 72f, 50f), "－", menuButtonStyle)) AdjustSpeed(-0.1f);
            GUI.Label(new Rect(left + 322f, rowY, 150f, rowHeight), $"{noteSpeed:0.0}×", settingsValueStyle);
            if (GUI.Button(new Rect(left + 482f, rowY, 72f, 50f), "＋", menuButtonStyle)) AdjustSpeed(0.1f);

            rowY += 60f;
            GUI.Label(new Rect(left, rowY, 220f, rowHeight), "最高帧率", settingsLabelStyle);
            if (GUI.Button(new Rect(left + 240f, rowY, 72f, 50f), "－", menuButtonStyle)) AdjustFps(-1);
            GUI.Label(new Rect(left + 322f, rowY, 150f, rowHeight), $"{FpsPresets[fpsIndex]}", settingsValueStyle);
            if (GUI.Button(new Rect(left + 482f, rowY, 72f, 50f), "＋", menuButtonStyle)) AdjustFps(1);

            rowY += 60f;
            GUI.Label(new Rect(left, rowY, 220f, rowHeight), "性能模式", settingsLabelStyle);
            if (GUI.Button(new Rect(left + 240f, rowY, 314f, 50f), performanceMode ? "开（低占用，更流畅）" : "关（默认画质）", menuButtonStyle)) TogglePerformanceMode();

            rowY += 68f;
            GUI.Label(new Rect(panel.x, rowY, panel.width, 24f), "—— 按键映射（点后按新键，Esc 取消）——", hintStyle);

            rowY += 30f;
            const float cellWidth = 330f;
            const float cellHeight = 48f;
            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 2; column++)
                {
                    var lane = row + column * 3;
                    var rect = new Rect(left + column * (cellWidth + 10f), rowY + row * 54f, cellWidth, cellHeight);
                    var label = rebindLane == lane ? $"{lane + 1}：请按新键…" : $"{lane + 1}：{KeyDisplay(playfield != null ? playfield.GetJudgeKey(lane) : KeyCode.None)}";
                    if (GUI.Button(rect, label, smallButtonStyle ??= UiTheme.SmallButtonStyle()))
                    {
                        rebindLane = lane;
                    }
                }
            }

            rowY += 3f * 54f + 14f;
            var songTime = clock != null ? clock.SongTime : 0.0;
            var beat = clock != null ? clock.Beat : 0.0;
            var bpm = clock != null ? clock.Bpm : 0f;
            GUI.Label(new Rect(panel.x, rowY, panel.width, 24f), $"歌曲时间 {songTime:F3} s      拍数 {beat:F2}      BPM {bpm:F0}      时钟偏差 {maxDriftMs:F1} ms", hintStyle);

            rowY += 30f;
            var hintLine = IsTouchPlatform
                ? "触摸六边形上的判定点即可打击；左上角按钮暂停"
                : "空格 暂停/继续 · R 重开 · Esc 暂停菜单 · Tab 自动/手动 · 也可直接触摸判定点";
            GUI.Label(new Rect(panel.x, rowY, panel.width, 24f), hintLine, hintStyle);

            if (GUI.Button(new Rect(panel.x + (panel.width - 240f) * 0.5f, panel.y + panelHeight - 66f, 240f, 52f), "返回", menuButtonStyle))
            {
                state = settingsReturn;
            }
        }

        static string KeyDisplay(KeyCode key)
        {
            if (key == KeyCode.None) return "-";
            if (key == KeyCode.Comma) return ",";
            if (key == KeyCode.Period) return ".";
            if (key == KeyCode.Semicolon) return ";";
            if (key == KeyCode.Slash) return "/";
            if (key == KeyCode.Quote) return "'";
            if (key == KeyCode.Space) return "空格";
            if (key == KeyCode.LeftShift) return "左Shift";
            if (key == KeyCode.RightShift) return "右Shift";
            if (key == KeyCode.UpArrow) return "↑";
            if (key == KeyCode.DownArrow) return "↓";
            if (key == KeyCode.LeftArrow) return "←";
            if (key == KeyCode.RightArrow) return "→";
            return key.ToString();
        }

        void DrawSongSelect()
        {
            titleStyle ??= UiTheme.TitleStyle();
            rowStyle ??= UiTheme.RowStyle();
            emptyRowStyle ??= UiTheme.EmptyRowStyle();
            hintStyle ??= UiTheme.HintStyle();
            loadingStyle ??= UiTheme.LoadingStyle();
            smallButtonStyle ??= UiTheme.SmallButtonStyle();

            GUI.Label(new Rect(0f, Screen.height * 0.075f, Screen.width, 62f), "RHYTHM PLAYER", titleStyle);
            GUI.Label(new Rect(0f, Screen.height * 0.075f + 58f, Screen.width, 28f), "六边形音游播放器", hintStyle);
            GUI.DrawTexture(new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.075f + 100f, 360f, 2f), UiTheme.AccentLine());

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
                    if (GUI.Button(rect, text, rowStyle)) StartSong(i);
                }
                else
                {
                    GUI.Label(rect, $"{i + 1:00}    （未导入 · 把歌曲文件夹或 zip / mcz 放进 Songs 目录）", emptyRowStyle);
                }
            }

            var selectHint = IsTouchPlatform
                ? "点按歌曲开始    ·    游玩中左上角可暂停    ·    右下角设置"
                : "数字键 1-4（最多 9）选歌    ·    鼠标 / 触摸点击列表    ·    游玩中左上角可暂停";
            GUI.Label(new Rect(0f, Screen.height - 84f, Screen.width, 24f), selectHint, hintStyle);
            GUI.Label(new Rect(0f, Screen.height - 56f, Screen.width, 24f),
                $"音量 {volume * 100f:0}%        下落速度 {noteSpeed:0.0}×        帧率上限 {FpsPresets[fpsIndex]}", hintStyle);

            if (GUI.Button(new Rect(Screen.width - 150f, Screen.height - 130f, 130f, 56f), "设置", smallButtonStyle))
            {
                settingsReturn = State.SongSelect;
                state = State.Settings;
            }

            if (!string.IsNullOrEmpty(loadingText))
            {
                var dots = new string('.', 1 + (int)(Time.unscaledTime * 3f) % 3);
                GUI.Box(new Rect((Screen.width - 520f) * 0.5f, Screen.height - 200f, 520f, 64f), loadingText + dots, loadingStyle);
            }
        }

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
            hintStyle ??= UiTheme.HintStyle();

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
            GUI.Label(new Rect(panelRect.x, panelRect.y + 448f, panelRect.width, 26f),
                IsTouchPlatform ? "点击任意处 返回选曲" : "Enter / 点击任意处 返回选曲", hintStyle);
        }
    }
}
