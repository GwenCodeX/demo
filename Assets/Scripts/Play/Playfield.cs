using System.Collections.Generic;
using UnityEngine;
using RhythmPlayer.Core;

namespace RhythmPlayer.Play
{
    /// 六边形演奏面板：左列上中下 = 键 1/2/3，右列上中下 = 键 4/5/6。
    /// 音符从六边形中心"冒出来"，沿判定点法线向外飞，到达边缘判定点的瞬间即拍点。
    /// 皮肤精灵留空时退回纯色矩形。
    public sealed class Playfield : MonoBehaviour
    {
        // 键位 1-6 → 六边形角度（度）：120°=左上, 180°=左中, 240°=左下, 60°=右上, 0°=右中, 300°=右下
        static readonly float[] PadAngleDeg = { 120f, 180f, 240f, 60f, 0f, 300f };
        const float Overshoot = 0.4f;      // 音符越过判定点后多远消失
        const float BothLineWindow = 0.9f; // 双押连线提前出现距离

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

        [Header("六边形布局（世界单位）")]
        [Tooltip("六边形中心到顶点的距离")]
        [SerializeField] float hexRadius = 4.3f;

        [Header("手感")]
        [Tooltip("每拍飞行距离，越大越快（中心到判定点的飞行时间 = 判定点距离 / 本值 拍）")]
        [SerializeField] float unitsPerBeat = 1.2f;

        [Header("按键映射（左列上中下 1/2/3，右列上中下 4/5/6）")]
        [SerializeField] KeyCode[] judgeKeys = { KeyCode.E, KeyCode.D, KeyCode.C, KeyCode.I, KeyCode.K, KeyCode.Comma };
        [SerializeField] bool showPadLabels = true;

        [Header("启动")]
        [SerializeField] bool autoStart = true;

        sealed class NoteView
        {
            public GameObject Root;
            public SpriteRenderer Head;
            public SpriteRenderer Body;
            public SpriteRenderer Tail;
            public SpriteRenderer BothLine;
            public ChartNote Note;
        }

        readonly List<NoteView> active = new List<NoteView>();
        readonly Stack<NoteView> pool = new Stack<NoteView>();
        readonly SpriteRenderer[] padFlashes = new SpriteRenderer[6];
        readonly float[] padFlashTimer = new float[6];
        ChartData chart;
        int nextIndex;
        double lastBeat;
        bool ready;
        string[] padLabelTexts;
        GUIStyle labelStyle;

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
            UpdateInputFlash();
            if (!ready || clock == null) return;

            var beat = clock.Beat;
            if (System.Math.Abs(beat - lastBeat) > 1.0) ResetTo(beat);
            lastBeat = beat;

            var apothem = hexRadius * 0.866f;

            // 音符到达中心点时才出现，之后一路向外飞
            while (nextIndex < chart.Notes.Count && (chart.Notes[nextIndex].StartBeat - beat) * unitsPerBeat <= apothem)
            {
                Activate(nextIndex);
                nextIndex++;
            }

            for (var i = active.Count - 1; i >= 0; i--)
            {
                var view = active[i];
                var note = view.Note;
                var lane = Mathf.Clamp(note.Lane, 0, 5);
                var radial = PadDirection(lane);
                var rotation = Quaternion.Euler(0f, 0f, PadAngleDeg[lane]);
                var noteWidth = 0.9f;

                // 距离中心：0 = 中心（出生点），apothem = 判定点（打击位置）
                var headDist = apothem - (float)(note.StartBeat - beat) * unitsPerBeat;
                var tailDist = apothem - (float)(note.EndBeat - beat) * unitsPerBeat;

                if (note.IsHold)
                {
                    headDist = Mathf.Min(headDist, apothem);  // 头到达判定点后钉住
                    tailDist = Mathf.Min(tailDist, headDist); // 尾不越过头
                    PlaceNote(view.Head, radial * headDist, rotation, noteWidth, 0.3f);

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

                    PlaceNote(view.Tail, radial * tailDist, rotation, noteWidth, 0.3f);
                    UpdateBothLine(view, apothem, headDist);
                    if (tailDist > apothem + Overshoot) Release(i);
                }
                else
                {
                    PlaceNote(view.Head, radial * headDist, rotation, noteWidth, 0.3f);
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
        }

        void Activate(int index)
        {
            var note = chart.Notes[index];
            var view = pool.Count > 0 ? pool.Pop() : CreateView();
            view.Root.SetActive(true);
            view.Note = note;

            SetSprite(view.Head, note.IsHold ? holdHeadSprite : (note.IsDouble ? tapBothSprite : tapSprite), new Color(0.78f, 0.92f, 1f));

            if (note.IsHold)
            {
                SetSprite(view.Body, holdBodySprite, new Color(0.45f, 0.68f, 1f, 0.5f));
                SetSprite(view.Tail, holdTailSprite, new Color(0.78f, 0.92f, 1f));
                view.Tail.gameObject.SetActive(true);
            }
            else
            {
                view.Body.gameObject.SetActive(false);
                view.Tail.gameObject.SetActive(false);
            }

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

        void PlaceNote(SpriteRenderer renderer, Vector3 position, Quaternion rotation, float width, float fallbackHeight)
        {
            renderer.transform.localPosition = position;
            renderer.transform.localRotation = rotation;
            if (renderer.sprite == SpriteFactory.Square())
            {
                renderer.transform.localScale = new Vector3(width, fallbackHeight, 1f);
                return;
            }
            var scale = width / Mathf.Max(0.0001f, renderer.sprite.bounds.size.x);
            renderer.transform.localScale = new Vector3(scale, scale, 1f);
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

        void UpdateInputFlash()
        {
            for (var i = 0; i < padFlashes.Length; i++)
            {
                if (judgeKeys != null && i < judgeKeys.Length && Input.GetKeyDown(judgeKeys[i])) padFlashTimer[i] = 0.18f;

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

            if (hexFrameSprite != null)
            {
                var frame = CreateQuad(transform, "HexFrame", -5, Color.white);
                frame.sprite = hexFrameSprite;
                var size = hexFrameSprite.bounds.size;
                var scale = hexRadius * 2f / Mathf.Max(0.0001f, Mathf.Max(size.x, size.y));
                frame.transform.localPosition = new Vector3(0f, 0f, 1f);
                frame.transform.localScale = new Vector3(scale, scale, 1f);
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
            if (!showPadLabels || padLabelTexts == null) return;
            var cam = Camera.main;
            if (cam == null) return;

            labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.85f) },
            };

            for (var i = 0; i < 6; i++)
            {
                var screen = cam.WorldToScreenPoint(PadPosition(i) * 0.82f);
                if (screen.z <= 0f) continue;
                GUI.Label(new Rect(screen.x - 45f, Screen.height - screen.y - 12f, 90f, 24f), padLabelTexts[i], labelStyle);
            }
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
