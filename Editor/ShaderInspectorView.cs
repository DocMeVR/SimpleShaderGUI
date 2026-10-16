using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace DocMe
{
    /// <summary>
    /// The UI Toolkit material inspector for every shader that names DocMe.SimpleShaderGUI. Built once per mount
    /// from the shader's layout; values are pulled again only when the materials' dirty counts change.
    /// </summary>
    public sealed class ShaderInspectorView : VisualElement, IShaderFieldHost
    {
        public const int CheckIntervalMs = 100;

        public const string RootClass = "docme-si";
        public const string LightClass = "docme-si--light";
        public const string BannerClass = "docme-si-banner";
        public const string BannerLinksClass = "docme-si-banner-links";
        public const string LinkClass = "docme-si-link";
        public const string LinkIconClass = "docme-si-link-icon";
        public const string LinkTextClass = "docme-si-link-text";
        public const string LinkSeparatorClass = "docme-si-link-separator";
        public const string ClickableClass = "docme-si-clickable";
        public const string SearchRowClass = "docme-si-search-row";
        public const string SearchIconClass = "docme-si-search-icon";
        public const string PlaceholderClass = "docme-si-placeholder";
        public const string CategoryClass = "docme-si-category";
        public const string SectionGroupClass = "docme-si-section-group";
        public const string SectionClass = "docme-si-section";
        public const string SectionOpenClass = "docme-si-section--open";
        public const string LinkBoxClass = "docme-si-linkbox";
        public const string HeaderSpacerClass = "docme-si-header-spacer";
        public const string HeaderLinkClass = "docme-si-header-link";
        public const string LinkRowClass = "docme-si-link-row";
        public const string FooterClass = "docme-si-footer";
        public const string QueueValueClass = "docme-si-queue-value";
        public const string NarrowClass = "docme-si--narrow";
        public const string ChangedClass = "docme-si-changed";
        public const string HeaderToggleClass = "docme-si-header-toggle";
        public const string SectionOffClass = "docme-si-section--off";
        public const string ReviewNoteClass = "docme-si-review-note";
        public const string ChangedTooltip = "Changed from the shader's default";

        /// <summary>Test seam: runs first in every refresh, so a test can stand in for a drawer or material that throws.</summary>
        internal static Action<ShaderInspectorView> RefreshHook;

        /// <summary>Below this width the texture thumbnails give their room to the object fields.</summary>
        public const float NarrowWidth = 360f;

        public const string SearchName = "docme-si-search";
        public const string ClearName = "docme-si-search-clear";
        public const string RenderQueueName = "docme-si-render-queue";
        public const string RenderQueueValueName = "docme-si-render-queue-value";
        public const string InstancingName = "docme-si-instancing";
        public const string DoubleSidedGIName = "docme-si-double-sided-gi";
        public const string ClassicName = "docme-si-classic";
        public const string LanguageName = "docme-si-language";
        public const string ReviewNoteName = "docme-si-language-review";

        sealed class SectionView
        {
            public ShaderLayout.Section section;
            public string key;
            public VisualElement group;
            public Foldout foldout;
            public VisualElement header;
            public VisualElement afterBoxes;
            public VisualElement mark;
            public ShaderPropertyRow toggleRow;
            public readonly List<ShaderPropertyRow> rows = new List<ShaderPropertyRow>();

            /// <summary>Every property of the section: the header switch, then the rows.</summary>
            public IEnumerable<ShaderPropertyRow> AllRows => toggleRow == null ? rows : rows.Prepend(toggleRow);
        }

        static readonly string[] QueueNames = { "From Shader", "Geometry", "AlphaTest", "Transparent" };
        static readonly int[] QueueValues = { -1, 2000, 2450, 3000 };

        static readonly Func<Shader, bool> HasInstancing = BuildHasInstancing();
        static readonly Func<LightingSettings> LightingFallback = BuildLightingFallback();
        static readonly Func<Material, int> RawQueueGetter = BuildRawQueueGetter();
        static readonly MethodInfo IsEnabledMethod = typeof(UnityEditor.Editor).GetMethod("IsEnabled", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null);

        readonly Shader shader;
        readonly ShaderLayout layout;
        readonly SimpleShaderConfig config;
        readonly ShaderTexts texts;
        readonly int builtGeneration;
        string[] queueNames;
        readonly Object[] targetsAtMount;
        readonly string[] propertyNames;
        readonly List<ShaderPropertyRow> rows = new List<ShaderPropertyRow>();
        readonly Dictionary<string, ShaderPropertyRow> rowsByName = new Dictionary<string, ShaderPropertyRow>();
        readonly Dictionary<VisualElement, ShaderPropertyRow> rowsByRoot = new Dictionary<VisualElement, ShaderPropertyRow>();
        readonly List<ShaderPropertyRow> looseRows = new List<ShaderPropertyRow>();
        readonly List<SectionView> sections = new List<SectionView>();
        readonly Dictionary<VisualElement, SectionView> sectionsByHeader = new Dictionary<VisualElement, SectionView>();
        readonly Dictionary<ShaderPropertyRow, SectionView> sectionOf = new Dictionary<ShaderPropertyRow, SectionView>();
        readonly Dictionary<string, MaterialProperty> controllers = new Dictionary<string, MaterialProperty>();
        readonly List<(Foldout foldout, string key, bool startCollapsed)> linkBoxes = new List<(Foldout, string, bool)>();
        readonly List<int> queueValues = new List<int>();

        VisualElement topBoxes, loose, bottomBoxes;
        TextField search;
        Label placeholder;
        Button clear;
        DropdownField renderQueue;
        IntegerField renderQueueValue;
        Toggle instancing, doubleSidedGI, classic;
        DropdownField languagePicker;
        MaterialProperty[] hostProperties;
        long signature;
        bool checkQueued;
        bool searching;
        bool hooked;
        bool lastAnimationMode;
        bool editable = true;
        bool enlightenOn;
        bool failed;

        public MaterialEditor Editor { get; }
        public ShaderGUI Owner { get; }
        public IMGUIContainer Container { get; }
        public int RefreshCount { get; private set; }
        public int WriteCount { get; private set; }
        public bool HooksReleased => !hooked;
        internal ShaderLayout Layout => layout;
        internal SimpleShaderConfig Config => config;
        internal ShaderTexts Texts => texts;
        internal IReadOnlyList<ShaderPropertyRow> Rows => rows;
        public IReadOnlyList<Foldout> SectionFoldouts => sections.Select(s => s.foldout).ToList();
        public IReadOnlyList<Foldout> LinkBoxFoldouts => linkBoxes.Select(b => b.foldout).ToList();
        public TextField SearchField => search;
        public string ShaderPath => shader != null ? AssetDatabase.GetAssetPath(shader) : "";

        public ShaderInspectorView(MaterialEditor editor, ShaderGUI owner, IMGUIContainer container, MaterialProperty[] properties)
        {
            Editor = editor;
            Owner = owner;
            Container = container;
            shader = ((Material)editor.target).shader;
            targetsAtMount = editor.targets.ToArray();
            layout = ShaderLayout.Build(shader);
            builtGeneration = ShaderTexts.Generation;
            texts = ShaderTexts.For(layout.ConfigName);
            layout.Localize(texts);
            config = ShaderUiServices.LoadConfig(layout.ConfigName);
            propertyNames = new string[layout.PropertyCount];
            for (int i = 0; i < propertyNames.Length; i++)
                propertyNames[i] = shader.GetPropertyName(i);
            hostProperties = properties;
            lastAnimationMode = AnimationMode.InAnimationMode();

            AddToClassList(RootClass);
            if (!EditorGUIUtility.isProSkin)
                AddToClassList(LightClass);
            var sheet = ShaderInspectorStyles.Sheet;
            if (sheet != null)
                styleSheets.Add(sheet);
            ApplyFont();

            BuildBanner();
            BuildLanguage();
            BuildSearch();
            BuildBody();
            BuildFooter();
            RefreshCore();
            ApplySearch(ShaderInspectorMount.SearchOf(owner), remember: false);

            RegisterCallback<GeometryChangedEvent>(e => EnableInClassList(NarrowClass, e.newRect.width < NarrowWidth));
            RegisterCallback<AttachToPanelEvent>(_ => Hook());
            RegisterCallback<DetachFromPanelEvent>(_ => ReleaseHooks());
            RegisterCallback<MouseDownEvent>(OnMenuMouseDown);
            RegisterCallback<MouseUpEvent>(OnMenuMouseUp);
            RegisterCallback<ContextualMenuPopulateEvent>(BuildContextMenu);
            schedule.Execute(Check).Every(CheckIntervalMs);
        }

        public void OnContainerDetached(DetachFromPanelEvent evt) => ShaderInspectorMount.Defer(() => ShaderInspectorMount.Unmount(this));

        void Hook()
        {
            if (hooked)
                return;
            hooked = true;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        public void ReleaseHooks()
        {
            hooked = false;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        void OnUndoRedo()
        {
            if (panel != null)
                Check();
        }

        public void SetHostProperties(MaterialProperty[] properties) => hostProperties = properties;

        public bool MatchesShape(MaterialProperty[] properties)
        {
            if (properties == null)
                return true;
            if (properties.Length != propertyNames.Length)
                return false;
            for (int i = 0; i < properties.Length; i++)
                if (properties[i] == null || properties[i].name != propertyNames[i])
                    return false;
            return true;
        }

        public void QueueCheck()
        {
            if (checkQueued)
                return;
            checkQueued = true;
            ShaderInspectorMount.Defer(Check);
        }

        /// <summary>
        /// Unmounts when the host moved on, follows the material slot's collapsed state, refreshes after a change. It runs
        /// on UI Toolkit's scheduler, which repeats a throwing item on every tick and skips everything after it, so a
        /// failure here hands the material to the classic inspector instead of escaping.
        /// </summary>
        public void Check()
        {
            checkQueued = false;
            if (failed)
                return;
            try
            {
                CheckCore();
            }
            catch (Exception e) when (!ShaderPropertyFields.IsExitGui(e))
            {
                Fail(e);
            }
        }

        void CheckCore()
        {
            if (Editor == null || Editor.customShaderGUI != Owner || !TargetsLiveOnShader() || TargetsChanged() || !MatchesShape(hostProperties))
            {
                ShaderInspectorMount.Unmount(this);
                return;
            }
            // Unity disables the whole inspector of a material that is not open for edit; inside it the folds could not open.
            if (ShaderUiServices.Classic || (Container != null && !Container.enabledInHierarchy))
            {
                ShaderInspectorMount.Unmount(this);
                Container?.MarkDirtyLayout();
                Editor.Repaint();
                return;
            }
            if (ShaderUiLanguage.Current != texts.Language || ShaderTexts.Generation != builtGeneration)
            {
                ShaderInspectorMount.Unmount(this);
                Container?.MarkDirtyLayout();
                Editor.Repaint();
                return;
            }

            bool animation = AnimationMode.InAnimationMode();
            if (animation != lastAnimationMode)
            {
                // The host decides in its next OnGUI whether animation mode needs the classic inspector.
                lastAnimationMode = animation;
                Container?.MarkDirtyLayout();
                Container?.MarkDirtyRepaint();
                Editor.Repaint();
            }

            bool show = ShaderInspectorMount.HostShowsBody(Editor);
            var display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (style.display != display)
                style.display = display;
            if (!show)
                return;
            UpdateEnabled();
            if (IsStale())
                RefreshCore();
        }

        void Fail(Exception e)
        {
            if (failed)
                return;
            failed = true;
            ShaderInspectorMount.Fail(this, e);
        }

        /// <summary>Every selected material still exists and uses this view's shader; Unity's own inspector shows no properties for a mixed selection.</summary>
        bool TargetsLiveOnShader()
        {
            var targets = Editor.targets;
            if (targets.Length == 0)
                return false;
            foreach (var target in targets)
                if (!(target is Material material) || material == null || material.shader != shader)
                    return false;
            return true;
        }

        Object[] LiveTargets() => Editor == null ? Array.Empty<Object>() : Editor.targets.Where(t => t is Material m && m != null).ToArray();

        Material[] LiveMaterials() => Editor == null ? Array.Empty<Material>() : Editor.targets.OfType<Material>().Where(m => m != null).ToArray();

        /// <summary>
        /// Read only materials (in a package, inside a model, locked by version control) keep their values read only,
        /// as in Unity's inspector; folds, search, links and the Classic switch stay usable. A property a parent material
        /// locked for its variants is read only in the variant.
        /// </summary>
        void UpdateEnabled()
        {
            editable = IsEditable();
            var materials = LiveMaterials();
            bool variants = materials.Any(m => m.isVariant);
            foreach (var row in rows)
            {
                bool enabled = editable && !row.alwaysReadOnly && !(variants && LockedByAncestor(materials, row.item.name));
                if (row.root.enabledSelf != enabled)
                    row.root.SetEnabled(enabled);
            }
            if (renderQueue.enabledSelf != editable)
                renderQueue.SetEnabled(editable);
            if (instancing.enabledSelf != editable)
                instancing.SetEnabled(editable);
            bool giEnabled = editable && !enlightenOn;
            if (doubleSidedGI.enabledSelf != giEnabled)
                doubleSidedGI.SetEnabled(giEnabled);
        }

        static bool LockedByAncestor(Material[] materials, string property)
        {
            foreach (var material in materials)
                if (material.isVariant && material.IsPropertyLockedByAncestor(property))
                    return true;
            return false;
        }

        bool IsEditable()
        {
            if (AnimationMode.InAnimationMode())
                return true;
            if (IsEnabledMethod != null)
            {
                try
                {
                    return (bool)IsEnabledMethod.Invoke(Editor, null);
                }
                catch (Exception)
                {
                }
            }
            foreach (var target in Editor.targets)
            {
                if (target == null || (target.hideFlags & HideFlags.NotEditable) != 0)
                    return false;
                if (EditorUtility.IsPersistent(target) && !AssetDatabase.IsOpenForEdit(target))
                    return false;
            }
            return true;
        }

        bool Writable(ShaderPropertyRow row)
            => !failed && IsEditable() && !row.alwaysReadOnly && !LockedByAncestor(LiveMaterials(), row.item.name);

        bool TargetsChanged()
        {
            var targets = Editor.targets;
            if (targets.Length != targetsAtMount.Length)
                return true;
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] != targetsAtMount[i])
                    return true;
            return false;
        }

        public bool IsStale() => Editor != null && Signature() != signature;

        long Signature()
        {
            long value = shader != null ? shader.GetInstanceID() : 0;
            foreach (var target in Editor.targets)
            {
                if (target == null)
                    continue;
                value = value * 31 + target.GetInstanceID();
                value = value * 31 + EditorUtility.GetDirtyCount(target);
            }
            return value;
        }

        /// <summary>Pulls every value from the materials (never from the host's last OnGUI, which an undo leaves behind).</summary>
        public void Refresh()
        {
            if (failed || Editor == null)
                return;
            try
            {
                RefreshCore();
            }
            catch (Exception e) when (!ShaderPropertyFields.IsExitGui(e))
            {
                Fail(e);
            }
        }

        void RefreshCore()
        {
            if (Editor == null)
                return;
            RefreshHook?.Invoke(this);
            var targets = LiveTargets();
            if (targets.Length == 0)
                return;
            var properties = MaterialEditor.GetMaterialProperties(targets);
            using (MixedCommit.Quiet())
            {
                foreach (var row in rows)
                {
                    var property = Find(properties, row.item);
                    if (property != null)
                        row.pull?.Invoke(property);
                    row.MarkImguiDirty();
                }
                RefreshFooterCore();
            }
            UpdateDerived(properties);
            UpdateEnabled();
            signature = Signature();
            RefreshCount++;
        }

        static MaterialProperty Find(MaterialProperty[] properties, ShaderLayout.Item item)
        {
            if (properties == null)
                return null;
            if (item.index < properties.Length && properties[item.index] != null && properties[item.index].name == item.name)
                return properties[item.index];
            return FindByName(properties, item.name);
        }

        static MaterialProperty FindByName(MaterialProperty[] properties, string name)
        {
            if (properties != null)
                foreach (var property in properties)
                    if (property != null && property.name == name)
                        return property;
            return null;
        }

        public MaterialProperty HostProperty(string propertyName)
        {
            if (rowsByName.TryGetValue(propertyName, out var row))
                return Find(hostProperties, row.item);
            return FindByName(hostProperties, propertyName);
        }

        public void Write(string propertyName, Action<MaterialProperty> apply, VisualElement source)
        {
            if (failed || Editor == null)
                return;
            var targets = LiveTargets();
            if (targets.Length == 0 || targets.Length != Editor.targets.Length)
                return;
            var host = HostProperty(propertyName);
            var target = host;
            if (host == null || IsBehind(host, targets))
            {
                target = MaterialEditor.GetMaterialProperty(targets, propertyName);
                if (target == null)
                    return;
                if (host != null)
                    target.applyPropertyCallback = host.applyPropertyCallback;
            }
            apply(target);
            if (source != null)
                UndoGrouping.Collapse(source);
            if (rowsByName.TryGetValue(propertyName, out var row))
            {
                using (MixedCommit.Quiet())
                    row.pull?.Invoke(target);
                UpdateChanged(row, target);
                if (row.headerToggle && sectionOf.TryGetValue(row, out var section))
                    UpdateSwitch(section, target);
            }
            foreach (var other in rows)
                other.MarkImguiDirty();
            if (layout.Controllers.Contains(propertyName))
            {
                controllers[propertyName] = target;
                ApplyVisibility();
            }
            signature = Signature();
            WriteCount++;
        }

        /// <summary>True when the host's object no longer holds the material's value (an undo or a script ran since its OnGUI).</summary>
        static bool IsBehind(MaterialProperty host, Object[] targets)
        {
            var fresh = MaterialEditor.GetMaterialProperty(targets, host.name);
            if (fresh == null || fresh.hasMixedValue != host.hasMixedValue)
                return true;
            switch (host.type)
            {
                case MaterialProperty.PropType.Float:
                case MaterialProperty.PropType.Range: return !fresh.floatValue.Equals(host.floatValue);
                case MaterialProperty.PropType.Int: return fresh.intValue != host.intValue;
                case MaterialProperty.PropType.Color: return fresh.colorValue != host.colorValue;
                case MaterialProperty.PropType.Vector: return fresh.vectorValue != host.vectorValue;
                case MaterialProperty.PropType.Texture: return fresh.textureValue != host.textureValue || fresh.textureScaleAndOffset != host.textureScaleAndOffset;
                default: return false;
            }
        }

        /// <summary>Changed marks, [ShowIf] visibility and the dimmed look of switched off sections, all from one read of the values.</summary>
        void UpdateDerived(MaterialProperty[] properties)
        {
            controllers.Clear();
            foreach (var name in layout.Controllers)
                controllers[name] = FindByName(properties, name);
            foreach (var row in rows)
                SetChanged(row, IsChanged(row, Find(properties, row.item)));
            foreach (var view in sections)
            {
                UpdateSectionMark(view);
                if (view.toggleRow != null)
                    UpdateSwitch(view, Find(properties, view.toggleRow.item));
            }
            ApplyVisibility();
        }

        void UpdateChanged(ShaderPropertyRow row, MaterialProperty property)
        {
            if (SetChanged(row, IsChanged(row, property)) && sectionOf.TryGetValue(row, out var view))
                UpdateSectionMark(view);
        }

        static bool SetChanged(ShaderPropertyRow row, bool changed)
        {
            if (row.changed == changed)
                return false;
            row.changed = changed;
            if (row.mark != null)
                SetShown(row.mark, changed);
            return true;
        }

        /// <summary>
        /// Differs from the shader's default on at least one of the selected materials. A [PerRendererData] or importer set
        /// texture is not the material's own value and never counts.
        /// </summary>
        static bool IsChanged(ShaderPropertyRow row, MaterialProperty property)
        {
            if (property == null || row.alwaysReadOnly)
                return false;
            if (property.hasMixedValue)
                return true;
            if (row.type == ShaderPropertyType.Texture && row.scaleOffset)
            {
                ShaderPropertyFields.MixedScaleOffset(property, out bool scale, out bool offset);
                if (scale || offset)
                    return true;
            }
            return !ShaderValue.Read(property).Matches(row.defaultValue, row.scaleOffset);
        }

        static void UpdateSectionMark(SectionView view)
        {
            bool changed = false;
            foreach (var row in view.AllRows)
                changed |= row.changed;
            SetShown(view.mark, changed);
        }

        /// <summary>A section whose header switch is off dims its rows; they stay editable.</summary>
        static void UpdateSwitch(SectionView view, MaterialProperty property)
        {
            bool off = property != null && !property.hasMixedValue && !ShaderPropertyFields.IsOn(property);
            view.foldout.EnableInClassList(SectionOffClass, off);
            foreach (var row in view.rows)
                row.SetDimmed(off);
        }

        bool Met(ShaderLayout.Condition condition)
        {
            if (condition == null)
                return true;
            controllers.TryGetValue(condition.property, out var controller);
            return ShaderLayout.ConditionMet(condition, controller);
        }

        bool Visible(ShaderLayout.Item item) => Met(item.showIf);

        /// <summary>What shows: [ShowIf] first, then the search, which only finds properties that show.</summary>
        void ApplyVisibility()
        {
            ShaderLayout.ParseSearch(search.value, out bool sectionMode, out string query);
            searching = query.Length > 0;

            SetShown(topBoxes, !searching);
            SetShown(bottomBoxes, !searching);
            foreach (var row in looseRows)
                SetShown(row.root, Visible(row.item) && (!searching || (!sectionMode && ShaderLayout.ItemMatches(row.item, query))));

            foreach (var view in sections)
            {
                bool shown = Met(view.section.showIf);
                if (shown && searching)
                    shown = sectionMode ? ShaderLayout.SectionMatches(view.section, query) : view.section.items.Any(i => Visible(i) && ShaderLayout.ItemMatches(i, query));
                SetShown(view.group, shown);
                SetShown(view.afterBoxes, !searching);
                if (view.toggleRow != null)
                    SetShown(view.toggleRow.root, Visible(view.toggleRow.item));
                foreach (var row in view.rows)
                    SetShown(row.root, Visible(row.item) && (!searching || sectionMode || ShaderLayout.ItemMatches(row.item, query)));
            }
        }

        public float ValueColumnX
        {
            get
            {
                var input = renderQueue?.Q(className: BaseField<string>.inputUssClassName);
                if (input == null || input.panel == null)
                    return float.NaN;
                float x = input.worldBound.x;
                return x > 0f ? x : float.NaN;
            }
        }

        /// <summary>A Shader UI text in the inspector's language; the English stands in the code.</summary>
        public string Ui(string key, string english) => texts.Ui(key, english);

        string UiFormat(string key, string english, params object[] args) => texts.UiFormat(key, english, args);

        /// <summary>A link label or link box title from the config, in the inspector's language.</summary>
        string LinkText(string text) => string.IsNullOrEmpty(text) ? text : texts.Text("link." + text, text);

        static FontAsset japaneseFont;
        static readonly string[] JapaneseFamilies = { "Yu Gothic UI", "Meiryo UI", "Noto Sans CJK JP", "Hiragino Sans" };

        /// <summary>Japanese in a Japanese font asset built from the system fonts the way NDMF does: outside Japan, Windows otherwise draws the same characters with Chinese glyph shapes, and a legacy dynamic font measures every label as empty.</summary>
        void ApplyFont()
        {
            if (ShaderUiLanguage.BaseOf(texts.Language) != "ja")
                return;
            if (japaneseFont == null)
                japaneseFont = LoadJapaneseFont();
            if (japaneseFont != null)
                style.unityFontDefinition = new StyleFontDefinition(japaneseFont);
        }

        static FontAsset LoadJapaneseFont()
        {
            FontAsset main = null;
            foreach (var family in JapaneseFamilies)
            {
                var loaded = FontAsset.CreateFontAsset(family, "");
                if (loaded == null)
                    continue;
                loaded.hideFlags = HideFlags.HideAndDontSave;
                if (main == null)
                {
                    main = loaded;
                    main.fallbackFontAssetTable = new List<FontAsset>();
                }
                else
                    main.fallbackFontAssetTable.Add(loaded);
            }
            return main;
        }

        /// <summary>The Language switch, labelled in English in every language so anyone can find it; hidden while only English exists.</summary>
        void BuildLanguage()
        {
            var codes = texts.Languages;
            if (codes.Count < 2)
                return;
            var names = codes.Select(ShaderUiLanguage.NativeName).ToList();
            languagePicker = new DropdownField("Language", names, IndexOfCode(codes, texts.Language))
            {
                name = LanguageName,
                tooltip = Ui("ui.language.tooltip", "The language of this inspector. Modular Avatar and other tools that use NDMF share this choice."),
            };
            languagePicker.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            languagePicker.RegisterValueChangedCallback(_ =>
            {
                int i = languagePicker.index;
                if (i < 0 || i >= codes.Count)
                    return;
                ShaderUiLanguage.Set(codes[i]);
                ShaderInspectorMount.Defer(Check);
            });
            Add(languagePicker);
            if (texts.Reviewed)
                return;
            var note = new Label(Ui("ui.language.review", "This translation has not been checked by a native speaker yet.")) { name = ReviewNoteName };
            note.AddToClassList(ReviewNoteClass);
            Add(note);
        }

        /// <summary>The entry of the language in use: the chosen one, else one of the same base language, else English.</summary>
        internal static int IndexOfCode(IReadOnlyList<string> codes, string language)
        {
            for (int i = 0; i < codes.Count; i++)
                if (codes[i] == language)
                    return i;
            string baseLanguage = ShaderUiLanguage.BaseOf(language);
            for (int i = 0; i < codes.Count; i++)
                if (ShaderUiLanguage.BaseOf(codes[i]) == baseLanguage)
                    return i;
            return 0;
        }

        void BuildBanner()
        {
            if (config == null)
                return;
            bool hasLinks = config.bannerLinks != null && config.bannerLinks.Count > 0;
            if (config.banner == null && !hasLinks)
                return;

            if (config.banner != null)
            {
                var banner = new Image { name = "docme-si-banner", image = config.banner };
                banner.AddToClassList(BannerClass);
                var texture = config.banner;
                switch (config.bannerFit)
                {
                    case SimpleShaderConfig.BannerFit.Stretch:
                        banner.scaleMode = ScaleMode.StretchToFill;
                        banner.style.height = config.bannerHeight;
                        break;
                    case SimpleShaderConfig.BannerFit.Crop:
                        banner.scaleMode = ScaleMode.ScaleAndCrop;
                        banner.style.height = config.bannerHeight;
                        break;
                    default:
                        banner.scaleMode = ScaleMode.ScaleToFit;
                        float aspect = texture.height > 0 ? texture.width / (float)texture.height : 1f;
                        banner.RegisterCallback<GeometryChangedEvent>(e =>
                        {
                            float height = Mathf.Round(e.newRect.width / Mathf.Max(0.01f, aspect));
                            if (Mathf.Abs(banner.resolvedStyle.height - height) > 0.5f)
                                banner.style.height = height;
                        });
                        break;
                }
                if (!string.IsNullOrEmpty(config.bannerUrl))
                {
                    banner.tooltip = config.bannerUrl;
                    MakeLink(banner, config.bannerUrl);
                }
                Add(banner);
            }

            if (hasLinks)
            {
                var iconSize = config.bannerIconSize;
                if (iconSize.x < 1f || iconSize.y < 1f)
                    iconSize = new Vector2(16f, 16f);
                var strip = new VisualElement { name = "docme-si-banner-links" };
                strip.AddToClassList(BannerLinksClass);
                bool first = true;
                foreach (var link in config.bannerLinks)
                {
                    if (link == null)
                        continue;
                    if (!first)
                    {
                        var separator = new Label("|");
                        separator.AddToClassList(LinkSeparatorClass);
                        strip.Add(separator);
                    }
                    first = false;
                    var icon = ShaderUiServices.IconOf(link);
                    string text = string.IsNullOrEmpty(link.label) && icon == null ? link.url : LinkText(link.label);
                    strip.Add(LinkElement(icon, text, link.url, iconSize));
                }
                Add(strip);
            }
        }

        VisualElement LinkElement(Texture icon, string text, string url, Vector2 iconSize)
        {
            var item = new VisualElement();
            item.AddToClassList(LinkClass);
            item.tooltip = string.IsNullOrEmpty(url) ? text ?? "" : url;
            if (icon != null)
            {
                var image = new Image { image = icon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                image.AddToClassList(LinkIconClass);
                image.style.width = iconSize.x;
                image.style.height = iconSize.y;
                item.Add(image);
            }
            if (!string.IsNullOrEmpty(text))
            {
                var label = new Label(text) { pickingMode = PickingMode.Ignore };
                label.AddToClassList(LinkTextClass);
                item.Add(label);
            }
            MakeLink(item, url);
            return item;
        }

        static void MakeLink(VisualElement element, string url)
        {
            element.userData = url;
            if (string.IsNullOrEmpty(url))
                return;
            element.AddToClassList(ClickableClass);
            element.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0)
                    return;
                ShaderUiServices.OpenUrl(url);
                e.StopPropagation();
                e.PreventDefault();
            });
            element.RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
            element.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            element.RegisterCallback<ClickEvent>(e => e.StopPropagation());
        }

        void BuildSearch()
        {
            var row = new VisualElement { name = "docme-si-search-row" };
            row.AddToClassList(SearchRowClass);

            var iconTexture = ShaderUiServices.ResolveBuiltin(EditorGUIUtility.isProSkin ? "d_Search Icon" : "Search Icon")
                ?? ShaderUiServices.ResolveBuiltin("Search Icon");
            if (iconTexture != null)
            {
                var icon = new Image { image = iconTexture, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                icon.AddToClassList(SearchIconClass);
                row.Add(icon);
            }

            search = new TextField { name = SearchName, tooltip = Ui("ui.search.tooltip", "Shows only the properties whose name contains this text. Start with s: to search section names instead.") };
            search.RegisterValueChangedCallback(e => ApplySearch(e.newValue, remember: true));
            row.Add(search);

            placeholder = new Label(Ui("ui.search.placeholder", "Search properties, or s: for sections")) { pickingMode = PickingMode.Ignore };
            placeholder.AddToClassList(PlaceholderClass);
            var input = search.Q(className: TextField.inputUssClassName);
            (input ?? search).Add(placeholder);
            search.RegisterCallback<FocusInEvent>(_ => UpdatePlaceholder(true));
            search.RegisterCallback<FocusOutEvent>(_ => UpdatePlaceholder(false));

            clear = new Button(() => search.value = "") { name = ClearName, text = "✕", tooltip = Ui("ui.search.clear", "Clears the search.") };
            row.Add(clear);
            Add(row);
        }

        void UpdatePlaceholder(bool focused)
        {
            bool show = !focused && string.IsNullOrEmpty(search.value);
            placeholder.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void BuildBody()
        {
            var top = new List<SimpleShaderConfig.LinkGroup>();
            var bottom = new List<SimpleShaderConfig.LinkGroup>();
            var after = new Dictionary<string, List<SimpleShaderConfig.LinkGroup>>();
            ShaderLayout.BucketLinkBoxes(config, layout.Sections, top, bottom, after);

            topBoxes = new VisualElement { name = "docme-si-top-links" };
            foreach (var box in top)
                topBoxes.Add(BuildLinkBox(box));
            Add(topBoxes);

            loose = new VisualElement { name = "docme-si-loose" };
            foreach (var item in layout.Ungrouped)
            {
                var row = AddRow(item);
                looseRows.Add(row);
                loose.Add(row.root);
            }
            Add(loose);

            foreach (var section in layout.Sections)
            {
                var view = new SectionView { section = section, key = layout.FoldKey(section.name) };
                view.group = new VisualElement { name = "group-" + section.name };
                view.group.AddToClassList(SectionGroupClass);
                if (!string.IsNullOrEmpty(section.category))
                {
                    var category = new Label(section.categoryTitle) { name = "category-" + section.name };
                    category.AddToClassList(CategoryClass);
                    view.group.Add(category);
                }

                view.foldout = new Foldout { text = section.title, name = "section-" + section.name };
                view.foldout.AddToClassList(SectionClass);
                view.foldout.SetValueWithoutNotify(ShaderUiServices.Prefs.GetBool(view.key, !section.startClosed));
                MarkOpen(view.foldout);
                view.header = view.foldout.Q(className: Foldout.toggleUssClassName);
                if (view.header != null && !string.IsNullOrEmpty(section.help))
                    view.header.tooltip = section.help;
                if (view.header != null)
                    sectionsByHeader[view.header] = view;
                if (section.toggle != null)
                {
                    var toggleRow = ShaderPropertyFields.CreateHeaderToggle(this, shader, section.toggle, section.toggleMode, section.toggleKeyword);
                    rows.Add(toggleRow);
                    rowsByName[section.toggle.name] = toggleRow;
                    sectionOf[toggleRow] = view;
                    view.toggleRow = toggleRow;
                    AddHeaderToggle(view.foldout, toggleRow.root);
                }
                AddHeaderLinks(view.foldout, ShaderLayout.FindSectionLinks(config, section.name));
                view.mark = NewMark("changed-section-" + section.name);
                view.foldout.hierarchy.Add(view.mark);
                var captured = view;
                view.foldout.RegisterValueChangedCallback(e =>
                {
                    if (e.target != captured.foldout)
                        return;
                    if (searching)
                    {
                        captured.foldout.SetValueWithoutNotify(true);
                        MarkOpen(captured.foldout);
                        return;
                    }
                    ShaderUiServices.Prefs.SetBool(captured.key, e.newValue);
                    MarkOpen(captured.foldout);
                });

                foreach (var item in section.items)
                {
                    if (item == section.toggle)
                        continue;
                    var row = AddRow(item);
                    view.rows.Add(row);
                    sectionOf[row] = view;
                    view.foldout.Add(row.root);
                }
                view.group.Add(view.foldout);
                Add(view.group);

                view.afterBoxes = new VisualElement { name = "after-" + section.name };
                if (after.TryGetValue(section.name, out var boxes))
                    foreach (var box in boxes)
                        view.afterBoxes.Add(BuildLinkBox(box));
                Add(view.afterBoxes);
                sections.Add(view);
            }

            bottomBoxes = new VisualElement { name = "docme-si-bottom-links" };
            foreach (var box in bottom)
                bottomBoxes.Add(BuildLinkBox(box));
            Add(bottomBoxes);
        }

        ShaderPropertyRow AddRow(ShaderLayout.Item item)
        {
            var row = ShaderPropertyFields.Create(this, shader, item);
            row.mark = NewMark("changed-" + item.name);
            row.root.Add(row.mark);
            rows.Add(row);
            rowsByName[item.name] = row;
            rowsByRoot[row.root] = row;
            return row;
        }

        /// <summary>The small accent bar in the inspector's left margin that shows a value differs from the shader's default.</summary>
        VisualElement NewMark(string name)
        {
            var mark = new VisualElement { name = name, tooltip = Ui("ui.changed", "Changed from the shader's default") };
            mark.AddToClassList(ChangedClass);
            mark.style.display = DisplayStyle.None;
            return mark;
        }

        /// <summary>
        /// Puts the [SectionToggle] checkbox between the fold arrow and the title, so that a left press on it never reaches
        /// the fold. The fold's own toggle is a composite root and so gets the press at the target phase, before anything
        /// bubbles up: while the checkbox is disabled (it then runs no callbacks of its own) the holder stops the press on
        /// its way down, and a press on the holder around the checkbox stops there.
        /// </summary>
        static void AddHeaderToggle(Foldout foldout, VisualElement toggle)
        {
            var input = foldout.Q(className: Foldout.inputUssClassName);
            if (input == null)
                return;
            var holder = new VisualElement { name = toggle.name + "-holder" };
            holder.AddToClassList(HeaderToggleClass);
            holder.Add(toggle);
            void StopWhileDisabled(EventBase e, int button)
            {
                if (button == 0 && !toggle.enabledInHierarchy)
                    e.StopPropagation();
            }
            holder.RegisterCallback<PointerDownEvent>(e => StopWhileDisabled(e, e.button), TrickleDown.TrickleDown);
            holder.RegisterCallback<PointerUpEvent>(e => StopWhileDisabled(e, e.button), TrickleDown.TrickleDown);
            holder.RegisterCallback<MouseDownEvent>(e => StopWhileDisabled(e, e.button), TrickleDown.TrickleDown);
            holder.RegisterCallback<MouseUpEvent>(e => StopWhileDisabled(e, e.button), TrickleDown.TrickleDown);
            holder.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) e.StopPropagation(); });
            holder.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0) e.StopPropagation(); });
            holder.RegisterCallback<MouseDownEvent>(e => { if (e.button == 0) e.StopPropagation(); });
            holder.RegisterCallback<MouseUpEvent>(e => { if (e.button == 0) e.StopPropagation(); });
            holder.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            var title = input.Q(className: Foldout.textUssClassName);
            int index = title != null ? input.IndexOf(title) : input.childCount;
            input.Insert(Mathf.Max(0, index), holder);
        }

        static void MarkOpen(Foldout foldout) => foldout.EnableInClassList(SectionOpenClass, foldout.value);

        void AddHeaderLinks(Foldout foldout, List<SimpleShaderConfig.SectionLink> links)
        {
            if (links == null)
                return;
            var input = foldout.Q(className: Foldout.inputUssClassName);
            if (input == null)
                return;
            var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            spacer.AddToClassList(HeaderSpacerClass);
            input.Add(spacer);
            // The classic inspector puts the first link at the far right.
            for (int i = links.Count - 1; i >= 0; i--)
            {
                var link = links[i];
                if (link == null || string.IsNullOrEmpty(link.url))
                    continue;
                var icon = new Image { image = ShaderUiServices.IconOf(link), scaleMode = ScaleMode.ScaleToFit, tooltip = link.url };
                icon.AddToClassList(HeaderLinkClass);
                MakeLink(icon, link.url);
                input.Add(icon);
            }
        }

        VisualElement BuildLinkBox(SimpleShaderConfig.LinkGroup box)
        {
            string title = ShaderLayout.LinkBoxTitle(box);
            string key = layout.LinkBoxKey(title);
            var foldout = new Foldout { text = string.IsNullOrEmpty(box.title) ? Ui("ui.links", "Links") : LinkText(box.title), name = "linkbox-" + title };
            foldout.AddToClassList(SectionClass);
            foldout.AddToClassList(LinkBoxClass);
            foldout.SetValueWithoutNotify(ShaderUiServices.Prefs.GetBool(key, !box.startCollapsed));
            MarkOpen(foldout);
            foldout.RegisterValueChangedCallback(e =>
            {
                if (e.target != foldout)
                    return;
                ShaderUiServices.Prefs.SetBool(key, e.newValue);
                MarkOpen(foldout);
            });
            foreach (var link in box.links)
            {
                if (link == null)
                    continue;
                string label = string.IsNullOrEmpty(link.label) ? link.url : LinkText(link.label);
                var row = LinkElement(ShaderUiServices.IconOf(link), label, link.url, new Vector2(14f, 14f));
                row.AddToClassList(LinkRowClass);
                foldout.Add(row);
            }
            linkBoxes.Add((foldout, key, box.startCollapsed));
            return foldout;
        }

        static bool OnMac => Application.platform == RuntimePlatform.OSXEditor;

        void OnMenuMouseDown(MouseDownEvent e)
        {
            if (OnMac && (e.button == (int)MouseButton.RightMouse || (e.button == (int)MouseButton.LeftMouse && e.modifiers == EventModifiers.Control)))
                OpenMenu(e);
        }

        void OnMenuMouseUp(MouseUpEvent e)
        {
            if (!OnMac && e.button == (int)MouseButton.RightMouse)
                OpenMenu(e);
        }

        /// <summary>Only a row or a section header opens a menu, so a right click anywhere else never shows an empty one.</summary>
        void OpenMenu(EventBase e)
        {
            if (failed || !MenuOwner(e.target as VisualElement, out _, out _))
                return;
            var manager = panel?.contextualMenuManager;
            if (manager == null)
                return;
            manager.DisplayMenu(e, this);
            e.StopPropagation();
            e.PreventDefault();
        }

        /// <summary>The row or section header under a right click. Nothing inside an IMGUI slice: a custom drawer shows Unity's own menu there.</summary>
        bool MenuOwner(VisualElement origin, out ShaderPropertyRow row, out SectionView section)
        {
            row = null;
            section = null;
            for (var e = origin; e != null && e != this; e = e.parent)
            {
                if (e is IMGUIContainer)
                    return false;
                if (rowsByRoot.TryGetValue(e, out row))
                    return true;
                if (sectionsByHeader.TryGetValue(e, out section))
                    return true;
            }
            return false;
        }

        void BuildContextMenu(ContextualMenuPopulateEvent evt)
        {
            // A text box's own Cut, Copy and Paste menu passes by on its way up: it keeps to itself.
            if (evt.target != this || failed)
                return;
            if (!MenuOwner(evt.triggerEvent?.target as VisualElement, out var row, out var section))
                return;
            if (row != null)
                AddRowItems(evt.menu, row);
            else
                AddSectionItems(evt.menu, section);
        }

        static DropdownMenuAction.Status When(bool enabled) => enabled ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;

        void AddRowItems(DropdownMenu menu, ShaderPropertyRow row)
        {
            bool writable = Writable(row);
            menu.AppendAction(Ui("ui.menu.copy", "Copy"), _ => CopyRow(row), When(TryReadCurrent(row, out _)));
            menu.AppendAction(Ui("ui.menu.paste", "Paste"), _ => PasteRow(row), When(writable && TryReadPaste(row, out _)));
            menu.AppendAction(Ui("ui.menu.reset", "Reset to default"), _ => ResetRow(row), When(writable));
        }

        void AddSectionItems(DropdownMenu menu, SectionView view)
        {
            bool canWrite = !failed && IsEditable();
            menu.AppendAction(Ui("ui.menu.copySection", "Copy section"), _ => CopySection(view), DropdownMenuAction.Status.Normal);
            menu.AppendAction(Ui("ui.menu.pasteSection", "Paste section"), _ => PasteSection(view), When(canWrite && SectionPaste(view).Count > 0));
            menu.AppendAction(Ui("ui.menu.resetSection", "Reset section to defaults"), _ => ResetSection(view), When(canWrite));
            menu.AppendSeparator();
            menu.AppendAction(Ui("ui.menu.expandAll", "Expand all sections"), _ => SetAllSections(true), DropdownMenuAction.Status.Normal);
            menu.AppendAction(Ui("ui.menu.collapseAll", "Collapse all sections"), _ => SetAllSections(false), DropdownMenuAction.Status.Normal);
        }

        /// <summary>The property's value on the selected materials; false while they differ, since there is no one value to copy.</summary>
        bool TryReadCurrent(ShaderPropertyRow row, out ShaderValue value)
        {
            value = default;
            var targets = LiveTargets();
            if (targets.Length == 0)
                return false;
            var property = MaterialEditor.GetMaterialProperty(targets, row.item.name);
            if (property == null || property.hasMixedValue)
                return false;
            if (row.type == ShaderPropertyType.Texture)
            {
                ShaderPropertyFields.MixedScaleOffset(property, out bool scale, out bool offset);
                if (scale || offset)
                    return false;
            }
            value = ShaderValue.Read(property);
            return true;
        }

        /// <summary>The clipboard's value for this row: its only property, or the one with this row's name, if the type fits.</summary>
        bool TryReadPaste(ShaderPropertyRow row, out ShaderValue value)
        {
            value = default;
            var data = ShaderClipboard.Read(ShaderUiServices.Clipboard.Text);
            if (data == null || data.properties.Count == 0)
                return false;
            var entry = data.properties.Count == 1 ? data.properties[0] : data.properties.FirstOrDefault(p => p != null && p.name == row.item.name);
            return ShaderClipboard.TryRead(entry, out value) && Fits(row, value);
        }

        static bool Fits(ShaderPropertyRow row, ShaderValue value)
            => ShaderValue.SameKind(value.type, row.type) && (row.accepts == null || row.accepts(value));

        void CopyRow(ShaderPropertyRow row)
        {
            if (TryReadCurrent(row, out var value))
                ShaderUiServices.Clipboard.Text = ShaderClipboard.Write(shader.name, new[] { (row.item.name, value) });
        }

        void PasteRow(ShaderPropertyRow row)
        {
            if (Writable(row) && TryReadPaste(row, out var value))
                WriteAsOneStep(UiFormat("ui.undo.paste", "Paste {0}", row.item.displayName), new List<(ShaderPropertyRow, ShaderValue)> { (row, value) }, reset: false);
        }

        void ResetRow(ShaderPropertyRow row)
        {
            if (Writable(row))
                WriteAsOneStep(UiFormat("ui.undo.reset", "Reset {0}", row.item.displayName), new List<(ShaderPropertyRow, ShaderValue)> { (row, row.defaultValue) }, reset: true);
        }

        void CopySection(SectionView view)
        {
            var entries = new List<(string, ShaderValue)>();
            foreach (var row in view.AllRows)
                if (TryReadCurrent(row, out var value))
                    entries.Add((row.item.name, value));
            ShaderUiServices.Clipboard.Text = ShaderClipboard.Write(shader.name, entries);
        }

        /// <summary>Every property the clipboard holds that this section has, matched by name and type; the rest is skipped.</summary>
        List<(ShaderPropertyRow row, ShaderValue value)> SectionPaste(SectionView view)
        {
            var writes = new List<(ShaderPropertyRow, ShaderValue)>();
            var data = ShaderClipboard.Read(ShaderUiServices.Clipboard.Text);
            if (data == null)
                return writes;
            foreach (var entry in data.properties)
            {
                if (entry == null)
                    continue;
                var row = view.AllRows.FirstOrDefault(r => r.item.name == entry.name);
                if (row != null && ShaderClipboard.TryRead(entry, out var value) && Fits(row, value) && Writable(row))
                    writes.Add((row, value));
            }
            return writes;
        }

        void PasteSection(SectionView view)
        {
            if (!failed && IsEditable())
                WriteAsOneStep(UiFormat("ui.undo.pasteSection", "Paste Section {0}", view.section.title), SectionPaste(view), reset: false);
        }

        void ResetSection(SectionView view)
        {
            if (failed || !IsEditable())
                return;
            var writes = view.AllRows.Where(Writable).Select(r => (r, r.defaultValue)).ToList();
            WriteAsOneStep(UiFormat("ui.undo.resetSection", "Reset Section {0}", view.section.title), writes, reset: true);
        }

        /// <summary>
        /// Writes through the same material properties as typing (keywords, every selected material, Animation window
        /// recording), as one undo step under the action's name.
        /// </summary>
        void WriteAsOneStep(string undoName, List<(ShaderPropertyRow row, ShaderValue value)> writes, bool reset)
        {
            var targets = LiveTargets();
            if (writes.Count == 0 || targets.Length == 0 || targets.Length != Editor.targets.Length)
                return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.RecordObjects(targets, undoName);
            bool customDrawer = false;
            foreach (var (row, value) in writes)
            {
                customDrawer |= row.bridge != null;
                Write(row.item.name, p =>
                {
                    row.assign(p, value);
                    if (reset && row.type == ShaderPropertyType.Texture)
                        p.textureScaleAndOffset = ShaderValue.DefaultScaleOffset;
                }, null);
            }
            // A custom drawer's keywords follow its value through its own Apply, as after an undo.
            if (customDrawer)
                MaterialEditor.ApplyMaterialPropertyDrawers(targets);
            Undo.SetCurrentGroupName(undoName);
            Undo.CollapseUndoOperations(group);
        }

        void SetAllSections(bool open)
        {
            foreach (var view in sections)
            {
                ShaderUiServices.Prefs.SetBool(view.key, open);
                if (searching)
                    continue;
                view.foldout.SetValueWithoutNotify(open);
                MarkOpen(view.foldout);
            }
        }

        void BuildFooter()
        {
            var footer = new VisualElement { name = "docme-si-footer" };
            footer.AddToClassList(FooterClass);

            queueNames = (string[])QueueNames.Clone();
            queueNames[0] = Ui("ui.queue.fromShader", "From Shader");
            renderQueue = new MixedDropdownField(Ui("ui.queue", "Render Queue"), new List<string>(queueNames), 0)
            {
                name = RenderQueueName,
                tooltip = Ui("ui.queue.tooltip", "The order this material draws in. From Shader uses the shader's own queue."),
            };
            renderQueue.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            renderQueue.RegisterValueChangedCallback(_ =>
            {
                int i = renderQueue.index;
                if (i >= 0 && i < queueValues.Count)
                    SetRenderQueue(queueValues[i]);
            });
            renderQueueValue = new MixedIntegerField { name = RenderQueueValueName, isDelayed = true, tooltip = Ui("ui.queue.value", "The exact render queue number.") };
            renderQueueValue.AddToClassList(QueueValueClass);
            renderQueueValue.RegisterValueChangedCallback(e => SetRenderQueue(e.newValue));
            renderQueue.Add(renderQueueValue);
            footer.Add(renderQueue);

            instancing = new MixedToggle(Ui("ui.instancing", "Enable GPU Instancing"))
            {
                name = InstancingName,
                tooltip = Ui("ui.instancing.tooltip", "Lets Unity draw many copies of this material in one go."),
            };
            instancing.AddToClassList(BaseField<bool>.alignedFieldUssClassName);
            instancing.RegisterValueChangedCallback(e => SetMaterialFlag(instancing.label, m => m.enableInstancing = e.newValue));
            instancing.style.display = HasInstancing(shader) ? DisplayStyle.Flex : DisplayStyle.None;
            footer.Add(instancing);

            doubleSidedGI = new MixedToggle(Ui("ui.gi", "Double Sided Global Illumination"))
            {
                name = DoubleSidedGIName,
                tooltip = Ui("ui.gi.tooltip", "Lets baked lighting treat both sides of the mesh as lit surfaces."),
            };
            doubleSidedGI.AddToClassList(BaseField<bool>.alignedFieldUssClassName);
            doubleSidedGI.RegisterValueChangedCallback(e => SetMaterialFlag(doubleSidedGI.label, m => m.doubleSidedGI = e.newValue));
            footer.Add(doubleSidedGI);

            classic = new Toggle(Ui("ui.classic", "Classic inspector"))
            {
                name = ClassicName,
                tooltip = Ui("ui.classic.tooltip", "Draws every DocMe shader material with the older inspector. Turn it off there to come back to this one."),
            };
            classic.AddToClassList(BaseField<bool>.alignedFieldUssClassName);
            classic.RegisterValueChangedCallback(e =>
            {
                ShaderUiServices.Classic = e.newValue;
                if (e.newValue)
                    ShaderInspectorMount.Defer(Check);
            });
            footer.Add(classic);
            Add(footer);
        }

        void RefreshFooter()
        {
            if (failed)
                return;
            try
            {
                RefreshFooterCore();
            }
            catch (Exception e) when (!ShaderPropertyFields.IsExitGui(e))
            {
                Fail(e);
            }
        }

        void RefreshFooterCore()
        {
            using var quiet = MixedCommit.Quiet();
            var materials = LiveMaterials();
            if (materials.Length == 0)
                return;
            var first = materials[0];
            int raw = RawRenderQueue(first);
            bool mixedQueue = materials.Any(m => RawRenderQueue(m) != raw);

            var names = new List<string>(queueNames);
            queueValues.Clear();
            queueValues.AddRange(QueueValues);
            int index = Array.IndexOf(QueueValues, raw);
            if (index < 0)
            {
                names.Add(CustomQueueName(raw));
                queueValues.Add(raw);
                index = names.Count - 1;
            }
            renderQueue.choices = names;
            ((MixedDropdownField)renderQueue).SetShown(names[index], mixedQueue);
            renderQueueValue.SetValueWithoutNotify(first.renderQueue);
            renderQueueValue.showMixedValue = mixedQueue;

            instancing.SetValueWithoutNotify(first.enableInstancing);
            instancing.showMixedValue = materials.Any(m => m.enableInstancing != first.enableInstancing);

            bool enlighten = IsEnlighten();
            enlightenOn = enlighten;
            doubleSidedGI.SetEnabled(!enlighten && editable);
            doubleSidedGI.SetValueWithoutNotify(!enlighten && first.doubleSidedGI);
            doubleSidedGI.showMixedValue = !enlighten && materials.Any(m => m.doubleSidedGI != first.doubleSidedGI);

            classic.SetValueWithoutNotify(ShaderUiServices.Classic);
        }

        static string CustomQueueName(int raw)
        {
            int best = 1, bestDiff = int.MaxValue;
            for (int i = 1; i < QueueValues.Length; i++)
            {
                int diff = Mathf.Abs(QueueValues[i] - raw);
                if (diff < bestDiff)
                {
                    best = i;
                    bestDiff = diff;
                }
            }
            int overflow = raw - QueueValues[best];
            return string.Format(overflow > 0 ? "{0}+{1}" : "{0}{1}", QueueNames[best], overflow);
        }

        void SetRenderQueue(int queue)
        {
            var targets = LiveTargets();
            if (failed || targets.Length == 0)
                return;
            queue = Mathf.Clamp(queue, -1, 5000);
            Undo.RecordObjects(targets, UiFormat("ui.undo.queue", "Modify Render Queue of {0}", TargetTitle(targets)));
            foreach (var target in targets)
                if (target is Material material)
                    material.renderQueue = queue;
            Undo.FlushUndoRecordObjects();
            RefreshFooter();
            signature = Signature();
            WriteCount++;
        }

        void SetMaterialFlag(string label, Action<Material> apply)
        {
            var targets = LiveTargets();
            if (failed || targets.Length == 0)
                return;
            Undo.RecordObjects(targets, UiFormat("ui.undo.flag", "Modify {0} of {1}", label, TargetTitle(targets)));
            foreach (var target in targets)
                if (target is Material material)
                    apply(material);
            Undo.FlushUndoRecordObjects();
            RefreshFooter();
            signature = Signature();
            WriteCount++;
        }

        string TargetTitle(Object[] targets)
            => targets.Length == 1 ? targets[0].name : UiFormat("ui.materials", "{0} Materials", targets.Length);

        public void ApplySearch(string text) => ApplySearch(text, remember: true);

        void ApplySearch(string text, bool remember)
        {
            text ??= "";
            if (search.value != text)
                search.SetValueWithoutNotify(text);
            if (remember)
                ShaderInspectorMount.RememberSearch(Owner, text);
            clear.SetEnabled(text.Length > 0);
            var focused = search.focusController?.focusedElement as VisualElement;
            UpdatePlaceholder(focused != null && (focused == search || search.Contains(focused)));

            ShaderLayout.ParseSearch(text, out _, out string query);
            searching = query.Length > 0;
            foreach (var view in sections)
            {
                view.foldout.SetValueWithoutNotify(searching || ShaderUiServices.Prefs.GetBool(view.key, !view.section.startClosed));
                MarkOpen(view.foldout);
            }
            ApplyVisibility();
        }

        static void SetShown(VisualElement element, bool shown)
        {
            var display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display != display)
                element.style.display = display;
        }

        static bool IsEnlighten()
        {
#pragma warning disable 618
            LightingSettings settings = LightingFallback?.Invoke();
            if (settings == null && !Lightmapping.TryGetLightingSettings(out settings))
                return false;
            return settings != null && settings.lightmapper == LightingSettings.Lightmapper.Enlighten;
#pragma warning restore 618
        }

        /// <summary>The queue stored on the material, -1 when it follows the shader (Material.rawRenderQueue is internal).</summary>
        static int RawRenderQueue(Material material)
        {
            if (RawQueueGetter != null)
                return RawQueueGetter(material);
            return material.shader != null && material.renderQueue == material.shader.renderQueue ? -1 : material.renderQueue;
        }

        static Func<Material, int> BuildRawQueueGetter()
        {
            try
            {
                var getter = typeof(Material).GetProperty("rawRenderQueue", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetGetMethod(true);
                if (getter != null && getter.ReturnType == typeof(int))
                    return (Func<Material, int>)Delegate.CreateDelegate(typeof(Func<Material, int>), getter);
            }
            catch (Exception)
            {
            }
            return null;
        }

        static Func<Shader, bool> BuildHasInstancing()
        {
            try
            {
                var method = typeof(ShaderUtil).GetMethod("HasInstancing", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(Shader) }, null);
                if (method != null && method.ReturnType == typeof(bool))
                {
                    var call = (Func<Shader, bool>)Delegate.CreateDelegate(typeof(Func<Shader, bool>), method);
                    return s => s != null && call(s);
                }
            }
            catch (Exception)
            {
            }
            return _ => true;
        }

        static Func<LightingSettings> BuildLightingFallback()
        {
            try
            {
                var method = typeof(Lightmapping).GetMethod("GetLightingSettingsOrDefaultsFallback", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null);
                if (method != null && method.ReturnType == typeof(LightingSettings))
                    return (Func<LightingSettings>)Delegate.CreateDelegate(typeof(Func<LightingSettings>), method);
            }
            catch (Exception)
            {
            }
            return null;
        }
    }

    internal static class ShaderInspectorStyles
    {
        const string KnownPath = "Assets/DocMe/ShaderUI/Editor/ShaderInspector.uss";
        static StyleSheet sheet;
        static bool searched;

        public static StyleSheet Sheet
        {
            get
            {
                if (sheet != null || searched)
                    return sheet;
                searched = true;
                sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(KnownPath);
                if (sheet == null)
                {
                    foreach (var guid in AssetDatabase.FindAssets("ShaderInspector t:StyleSheet"))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        if (path.EndsWith("/ShaderInspector.uss", StringComparison.Ordinal))
                        {
                            sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                            break;
                        }
                    }
                }
                return sheet;
            }
        }

        public static string SheetPath => sheet != null ? AssetDatabase.GetAssetPath(sheet) : KnownPath;
    }
}
