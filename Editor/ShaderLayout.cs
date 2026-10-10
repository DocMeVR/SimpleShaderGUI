using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DocMe
{
    /// <summary>
    /// The shader author vocabulary read from a shader's Properties block: which property sits in which
    /// [Foldable] section, its [Tip] and [Help], the [Category] above a section, the [Config] asset name, the
    /// [ShowIf] conditions and the [SectionToggle] header switches. Both the IMGUI path of SimpleShaderGUI and the
    /// UI Toolkit view read the same layout, so they agree on sections, search results and the EditorPrefs keys of
    /// the folds.
    /// </summary>
    internal sealed class ShaderLayout
    {
        public const string FoldPrefix = "DocMe.fold";

        internal sealed class Item
        {
            public int index;
            public string name;
            public string displayName;
            public string tooltip;
            public string help;
            public string[] attributes = Array.Empty<string>();
            public Condition showIf;
        }

        internal sealed class Section
        {
            public string name;
            public string category;
            public bool startClosed;
            public Condition showIf;

            /// <summary>The first item, drawn as a checkbox in the header because it carries [SectionToggle].</summary>
            public Item toggle;
            public ShaderPropertyFields.ToggleKeyword toggleMode;
            public string toggleKeyword;
            public readonly List<Item> items = new List<Item>();
        }

        /// <summary>[ShowIf(_Prop)] shows while _Prop is not 0, [ShowIf(_Prop, value)] while _Prop equals value.</summary>
        internal sealed class Condition
        {
            public string property;
            public bool hasValue;
            public float value;

            /// <summary>The attribute as written, for the console note.</summary>
            public string text;

            /// <summary>Names a property the shader lacks or one that holds no number: the property then always shows.</summary>
            public bool broken;
        }

        sealed class BuildContext
        {
            public ShaderLayout layout;
            public Shader shader;
            public string name;
            public Item current;
            public Section group;
            public bool startsSection;
        }

        // Section openers run first so Category attaches to the section it starts.
        static readonly HashSet<string> Starters = new HashSet<string> { "Foldable" };

        static readonly Dictionary<string, Action<string[], BuildContext>> Handlers =
            new Dictionary<string, Action<string[], BuildContext>>
        {
            ["Foldable"] = (a, c) =>
            {
                if (a.Length == 0) return;
                c.group = new Section { name = a[0], startClosed = a.Length <= 1 || a[1] != "open" };
                c.layout.Sections.Add(c.group);
                c.startsSection = true;
            },
            ["Category"] = (a, c) => { if (c.group != null && a.Length > 0) c.group.category = a[0]; },
            ["Tip"]      = (a, c) => { if (c.current != null && a.Length > 0) c.current.tooltip = a[0]; },
            ["Help"]     = (a, c) => { if (c.current != null && a.Length > 0) c.current.help = a[0]; },
            ["Config"]   = (a, c) => { if (a.Length > 0) c.layout.ConfigName = a[0]; },
            ["ShowIf"]   = ReadShowIf,
            ["SectionToggle"] = ReadSectionToggle,
        };

        /// <summary>The attribute names this framework itself reads; Unity's own drawers ignore them.</summary>
        public static readonly HashSet<string> OwnAttributes = new HashSet<string>(Handlers.Keys);

        static readonly HashSet<string> Reported = new HashSet<string>();

        public readonly List<Item> Ungrouped = new List<Item>();
        public readonly List<Section> Sections = new List<Section>();

        /// <summary>The properties some [ShowIf] reads: a change to one of them can show or hide others.</summary>
        public readonly HashSet<string> Controllers = new HashSet<string>();
        public string ConfigName;
        public string ShaderName = "";
        public int PropertyCount;

        readonly List<(Condition condition, string owner)> conditions = new List<(Condition, string)>();

        public static ShaderLayout Build(Shader shader)
        {
            var layout = new ShaderLayout();
            if (shader == null)
                return layout;

            layout.ShaderName = shader.name;
            var ctx = new BuildContext { layout = layout, shader = shader };
            int count = shader.GetPropertyCount();
            layout.PropertyCount = count;
            for (int i = 0; i < count; i++)
            {
                bool hidden = (shader.GetPropertyFlags(i) & ShaderPropertyFlags.HideInInspector) != 0;
                string[] attributes = shader.GetPropertyAttributes(i) ?? Array.Empty<string>();
                ctx.name = shader.GetPropertyName(i);
                ctx.startsSection = false;
                ctx.current = hidden ? null : new Item
                {
                    index = i,
                    name = ctx.name,
                    displayName = shader.GetPropertyDescription(i),
                    attributes = attributes,
                };

                RunHandlers(attributes, ctx, onlyStarters: true);
                RunHandlers(attributes, ctx, onlyStarters: false);

                if (hidden)
                    continue;
                if (ctx.group != null)
                    ctx.group.items.Add(ctx.current);
                else
                    layout.Ungrouped.Add(ctx.current);
            }
            layout.CheckConditions(shader);
            return layout;
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

        static void ReadShowIf(string[] a, BuildContext c)
        {
            string text = "ShowIf(" + string.Join(", ", a) + ")";
            if (a.Length == 0 || a.Length > 2 || a[0].Length == 0)
            {
                Report(c.layout, c.shader, c.name, text, "[" + text + "] on " + c.name + " needs a property name and at most one value, so it is ignored.");
                return;
            }
            var condition = new Condition { property = a[0], text = text };
            if (a.Length == 2)
            {
                if (!float.TryParse(a[1], NumberStyles.Float, CultureInfo.InvariantCulture, out condition.value))
                {
                    Report(c.layout, c.shader, c.name, text, "[" + text + "] on " + c.name + " compares with something that is not a number, so it is ignored.");
                    return;
                }
                condition.hasValue = true;
            }
            if (c.startsSection && c.group != null)
                c.group.showIf = condition;
            else if (c.current != null)
                c.current.showIf = condition;
            else
                return;
            c.layout.conditions.Add((condition, c.name));
        }

        static void ReadSectionToggle(string[] a, BuildContext c)
        {
            if (c.current == null || c.group == null || c.group.items.Count > 0)
            {
                Report(c.layout, c.shader, c.name, "SectionToggle", "[SectionToggle] on " + c.name + " is ignored: it only works on the first property of a [Foldable] section.");
                return;
            }
            if (!ShaderPropertyFields.TryReadHeaderToggle(c.shader, c.current, out var mode, out string keyword))
            {
                Report(c.layout, c.shader, c.name, "SectionToggle", "[SectionToggle] on " + c.name + " is ignored: the property must be a Toggle, ToggleOff, ToggleUI or plain Float.");
                return;
            }
            c.group.toggle = c.current;
            c.group.toggleMode = mode;
            c.group.toggleKeyword = keyword;
        }

        void CheckConditions(Shader shader)
        {
            foreach (var (condition, owner) in conditions)
            {
                int index = shader.FindPropertyIndex(condition.property);
                string problem = null;
                if (index < 0)
                    problem = "names a property the shader does not have";
                else if (!IsNumber(shader.GetPropertyType(index)))
                    problem = "names " + condition.property + ", which holds no number";
                else if (condition.property == owner)
                    problem = "names its own property, which could then never show again";
                if (problem == null)
                {
                    Controllers.Add(condition.property);
                    continue;
                }
                condition.broken = true;
                Report(this, shader, owner, condition.text, "[" + condition.text + "] on " + owner + " " + problem + ", so " + owner + " always shows.");
            }
        }

        static bool IsNumber(ShaderPropertyType type)
            => type == ShaderPropertyType.Float || type == ShaderPropertyType.Range || type == ShaderPropertyType.Int;

        /// <summary>A mistake in a shader's attributes goes to the console once per editor session, naming the shader and the attribute.</summary>
        static void Report(ShaderLayout layout, Shader shader, string property, string attribute, string message)
        {
            if (!Reported.Add(layout.ShaderName + "|" + property + "|" + attribute))
                return;
            Debug.LogWarning("[DocMe Shader UI] Shader '" + layout.ShaderName + "': " + message, shader);
        }

        internal static void ForgetReports() => Reported.Clear();

        /// <summary>
        /// True while the property or section may show: the condition holds, or there is nothing to check (no condition,
        /// a broken one, no controlling property) or the controlling property differs between the selected materials.
        /// </summary>
        public static bool ConditionMet(Condition condition, MaterialProperty controller)
        {
            if (condition == null || condition.broken || controller == null || controller.hasMixedValue)
                return true;
            if (!condition.hasValue)
                return ShaderPropertyFields.IsOn(controller);
            float value = controller.type == MaterialProperty.PropType.Int ? controller.intValue : controller.floatValue;
            return Mathf.Abs(value - condition.value) <= ShaderValue.Tolerance;
        }


        public string FoldKey(string section) => FoldKey(ShaderName, section);

        public string LinkBoxKey(string title) => $"{FoldPrefix}.{ShaderName}.linkbox.{title}";

        public static string FoldKey(string shaderName, string section) => $"{FoldPrefix}.{shaderName}.{section}";

        public static string LinkBoxTitle(SimpleShaderConfig.LinkGroup box)
            => string.IsNullOrEmpty(box.title) ? "Links" : box.title;

        // "Foldable(Eye Look, closed)" gives name "Foldable" and args ["Eye Look", "closed"].
        public static void ParseAttr(string raw, out string name, out string[] args)
        {
            raw ??= "";
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

        public static void ParseSearch(string search, out bool sectionMode, out string query)
        {
            search ??= "";
            sectionMode = search.StartsWith("s:", StringComparison.OrdinalIgnoreCase);
            query = (sectionMode ? search.Substring(2) : search).Trim();
        }

        public static bool TextMatches(string text, string search)
            => !string.IsNullOrEmpty(text) && text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool ItemMatches(Item item, string search)
            => TextMatches(item.displayName, search) || TextMatches(item.name, search);

        public static bool SectionMatches(Section section, string search)
            => TextMatches(section.name, search) || TextMatches(section.category, search);

        public static bool SectionHasItemMatch(Section section, string search)
        {
            foreach (var item in section.items)
                if (ItemMatches(item, search))
                    return true;
            return false;
        }

        public static void BucketLinkBoxes(SimpleShaderConfig config, List<Section> sections,
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

        public static List<SimpleShaderConfig.SectionLink> FindSectionLinks(SimpleShaderConfig config, string sectionName)
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
    }
}
