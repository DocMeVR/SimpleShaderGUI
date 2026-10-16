using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DocMe
{
    /// <summary>
    /// Keeps a shader's language files in step with the shader: adds every new key with its English, marks a translation
    /// whose English changed as outdated, keeps the inspector's order and never deletes a translation (keys no setting
    /// uses any more move to the end as obsolete entries).
    /// </summary>
    public static class LanguageFileUpdater
    {
        public const string EnglishCode = "en-US";

        static readonly Regex UiCall = new Regex("\\bUi(?:Format)?\\(\\s*\"(ui\\.[^\"]+)\"\\s*,\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");

        /// <summary>The keys whose English lives in the English file rather than in the shader: tooltips and help.</summary>
        public static bool InEnglishFile(string key)
            => key.EndsWith(".tip", StringComparison.Ordinal) || key.EndsWith(".help", StringComparison.Ordinal);

        /// <summary>Every key of a shader's inspector with its English, in inspector order.</summary>
        public static List<(string key, string english)> Keys(Shader shader, PoFile englishFile)
        {
            var keys = new List<(string key, string english)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Add(string key, string english)
            {
                if (seen.Add(key))
                    keys.Add((key, english ?? ""));
            }
            string English(string key, string fromShader) => englishFile?.Text(key) ?? fromShader ?? "";

            void AddItem(ShaderLayout.Item item)
            {
                Add(item.name, item.englishName);
                Add(item.name + ".tip", English(item.name + ".tip", item.tooltip));
                string helpKey = item.name + ".help";
                if (!string.IsNullOrEmpty(item.help) || englishFile?.Find(helpKey) != null)
                    Add(helpKey, English(helpKey, item.help));
                bool headerSeen = false;
                foreach (var raw in item.attributes)
                {
                    ShaderLayout.ParseAttr(raw, out string name, out string[] args);
                    if (!headerSeen && ShaderPropertyFields.IsHeader(name) && args.Length == 1)
                    {
                        headerSeen = true;
                        Add(item.name + ".header", args[0]);
                    }
                    var options = ShaderPropertyFields.OptionNames(name, args);
                    if (options != null)
                        foreach (var option in options)
                            Add(item.name + ".opt." + option, option);
                }
            }

            var layout = ShaderLayout.Build(shader);
            foreach (var item in layout.Ungrouped)
                AddItem(item);
            foreach (var section in layout.Sections)
            {
                if (!string.IsNullOrEmpty(section.category))
                    Add("category." + section.category, section.category);
                Add("section." + section.name, section.name);
                Add("section." + section.name + ".help", English("section." + section.name + ".help", null));
                foreach (var item in section.items)
                    AddItem(item);
            }

            var config = ShaderUiServices.LoadConfig(layout.ConfigName);
            if (config != null)
            {
                foreach (var link in config.bannerLinks ?? new List<SimpleShaderConfig.Link>())
                    if (!string.IsNullOrEmpty(link?.label))
                        Add("link." + link.label, link.label);
                foreach (var box in config.linkBoxes ?? new List<SimpleShaderConfig.LinkGroup>())
                {
                    if (box == null)
                        continue;
                    if (!string.IsNullOrEmpty(box.title))
                        Add("link." + box.title, box.title);
                    foreach (var link in box.links ?? new List<SimpleShaderConfig.Link>())
                        if (!string.IsNullOrEmpty(link?.label))
                            Add("link." + link.label, link.label);
                }
            }
            return keys;
        }

        /// <summary>Brings the English file up to date: every tooltip and help key in inspector order; a [Tip] or [Help] still in the shader is the first text of a new key.</summary>
        public static void UpdateEnglish(PoFile file, Shader shader)
        {
            if (file.HeaderValue(PoFile.LanguageField) == null)
                file.SetHeader(PoFile.LanguageField, EnglishCode);
            if (file.HeaderValue("Content-Type") == null)
                file.SetHeader("Content-Type", "text/plain; charset=UTF-8");
            var wanted = Keys(shader, file).Where(k => InEnglishFile(k.key)).ToList();
            Merge(file, wanted, (entry, english) => { }, (key, english) => new PoFile.Entry { key = key, text = english });
        }

        /// <summary>Brings a translation up to date with the wanted keys and their English; memory gives a new key a translation another file already has.</summary>
        public static void UpdateTranslation(PoFile file, List<(string key, string english)> wanted, Func<string, string, string> memory)
        {
            Merge(file, wanted,
                (entry, english) =>
                {
                    if (entry.english == english)
                    {
                        if (!entry.fuzzy)
                            entry.previousEnglish = null;
                        return;
                    }
                    if (entry.text.Length > 0)
                    {
                        if (!entry.fuzzy)
                            entry.previousEnglish = entry.english;
                        entry.fuzzy = true;
                    }
                    entry.english = english;
                },
                (key, english) => new PoFile.Entry { key = key, english = english, text = memory?.Invoke(key, english) ?? "" });
        }

        static void Merge(PoFile file, List<(string key, string english)> wanted, Action<PoFile.Entry, string> keep, Func<string, string, PoFile.Entry> create)
        {
            var old = new Dictionary<string, PoFile.Entry>(StringComparer.Ordinal);
            var order = new List<PoFile.Entry>();
            foreach (var entry in file.Entries)
            {
                if (old.ContainsKey(entry.key))
                    continue;
                old[entry.key] = entry;
                order.Add(entry);
            }
            var result = new List<PoFile.Entry>();
            foreach (var (key, english) in wanted)
            {
                if (old.TryGetValue(key, out var entry))
                {
                    old.Remove(key);
                    entry.obsolete = false;
                    keep(entry, english);
                    result.Add(entry);
                }
                else
                    result.Add(create(key, english));
            }
            foreach (var entry in order)
            {
                if (!old.ContainsKey(entry.key))
                    continue;
                entry.obsolete = true;
                result.Add(entry);
            }
            file.SetEntries(result);
        }

        /// <summary>Translations the given files have, for the same kind of key (label, tooltip, entry, section) with the same English.</summary>
        internal static Func<string, string, string> MemoryOf(IEnumerable<PoFile> files)
        {
            var known = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in files)
                foreach (var entry in file.Entries)
                {
                    if (entry.obsolete || entry.fuzzy || entry.text.Length == 0 || entry.english == null)
                        continue;
                    string id = KindOf(entry.key) + "\n" + entry.english;
                    if (!known.ContainsKey(id))
                        known[id] = entry.text;
                }
            return (key, english) => known.TryGetValue(KindOf(key) + "\n" + english, out var text) ? text : null;
        }

        static string KindOf(string key)
        {
            if (key.StartsWith("section.", StringComparison.Ordinal))
                return key.EndsWith(".help", StringComparison.Ordinal) ? "section.help" : "section";
            if (key.StartsWith("category.", StringComparison.Ordinal))
                return "category";
            if (key.StartsWith("link.", StringComparison.Ordinal))
                return "link";
            if (key.StartsWith("ui.", StringComparison.Ordinal))
                return "ui";
            if (key.Contains(".opt."))
                return "opt";
            int dot = key.LastIndexOf('.');
            return dot < 0 ? "label" : key.Substring(dot + 1);
        }

        /// <summary>Every Shader UI text the code passes to Ui or UiFormat as a key and an English literal, in code order. problems lists a key given two different English texts.</summary>
        public static List<(string key, string english)> UiKeys(out List<string> problems)
        {
            problems = new List<string>();
            var keys = new List<(string key, string english)>();
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in CodeFiles())
                foreach (Match match in UiCall.Matches(File.ReadAllText(path)))
                {
                    string key = match.Groups[1].Value;
                    string english = PoFile.Unescape(match.Groups[2].Value);
                    if (seen.TryGetValue(key, out var known))
                    {
                        if (known != english)
                            problems.Add(key + " has two English texts: \"" + known + "\" and \"" + english + "\"");
                        continue;
                    }
                    seen[key] = english;
                    keys.Add((key, english));
                }
            return keys;
        }

        static IEnumerable<string> CodeFiles()
        {
            string guid = AssetDatabase.FindAssets("ShaderInspectorView t:MonoScript").FirstOrDefault();
            if (guid == null)
                return Array.Empty<string>();
            string folder = Path.GetDirectoryName(Path.GetFullPath(AssetDatabase.GUIDToAssetPath(guid)));
            return Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal);
        }

        /// <summary>Updates every language file of a shader and creates its English file beside the shader when there is none. Returns the files written.</summary>
        public static List<string> Update(Shader shader)
        {
            var written = new List<string>();
            string baseName = ShaderLayout.Build(shader).ConfigName;
            if (string.IsNullOrEmpty(baseName))
                return written;
            var paths = ShaderTexts.FindPaths(baseName);
            string englishPath = paths.FirstOrDefault(p => ShaderUiLanguage.Normalize(ShaderTexts.CodeOf(p, baseName)) == ShaderUiLanguage.English)
                ?? Path.GetDirectoryName(AssetDatabase.GetAssetPath(shader)).Replace('\\', '/') + "/" + baseName + "." + EnglishCode + ShaderTexts.Extension;
            var english = Load(englishPath);
            UpdateEnglish(english, shader);
            Save(englishPath, english, written);
            var wanted = Keys(shader, english);
            foreach (var path in paths)
            {
                string code = ShaderTexts.CodeOf(path, baseName);
                if (path == englishPath || ShaderUiLanguage.Normalize(code) == ShaderUiLanguage.English)
                    continue;
                var file = Load(path);
                PrepareHeader(file, code);
                UpdateTranslation(file, wanted, MemoryOf(LanguageFilesOf(code)));
                Save(path, file, written);
            }
            return written;
        }

        /// <summary>Updates the Shader UI's own language files from the texts in its code. Returns the files written.</summary>
        public static List<string> UpdateShaderUi()
        {
            var written = new List<string>();
            var wanted = UiKeys(out _);
            foreach (var path in ShaderTexts.FindPaths(ShaderTexts.UiBaseName))
            {
                string code = ShaderTexts.CodeOf(path, ShaderTexts.UiBaseName);
                if (ShaderUiLanguage.Normalize(code) == ShaderUiLanguage.English)
                    continue;
                var file = Load(path);
                PrepareHeader(file, code);
                UpdateTranslation(file, wanted, null);
                Save(path, file, written);
            }
            return written;
        }

        /// <summary>A new, empty translation starts with its language, its encoding and the "not reviewed" mark.</summary>
        static void PrepareHeader(PoFile file, string code)
        {
            bool fresh = file.Entries.Count == 0;
            if (file.HeaderValue(PoFile.LanguageField) == null)
                file.SetHeader(PoFile.LanguageField, code);
            if (file.HeaderValue("Content-Type") == null)
                file.SetHeader("Content-Type", "text/plain; charset=UTF-8");
            if (fresh && file.HeaderValue(PoFile.ReviewedField) == null)
                file.SetHeader(PoFile.ReviewedField, "no");
        }

        static IEnumerable<PoFile> LanguageFilesOf(string code)
        {
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.EndsWith(ShaderTexts.Extension, StringComparison.OrdinalIgnoreCase))
                    continue;
                string name = Path.GetFileNameWithoutExtension(path);
                int dot = name.LastIndexOf('.');
                if (dot >= 0 && string.Equals(name.Substring(dot + 1), code, StringComparison.OrdinalIgnoreCase))
                    yield return Load(path);
            }
        }

        static PoFile Load(string path)
        {
            string full = Path.GetFullPath(path);
            return File.Exists(full) ? PoFile.Parse(File.ReadAllText(full)) : new PoFile();
        }

        static void Save(string path, PoFile file, List<string> written)
        {
            string full = Path.GetFullPath(path);
            string text = file.Write();
            if (File.Exists(full) && File.ReadAllText(full) == text)
                return;
            File.WriteAllText(full, text, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
            written.Add(path);
        }

        [MenuItem("Assets/DocMe Shader UI/Update Language Files", false, 2000)]
        static void UpdateSelected()
        {
            var written = new List<string>();
            foreach (var shader in Selection.objects.OfType<Shader>())
                written.AddRange(Update(shader));
            Debug.Log(written.Count == 0
                ? "[DocMe Shader UI] The language files were already up to date."
                : "[DocMe Shader UI] Updated " + string.Join(", ", written));
        }

        [MenuItem("Assets/DocMe Shader UI/Update Language Files", true)]
        static bool CanUpdateSelected()
            => Selection.objects.OfType<Shader>().Any(s => !string.IsNullOrEmpty(ShaderLayout.Build(s).ConfigName));
    }
}
