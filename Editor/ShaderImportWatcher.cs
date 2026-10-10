using System;
using System.Collections.Generic;
using UnityEditor;

namespace DocMe
{
    /// <summary>Rebuilds the shader inspector of a material whose shader file was just reimported, so new attributes show at once.</summary>
    internal sealed class ShaderImportWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (ShaderInspectorMount.LiveViewCount == 0)
                return;
            List<string> shaders = null;
            foreach (var path in importedAssets)
                if (path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                    (shaders ??= new List<string>()).Add(path);
            if (shaders != null)
                ShaderInspectorMount.ShadersChanged(shaders);
        }
    }
}
