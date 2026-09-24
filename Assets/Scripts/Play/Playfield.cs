using System.Collections.Generic;
using UnityEngine;
using RhythmPlayer.Core;

namespace RhythmPlayer.Play
{
    /// 6 轨下落播放器：轨道 = 角度 / 60。音符位置完全由拍数决定（位置 = f(剩余拍数)），
    /// 所以暂停、跳转、变速都不需要额外状态，天然和音乐同步。
    public sealed class Playfield : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] SongClock clock;
        [SerializeField] TextAsset chartAsset;

        [Header("布局（世界单位）")]
        [SerializeField] int laneCount = 6;
        [SerializeField] float laneWidth = 1f;
        [SerializeField] float judgeLineY = -4f;
        [SerializeField] float viewHeight = 10f;

        [Header("手感")]
        [Tooltip("每拍下落距离，越大音符越快")]
        [SerializeField] float unitsPerBeat = 2.2f;

        [Header("启动")]
        [SerializeField] bool autoStart = true;

        sealed class NoteView
        {
            public GameObject Root;
            public Transform Head;
            public Transform Body;
            public Transform Tail;
            public ChartNote Note;
        }

        readonly List<NoteView> active = new List<NoteView>();
        readonly Stack<NoteView> pool = new Stack<NoteView>();
        ChartData chart;
        int nextIndex;
        double lastBeat;
        bool ready;

        void Start()
        {
            if (clock == null) clock = FindObjectOfType<SongClock>();
            BuildVisuals();

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
            if (!ready || clock == null) return;
            var beat = clock.Beat;
            if (System.Math.Abs(beat - lastBeat) > 1.0) ResetTo(beat);
            lastBeat = beat;

            var spawnBeats = viewHeight / unitsPerBeat;
            while (nextIndex < chart.Notes.Count && chart.Notes[nextIndex].StartBeat - beat <= spawnBeats)
            {
                Activate(nextIndex);
                nextIndex++;
            }

            for (var i = active.Count - 1; i >= 0; i--)
            {
                var view = active[i];
                var headY = judgeLineY + (float)(view.Note.StartBeat - beat) * unitsPerBeat;
                var tailY = judgeLineY + (float)(view.Note.EndBeat - beat) * unitsPerBeat;
                var noteWidth = laneWidth * 0.9f;

                view.Head.localPosition = new Vector3(0f, headY, 0f);
                view.Head.localScale = new Vector3(noteWidth, 0.22f, 1f);

                if (view.Note.IsHold)
                {
                    view.Body.gameObject.SetActive(true);
                    view.Tail.gameObject.SetActive(true);
                    view.Body.localPosition = new Vector3(0f, (headY + tailY) * 0.5f, 0f);
                    view.Body.localScale = new Vector3(noteWidth * 0.55f, Mathf.Max(0.05f, tailY - headY), 1f);
                    view.Tail.localPosition = new Vector3(0f, tailY, 0f);
                    view.Tail.localScale = new Vector3(noteWidth, 0.22f, 1f);
                }

                if (tailY < judgeLineY - 3f) Release(i);
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
            view.Body.gameObject.SetActive(false);
            view.Tail.gameObject.SetActive(false);
            var lane = Mathf.Clamp(note.Lane, 0, laneCount - 1);
            view.Root.transform.localPosition = new Vector3(LaneCenterX(lane), 0f, 0f);
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
                Head = CreateQuad(root.transform, "Head", 11, new Color(0.78f, 0.92f, 1f)).transform,
                Body = CreateQuad(root.transform, "Body", 10, new Color(0.45f, 0.68f, 1f, 0.5f)).transform,
                Tail = CreateQuad(root.transform, "Tail", 11, new Color(0.78f, 0.92f, 1f)).transform,
            };
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

        float LaneCenterX(int lane) => (lane - (laneCount - 1) * 0.5f) * laneWidth;

        void BuildVisuals()
        {
            for (var i = 0; i <= laneCount; i++)
            {
                var line = CreateQuad(transform, "LaneLine", 0, new Color(1f, 1f, 1f, 0.08f));
                var x = (i - laneCount * 0.5f) * laneWidth;
                line.transform.localPosition = new Vector3(x, judgeLineY + viewHeight * 0.5f, 0f);
                line.transform.localScale = new Vector3(0.03f, viewHeight, 1f);
            }

            var judge = CreateQuad(transform, "JudgeLine", 5, new Color(1f, 1f, 1f, 0.85f));
            judge.transform.localPosition = new Vector3(0f, judgeLineY, 0f);
            judge.transform.localScale = new Vector3(laneCount * laneWidth, 0.06f, 1f);
        }
    }
}
