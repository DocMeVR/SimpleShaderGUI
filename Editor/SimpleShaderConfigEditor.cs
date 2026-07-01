using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DocMe
{
    [CustomEditor(typeof(SimpleShaderConfig))]
    public class SimpleShaderConfigEditor : Editor
    {
        const string EditorLine = "CustomEditor \"DocMe.SimpleShaderGUI\"";
        Shader targetShader;
        string prefKey;

        void OnEnable()
        {
            string path = AssetDatabase.GetAssetPath(target);
            prefKey = "DocMe.cfgShader." + AssetDatabase.AssetPathToGUID(path);
            string saved = EditorPrefs.GetString(prefKey, "");
            if (!string.IsNullOrEmpty(saved))
                targetShader = AssetDatabase.LoadAssetAtPath<Shader>(saved);
        }

        public override void OnInspectorGUI()
        {
            var config = (SimpleShaderConfig)target;
            string propLine = $"[HideInInspector][Config({config.name})] _Config (\"\", Float) = 0";

            EditorGUILayout.LabelField("Wire a shader to this config", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "1. Paste near the top of Properties:\n   " + propLine +
                "\n\n2. Paste just before the shader's final }:\n   " + EditorLine,
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy Properties line"))
                    Copy(propLine);
                if (GUILayout.Button("Copy CustomEditor line"))
                    Copy(EditorLine);
            }

            EditorGUI.BeginChangeCheck();
            targetShader = (Shader)EditorGUILayout.ObjectField("Shader (for insert + section list)", targetShader, typeof(Shader), false);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(prefKey, targetShader != null ? AssetDatabase.GetAssetPath(targetShader) : "");
            using (new EditorGUI.DisabledScope(targetShader == null))
            {
                if (GUILayout.Button("Insert both into shader"))
                    InsertIntoShader(targetShader, propLine);
            }

            // Feed the section dropdown used by [SectionName] fields below.
            SectionNameDrawer.Sections = GetSectionTitles(targetShader);

            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        static List<string> GetSectionTitles(Shader shader)
        {
            var titles = new List<string>();
            if (shader == null)
                return titles;

            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                foreach (var raw in shader.GetPropertyAttributes(i))
                {
                    const string key = "Foldable(";
                    if (!raw.StartsWith(key) || !raw.EndsWith(")"))
                        continue;
                    string inside = raw.Substring(key.Length, raw.Length - key.Length - 1);
                    string title = inside.Split(',')[0].Trim();
                    if (!string.IsNullOrEmpty(title) && !titles.Contains(title))
                        titles.Add(title);
                }
            }
            return titles;
        }

        static void Copy(string text)
        {
            EditorGUIUtility.systemCopyBuffer = text;
            if (EditorWindow.focusedWindow != null)
                EditorWindow.focusedWindow.ShowNotification(new GUIContent("Copied"));
        }

        static void InsertIntoShader(Shader shader, string propLine)
        {
            string path = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".shader"))
            {
                EditorUtility.DisplayDialog("Insert failed", "That is not a text .shader file.", "OK");
                return;
            }

            string text = File.ReadAllText(path);
            bool changed = false;

            if (!text.Contains("[Config("))
            {
                int propsIdx = text.IndexOf("Properties", System.StringComparison.Ordinal);
                int brace = propsIdx >= 0 ? text.IndexOf('{', propsIdx) : -1;
                if (brace >= 0)
                {
                    text = text.Insert(brace + 1, "\n        " + propLine);
                    changed = true;
                }
            }

            if (!text.Contains("CustomEditor"))
            {
                int lastBrace = text.LastIndexOf('}');
                if (lastBrace >= 0)
                {
                    text = text.Insert(lastBrace, "    " + EditorLine + "\n");
                    changed = true;
                }
            }

            if (!changed)
            {
                EditorUtility.DisplayDialog("Nothing to do", "This shader already has the config keyword and a CustomEditor.", "OK");
                return;
            }

            File.WriteAllText(path, text);
            AssetDatabase.ImportAsset(path);
            EditorUtility.DisplayDialog("Done", "Wired " + Path.GetFileName(path) + " to this config.", "OK");
        }
    }
}
