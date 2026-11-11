// Hawkes Studio's build of this repo (project hawkes-net-unity, build "demo"): Hawkes Gen builds the commit in its cloud
// with -executeMethod Hawkes.Demo.Editor.HawkesNetDemoBuild.CloudBuild, which makes the demo scene (one object with
// HawkesNetDemo: the package's Avatars sample) and builds it to WebGL into the folder in env HAWKES_OUT. A failed
// compile of the package or the demo fails the build, so every build checks com.hawkes.net for WebGL (its jslib too).
// In the Editor: Hawkes Net > Build the demo (WebGL) builds into Build/Demo.
#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hawkes.Demo.Editor
{
    public static class HawkesNetDemoBuild
    {
        public const string ScenePath = "Assets/HawkesNetDemo/Demo.unity";

        /// <summary>Makes (or remakes) the demo scene: a light and one object running HawkesNetDemo.</summary>
        [MenuItem("Hawkes Net/Make the demo scene")]
        public static void MakeScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            new GameObject("Hawkes Net demo").AddComponent<HawkesNetDemo>();
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new Exception("could not save " + ScenePath);
            AssetDatabase.Refresh();
        }

        [MenuItem("Hawkes Net/Build the demo (WebGL)")]
        public static void BuildInEditor() => Build(Path.GetFullPath("Build/Demo"), false);

        /// <summary>Hawkes Gen's cloud build: into HAWKES_OUT, then quits with 0 on success.</summary>
        public static void CloudBuild()
        {
            var outDir = Environment.GetEnvironmentVariable("HAWKES_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.GetFullPath("Build/Demo");
            try { Build(outDir, true); }
            catch (Exception e)
            {
                Debug.LogError("[Hawkes] demo build failed: " + e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Puts a shader in Graphics settings > Always Included Shaders. The demo makes its materials at run time
        /// (Shader.Find), so no asset references the shader and a WebGL build would strip it: magenta everywhere.
        /// </summary>
        static void IncludeShader(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null) throw new Exception("shader not found: " + name);
            var graphics = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (graphics == null || graphics.Length == 0) throw new Exception("could not open ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(graphics[0]);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        static void Build(string outDir, bool quit)
        {
            IncludeShader("Standard");
            // Nameplates (HawkesNetNamePlate) need no included shader: the font carries its own material, and the
            // backing falls back to Standard (fade mode). Including Sprites/Default and GUI/Text Shader here made
            // Unity segfault (exit 139) while compiling shaders in the WebGL build (demo 44-46).
            // Bake the RobotExpressive humanoid + Animator into package Resources before the player build
            // (cloud batchmode does not run InitializeOnLoad delayCalls in time).
            if (!Hawkes.Editor.HawkesNetAvatarBake.Ensure())
                throw new Exception("HawkesNetAvatarBake.Ensure failed: Avatars humanoid Resources were not built");
            MakeScene();
            // Custom page: early window.hawkesPlaytest stub, relative Build/ paths, demo-root.json relocation.
            PlayerSettings.WebGL.template = "PROJECT:HawkesNetDemo";
            // Served by static hosts without Content-Encoding (Hawkes Gen's own default build does the same).
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.runInBackground = true;
            Directory.CreateDirectory(outDir);
            Debug.Log("[Hawkes] building the Hawkes Net demo to WebGL: " + outDir);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            var s = report.summary;
            if (s.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[Hawkes] demo build failed: {s.result}, {s.totalErrors} error(s)");
                if (quit) EditorApplication.Exit(1);
                return;
            }
            WriteDemoRoot(outDir);
            AddPlaytest(outDir);
            Debug.Log($"[Hawkes] built the Hawkes Net demo in {s.totalTime.TotalSeconds:0}s, {s.totalSize} bytes");
            if (quit) EditorApplication.Exit(0);
        }

        /// <summary>
        /// Marker next to index.html so a page opened too deep under the beta site can find the real demo folder
        /// (Assets/WebGLTemplates/HawkesNetDemo probes ./, ../, ../../demo-root.json and location.replace()s).
        /// </summary>
        static void WriteDemoRoot(string outDir)
        {
            File.WriteAllText(Path.Combine(outDir, "demo-root.json"), "{\"demo\":\"hawkes-net-unity\"}\n");
        }

        /// <summary>
        /// Hawkes Studio's playtest bot (DevTools/demo-web: window.hawkesPlaytest): copied beside the build's index.html and loaded by
        /// it, so a Studio playtest opens a second player in the Unity player's room and checks poses and messages both
        /// ways. Nothing runs unless a playtest starts.
        /// </summary>
        static void AddPlaytest(string outDir)
        {
            var web = Path.GetFullPath("DevTools/demo-web");
            foreach (var f in new[] { "net-playtest.mjs", "unity-playtest.mjs", "mobile.css" }) File.Copy(Path.Combine(web, f), Path.Combine(outDir, f), true);
            var index = Path.Combine(outDir, "index.html");
            if (!File.Exists(index)) throw new Exception("the WebGL build has no index.html to add the playtest bot to");
            const string tag = "<script type=\"module\" src=\"unity-playtest.mjs\"></script>";
            var html = File.ReadAllText(index);
            html = MakeMobileFriendly(html);
            if (!html.Contains(tag)) html = html.Contains("</body>") ? html.Replace("</body>", tag + "\n</body>") : html + "\n" + tag + "\n";
            File.WriteAllText(index, html);
        }

        /// <summary>
        /// The demo page on a phone: one viewport meta (device width, no zoom), the style sheet that makes the canvas fill the
        /// screen (mobile.css), and the template's "devicePixelRatio = 1" (if this Unity's template sets it) left at the
        /// phone's own ratio, so the canvas is drawn at the screen's real resolution.
        /// </summary>
        static string MakeMobileFriendly(string html)
        {
            html = System.Text.RegularExpressions.Regex.Replace(html, "<meta\\s+name=[\"']viewport[\"'][^>]*>\\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            html = System.Text.RegularExpressions.Regex.Replace(html, "(?m)^(\\s*)config\\.devicePixelRatio\\s*=\\s*1\\s*;", "$1config.devicePixelRatio = window.devicePixelRatio;");
            const string head = "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no, viewport-fit=cover\">\n<link rel=\"stylesheet\" href=\"mobile.css\">\n";
            if (html.Contains("href=\"mobile.css\"")) return html;
            return html.Contains("</head>") ? html.Replace("</head>", head + "</head>") : head + html;
        }
    }
}
#endif
