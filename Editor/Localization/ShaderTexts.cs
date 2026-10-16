using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

namespace DocMe
{
    /// <summary>
    /// The texts of one shader inspector in the chosen language. Language files are named after the shader's [Config]
    /// ("CyberCatEye.ja-JP.po") and "DocMeShaderUI.ja-JP.po" for the Shader UI's own texts, anywhere in the project.
    /// A text comes from the chosen language, then another file of the same base language, then English, then the
    /// text the shader or the code gives, so a raw key never shows.
    /// </summary>
    public sealed class ShaderTexts
    {
        public const string UiBaseName = "DocMeShaderUI";
        public const string Extension = ".po";
        public const string PseudoOpen = "【";
        public const string PseudoClose = "】";

        static readonly Regex CodePattern = new Regex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$");
        static readonly Dictionary<string, Dictionary<string, PoFile>> filesByBase = new Dictionary<string, Dictionary<string, PoFile>>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, ShaderTexts> cache = new Dictionary<string, ShaderTexts>(StringComparer.Ordinal);
        static readonly HashSet<string> registered = new HashSet<string>(StringComparer.Ordinal);
        static Func<string, IEnumerable<(string code, string text)>> fileOverride;

        readonly List<(string code, PoFile file)> chain;
        readonly List<(string code, PoFile file)> uiChain;
        readonly bool pseudo;

        /// <summary>The language asked for, lowercase ("ja-jp").</summary>
        public string Language { get; }

        /// <summary>False while a file this inspector reads for the chosen language says X-DocMe-Reviewed: no.</summary>
        public bool Reviewed { get; }

        /// <summary>The languages this inspector has a file for, lowercase, English first.</summary>
        public IReadOnlyList<string> Languages { get; }

        /// <summary>Test seam: the files of a base name as (code, file text) pairs. Returning null leaves that name to the project search.</summary>
        public static Func<string, IEnumerable<(string code, string text)>> FileOverride
        {
            get => fileOverride;
            set
            {
                fileOverride = value;
                Invalidate();
            }
        }

        /// <summary>Counts every text lookup, so a test can prove that a redraw does none.</summary>
        public static int LookupCount { get; private set; }

        /// <summary>Goes up whenever a language file changes; open inspectors rebuild when it does.</summary>
        public static int Generation { get; private set; }

        public static ShaderTexts For(string configName)
        {
            string language = ShaderUiLanguage.Current;
            string key = (configName ?? "") + "|" + language;
            if (!cache.TryGetValue(key, out var texts))
            {
                texts = new ShaderTexts(configName, language);
                cache[key] = texts;
            }
            return texts;
        }

        public static void Invalidate()
        {
            filesByBase.Clear();
            cache.Clear();
            registered.Clear();
            Generation++;
        }

        ShaderTexts(string configName, string language)
        {
            Language = language;
            pseudo = language == ShaderUiLanguage.Pseudo;
            var files = string.IsNullOrEmpty(configName) ? new Dictionary<string, PoFile>() : FilesOf(configName);
            var uiFiles = FilesOf(UiBaseName);
            string lookup = pseudo ? ShaderUiLanguage.English : language;
            chain = Chain(files, lookup);
            uiChain = Chain(uiFiles, lookup);
            Reviewed = chain.Concat(uiChain).All(f => f.code == ShaderUiLanguage.English || f.file.Reviewed);

            var codes = new List<string> { ShaderUiLanguage.English };
            foreach (var code in files.Keys.Concat(uiFiles.Keys).OrderBy(c => c, StringComparer.Ordinal))
                if (!codes.Contains(code))
                    codes.Add(code);
            Languages = codes;
            foreach (var code in codes)
                if (registered.Add(code))
                    ShaderUiLanguage.Store.Register(code);
        }

        public string Text(string key, string fallback)
        {
            LookupCount++;
            string text = Find(chain, key) ?? fallback;
            return pseudo && !string.IsNullOrEmpty(text) ? PseudoOf(text) : text;
        }

