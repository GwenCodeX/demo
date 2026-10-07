using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RhythmPlayer.Core;
using RhythmPlayer.UI;

namespace RhythmPlayer.Play
{
    public sealed class Playfield : MonoBehaviour
    {
        static readonly float[] PadAngleDeg = { 120f, 180f, 240f, 60f, 0f, 300f };

        const float Overshoot = 0.4f;
        const float BothLineWindow = 0.9f;
        const float BirthScaleFrom = 0.2f;
        const float MissWindowSeconds = 0.16f;
        const float BestWindowMs = 80f;
        const float CoolWindowMs = 120f;
        const float GoodWindowMs = 160f;
        const float FrameDuration = 0.045f;
        const int MaxPointers = 10;
        const float PopupLife = 0.45f;
        const float EffectWidth = 1.7f;
        const int ComboShowFrom = 4;
        const float ComboDigitHeight = 0.45f;
        const float ComboDigitGap = 0.04f;
        const float ComboDigitAlpha = 0.7f;
        const float ComboTagAlpha = 0.85f;
        const float ComboTagWidth = 0.65f;
        const float ComboCenterY = -0.1f;
        const float ComboTagY = 0.42f;
        const int ComboMaxDigits = 5;

        const int GradeBest = 0;
        const int GradeCool = 1;
        const int GradeGood = 2;
        const int GradeMiss = 3;

        [Header("引用")]
        [SerializeField] SongClock clock;

        [Header("皮肤（留空则用纯色矩形）")]
        [SerializeField] Sprite tapSprite;
        [SerializeField] Sprite tapBothSprite;
        [SerializeField] Sprite holdHeadSprite;
        [SerializeField] Sprite holdBothSprite;
        [SerializeField] Sprite holdBodySprite;
        [SerializeField] Sprite holdTailSprite;
        [SerializeField] Sprite backgroundSprite;
        [SerializeField] Sprite hexFrameSprite;
        [SerializeField] Sprite bothLineSprite;

        [Header("打击特效与判定字")]
        [SerializeField] Sprite[] hitFxFrames;
        [SerializeField] Sprite bestSprite;
        [SerializeField] Sprite coolSprite;
        [SerializeField] Sprite goodSprite;
        [SerializeField] Sprite missSprite;
        [SerializeField] Sprite[] comboDigitSprites;
        [SerializeField] Sprite comboTagSprite;

        [Header("音效")]
        [SerializeField] AudioClip hitSound;
        [Range(0f, 1f)]
        [SerializeField] float hitSoundVolume = 0.7f;

        [Header("六边形布局（世界单位）")]
        [Tooltip("外六边形中心到顶点的距离")]
        [SerializeField] float hexRadius = 4.3f;
        [Tooltip("中心出生区的判定点距离，音符从这里长出来")]
        [SerializeField] float spawnRadius = 0.9f;
        [SerializeField] bool showSpawnZone = true;

        [Header("手感")]
        [Tooltip("每拍飞行距离，越大越快")]
        [SerializeField] float unitsPerBeat = 2.4f;
        [SerializeField] float birthBeats = 0.8f;

        [Header("按键映射（左列上中下 1/2/3，右列上中下 4/5/6）")]
        [SerializeField] KeyCode[] judgeKeys = { KeyCode.E, KeyCode.D, KeyCode.C, KeyCode.I, KeyCode.K, KeyCode.Comma };

        [Header("触摸输入")]
        [SerializeField] bool touchEnabled = true;
        [SerializeField] float touchRadius = 1.1f;
        [Tooltip("落点分区判定：手指落在判定点的分区内即算命中（可搓：滑过进入也触发）")]
        [SerializeField] bool innerScreenJudge = true;
        [Tooltip("落点分区内边界（相对判定点所在外六边形的比例，1 = 正好压在外六边形边上）")]
        [SerializeField] float landingInnerRatio = 0.9f;
        [Tooltip("落点分区外边界（同比例，1.3 = 判定点外侧 30%）")]
        [SerializeField] float landingOuterRatio = 1.3f;
        [Tooltip("判定区线框颜色")]
        [SerializeField] Color judgeZoneColor = new Color(0.55f, 0.85f, 1f, 0.55f);
        [Tooltip("判定区线框粗细（世界单位）")]
        [SerializeField] float judgeZoneLineWidth = 0.05f;

        [Header("启动")]
        [SerializeField] bool autoPlay = true;

        sealed class NoteView
        {
            public GameObject Root;
            public SpriteRenderer Head;
            public SpriteRenderer Body;
            public SpriteRenderer Tail;
            public SpriteRenderer BothLine;
            public RuntimeNote Note;
            public bool Judged;
            public bool Vanish;
            public bool HoldActive;
        }

        /// <summary>FAST / SLOW 提示（在判定点内侧淡出）</summary>
        sealed class TimingPopup
        {
            public Vector3 WorldPosition;
            public bool IsFast;
            public float Age;
        }

        sealed class Effect
        {
            public SpriteRenderer Renderer;
            public Sprite[] Frames;
            public float Age;
            public float Life;
            public float BaseScale;
            public bool IsPopup;
        }

        readonly List<NoteView> active = new List<NoteView>();
        readonly Stack<NoteView> pool = new Stack<NoteView>();
        readonly List<Effect> effects = new List<Effect>();
        readonly Stack<SpriteRenderer> effectPool = new Stack<SpriteRenderer>();
        readonly List<SpriteRenderer> comboDigits = new List<SpriteRenderer>();
        readonly SpriteRenderer[] padFlashes = new SpriteRenderer[6];
        readonly float[] padFlashTimer = new float[6];
        readonly float[] padMissTimer = new float[6];
        readonly bool[] laneTouchHeld = new bool[6];
        readonly int[] pointerLane = new int[MaxPointers];

