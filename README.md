# Simple Shader UI

A lightweight, attribute-driven custom material inspector for Unity built-in
pipeline shaders. Group properties into collapsible sections, add tooltips and
help boxes, and attach a banner plus links, all from the shader's `Properties`
block and an optional config asset. No per-shader inspector code.

## Install

Download the `.unitypackage` (or the `.zip` for a package folder) from the
[Releases](https://github.com/DocMeVR/SimpleShaderGUI/releases) page and import it
into a Unity 2022.3 project.

## Setup

1. Add to the bottom of your shader, just before the final `}`:

   ```
   CustomEditor "DocMe.SimpleShaderGUI"
   ```

2. Optional: create a config for the banner and links via
   `Assets > Create > DocMe > Shader UI Config`, then reference it by name from
   the shader's `Properties` block:

   ```
   [HideInInspector][Config(MyConfigName)] _Config ("", Float) = 0
   ```

   The config Inspector has buttons to copy or auto-insert these two lines.

## Shader attributes

| Attribute | Effect |
| --- | --- |
| `[Foldable(Title)]` / `[Foldable(Title, open)]` | Collapsible section on the next property, closed by default; add `open` to expand |
| `[Category(Title)]` | Bold heading above a section |
| `[Tip(text)]` | Tooltip on a property |
| `[Help(text)]` | Info box above a property |
| `[Config(AssetName)]` | Name a Shader UI Config asset for the banner and links |
| `[ShowIf(_Prop)]` / `[ShowIf(_Prop, value)]` | Shows the property only while `_Prop` is not 0, or only while `_Prop` equals `value`. `_Prop` is a Float, Range or Int property, such as a Toggle, ToggleUI or Enum. On the property that carries `[Foldable]`, it shows or hides the whole section, header included |
| `[SectionToggle]` | On the first property of a section, when it is a `[Toggle]`, `[ToggleOff]`, `[ToggleUI]` or plain Float: draws it as a checkbox in the section header, left of the title, with the property's `[Tip]` as its tooltip. Clicking it never opens or closes the section. While it is off, the section's rows are dimmed but stay editable. A `[Help]`, `[Space]` or `[Header]` on that property is not drawn |

Attribute arguments accept plain text only (a ShaderLab limitation), so URLs,
paths, and icons live in the config asset and are edited in its Inspector.

A `[ShowIf]` naming a property the shader does not have, or one that holds no
number, is ignored, so the property always shows. A `[SectionToggle]` is ignored
too when its property is not the first of its section, when the property is
neither a Toggle, ToggleOff or ToggleUI nor a plain Float (a Float with no drawer
of its own), or when the property also has a custom drawer or a custom
decorator. Each such mistake is reported once in the Console, with the shader's
name.

With several materials selected, a `[ShowIf]` whose property differs between
them shows its property. Search finds only the properties that show.

A search box at the top of the inspector filters properties live: type to show
only matching properties, each under its section. Prefix the query with `s:` to
search section names instead.

Unity's own attributes work as usual: `[Toggle]`, `[ToggleOff]`, `[ToggleUI]`,
`[Enum]`, `[KeywordEnum]`, `[IntRange]`, `[PowerSlider]`, `[Space]`, `[Header]`,
`[HDR]`, `[NoScaleOffset]`, `[Normal]`, `[PerRendererData]` and `[HideInInspector]`.
Unity's own Enum drawer stops at seven entries, and for a longer `[Enum]` it
prints "Failed to create material drawer Enum" in the Console. For a longer list
write `[Dropdown(...)]` instead, with the same name and value pairs:
`[Dropdown(Round,0,Slit,1,Star,2,...)] _Shape ("Shape", Float) = 0`. The inspector
shows it as a dropdown with every entry; Unity knows no drawer by that name, so it
prints nothing (the older inspector shows a number box there).

## How the inspector draws

The material inspector is built with UI Toolkit. `SimpleShaderGUI` places its
view right below the material header in the Inspector, so it works under Unity's
own Material editor and under Poiyomi's alike, and every shader that names
`DocMe.SimpleShaderGUI` gets it without any change to the shader.

- **Your own drawers keep working.** A custom decorator (such as a button that
  bakes a texture) draws with Unity's classic code in its own strip right above
  the property, which keeps its new field. A property with a custom property
  drawer is drawn whole by Unity's classic code inside the new inspector. Either
  way the drawer works exactly as before, including anything it does while it draws.
- **Fallback picture for VRChat.** `[BakeFallback]` on a texture property (usually
  `_MainTex`) adds a **Bake Fallback Picture** button. It renders the material on a
  flat quad that spans its UV square and saves `<Material>_Fallback.png` next to the
  material with a transparent background, then puts it into that property: with the
  shader's `"VRCFallback"="UnlitCutout"` tag, players who block custom shaders see it
  cut out at the same size as the real shader. While it renders, the global
  float `_DocMeBaking` is 1: a shader that supports baking freezes its animations then
  and draws a soft outline. Only shaders that carry the attribute show the button.
- **Undo and multi select** behave as in Unity's inspector: every change goes
  through the same material properties Unity hands to the inspector. Selecting
  several materials shows differing values as mixed, and a change writes all of them.
- **Animation window.** While the Animation window previews or records and the
  material is shown under a renderer, the older inspector draws instead, so the
  animated values, the keyed field colours and the right click Add Key menu are
  Unity's own. The new inspector comes back when the preview ends.
- **Read only materials** (in a package, inside a model, locked by version control)
  stay read only. Where Unity disables the whole inspector for such a material,
  the older inspector draws instead, so its sections still open.
- **Material variants.** A property the parent material locked (Lock in children)
  is read only in the variant.
- **Right click menus.** Right click a property's name for Copy, Paste and Reset
  to default, or a section header for Copy section, Paste section, Reset section
  to defaults, Expand all sections and Collapse all sections. Copy puts readable
  text on the clipboard: the shader's name and each property's name, type and
  value. Paste is greyed out unless the clipboard holds a value that fits: Float
  and Range values paste into each other; Int, colour, vector and texture values
  only into their own type. Paste section writes
  every property the clipboard holds that the section has, matched by name and
  type, and skips the rest, so values also move between materials of different
  shaders that share property names. Reset uses the shader's defaults. Each action
  is one undo step named after it, and switches keywords as typing would. On a
  read only material only Copy and the fold items work. A text box keeps its own
  Cut, Copy and Paste menu.
- **Changed marks.** A small green bar in the left margin marks every property
  whose value differs from the shader's default, and every section header with
  such a property inside. A texture counts as changed when a texture is set, or
  when its Tiling or Offset differ. The bar's tooltip says "Changed from the
  shader's default".
- **Fold states carry over.** Sections and link boxes remember open or closed
  per user under the same keys as before.
- **Classic inspector.** The switch at the bottom of the inspector, below Render
  Queue, GPU Instancing and Double Sided Global Illumination, goes back to the
  older inspector for every DocMe shader material. The older inspector shows the
  same switch at its bottom to come back.
- **Automatic fallback.** Where the new inspector cannot be placed (a tool window
  that draws a material itself, a future Unity version that changed the way it is
  placed, or another tool's error that keeps it from being placed in time), the
  older inspector draws instead, so a material always shows. If the new inspector
  fails while it shows, the older inspector takes over for that material, with
  one line in the Console. A custom drawer that throws while it draws is left out,
  also with one line, and the rest of the inspector keeps working.
- **Both inspectors** read the same layout: `[ShowIf]` and `[SectionToggle]` work
  in the older inspector too. The right click menus and the changed marks are in
  the new inspector only.

## Example

`Example/ExampleShader.shader` with `Example/ExampleConfig.asset` shows every
feature together. Assign the shader to a material and open it.

## Files

The code compiles in its own assemblies: `DocMe.ShaderUI` (runtime, from
`DocMe.ShaderUI.asmdef`) and `DocMe.ShaderUI.Editor` (from `Editor/DocMe.ShaderUI.Editor.asmdef`).
Code added under this folder cannot use types from
Assembly-CSharp or Assembly-CSharp-Editor (scripts outside an assembly definition);
the other way round works, since both assemblies are referenced automatically.

- `SimpleShaderConfig.cs`: config ScriptableObject (banner, links, section links).
- `Editor/SimpleShaderGUI.cs`: the ShaderGUI the shaders name; it places the view, and draws the classic inspector when the view cannot show.
- `DocMe.ShaderUI.asmdef`, `Editor/DocMe.ShaderUI.Editor.asmdef`: the two assemblies.
- `Editor/AssemblyInfo.cs`: lets a test assembly see the internal classes.
- `Editor/ShaderInspectorMount.cs`: places one view per material editor and removes it again.
- `Editor/ShaderImportWatcher.cs`: rebuilds an open inspector when its shader file is reimported.
- `Editor/ShaderInspectorView.cs`: the UI Toolkit inspector (banner, search, sections, link boxes, footer).
- `Editor/ShaderPropertyFields.cs`: one field per property type and attribute, plus the bridge for custom drawers.
- `Editor/ShaderLayout.cs`: reads the attributes above; both inspectors use it.
- `Editor/ShaderValue.cs`: property values and shader defaults for Copy, Paste, Reset and the changed marks, and the clipboard text.
- `Editor/ShaderUiServices.cs`: fold states and the Classic switch, the clipboard, config lookup, icons, links.
- `Editor/ShaderInspector.uss`: the look.
- `Editor/SimpleShaderConfigEditor.cs`: config Inspector with wiring helpers.
- `Editor/SectionNameDrawer.cs`: section-name dropdown for config fields.

## How this was made

The code in this repository was written by AI (Claude, by Anthropic). The idea, the design, every feature, the testing on real shaders and the decisions on how it should behave are mine (DocMe). I planned it and directed the work; the AI typed it out. Bug reports and pull requests are welcome, and anything that gets merged goes through my own review.

## License

MIT. See `LICENSE.md`.