        public string Ui(string key, string english)
        {
            LookupCount++;
            string text = Find(uiChain, key) ?? english;
            return pseudo ? PseudoOf(text) : text;
        }

        /// <summary>A Shader UI sentence with {0} placeholders; a translation with wrong placeholders gives the English sentence.</summary>
        public string UiFormat(string key, string english, params object[] args)
        {
            try
            {
                return string.Format(Ui(key, english), args);
            }
            catch (FormatException)
            {
                return string.Format(english, args);
            }
        }

        public static string PseudoOf(string text) => PseudoOpen + text + PseudoClose;

        static string Find(List<(string code, PoFile file)> files, string key)
        {
            foreach (var (_, file) in files)
            {
                string text = file.Text(key);
                if (text != null)
                    return text;
            }
            return null;
        }

        static List<(string code, PoFile file)> Chain(Dictionary<string, PoFile> files, string language)
        {
            var chain = new List<(string code, PoFile file)>();
            void Add(string code)
            {
                if (files.TryGetValue(code, out var file) && !chain.Exists(c => c.code == code))
                    chain.Add((code, file));
            }
            Add(language);
            string baseLanguage = ShaderUiLanguage.BaseOf(language);
            foreach (var code in files.Keys.OrderBy(c => c, StringComparer.Ordinal))
                if (ShaderUiLanguage.BaseOf(code) == baseLanguage)
                    Add(code);
            Add(ShaderUiLanguage.English);
            return chain;
        }

        static Dictionary<string, PoFile> FilesOf(string baseName)
        {
            if (filesByBase.TryGetValue(baseName, out var files))
                return files;
            files = new Dictionary<string, PoFile>(StringComparer.Ordinal);
            foreach (var (code, text) in fileOverride?.Invoke(baseName) ?? ReadProjectFiles(baseName))
            {
                string normalized = ShaderUiLanguage.Normalize(code);
                if (!files.ContainsKey(normalized))
                    files[normalized] = PoFile.Parse(text);
            }
            filesByBase[baseName] = files;
            return files;
        }

        static IEnumerable<(string code, string text)> ReadProjectFiles(string baseName)
        {
            var found = new List<(string code, string text)>();
            foreach (var path in FindPaths(baseName))
            {
                try
                {
                    found.Add((CodeOf(path, baseName), File.ReadAllText(Path.GetFullPath(path))));
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
            return found;
        }

        /// <summary>The project paths of every "baseName.code.po", Packages included, in path order.</summary>
        public static List<string> FindPaths(string baseName)
        {
            var paths = new List<string>();
            if (string.IsNullOrEmpty(baseName))
                return paths;
            foreach (var guid in AssetDatabase.FindAssets(baseName))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (CodeOf(path, baseName) != null && !paths.Contains(path))
                    paths.Add(path);
            }
            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        /// <summary>The language code in a file name such as "CyberCatEye.ja-JP.po", or null when the name does not fit.</summary>
        public static string CodeOf(string path, string baseName)
        {
            string file = Path.GetFileName(path ?? "");
            if (string.IsNullOrEmpty(baseName) || file.Length <= baseName.Length + 1 + Extension.Length)
                return null;
            if (!file.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || !file.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase))
                return null;
            string code = file.Substring(baseName.Length + 1, file.Length - baseName.Length - 1 - Extension.Length);
            return CodePattern.IsMatch(code) ? code : null;
        }
    }

    /// <summary>A language file that is imported, deleted or moved clears the texts, so open inspectors rebuild with it.</summary>
    internal sealed class LanguageFileWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (AnyLanguageFile(importedAssets) || AnyLanguageFile(deletedAssets) || AnyLanguageFile(movedAssets) || AnyLanguageFile(movedFromAssetPaths))
                ShaderTexts.Invalidate();
        }

        static bool AnyLanguageFile(string[] paths)
        {
            foreach (var path in paths)
                if (path.EndsWith(ShaderTexts.Extension, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
