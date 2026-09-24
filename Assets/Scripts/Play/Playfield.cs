using System.Collections.Generic;
using UnityEngine;
using RhythmPlayer.Core;

namespace RhythmPlayer.Play
{
    /// 六边形演奏面板：左列上中下 = 键 1/2/3，右列上中下 = 键 4/5/6。
    /// 音符在中心小六边形边界"长出来"，向外飞至边缘判定点；到达判定点的时间即拍点。
    /// 判定窗：±80ms = Best，±120ms = Cool，±160ms = Good，超过或没打 = Miss。
    /// 默认自动演示（Tab 切换到手动游玩）。
    public sealed class Playfield : MonoBehaviour
    {
        // 键位 1-6 → 六边形角度（度）：120°=左上, 180°=左中, 240°=左下, 60°=右上, 0°=右中, 300°=右下
        static readonly float[] PadAngleDeg = { 120f, 180f, 240f, 60f, 0f, 300f };
        const float Overshoot = 0.4f;           // 音符越过判定点后多远消失
        const float BothLineWindow = 0.9f;      // 双押连线提前出现距离
        const float BirthScaleFrom = 0.2f;      // 浮现动画起始缩放
        const float MissWindowSeconds = 0.16f;  // 超过该时间未打中即判 Miss
        const float BestWindowMs = 80f;
        const float CoolWindowMs = 120f;
        const float GoodWindowMs = 160f;
        const float FrameDuration = 0.045f;     // 打击特效每帧时长
        const float PopupLife = 0.45f;          // 判定字存活时长
        const float EffectWidth = 1.7f;         // 特效/判定字基准宽度（世界单位）

        const int GradeBest = 0;
        const int GradeCool = 1;
        const int GradeGood = 2;
        const int GradeMiss = 3;

        [Header("引用")]
        [SerializeField] SongClock clock;
        [SerializeField] TextAsset chartAsset;

        [Header("皮肤（留空则纯色矩形）")]
        [SerializeField] Sprite tapSprite;
        [SerializeField] Sprite tapBothSprite;
        [SerializeField] Sprite holdHeadSprite;
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

        [Header("六边形布局（世界单位）")]
        [Tooltip("外六边形中心到顶点的距离")]
        [SerializeField] float hexRadius = 4.3f;
        [Tooltip("中心出生区（小六边形）的判定点距离，音符从这里长出来")]
        [SerializeField] float spawnRadius = 0.9f;
        [Tooltip("显示中心出生区轮廓")]
        [SerializeField] bool showSpawnZone = true;

        [Header("手感")]
        [Tooltip("每拍飞行距离，越大越快")]
        [SerializeField] float unitsPerBeat = 2.4f;
        [Tooltip("出生动画时长（拍）")]
        [SerializeField] float birthBeats = 0.8f;

        [Header("按键映射（左列上中下 1/2/3，右列上中下 4/5/6）")]
        [SerializeField] KeyCode[] judgeKeys = { KeyCode.E, KeyCode.D, KeyCode.C, KeyCode.I, KeyCode.K, KeyCode.Comma };
        [SerializeField] bool showPadLabels = true;

        [Header("启动")]
        [SerializeField] bool autoStart = true;
        [Tooltip("自动演示：自动在拍点打出 Best；Tab 切换手动")]
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
        readonly SpriteRenderer[] padFlashes = new SpriteRenderer[6];
        readonly float[] padFlashTimer = new float[6];
        ChartData chart;
        int nextIndex;
        double lastBeat;
        bool ready;
        string[] padLabelTexts;
        GUIStyle labelStyle;
        GUIStyle statsStyle;
        int bestCount;
        int coolCount;
        int goodCount;
        int missCount;
        int combo;

        void Start()
        {
            if (clock == null) clock = FindObjectOfType<SongClock>();
            QualitySettings.vSyncCount = 1;
            BuildVisuals();
            BuildPadLabels();

            chart = ChartParser.Parse(chartAsset);
            if (chart == null || chart.Notes.Count == 0)
            {
                Debug.LogError("谱面为空：检查 Playfield 的 chartAsset 是否已指定");
                return;
            }

            foreach (var error in chart.Errors) Debug.LogWarning("[谱面] " + error);
            Debug.Log($"[谱面] {chart.Notes.Count} 个音符，其中长条 {chart.HoldCount} 个");
            ready = true;

            if (autoStart && clock != null) clock.PlayFrom(0.0);
        }

        void Update()
        {
            HandleInput();
            UpdateEffects();
            if (!ready || clock == null) return;

            var beat = clock.Beat;
            if (System.Math.Abs(beat - lastBeat) > 1.0) ResetTo(beat);
            lastBeat = beat;

            var apothem = hexRadius * 0.866f;
            var flightStart = apothem - spawnRadius;      // 出生区边界 → 判定点的飞行距离
            var birthLength = birthBeats * unitsPerBeat;  // 出生动画对应的距离

            // 音符进入出生动画时才激活
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

                // 判定：到期未打 → Miss；自动演示则在拍点直接判 Best
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
                    // 出生期：停在出生区边界上，由小变大
                    headDist = spawnRadius;
                    var t = Mathf.Clamp01((flightStart + birthLength - ageHead) / birthLength);
                    headScale = Mathf.Lerp(BirthScaleFrom, 1f, t * t * (3f - 2f * t));
                    headAlpha = t;
                }
                else
                {
                    // 飞行期：从出生区边界飞向判定点，越过判定点后继续外飞一小段
                    headDist = apothem - ageHead;
                    headScale = 1f;
                    headAlpha = 1f;
                }