        CompiledChart chart;
        int nextIndex;
        double lastTime;
        float secondsPerBeat = 0.3f;
        float judgeOffsetSeconds;
        float holdReleaseGrace = 0.12f;
        int fastCount;
        int slowCount;
        bool ready;
        Transform boardRoot;
        Transform judgeZoneRoot;
        bool judgeZonesVisible = true;
        SpriteRenderer backgroundRenderer;
        GameObject comboRoot;
        SpriteRenderer comboTag;
        AudioSource sfxSource;
        AudioSource missSource;
        readonly AudioSource[] countInSources = new AudioSource[4];
        readonly List<TimingPopup> timingPopups = new List<TimingPopup>();
        GUIStyle fastSlowStyle;
        Camera mainCamera;

        string percentText = "0.00%";
        string comboTextCache = "0";
        int lastComboShown = -1;
        float lastAccuracy = -1f;

        GUIStyle percentStyle;

        int bestCount;
        int coolCount;
        int goodCount;
        int missCount;
        int combo;
        int maxCombo;
        float comboPulse;
        float comboBreakFlash;

        void Start()
        {
            if (clock == null) clock = FindObjectOfType<SongClock>();
            mainCamera = Camera.main;

            // 手机端把镜头拉近，游玩区域占屏更大
            if (mainCamera != null && (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer))
            {
                mainCamera.orthographicSize = 5.0f;
            }

            BuildVisuals();

            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;

            // Miss / 断连专用音源：同素材降调，听感区分
            missSource = gameObject.AddComponent<AudioSource>();
            missSource.playOnAwake = false;
            missSource.spatialBlend = 0f;
            missSource.pitch = 0.7f;
        }

        public void LoadSong(SongInfo song)
        {
            ClearForSelect();

            if (song == null || string.IsNullOrEmpty(song.ChartPath) || !File.Exists(song.ChartPath))
            {
                Debug.LogError("[谱面] 歌曲文件无效，无法装载");
                return;
            }

            secondsPerBeat = 60f / Mathf.Max(1f, song.Bpm);
            chart = ChartCompiler.Get(song.ChartPath, song.FirstTime, secondsPerBeat); // 编译一次 + 缓存
            if (chart == null || chart.TotalNotes == 0)
            {
                Debug.LogError("[谱面] 无法装载：" + song.ChartPath);
                ready = false;
                return;
            }
            Debug.Log($"[谱面] {song.Name}：{chart.TotalNotes} 个音符，其中长条 {chart.HoldCount} 个");

            nextIndex = 0;
            lastTime = 0.0;
            ready = true;
            SetBoardVisible(true);
            lastAccuracy = -1f;
            lastComboShown = -1;

            RefreshBackground(song.BackgroundPath);
        }

        public void ClearForSelect()
        {
            for (var i = active.Count - 1; i >= 0; i--) Release(i);

            for (var i = effects.Count - 1; i >= 0; i--)
            {
                effects[i].Renderer.gameObject.SetActive(false);
                effectPool.Push(effects[i].Renderer);
            }
            effects.Clear();
            timingPopups.Clear();

            chart = null;
            ready = false;
            SetBoardVisible(false);
            ResetCounters();
            if (backgroundRenderer != null) backgroundRenderer.gameObject.SetActive(false);
            if (comboRoot != null) comboRoot.SetActive(false);
        }

        void ResetCounters()
        {
            bestCount = 0;
            coolCount = 0;
            goodCount = 0;
            missCount = 0;
            combo = 0;
            maxCombo = 0;
            comboPulse = 0f;
            comboBreakFlash = 0f;
            fastCount = 0;
            slowCount = 0;
        }

        public int BestCount => bestCount;
        public int CoolCount => coolCount;
        public int GoodCount => goodCount;
        public int MissCount => missCount;
        public int MaxCombo => maxCombo;
        public int TotalNotes => chart != null ? chart.TotalNotes : 0;
        public int FastCount => fastCount;
        public int SlowCount => slowCount;

        public float Accuracy
        {
            get
            {
                if (chart == null || chart.TotalNotes == 0) return 0f;
                var weight = bestCount + coolCount * 0.8f + goodCount * 0.6f;
                return weight / chart.TotalNotes;
            }
        }

        public int Score => Mathf.RoundToInt(Accuracy * 1000000f);

        public float NoteSpeed => unitsPerBeat;

        public void SetNoteSpeed(float value) => unitsPerBeat = Mathf.Clamp(value, 0.6f, 6f);

        public void SetAutoPlay(bool value) => autoPlay = value;
        public void SetBackground(string path) => RefreshBackground(path);
        public void SetBoardVisible(bool visible)
        {
            if (boardRoot != null) boardRoot.gameObject.SetActive(visible);
        }

        public void SetJudgeZonesVisible(bool visible)
        {
            judgeZonesVisible = visible;
            if (judgeZoneRoot != null) judgeZoneRoot.gameObject.SetActive(visible);
        }

        /// <summary>设置判定偏移（秒，正值 = 判定整体延后，补偿习惯性打晚）</summary>
        public void SetJudgeOffsetSeconds(float value) => judgeOffsetSeconds = value;

        /// <summary>设置长条松手容差（秒，尾前该时间内松手不算断）</summary>
        public void SetHoldReleaseGrace(float value) => holdReleaseGrace = Mathf.Clamp(value, 0f, 0.3f);

