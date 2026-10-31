using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DocMe
{
    /// <summary>Where the per user choices of the shader inspector live: fold states and the Classic inspector switch.</summary>
    public interface IShaderUiPrefs
    {
        bool GetBool(string key, bool fallback);
        void SetBool(string key, bool value);
    }

    public sealed class EditorPrefsShaderUiPrefs : IShaderUiPrefs
    {
        public bool GetBool(string key, bool fallback) => EditorPrefs.GetBool(key, fallback);
        public void SetBool(string key, bool value) => EditorPrefs.SetBool(key, value);
    }

    public sealed class MemoryShaderUiPrefs : IShaderUiPrefs
    {
        public readonly Dictionary<string, bool> Values = new Dictionary<string, bool>();
        public bool GetBool(string key, bool fallback) => Values.TryGetValue(key, out var value) ? value : fallback;
        public void SetBool(string key, bool value) => Values[key] = value;
    }

    /// <summary>Where the right click Copy and Paste of the shader inspector read and write their text.</summary>
    public interface IShaderUiClipboard
    {
        string Text { get; set; }
    }

    public sealed class SystemShaderUiClipboard : IShaderUiClipboard
    {
        public string Text
        {
            get => EditorGUIUtility.systemCopyBuffer;
            set => EditorGUIUtility.systemCopyBuffer = value;
        }
    }

    public sealed class MemoryShaderUiClipboard : IShaderUiClipboard
    {
        public string Text { get; set; } = "";
    }

    /// <summary>Shared services of both inspector paths, each with a seam so tests never touch EditorPrefs, the clipboard, open a browser or need a real config asset.</summary>
    public static class ShaderUiServices
    {
        public const string ClassicKey = "DocMe.ShaderUI.classic";

        static readonly IShaderUiPrefs DefaultPrefs = new EditorPrefsShaderUiPrefs();
        static IShaderUiPrefs prefs;
        static readonly IShaderUiClipboard DefaultClipboard = new SystemShaderUiClipboard();
        static IShaderUiClipboard clipboard;

        public static IShaderUiPrefs Prefs
        {
            get => prefs ?? DefaultPrefs;
            set => prefs = value;
        }

        public static IShaderUiClipboard Clipboard
        {
            get => clipboard ?? DefaultClipboard;
            set => clipboard = value;
        }

        public static bool Classic
        {
            get => Prefs.GetBool(ClassicKey, false);
            set => Prefs.SetBool(ClassicKey, value);
        }

        /// <summary>Replaces the browser for tests.</summary>
        public static Action<string> UrlOpener;

        /// <summary>Resolves a [Config] name before the asset search, for tests.</summary>
        public static Func<string, SimpleShaderConfig> ConfigOverride;

        static readonly Dictionary<string, SimpleShaderConfig> configCache = new Dictionary<string, SimpleShaderConfig>();
        static readonly Dictionary<string, Texture> iconCache = new Dictionary<string, Texture>();
        static readonly Dictionary<string, string[]> iconAliases = new Dictionary<string, string[]>
        {
            ["web"] = new[] { "BuildSettings.Web.Small", "BuildSettings.WebGL.Small", "d_BuildSettings.WebGL.Small" },
        };

        static readonly Func<string, Texture2D> SilentIconLoader = BuildSilentIconLoader();

        public static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return;
            if (!url.Contains("://"))
                url = "https://" + url;
            // a config from any package can hold the link, and a file:// one would start a program
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                Debug.LogWarning("[DocMe Shader UI] only web links open from the inspector, not " + url);
                return;
            }
            if (UrlOpener != null)
                UrlOpener(url);
            else
                Application.OpenURL(url);
        }

        public static SimpleShaderConfig LoadConfig(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (ConfigOverride != null)
            {
                var overridden = ConfigOverride(name);
                if (overridden != null)
                    return overridden;
            }
            if (configCache.TryGetValue(name, out var cached) && cached != null)
                return cached;

            SimpleShaderConfig found = null;
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:SimpleShaderConfig"))
            {
                var cfg = AssetDatabase.LoadAssetAtPath<SimpleShaderConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (cfg != null && cfg.name == name)
                {
                    found = cfg;
                    break;
                }
            }

            if (found != null)
                configCache[name] = found;
            return found;
        }

        public static Texture IconOf(SimpleShaderConfig.Link link)
            => link.icon != null ? link.icon : ResolveBuiltin(link.builtinIcon);

        public static Texture IconOf(SimpleShaderConfig.SectionLink link)
            => link.icon != null ? link.icon : ResolveBuiltin(string.IsNullOrEmpty(link.builtinIcon) ? "web" : link.builtinIcon);

        /// <summary>
        /// The editor icon of that name, or null. Looked up the way EditorGUIUtility.IconContent does, but
        /// without its console error for a name that does not exist.
        /// </summary>
        public static Texture ResolveBuiltin(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (iconCache.TryGetValue(name, out var cached))
                return cached;

            Texture tex = null;
            if (iconAliases.TryGetValue(name, out var candidates))
            {
                foreach (var candidate in candidates)
                {
                    tex = LoadIcon(candidate);
                    if (tex != null)
                        break;
                }
            }
            else
            {
                tex = LoadIcon(name);
            }

            iconCache[name] = tex;
            return tex;
        }

        static Texture LoadIcon(string name)
        {
            if (SilentIconLoader != null)
                return SilentIconLoader(name);
            return EditorGUIUtility.IconContent(name)?.image;
        }

        static Func<string, Texture2D> BuildSilentIconLoader()
        {
            try
            {
                var method = typeof(EditorGUIUtility).GetMethod("LoadIcon", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                    null, new[] { typeof(string) }, null);
                if (method == null || method.ReturnType != typeof(Texture2D))
                    return null;
                return (Func<string, Texture2D>)Delegate.CreateDelegate(typeof(Func<string, Texture2D>), method);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
