using System;
using System.Linq;
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
    //   [ShowIf(_Prop)] / [ShowIf(_Prop, value)]         show only while _Prop is not 0, or equals value
    //   [SectionToggle]                                  first property of a section as a checkbox in its header
    //   [NoAlpha]                                        colour field without alpha, for a colour whose alpha the shader ignores
    //
    // Banner, banner buttons, link sections and section header links live in the
    // SimpleShaderConfig asset, edited in its Inspector. The shader only names it once.
    //
    // Inside an Inspector the material draws with the UI Toolkit view (ShaderInspectorView), mounted from
    // OnGUI. The IMGUI code below is the fallback: hosts that are not an Inspector, a Unity version where the
    // mount is unavailable, and the per user Classic inspector switch.
    public class SimpleShaderGUI : ShaderGUI
    {
        /// <summary>Test seam: the classic inspector reports where it drew a section's header checkbox, in its container's coordinates.</summary>
        internal static Action<string, Rect> HeaderToggleDrawn;

        string search = "";
        ShaderLayout cachedLayout;
        Shader cachedShader;
        string cachedLanguage;
        int cachedGeneration = -1;
        int cachedStamp = -1;
        ShaderTexts texts;
        readonly Dictionary<string, string> linkTexts = new Dictionary<string, string>();
        GUIContent languageLabel, reviewNote, classicToggle;
        GUIContent[] languageNames;
        string searchTip, linksTitle;

        internal ShaderLayout CachedLayout => cachedLayout;

        /// <summary>The layout in the chosen language, built again only when the shader, a shader file, the language or a language file changed.</summary>
        ShaderLayout LayoutFor(Shader shader)
        {
            string language = ShaderUiLanguage.Current;
            if (cachedLayout != null && cachedShader == shader && cachedLanguage == language
                && cachedGeneration == ShaderTexts.Generation && cachedStamp == ShaderImportWatcher.Stamp)
                return cachedLayout;
            cachedShader = shader;
            cachedLanguage = language;
            cachedGeneration = ShaderTexts.Generation;
            cachedStamp = ShaderImportWatcher.Stamp;
            cachedLayout = ShaderLayout.Build(shader);
            texts = ShaderTexts.For(cachedLayout.ConfigName);
            cachedLayout.Localize(texts);
            CacheTexts(ShaderUiServices.LoadConfig(cachedLayout.ConfigName));
            return cachedLayout;
        }

        void CacheTexts(SimpleShaderConfig config)
        {
            linkTexts.Clear();
            if (config != null)
            {
                foreach (var link in config.bannerLinks ?? new List<SimpleShaderConfig.Link>())
                    AddLinkText(link?.label);
                foreach (var box in config.linkBoxes ?? new List<SimpleShaderConfig.LinkGroup>())
                {
                    if (box == null)
                        continue;
                    AddLinkText(box.title);
                    foreach (var link in box.links ?? new List<SimpleShaderConfig.Link>())
                        AddLinkText(link?.label);
                }
            }
            linksTitle = texts.Ui("ui.links", "Links");
            languageNames = texts.Languages.Select(c => new GUIContent(ShaderUiLanguage.NativeName(c))).ToArray();
            languageLabel = new GUIContent("Language", texts.Ui("ui.language.tooltip", "The language of this inspector. Modular Avatar and other tools that use NDMF share this choice."));
            reviewNote = texts.Reviewed ? null : new GUIContent(texts.Ui("ui.language.review", "This translation has not been checked by a native speaker yet."));
            searchTip = texts.Ui("ui.classic.search", "Search properties. Prefix with s: to search sections.");
            classicToggle = new GUIContent(texts.Ui("ui.classic", "Classic inspector"),
                texts.Ui("ui.classic.back", "Draws every DocMe shader material with this older inspector. Turn it off to use the newer one."));
        }

        void AddLinkText(string label)
        {
            if (!string.IsNullOrEmpty(label) && !linkTexts.ContainsKey(label))
                linkTexts[label] = texts.Text("link." + label, label);
        }

        string LinkText(string label) => !string.IsNullOrEmpty(label) && linkTexts.TryGetValue(label, out var text) ? text : label;

        void DrawLanguage()
        {
            var codes = texts.Languages;
            if (codes.Count < 2)
                return;
            int current = ShaderInspectorView.IndexOfCode(codes, texts.Language);
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(languageLabel, current, languageNames);
            if (EditorGUI.EndChangeCheck() && picked >= 0 && picked < codes.Count && picked != current)
                ShaderUiLanguage.Set(codes[picked]);
            if (reviewNote != null)
                EditorGUILayout.LabelField(reviewNote, EditorStyles.wordWrappedMiniLabel);
        }

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

        static GUIStyle headerSwitchStyle;
        static GUIStyle HeaderSwitchStyle
        {
            get
            {
                if (headerSwitchStyle == null)
                    headerSwitchStyle = new GUIStyle(HeaderStyle) { contentOffset = new Vector2(40f, -2f) };
                return headerSwitchStyle;
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
            if (ShaderInspectorMount.TryMount(editor, props, this))
            {
                GUILayout.Space(0f);
                return;
            }

            Material material = editor.target as Material;
            Shader shader = material.shader;

            ShaderLayout layout = LayoutFor(shader);
            var byName = new Dictionary<string, MaterialProperty>(props.Length);
            foreach (var p in props)
                byName[p.name] = p;
            SimpleShaderConfig config = ShaderUiServices.LoadConfig(layout.ConfigName);

            var topBoxes = new List<SimpleShaderConfig.LinkGroup>();
            var bottomBoxes = new List<SimpleShaderConfig.LinkGroup>();
            var afterBoxes = new Dictionary<string, List<SimpleShaderConfig.LinkGroup>>();
            ShaderLayout.BucketLinkBoxes(config, layout.Sections, topBoxes, bottomBoxes, afterBoxes);

            DrawBanner(config);
            DrawLanguage();
            search = DrawSearchField(search, searchTip);
            ShaderLayout.ParseSearch(search, out bool sectionMode, out string query);
            bool searching = !string.IsNullOrEmpty(query);

            if (!searching)
                foreach (var box in topBoxes)
                    DrawLinkBox(layout, box);

            foreach (var item in layout.Ungrouped)
                if (Met(item.showIf, byName) && (!searching || (!sectionMode && ShaderLayout.ItemMatches(item, query))))
                    DrawProperty(editor, byName, item);

            foreach (var s in layout.Sections)
            {
                DrawSection(editor, layout, byName, s, config, query, sectionMode);
                if (!searching && afterBoxes.TryGetValue(s.name, out var boxes))
                    foreach (var box in boxes)
                        DrawLinkBox(layout, box);
            }

            if (!searching)
                foreach (var box in bottomBoxes)
                    DrawLinkBox(layout, box);

            DrawFooter(editor, classicToggle);
        }

        public override void OnClosed(Material material)
        {
            ShaderInspectorMount.OnClosed(this);
            base.OnClosed(material);
        }

        static bool Met(ShaderLayout.Condition condition, Dictionary<string, MaterialProperty> byName)
        {
            if (condition == null)
                return true;
            byName.TryGetValue(condition.property, out var controller);
            return ShaderLayout.ConditionMet(condition, controller);
        }

        void DrawSection(MaterialEditor editor, ShaderLayout layout, Dictionary<string, MaterialProperty> byName,
            ShaderLayout.Section section, SimpleShaderConfig config, string query, bool sectionMode)
        {
            if (!Met(section.showIf, byName))
                return;
            bool searching = !string.IsNullOrEmpty(query);
            if (searching)
            {
                bool visible = sectionMode
                    ? ShaderLayout.SectionMatches(section, query)
                    : section.items.Exists(item => Met(item.showIf, byName) && ShaderLayout.ItemMatches(item, query));
                if (!visible)
                    return;
            }

            if (!string.IsNullOrEmpty(section.category))
                DrawCategoryLabel(section.categoryTitle);

            string key = layout.FoldKey(section.name);
            var links = ShaderLayout.FindSectionLinks(config, section.name);
            MaterialProperty switchProperty = null;
            if (section.toggle != null && Met(section.toggle.showIf, byName))
                byName.TryGetValue(section.toggle.name, out switchProperty);

            bool open;
            if (searching)
            {
                DrawFoldoutHeader(section.title, true, links, section, switchProperty);
                open = true;
            }
            else
            {
                open = DrawFoldoutState(key, section.startClosed, section.title, links, section, switchProperty);
            }

            if (open)
            {
                bool off = switchProperty != null && !switchProperty.hasMixedValue && !ShaderPropertyFields.IsOn(switchProperty);
                var color = GUI.color;
                if (off)
                    GUI.color = new Color(color.r, color.g, color.b, color.a * 0.5f);
                try
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    foreach (var item in section.items)
                        if (item != section.toggle && Met(item.showIf, byName) && (!searching || sectionMode || ShaderLayout.ItemMatches(item, query)))
                            DrawProperty(editor, byName, item);
                    EditorGUILayout.EndVertical();
                }
                finally
                {
                    GUI.color = color;
                }
            }

            EditorGUILayout.Space(2);
        }

        /// <summary>The [SectionToggle] checkbox in a classic section header; it writes the property and its keyword like the Toggle row it replaces.</summary>
        static void DrawHeaderSwitch(Rect rect, ShaderLayout.Section section, MaterialProperty property)
        {
            string tip = string.IsNullOrEmpty(section.toggle.tooltip) ? section.toggle.displayName : section.toggle.tooltip;
            bool wasMixed = EditorGUI.showMixedValue;
            int indent = EditorGUI.indentLevel;
            EditorGUI.showMixedValue = property.hasMixedValue;
            EditorGUI.indentLevel = 0;
            try
            {
                EditorGUI.BeginChangeCheck();
                bool on = EditorGUI.Toggle(rect, ShaderPropertyFields.IsOn(property));
                if (EditorGUI.EndChangeCheck())
                    ShaderPropertyFields.ApplyToggle(property, on, property.type == MaterialProperty.PropType.Int, section.toggleMode, section.toggleKeyword);
            }
            finally
            {
                EditorGUI.showMixedValue = wasMixed;
                EditorGUI.indentLevel = indent;
            }
            GUI.Label(rect, new GUIContent("", tip));
        }

        void DrawLinkBox(ShaderLayout layout, SimpleShaderConfig.LinkGroup box)
        {
            string title = ShaderLayout.LinkBoxTitle(box);
            string key = layout.LinkBoxKey(title);
            bool open = DrawFoldoutState(key, box.startCollapsed, string.IsNullOrEmpty(box.title) ? linksTitle : LinkText(box.title), null);

            if (open)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                foreach (var link in box.links)
                    DrawTextLink(link);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(2);
        }

        void DrawBanner(SimpleShaderConfig config)
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

        void DrawBannerLinks(List<SimpleShaderConfig.Link> links, Vector2 iconSize)
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
                icons[i] = ShaderUiServices.IconOf(links[i]);
                string label = string.IsNullOrEmpty(links[i].label) && icons[i] == null ? links[i].url : LinkText(links[i].label);
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
                        ShaderUiServices.OpenUrl(links[i].url);
                        Event.current.Use();
                    }
                    x += widths[i];
                }
            }
        }

        static void DrawProperty(MaterialEditor editor, Dictionary<string, MaterialProperty> byName, ShaderLayout.Item item)
        {
            if (!byName.TryGetValue(item.name, out var prop))
                return;

            if (!string.IsNullOrEmpty(item.help))
                EditorGUILayout.HelpBox(item.help, MessageType.Info);

            editor.ShaderProperty(prop, new GUIContent(item.displayName, item.tooltip));
        }

        static string DrawSearchField(string search, string tip)
        {
            EditorGUILayout.BeginHorizontal();

            GUIContent icon = EditorGUIUtility.IconContent("Search Icon");
            if (icon != null && icon.image != null)
                GUILayout.Label(new GUIContent(icon.image, tip), GUILayout.Width(18f), GUILayout.Height(18f));

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

        void DrawTextLink(SimpleShaderConfig.Link link)
        {
            Texture icon = ShaderUiServices.IconOf(link);
            string label = string.IsNullOrEmpty(link.label) ? link.url : LinkText(link.label);
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

        static bool DrawFoldoutHeader(string title, bool open, List<SimpleShaderConfig.SectionLink> links,
            ShaderLayout.Section section = null, MaterialProperty switchProperty = null)
        {
            bool hasSwitch = section?.toggle != null && switchProperty != null;
            Rect rect = GUILayoutUtility.GetRect(16f, 22f, HeaderStyle);
            GUI.Box(rect, new GUIContent(title, section?.help), hasSwitch ? HeaderSwitchStyle : HeaderStyle);

            Event e = Event.current;
            Rect switchRect = new Rect(rect.x + 20f, rect.y + (rect.height - 16f) * 0.5f, 16f, 16f);
            if (hasSwitch)
            {
                DrawHeaderSwitch(switchRect, section, switchProperty);
                if (e.type == EventType.Repaint)
                    HeaderToggleDrawn?.Invoke(section.name, switchRect);
            }
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

                    Texture icon = ShaderUiServices.IconOf(link);
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

            // A press on the checkbox is the checkbox's, also while a read only material leaves it disabled.
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition) && !(hasSwitch && switchRect.Contains(e.mousePosition)))
            {
                if (clickedUrl != null)
                    ShaderUiServices.OpenUrl(clickedUrl);
                else
                    open = !open;
                e.Use();
            }

            return open;
        }

        static bool DrawFoldoutState(string key, bool startClosed, string title, List<SimpleShaderConfig.SectionLink> links,
            ShaderLayout.Section section = null, MaterialProperty switchProperty = null)
        {
            bool wasOpen = ShaderUiServices.Prefs.GetBool(key, !startClosed);
            bool open = DrawFoldoutHeader(title, wasOpen, links, section, switchProperty);
            if (open != wasOpen)
                ShaderUiServices.Prefs.SetBool(key, open);
            return open;
        }

        static void DrawFooter(MaterialEditor editor, GUIContent classicToggle)
        {
            EditorGUILayout.Space();
            editor.RenderQueueField();
            editor.EnableInstancingField();
            editor.DoubleSidedGIField();

            if (ShaderUiServices.Classic)
            {
                EditorGUI.BeginChangeCheck();
                bool classic = EditorGUILayout.Toggle(classicToggle, true);
                if (EditorGUI.EndChangeCheck() && !classic)
                {
                    ShaderUiServices.Classic = false;
                    editor.Repaint();
                }
            }
        }

        static void HandleLinkClick(Rect rect, string url)
        {
            if (string.IsNullOrEmpty(url))
                return;
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            Event e = Event.current;
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                ShaderUiServices.OpenUrl(url);
                e.Use();
            }
        }
    }
}
