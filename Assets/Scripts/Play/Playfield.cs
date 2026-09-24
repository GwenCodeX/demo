using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RhythmPlayer.Core;
using RhythmPlayer.UI;

namespace RhythmPlayer.Play
{
    /// <summary>
    /// 六边形演奏面板。
    /// 布局：左列上中下 = 键 1/2/3，右列上中下 = 键 4/5/6（对应六边形六个边的中点）。
    /// 动画：音符在中心小六边形边界"长出来"（由小变大），随后向外飞向判定点；
    ///       到达判定点的瞬间即拍点，打中即消失并播放打击特效。
    /// 判定：±80ms = Best，±120ms = Cool，±160ms = Good，超过或没打 = Miss。
    /// 输入：键盘（默认 E/D/C/I/K/,）、触摸（点判定点附近 = 按对应键）、自动演示（Tab 切换）。
    /// </summary>
    public sealed class Playfield : MonoBehaviour
    {
        // ===== 常量 =====

        /// <summary>键位 1-6 对应的六边形方向角（度）：120°=左上, 180°=左中, 240°=左下, 60°=右上, 0°=右中, 300°=右下</summary>
        static readonly float[] PadAngleDeg = { 120f, 180f, 240f, 60f, 0f, 300f };

        const float Overshoot = 0.4f;           // 音符越过判定点后多远消失（世界单位）
        const float BothLineWindow = 0.9f;      // 双押连线在到达判定点前多远开始显示
        const float BirthScaleFrom = 0.2f;      // 浮现动画的起始缩放（由小变大）
        const float MissWindowSeconds = 0.16f;  // 超过拍点该时长仍未打中 → Miss
        const float BestWindowMs = 80f;         // Best 判定窗（毫秒）
        const float CoolWindowMs = 120f;        // Cool 判定窗（毫秒）
        const float GoodWindowMs = 160f;        // Good 判定窗（毫秒）
        const float FrameDuration = 0.045f;     // 打击特效每帧时长（秒）
        const float PopupLife = 0.45f;          // 判定字存活时长（秒）
        const float EffectWidth = 1.7f;         // 特效/判定字的基准宽度（世界单位）

        // 判定等级
        const int GradeBest = 0;
        const int GradeCool = 1;
        const int GradeGood = 2;
        const int GradeMiss = 3;

        // ===== 序列化字段 =====

        [Header("引用")]
        [SerializeField] SongClock clock;

        [Header("皮肤（留空则用纯色矩形）")]
        [SerializeField] Sprite tapSprite;           // 单点音符
        [SerializeField] Sprite tapBothSprite;       // 双押音符
        [SerializeField] Sprite holdHeadSprite;      // 长条头部
        [SerializeField] Sprite holdBothSprite;      // 双押长条头部
        [SerializeField] Sprite holdBodySprite;      // 长条身体
        [SerializeField] Sprite holdTailSprite;      // 长条尾部
        [SerializeField] Sprite backgroundSprite;    // 默认背景（歌曲包没带背景时使用）
        [SerializeField] Sprite hexFrameSprite;      // 六边形外框
        [SerializeField] Sprite bothLineSprite;      // 双押连线

        [Header("打击特效与判定字")]
        [SerializeField] Sprite[] hitFxFrames;       // 打击特效序列帧（hit-0 ~ hit-7）
        [SerializeField] Sprite bestSprite;          // Best 判定字
        [SerializeField] Sprite coolSprite;          // Cool 判定字
        [SerializeField] Sprite goodSprite;          // Good 判定字
        [SerializeField] Sprite missSprite;          // Miss 判定字

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

        [Header("触摸输入（手机 / 触屏电脑）")]
        [Tooltip("开启后，点判定点附近 = 按对应键")]
        [SerializeField] bool touchEnabled = true;
        [Tooltip("触摸判定半径（世界单位）")]
        [SerializeField] float touchRadius = 1.1f;

        [Header("音效")]
        [SerializeField] AudioClip hitSound;          // 打击音效（命中/空按；建议用皮肤里的 cube-arcade.wav）
        [Range(0f, 1f)]
        [SerializeField] float hitSoundVolume = 0.7f; // 命中音量

