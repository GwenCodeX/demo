using System.IO;
using RhythmPlayer.Core;
using RhythmPlayer.Play;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RhythmPlayer.EditorTools
{
    public static class RhythmPlayerSetup
    {
        const string SongFolder = "Assets/Songs/WuJiZhiXian";
        const string DemoScenePath = "Assets/Scenes/PlayerDemo.unity";

        [MenuItem("Tools/音游播放器/搭建播放器场景")]
        static void BuildPlayerSceneMenu()
        {
            SetupSceneContents();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("场景就绪：按 Play 开始（自动播放）；空格暂停/继续，R 重开。记得 Ctrl+S 保存场景。");
        }

        /// 批处理打包入口：
        /// Tuanjie.exe -batchmode -quit -projectPath <工程> -executeMethod RhythmPlayer.EditorTools.RhythmPlayerSetup.BuildDemo
        public static void BuildDemo()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            SetupSceneContents();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, DemoScenePath);

            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;

            var output = Path.GetFullPath("Build/RhythmDemo.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { DemoScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });

            var result = report.summary.result;
            Debug.Log($"[BuildDemo] 打包结果：{result}，产物：{output}");
            EditorApplication.Exit(result == BuildResult.Succeeded ? 0 : 1);
        }

        static void SetupSceneContents()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SongFolder + "/audio.mp3");
            var chart = AssetDatabase.LoadAssetAtPath<TextAsset>(SongFolder + "/E119.txt");
            if (clip == null || chart == null)
            {
                Debug.LogError($"素材缺失：请确认 {SongFolder} 下有 audio.mp3 和 E119.txt");
                return;
            }

            var clock = Object.FindObjectOfType<SongClock>();
            if (clock == null) clock = CreateConductor(clip);
            else Debug.Log("Conductor 已存在，跳过创建。");

            if (Object.FindObjectOfType<Playfield>() == null)
            {
                var playfield = new GameObject("Playfield");
                var component = playfield.AddComponent<Playfield>();
                var serialized = new SerializedObject(component);
                serialized.FindProperty("clock").objectReferenceValue = clock;
                serialized.FindProperty("chartAsset").objectReferenceValue = chart;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Undo.RegisterCreatedObjectUndo(playfield, "Build Player Scene");
                Selection.activeGameObject = playfield;
            }
            else
            {
                Debug.Log("Playfield 已存在，跳过创建。");
            }

            SetupCamera();
        }

        static SongClock CreateConductor(AudioClip clip)
        {
            var conductor = new GameObject("Conductor");
            var source = conductor.AddComponent<AudioSource>();
            source.clip = clip;
            source.playOnAwake = false;
            source.volume = 0.8f;
            conductor.AddComponent<SongClock>();
            conductor.AddComponent<Metronome>();
            conductor.AddComponent<ClockDebugOverlay>();

            var serialized = new SerializedObject(conductor.GetComponent<SongClock>());
            serialized.FindProperty("bpm").floatValue = 182f;
            serialized.FindProperty("firstBeatOffsetSeconds").floatValue = 0f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Undo.RegisterCreatedObjectUndo(conductor, "Build Player Scene");
            return conductor.GetComponent<SongClock>();
        }

        static void SetupCamera()
        {
            var camera = Camera.main;
            if (camera == null) return;
            camera.orthographic = true;
            camera.orthographicSize = 5.8f;
            camera.transform.position = new Vector3(0f, 1f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.08f, 0.11f);
        }
    }
}
