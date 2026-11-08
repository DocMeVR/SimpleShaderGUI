using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DocMe
{
    /// <summary>
    /// <c>[VRCFallback]</c> on the texture VRChat's fallback shows (<c>_MainTex</c>) draws a VRChat Fallback dropdown above
    /// it. The dropdown sets the material's "VRCFallback" override tag, which VRChat reads before the shader's own tag to
    /// choose how players who block custom shaders see the material. This class draws the dropdown in the classic
    /// inspector and in other inspectors; the new inspector builds its own (ShaderPropertyFields).
    /// </summary>
    public class VRCFallbackDecorator : MaterialPropertyDrawer
    {
        static string language;
        static int generation = -1;
        static GUIContent labelContent;
        static readonly Dictionary<string, (List<VRCFallback.Entry> entries, GUIContent[] options)> menus = new Dictionary<string, (List<VRCFallback.Entry>, GUIContent[])>(StringComparer.Ordinal);
        static readonly Dictionary<string, GUIContent> others = new Dictionary<string, GUIContent>(StringComparer.Ordinal);

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
            => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        /// <summary>The label and the entries in the chosen language, looked up again only after a language or language file change.</summary>
        static (List<VRCFallback.Entry> entries, GUIContent[] options) MenuFor(string shaderTag)
        {
            if (language != ShaderUiLanguage.Current || generation != ShaderTexts.Generation)
            {
                language = ShaderUiLanguage.Current;
                generation = ShaderTexts.Generation;
                menus.Clear();
                others.Clear();
                var texts = ShaderTexts.For(null);
                labelContent = new GUIContent(VRCFallback.Label(texts), VRCFallback.Tooltip(texts));
            }
            if (!menus.TryGetValue(shaderTag, out var menu))
            {
                var entries = VRCFallback.Entries(ShaderTexts.For(null), shaderTag);
                var options = new GUIContent[entries.Count];
                for (int i = 0; i < entries.Count; i++)
                    options[i] = new GUIContent(entries[i].path);
                menu = (entries, options);
                menus[shaderTag] = menu;
            }
            return menu;
        }

        public override void OnGUI(Rect position, MaterialProperty prop, string label, MaterialEditor editor)
        {
            var materials = VRCFallback.MaterialsOf(prop.targets);
            if (materials.Length == 0)
                return;
            string shaderTag = VRCFallback.ShaderTag(materials[0].shader);
            var (entries, options) = MenuFor(shaderTag);
            string current = VRCFallback.Current(materials, shaderTag, out bool mixed);
            int index = mixed ? -1 : VRCFallback.IndexOf(entries, current);
            if (!mixed && index < 0)
            {
                var withOther = new GUIContent[options.Length + 1];
                options.CopyTo(withOther, 0);
                if (!others.TryGetValue(current, out var other))
                {
                    other = new GUIContent(VRCFallback.Other(ShaderTexts.For(null), current).path);
                    others[current] = other;
                }
                withOther[options.Length] = other;
                options = withOther;
                index = options.Length - 1;
            }

            var rect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            bool wasMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = mixed;
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUI.Popup(rect, labelContent, index, options);
            EditorGUI.showMixedValue = wasMixed;
            if (!EditorGUI.EndChangeCheck() || picked < 0 || picked == index || picked >= entries.Count)
                return;
            VRCFallback.Write(materials, entries[picked].value, VRCFallback.UndoName(ShaderTexts.For(null), materials));
        }
    }

    /// <summary>
    /// The entries of the VRChat Fallback dropdown and the material's "VRCFallback" override tag they write. A value is a
    /// shader type, then Cutout, Transparent or Fade (none for opaque), then DoubleSided (none for one sided), as VRChat
    /// reads it; Hidden hides the mesh. Toon Standard and Toon Standard Outline take no other part.
    /// </summary>
    internal static class VRCFallback
    {
        public const string AttributeName = "VRCFallback";
        public const string Tag = "VRCFallback";
        public const string FieldSuffix = "-vrc-fallback";
        public const string StandardName = "Standard";

        internal sealed class Entry
        {
            /// <summary>The menu path, nested at each "/".</summary>
            public readonly string path;

            /// <summary>The tag the entry writes; empty clears the material's own tag so the shader's tag counts.</summary>
            public readonly string value;

            public Entry(string path, string value)
            {
                this.path = path;
                this.value = value;
            }
        }

        public static readonly string[] ShaderTypes = { "Unlit", "Toon", "Standard", "VertexLit", "MobileToon", "Matcap", "Particle", "Sprite" };
        static readonly string[] ModeParts = { "", "Cutout", "Transparent", "Fade" };
        static readonly string[] SideParts = { "", "DoubleSided" };
        static readonly (string path, string value)[] OnlyAlone = { ("Toon Standard", "toonstandard"), ("Toon Standard Outline", "toonstandardoutline") };

        public static string Label(ShaderTexts texts) => texts.Ui("ui.fallback", "VRChat Fallback");

        public static string Tooltip(ShaderTexts texts)
            => texts.Ui("ui.fallback.tooltip", "Picks the shader VRChat draws this material with for players who block custom shaders.");

        /// <summary>Every entry in menu order for a shader whose own tag is shaderTag (empty for none), in the chosen language.</summary>
        public static List<Entry> Entries(ShaderTexts texts, string shaderTag)
        {
            var entries = new List<Entry>
            {
                new Entry(texts.UiFormat("ui.fallback.default", "Shader default ({0})", string.IsNullOrEmpty(shaderTag) ? StandardName : shaderTag), ""),
                new Entry(texts.Ui("ui.fallback.hidden", "Hidden"), "Hidden"),
            };
            string[] modes =
            {
                texts.Ui("ui.fallback.opaque", "Opaque"),
                texts.Ui("ui.fallback.cutout", "Cutout"),
                texts.Ui("ui.fallback.transparent", "Transparent"),
                texts.Ui("ui.fallback.fade", "Fade"),
            };
            string[] sides = { texts.Ui("ui.fallback.oneSided", "One sided"), texts.Ui("ui.fallback.doubleSided", "Double sided") };
            foreach (var type in ShaderTypes)
                for (int m = 0; m < ModeParts.Length; m++)
                    for (int s = 0; s < SideParts.Length; s++)
                        entries.Add(new Entry(type + "/" + modes[m] + "/" + sides[s], type + ModeParts[m] + SideParts[s]));
            foreach (var (path, value) in OnlyAlone)
                entries.Add(new Entry(path, value));
            return entries;
        }

        /// <summary>The entry for a tag this list does not know, such as one another tool wrote: it shows and stays until someone picks another.</summary>
        public static Entry Other(ShaderTexts texts, string value) => new Entry(texts.UiFormat("ui.fallback.other", "Other: {0}", value), value);

        public static string Shown(string path) => string.IsNullOrEmpty(path) ? "" : path.Replace("/", " / ");

        public static int IndexOf(List<Entry> entries, string value)
        {
            for (int i = 0; i < entries.Count; i++)
                if (string.Equals(entries[i].value, value, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        public static string UndoName(ShaderTexts texts, Material[] materials)
        {
            string target = materials.Length == 1 ? materials[0].name : texts.UiFormat("ui.materials", "{0} Materials", materials.Length);
            return texts.UiFormat("ui.undo.flag", "Modify {0} of {1}", Label(texts), target);
        }

        public static Material[] MaterialsOf(Object[] targets)
        {
            if (targets == null)
                return Array.Empty<Material>();
            var materials = new List<Material>(targets.Length);
            foreach (var target in targets)
                if (target is Material material && material != null)
                    materials.Add(material);
            return materials.ToArray();
        }

        static readonly Dictionary<int, (int stamp, string tag)> shaderTags = new Dictionary<int, (int, string)>();

        /// <summary>The shader's own "VRCFallback" tag, empty when it has none. A fresh material has no tag of its own, so it reads the shader's.</summary>
        public static string ShaderTag(Shader shader)
        {
            if (shader == null)
                return "";
            int id = shader.GetInstanceID();
            if (shaderTags.TryGetValue(id, out var known) && known.stamp == ShaderImportWatcher.Stamp)
                return known.tag;
            var probe = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            string tag;
            try
            {
                tag = probe.GetTag(Tag, false, "");
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
            shaderTags[id] = (ShaderImportWatcher.Stamp, tag);
            return tag;
        }

        /// <summary>
        /// The material's own tag, empty when it follows the shader. Material.GetTag answers the shader's tag for a material
        /// without one, so a value equal to the shader's tag is looked up in the material's stored tags.
        /// </summary>
        public static string Override(Material material, string shaderTag)
        {
            string tag = material.GetTag(Tag, false, "");
            if (string.IsNullOrEmpty(shaderTag) || tag != shaderTag)
                return tag;
            using (var serialized = new SerializedObject(material))
            {
                var map = serialized.FindProperty("stringTagMap");
                if (map == null || !map.isArray)
                    return tag;
                for (int i = 0; i < map.arraySize; i++)
                {
                    var pair = map.GetArrayElementAtIndex(i);
                    if (pair.FindPropertyRelative("first")?.stringValue == Tag)
                        return tag;
                }
            }
            return "";
        }

        /// <summary>The first material's own tag; mixed when another selected material has a different one.</summary>
        public static string Current(Material[] materials, string shaderTag, out bool mixed)
        {
            mixed = false;
            if (materials.Length == 0)
                return "";
            string first = Override(materials[0], shaderTag);
            for (int i = 1; i < materials.Length; i++)
                if (Override(materials[i], shaderTag) != first)
                {
                    mixed = true;
                    break;
                }
            return first;
        }

        /// <summary>Sets the tag on every material as one undo step; an empty value removes the material's own tag.</summary>
        public static void Write(Material[] materials, string value, string undoName)
        {
            if (materials.Length == 0)
                return;
            Undo.RecordObjects(materials, undoName);
            foreach (var material in materials)
            {
                material.SetOverrideTag(Tag, value ?? "");
                EditorUtility.SetDirty(material);
            }
            Undo.FlushUndoRecordObjects();
        }
    }
}