        [Header("启动")]
        [Tooltip("自动演示：自动在拍点打出 Best；Tab 切换手动")]
        [SerializeField] bool autoPlay = true;

        // ===== 运行时数据 =====

        /// <summary>一条正在显示的音符（对象池复用）</summary>
        sealed class NoteView
        {
            public GameObject Root;             // 根对象（挂在面板下）
            public SpriteRenderer Head;         // 头（单点就是它本体）
            public SpriteRenderer Body;         // 长条身体
            public SpriteRenderer Tail;         // 长条尾巴
            public SpriteRenderer BothLine;     // 双押连线
            public ChartNote Note;              // 对应的谱面数据
            public bool Judged;                 // 是否已判定（防止重复判定/Miss）
            public bool Vanish;                 // 打中后立即消失标记
        }

        /// <summary>一个特效（打击动画或判定字）</summary>
        sealed class Effect
        {
            public SpriteRenderer Renderer;     // 渲染器
            public Sprite[] Frames;             // 序列帧（判定字为 null）
            public float Age;                   // 已存活时间
            public float Life;                  // 总时长
            public float BaseScale;             // 基准缩放（按目标宽度算好）
            public bool IsPopup;                // 是否是判定字（弹出动画不同）
        }

        readonly List<NoteView> active = new List<NoteView>();          // 场上音符
        readonly Stack<NoteView> pool = new Stack<NoteView>();          // 音符对象池
        readonly List<Effect> effects = new List<Effect>();             // 场上特效
        readonly Stack<SpriteRenderer> effectPool = new Stack<SpriteRenderer>(); // 特效渲染器池
        readonly SpriteRenderer[] padFlashes = new SpriteRenderer[6];   // 六个判定点的高亮
        readonly float[] padFlashTimer = new float[6];                  // 高亮剩余时间

        ChartData chart;            // 当前谱面
        int nextIndex;              // 下一个待激活的音符下标（谱面按拍点排序）
        double lastBeat;            // 上一帧的拍数（检测跳转用）
        bool ready;                 // 是否已有可玩的谱面
        SpriteRenderer backgroundRenderer; // 当前背景（换曲时替换精灵）
        string[] padLabelTexts;     // 判定点标签文字（预生成，避免每帧分配）
        GUIStyle labelStyle;
        GUIStyle percentStyle;      // 右上角：准确率百分比
        GUIStyle comboStyle;        // 正中心：连击数字
        GUIStyle comboCaptionStyle; // 连击数字下方的小字
        AudioSource sfxSource;      // 打击音效音源（2D）

        // 判定统计
        int bestCount;
        int coolCount;
        int goodCount;
        int missCount;
        int combo;
        int maxCombo;               // 最大连击
        float comboPulse;           // 连击数字弹跳动画计时（1 → 0 衰减）

        void Start()
        {
            if (clock == null) clock = FindObjectOfType<SongClock>();
            QualitySettings.vSyncCount = 1; // 垂直同步，画面更顺滑
            BuildVisuals();
            BuildPadLabels();

            // 打击音效音源（2D 播放，多个音效可叠加）
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;

            // 谱面由 GameRoot 通过 LoadSong 装载
        }

        // ===== 歌曲装载 / 清场 =====

        /// <summary>装载一首歌：清场 → 读取并解析谱面 → 换背景。时钟的启动由 GameRoot 负责。</summary>
        public void LoadSong(SongInfo song)
        {
            ClearForSelect();

            if (song == null || string.IsNullOrEmpty(song.ChartPath) || !File.Exists(song.ChartPath))
            {
                Debug.LogError("[谱面] 歌曲文件无效，无法装载");
                return;
            }

            chart = ChartParser.LoadFile(song.ChartPath); // 文本谱面与 Malody（.mc）谱面都能装载
            foreach (var error in chart.Errors) Debug.LogWarning("[谱面] " + error);
            Debug.Log($"[谱面] {song.Name}：{chart.Notes.Count} 个音符，其中长条 {chart.HoldCount} 个");

            nextIndex = 0;
            lastBeat = 0.0;
            ready = chart.Notes.Count > 0;

            RefreshBackground(song.BackgroundPath);
        }

        /// <summary>回到选曲界面：清空音符、特效与统计（场景与皮肤保留）</summary>
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
        }

