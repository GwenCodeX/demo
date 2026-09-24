using System.Collections.Generic;
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
        const string SongFolder = "Assets/Songs/Sinsekai";
        const string ChartFile = "C017.txt";
        const float SongBpm = 193f;
        const string SkinFolder = "Assets/Skins/ClassicDance3V";
        const string DemoScenePath = "Assets/Scenes/PlayerDemo.unity";

        static readonly string[] SpriteFiles =
        {
            SkinFolder + "/tap.png",
            SkinFolder + "/tapboth.png",
            SkinFolder + "/hold-0.png",
            SkinFolder + "/holdbody.png",
            SkinFolder + "/holdend.png",
            SkinFolder + "/lineout.png",
            SkinFolder + "/both line.png",
        };

        [MenuItem("Tools/音游播放器/搭建播放器场景")]
        static void BuildPlayerSceneMenu()
        {
            BuildSceneInto();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("场景就绪（sinsekai / BPM 193）。按 Play 开始；空格暂停/继续，R 重开。记得 Ctrl+S 保存场景。");
        }

        /// 批处理打包入口：
        /// Tuanjie.exe -batchmode -quit -projectPath <工程> -executeMethod RhythmPlayer.EditorTools.RhythmPlayerSetup.BuildDemo
        public static void BuildDemo()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            BuildSceneInto();
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

        static void BuildSceneInto()
        {
            EnsureSpriteImports();

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SongFolder + "/audio.mp3");
            var chart = AssetDatabase.LoadAssetAtPath<TextAsset>(SongFolder + "/" + ChartFile);
            if (clip == null || chart == null)
            {
                Debug.LogError($"素材缺失：请确认 {SongFolder} 下有 audio.mp3 和 {ChartFile}");
                return;
            }

            var clock = Object.FindObjectOfType<SongClock>();
            if (clock == null) clock = CreateConductor(clip);
            else Debug.Log("Conductor 已存在，跳过创建。");

            var playfield = Object.FindObjectOfType<Playfield>();
            if (playfield == null)
            {
                var go = new GameObject("Playfield");
                playfield = go.AddComponent<Playfield>();
                Undo.RegisterCreatedObjectUndo(go, "Build Player Scene");
                Selection.activeGameObject = go;
            }
            else
            {
                Debug.Log("Playfield 已存在，更新引用。");
            }

            var serialized = new SerializedObject(playfield);
            serialized.FindProperty("clock").objectReferenceValue = clock;
            serialized.FindProperty("chartAsset").objectReferenceValue = chart;
            serialized.FindProperty("tapSprite").objectReferenceValue = LoadSprite(SpriteFiles[0]);
            serialized.FindProperty("tapBothSprite").objectReferenceValue = LoadSprite(SpriteFiles[1]);
            serialized.FindProperty("holdHeadSprite").objectReferenceValue = LoadSprite(SpriteFiles[2]);
            serialized.FindProperty("holdBodySprite").objectReferenceValue = LoadSprite(SpriteFiles[3]);
            serialized.FindProperty("holdTailSprite").objectReferenceValue = LoadSprite(SpriteFiles[4]);
            serialized.FindProperty("backgroundSprite").objectReferenceValue = LoadSprite(SongFolder + "/bg.jpg");
            serialized.FindProperty("hexFrameSprite").objectReferenceValue = LoadSprite(SpriteFiles[5]);
            serialized.FindProperty("bothLineSprite").objectReferenceValue = LoadSprite(SpriteFiles[6]);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            SetupCamera();
        }

        /// PNG/JPG 默认导入为 Texture，这里统一改成 Sprite 才能给 SpriteRenderer 用
        static void EnsureSpriteImports()
        {
            var paths = new List<string>(SpriteFiles) { SongFolder + "/bg.jpg" };
            foreach (var path in paths)
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                var changed = false;
                if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; changed = true; }
                if (importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; changed = true; }
                if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
                if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
                if (importer.spritePixelsPerUnit != 100f) { importer.spritePixelsPerUnit = 100f; changed = true; }
                if (changed) importer.SaveAndReimport();
            }
        }

        static Sprite LoadSprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

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
            serialized.FindProperty("bpm").floatValue = SongBpm;
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
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.08f, 0.11f);
        }
    }
}
