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
            MainMenu,
            SongSelect,
            Loading,
            Playing,
            Paused,
            Settings,
            Import,
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

        [Header("试听片段（秒）")]
        [SerializeField] float previewStartSeconds = 30f;
        [SerializeField] float previewLengthSeconds = 20f;

        readonly List<SongInfo> songs = new List<SongInfo>();
        State state = State.MainMenu;
        State settingsReturn = State.MainMenu;
        SongInfo currentSong;
        ResultData result;
        string loadingText = "";
        string importMessage = "";
        float volume = 1f;
        float noteSpeed = 2.4f;
        int fpsIndex = FpsPresets.Length - 1;
        bool performanceMode = true;
        int rebindLane = -1;
        float resultTimer;
        float maxDriftMs;
        int selectedIndex;
        int loadedIndex = -1;
        bool previewActive;
        float previewStartTime;
        float menuAnim;

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
        GUIStyle menuRowStyle;
        GUIStyle menuRowSelectedStyle;
        GUIStyle songNameStyle;
        GUIStyle songInfoStyle;
        GUIStyle pathStyle;
        GUIStyle selectTitleStyle;
        GUIStyle previewLabelStyle;

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
            EnterMainMenu();
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

        void EnterMainMenu()
        {
            state = State.MainMenu;
            rebindLane = -1;
            previewActive = false;
            loadingText = "";
            result = null;
            if (clock != null) clock.Stop();
            if (playfield != null) playfield.ClearForSelect();
        }

        void EnterSongSelect()
        {
            state = State.SongSelect;
            rebindLane = -1;
            previewActive = false;
            loadingText = "";
            result = null;
            menuAnim = 0f;
            if (clock != null) clock.Stop();
            if (playfield != null) playfield.ClearForSelect();

            if (songs.Count > 0)
            {
                selectedIndex = Mathf.Clamp(selectedIndex, 0, songs.Count - 1);
                SelectSong(selectedIndex);
            }
        }

        void SelectSong(int index)
        {
            if (index < 0 || index >= songs.Count || state == State.Loading) return;
            selectedIndex = index;
            StartCoroutine(LoadSongRoutine(index, true));
        }

        void StartSelectedSong()
        {
            if (songs.Count == 0 || state == State.Loading) return;
            if (loadedIndex == selectedIndex && clock != null && clock.HasClip)
            {
                previewActive = false;
                clock.PlayFrom(0.0);
                state = State.Playing;
                return;
            }
            StartCoroutine(LoadSongRoutine(Mathf.Clamp(selectedIndex, 0, songs.Count - 1), false));
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
                            SelectSong(i);
                            return;
                        }
                    }
                    if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
                    {
                        StartSelectedSong();
                        break;
                    }
                    if (Input.GetKeyDown(KeyCode.Escape)) EnterMainMenu();
                    UpdatePreviewLoop();
                    break;

                case State.Import:
                    if (Input.GetKeyDown(KeyCode.Escape)) EnterMainMenu();
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

                case State.Settings:
                    if (rebindLane < 0 && Input.GetKeyDown(KeyCode.Escape)) state = settingsReturn;
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

            if ((state == State.SongSelect || state == State.Loading) && menuAnim < 1f)
            {
                menuAnim = Mathf.Min(1f, menuAnim + Time.unscaledDeltaTime / 0.35f);
            }
        }

        void UpdatePreviewLoop()
        {
            if (!previewActive || clock == null) return;
            if (clock.IsFinished)
            {
                clock.PlayFrom(previewStartTime);
                return;
            }
            if (clock.IsRunning && clock.SongTime > previewStartTime + previewLengthSeconds)
            {
                clock.PlayFrom(previewStartTime);
            }
        }

        void TrackDrift()
        {
            if (clock == null || !clock.IsRunning || clock.SourceTime <= 0.1) return;
            var drift = Mathf.Abs((float)(clock.SongTime - clock.SourceTime)) * 1000f;
            if (drift < 500f) maxDriftMs = Mathf.Max(maxDriftMs, drift);
        }

        IEnumerator LoadSongRoutine(int index, bool preview)
        {
            var song = songs[index];
            state = State.Loading;
            previewActive = false;
            loadingText = preview ? $"载入试听「{song.Name}」" : $"正在加载「{song.Name}」";
            if (clock != null) clock.Stop();
            if (playfield != null) playfield.ClearForSelect();

            var url = "file:///" + song.AudioPath.Replace('\\', '/');
            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, GuessAudioType(song.AudioPath)))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    loadingText = "音频加载失败：" + request.error;
                    Debug.LogError("[选曲] " + loadingText);
                    loadedIndex = -1;
                    state = State.SongSelect;
                    yield break;
                }

                var clip = DownloadHandlerAudioClip.GetContent(request);
                loadingText = "";
                currentSong = song;
                maxDriftMs = 0f;
                loadedIndex = index;
                clock.LoadClip(clip, song.Bpm, song.FirstTime);
                playfield.LoadSong(song);

                if (preview)
                {
                    playfield.SetAutoPlay(true);
                    previewStartTime = Mathf.Clamp(previewStartSeconds, 0f, Mathf.Max(0f, clip.length - previewLengthSeconds - 1f));
                    previewActive = true;
                    clock.PlayFrom(previewStartTime);
                    state = State.SongSelect;
                }
                else
                {
                    clock.PlayFrom(0.0);
                    state = State.Playing;
                }
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

        void OpenSongsFolder()
        {
            try
            {
                var path = SongRepository.Root;
                Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[导入] 打开目录失败：" + e.Message);
            }
        }

        void OnGUI()
        {
            switch (state)
            {
                case State.Result:
                    UiTheme.DrawBackdrop();
                    DrawResult();
                    return;

                case State.Settings:
                    UiTheme.DrawBackdrop();
                    DrawSettings();
                    return;

                case State.MainMenu:
                    UiTheme.DrawBackdrop();
                    DrawMainMenu();
                    return;

                case State.Import:
                    UiTheme.DrawBackdrop();
                    DrawImport();
                    return;

                case State.SongSelect:
                case State.Loading:
                    UiTheme.DrawBackdrop();
                    DrawSongSelect();
                    return;
            }

            DrawPauseButton();
            if (state == State.Paused) DrawPauseMenu();
        }

        void DrawMainMenu()
        {
            titleStyle ??= UiTheme.TitleStyle();
            hintStyle ??= UiTheme.HintStyle();
            menuButtonStyle ??= UiTheme.MenuButtonStyle();

            GUI.Label(new Rect(0f, Screen.height * 0.15f, Screen.width, 66f), "RHYTHM PLAYER", titleStyle);
            GUI.Label(new Rect(0f, Screen.height * 0.15f + 64f, Screen.width, 28f), "六边形音游播放器", hintStyle);
            GUI.DrawTexture(new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.15f + 106f, 360f, 2f), UiTheme.AccentLine());

            const float buttonWidth = 420f;
            const float buttonHeight = 84f;
            const float buttonGap = 22f;
            var left = (Screen.width - buttonWidth) * 0.5f;
            var top = Screen.height * 0.37f;

            if (GUI.Button(new Rect(left, top, buttonWidth, buttonHeight), $"游玩    （{songs.Count} 首）", menuButtonStyle)) EnterSongSelect();
            if (GUI.Button(new Rect(left, top + (buttonHeight + buttonGap), buttonWidth, buttonHeight), "导入歌曲", menuButtonStyle))
            {
                importMessage = "";
                state = State.Import;
            }
            if (GUI.Button(new Rect(left, top + 2f * (buttonHeight + buttonGap), buttonWidth, buttonHeight), "设置", menuButtonStyle))
            {
                settingsReturn = State.MainMenu;
                state = State.Settings;
            }

            GUI.Label(new Rect(0f, Screen.height - 58f, Screen.width, 24f),
                IsTouchPlatform ? "点按即可进入" : "鼠标 / 触摸点击    ·    游玩中左上角可暂停", hintStyle);
        }

        void DrawImport()
        {
            resultPanelStyle ??= UiTheme.PanelStyle();
            menuTitleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 34,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = UiTheme.TextMain },
            };
            hintStyle ??= UiTheme.HintStyle();
            menuButtonStyle ??= UiTheme.MenuButtonStyle();
            smallButtonStyle ??= UiTheme.SmallButtonStyle();
            settingsValueStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UiTheme.Accent },
            };
            pathStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = UiTheme.TextDim },
            };

            var panel = new Rect((Screen.width - 940f) * 0.5f, (Screen.height - 480f) * 0.5f, 940f, 480f);
            GUI.Box(panel, GUIContent.none, resultPanelStyle);
            GUI.Label(new Rect(panel.x, panel.y + 26f, panel.width, 44f), "导入歌曲", menuTitleStyle);

            GUI.Label(new Rect(panel.x + 30f, panel.y + 100f, panel.width - 60f, 24f), "歌曲目录（把歌曲文件夹或 zip / mcz 歌包放进去）", hintStyle);
            GUI.Label(new Rect(panel.x + 30f, panel.y + 132f, panel.width - 60f, 24f), SongRepository.Root, pathStyle);
            GUI.Label(new Rect(panel.x, panel.y + 176f, panel.width, 30f), $"当前识别 {songs.Count} 首歌曲", settingsValueStyle);

            if (!string.IsNullOrEmpty(importMessage))
            {
                GUI.Label(new Rect(panel.x, panel.y + 214f, panel.width, 24f), importMessage, settingsValueStyle);
            }

            const float buttonWidth = 260f;
            const float buttonHeight = 64f;
            var buttonY = panel.y + panel.height - 110f;
            var buttonCount = IsTouchPlatform ? 2 : 3;
            const float gap = 20f;
            var startX = panel.x + (panel.width - (buttonCount * buttonWidth + (buttonCount - 1) * gap)) * 0.5f;

            var slot = 0;
            if (!IsTouchPlatform)
            {
                if (GUI.Button(new Rect(startX + slot * (buttonWidth + gap), buttonY, buttonWidth, buttonHeight), "打开歌曲文件夹", smallButtonStyle)) OpenSongsFolder();
                slot++;
            }
            if (GUI.Button(new Rect(startX + slot * (buttonWidth + gap), buttonY, buttonWidth, buttonHeight), "重新扫描", smallButtonStyle))
            {
                RefreshSongs();
                importMessage = $"已重新扫描：识别到 {songs.Count} 首歌曲";
            }
            slot++;
            if (GUI.Button(new Rect(startX + slot * (buttonWidth + gap), buttonY, buttonWidth, buttonHeight), "返回", smallButtonStyle)) EnterMainMenu();
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
            hintStyle ??= UiTheme.HintStyle();
            smallButtonStyle ??= UiTheme.SmallButtonStyle();

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
                    if (GUI.Button(rect, label, smallButtonStyle)) rebindLane = lane;
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
                : "空格 暂停/继续 · R 重开 · Esc 返回 · Tab 自动/手动";
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
            hintStyle ??= UiTheme.HintStyle();
            loadingStyle ??= UiTheme.LoadingStyle();
            resultPanelStyle ??= UiTheme.PanelStyle();
            menuButtonStyle ??= UiTheme.MenuButtonStyle();
            smallButtonStyle ??= UiTheme.SmallButtonStyle();
            menuRowStyle ??= UiTheme.MenuRowStyle();
            menuRowSelectedStyle ??= UiTheme.RowSelectedStyle();
            songNameStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 36,
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                normal = { textColor = UiTheme.TextMain },
            };
            songInfoStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = UiTheme.TextDim },
            };

            var eased = menuAnim * menuAnim * (3f - 2f * menuAnim);

            selectTitleStyle ??= new GUIStyle(UiTheme.TitleStyle())
            {
                fontSize = 30,
                alignment = TextAnchor.MiddleLeft,
            };
            GUI.Label(new Rect(40f, 26f, 500f, 44f), "RHYTHM PLAYER", selectTitleStyle);

            var leftPanel = new Rect(70f, Screen.height * 0.28f, 620f, 340f);
            GUI.Box(leftPanel, GUIContent.none, resultPanelStyle);

            if (songs.Count > 0 && selectedIndex >= 0 && selectedIndex < songs.Count)
            {
                var song = songs[selectedIndex];
                var artist = string.IsNullOrEmpty(song.Artist) ? "未知曲师" : song.Artist;
                GUI.Label(new Rect(leftPanel.x + 34f, leftPanel.y + 34f, leftPanel.width - 68f, 48f), song.Name, songNameStyle);
                GUI.Label(new Rect(leftPanel.x + 34f, leftPanel.y + 90f, leftPanel.width - 68f, 30f), artist, songInfoStyle);
                GUI.Label(new Rect(leftPanel.x + 34f, leftPanel.y + 124f, leftPanel.width - 68f, 30f),
                    $"BPM {song.Bpm:0}" + (song.NoteCount > 0 ? $"      音符 {song.NoteCount}" : ""), songInfoStyle);
                if (previewActive)
                {
                    previewLabelStyle ??= new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 20,
                        alignment = TextAnchor.MiddleLeft,
                        normal = { textColor = UiTheme.Accent },
                    };
                    GUI.Label(new Rect(leftPanel.x + 34f, leftPanel.y + 158f, leftPanel.width - 68f, 28f), "试听中……", previewLabelStyle);
                }

                if (GUI.Button(new Rect(leftPanel.x + 34f, leftPanel.y + 216f, 300f, 88f), "开始 ▶", menuButtonStyle)) StartSelectedSong();
                if (GUI.Button(new Rect(leftPanel.x + 348f, leftPanel.y + 216f, 238f, 88f), "返回主菜单", smallButtonStyle)) EnterMainMenu();
            }
            else
            {
                GUI.Label(new Rect(leftPanel.x + 34f, leftPanel.y + 34f, leftPanel.width - 68f, 40f), "还没有导入歌曲", songNameStyle);
                GUI.Label(new Rect(leftPanel.x + 34f, leftPanel.y + 90f, leftPanel.width - 68f, 30f), "到「导入歌曲」里看看使用方法", songInfoStyle);
                if (GUI.Button(new Rect(leftPanel.x + 34f, leftPanel.y + 216f, 552f, 88f), "返回主菜单", menuButtonStyle)) EnterMainMenu();
            }

            var menuWidth = 460f;
            var menuX = Screen.width - menuWidth - 24f + (1f - eased) * (menuWidth + 60f);
            GUI.color = new Color(1f, 1f, 1f, Mathf.Max(0.05f, eased));

            GUI.Label(new Rect(menuX, 60f, menuWidth, 30f), "曲目列表", hintStyle);

            var listTop = 100f;
            var count = Mathf.Min(songs.Count, 9);
            for (var i = 0; i < count; i++)
            {
                var song = songs[i];
                var rect = new Rect(menuX, listTop + i * 62f, menuWidth, 52f);
                var label = $"{i + 1:00}   {song.Name}";
                var style = i == selectedIndex ? menuRowSelectedStyle : menuRowStyle;
                if (GUI.Button(rect, label, style)) SelectSong(i);
            }

            GUI.color = Color.white;

            var selectHint = IsTouchPlatform
                ? "点按曲目试听    ·    「开始」游玩    ·    Esc 返回"
                : "点按曲目试听    ·    Enter 开始    ·    数字键选曲    ·    Esc 返回";
            GUI.Label(new Rect(40f, Screen.height - 56f, Screen.width - 80f, 24f), selectHint, hintStyle);

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
