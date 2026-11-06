using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hawkes.Editor
{
    /// <summary>Window > Hawkes Net > Setup. Checks the project and the scene for what a first join needs (a Hawkes Net
    /// project id, a build target, a player) and flags what is missing, with a button where one can fix it.</summary>
    public sealed class HawkesNetSetupWindow : EditorWindow
    {
        const string ProjectKey = "Hawkes.Net.ProjectId";   // per machine, per Unity project (see Key())
        const string Site = "https://net.hawkesgen.ai";

        string projectId = "";
        Vector2 scroll;

        [MenuItem("Window/Hawkes Net/Setup")]
        public static void Open()
        {
            var w = GetWindow<HawkesNetSetupWindow>("Hawkes Net Setup");
            w.minSize = new Vector2(380, 360);
            w.Show();
        }

        static string Key() => ProjectKey + ":" + Application.dataPath;

        void OnEnable() { projectId = EditorPrefs.GetString(Key(), ""); }

        // The checks read the open scene: redraw when it, the selection or the build target changes.
        void OnHierarchyChange() { Repaint(); }
        void OnProjectChange() { Repaint(); }
        void OnFocus() { Repaint(); }

        enum Level { Ok, Warn, Missing }

        static void Row(Level level, string title, string detail)
        {
            var icon = level == Level.Ok ? "TestPassed" : level == Level.Warn ? "console.warnicon.sml" : "console.erroricon.sml";
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(EditorGUIUtility.IconContent(icon), GUILayout.Width(20), GUILayout.Height(18));
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label(title, EditorStyles.boldLabel);
                    if (!string.IsNullOrEmpty(detail)) EditorGUILayout.LabelField(detail, EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        static T[] Find<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<T>();
#endif
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            GUILayout.Space(6);
            GUILayout.Label("Hawkes Net setup", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("Five things a first join needs. Each line says what is missing and how to fix it.", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(6);

            // 1. The project id.
            var id = (projectId ?? "").Trim();
            var avatars = Find<HawkesNetAvatars>();
            var withProject = 0;
            foreach (var a in avatars) if (!string.IsNullOrEmpty(a.Project)) withProject++;
            EditorGUILayout.LabelField("1. Your project id", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            projectId = EditorGUILayout.TextField("Project id (usr_...)", projectId);
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(Key(), (projectId ?? "").Trim());
            if (id.Length == 0 && withProject == 0)
                Row(Level.Missing, "No project id yet", "Make a free account and a project at net.hawkesgen.ai, then paste the project's id above. Games pass it as `project`.");
            else if (id.Length > 0 && !id.StartsWith("usr_"))
                Row(Level.Warn, "That id does not look right", "Project ids start with usr_ . Copy it from the project's page on net.hawkesgen.ai.");
            else
                Row(Level.Ok, "Project id set", id.Length > 0 ? id : "Set on the Avatars component in this scene.");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open net.hawkesgen.ai")) Application.OpenURL(Site);
                using (new EditorGUI.DisabledScope(id.Length == 0 || avatars.Length == 0))
                    if (GUILayout.Button("Use it on the scene's Avatars"))
                        foreach (var a in avatars) { Undo.RecordObject(a, "Hawkes Net project"); a.Project = id; EditorUtility.SetDirty(a); }
            }
            GUILayout.Space(8);

            // 2. A player and a place in the scene.
            EditorGUILayout.LabelField("2. The scene", EditorStyles.boldLabel);
            if (avatars.Length == 0)
                Row(Level.Warn, "No HawkesNetAvatars in the open scene", "Add it to an empty GameObject (or import Package Manager > Hawkes Net > Samples > Avatars). Skip this if you use HawkesNetLobby from your own code.");
            else
            {
                var noLocal = new List<string>();
                foreach (var a in avatars) if (a.Local == null) noLocal.Add(a.name);
                if (noLocal.Count > 0) Row(Level.Warn, "Avatars has no Local player", "Set Local on: " + string.Join(", ", noLocal) + ". Others see this transform.");
                else Row(Level.Ok, "Avatars in the scene", avatars.Length + " component(s) with a Local player.");
            }
            GUILayout.Space(8);

            // 3. How it joins in the Editor.
            EditorGUILayout.LabelField("3. Joining from here", EditorStyles.boldLabel);
            Row(Level.Ok, "Play mode joins test rooms", "In the Editor and on localhost a join goes to your project's test rooms (t-...), no key needed. Press Play, and a second Editor or a WebGL tab to meet yourself.");
            GUILayout.Space(8);

            // 4. WebGL.
            EditorGUILayout.LabelField("4. Build target", EditorStyles.boldLabel);
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
                Row(Level.Ok, "WebGL is the active build target", "The page loads Hawkes Net's web SDK.");
            else
            {
                Row(Level.Warn, "Active build target is " + EditorUserBuildSettings.activeBuildTarget, "Fine for the Editor and desktop (the managed client). For a WebGL game, switch the target in Build Settings.");
                if (GUILayout.Button("Open Build Settings")) EditorWindow.GetWindow(System.Type.GetType("UnityEditor.BuildPlayerWindow,UnityEditor"));
            }
            GUILayout.Space(8);

            // 5. Which sites are live.
            EditorGUILayout.LabelField("5. Live sites", EditorStyles.boldLabel);
            var anyLive = false;
            foreach (var a in avatars) if (a.LiveHosts != null && a.LiveHosts.Length > 0) anyLive = true;
            if (anyLive) Row(Level.Ok, "Live hosts listed", "Those pages share the live rooms; every other page uses test rooms.");
            else Row(Level.Warn, "No live hosts yet", "Fine while you build: every page uses test rooms (t-...). Before release, list your site in LiveHosts and under the project's Settings on net.hawkesgen.ai.");

            GUILayout.Space(10);
            if (GUILayout.Button("Read the quickstart")) Application.OpenURL("https://github.com/jgriffith3433/hawkesnet-unity#quickstart");
            EditorGUILayout.EndScrollView();
        }
    }
}