        /// <summary>清空判定统计</summary>
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

        // ===== 结算数据（GameRoot 读取） =====

        /// <summary>Best 数量</summary>
        public int BestCount => bestCount;
        /// <summary>Cool 数量</summary>
        public int CoolCount => coolCount;
        /// <summary>Good 数量</summary>
        public int GoodCount => goodCount;
        /// <summary>Miss 数量</summary>
        public int MissCount => missCount;
        /// <summary>最大连击</summary>
        public int MaxCombo => maxCombo;
        /// <summary>谱面音符总数</summary>
        public int TotalNotes => chart != null ? chart.Notes.Count : 0;

        /// <summary>完成度百分比（0-1）：Best=100%、Cool=80%、Good=60%、Miss=0%，按整谱音符数计算。
        /// 开局 0%，随打随涨，全 Best 到 100%（结算时与"准确率"一致）。</summary>
        public float Accuracy
        {
            get
            {
                if (chart == null || chart.Notes.Count == 0) return 0f;
                var weight = bestCount + coolCount * 0.8f + goodCount * 0.6f;
                return weight / chart.Notes.Count;
            }
        }

        /// <summary>分数（0-1000000）：等于完成度 × 1,000,000（全 Best 满分）</summary>
        public int Score => Mathf.RoundToInt(Accuracy * 1000000f);

        /// <summary>当前下落速度（每拍距离）</summary>
        public float NoteSpeed => unitsPerBeat;

        /// <summary>设置下落速度（设置项，范围 0.6 ~ 6）</summary>
        public void SetNoteSpeed(float value) => unitsPerBeat = Mathf.Clamp(value, 0.6f, 6f);

