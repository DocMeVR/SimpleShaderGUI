using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace DocMe
{
    // Shader author vocabulary (attributes in the Properties block):
    //   [Foldable(Title)] / [Foldable(Title, open)]     collapsible section (closed by default)
    //   [Category(Title)]                                bold super-header above a section
    //   [Tip(text)]                                      tooltip (plain text)
    //   [Help(text)]                                     info box above the property (plain text)
    //   [Config(AssetName)]                              name a SimpleShaderConfig asset for banner + links
    //
    // Banner, banner buttons, link sections and section header links live in the
    // SimpleShaderConfig asset, edited in its Inspector. The shader only names it once.
    public class SimpleShaderGUI : ShaderGUI
    {
        const string PrefKeyPrefix = "DocMe.fold";

        class UiProperty
        {
            public MaterialProperty prop;
            public string tooltip;
            public string help;
        }

        class Section
        {
            public string name;
            public string category;
            public bool startClosed;
            public readonly List<UiProperty> properties = new List<UiProperty>();
        }

        class BuildContext
        {
            public readonly List<UiProperty> ungrouped = new List<UiProperty>();
            public readonly List<Section> sections = new List<Section>();
            public UiProperty currentProperty;
            public Section currentGroup;
            public string configName;

            public void StartGroup(string title, bool closed)
            {
                currentGroup = new Section { name = title, startClosed = closed };
                sections.Add(currentGroup);
            }
        }

        // Section openers run first so Category attaches to the section it starts.
        static readonly HashSet<string> Starters = new HashSet<string> { "Foldable" };

        // Add a per-property attribute = one line here, a field to hold it, a read in the renderer.
        static readonly Dictionary<string, Action<string[], BuildContext>> Handlers =
            new Dictionary<string, Action<string[], BuildContext>>
        {
            ["Foldable"] = (a, c) => { if (a.Length > 0) c.StartGroup(a[0], a.Length <= 1 || a[1] != "open"); },
            ["Category"] = (a, c) => { if (c.currentGroup != null && a.Length > 0) c.currentGroup.category = a[0]; },
            ["Tip"]      = (a, c) => { if (c.currentProperty != null && a.Length > 0) c.currentProperty.tooltip = a[0]; },
            ["Help"]     = (a, c) => { if (c.currentProperty != null && a.Length > 0) c.currentProperty.help = a[0]; },
            ["Config"]   = (a, c) => { if (a.Length > 0) c.configName = a[0]; },
        };

        readonly Dictionary<string, string[]> attributeMap = new Dictionary<string, string[]>();
        string search = "";

        static readonly Dictionary<string, Texture> iconCache = new Dictionary<string, Texture>();
        static readonly Dictionary<string, string[]> iconAliases = new Dictionary<string, string[]>
        {
            ["web"] = new[] { "BuildSettings.Web.Small", "BuildSettings.WebGL.Small", "d_BuildSettings.WebGL.Small" },
        };

        static readonly Dictionary<string, SimpleShaderConfig> configCache = new Dictionary<string, SimpleShaderConfig>();

        static GUIStyle headerStyle;
        static GUIStyle HeaderStyle
        {
            get
            {
                if (headerStyle == null)
                    headerStyle = new GUIStyle("ShurikenModuleTitle")
                    {
                        font = EditorStyles.boldLabel.font,
                        fontSize = EditorStyles.boldLabel.fontSize,
                        border = new RectOffset(15, 7, 4, 4),
                        fixedHeight = 22,
                        contentOffset = new Vector2(20f, -2f)
                    };
                return headerStyle;
            }
        }

        static GUIStyle categoryStyle;
        static GUIStyle CategoryStyle
        {
            get
            {
                if (categoryStyle == null)
                    categoryStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = EditorStyles.boldLabel.fontSize + 1
                    };
                return categoryStyle;
            }
        }

        static GUIStyle linkLabelStyle;
        static GUIStyle LinkLabelStyle
        {
            get
            {
                if (linkLabelStyle == null)
                    linkLabelStyle = new GUIStyle(EditorStyles.label)
                    {
                        normal = { textColor = new Color(0.35f, 0.6f, 1f) },
                        hover = { textColor = new Color(0.5f, 0.72f, 1f) }
                    };
                return linkLabelStyle;
            }
        }

        public override void OnGUI(MaterialEditor editor, MaterialProperty[] props)
        {
            Material material = editor.target as Material;
            Shader shader = material.shader;

            BuildAttributeMap(shader);
            BuildContext ctx = Build(props);
            SimpleShaderConfig config = LoadConfig(ctx.configName);

            var topBoxes = new List<SimpleShaderConfig.LinkGroup>();
            var bottomBoxes = new List<SimpleShaderConfig.LinkGroup>();
            var afterBoxes = new Dictionary<string, List<SimpleShaderConfig.LinkGroup>>();
            BucketLinkBoxes(config, ctx.sections, topBoxes, bottomBoxes, afterBoxes);

            DrawBanner(config);
            search = DrawSearchField(search);
            bool sectionMode = search.StartsWith("s:", StringComparison.OrdinalIgnoreCase);
            string query = (sectionMode ? search.Substring(2) : search).Trim();
            bool searching = !string.IsNullOrEmpty(query);

            if (!searching)
                foreach (var box in topBoxes)
                    DrawLinkBox(shader, box);

            foreach (var up in ctx.ungrouped)
                if (!searching || (!sectionMode && PropMatches(up, query)))
                    DrawProperty(editor, up);

            foreach (var s in ctx.sections)
            {
                DrawSection(editor, shader, s, config, query, sectionMode);
                if (!searching && afterBoxes.TryGetValue(s.name, out var boxes))
                    foreach (var box in boxes)
                        DrawLinkBox(shader, box);
            }

            if (!searching)
                foreach (var box in bottomBoxes)
                    DrawLinkBox(shader, box);

            DrawFooter(editor);
        }

        void BuildAttributeMap(Shader shader)
        {
            attributeMap.Clear();
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
                attributeMap[shader.GetPropertyName(i)] = shader.GetPropertyAttributes(i);
        }

        BuildContext Build(MaterialProperty[] props)
        {
            var ctx = new BuildContext();
            foreach (var p in props)
            {
                bool hidden = (p.flags & MaterialProperty.PropFlags.HideInInspector) != 0;
                ctx.currentProperty = hidden ? null : new UiProperty { prop = p };

                Dispatch(p, ctx);

                if (!hidden)
                {
                    if (ctx.currentGroup != null)
                        ctx.currentGroup.properties.Add(ctx.currentProperty);
                    else
                        ctx.ungrouped.Add(ctx.currentProperty);
                }
            }
            return ctx;
        }

        void Dispatch(MaterialProperty p, BuildContext ctx)
        {
            if (!attributeMap.TryGetValue(p.name, out var attrs))
                return;

            RunHandlers(attrs, ctx, onlyStarters: true);
            RunHandlers(attrs, ctx, onlyStarters: false);
        }

        static void RunHandlers(string[] attrs, BuildContext ctx, bool onlyStarters)
        {
            foreach (var raw in attrs)
            {
                ParseAttr(raw, out string name, out string[] args);
                if (Starters.Contains(name) != onlyStarters)
                    continue;
                if (Handlers.TryGetValue(name, out var handler))
                    handler(args, ctx);
            }
        }

        static SimpleShaderConfig LoadConfig(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
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

        void DrawSection(MaterialEditor editor, Shader shader, Section section, SimpleShaderConfig config, string query, bool sectionMode)
        {
            bool searching = !string.IsNullOrEmpty(query);
            if (searching)
            {
                bool visible = sectionMode ? SectionMatches(section, query) : SectionHasPropMatch(section, query);
                if (!visible)
                    return;
            }

            if (!string.IsNullOrEmpty(section.category))
                DrawCategoryLabel(section.category);

            string key = $"{PrefKeyPrefix}.{shader.name}.{section.name}";
            var links = FindSectionLinks(config, section.name);

            bool open;
            if (searching)
            {
                DrawFoldoutHeader(section.name, true, links);
                open = true;
            }
            else
            {
                open = DrawFoldoutState(key, section.startClosed, section.name, links);
            }

            if (open)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                foreach (var up in section.properties)
                    if (!searching || sectionMode || PropMatches(up, query))
                        DrawProperty(editor, up);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(2);
        }

        static void BucketLinkBoxes(SimpleShaderConfig config, List<Section> sections,
            List<SimpleShaderConfig.LinkGroup> top, List<SimpleShaderConfig.LinkGroup> bottom,
            Dictionary<string, List<SimpleShaderConfig.LinkGroup>> after)
        {
            if (config == null || config.linkBoxes == null)
                return;

            var names = new HashSet<string>();
            foreach (var s in sections)
                names.Add(s.name);

            foreach (var box in config.linkBoxes)
            {
                if (box == null || box.links == null || box.links.Count == 0)
                    continue;

                if (!string.IsNullOrEmpty(box.afterSection) && names.Contains(box.afterSection))
                {
                    if (!after.TryGetValue(box.afterSection, out var list))
                    {
                        list = new List<SimpleShaderConfig.LinkGroup>();
                        after[box.afterSection] = list;
                    }
                    list.Add(box);
                }
                else if (box.placement == SimpleShaderConfig.LinkPlacement.Top)
                    top.Add(box);
                else
                    bottom.Add(box);
            }
        }

        void DrawLinkBox(Shader shader, SimpleShaderConfig.LinkGroup box)
        {
            string title = string.IsNullOrEmpty(box.title) ? "Links" : box.title;
            string key = $"{PrefKeyPrefix}.{shader.name}.linkbox.{title}";
            bool open = DrawFoldoutState(key, box.startCollapsed, title, null);

            if (open)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                foreach (var link in box.links)
                    DrawTextLink(link);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(2);
        }

        static void DrawBanner(SimpleShaderConfig config)
        {
            if (config == null)
                return;

            bool hasLinks = config.bannerLinks != null && config.bannerLinks.Count > 0;
            if (config.banner == null && !hasLinks)
                return;

            if (config.banner != null)
            {
                Rect r;
                ScaleMode mode;
                if (config.bannerFit == SimpleShaderConfig.BannerFit.Stretch)
                {
                    r = GUILayoutUtility.GetRect(0f, config.bannerHeight, GUILayout.ExpandWidth(true));
                    mode = ScaleMode.StretchToFill;
                }
                else if (config.bannerFit == SimpleShaderConfig.BannerFit.Crop)
                {
                    r = GUILayoutUtility.GetRect(0f, config.bannerHeight, GUILayout.ExpandWidth(true));
                    mode = ScaleMode.ScaleAndCrop;
                }
                else
                {
                    r = GUILayoutUtility.GetAspectRect(config.banner.width / (float)config.banner.height);
                    mode = ScaleMode.ScaleToFit;
                }

                GUI.DrawTexture(r, config.banner, mode);
                HandleLinkClick(r, config.bannerUrl);
            }

            if (hasLinks)
                DrawBannerLinks(config.bannerLinks, config.bannerIconSize);

            EditorGUILayout.Space(4);
        }

        static void DrawBannerLinks(List<SimpleShaderConfig.Link> links, Vector2 iconSize)
        {
            if (iconSize.x < 1f || iconSize.y < 1f)
                iconSize = new Vector2(16f, 16f);

            GUIStyle linkStyle = LinkLabelStyle;
            GUIStyle sepStyle = EditorStyles.label;
            GUIContent sep = new GUIContent(" | ");
            float sepWidth = sepStyle.CalcSize(sep).x;
            float maxWidth = EditorGUIUtility.currentViewWidth - 24f;
            const float iconGap = 3f;
            float rowHeight = Mathf.Max(EditorGUIUtility.singleLineHeight, iconSize.y);

            int count = links.Count;
            var texts = new GUIContent[count];
            var icons = new Texture[count];
            var widths = new float[count];
            for (int i = 0; i < count; i++)
            {
                icons[i] = IconOf(links[i]);
                string label = string.IsNullOrEmpty(links[i].label) && icons[i] == null ? links[i].url : links[i].label;
                texts[i] = new GUIContent(label ?? "", string.IsNullOrEmpty(links[i].url) ? label : links[i].url);
                float textWidth = string.IsNullOrEmpty(texts[i].text) ? 0f : linkStyle.CalcSize(texts[i]).x;
                float iconWidth = icons[i] != null ? iconSize.x + (textWidth > 0f ? iconGap : 0f) : 0f;
                widths[i] = iconWidth + textWidth;
            }

            // Split into rows up front, then reserve a single rect and draw with manual
            // positions. One layout allocation regardless of row count keeps IMGUI's layout
            // and repaint passes consistent (variable BeginHorizontal groups would not).
            var rowStart = new List<int>();
            var rowEnd = new List<int>();
            var rowWidths = new List<float>();
            int start = 0;
            while (start < count)
            {
                int end = start;
                float rowWidth = widths[start];
                while (end + 1 < count && rowWidth + sepWidth + widths[end + 1] <= maxWidth)
                {
                    end++;
                    rowWidth += sepWidth + widths[end];
                }
                rowStart.Add(start);
                rowEnd.Add(end);
                rowWidths.Add(rowWidth);
                start = end + 1;
            }

            Rect area = GUILayoutUtility.GetRect(maxWidth, rowStart.Count * rowHeight);

            for (int row = 0; row < rowStart.Count; row++)
            {
                float y = area.y + row * rowHeight;
                float x = area.x + Mathf.Max(0f, (area.width - rowWidths[row]) * 0.5f);
                for (int i = rowStart[row]; i <= rowEnd[row]; i++)
                {
                    if (i > rowStart[row])
                    {
                        GUI.Label(new Rect(x, y, sepWidth, rowHeight), sep, sepStyle);
                        x += sepWidth;
                    }

                    Rect itemRect = new Rect(x, y, widths[i], rowHeight);
                    float cursorX = itemRect.x;
                    if (icons[i] != null)
                    {
                        GUI.DrawTexture(new Rect(cursorX, y + (rowHeight - iconSize.y) * 0.5f, iconSize.x, iconSize.y), icons[i], ScaleMode.ScaleToFit);
                        cursorX += iconSize.x + iconGap;
                    }
                    if (!string.IsNullOrEmpty(texts[i].text))
                        GUI.Label(new Rect(cursorX, y, itemRect.xMax - cursorX, rowHeight), texts[i], linkStyle);

                    EditorGUIUtility.AddCursorRect(itemRect, MouseCursor.Link);
                    if (Event.current.type == EventType.MouseDown && itemRect.Contains(Event.current.mousePosition))
                    {
                        OpenUrl(links[i].url);
                        Event.current.Use();
                    }
                    x += widths[i];
                }
            }
        }

        static void DrawProperty(MaterialEditor editor, UiProperty up)
        {
            if (!string.IsNullOrEmpty(up.help))
                EditorGUILayout.HelpBox(up.help, MessageType.Info);

            editor.ShaderProperty(up.prop, new GUIContent(up.prop.displayName, up.tooltip));
        }

        static bool TextMatches(string text, string search)
            => !string.IsNullOrEmpty(text) && text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        static bool PropMatches(UiProperty up, string search)
            => TextMatches(up.prop.displayName, search) || TextMatches(up.prop.name, search);

        static bool SectionMatches(Section section, string search)
            => TextMatches(section.name, search) || TextMatches(section.category, search);

        static bool SectionHasPropMatch(Section section, string search)
        {
            foreach (var up in section.properties)
                if (PropMatches(up, search))
                    return true;
            return false;
        }

        static string DrawSearchField(string search)
        {
            EditorGUILayout.BeginHorizontal();

            GUIContent icon = EditorGUIUtility.IconContent("Search Icon");
            if (icon != null && icon.image != null)
                GUILayout.Label(new GUIContent(icon.image, "Search properties. Prefix with s: to search sections."), GUILayout.Width(18f), GUILayout.Height(18f));

            search = EditorGUILayout.TextField(search);

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(search)))
            {
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22f)))
                {
                    search = "";
                    GUI.FocusControl(null);
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
            return search;
        }

        static void DrawTextLink(SimpleShaderConfig.Link link)
        {
            Texture icon = IconOf(link);
            string label = string.IsNullOrEmpty(link.label) ? link.url : link.label;
            string tip = string.IsNullOrEmpty(link.url) ? label : link.url;

            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect textRect = row;

            if (icon != null)
            {
                Rect iconRect = new Rect(row.x, row.y + (row.height - 14f) * 0.5f, 14f, 14f);
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                textRect.xMin += 18f;
            }

            GUI.Label(textRect, new GUIContent(label, tip), LinkLabelStyle);
            HandleLinkClick(row, link.url);
        }

        static void DrawCategoryLabel(string text)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(text, CategoryStyle);
        }

        static bool DrawFoldoutHeader(string title, bool open, List<SimpleShaderConfig.SectionLink> links)
        {
            Rect rect = GUILayoutUtility.GetRect(16f, 22f, HeaderStyle);
            GUI.Box(rect, title, HeaderStyle);

            Event e = Event.current;
            const float iconSize = 14f;
            const float gap = 6f;
            const float rightPad = 6f;
            float iconY = rect.y + (rect.height - iconSize) * 0.5f;
            float x = rect.xMax - rightPad - iconSize;
            string clickedUrl = null;

            if (links != null)
            {
                foreach (var link in links)
                {
                    if (link == null || string.IsNullOrEmpty(link.url))
                        continue;

                    Texture icon = link.icon != null ? link.icon : ResolveBuiltin(string.IsNullOrEmpty(link.builtinIcon) ? "web" : link.builtinIcon);
                    Rect iconRect = new Rect(x, iconY, iconSize, iconSize);
                    Rect hitRect = new Rect(x - gap * 0.5f, rect.y, iconSize + gap, rect.height);

                    if (e.type == EventType.Repaint && icon != null)
                        GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                    EditorGUIUtility.AddCursorRect(hitRect, MouseCursor.Link);
                    if (e.type == EventType.MouseDown && hitRect.Contains(e.mousePosition))
                        clickedUrl = link.url;

                    x -= iconSize + gap;
                }
            }

            Rect arrow = new Rect(rect.x + 5f, rect.y + 4f, 13f, 13f);
            if (e.type == EventType.Repaint)
                EditorStyles.foldout.Draw(arrow, false, false, open, false);

            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                if (clickedUrl != null)
                    OpenUrl(clickedUrl);
                else
                    open = !open;
                e.Use();
            }

            return open;
        }

        static bool DrawFoldoutState(string key, bool startClosed, string title, List<SimpleShaderConfig.SectionLink> links)
        {
            bool wasOpen = EditorPrefs.GetBool(key, !startClosed);
            bool open = DrawFoldoutHeader(title, wasOpen, links);
            if (open != wasOpen)
                EditorPrefs.SetBool(key, open);
            return open;
        }

        static void DrawFooter(MaterialEditor editor)
        {
            EditorGUILayout.Space();
            editor.RenderQueueField();
            editor.EnableInstancingField();
            editor.DoubleSidedGIField();
        }

        static List<SimpleShaderConfig.SectionLink> FindSectionLinks(SimpleShaderConfig config, string sectionName)
        {
            if (config == null || config.sectionLinks == null)
                return null;

            List<SimpleShaderConfig.SectionLink> result = null;
            foreach (var sl in config.sectionLinks)
            {
                if (sl == null || sl.section != sectionName)
                    continue;
                result ??= new List<SimpleShaderConfig.SectionLink>();
                result.Add(sl);
            }
            return result;
        }

        static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return;
            if (!url.Contains("://"))
                url = "https://" + url;
            Application.OpenURL(url);
        }

        static void HandleLinkClick(Rect rect, string url)
        {
            if (string.IsNullOrEmpty(url))
                return;
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            Event e = Event.current;
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                OpenUrl(url);
                e.Use();
            }
        }

        static Texture IconOf(SimpleShaderConfig.Link link)
            => link.icon != null ? link.icon : ResolveBuiltin(link.builtinIcon);

        static Texture ResolveBuiltin(string name)
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
                    tex = EditorGUIUtility.IconContent(candidate)?.image;
                    if (tex != null)
                        break;
                }
            }
            else
            {
                tex = EditorGUIUtility.IconContent(name)?.image;
            }

            iconCache[name] = tex;
            return tex;
        }

        // "Foldable(Eye Look, closed)" -> name = "Foldable", args = ["Eye Look", "closed"]
        static void ParseAttr(string raw, out string name, out string[] args)
        {
            int open = raw.IndexOf('(');
            int close = raw.LastIndexOf(')');
            if (open < 0 || close < open)
            {
                name = raw.Trim();
                args = Array.Empty<string>();
                return;
            }

            name = raw[..open].Trim();
            string inside = raw[(open + 1)..close];
            args = inside.Split(',');
            for (int i = 0; i < args.Length; i++)
                args[i] = args[i].Trim();
        }
    }
}
