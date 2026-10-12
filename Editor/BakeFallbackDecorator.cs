using System.IO;
using UnityEditor;
using UnityEngine;

namespace DocMe
{
    /// <summary>
    /// <c>[BakeFallback]</c> on a texture property (usually <c>_MainTex</c>) draws a button that bakes a picture of the
    /// material with a transparent background and assigns it to that property: what VRChat's cutout fallback shows to
    /// players who block custom shaders. Only shaders that carry the attribute get the button. While baking, the global
    /// <c>_DocMeBaking</c> is 1: a shader that supports baking freezes its animations then and draws a soft outline. The
    /// material is drawn on a flat quad that spans its UV square, so the picture lines up with the mesh's UVs.
    /// </summary>
    public class BakeFallbackDecorator : MaterialPropertyDrawer
    {
        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor) => 24f;

        public override void OnGUI(Rect position, MaterialProperty prop, string label, MaterialEditor editor)
        {
            var r = new Rect(position.x, position.y + 2f, position.width, 20f);
            var content = new GUIContent("Bake Fallback Picture",
                "Makes the picture players see when they block custom shaders, at the same size as the real shader.");
            if (!GUI.Button(r, content)) return;
            foreach (Object target in editor.targets)
                if (target is Material material)
                    FallbackBaker.Bake(material, prop.name);
        }
    }

    public static class FallbackBaker
    {
        public const string BakingGlobal = "_DocMeBaking";
        const int Size = 512;
        const int Layer = 31;

        public static void Bake(Material material, string fallbackProperty)
        {
            string matPath = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(matPath))
            {
                Debug.LogWarning("Bake Fallback: save the material as an asset first.", material);
                return;
            }
            string pngPath = Path.GetDirectoryName(matPath).Replace('\\', '/') + "/" + material.name + "_Fallback.png";
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), pngPath), Render(material));

            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(pngPath) is TextureImporter importer)
            {
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            var picture = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            if (material.HasProperty(fallbackProperty))
            {
                Undo.RecordObject(material, "Bake Fallback Picture");
                material.SetTexture(fallbackProperty, picture);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
            }
            Debug.Log("Bake Fallback: made " + pngPath + ".", material);
        }

        // Straight (unpremultiplied) colour with the coverage as alpha, as PNG bytes.
        static byte[] Render(Material source)
        {
            var root = new GameObject("_DocMeFallbackBake") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.position = new Vector3(0f, -5000f, 0f);
            var mat = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture rt = null;
            Texture2D read = null, output = null;
            float baking = Shader.GetGlobalFloat(BakingGlobal);
            try
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.hideFlags = HideFlags.HideAndDontSave;
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.SetParent(root.transform, false);
                quad.layer = Layer;
                quad.GetComponent<MeshRenderer>().sharedMaterial = mat;

                var camGo = new GameObject("_DocMeFallbackCam") { hideFlags = HideFlags.HideAndDontSave };
                camGo.transform.SetParent(root.transform, false);
                camGo.transform.localPosition = new Vector3(0f, 0f, -1f);
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = 0.5f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 10f;
                cam.cullingMask = 1 << Layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.allowHDR = true;
                cam.allowMSAA = false;

                rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBHalf);
                rt.Create();
                cam.targetTexture = rt;
                Shader.SetGlobalFloat(BakingGlobal, 1f);
                cam.Render();

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                read = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
                read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                RenderTexture.active = prev;

                bool linear = PlayerSettings.colorSpace == ColorSpace.Linear;
                Color[] px = read.GetPixels();
                for (int i = 0; i < px.Length; i++)
                {
                    Color c = px[i];
                    float a = Mathf.Clamp01(c.a);
                    if (a > 1e-4f) { c.r /= a; c.g /= a; c.b /= a; }
                    if (linear) { c.r = Mathf.LinearToGammaSpace(c.r); c.g = Mathf.LinearToGammaSpace(c.g); c.b = Mathf.LinearToGammaSpace(c.b); }
                    px[i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), a);
                }
                output = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false);
                output.SetPixels(px);
                output.Apply();
                return output.EncodeToPNG();
            }
            finally
            {
                Shader.SetGlobalFloat(BakingGlobal, baking);
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (read != null) Object.DestroyImmediate(read);
                if (output != null) Object.DestroyImmediate(output);
                Object.DestroyImmediate(mat);
                Object.DestroyImmediate(root);
            }
        }
    }
}
