using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DocMe
{
    /// <summary>One property's value as the material holds it or as the shader declares it, for Copy, Paste, Reset and the changed marks.</summary>
    internal struct ShaderValue
    {
        public const float Tolerance = 1e-4f;
        public static readonly Vector4 DefaultScaleOffset = new Vector4(1f, 1f, 0f, 0f);

        public ShaderPropertyType type;
        public float number;
        public int integer;
        public Vector4 vector;
        public Texture texture;
        public Vector4 scaleOffset;

        public static ShaderValue Read(MaterialProperty property)
        {
            var value = new ShaderValue { type = TypeOf(property.type) };
            switch (value.type)
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    value.number = property.floatValue;
                    break;
                case ShaderPropertyType.Int:
                    value.integer = property.intValue;
                    break;
                case ShaderPropertyType.Color:
                    value.vector = property.colorValue;
                    break;
                case ShaderPropertyType.Vector:
                    value.vector = property.vectorValue;
                    break;
                case ShaderPropertyType.Texture:
                    value.texture = property.textureValue;
                    value.scaleOffset = property.textureScaleAndOffset;
                    break;
            }
            return value;
        }

        /// <summary>The value a new material gets: the Properties block's default, or the texture set in the shader's import settings with Tiling (1, 1) and Offset (0, 0).</summary>
        public static ShaderValue DefaultOf(Shader shader, int index)
        {
            var value = new ShaderValue { type = shader.GetPropertyType(index) };
            switch (value.type)
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    value.number = shader.GetPropertyDefaultFloatValue(index);
                    break;
                case ShaderPropertyType.Int:
                    value.integer = shader.GetPropertyDefaultIntValue(index);
                    break;
                case ShaderPropertyType.Color:
                case ShaderPropertyType.Vector:
                    value.vector = shader.GetPropertyDefaultVectorValue(index);
                    break;
                case ShaderPropertyType.Texture:
                    var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(shader)) as ShaderImporter;
                    value.texture = importer != null ? importer.GetDefaultTexture(shader.GetPropertyName(index)) : null;
                    value.scaleOffset = DefaultScaleOffset;
                    break;
            }
            return value;
        }

        public static ShaderPropertyType TypeOf(MaterialProperty.PropType type)
        {
            switch (type)
            {
                case MaterialProperty.PropType.Color: return ShaderPropertyType.Color;
                case MaterialProperty.PropType.Vector: return ShaderPropertyType.Vector;
                case MaterialProperty.PropType.Range: return ShaderPropertyType.Range;
                case MaterialProperty.PropType.Texture: return ShaderPropertyType.Texture;
                case MaterialProperty.PropType.Int: return ShaderPropertyType.Int;
                default: return ShaderPropertyType.Float;
            }
        }

        /// <summary>Float and Range hold the same kind of value; Int, Color, Vector and Texture each their own.</summary>
        public static bool SameKind(ShaderPropertyType a, ShaderPropertyType b) => Kind(a) == Kind(b);

        static ShaderPropertyType Kind(ShaderPropertyType type) => type == ShaderPropertyType.Range ? ShaderPropertyType.Float : type;

        public bool Matches(ShaderValue other, bool withScaleOffset)
        {
            switch (type)
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    return Close(number, other.number);
                case ShaderPropertyType.Int:
                    return integer == other.integer;
                case ShaderPropertyType.Color:
                case ShaderPropertyType.Vector:
                    return Close(vector, other.vector);
                case ShaderPropertyType.Texture:
                    return texture == other.texture && (!withScaleOffset || Close(scaleOffset, other.scaleOffset));
                default:
                    return true;
            }
        }

        static bool Close(float a, float b) => Mathf.Abs(a - b) <= Tolerance * Mathf.Max(1f, Mathf.Abs(b));

        static bool Close(Vector4 a, Vector4 b) => Close(a.x, b.x) && Close(a.y, b.y) && Close(a.z, b.z) && Close(a.w, b.w);

        public string Text()
        {
            switch (type)
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    return number.ToString("R", CultureInfo.InvariantCulture);
                case ShaderPropertyType.Int:
                    return integer.ToString(CultureInfo.InvariantCulture);
                case ShaderPropertyType.Color:
                case ShaderPropertyType.Vector:
                    return VectorText(vector);
                case ShaderPropertyType.Texture:
                    return TextureText(texture);
                default:
                    return "";
            }
        }

        public static string VectorText(Vector4 v)
            => string.Join(", ", v.x.ToString("R", CultureInfo.InvariantCulture), v.y.ToString("R", CultureInfo.InvariantCulture),
                v.z.ToString("R", CultureInfo.InvariantCulture), v.w.ToString("R", CultureInfo.InvariantCulture));

        /// <summary>A texture asset by its path; one that is not an asset by its object id, which only means something in this editor session.</summary>
        static string TextureText(Texture texture)
        {
            if (texture == null)
                return "";
            string path = AssetDatabase.GetAssetPath(texture);
            if (!string.IsNullOrEmpty(path) && AssetDatabase.LoadAssetAtPath<Texture>(path) == texture)
                return path;
            return "instance:" + texture.GetInstanceID().ToString(CultureInfo.InvariantCulture);
        }

        public static bool TryParse(ShaderPropertyType type, string text, string scaleOffset, out ShaderValue value)
        {
            value = new ShaderValue { type = type, scaleOffset = DefaultScaleOffset };
            text ??= "";
            switch (type)
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value.number);
                case ShaderPropertyType.Int:
                    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value.integer);
                case ShaderPropertyType.Color:
                case ShaderPropertyType.Vector:
                    return TryParseVector(text, out value.vector);
                case ShaderPropertyType.Texture:
                    if (!string.IsNullOrEmpty(scaleOffset) && !TryParseVector(scaleOffset, out value.scaleOffset))
                        return false;
                    return TryParseTexture(text, out value.texture);
                default:
                    return false;
            }
        }

        static bool TryParseVector(string text, out Vector4 vector)
        {
            vector = Vector4.zero;
            var parts = text.Split(',');
            if (parts.Length < 1 || parts.Length > 4)
                return false;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float component))
                    return false;
                vector[i] = component;
            }
            return true;
        }

        static bool TryParseTexture(string text, out Texture texture)
        {
            texture = null;
            if (text.Length == 0)
                return true;
            const string instance = "instance:";
            if (text.StartsWith(instance, StringComparison.Ordinal))
            {
                if (!int.TryParse(text.Substring(instance.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                    return false;
                texture = EditorUtility.InstanceIDToObject(id) as Texture;
                return texture != null;
            }
            texture = AssetDatabase.LoadAssetAtPath<Texture>(text);
            return texture != null;
        }
    }

    [Serializable]
    internal sealed class ShaderClipboardData
    {
        public string shader;
        public List<ShaderClipboardEntry> properties = new List<ShaderClipboardEntry>();
    }

    [Serializable]
    internal sealed class ShaderClipboardEntry
    {
        public string name;
        public string type;
        public string value;
        public string scaleOffset;
    }

    /// <summary>The text Copy puts on the clipboard: readable JSON with the shader's name and each property's name, type and value.</summary>
    internal static class ShaderClipboard
    {
        public static string Write(string shaderName, IEnumerable<(string name, ShaderValue value)> entries)
        {
            var text = new StringBuilder();
            text.Append("{\n    \"shader\": ").Append(Quote(shaderName)).Append(",\n    \"properties\": [");
            bool first = true;
            foreach (var (name, value) in entries)
            {
                text.Append(first ? "\n" : ",\n");
                first = false;
                text.Append("        { \"name\": ").Append(Quote(name))
                    .Append(", \"type\": ").Append(Quote(value.type.ToString()))
                    .Append(", \"value\": ").Append(Quote(value.Text()));
                if (value.type == ShaderPropertyType.Texture)
                    text.Append(", \"scaleOffset\": ").Append(Quote(ShaderValue.VectorText(value.scaleOffset)));
                text.Append(" }");
            }
            text.Append(first ? "]\n}" : "\n    ]\n}");
            return text.ToString();
        }

        /// <summary>The copied properties, or null when the text is not something Copy wrote.</summary>
        public static ShaderClipboardData Read(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.TrimStart()[0] != '{')
                return null;
            try
            {
                var data = JsonUtility.FromJson<ShaderClipboardData>(text);
                if (data == null || string.IsNullOrEmpty(data.shader) || data.properties == null)
                    return null;
                return data;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool TryRead(ShaderClipboardEntry entry, out ShaderValue value)
        {
            value = default;
            if (entry == null || string.IsNullOrEmpty(entry.name) || !Enum.TryParse(entry.type, out ShaderPropertyType type))
                return false;
            return ShaderValue.TryParse(type, entry.value, entry.scaleOffset, out value);
        }

        static string Quote(string text)
        {
            var quoted = new StringBuilder("\"");
            foreach (char c in text ?? "")
            {
                switch (c)
                {
                    case '"': quoted.Append("\\\""); break;
                    case '\\': quoted.Append("\\\\"); break;
                    case '\n': quoted.Append("\\n"); break;
                    case '\r': quoted.Append("\\r"); break;
                    case '\t': quoted.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            quoted.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            quoted.Append(c);
                        break;
                }
            }
            return quoted.Append('"').ToString();
        }
    }
}
