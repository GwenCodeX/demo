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
    /// <summary>
    /// 场景搭建 / 一键打包工具。
    /// 用法一（编辑器）：菜单 Tools → 音游播放器 → 搭建播放器场景（幂等，缺什么补什么）
    /// 用法二（命令行打包）：
    ///   Tuanjie.exe -batchmode -quit -projectPath &lt;工程&gt; -executeMethod RhythmPlayer.EditorTools.RhythmPlayerSetup.BuildDemo
    /// 打包会把 Assets/Songs 里的歌曲包复制到 exe 同级的 Songs 目录（运行时由 SongRepository 扫描导入）。
    /// </summary>
    public static class RhythmPlayerSetup
    {
        const string SkinFolder = "Assets/Skins/ClassicDance3V"; // 皮肤素材目录
        const string SongsFolder = "Assets/Songs";               // 歌曲包目录
        const string DemoSongFolder = "Sinsekai";                // 安卓包只内置的演示歌曲（第一首）
        const string DemoScenePath = "Assets/Scenes/PlayerDemo.unity";
        const int HitFxFrameCount = 8;                           // 打击特效帧数（hit-0 ~ hit-7）

        static readonly string[] SpriteFiles = BuildSpriteFileList();

        /// <summary>需要用到的皮肤图片（导入时会统一改成 Sprite 格式）</summary>
        static string[] BuildSpriteFileList()
        {
            var list = new List<string>
            {
                SkinFolder + "/SIMPLETap.png",
                SkinFolder + "/SIMPLETapboth.png",
                SkinFolder + "/SIMPLEHold.png",
                SkinFolder + "/SIMPLEholdboth.png",
                SkinFolder + "/holdbody.png",
                SkinFolder + "/lineout.png",
                SkinFolder + "/both line.png",
                SkinFolder + "/best.png",
                SkinFolder + "/cool.png",
                SkinFolder + "/good.png",
                SkinFolder + "/miss.png",
            };
            for (var i = 0; i < HitFxFrameCount; i++) list.Add($"{SkinFolder}/hit-{i}.png");
            list.Add(SkinFolder + "/ComboTAG.png");
            for (var i = 0; i < 10; i++) list.Add($"{SkinFolder}/num-{i}.png");
            return list.ToArray();
        }

        [MenuItem("Tools/音游播放器/搭建播放器场景")]
        static void BuildPlayerSceneMenu()
        {
            BuildSceneInto();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("场景就绪。按 Play 进入选曲界面：数字键/点击选歌；游玩中空格暂停、R 重开、Esc 返回、Tab 自动/手动。记得 Ctrl+S 保存场景。");
        }

        /// <summary>批处理打包入口（见类注释）</summary>
        public static void BuildDemo()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            BuildSceneInto();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, DemoScenePath);

            // 窗口化运行，1280x720，可缩放
            PlayerSettings.productName = "RhythmPlayer";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;

            var output = Path.GetFullPath("Build/RhythmDemo.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            // 若当前激活目标是安卓，先切回 Windows，保证两条打包管线可以交替执行
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { DemoScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });

            var result = report.summary.result;
            if (result == BuildResult.Succeeded) CopySongsToBuild(output); // 歌曲包放到 exe 旁边
            Debug.Log($"[BuildDemo] 打包结果：{result}，产物：{output}");
            EditorApplication.Exit(result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>
        /// 批处理打包安卓（APK）：
        ///   Tuanjie.exe -batchmode -quit -projectPath &lt;工程&gt; -executeMethod RhythmPlayer.EditorTools.RhythmPlayerSetup.BuildAndroid
        /// 歌包会随包内置（StreamingAssets），安卓首次运行时自动释放到可写目录。
        /// </summary>
        public static void BuildAndroid()
        {
            // 0. 配置安卓工具链路径（仅在本机目录存在时生效；编辑器会记住这些路径）
            //    键名与编辑器实际读取的一致：AndroidSdkRoot / AndroidNdkRootR23B / Jdk17Path / JdkUseEmbedded
            const string sdkPath = "D:/AndroidSDK";
            const string ndkPath = "D:/AndroidNDK/android-ndk-r23b";
            const string jdkPath = "D:/AndroidJDK17";
            if (Directory.Exists(sdkPath)) EditorPrefs.SetString("AndroidSdkRoot", sdkPath);
            if (Directory.Exists(ndkPath)) EditorPrefs.SetString("AndroidNdkRootR23B", ndkPath);
            if (Directory.Exists(jdkPath))
            {
                EditorPrefs.SetString("Jdk17Path", jdkPath);
                EditorPrefs.SetBool("JdkUseEmbedded", false);
            }
            Debug.Log($"[BuildAndroid] 工具链配置：SDK={EditorPrefs.GetString("AndroidSdkRoot", "(未设置)")}  NDK={EditorPrefs.GetString("AndroidNdkRootR23B", "(未设置)")}  JDK={EditorPrefs.GetString("Jdk17Path", "(未设置)")}");

            // 1. 同步歌包 + 清单到 StreamingAssets
            SyncStreamingSongs();

            // 2. 切换到安卓目标（首次切换会触发一次完整重新导入，比较慢）
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("[BuildAndroid] 切换安卓目标失败：请确认已安装 Android Build Support 模块");
                EditorApplication.Exit(1);
                return;
            }

            // 3. 安卓玩家设置：横屏、包名、Mono 后端（构建快，方便测试；Mono 只支持 ARMv7）
            PlayerSettings.productName = "RhythmPlayer";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.gwencodex.rhythmplayer");
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.Mono2x);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;

            // 4. 搭建场景并打包
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            BuildSceneInto();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, DemoScenePath);

            var output = Path.GetFullPath("Build/Android/RhythmDemo.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { DemoScenePath },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });

            var result = report.summary.result;
            Debug.Log($"[BuildAndroid] 打包结果：{result}，产物：{output}");
            EditorApplication.Exit(result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>把安卓演示歌包同步到 StreamingAssets（只带 Sinsekai 一首，含清单），供安卓首次运行释放</summary>
        static void SyncStreamingSongs()
        {
            if (Directory.Exists("Assets/StreamingAssets")) Directory.Delete("Assets/StreamingAssets", true);
            var targetRoot = "Assets/StreamingAssets/Songs";
            Directory.CreateDirectory(targetRoot);

            var manifest = new List<string>();
            var sourceDir = Path.Combine(SongsFolder, DemoSongFolder);
            if (!Directory.Exists(sourceDir))
            {
                Debug.LogWarning($"[BuildAndroid] 找不到演示歌曲目录 {sourceDir}");
                return;
            }

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                if (file.EndsWith(".meta")) continue;
                var name = Path.GetFileName(file);
                var destination = Path.Combine(targetRoot, DemoSongFolder, name);
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? targetRoot);
                File.Copy(file, destination, true);
                manifest.Add(DemoSongFolder + "/" + name);
            }

            File.WriteAllLines("Assets/StreamingAssets/" + SongRepository.ManifestName, manifest);
            Debug.Log($"[BuildAndroid] 已同步演示歌曲 {DemoSongFolder}（{manifest.Count} 个文件）到 StreamingAssets");
        }

        /// <summary>把 Assets/Songs 下的歌曲包复制到打包输出目录的 Songs 子目录（排除 .meta）</summary>
        static void CopySongsToBuild(string outputExePath)
        {
            var targetRoot = Path.Combine(Path.GetDirectoryName(outputExePath) ?? ".", "Songs");
            if (Directory.Exists(targetRoot)) Directory.Delete(targetRoot, true);
            Directory.CreateDirectory(targetRoot);

            if (!Directory.Exists(SongsFolder))
            {
                Debug.LogWarning($"[BuildDemo] 找不到歌曲目录 {SongsFolder}");
                return;
            }

            // 根目录下的压缩包（.zip / .mcz）也要一起带过去
            foreach (var file in Directory.GetFiles(SongsFolder))
            {
                if (file.EndsWith(".meta")) continue;
                File.Copy(file, Path.Combine(targetRoot, Path.GetFileName(file)), true);
            }

            foreach (var dir in Directory.GetDirectories(SongsFolder))
            {
                if (Path.GetFileName(dir).StartsWith("_")) continue; // 跳过缓存目录
                var target = Path.Combine(targetRoot, Path.GetFileName(dir));
                Directory.CreateDirectory(target);
                foreach (var file in Directory.GetFiles(dir))
                {
                    if (file.EndsWith(".meta")) continue;
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
                }
            }
            Debug.Log($"[BuildDemo] 歌曲包已复制到 {targetRoot}");
        }

        /// <summary>
        /// 在当前场景里搭好整套对象（幂等：已存在的不重复创建，只更新引用）：
        /// Conductor（时钟/节拍器/调试面板）+ Playfield（六边形面板）+ GameRoot（选曲入口）+ 相机。
        /// </summary>
        static void BuildSceneInto()
        {
            EnsureSpriteImports();

            var clock = Object.FindObjectOfType<SongClock>();
            if (clock == null) clock = CreateConductor();
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

            // 给面板挂上皮肤精灵与时钟引用
            var serialized = new SerializedObject(playfield);
            serialized.FindProperty("clock").objectReferenceValue = clock;
            serialized.FindProperty("tapSprite").objectReferenceValue = LoadSprite(SkinFolder + "/SIMPLETap.png");
            serialized.FindProperty("tapBothSprite").objectReferenceValue = LoadSprite(SkinFolder + "/SIMPLETapboth.png");
            serialized.FindProperty("holdHeadSprite").objectReferenceValue = LoadSprite(SkinFolder + "/SIMPLEHold.png");
            serialized.FindProperty("holdBothSprite").objectReferenceValue = LoadSprite(SkinFolder + "/SIMPLEholdboth.png");
            serialized.FindProperty("holdBodySprite").objectReferenceValue = LoadSprite(SkinFolder + "/holdbody.png");
            serialized.FindProperty("holdTailSprite").objectReferenceValue = LoadSprite(SkinFolder + "/SIMPLEHold.png");
            serialized.FindProperty("hexFrameSprite").objectReferenceValue = LoadSprite(SkinFolder + "/lineout.png");
            serialized.FindProperty("bothLineSprite").objectReferenceValue = LoadSprite(SkinFolder + "/both line.png");
            serialized.FindProperty("bestSprite").objectReferenceValue = LoadSprite(SkinFolder + "/best.png");
            serialized.FindProperty("coolSprite").objectReferenceValue = LoadSprite(SkinFolder + "/cool.png");
            serialized.FindProperty("goodSprite").objectReferenceValue = LoadSprite(SkinFolder + "/good.png");
            serialized.FindProperty("missSprite").objectReferenceValue = LoadSprite(SkinFolder + "/miss.png");
            serialized.FindProperty("hitSound").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(SkinFolder + "/cube-arcade.wav");

            // 连击数字（num-0 ~ num-9）与连击标记（ComboTAG）
            var comboDigits = serialized.FindProperty("comboDigitSprites");
            comboDigits.arraySize = 10;
            for (var i = 0; i < 10; i++)
            {
                comboDigits.GetArrayElementAtIndex(i).objectReferenceValue = LoadSprite($"{SkinFolder}/num-{i}.png");
            }
            serialized.FindProperty("comboTagSprite").objectReferenceValue = LoadSprite(SkinFolder + "/ComboTAG.png");

            var fxProperty = serialized.FindProperty("hitFxFrames");
            fxProperty.arraySize = HitFxFrameCount;
            for (var i = 0; i < HitFxFrameCount; i++)
            {
                fxProperty.GetArrayElementAtIndex(i).objectReferenceValue = LoadSprite($"{SkinFolder}/hit-{i}.png");
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 游戏入口（选曲界面）
            var gameRoot = Object.FindObjectOfType<GameRoot>();
            if (gameRoot == null)
            {
                var go = new GameObject("GameRoot");
                gameRoot = go.AddComponent<GameRoot>();
                Undo.RegisterCreatedObjectUndo(go, "Build Player Scene");
            }
            var serializedRoot = new SerializedObject(gameRoot);
            serializedRoot.FindProperty("playfield").objectReferenceValue = playfield;
            serializedRoot.FindProperty("clock").objectReferenceValue = clock;
            serializedRoot.ApplyModifiedPropertiesWithoutUndo();

            SetupCamera();
        }

        /// <summary>PNG/JPG 默认导入为 Texture，这里统一改成 Sprite 才能给 SpriteRenderer 用</summary>
        static void EnsureSpriteImports()
        {
            foreach (var path in SpriteFiles)
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

        /// <summary>创建 Conductor：音频源（音频在选曲后由 SongClock.LoadClip 换入）+ 时钟 + 节拍器 + 调试面板</summary>
        static SongClock CreateConductor()
        {
            var conductor = new GameObject("Conductor");
            var source = conductor.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.volume = 0.8f;
            conductor.AddComponent<SongClock>();
            conductor.AddComponent<Metronome>();

            Undo.RegisterCreatedObjectUndo(conductor, "Build Player Scene");
            return conductor.GetComponent<SongClock>();
        }

        /// <summary>相机：正交投影，黑背景，位置对准面板中心</summary>
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