        /// <summary>开曲定位音：在起播 dspTime 之前的若干个整拍上排入打击音（每拍一个独立音源，时序精确）</summary>
        public void ScheduleCountIn(int ticks, double startDsp, float beatSeconds, float volume)
        {
            if (hitSound == null) return;
            for (var i = 0; i < ticks && i < countInSources.Length; i++)
            {
                var time = startDsp - (ticks - i) * beatSeconds;
                if (time < AudioSettings.dspTime) continue;

                var source = countInSources[i];
                if (source == null)
                {
                    source = gameObject.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    source.spatialBlend = 0f;
                    source.clip = hitSound;
                    countInSources[i] = source;
                }
                source.volume = volume;
                source.PlayScheduled(time);
            }
        }

        public KeyCode GetJudgeKey(int lane)
        {
            if (judgeKeys == null || lane < 0 || lane >= judgeKeys.Length) return KeyCode.None;
            return judgeKeys[lane];
        }

        public void SetJudgeKey(int lane, KeyCode key)
        {
            if (judgeKeys == null || lane < 0 || lane >= judgeKeys.Length) return;
            judgeKeys[lane] = key;
        }

        void RefreshBackground(string path)
        {
            var sprite = LoadSpriteFromFile(path) ?? backgroundSprite;
            if (sprite == null)
            {
                if (backgroundRenderer != null) backgroundRenderer.gameObject.SetActive(false);
                return;
            }

            if (backgroundRenderer == null)
            {
                backgroundRenderer = CreateQuad(transform, "Background", -10, new Color(0.65f, 0.65f, 0.75f));
                backgroundRenderer.transform.localPosition = new Vector3(0f, 0f, 2f);
            }

            backgroundRenderer.gameObject.SetActive(true);
            backgroundRenderer.sprite = sprite;

            var cam = MainCamera;
            var viewHeight = cam != null && cam.orthographic ? cam.orthographicSize * 2f : 11.6f;
            var viewWidth = viewHeight * UiTheme.Width / Mathf.Max(1, UiTheme.Height);
            var size = sprite.bounds.size;
            var scale = Mathf.Max(viewWidth / Mathf.Max(0.0001f, size.x), viewHeight / Mathf.Max(0.0001f, size.y)) * 1.02f;
            backgroundRenderer.transform.localScale = new Vector3(scale, scale, 1f);
        }