        /// <summary>换背景图（歌曲包没带背景时隐藏背景）</summary>
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
                // 背景放最远处（z 越大越远），渲染顺序最低
                backgroundRenderer = CreateQuad(transform, "Background", -10, new Color(0.65f, 0.65f, 0.75f));
                backgroundRenderer.transform.localPosition = new Vector3(0f, 0f, 2f);
            }

            backgroundRenderer.gameObject.SetActive(true);
            backgroundRenderer.sprite = sprite;

            // 等比放大到铺满屏幕（取横竖两个方向所需缩放的较大者）
            var cam = Camera.main;
            var viewHeight = cam != null && cam.orthographic ? cam.orthographicSize * 2f : 11.6f;
            var viewWidth = viewHeight * Screen.width / Mathf.Max(1, Screen.height);
            var size = sprite.bounds.size;
            var scale = Mathf.Max(viewWidth / Mathf.Max(0.0001f, size.x), viewHeight / Mathf.Max(0.0001f, size.y)) * 1.02f;
            backgroundRenderer.transform.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>从磁盘读取图片（jpg/png）生成 Sprite；失败返回 null</summary>
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

        // ===== 每帧主循环 =====

        void Update()
        {
            if (comboPulse > 0f) comboPulse = Mathf.Max(0f, comboPulse - Time.deltaTime * 2.5f); // 连击弹跳衰减
            HandleInput();
            UpdateEffects();
            if (!ready || clock == null) return;

            var beat = clock.Beat;

            // 拍数大跳（跳转/重开）时重建场上音符
            if (System.Math.Abs(beat - lastBeat) > 1.0) ResetTo(beat);
            lastBeat = beat;

            var apothem = hexRadius * 0.866f;             // 中心到判定点的距离
            var flightStart = apothem - spawnRadius;      // 出生区边界 → 判定点的飞行距离
            var birthLength = birthBeats * unitsPerBeat;  // 出生动画对应的距离

            // 音符进入出生动画范围时激活
            while (nextIndex < chart.Notes.Count && (chart.Notes[nextIndex].StartBeat - beat) * unitsPerBeat <= flightStart + birthLength)
            {
                Activate(nextIndex);
                nextIndex++;
            }

            var nowSeconds = clock.SongTime;

            for (var i = active.Count - 1; i >= 0; i--)
            {
                var view = active[i];

                // 打中后标记消失：立即回收
                if (view.Vanish)
                {
                    Release(i);
                    continue;
                }

                var note = view.Note;

                // ---- 判定：到期未打 → Miss；自动演示则在拍点直接判 Best ----
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

                // ---- 位置：全部由拍数推算 ----
                var lane = Mathf.Clamp(note.Lane, 0, 5);
                var radial = PadDirection(lane);                                 // 该键位的向外方向
                var rotation = Quaternion.Euler(0f, 0f, PadAngleDeg[lane]);      // 轨道朝向
                var noteWidth = 0.9f;                                            // 音符宽度
                var ageHead = (float)(note.StartBeat - beat) * unitsPerBeat;     // 头距判定点还有多远
                var ageTail = (float)(note.EndBeat - beat) * unitsPerBeat;       // 尾距判定点还有多远

                float headDist;
                float headScale;
                float headAlpha;
                if (ageHead > flightStart)
                {
                    // 出生期：停在出生区边界上，由小变大
                    headDist = spawnRadius;
                    var t = Mathf.Clamp01((flightStart + birthLength - ageHead) / birthLength);
                    headScale = Mathf.Lerp(BirthScaleFrom, 1f, t * t * (3f - 2f * t)); // smoothstep，起步缓
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
                    // ---- 长条 ----
                    headDist = Mathf.Min(headDist, apothem); // 头到达判定点后钉住，等待被"消耗"

                    var tailRaw = apothem - ageTail;
                    var tailEmerged = tailRaw > spawnRadius;                                          // 尾巴是否已从出生区长出
                    var tailDist = Mathf.Min(headDist, Mathf.Max(tailRaw, spawnRadius));              // 没长出来前贴在出生区边界

                    PlaceNote(view.Head, radial * headDist, rotation, noteWidth, 0.3f, headScale, headAlpha);

                    // 身体 = 头到尾之间的段；头还在出生区时长度为 0（隐藏）
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

                    // 尾巴：长出后才显示
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
                    if (tailRaw > apothem + Overshoot) Release(i); // 整条越过判定点，回收
                }
                else
                {
                    // ---- 单点 ----
                    PlaceNote(view.Head, radial * headDist, rotation, noteWidth, 0.3f, headScale, headAlpha);
                    UpdateBothLine(view, apothem, headDist);
                    if (headDist > apothem + Overshoot) Release(i);
                }
            }
        }

        /// <summary>跳转/重开：按当前拍数重建场上音符</summary>
        void ResetTo(double beat)
        {
            for (var i = active.Count - 1; i >= 0; i--) Release(i);

            nextIndex = 0;
            while (nextIndex < chart.Notes.Count && chart.Notes[nextIndex].StartBeat <= beat) nextIndex++;

            // 正在持续中的长条重新激活
            for (var i = 0; i < nextIndex; i++)
            {
                if (chart.Notes[i].EndBeat > beat) Activate(i);
            }

            ResetCounters();
        }

        /// <summary>从对象池取一个音符视图并激活</summary>
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
                // 长条：头部与尾部同色同形——单押蓝色（SIMPLEHold），双押橙色（SIMPLEholdboth）
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

        /// <summary>回收一个音符视图到对象池</summary>
        void Release(int index)
        {
            var view = active[index];
            view.Root.SetActive(false);
            pool.Push(view);
            active.RemoveAt(index);
        }

        // ===== 判定 =====

        /// <summary>执行判定：打中 → 特效 + 判定字 + 消失；Miss → 判定字 + 清零 combo</summary>
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
                SpawnPopup(radial * (apothem - 0.75f), missSprite); // 判定字画在判定点稍内侧
                return;
            }

            if (grade == GradeBest) bestCount++;
            else if (grade == GradeCool) coolCount++;
            else goodCount++;
            combo++;
            if (combo > maxCombo) maxCombo = combo;
            comboPulse = 1f;                                    // 触发连击数字弹跳

            PlayHitSound(hitSoundVolume);                       // 命中音效
            SpawnFx(radial * apothem);                          // 打击特效在判定点上
            SpawnPopup(radial * (apothem - 0.75f), grade == GradeBest ? bestSprite : grade == GradeCool ? coolSprite : goodSprite);
            if (!view.Note.IsHold) view.Vanish = true;          // 单点打中即消失；长条继续被"消耗"
        }

        /// <summary>按键/触摸触发判定：在该轨道里找判定窗内最近的音符，按偏差给等级</summary>
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
                if (delta > GoodWindowMs / 1000f) continue; // 超出 Good 窗，不参与
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestIndex = i;
                }
            }
            if (bestIndex < 0)
            {
                PlayHitSound(hitSoundVolume * 0.3f); // 附近没有音符：给一个轻微的空按反馈
                return;
            }

            var ms = bestDelta * 1000f;
            var grade = ms <= BestWindowMs ? GradeBest : ms <= CoolWindowMs ? GradeCool : GradeGood;
            ApplyJudgment(active[bestIndex], grade);
        }

        /// <summary>播放打击音效（多个音叠加时 PlayOneShot 会自动混音）</summary>
        void PlayHitSound(float volume)
        {
            if (sfxSource == null || hitSound == null || volume <= 0.001f) return;
            sfxSource.PlayOneShot(hitSound, volume);
        }

        // ===== 特效（打击动画 / 判定字） =====

        /// <summary>在指定位置播放打击特效（hit-0~7 序列帧）</summary>
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

        /// <summary>在指定位置弹出判定字（best/cool/good/miss）</summary>
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

        /// <summary>推进所有特效：序列帧播放、弹出动画、淡出、回收</summary>
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
                    // 序列帧动画：按进度切帧，最后 25% 淡出
                    var index = Mathf.Min(effect.Frames.Length - 1, (int)(t * effect.Frames.Length));
                    effect.Renderer.sprite = effect.Frames[index];
                    var alpha = t < 0.75f ? 1f : Mathf.InverseLerp(1f, 0.75f, t);
                    effect.Renderer.color = new Color(1f, 1f, 1f, alpha);
                }
                else
                {
                    // 判定字：先快速弹出（略过冲），再回到基准大小；后半段淡出
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

        // ===== 输入（键盘 + 触摸） =====

        void HandleInput()
        {
            for (var i = 0; i < padFlashes.Length; i++)
            {
                // 键盘按下：高亮判定点；手动模式下触发判定
                if (judgeKeys != null && i < judgeKeys.Length && Input.GetKeyDown(judgeKeys[i]))
                {
                    padFlashTimer[i] = 0.18f;
                    if (ready && !autoPlay && clock != null) TryJudgeByInput(i);
                }

                // 判定点高亮淡出
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

            // Tab：自动演示 / 手动游玩 切换
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                autoPlay = !autoPlay;
                Debug.Log(autoPlay ? "[模式] 自动演示" : "[模式] 手动游玩");
            }
        }

        /// <summary>触摸输入：手机多点触控逐点判定；没有触摸时用鼠标左键兜底（方便电脑调试）</summary>
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

        /// <summary>屏幕坐标 → 世界坐标，命中半径内最近的判定点则触发该键</summary>
        void TryPadAtScreenPoint(Vector2 screenPoint)
        {
            var cam = Camera.main;
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
            if (ready && !autoPlay && clock != null) TryJudgeByInput(nearest);
        }

        // ===== 视图构建 =====

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

        /// <summary>设置精灵：有皮肤用皮肤（白色 tint 保留原色），没有则用纯色方块兜底</summary>
        void SetSprite(SpriteRenderer renderer, Sprite sprite, Color fallbackColor)
        {
            renderer.sprite = sprite != null ? sprite : SpriteFactory.Square();
            renderer.color = sprite != null ? Color.white : fallbackColor;
        }

        /// <summary>摆放音符（头/尾）：按目标宽度等比缩放，可附加出生动画的缩放与透明度</summary>
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

        /// <summary>摆放长条身体：沿径向拉伸（长度 = 拍数 × 每拍距离）</summary>
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

        /// <summary>双押连线：附着在其中一个音符视图上，连接两个判定点（临近判定点时显示）</summary>
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

        /// <summary>键位（0-5）对应的向外单位方向</summary>
        static Vector3 PadDirection(int lane)
        {
            var radians = PadAngleDeg[Mathf.Clamp(lane, 0, 5)] * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f);
        }

        /// <summary>键位（0-5）判定点的世界坐标</summary>
        Vector3 PadPosition(int lane) => PadDirection(lane) * (hexRadius * 0.866f);

        /// <summary>搭建静态视觉：背景、六边形外框、出生区轮廓、判定点高亮</summary>
        void BuildVisuals()
        {
            var cam = Camera.main;
            var viewHeight = cam != null && cam.orthographic ? cam.orthographicSize * 2f : 11.6f;
            var viewWidth = viewHeight * Screen.width / Mathf.Max(1, Screen.height);

            if (backgroundSprite != null)
            {
                // 默认背景（歌曲没带背景时用），实际换曲时由 RefreshBackground 替换
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

                // 外六边形框
                var frame = CreateQuad(transform, "HexFrame", -5, Color.white);
                frame.sprite = hexFrameSprite;
                frame.transform.localPosition = new Vector3(0f, 0f, 1f);
                frame.transform.localScale = new Vector3(frameScale, frameScale, 1f);

                // 中心出生区（小六边形轮廓）
                if (showSpawnZone)
                {
                    var zone = CreateQuad(transform, "SpawnZone", -4, new Color(0.6f, 0.9f, 1f, 0.22f));
                    zone.sprite = hexFrameSprite;
                    var zoneScale = frameScale * (spawnRadius / hexApothem);
                    zone.transform.localPosition = new Vector3(0f, 0f, 0.5f);
                    zone.transform.localScale = new Vector3(zoneScale, zoneScale, 1f);
                }
            }

            // 六个判定点的按下高亮
            for (var i = 0; i < padFlashes.Length; i++)
            {
                var flash = CreateQuad(transform, "PadFlash" + (i + 1), 3, new Color(0.55f, 0.95f, 1f, 0f));
                flash.transform.localPosition = PadPosition(i);
                flash.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
                flash.gameObject.SetActive(false);
                padFlashes[i] = flash;
            }
        }

        /// <summary>预生成判定点标签文字（避免每帧创建字符串）</summary>
        void BuildPadLabels()
        {
            padLabelTexts = new string[6];
            for (var i = 0; i < 6; i++) padLabelTexts[i] = $"{i + 1} ({KeyLabel(i)})";
        }

        // ===== HUD（OnGUI） =====

        void OnGUI()
        {
            var cam = Camera.main;
            if (cam == null || !ready) return; // 只有在装载了曲子后才画 HUD

            // 样式（懒创建））
            labelStyle ??= UiTheme.PadLabelStyle();
            percentStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                alignment = TextAnchor.UpperRight,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.92f, 0.97f, 1f, 0.92f) },
            };
            comboStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 88,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 1f, 1f, 0.85f) },
            };
            comboCaptionStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.6f, 0.85f, 1f, 0.55f) },
            };

            // 判定点标签（编号 + 按键）
            if (showPadLabels && padLabelTexts != null)
            {
                for (var i = 0; i < 6; i++)
                {
                    var padScreen = cam.WorldToScreenPoint(PadPosition(i) * 0.82f);
                    if (padScreen.z <= 0f) continue;
                    GUI.Label(new Rect(padScreen.x - 45f, Screen.height - padScreen.y - 12f, 90f, 24f), padLabelTexts[i], labelStyle);
                }
            }

            // 右上角：准确率百分比
            GUI.Label(new Rect(Screen.width - 340f, 16f, 320f, 40f), $"{Accuracy * 100f:0.00}%", percentStyle);

            // 正中心：连击数（2 连以上才显示；增长时轻微弹跳）
            if (combo >= 2)
            {
                var center = cam.WorldToScreenPoint(Vector3.zero);
                var comboRect = new Rect(center.x - 220f, Screen.height - center.y - 80f, 440f, 120f);
                var previousMatrix = GUI.matrix;
                var pulse = 1f + 0.16f * comboPulse * comboPulse;
                GUIUtility.ScaleAroundPivot(Vector2.one * pulse, new Vector2(comboRect.center.x, comboRect.center.y));
                GUI.Label(comboRect, combo.ToString(), comboStyle);
                GUI.matrix = previousMatrix;
                GUI.Label(new Rect(comboRect.x, comboRect.y + 96f, comboRect.width, 24f), "COMBO", comboCaptionStyle);
            }
        }

        /// <summary>按键显示名（逗号/句号显示为符号）</summary>
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
