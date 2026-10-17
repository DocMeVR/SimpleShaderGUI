using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace DocMe
{
    internal interface IShaderFieldHost
    {
        MaterialEditor Editor { get; }

        /// <summary>The MaterialProperty the host handed to its latest OnGUI, so writes keep Animation window recording and multi select.</summary>
        MaterialProperty HostProperty(string propertyName);

        void Write(string propertyName, Action<MaterialProperty> apply, VisualElement source);

        /// <summary>World x where the value column of aligned fields starts, or NaN before the first layout.</summary>
        float ValueColumnX { get; }

        /// <summary>A Shader UI text in the inspector's language; the English stands in the code.</summary>
        string Ui(string key, string english);
    }

    internal sealed class ShaderPropertyRow
    {
        public ShaderLayout.Item item;
        public string kind;
        public VisualElement root;
        public VisualElement field;
        public IMGUIContainer bridge;
        public readonly List<IMGUIContainer> decorators = new List<IMGUIContainer>();

        /// <summary>[PerRendererData] and non modifiable texture data: read only even on an editable material.</summary>
        public bool alwaysReadOnly;
        public Action<MaterialProperty> pull;

        public ShaderPropertyType type;

        /// <summary>Writes a value into the property the way the row's own field does, keywords included (Paste and Reset).</summary>
        public Action<MaterialProperty, ShaderValue> assign;

        /// <summary>False for a value the field itself would refuse, such as a 2D texture for a cubemap slot.</summary>
        public Func<ShaderValue, bool> accepts;
        public ShaderValue defaultValue;

        /// <summary>A texture with Tiling and Offset rows; without them the scale and offset are not compared or pasted.</summary>
        public bool scaleOffset;

        /// <summary>Drawn as a checkbox in its section's header ([SectionToggle]) instead of as a row.</summary>
        public bool headerToggle;

        public VisualElement mark;
        public bool changed;

        /// <summary>In a section whose header switch is off: the IMGUI parts ignore the content's opacity, so they draw at half strength themselves.</summary>
        public bool dimmed;

        /// <summary>Every IMGUI part of the row, which must lay out again when any value changes (a decorator's height can follow another property).</summary>
        public void MarkImguiDirty()
        {
            bridge?.MarkDirtyLayout();
            foreach (var decorator in decorators)
                decorator.MarkDirtyLayout();
        }

        public void SetDimmed(bool value)
        {
            if (dimmed == value)
                return;
            dimmed = value;
            bridge?.MarkDirtyRepaint();
            foreach (var decorator in decorators)
                decorator.MarkDirtyRepaint();
        }
    }

    /// <summary>One UI Toolkit field per shader property type and Unity drawer, plus the IMGUI bridge for every drawer it does not know.</summary>
    internal static class ShaderPropertyFields
    {
        public const string RowClass = "docme-si-row";
        public const string BridgeClass = "docme-si-bridge";
        public const string DecoratorClass = "docme-si-decorator";
        public const string HeaderClass = "docme-si-header";
        public const string SpaceClass = "docme-si-space";
        public const string HelpClass = "docme-si-help";
        public const string TextureClass = "docme-si-texture";
        public const string TextureFieldsClass = "docme-si-texture-fields";
        public const string ThumbClass = "docme-si-thumb";
        public const string ThumbSmallClass = "docme-si-thumb--small";
        public const string ThumbEmptyClass = "docme-si-thumb--empty";
        public const string PowerValueClass = "docme-si-power-value";
        public const string NormalFixClass = "docme-si-normal-fix";

        public const string KindBridge = "bridge";
        public const string KindSlider = "slider";
        public const string KindPowerSlider = "power-slider";
        public const string KindIntRange = "int-range";
        public const string KindFloat = "float";
        public const string KindInt = "int";
        public const string KindToggle = "toggle";
        public const string KindEnum = "enum";
        public const string KindKeywordEnum = "keyword-enum";
        public const string KindColor = "color";
        public const string KindVector = "vector";
        public const string KindTexture = "texture";

        internal enum ToggleKeyword { None, On, Off }

        static readonly HashSet<string> ToggleOn = new HashSet<string> { "Toggle", "MaterialToggle" };
        static readonly HashSet<string> ToggleOff = new HashSet<string> { "ToggleOff", "MaterialToggleOff" };
        static readonly HashSet<string> ToggleUi = new HashSet<string> { "ToggleUI", "MaterialToggleUI" };
        // [Dropdown] reads like [Enum] but takes any number of entries, and Unity has no drawer by that name, so it stays quiet.
        static readonly HashSet<string> EnumNames = new HashSet<string> { "Enum", "MaterialEnum", "Dropdown" };
        static readonly HashSet<string> KeywordEnumNames = new HashSet<string> { "KeywordEnum", "MaterialKeywordEnum" };
        static readonly HashSet<string> IntRangeNames = new HashSet<string> { "IntRange", "MaterialIntRange" };
        static readonly HashSet<string> PowerSliderNames = new HashSet<string> { "PowerSlider", "MaterialPowerSlider" };
        static readonly HashSet<string> SpaceNames = new HashSet<string> { "Space", "MaterialSpace" };
        internal static bool IsHeader(string attribute) => HeaderNames.Contains(attribute);

        static readonly HashSet<string> HeaderNames = new HashSet<string> { "Header", "MaterialHeader" };
        static readonly HashSet<string> FlagNames = new HashSet<string>
        {
            "HDR", "Gamma", "NoScaleOffset", "Normal", "MainTexture", "MainColor", "PerRendererData", "HideInInspector", "NonModifiableTextureData",
        };

        sealed class Decoration
        {
            public string name;
            public string[] args;
            public MaterialPropertyDrawer custom;
        }

        sealed class Plan
        {
            public string drawer;
            public string[] drawerArgs = Array.Empty<string>();
            public bool unknownDrawer;
            public readonly List<Decoration> decorations = new List<Decoration>();
        }

        public static ShaderPropertyRow Create(IShaderFieldHost host, Shader shader, ShaderLayout.Item item)
        {
            var row = CreateField(host, shader, item);
            row.type = shader.GetPropertyType(item.index);
            row.defaultValue = ShaderValue.DefaultOf(shader, item.index);
            row.assign ??= PlainAssign(row, shader);
            return row;
        }

        /// <summary>The section header checkbox of a [SectionToggle] property: it writes like the Toggle row it replaces.</summary>
        public static ShaderPropertyRow CreateHeaderToggle(IShaderFieldHost host, Shader shader, ShaderLayout.Item item, ToggleKeyword mode, string keyword)
        {
            var row = new ShaderPropertyRow
            {
                item = item,
                headerToggle = true,
                kind = KindToggle,
                type = shader.GetPropertyType(item.index),
                defaultValue = ShaderValue.DefaultOf(shader, item.index),
            };
            bool isInt = row.type == ShaderPropertyType.Int;
            var toggle = new MixedToggle
            {
                name = item.name,
                tooltip = string.IsNullOrEmpty(item.tooltip) ? item.displayName ?? "" : item.tooltip,
            };
            UndoGrouping.Attach(toggle);
            row.root = toggle;
            row.field = toggle;
            string name = item.name;
            toggle.RegisterValueChangedCallback(e =>
            {
                if (e.target != toggle)
                    return;
                bool on = e.newValue;
                host.Write(name, p => ApplyToggle(p, on, isInt, mode, keyword), toggle);
            });
            row.pull = p =>
            {
                toggle.SetValueWithoutNotify(IsOn(p));
                toggle.showMixedValue = p.hasMixedValue;
            };
            row.assign = (p, v) => ApplyToggle(p, IsOn(v, isInt), isInt, mode, keyword);
            if ((shader.GetPropertyFlags(item.index) & ShaderPropertyFlags.PerRendererData) != 0)
            {
                row.alwaysReadOnly = true;
                toggle.SetEnabled(false);
            }
            return row;
        }

        /// <summary>
        /// Whether [SectionToggle] can put this property in its section's header: a [Toggle], [ToggleOff] or [ToggleUI]
        /// number, or a plain Float, with no custom drawer or decorator that would have to draw.
        /// </summary>
        internal static bool TryReadHeaderToggle(Shader shader, ShaderLayout.Item item, out ToggleKeyword mode, out string keyword)
        {
            mode = ToggleKeyword.None;
            keyword = null;
            var type = shader.GetPropertyType(item.index);
            if (type != ShaderPropertyType.Float && type != ShaderPropertyType.Range && type != ShaderPropertyType.Int)
                return false;
            var plan = PlanFor(shader, item);
            if (plan.unknownDrawer || plan.decorations.Exists(d => d.custom != null))
                return false;
            switch (plan.drawer)
            {
                case "Toggle": mode = ToggleKeyword.On; break;
                case "ToggleOff": mode = ToggleKeyword.Off; break;
                case "ToggleUI": mode = ToggleKeyword.None; break;
                case null:
                    if (type != ShaderPropertyType.Float)
                        return false;
                    break;
                default:
                    return false;
            }
            keyword = plan.drawerArgs.Length == 1 && plan.drawerArgs[0].Length > 0 ? plan.drawerArgs[0] : null;
            return true;
        }

        /// <summary>Above this a switch reads as on, as in Unity's own toggle drawers; [ShowIf(_Prop)] uses the same rule for "not 0".</summary>
        internal const float OnThreshold = 0.001f;

        internal static bool IsOn(MaterialProperty p)
            => p.type == MaterialProperty.PropType.Int ? p.intValue != 0 : Math.Abs(p.floatValue) > OnThreshold;

        static bool IsOn(ShaderValue v, bool isInt) => isInt ? v.integer != 0 : Math.Abs(v.number) > OnThreshold;

        internal static void ApplyToggle(MaterialProperty p, bool on, bool isInt, ToggleKeyword mode, string keyword)
        {
            if (isInt) p.intValue = on ? 1 : 0;
            else p.floatValue = on ? 1f : 0f;
            SetToggleKeyword(p, on, mode, keyword);
        }

        /// <summary>The writer for a row whose field sets nothing but the value; Range values are kept inside the range.</summary>
        static Action<MaterialProperty, ShaderValue> PlainAssign(ShaderPropertyRow row, Shader shader)
        {
            var limits = row.type == ShaderPropertyType.Range ? shader.GetPropertyRangeLimits(row.item.index) : Vector2.zero;
            return (p, v) =>
            {
                switch (row.type)
                {
                    case ShaderPropertyType.Float: p.floatValue = v.number; break;
                    case ShaderPropertyType.Range: p.floatValue = ClampToRange(v.number, limits); break;
                    case ShaderPropertyType.Int: p.intValue = v.integer; break;
                    case ShaderPropertyType.Color: p.colorValue = v.vector; break;
                    case ShaderPropertyType.Vector: p.vectorValue = v.vector; break;
                    case ShaderPropertyType.Texture:
                        p.textureValue = v.texture;
                        if (row.scaleOffset)
                            p.textureScaleAndOffset = v.scaleOffset;
                        break;
                }
            };
        }

        static ShaderPropertyRow CreateField(IShaderFieldHost host, Shader shader, ShaderLayout.Item item)
        {
            var row = new ShaderPropertyRow { item = item, root = new VisualElement() };
            row.root.AddToClassList(RowClass);
            row.root.name = "row-" + item.name;

            if (!string.IsNullOrEmpty(item.help))
            {
                var help = new HelpBox(item.help, HelpBoxMessageType.Info);
                help.AddToClassList(HelpClass);
                row.root.Add(help);
            }

            var plan = PlanFor(shader, item);
            if (plan.unknownDrawer)
            {
                BuildBridge(host, row);
                return row;
            }

            bool firstHeader = true;
            for (int i = 0; i < plan.decorations.Count; i++)
            {
                var decoration = plan.decorations[i];
                if (decoration.custom != null)
                    AddDecoratorContainer(host, row, decoration.custom, i);
                else
                {
                    bool header = IsHeader(decoration.name);
                    AddDecoration(row.root, decoration.name, decoration.args, header && firstHeader ? row.item.headerTitle : null);
                    firstHeader &= !header;
                }
            }

            var type = shader.GetPropertyType(item.index);
            var flags = shader.GetPropertyFlags(item.index);
            switch (type)
            {
                case ShaderPropertyType.Range:
                    if (plan.drawer == "IntRange") BuildIntRange(host, shader, row);
                    else if (plan.drawer == "PowerSlider") BuildPowerSlider(host, shader, row, ParseFloat(plan.drawerArgs[0]));
                    else if (!BuildNumberDrawer(host, row, plan, false)) BuildSlider(host, shader, row);
                    break;
                case ShaderPropertyType.Float:
                    if (!BuildNumberDrawer(host, row, plan, false)) BuildFloat(host, row);
                    break;
                case ShaderPropertyType.Int:
                    if (!BuildNumberDrawer(host, row, plan, true)) BuildInt(host, row);
                    break;
                case ShaderPropertyType.Color:
                    BuildColor(host, row, (flags & ShaderPropertyFlags.HDR) != 0);
                    break;
                case ShaderPropertyType.Vector:
                    BuildVector(host, row);
                    break;
                case ShaderPropertyType.Texture:
                    BuildTexture(host, shader, row, flags);
                    break;
                default:
                    BuildBridge(host, row);
                    return row;
            }

            if ((flags & (ShaderPropertyFlags.PerRendererData | ShaderPropertyFlags.NonModifiableTextureData)) != 0)
            {
                row.alwaysReadOnly = true;
                row.root.SetEnabled(false);
            }
            return row;
        }

        static Plan PlanFor(Shader shader, ShaderLayout.Item item)
        {
            var plan = new Plan();
            var propertyType = shader.GetPropertyType(item.index);
            bool number = propertyType == ShaderPropertyType.Float || propertyType == ShaderPropertyType.Range || propertyType == ShaderPropertyType.Int;
            foreach (var raw in item.attributes)
            {
                ShaderLayout.ParseAttr(raw, out string name, out string[] args);
                if (ShaderLayout.OwnAttributes.Contains(name) || FlagNames.Contains(name))
                    continue;
                if (SpaceNames.Contains(name) || HeaderNames.Contains(name))
                {
                    plan.decorations.Add(new Decoration { name = name, args = args });
                    continue;
                }

                string drawer = null;
                bool suitable = false;
                if (ToggleOn.Contains(name)) { drawer = "Toggle"; suitable = number && args.Length <= 1; }
                else if (ToggleOff.Contains(name)) { drawer = "ToggleOff"; suitable = number && args.Length <= 1; }
                else if (ToggleUi.Contains(name)) { drawer = "ToggleUI"; suitable = number && args.Length <= 1; }
                else if (EnumNames.Contains(name)) { drawer = "Enum"; suitable = number && TryReadEnum(args, out _, out _); }
                else if (KeywordEnumNames.Contains(name)) { drawer = "KeywordEnum"; suitable = number && args.Length > 0 && args.Length <= 9; }
                else if (IntRangeNames.Contains(name)) { drawer = "IntRange"; suitable = propertyType == ShaderPropertyType.Range && args.Length == 0; }
                else if (PowerSliderNames.Contains(name)) { drawer = "PowerSlider"; suitable = propertyType == ShaderPropertyType.Range && args.Length == 1 && !float.IsNaN(ParseFloat(args[0])); }

                if (drawer == null)
                {
                    var type = FindDrawerType(name, out bool decorator);
                    if (type == null)
                        continue;
                    var custom = decorator ? CreateDrawer(type, args) : null;
                    if (custom == null)
                        plan.unknownDrawer = true;
                    else
                        plan.decorations.Add(new Decoration { name = name, args = args, custom = custom });
                    continue;
                }
                if (!suitable || plan.drawer != null)
                {
                    plan.unknownDrawer = true;
                    continue;
                }
                plan.drawer = drawer;
                plan.drawerArgs = args;
            }
            return plan;
        }

        /// <summary>The class Unity would pick for [name]: name, nameDrawer, MaterialnameDrawer, nameDecorator or MaterialnameDecorator, first match wins.</summary>
        static Type FindDrawerType(string name, out bool decorator)
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<MaterialPropertyDrawer>())
            {
                string n = type.Name;
                if (n == name || n == name + "Drawer" || n == "Material" + name + "Drawer" || n == name + "Decorator" || n == "Material" + name + "Decorator")
                {
                    decorator = n.EndsWith("Decorator", StringComparison.Ordinal);
                    return type;
                }
            }
            decorator = false;
            return null;
        }

        /// <summary>Builds the drawer the way Unity does: arguments that read as numbers are passed as floats, the rest as text.</summary>
        static MaterialPropertyDrawer CreateDrawer(Type type, string[] args)
        {
            try
            {
                if (args.Length == 0 || (args.Length == 1 && args[0].Length == 0))
                    return Activator.CreateInstance(type) as MaterialPropertyDrawer;
                var values = new object[args.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    float number = ParseFloat(args[i]);
                    values[i] = float.IsNaN(number) ? (object)args[i] : number;
                }
                return Activator.CreateInstance(type, values) as MaterialPropertyDrawer;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// A decorator Unity does not ship (a button, a tag kept in step) draws in its own small IMGUI slice above the
        /// native field, the way Unity's handler draws each decorator in its own slice before the property.
        /// </summary>
        static void AddDecoratorContainer(IShaderFieldHost host, ShaderPropertyRow row, MaterialPropertyDrawer decorator, int index)
        {
            var item = row.item;
            var container = new IMGUIContainer { name = item.name + "-decorator-" + index };
            container.AddToClassList(DecoratorClass);
            bool failed = false;
            container.onGUIHandler = () =>
            {
                if (failed)
                    return;
                var editor = host.Editor;
                var prop = editor == null ? null : host.HostProperty(item.name);
                if (prop == null)
                    return;
                float oldLabelWidth = EditorGUIUtility.labelWidth;
                float oldFieldWidth = EditorGUIUtility.fieldWidth;
                var oldColor = GUI.color;
                try
                {
                    if (row.dimmed)
                        GUI.color = Dimmed(oldColor);
                    float height = decorator.GetPropertyHeight(prop, item.displayName, editor);
                    Rect rect = GUILayoutUtility.GetRect(0f, Mathf.Max(0f, height), GUILayout.ExpandWidth(true));
                    decorator.OnGUI(rect, prop, new GUIContent(item.displayName, item.tooltip), editor);
                }
                catch (Exception e) when (!IsExitGui(e))
                {
                    failed = true;
                    ReportDrawFailure("The decorator above " + item.name, e);
                    container.MarkDirtyLayout();
                }
                finally
                {
                    EditorGUIUtility.labelWidth = oldLabelWidth;
                    EditorGUIUtility.fieldWidth = oldFieldWidth;
                    GUI.color = oldColor;
                }
            };
            row.decorators.Add(container);
            row.root.Add(container);
        }

        /// <summary>
        /// A drawer that throws while it draws is left out from then on, after one console line, the way Unity's own inspector
        /// contains it: rethrown, the exception would stop UI Toolkit from drawing the rest of the window every frame.
        /// </summary>
        static void ReportDrawFailure(string what, Exception e)
            => Debug.LogWarning("[DocMe Shader UI] " + what + " failed while drawing and is left out: " + e);

        /// <summary>IMGUI's own way out of a GUI pass, which must reach Unity untouched.</summary>
        internal static bool IsExitGui(Exception e)
        {
            while (e is System.Reflection.TargetInvocationException && e.InnerException != null)
                e = e.InnerException;
            return e is ExitGUIException;
        }

        static void AddDecoration(VisualElement root, string name, string[] args, string headerTitle)
        {
            if (SpaceNames.Contains(name))
            {
                float height = args.Length == 1 ? ParseFloat(args[0]) : 6f;
                if (args.Length > 1 || float.IsNaN(height))
                    return;
                var space = new VisualElement();
                space.AddToClassList(SpaceClass);
                space.style.height = height;
                root.Add(space);
                return;
            }
            if (args.Length != 1)
                return;
            var header = new Label(headerTitle ?? args[0]);
            header.AddToClassList(HeaderClass);
            root.Add(header);
        }

        static T Prepare<T>(ShaderPropertyRow row, T field, string kind) where T : VisualElement
        {
            field.name = row.item.name;
            field.tooltip = row.item.tooltip ?? "";
            field.AddToClassList(BaseField<float>.alignedFieldUssClassName);
            UndoGrouping.Attach(field);
            row.field = field;
            row.kind = kind;
            row.root.Add(field);
            GrowWithLabel(field);
            return field;
        }

        static void GrowWithLabel(VisualElement field)
        {
            var label = field.Q<Label>(className: BaseField<float>.labelUssClassName);
            if (label != null)
                label.RegisterCallback<GeometryChangedEvent>(e => GrowWithText((Label)e.target));
        }

        /// <summary>
        /// The layout sizes a wrapping label at its exact fractional width, the text is drawn at the rounded width:
        /// a label whose text just fits the first can need a second line in the second.
        /// </summary>
        static void GrowWithText(Label label)
        {
            var rect = label.contentRect;
            if (string.IsNullOrEmpty(label.text) || rect.width <= 0f || label.resolvedStyle.whiteSpace != WhiteSpace.Normal)
                return;
            var size = label.MeasureTextSize(label.text, rect.width, VisualElement.MeasureMode.Exactly, 0f, VisualElement.MeasureMode.Undefined);
            var style = label.resolvedStyle;
            float needed = Mathf.Ceil(size.y + style.paddingTop + style.paddingBottom + style.borderTopWidth + style.borderBottomWidth);
            var current = label.style.minHeight;
            if (needed > label.layout.height + 0.5f)
                label.style.minHeight = needed;
            else if (current.keyword == StyleKeyword.Undefined && needed < current.value.value - 0.5f)
                label.style.minHeight = StyleKeyword.Null;
        }

        static void BuildSlider(IShaderFieldHost host, Shader shader, ShaderPropertyRow row)
        {
            var limits = shader.GetPropertyRangeLimits(row.item.index);
            var slider = Prepare(row, new MixedSlider(row.item.displayName, limits.x, limits.y) { showInputField = true }, KindSlider);
            string name = row.item.name;
            slider.RegisterValueChangedCallback(e => host.Write(name, p => p.floatValue = e.newValue, slider));
            row.pull = p =>
            {
                slider.SetValueWithoutNotify(ClampToRange(p.floatValue, limits));
                slider.showMixedValue = p.hasMixedValue;
            };
        }

        static void BuildPowerSlider(IShaderFieldHost host, Shader shader, ShaderPropertyRow row, float power)
        {
            var limits = shader.GetPropertyRangeLimits(row.item.index);
            float inverse = 1f / power;
            var slider = Prepare(row, new MixedSlider(row.item.displayName, PowPreserveSign(limits.x, inverse), PowPreserveSign(limits.y, inverse)), KindPowerSlider);
            var box = new MixedFloatField { name = row.item.name + "-value", tooltip = row.item.tooltip ?? "" };
            box.AddToClassList(PowerValueClass);
            slider.Add(box);
            string name = row.item.name;

            slider.RegisterValueChangedCallback(e =>
            {
                if (e.target != slider)
                    return;
                float value = ClampToRange(PowPreserveSign(e.newValue, power), limits);
                box.SetValueWithoutNotify(value);
                box.showMixedValue = false;
                host.Write(name, p => p.floatValue = value, slider);
            });
            box.RegisterValueChangedCallback(e =>
            {
                e.StopPropagation();
                float value = ClampToRange(e.newValue, limits);
                slider.SetValueWithoutNotify(PowPreserveSign(value, inverse));
                slider.showMixedValue = false;
                host.Write(name, p => p.floatValue = value, box);
            });
            row.pull = p =>
            {
                float value = ClampToRange(p.floatValue, limits);
                slider.SetValueWithoutNotify(PowPreserveSign(value, inverse));
                box.SetValueWithoutNotify(value);
                slider.showMixedValue = p.hasMixedValue;
                box.showMixedValue = p.hasMixedValue;
            };
        }

        static void BuildIntRange(IShaderFieldHost host, Shader shader, ShaderPropertyRow row)
        {
            var limits = shader.GetPropertyRangeLimits(row.item.index);
            var slider = Prepare(row, new MixedSliderInt(row.item.displayName, (int)limits.x, (int)limits.y) { showInputField = true }, KindIntRange);
            string name = row.item.name;
            slider.RegisterValueChangedCallback(e => host.Write(name, p => p.floatValue = e.newValue, slider));
            row.pull = p =>
            {
                slider.SetValueWithoutNotify((int)p.floatValue);
                slider.showMixedValue = p.hasMixedValue;
            };
            row.assign = (p, v) => p.floatValue = Mathf.Round(ClampToRange(v.number, limits));
        }

        static void BuildFloat(IShaderFieldHost host, ShaderPropertyRow row)
        {
            var field = Prepare(row, new MixedFloatField(row.item.displayName), KindFloat);
            string name = row.item.name;
            field.RegisterValueChangedCallback(e => host.Write(name, p => p.floatValue = e.newValue, field));
            row.pull = p =>
            {
                field.SetValueWithoutNotify(p.floatValue);
                field.showMixedValue = p.hasMixedValue;
            };
        }

        static void BuildInt(IShaderFieldHost host, ShaderPropertyRow row)
        {
            var field = Prepare(row, new MixedIntegerField(row.item.displayName), KindInt);
            string name = row.item.name;
            field.RegisterValueChangedCallback(e => host.Write(name, p => p.intValue = e.newValue, field));
            row.pull = p =>
            {
                field.SetValueWithoutNotify(p.intValue);
                field.showMixedValue = p.hasMixedValue;
            };
        }

        static bool BuildNumberDrawer(IShaderFieldHost host, ShaderPropertyRow row, Plan plan, bool isInt)
        {
            string name = row.item.name;
            switch (plan.drawer)
            {
                case "Toggle":
                case "ToggleOff":
                case "ToggleUI":
                {
                    var mode = plan.drawer == "Toggle" ? ToggleKeyword.On : plan.drawer == "ToggleOff" ? ToggleKeyword.Off : ToggleKeyword.None;
                    string keyword = plan.drawerArgs.Length == 1 ? plan.drawerArgs[0] : null;
                    var toggle = Prepare(row, new MixedToggle(row.item.displayName), KindToggle);
                    toggle.RegisterValueChangedCallback(e =>
                    {
                        bool on = e.newValue;
                        host.Write(name, p => ApplyToggle(p, on, isInt, mode, keyword), toggle);
                    });
                    row.pull = p =>
                    {
                        toggle.SetValueWithoutNotify(IsOn(p));
                        toggle.showMixedValue = p.hasMixedValue;
                    };
                    row.assign = (p, v) => ApplyToggle(p, IsOn(v, isInt), isInt, mode, keyword);
                    return true;
                }
                case "Enum":
                {
                    TryReadEnum(plan.drawerArgs, out var names, out var values);
                    var shown = names.Select(row.item.OptionTitle).ToList();
                    var dropdown = Prepare(row, new MixedDropdownField(row.item.displayName, shown, -1), KindEnum);
                    dropdown.RegisterValueChangedCallback(_ =>
                    {
                        int i = dropdown.index;
                        if (i < 0 || i >= values.Count)
                            return;
                        int value = values[i];
                        host.Write(name, p =>
                        {
                            if (isInt) p.intValue = value;
                            else p.floatValue = value;
                        }, dropdown);
                    });
                    row.pull = p =>
                    {
                        int current = isInt ? p.intValue : (int)p.floatValue;
                        int i = values.IndexOf(current);
                        dropdown.SetShown(i >= 0 ? shown[i] : "", p.hasMixedValue);
                    };
                    return true;
                }
                case "KeywordEnum":
                {
                    var names = new List<string>(plan.drawerArgs);
                    var shown = names.Select(row.item.OptionTitle).ToList();
                    var dropdown = Prepare(row, new MixedDropdownField(row.item.displayName, shown, -1), KindKeywordEnum);
                    dropdown.RegisterValueChangedCallback(_ =>
                    {
                        int i = dropdown.index;
                        if (i < 0)
                            return;
                        host.Write(name, p =>
                        {
                            if (isInt) p.intValue = i;
                            else p.floatValue = i;
                            SetKeywordEnum(p, names, i);
                        }, dropdown);
                    });
                    row.pull = p =>
                    {
                        int i = isInt ? p.intValue : (int)p.floatValue;
                        dropdown.SetShown(i >= 0 && i < shown.Count ? shown[i] : "", p.hasMixedValue);
                    };
                    row.assign = (p, v) =>
                    {
                        int i = isInt ? v.integer : (int)v.number;
                        if (isInt) p.intValue = i;
                        else p.floatValue = i;
                        if (i >= 0 && i < names.Count)
                            SetKeywordEnum(p, names, i);
                    };
                    return true;
                }
            }
            return false;
        }

        static void BuildColor(IShaderFieldHost host, ShaderPropertyRow row, bool hdr)
        {
            var field = Prepare(row, new MixedColorField(row.item.displayName) { showAlpha = !row.item.noAlpha, hdr = hdr }, KindColor);
            string name = row.item.name;
            field.RegisterValueChangedCallback(e => host.Write(name, p => p.colorValue = e.newValue, field));
            row.pull = p =>
            {
                field.SetValueWithoutNotify(p.colorValue);
                field.showMixedValue = p.hasMixedValue;
            };
        }

        static void BuildVector(IShaderFieldHost host, ShaderPropertyRow row)
        {
            var field = Prepare(row, new MixedVector4Field(row.item.displayName), KindVector);
            string name = row.item.name;
            field.RegisterValueChangedCallback(e => host.Write(name, p => p.vectorValue = e.newValue, field));
            row.pull = p =>
            {
                field.SetValueWithoutNotify(p.vectorValue);
                field.showMixedValue = p.hasMixedValue;
            };
        }

        static void BuildTexture(IShaderFieldHost host, Shader shader, ShaderPropertyRow row, ShaderPropertyFlags flags)
        {
            string name = row.item.name;
            var dimension = shader.GetPropertyTextureDimension(row.item.index);
            bool scaleOffset = (flags & ShaderPropertyFlags.NoScaleOffset) == 0;
            bool normal = (flags & ShaderPropertyFlags.Normal) != 0;

            var block = new VisualElement();
            block.AddToClassList(TextureClass);
            var fields = new VisualElement();
            fields.AddToClassList(TextureFieldsClass);
            block.Add(fields);

            var objectField = new MixedObjectField(row.item.displayName)
            {
                objectType = TextureType(dimension),
                allowSceneObjects = false,
                name = name,
                tooltip = row.item.tooltip ?? "",
            };
            objectField.AddToClassList(BaseField<float>.alignedFieldUssClassName);
            GrowWithLabel(objectField);
            fields.Add(objectField);

            var thumb = new Image { name = name + "-thumb", scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            thumb.AddToClassList(ThumbClass);
            if (!scaleOffset)
                thumb.AddToClassList(ThumbSmallClass);
            block.Add(thumb);

            MixedVector2Field tiling = null, offset = null;
            if (scaleOffset)
            {
                tiling = new MixedVector2Field(host.Ui("ui.texture.tiling", "Tiling")) { name = name + "-tiling", tooltip = host.Ui("ui.texture.tilingTip", "How often the texture repeats across the surface.") };
                offset = new MixedVector2Field(host.Ui("ui.texture.offset", "Offset")) { name = name + "-offset", tooltip = host.Ui("ui.texture.offsetTip", "Shifts the texture across the surface.") };
                tiling.AddToClassList(BaseField<float>.alignedFieldUssClassName);
                offset.AddToClassList(BaseField<float>.alignedFieldUssClassName);
                GrowWithLabel(tiling);
                GrowWithLabel(offset);
                UndoGrouping.Attach(tiling);
                UndoGrouping.Attach(offset);
                fields.Add(tiling);
                fields.Add(offset);
            }

            var fix = new VisualElement { name = name + "-normal-fix" };
            fix.AddToClassList(NormalFixClass);
            fix.Add(new HelpBox(host.Ui("ui.texture.notNormal", "This texture is not marked as a normal map."), HelpBoxMessageType.Warning));
            var fixButton = new Button { text = host.Ui("ui.texture.fix", "Fix Now"), tooltip = host.Ui("ui.texture.fixTip", "Changes the texture's import type to Normal map.") };
            fix.Add(fixButton);
            fix.style.display = DisplayStyle.None;

            row.root.Add(block);
            row.root.Add(fix);
            row.field = objectField;
            row.kind = KindTexture;
            row.scaleOffset = scaleOffset;
            row.accepts = v => v.texture == null || dimension == TextureDimension.Any || v.texture.dimension == dimension;
            UndoGrouping.Attach(objectField);

            objectField.RegisterValueChangedCallback(e =>
            {
                var texture = e.newValue as Texture;
                if (texture != null && dimension != TextureDimension.Any && texture.dimension != dimension)
                {
                    objectField.SetValueWithoutNotify(e.previousValue);
                    return;
                }
                host.Write(name, p => p.textureValue = texture, objectField);
            });
            if (scaleOffset)
            {
                tiling.RegisterValueChangedCallback(e => host.Write(name, p =>
                {
                    var current = p.textureScaleAndOffset;
                    p.textureScaleAndOffset = new Vector4(e.newValue.x, e.newValue.y, current.z, current.w);
                }, tiling));
                offset.RegisterValueChangedCallback(e => host.Write(name, p =>
                {
                    var current = p.textureScaleAndOffset;
                    p.textureScaleAndOffset = new Vector4(current.x, current.y, e.newValue.x, e.newValue.y);
                }, offset));
            }
            fixButton.clicked += () =>
            {
                var editor = host.Editor;
                if (editor == null)
                    return;
                foreach (var target in editor.targets)
                    if (target is Material material)
                        InternalEditorUtility.FixNormalmapTextureInternal(material, name);
            };

            row.pull = p =>
            {
                objectField.SetValueWithoutNotify(p.textureValue);
                objectField.showMixedValue = p.hasMixedValue;
                thumb.image = p.hasMixedValue ? null : Thumbnail(p.textureValue);
                thumb.EnableInClassList(ThumbEmptyClass, thumb.image == null);
                if (scaleOffset)
                {
                    var so = p.textureScaleAndOffset;
                    tiling.SetValueWithoutNotify(new Vector2(so.x, so.y));
                    offset.SetValueWithoutNotify(new Vector2(so.z, so.w));
                    MixedScaleOffset(p, out bool mixedScale, out bool mixedOffset);
                    tiling.showMixedValue = mixedScale;
                    offset.showMixedValue = mixedOffset;
                }
                bool needsFix = false;
                if (p.textureValue != null)
                    foreach (var target in p.targets)
                        if (target is Material material && InternalEditorUtility.BumpMapTextureNeedsFixingInternal(material, p.name, normal))
                            needsFix = true;
                var display = needsFix ? DisplayStyle.Flex : DisplayStyle.None;
                if (fix.style.display != display)
                    fix.style.display = display;
            };
        }

        static void BuildBridge(IShaderFieldHost host, ShaderPropertyRow row)
        {
            var bridge = new IMGUIContainer { name = row.item.name };
            bridge.AddToClassList(BridgeClass);
            bool failed = false;
            bridge.onGUIHandler = () =>
            {
                if (failed)
                    return;
                try
                {
                    DrawBridge(host, row, bridge);
                }
                catch (Exception e) when (!IsExitGui(e))
                {
                    failed = true;
                    ReportDrawFailure("The drawer of " + row.item.name, e);
                    bridge.MarkDirtyLayout();
                }
            };
            row.bridge = bridge;
            row.field = bridge;
            row.kind = KindBridge;
            row.root.Add(bridge);
            row.pull = _ => bridge.MarkDirtyLayout();
        }

        static void DrawBridge(IShaderFieldHost host, ShaderPropertyRow row, IMGUIContainer bridge)
        {
            var item = row.item;
            var editor = host.Editor;
            var prop = editor == null ? null : host.HostProperty(item.name);
            if (prop == null)
                return;

            float oldLabelWidth = EditorGUIUtility.labelWidth;
            bool oldWideMode = EditorGUIUtility.wideMode;
            var oldColor = GUI.color;
            try
            {
                if (row.dimmed)
                    GUI.color = Dimmed(oldColor);
                var label = new GUIContent(item.displayName, item.tooltip);
                float height = editor.GetPropertyHeight(prop, item.displayName);
                Rect rect = EditorGUILayout.GetControlRect(true, height, EditorStyles.layerMaskField);
                float column = host.ValueColumnX;
                if (!float.IsNaN(column) && !float.IsNaN(bridge.worldBound.x))
                    EditorGUIUtility.labelWidth = Mathf.Max(60f, column - bridge.worldBound.x - rect.x);
                EditorGUIUtility.wideMode = bridge.layout.width > 330f;
                editor.ShaderProperty(rect, prop, label);
            }
            finally
            {
                EditorGUIUtility.labelWidth = oldLabelWidth;
                EditorGUIUtility.wideMode = oldWideMode;
                GUI.color = oldColor;
            }
        }

        /// <summary>Half strength, the IMGUI counterpart of the dimmed section's opacity.</summary>
        static Color Dimmed(Color color) => new Color(color.r, color.g, color.b, color.a * 0.5f);

        static void SetToggleKeyword(MaterialProperty prop, bool on, ToggleKeyword mode, string keyword)
        {
            if (mode == ToggleKeyword.None)
                return;
            string suffix = mode == ToggleKeyword.On ? "_ON" : "_OFF";
            string kw = string.IsNullOrEmpty(keyword) ? prop.name.ToUpperInvariant() + suffix : keyword;
            bool enable = mode == ToggleKeyword.On ? on : !on;
            foreach (var target in prop.targets)
            {
                if (!(target is Material material))
                    continue;
                if (enable) material.EnableKeyword(kw);
                else material.DisableKeyword(kw);
            }
        }

        static void SetKeywordEnum(MaterialProperty prop, List<string> names, int index)
        {
            for (int i = 0; i < names.Count; i++)
            {
                string keyword = (prop.name + "_" + names[i]).Replace(' ', '_').ToUpperInvariant();
                foreach (var target in prop.targets)
                {
                    if (!(target is Material material))
                        continue;
                    if (i == index) material.EnableKeyword(keyword);
                    else material.DisableKeyword(keyword);
                }
            }
        }

        static readonly Dictionary<string, Type> enumTypes = new Dictionary<string, Type>(StringComparer.Ordinal);

        /// <summary>The English entries of an [Enum], [Dropdown] or [KeywordEnum] attribute; null for any other attribute.</summary>
        internal static List<string> OptionNames(string attribute, string[] args)
        {
            if (EnumNames.Contains(attribute))
                return TryReadEnum(args, out var names, out _) ? names : null;
            if (KeywordEnumNames.Contains(attribute))
                return new List<string>(args);
            return null;
        }

        public static bool TryReadEnum(string[] args, out List<string> names, out List<int> values)
        {
            names = new List<string>();
            values = new List<int>();
            if (args.Length == 1)
            {
                if (!enumTypes.TryGetValue(args[0], out var type))
                {
                    foreach (var candidate in TypeCache.GetTypesDerivedFrom(typeof(Enum)))
                    {
                        if (candidate.Name == args[0] || candidate.FullName == args[0])
                        {
                            type = candidate;
                            break;
                        }
                    }
                    enumTypes[args[0]] = type;
                }
                if (type == null || !type.IsEnum)
                    return false;
                foreach (var value in Enum.GetValues(type))
                {
                    names.Add(Enum.GetName(type, value));
                    values.Add(Convert.ToInt32(value));
                }
                return names.Count > 0;
            }
            // Unity's own [Enum] drawer stops at seven name and value pairs; this dropdown takes any number.
            if (args.Length < 2 || args.Length % 2 != 0)
                return false;
            for (int i = 0; i < args.Length; i += 2)
            {
                float value = ParseFloat(args[i + 1]);
                if (float.IsNaN(value))
                    return false;
                names.Add(args[i]);
                values.Add((int)value);
            }
            return true;
        }

        static float ParseFloat(string text)
            => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : float.NaN;

        static float PowPreserveSign(float value, float power)
        {
            float result = Mathf.Pow(Mathf.Abs(value), power);
            return value < 0f ? -result : result;
        }

        static float ClampToRange(float value, Vector2 limits)
        {
            bool invert = limits.x > limits.y;
            return Mathf.Clamp(value, invert ? limits.y : limits.x, invert ? limits.x : limits.y);
        }

        static Type TextureType(TextureDimension dimension)
        {
            switch (dimension)
            {
                case TextureDimension.Cube: return typeof(Cubemap);
                case TextureDimension.Tex3D: return typeof(Texture3D);
                case TextureDimension.Tex2DArray: return typeof(Texture2DArray);
                case TextureDimension.CubeArray: return typeof(CubemapArray);
                default: return typeof(Texture);
            }
        }

        static Texture Thumbnail(Texture texture)
        {
            if (texture == null)
                return null;
            if (texture.dimension == TextureDimension.Tex2D)
                return texture;
            return AssetPreview.GetMiniThumbnail(texture);
        }

        internal static void MixedScaleOffset(MaterialProperty prop, out bool scale, out bool offset)
        {
            scale = false;
            offset = false;
            var targets = prop.targets;
            if (targets == null || targets.Length < 2 || !(targets[0] is Material first))
                return;
            var s0 = first.GetTextureScale(prop.name);
            var o0 = first.GetTextureOffset(prop.name);
            for (int i = 1; i < targets.Length; i++)
            {
                if (!(targets[i] is Material other))
                    continue;
                scale |= other.GetTextureScale(prop.name) != s0;
                offset |= other.GetTextureOffset(prop.name) != o0;
            }
        }
    }

    /// <summary>
    /// One undo step per drag or per typed edit, the way Unity's bound fields do it: the group starts when the
    /// pointer goes down or a key is pressed in the field and ends when the pointer comes up or focus leaves.
    /// </summary>
    internal static class UndoGrouping
    {
        sealed class Group { public int id = -1; }

        static readonly ConditionalWeakTable<VisualElement, Group> Groups = new ConditionalWeakTable<VisualElement, Group>();

        public static void Attach(VisualElement field)
        {
            field.RegisterCallback<PointerDownEvent>(_ => Begin(field), TrickleDown.TrickleDown);
            field.RegisterCallback<KeyDownEvent>(_ => Begin(field), TrickleDown.TrickleDown);
            field.RegisterCallback<PointerUpEvent>(_ => End(field), TrickleDown.TrickleDown);
            field.RegisterCallback<FocusOutEvent>(_ => End(field));
        }

        static void Begin(VisualElement field)
        {
            var group = Groups.GetOrCreateValue(field);
            if (group.id >= 0)
                return;
            Undo.IncrementCurrentGroup();
            group.id = Undo.GetCurrentGroup();
        }

        static void End(VisualElement field)
        {
            if (!Groups.TryGetValue(field, out var group) || group.id < 0)
                return;
            group.id = -1;
            Undo.IncrementCurrentGroup();
        }

        public static void Collapse(VisualElement field)
        {
            for (var element = field; element != null; element = element.parent)
            {
                if (Groups.TryGetValue(element, out var group) && group.id >= 0)
                {
                    Undo.CollapseUndoOperations(group.id);
                    return;
                }
            }
        }
    }
}

namespace DocMe
{
    /// <summary>
    /// A field showing a mixed value, set to the value it happens to display, must still report a change so every
    /// target gets written (IMGUI does). Unity 2022.3.22's BaseField drops that set as "unchanged"; these fields
    /// send the change themselves, the way later Unity versions do.
    /// </summary>
    internal static class MixedCommit
    {
        static bool busy;
        static int quiet;

        /// <summary>While the view pulls values into its fields, a set from Unity's own redraw is not a user commit.</summary>
        public static IDisposable Quiet()
        {
            quiet++;
            return new QuietScope();
        }

        sealed class QuietScope : IDisposable
        {
            bool done;

            public void Dispose()
            {
                if (done)
                    return;
                done = true;
                quiet--;
            }
        }

        public static bool Handle<T>(BaseField<T> field, T newValue)
        {
            if (busy || quiet > 0 || !field.showMixedValue || !System.Collections.Generic.EqualityComparer<T>.Default.Equals(field.value, newValue))
                return false;
            T previous = field.value;
            busy = true;
            try
            {
                field.SetValueWithoutNotify(newValue);
                field.showMixedValue = false;
            }
            finally
            {
                busy = false;
            }
            if (field.panel != null)
            {
                using (var evt = UnityEngine.UIElements.ChangeEvent<T>.GetPooled(previous, newValue))
                {
                    evt.target = field;
                    field.SendEvent(evt);
                }
            }
            return true;
        }
    }

    internal sealed class MixedFloatField : FloatField
    {
        public MixedFloatField(string label = null) : base(label) { }
        public override float value { get => base.value; set { if (!MixedCommit.Handle(this, value)) base.value = value; } }
    }

    internal sealed class MixedIntegerField : IntegerField
    {
        public MixedIntegerField(string label = null) : base(label) { }
        public override int value { get => base.value; set { if (!MixedCommit.Handle(this, value)) base.value = value; } }
    }

    internal sealed class MixedSlider : Slider
    {
        public MixedSlider(string label, float low, float high) : base(label, low, high) { }
        public override float value
        {
            get => base.value;
            set
            {
                float clamped = Mathf.Clamp(value, Mathf.Min(lowValue, highValue), Mathf.Max(lowValue, highValue));
                if (!MixedCommit.Handle(this, clamped)) base.value = value;
            }
        }
    }

    internal sealed class MixedSliderInt : SliderInt
    {
        public MixedSliderInt(string label, int low, int high) : base(label, low, high) { }
        public override int value
        {
            get => base.value;
            set
            {
                int clamped = Mathf.Clamp(value, Mathf.Min(lowValue, highValue), Mathf.Max(lowValue, highValue));
                if (!MixedCommit.Handle(this, clamped)) base.value = value;
            }
        }
    }

    internal sealed class MixedToggle : Toggle
    {
        public MixedToggle(string label = null) : base(label) { }
        public override bool value { get => base.value; set { if (!MixedCommit.Handle(this, value)) base.value = value; } }
    }

    /// <summary>
    /// Unity's popup field sets its own value while it redraws the mixed look, so instead of watching sets it holds
    /// an empty value while mixed: any entry the user picks is then a change.
    /// </summary>
    internal sealed class MixedDropdownField : DropdownField
    {
        public MixedDropdownField(string label, List<string> choices, int index) : base(label, choices, index) { }

        public void SetShown(string shown, bool mixed)
        {
            SetValueWithoutNotify(mixed ? "" : shown);
            showMixedValue = mixed;
        }
    }

    internal sealed class MixedColorField : ColorField
    {
        public MixedColorField(string label) : base(label) { }
        public override Color value { get => base.value; set { if (!MixedCommit.Handle(this, value)) base.value = value; } }
    }

    internal sealed class MixedVector4Field : Vector4Field
    {
        public MixedVector4Field(string label) : base(label) { }
        public override Vector4 value { get => base.value; set { if (!MixedCommit.Handle(this, value)) base.value = value; } }
    }

    internal sealed class MixedVector2Field : Vector2Field
    {
        public MixedVector2Field(string label) : base(label) { }
        public override Vector2 value { get => base.value; set { if (!MixedCommit.Handle(this, value)) base.value = value; } }
    }

    internal sealed class MixedObjectField : ObjectField
    {
        public MixedObjectField(string label) : base(label) { }
        public override Object value { get => base.value; set { if (!MixedCommit.Handle(this, value)) base.value = value; } }
    }
}
