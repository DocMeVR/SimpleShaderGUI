# Simple Shader UI

A lightweight, attribute-driven custom material inspector for Unity built-in
pipeline shaders. Group properties into collapsible sections, add tooltips and
help boxes, and attach a banner plus links, all from the shader's `Properties`
block and an optional config asset. No per-shader inspector code.

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
| `[Foldable(Title)]` / `[Foldable(Title, closed)]` | Start a collapsible section on the next property |
| `[Category(Title)]` | Bold heading above a section |
| `[Tip(text)]` | Tooltip on a property |
| `[Help(text)]` | Info box above a property |
| `[Config(AssetName)]` | Name a Shader UI Config asset for the banner and links |

Attribute arguments accept plain text only (a ShaderLab limitation), so URLs,
paths, and icons live in the config asset and are edited in its Inspector.

## Files

- `SimpleShaderConfig.cs`: config ScriptableObject (banner, links, section links).
- `Editor/SimpleShaderGUI.cs`: the material inspector.
- `Editor/SimpleShaderConfigEditor.cs`: config Inspector with wiring helpers.
- `Editor/SectionNameDrawer.cs`: section-name dropdown for config fields.