                if (note.IsHold)
                {
                    headDist = Mathf.Min(headDist, apothem); // 头到达判定点后钉住，等待被"消耗"

                    var tailRaw = apothem - ageTail;
                    var tailEmerged = tailRaw > spawnRadius;
                    var tailDist = Mathf.Min(headDist, Mathf.Max(tailRaw, spawnRadius)); // 尾没长出来前贴在出生区边界

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

            bestCount = 0;
            coolCount = 0;
            goodCount = 0;
            missCount = 0;
            combo = 0;
        }

        void Activate(int index)
        {
            var note = chart.Notes[index];
            var view = pool.Count > 0 ? pool.Pop() : CreateView();
            view.Root.SetActive(true);
            view.Note = note;
            view.Judged = false;
            view.Vanish = false;

            SetSprite(view.Head, note.IsHold ? holdHeadSprite : (note.IsDouble ? tapBothSprite : tapSprite), new Color(0.78f, 0.92f, 1f));
            view.Body.gameObject.SetActive(false);
            view.Tail.gameObject.SetActive(false);
            view.BothLine.gameObject.SetActive(false);

            if (note.IsHold) SetSprite(view.Body, holdBodySprite, new Color(0.45f, 0.68f, 1f, 0.5f));
            if (note.IsHold) SetSprite(view.Tail, holdTailSprite, new Color(0.78f, 0.92f, 1f));

            active.Add(view);
        }

        void Release(int index)
        {
            var view = active[index];
            view.Root.SetActive(false);
            pool.Push(view);
            active.RemoveAt(index);
        }

        // ---------- 判定 ----------

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
            if (bestIndex < 0) return;

            var ms = bestDelta * 1000f;
            var grade = ms <= BestWindowMs ? GradeBest : ms <= CoolWindowMs ? GradeCool : GradeGood;
            ApplyJudgment(active[bestIndex], grade);
        }

        // ---------- 特效 ----------

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

        // ---------- 输入与视觉 ----------

        void HandleInput()
        {
            for (var i = 0; i < padFlashes.Length; i++)
            {
                if (judgeKeys != null && i < judgeKeys.Length && Input.GetKeyDown(judgeKeys[i]))
                {
                    padFlashTimer[i] = 0.18f;
                    if (ready && !autoPlay && clock != null) TryJudgeByInput(i);
                }

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

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                autoPlay = !autoPlay;
                Debug.Log(autoPlay ? "[模式] 自动演示" : "[模式] 手动游玩");
            }
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
            var cam = Camera.main;
            var viewHeight = cam != null && cam.orthographic ? cam.orthographicSize * 2f : 11.6f;
            var viewWidth = viewHeight * Screen.width / Mathf.Max(1, Screen.height);

            if (backgroundSprite != null)
            {
                var background = CreateQuad(transform, "Background", -10, new Color(0.65f, 0.65f, 0.75f));
                background.sprite = backgroundSprite;
                var size = backgroundSprite.bounds.size;
                var scale = Mathf.Max(viewWidth / Mathf.Max(0.0001f, size.x), viewHeight / Mathf.Max(0.0001f, size.y)) * 1.02f;
                background.transform.localPosition = new Vector3(0f, 0f, 2f);
                background.transform.localScale = new Vector3(scale, scale, 1f);
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
        }

        void BuildPadLabels()
        {
            padLabelTexts = new string[6];
            for (var i = 0; i < 6; i++) padLabelTexts[i] = $"{i + 1} ({KeyLabel(i)})";
        }

        void OnGUI()
        {
            var cam = Camera.main;
            if (cam == null) return;

            labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.85f) },
            };
            statsStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                alignment = TextAnchor.UpperRight,
                normal = { textColor = new Color(1f, 1f, 1f, 0.9f) },
            };

            if (showPadLabels && padLabelTexts != null)
            {
                for (var i = 0; i < 6; i++)
                {
                    var screen = cam.WorldToScreenPoint(PadPosition(i) * 0.82f);
                    if (screen.z <= 0f) continue;
                    GUI.Label(new Rect(screen.x - 45f, Screen.height - screen.y - 12f, 90f, 24f), padLabelTexts[i], labelStyle);
                }
            }

            var mode = autoPlay ? "自动演示" : "手动游玩";
            GUI.Label(new Rect(Screen.width - 640f, 16f, 620f, 30f),
                $"{mode}（Tab 切换）    Best {bestCount}  Cool {coolCount}  Good {goodCount}  Miss {missCount}    Combo {combo}", statsStyle);
        }

        string KeyLabel(int index)
        {
            if (judgeKeys == null || index >= judgeKeys.Length) return "-";
            var key = judgeKeys[index];
            if (key == KeyCode.Comma) return ",";
            if (key == KeyCode.Period) return ".";
            return key.ToString();
        }
    }
}