        static Sprite LoadSpriteFromFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                if (!texture.LoadImage(File.ReadAllBytes(path))) return null;
                var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                sprite.hideFlags = HideFlags.HideAndDontSave;
                return sprite;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[背景] 读取 {path} 失败：{e.Message}");
                return null;
            }
        }

        Camera MainCamera => mainCamera != null ? mainCamera : (mainCamera = Camera.main);

        void Update()
        {
            if (comboPulse > 0f) comboPulse = Mathf.Max(0f, comboPulse - Time.deltaTime * 2.5f);
            if (comboBreakFlash > 0f) comboBreakFlash = Mathf.Max(0f, comboBreakFlash - Time.deltaTime * 1.8f);
            UpdateTimingPopups();
            UpdateComboDisplay();
            HandleInput();
            UpdateEffects();
            if (!ready || clock == null) return;

            var nowSeconds = clock.SongTime;
            if (System.Math.Abs(nowSeconds - lastTime) > secondsPerBeat) ResetTo(nowSeconds);
            lastTime = nowSeconds;

            var apothem = hexRadius * 0.866f;
            var flightStart = apothem - spawnRadius;
            var birthLength = birthBeats * unitsPerBeat;
            var unitsPerSecond = unitsPerBeat / Mathf.Max(0.01f, secondsPerBeat);

            while (nextIndex < chart.TotalNotes && (chart.Notes[nextIndex].StartSeconds - nowSeconds) * unitsPerSecond <= flightStart + birthLength)
            {
                Activate(nextIndex);
                nextIndex++;
            }

            for (var i = active.Count - 1; i >= 0; i--)
            {
                var view = active[i];
                if (view.Vanish)
                {
                    Release(i);
                    continue;
                }

                var note = view.Note;

                if (!view.Judged)
                {
                    if (nowSeconds - judgeOffsetSeconds >= note.StartSeconds + MissWindowSeconds) ApplyJudgment(view, GradeMiss);
                    else if (autoPlay && nowSeconds >= note.StartSeconds) ApplyJudgment(view, GradeBest);
                }
                if (view.Vanish)
                {
                    Release(i);
                    continue;
                }

                // 长条按持：手动模式提前松手 = 断连（Miss）
                if (note.IsHold && view.HoldActive && !autoPlay)
                {
                    var holdLane = Mathf.Clamp(note.Lane, 0, 5);
                    if (!IsLaneHeld(holdLane))
                    {
                        if (nowSeconds - judgeOffsetSeconds < note.EndSeconds - holdReleaseGrace)
                        {
                            view.HoldActive = false;
                            RegisterMiss(holdLane);
                            SpawnPopup(PadDirection(holdLane) * (hexRadius * 0.866f - 0.75f), missSprite);
                            Release(i);
                            continue;
                        }
                        view.HoldActive = false; // 按住到尾部，正常结束
                    }
                    else
                    {
                        padFlashTimer[holdLane] = Mathf.Max(padFlashTimer[holdLane], 0.08f); // 按住期间判定点持续亮
                    }
                }

                var lane = Mathf.Clamp(note.Lane, 0, 5);
                var radial = PadDirection(lane);
                var rotation = Quaternion.Euler(0f, 0f, PadAngleDeg[lane]);
                var noteWidth = 0.9f;
                var ageHead = (float)(note.StartSeconds - nowSeconds) * unitsPerSecond;
                var ageTail = (float)(note.EndSeconds - nowSeconds) * unitsPerSecond;

                float headDist;
                float headScale;
                float headAlpha;
                if (ageHead > flightStart)
                {
                    headDist = spawnRadius;
                    var t = Mathf.Clamp01((flightStart + birthLength - ageHead) / birthLength);
                    headScale = Mathf.Lerp(BirthScaleFrom, 1f, t * t * (3f - 2f * t));
                    headAlpha = t;
                }
                else
                {
                    headDist = apothem - ageHead;
                    headScale = 1f;
                    headAlpha = 1f;
                }

                if (note.IsHold)
                {
                    headDist = Mathf.Min(headDist, apothem);

                    var tailRaw = apothem - ageTail;
                    var tailDist = Mathf.Min(headDist, Mathf.Max(tailRaw, spawnRadius));

                    PlaceNote(view.Head, radial * headDist, rotation, noteWidth, 0.3f, headScale, headAlpha);

                    var bodyLength = headDist - tailDist;
                    if (bodyLength > 0.02f)
                    {
                        view.Body.gameObject.SetActive(true);
                        PlaceBody(view.Body, radial * ((headDist + tailDist) * 0.5f), rotation * Quaternion.Euler(0f, 0f, 90f), noteWidth * 0.55f, bodyLength);
                    }
                    else if (view.Body.gameObject.activeSelf)
                    {
                        view.Body.gameObject.SetActive(false);
                    }

                    view.Tail.gameObject.SetActive(true);
                    PlaceNote(view.Tail, radial * tailDist, rotation, noteWidth, 0.3f, headScale, headAlpha);

                    UpdateBothLine(view, apothem, headDist);
                    if (tailRaw > apothem + Overshoot) Release(i);
                }
                else
                {
                    PlaceNote(view.Head, radial * headDist, rotation, noteWidth, 0.3f, headScale, headAlpha);
                    UpdateBothLine(view, apothem, headDist);
                    if (headDist > apothem + Overshoot) Release(i);
                }
            }
        }

        void ResetTo(double nowSeconds)
        {
            for (var i = active.Count - 1; i >= 0; i--) Release(i);

            nextIndex = 0;
            while (nextIndex < chart.TotalNotes && chart.Notes[nextIndex].StartSeconds <= nowSeconds) nextIndex++;
            for (var i = 0; i < nextIndex; i++)
            {
                if (chart.Notes[i].EndSeconds > nowSeconds) Activate(i);
            }

            ResetCounters();
        }

        void Activate(int index)
        {
            var note = chart.Notes[index];
            var view = pool.Count > 0 ? pool.Pop() : CreateView();
            view.Root.SetActive(true);
            view.Note = note;
            view.Judged = false;
            view.Vanish = false;
            view.HoldActive = false;

            if (note.IsHold)
            {
                var holdSprite = note.IsDouble
                    ? (holdBothSprite != null ? holdBothSprite : holdHeadSprite)
                    : (holdHeadSprite != null ? holdHeadSprite : holdTailSprite);
                SetSprite(view.Head, holdSprite, new Color(0.78f, 0.92f, 1f));
                SetSprite(view.Tail, holdSprite, new Color(0.78f, 0.92f, 1f));
                SetSprite(view.Body, holdBodySprite, new Color(0.45f, 0.68f, 1f, 0.5f));
            }
            else
            {
                SetSprite(view.Head, note.IsDouble ? tapBothSprite : tapSprite, new Color(0.78f, 0.92f, 1f));
            }

            view.Body.gameObject.SetActive(false);
            view.Tail.gameObject.SetActive(false);
            view.BothLine.gameObject.SetActive(false);
            active.Add(view);
        }

        void Release(int index)
        {
            var view = active[index];
            view.Root.SetActive(false);
            pool.Push(view);
            active.RemoveAt(index);
        }

        void ApplyJudgment(NoteView view, int grade)
        {
            view.Judged = true;
            var lane = Mathf.Clamp(view.Note.Lane, 0, 5);
            var radial = PadDirection(lane);
            var apothem = hexRadius * 0.866f;

            if (grade == GradeMiss)
            {
                RegisterMiss(lane);
                SpawnPopup(radial * (apothem - 0.75f), missSprite);
                return;
            }

            if (grade == GradeBest) bestCount++;
            else if (grade == GradeCool) coolCount++;
            else goodCount++;
            combo++;
            if (combo > maxCombo) maxCombo = combo;
            comboPulse = 1f;
            padFlashTimer[lane] = Mathf.Max(padFlashTimer[lane], 0.12f); // 命中即亮判定点

            if (sfxSource != null) sfxSource.pitch = 1f + Mathf.Min(combo, 60) * 0.005f; // 连击越高音调越高
            PlayHitSound(hitSoundVolume);
            SpawnFx(radial * apothem);
            SpawnPopup(radial * (apothem - 0.75f), grade == GradeBest ? bestSprite : grade == GradeCool ? coolSprite : goodSprite);

            if (!view.Note.IsHold) view.Vanish = true;
            else view.HoldActive = true; // 长条进入按持状态
        }

        /// <summary>Miss / 长条断开统一处理：清零连击、判定点红闪、Miss 音效</summary>
        void RegisterMiss(int lane)
        {
            missCount++;
            if (combo > 0) comboBreakFlash = 1f;
            combo = 0;
            padMissTimer[lane] = 0.35f;
            if (missSource != null && hitSound != null) missSource.PlayOneShot(hitSound, 0.5f);
        }

        void TryJudgeByInput(int lane)
        {
            var now = clock.SongTime - judgeOffsetSeconds;
            var bestIndex = -1;
            var bestDelta = float.MaxValue;

            for (var i = 0; i < active.Count; i++)
            {
                var view = active[i];
                if (view.Judged || view.Note.Lane != lane) continue;

                var delta = (float)(now - view.Note.StartSeconds);
                if (Mathf.Abs(delta) > GoodWindowMs / 1000f) continue;
                if (Mathf.Abs(delta) < Mathf.Abs(bestDelta))
                {
                    bestDelta = delta;
                    bestIndex = i;
                }
            }
            if (bestIndex < 0)
            {
                PlayHitSound(hitSoundVolume * 0.3f);
                return;
            }

            var ms = Mathf.Abs(bestDelta) * 1000f;
            var grade = ms <= BestWindowMs ? GradeBest : ms <= CoolWindowMs ? GradeCool : GradeGood;

            // FAST / SLOW 统计与提示（偏差超过 8ms 才提示）
            if (bestDelta < -0.008f) fastCount++;
            else if (bestDelta > 0.008f) slowCount++;
            if (ms > 8f)
            {
                var laneIndex = Mathf.Clamp(active[bestIndex].Note.Lane, 0, 5);
                timingPopups.Add(new TimingPopup
                {
                    WorldPosition = PadDirection(laneIndex) * (hexRadius * 0.866f - 1.15f),
                    IsFast = bestDelta < 0f,
                    Age = 0f,
                });
            }

            ApplyJudgment(active[bestIndex], grade);
        }

        /// <summary>推进 FAST / SLOW 提示的淡出</summary>
        void UpdateTimingPopups()
        {
            for (var i = timingPopups.Count - 1; i >= 0; i--)
            {
                timingPopups[i].Age += Time.deltaTime;
                if (timingPopups[i].Age >= 0.5f) timingPopups.RemoveAt(i);
            }
        }

        void PlayHitSound(float volume)
        {
            if (sfxSource == null || hitSound == null || volume <= 0.001f) return;
            sfxSource.PlayOneShot(hitSound, volume);
        }

        void SpawnFx(Vector3 position)
        {
            if (hitFxFrames == null || hitFxFrames.Length == 0 || hitFxFrames[0] == null) return;

            var effect = RentEffect();
            effect.Frames = hitFxFrames;
            effect.IsPopup = false;
            effect.Life = hitFxFrames.Length * FrameDuration;
            effect.BaseScale = EffectWidth / Mathf.Max(0.0001f, hitFxFrames[0].bounds.size.x);
            InitEffect(effect, position, 13);
        }

        void SpawnPopup(Vector3 position, Sprite sprite)
        {
            if (sprite == null) return;

            var effect = RentEffect();
            effect.Frames = null;
            effect.IsPopup = true;
            effect.Life = PopupLife;
            effect.BaseScale = EffectWidth / Mathf.Max(0.0001f, sprite.bounds.size.x);
            InitEffect(effect, position, 14);
            effect.Renderer.sprite = sprite;
        }

        void InitEffect(Effect effect, Vector3 position, int sortingOrder)
        {
            effect.Age = 0f;
            effect.Renderer.gameObject.SetActive(true);
            effect.Renderer.sortingOrder = sortingOrder;
            effect.Renderer.color = Color.white;
            effect.Renderer.transform.localPosition = position;
            effect.Renderer.transform.localRotation = Quaternion.identity;
            effect.Renderer.transform.localScale = Vector3.one * effect.BaseScale;
            effects.Add(effect);
        }

        Effect RentEffect()
        {
            var renderer = effectPool.Count > 0 ? effectPool.Pop() : CreateEffectRenderer();
            return new Effect { Renderer = renderer };
        }

        SpriteRenderer CreateEffectRenderer()
        {
            var go = new GameObject("Effect");
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = SpriteFactory.Square();
            return renderer;
        }

        void UpdateEffects()
        {
            var delta = Time.deltaTime;
            for (var i = effects.Count - 1; i >= 0; i--)
            {
                var effect = effects[i];
                effect.Age += delta;
                var t = Mathf.Clamp01(effect.Age / effect.Life);

                if (effect.Frames != null)
                {
                    var index = Mathf.Min(effect.Frames.Length - 1, (int)(t * effect.Frames.Length));
                    effect.Renderer.sprite = effect.Frames[index];
                    var alpha = t < 0.75f ? 1f : Mathf.InverseLerp(1f, 0.75f, t);
                    effect.Renderer.color = new Color(1f, 1f, 1f, alpha);
                }
                else
                {
                    var pop = t < 0.3f ? Mathf.Lerp(0.6f, 1.12f, t / 0.3f) : Mathf.Lerp(1.12f, 1f, (t - 0.3f) / 0.7f);
                    effect.Renderer.transform.localScale = Vector3.one * (effect.BaseScale * pop);
                    var alpha = t < 0.6f ? 1f : Mathf.InverseLerp(1f, 0.6f, t);
                    effect.Renderer.color = new Color(1f, 1f, 1f, alpha);
                }

                if (effect.Age >= effect.Life)
                {
                    effect.Renderer.gameObject.SetActive(false);
                    effectPool.Push(effect.Renderer);
                    effects.RemoveAt(i);
                }
            }
        }

        void UpdateComboDisplay()
        {
            if (comboRoot == null) return;

            var visible = ready && combo >= ComboShowFrom && comboDigitSprites != null && comboDigitSprites.Length >= 10;
            if (comboRoot.activeSelf != visible) comboRoot.SetActive(visible);
            if (!visible)
            {
                lastComboShown = -1;
                return;
            }

            if (combo != lastComboShown)
            {
                comboTextCache = combo.ToString();
                lastComboShown = combo;
            }

            var reference = comboDigitSprites[0];
            var referenceSize = reference != null ? reference.bounds.size : Vector3.one;
            var digitWidth = ComboDigitHeight * referenceSize.x / Mathf.Max(0.0001f, referenceSize.y);

            var count = Mathf.Min(comboTextCache.Length, comboDigits.Count);
            var totalWidth = count * digitWidth + (count - 1) * ComboDigitGap;
            var startX = -totalWidth * 0.5f + digitWidth * 0.5f;

            for (var i = 0; i < comboDigits.Count; i++)
            {
                var digit = comboDigits[i];
                if (i >= count)
                {
                    if (digit.gameObject.activeSelf) digit.gameObject.SetActive(false);
                    continue;
                }

                var sprite = comboDigitSprites[comboTextCache[i] - '0'];
                digit.gameObject.SetActive(true);
                digit.sprite = sprite != null ? sprite : SpriteFactory.Square();
                var tint = comboBreakFlash > 0.01f
                    ? Color.Lerp(new Color(1f, 0.45f, 0.45f), Color.white, 1f - comboBreakFlash)
                    : Color.white;
                digit.color = new Color(tint.r, tint.g, tint.b, ComboDigitAlpha);
                var scale = ComboDigitHeight / Mathf.Max(0.0001f, digit.sprite.bounds.size.y);
                digit.transform.localScale = new Vector3(scale, scale, 1f);
                digit.transform.localPosition = new Vector3(startX + i * (digitWidth + ComboDigitGap), ComboCenterY, 0f);
            }

            if (comboTagSprite != null)
            {
                comboTag.gameObject.SetActive(true);
                comboTag.sprite = comboTagSprite;
                comboTag.color = new Color(1f, 1f, 1f, ComboTagAlpha);
                var tagScale = ComboTagWidth / Mathf.Max(0.0001f, comboTagSprite.bounds.size.x);
                comboTag.transform.localScale = new Vector3(tagScale, tagScale, 1f);
                comboTag.transform.localPosition = new Vector3(0f, ComboTagY, 0f);
            }
            else if (comboTag.gameObject.activeSelf)
            {
                comboTag.gameObject.SetActive(false);
            }

            var pulse = 1f + 0.12f * comboPulse * comboPulse;
            comboRoot.transform.localScale = new Vector3(pulse, pulse, 1f);
        }

        void HandleInput()
        {
            var keys = judgeKeys;
            if (keys != null)
            {
                for (var i = 0; i < padFlashes.Length && i < keys.Length; i++)
                {
                    if (Input.GetKeyDown(keys[i]))
                    {
                        padFlashTimer[i] = 0.18f;
                        if (ready && !autoPlay && clock != null && clock.IsRunning) TryJudgeByInput(i);
                    }
                }
            }

            for (var i = 0; i < padFlashes.Length; i++)
            {
                if (padMissTimer[i] > 0f)
                {
                    padMissTimer[i] -= Time.deltaTime;
                    padFlashes[i].gameObject.SetActive(true);
                    padFlashes[i].color = new Color(1f, 0.3f, 0.3f, 0.25f + 0.55f * Mathf.Clamp01(padMissTimer[i] / 0.35f));
                }
                else if (padFlashTimer[i] > 0f)
                {
                    padFlashTimer[i] -= Time.deltaTime;
                    var alpha = Mathf.Clamp01(padFlashTimer[i] / 0.18f) * 0.85f;
                    padFlashes[i].gameObject.SetActive(true);
                    padFlashes[i].color = new Color(0.55f, 0.95f, 1f, alpha);
                }
                else if (padFlashes[i].gameObject.activeSelf)
                {
                    padFlashes[i].gameObject.SetActive(false);
                }
            }

            HandleTouch();
        }

        bool IsLaneHeld(int lane)
        {
            var key = GetJudgeKey(lane);
            if (key != KeyCode.None && Input.GetKey(key)) return true;
            return laneTouchHeld[lane];
        }

        void HandleTouch()
        {
            for (var i = 0; i < laneTouchHeld.Length; i++) laneTouchHeld[i] = false;
            if (!touchEnabled) return;

            if (Input.touchCount > 0)
            {
                for (var i = 0; i < Input.touchCount; i++)
                {
                    var touch = Input.GetTouch(i);
                    var slot = touch.fingerId % MaxPointers;
                    var ended = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
                    var lane = ended ? -1 : PadAtScreenPoint(touch.position);
                    TriggerLane(slot, lane);
                    if (!ended && lane >= 0) laneTouchHeld[lane] = true;
                }
            }
            else
            {
                var lane = Input.GetMouseButton(0) ? PadAtScreenPoint(Input.mousePosition) : -1;
                TriggerLane(0, lane);
                if (lane >= 0) laneTouchHeld[lane] = true;
            }
        }

        /// <summary>指针进入某条轨道的落点分区即触发一次判定；滑过（搓）到别的分区同样触发</summary>
        void TriggerLane(int pointer, int lane)
        {
            if (pointer < 0 || pointer >= MaxPointers) return;

            var stored = lane >= 0 ? lane + 1 : 0;
            if (pointerLane[pointer] == stored) return;
            pointerLane[pointer] = stored;
            if (lane < 0) return;

            padFlashTimer[lane] = 0.18f;
            if (ready && !autoPlay && clock != null && clock.IsRunning) TryJudgeByInput(lane);
        }

        int SectorAt(Vector2 world)
        {
            var angle = Mathf.Atan2(world.y, world.x) * Mathf.Rad2Deg;
            var sector = -1;
            var sectorDelta = float.MaxValue;
            for (var i = 0; i < 6; i++)
            {
                var delta = Mathf.Abs(Mathf.DeltaAngle(angle, PadAngleDeg[i]));
                if (delta >= sectorDelta) continue;
                sector = i;
                sectorDelta = delta;
            }
            return sector;
        }

        float HexDistance(Vector2 world)
        {
            var distance = 0f;
            for (var i = 0; i < 6; i++) distance = Mathf.Max(distance, Vector3.Dot(world, PadDirection(i)));
            return distance;
        }

        /// <summary>屏幕坐标命中的轨道下标（-1 = 没命中）：判定点落点分区，滑过进入即触发</summary>
        int PadAtScreenPoint(Vector2 screenPoint)
        {
            var cam = MainCamera;
            if (cam == null) return -1;

            var world = cam.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, -cam.transform.position.z));

            if (innerScreenJudge)
            {
                var apothem = hexRadius * 0.866f;
                var inner = apothem * (landingInnerRatio > 0f ? landingInnerRatio : 0.9f);
                var outer = apothem * (landingOuterRatio > 0f ? landingOuterRatio : 1.3f);
                var distance = HexDistance(world);
                if (distance >= inner && distance <= outer) return SectorAt(world);
                return -1;
            }

            var nearest = -1;
            var nearestDistance = float.MaxValue;
            for (var i = 0; i < 6; i++)
            {
                var distance = Vector2.Distance(world, PadPosition(i));
                if (distance > touchRadius || distance >= nearestDistance) continue;
                nearest = i;
                nearestDistance = distance;
            }
            return nearest;
        }

        NoteView CreateView()
        {
            var root = new GameObject("Note");
            root.transform.SetParent(transform, false);
            return new NoteView
            {
                Root = root,
                Head = CreateQuad(root.transform, "Head", 11, new Color(0.78f, 0.92f, 1f)),
                Body = CreateQuad(root.transform, "Body", 10, new Color(0.45f, 0.68f, 1f, 0.5f)),
                Tail = CreateQuad(root.transform, "Tail", 11, new Color(0.78f, 0.92f, 1f)),
                BothLine = CreateQuad(root.transform, "BothLine", 9, new Color(1f, 1f, 1f, 0.75f)),
            };
        }

        void SetSprite(SpriteRenderer renderer, Sprite sprite, Color fallbackColor)
        {
            renderer.sprite = sprite != null ? sprite : SpriteFactory.Square();
            renderer.color = sprite != null ? Color.white : fallbackColor;
        }

        void PlaceNote(SpriteRenderer renderer, Vector3 position, Quaternion rotation, float width, float fallbackHeight, float scale, float alpha)
        {
            renderer.transform.localPosition = position;
            renderer.transform.localRotation = rotation;

            Vector3 baseScale;
            if (renderer.sprite == SpriteFactory.Square())
            {
                baseScale = new Vector3(width, fallbackHeight, 1f);
            }
            else
            {
                baseScale = Vector3.one * (width / Mathf.Max(0.0001f, renderer.sprite.bounds.size.x));
                renderer.color = new Color(1f, 1f, 1f, alpha);
            }
            renderer.transform.localScale = baseScale * scale;
        }

        void PlaceBody(SpriteRenderer renderer, Vector3 position, Quaternion rotation, float width, float length)
        {
            renderer.transform.localPosition = position;
            renderer.transform.localRotation = rotation;
            if (renderer.sprite == SpriteFactory.Square())
            {
                renderer.transform.localScale = new Vector3(width, length, 1f);
                return;
            }
            var size = renderer.sprite.bounds.size;
            renderer.transform.localScale = new Vector3(width / Mathf.Max(0.0001f, size.x), length / Mathf.Max(0.0001f, size.y), 1f);
        }

        void UpdateBothLine(NoteView view, float apothem, float headDist)
        {
            var note = view.Note;
            var show = bothLineSprite != null && note.PartnerLane >= 0 && note.Lane < note.PartnerLane
                       && headDist >= apothem - BothLineWindow;
            if (!show)
            {
                if (view.BothLine.gameObject.activeSelf) view.BothLine.gameObject.SetActive(false);
                return;
            }

            var a = PadPosition(note.Lane);
            var b = PadPosition(note.PartnerLane);
            var delta = b - a;
            var size = bothLineSprite.bounds.size;
            view.BothLine.gameObject.SetActive(true);
            view.BothLine.sprite = bothLineSprite;
            view.BothLine.color = new Color(1f, 1f, 1f, 0.75f);
            view.BothLine.transform.localPosition = (a + b) * 0.5f;
            view.BothLine.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            view.BothLine.transform.localScale = new Vector3(delta.magnitude / Mathf.Max(0.0001f, size.x), 0.75f / Mathf.Max(0.0001f, size.y), 1f);
        }

        void BuildJudgeZones()
        {
            var root = new GameObject("JudgeZones");
            root.transform.SetParent(boardRoot, false);
            judgeZoneRoot = root.transform;

            var innerRatio = landingInnerRatio > 0f ? landingInnerRatio : 0.9f;
            var outerRatio = landingOuterRatio > 0f ? landingOuterRatio : 1.3f;

            for (var i = 0; i < 6; i++)
            {
                var leftAngle = (PadAngleDeg[i] - 30f) * Mathf.Deg2Rad;
                var rightAngle = (PadAngleDeg[i] + 30f) * Mathf.Deg2Rad;
                var left = new Vector3(Mathf.Cos(leftAngle), Mathf.Sin(leftAngle), 0f);
                var right = new Vector3(Mathf.Cos(rightAngle), Mathf.Sin(rightAngle), 0f);

                var innerLeft = left * (hexRadius * innerRatio);
                var innerRight = right * (hexRadius * innerRatio);
                var outerLeft = left * (hexRadius * outerRatio);
                var outerRight = right * (hexRadius * outerRatio);

                AddZoneLine(innerLeft, innerRight);
                AddZoneLine(outerLeft, outerRight);
                AddZoneLine(innerLeft, outerLeft);
                AddZoneLine(innerRight, outerRight);
            }

            judgeZoneRoot.gameObject.SetActive(judgeZonesVisible);
        }

        void AddZoneLine(Vector3 from, Vector3 to)
        {
            var line = CreateQuad(judgeZoneRoot, "ZoneLine", -6, judgeZoneColor);
            var delta = to - from;
            line.transform.localPosition = (from + to) * 0.5f;
            line.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f);
            line.transform.localScale = new Vector3(judgeZoneLineWidth, delta.magnitude, 1f);
        }

        SpriteRenderer CreateQuad(Transform parent, string name, int sortingOrder, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = SpriteFactory.Square();
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        static Vector3 PadDirection(int lane)
        {
            var radians = PadAngleDeg[Mathf.Clamp(lane, 0, 5)] * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f);
        }

        Vector3 PadPosition(int lane) => PadDirection(lane) * (hexRadius * 0.866f);

        void BuildVisuals()
        {
            var cam = MainCamera;
            var viewHeight = cam != null && cam.orthographic ? cam.orthographicSize * 2f : 11.6f;
            var viewWidth = viewHeight * UiTheme.Width / Mathf.Max(1, UiTheme.Height);

            if (backgroundSprite != null)
            {
                backgroundRenderer = CreateQuad(transform, "Background", -10, new Color(0.65f, 0.65f, 0.75f));
                backgroundRenderer.sprite = backgroundSprite;
                var size = backgroundSprite.bounds.size;
                var scale = Mathf.Max(viewWidth / Mathf.Max(0.0001f, size.x), viewHeight / Mathf.Max(0.0001f, size.y)) * 1.02f;
                backgroundRenderer.transform.localPosition = new Vector3(0f, 0f, 2f);
                backgroundRenderer.transform.localScale = new Vector3(scale, scale, 1f);
            }

            var board = new GameObject("Board");
            board.transform.SetParent(transform, false);
            boardRoot = board.transform;
            board.SetActive(ready);

            var hexApothem = hexRadius * 0.866f;
            if (hexFrameSprite != null)
            {
                var size = hexFrameSprite.bounds.size;
                var frameScale = hexRadius * 2f / Mathf.Max(0.0001f, Mathf.Max(size.x, size.y));

                var frame = CreateQuad(boardRoot, "HexFrame", -5, Color.white);
                frame.sprite = hexFrameSprite;
                frame.transform.localPosition = new Vector3(0f, 0f, 1f);
                frame.transform.localScale = new Vector3(frameScale, frameScale, 1f);

                if (showSpawnZone)
                {
                    var zone = CreateQuad(boardRoot, "SpawnZone", -4, new Color(0.6f, 0.9f, 1f, 0.22f));
                    zone.sprite = hexFrameSprite;
                    var zoneScale = frameScale * (spawnRadius / hexApothem);
                    zone.transform.localPosition = new Vector3(0f, 0f, 0.5f);
                    zone.transform.localScale = new Vector3(zoneScale, zoneScale, 1f);
                }
            }

            BuildJudgeZones();

            for (var i = 0; i < padFlashes.Length; i++)
            {
                var flash = CreateQuad(transform, "PadFlash" + (i + 1), 3, new Color(0.55f, 0.95f, 1f, 0f));
                flash.transform.localPosition = PadPosition(i);
                flash.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
                flash.gameObject.SetActive(false);
                padFlashes[i] = flash;
            }

            comboRoot = new GameObject("ComboDisplay");
            comboRoot.transform.SetParent(transform, false);
            comboRoot.transform.localPosition = Vector3.zero;
            comboRoot.SetActive(false);

            for (var i = 0; i < ComboMaxDigits; i++)
            {
                var digit = CreateQuad(comboRoot.transform, "Digit" + i, 2, Color.white);
                digit.gameObject.SetActive(false);
                comboDigits.Add(digit);
            }

            comboTag = CreateQuad(comboRoot.transform, "ComboTag", 2, new Color(1f, 1f, 1f, ComboTagAlpha));
            comboTag.gameObject.SetActive(false);
        }

        void OnGUI()
        {
            UiTheme.BeginGui();
            if (!ready) return;

            percentStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                alignment = TextAnchor.UpperRight,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.92f, 0.97f, 1f, 0.92f) },
            };

            var accuracy = Accuracy;
            if (!Mathf.Approximately(accuracy, lastAccuracy))
            {
                lastAccuracy = accuracy;
                percentText = (accuracy * 100f).ToString("0.00") + "%";
            }
            GUI.Label(new Rect(UiTheme.Width - 340f, 16f, 320f, 40f), percentText, percentStyle);

            // FAST / SLOW 提示（世界坐标 → 设计空间坐标）
            if (timingPopups.Count > 0)
            {
                var cam = MainCamera;
                if (cam != null)
                {
                    fastSlowStyle ??= new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 18,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                    };
                    var scale = Mathf.Max(0.0001f, UiTheme.GuiScale);
                    for (var i = 0; i < timingPopups.Count; i++)
                    {
                        var popup = timingPopups[i];
                        var screenPos = cam.WorldToScreenPoint(popup.WorldPosition);
                        if (screenPos.z <= 0f) continue;
                        var alpha = Mathf.Clamp01(1f - popup.Age / 0.5f);
                        fastSlowStyle.normal.textColor = popup.IsFast
                            ? new Color(0.65f, 0.85f, 1f, alpha)
                            : new Color(1f, 0.72f, 0.5f, alpha);
                        var designX = screenPos.x / scale;
                        var designY = UiTheme.Height - screenPos.y / scale;
                        GUI.Label(new Rect(designX - 50f, designY - 10f, 100f, 20f), popup.IsFast ? "FAST" : "SLOW", fastSlowStyle);
                    }
                }
            }
        }
    }
}
