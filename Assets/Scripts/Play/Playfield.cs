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
        const float PopupLife = 0.45f;
        const float EffectWidth = 1.7f;
        const int ComboShowFrom = 4;
        const float ComboDigitHeight = 0.6f;
        const float ComboDigitGap = 0.05f;
        const float ComboDigitAlpha = 0.7f;
        const float ComboTagAlpha = 0.85f;
        const float ComboTagWidth = 0.85f;
        const float ComboCenterY = -0.12f;
        const float ComboTagY = 0.55f;
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
        [SerializeField] bool showPadLabels = true;

        [Header("触摸输入")]
        [SerializeField] bool touchEnabled = true;
        [SerializeField] float touchRadius = 1.1f;

        [Header("启动")]
        [SerializeField] bool autoPlay = true;

        sealed class NoteView
        {
            public GameObject Root;
            public SpriteRenderer Head;
            public SpriteRenderer Body;
            public SpriteRenderer Tail;
            public SpriteRenderer BothLine;
            public ChartNote Note;
            public bool Judged;
            public bool Vanish;
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

        ChartData chart;
        int nextIndex;
        double lastBeat;
        bool ready;
        SpriteRenderer backgroundRenderer;
        GameObject comboRoot;
        SpriteRenderer comboTag;
        AudioSource sfxSource;
        Camera mainCamera;

        string[] padLabelTexts;
        string percentText = "0.00%";
        string comboTextCache = "0";
        int lastComboShown = -1;
        float lastAccuracy = -1f;

        GUIStyle labelStyle;
        GUIStyle percentStyle;

        int bestCount;
        int coolCount;
        int goodCount;
        int missCount;
        int combo;
        int maxCombo;
        float comboPulse;

        void Start()
        {
            if (clock == null) clock = FindObjectOfType<SongClock>();
            mainCamera = Camera.main;
            BuildVisuals();
            BuildPadLabels();

            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;
        }

        public void LoadSong(SongInfo song)
        {
            ClearForSelect();

            if (song == null || string.IsNullOrEmpty(song.ChartPath) || !File.Exists(song.ChartPath))
            {
                Debug.LogError("[谱面] 歌曲文件无效，无法装载");
                return;
            }

            chart = ChartParser.LoadFile(song.ChartPath);
            foreach (var error in chart.Errors) Debug.LogWarning("[谱面] " + error);
            Debug.Log($"[谱面] {song.Name}：{chart.Notes.Count} 个音符，其中长条 {chart.HoldCount} 个");

            nextIndex = 0;
            lastBeat = 0.0;
            ready = chart.Notes.Count > 0;
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

            chart = null;
            ready = false;
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
        }

        public int BestCount => bestCount;
        public int CoolCount => coolCount;
        public int GoodCount => goodCount;
        public int MissCount => missCount;
        public int MaxCombo => maxCombo;
        public int TotalNotes => chart != null ? chart.Notes.Count : 0;

        public float Accuracy
        {
            get
            {
                if (chart == null || chart.Notes.Count == 0) return 0f;
                var weight = bestCount + coolCount * 0.8f + goodCount * 0.6f;
                return weight / chart.Notes.Count;
            }
        }

        public int Score => Mathf.RoundToInt(Accuracy * 1000000f);

        public float NoteSpeed => unitsPerBeat;

        public void SetNoteSpeed(float value) => unitsPerBeat = Mathf.Clamp(value, 0.6f, 6f);

        public void SetAutoPlay(bool value) => autoPlay = value;

        public KeyCode GetJudgeKey(int lane)
        {
            if (judgeKeys == null || lane < 0 || lane >= judgeKeys.Length) return KeyCode.None;
            return judgeKeys[lane];
        }

        public void SetJudgeKey(int lane, KeyCode key)
        {
            if (judgeKeys == null || lane < 0 || lane >= judgeKeys.Length) return;
            judgeKeys[lane] = key;
            BuildPadLabels();
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
            var viewWidth = viewHeight * Screen.width / Mathf.Max(1, Screen.height);
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
            UpdateComboDisplay();
            HandleInput();
            UpdateEffects();
            if (!ready || clock == null) return;

            var beat = clock.Beat;
            if (System.Math.Abs(beat - lastBeat) > 1.0) ResetTo(beat);
            lastBeat = beat;

            var apothem = hexRadius * 0.866f;
            var flightStart = apothem - spawnRadius;
            var birthLength = birthBeats * unitsPerBeat;

            while (nextIndex < chart.Notes.Count && (chart.Notes[nextIndex].StartBeat - beat) * unitsPerBeat <= flightStart + birthLength)
            {
                Activate(nextIndex);
                nextIndex++;
            }

            var nowSeconds = clock.SongTime;

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
                    var noteSeconds = clock.BeatToSeconds(note.StartBeat);
                    if (nowSeconds >= noteSeconds + MissWindowSeconds) ApplyJudgment(view, GradeMiss);
                    else if (autoPlay && nowSeconds >= noteSeconds) ApplyJudgment(view, GradeBest);
                }
                if (view.Vanish)
                {
                    Release(i);
                    continue;
                }

                var lane = Mathf.Clamp(note.Lane, 0, 5);
                var radial = PadDirection(lane);
                var rotation = Quaternion.Euler(0f, 0f, PadAngleDeg[lane]);
                var noteWidth = 0.9f;
                var ageHead = (float)(note.StartBeat - beat) * unitsPerBeat;
                var ageTail = (float)(note.EndBeat - beat) * unitsPerBeat;

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
                    var tailEmerged = tailRaw > spawnRadius;
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

                    if (tailEmerged)
                    {
                        view.Tail.gameObject.SetActive(true);
                        PlaceNote(view.Tail, radial * tailDist, rotation, noteWidth, 0.3f, 1f, 1f);
                    }
                    else if (view.Tail.gameObject.activeSelf)
                    {
                        view.Tail.gameObject.SetActive(false);
                    }

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

        void ResetTo(double beat)
        {
            for (var i = active.Count - 1; i >= 0; i--) Release(i);

            nextIndex = 0;
            while (nextIndex < chart.Notes.Count && chart.Notes[nextIndex].StartBeat <= beat) nextIndex++;
            for (var i = 0; i < nextIndex; i++)
            {
                if (chart.Notes[i].EndBeat > beat) Activate(i);
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
                missCount++;
                combo = 0;
                SpawnPopup(radial * (apothem - 0.75f), missSprite);
                return;
            }

            if (grade == GradeBest) bestCount++;
            else if (grade == GradeCool) coolCount++;
            else goodCount++;
            combo++;
            if (combo > maxCombo) maxCombo = combo;
            comboPulse = 1f;

            PlayHitSound(hitSoundVolume);
            SpawnFx(radial * apothem);
            SpawnPopup(radial * (apothem - 0.75f), grade == GradeBest ? bestSprite : grade == GradeCool ? coolSprite : goodSprite);
            if (!view.Note.IsHold) view.Vanish = true;
        }

        void TryJudgeByInput(int lane)
        {
            var now = clock.SongTime;
            var bestIndex = -1;
            var bestDelta = float.MaxValue;

            for (var i = 0; i < active.Count; i++)
            {
                var view = active[i];
                if (view.Judged || view.Note.Lane != lane) continue;

                var delta = Mathf.Abs((float)(clock.BeatToSeconds(view.Note.StartBeat) - now));
                if (delta > GoodWindowMs / 1000f) continue;
                if (delta < bestDelta)
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

            var ms = bestDelta * 1000f;
            var grade = ms <= BestWindowMs ? GradeBest : ms <= CoolWindowMs ? GradeCool : GradeGood;
            ApplyJudgment(active[bestIndex], grade);
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
                digit.color = new Color(1f, 1f, 1f, ComboDigitAlpha);
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
                if (padFlashTimer[i] > 0f)
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

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                autoPlay = !autoPlay;
                Debug.Log(autoPlay ? "[模式] 自动演示" : "[模式] 手动游玩");
            }
        }

        void HandleTouch()
        {
            if (!touchEnabled) return;

            if (Input.touchCount > 0)
            {
                for (var i = 0; i < Input.touchCount; i++)
                {
                    var touch = Input.GetTouch(i);
                    if (touch.phase == TouchPhase.Began) TryPadAtScreenPoint(touch.position);
                }
            }
            else if (Input.GetMouseButtonDown(0))
            {
                TryPadAtScreenPoint(Input.mousePosition);
            }
        }

        void TryPadAtScreenPoint(Vector2 screenPoint)
        {
            var cam = MainCamera;
            if (cam == null) return;

            var world = cam.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, -cam.transform.position.z));

            var nearest = -1;
            var nearestDistance = float.MaxValue;
            for (var i = 0; i < 6; i++)
            {
                var distance = Vector2.Distance(world, PadPosition(i));
                if (distance > touchRadius || distance >= nearestDistance) continue;
                nearest = i;
                nearestDistance = distance;
            }
            if (nearest < 0) return;

            padFlashTimer[nearest] = 0.18f;
            if (ready && !autoPlay && clock != null && clock.IsRunning) TryJudgeByInput(nearest);
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
            var viewWidth = viewHeight * Screen.width / Mathf.Max(1, Screen.height);

            if (backgroundSprite != null)
            {
                backgroundRenderer = CreateQuad(transform, "Background", -10, new Color(0.65f, 0.65f, 0.75f));
                backgroundRenderer.sprite = backgroundSprite;
                var size = backgroundSprite.bounds.size;
                var scale = Mathf.Max(viewWidth / Mathf.Max(0.0001f, size.x), viewHeight / Mathf.Max(0.0001f, size.y)) * 1.02f;
                backgroundRenderer.transform.localPosition = new Vector3(0f, 0f, 2f);
                backgroundRenderer.transform.localScale = new Vector3(scale, scale, 1f);
            }

            var hexApothem = hexRadius * 0.866f;
            if (hexFrameSprite != null)
            {
                var size = hexFrameSprite.bounds.size;
                var frameScale = hexRadius * 2f / Mathf.Max(0.0001f, Mathf.Max(size.x, size.y));

                var frame = CreateQuad(transform, "HexFrame", -5, Color.white);
                frame.sprite = hexFrameSprite;
                frame.transform.localPosition = new Vector3(0f, 0f, 1f);
                frame.transform.localScale = new Vector3(frameScale, frameScale, 1f);

                if (showSpawnZone)
                {
                    var zone = CreateQuad(transform, "SpawnZone", -4, new Color(0.6f, 0.9f, 1f, 0.22f));
                    zone.sprite = hexFrameSprite;
                    var zoneScale = frameScale * (spawnRadius / hexApothem);
                    zone.transform.localPosition = new Vector3(0f, 0f, 0.5f);
                    zone.transform.localScale = new Vector3(zoneScale, zoneScale, 1f);
                }
            }

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

        void BuildPadLabels()
        {
            padLabelTexts = new string[6];
            var showKeys = Application.platform != RuntimePlatform.Android;
            for (var i = 0; i < 6; i++)
            {
                padLabelTexts[i] = showKeys ? $"{i + 1} ({KeyLabel(i)})" : (i + 1).ToString();
            }
        }

        void OnGUI()
        {
            if (!ready) return;
            var cam = MainCamera;
            if (cam == null) return;

            labelStyle ??= UiTheme.PadLabelStyle();
            percentStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                alignment = TextAnchor.UpperRight,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.92f, 0.97f, 1f, 0.92f) },
            };

            if (showPadLabels && padLabelTexts != null)
            {
                for (var i = 0; i < 6; i++)
                {
                    var padScreen = cam.WorldToScreenPoint(PadPosition(i) * 0.82f);
                    if (padScreen.z <= 0f) continue;
                    GUI.Label(new Rect(padScreen.x - 45f, Screen.height - padScreen.y - 12f, 90f, 24f), padLabelTexts[i], labelStyle);
                }
            }

            var accuracy = Accuracy;
            if (!Mathf.Approximately(accuracy, lastAccuracy))
            {
                lastAccuracy = accuracy;
                percentText = (accuracy * 100f).ToString("0.00") + "%";
            }
            GUI.Label(new Rect(Screen.width - 340f, 16f, 320f, 40f), percentText, percentStyle);
        }

        string KeyLabel(int index)
        {
            var key = GetJudgeKey(index);
            if (key == KeyCode.None) return "-";
            if (key == KeyCode.Comma) return ",";
            if (key == KeyCode.Period) return ".";
            if (key == KeyCode.Semicolon) return ";";
            if (key == KeyCode.Slash) return "/";
            if (key == KeyCode.Quote) return "'";
            return key.ToString();
        }
    }
}
