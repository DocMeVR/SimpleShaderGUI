using System;
using System.Collections.Generic;
using UnityEditor;

namespace DocMe
{
    /// <summary>Rebuilds the shader inspector of a material whose shader file was just reimported, so new attributes show at once.</summary>
    internal sealed class ShaderImportWatcher : AssetPostprocessor
    {
        /// <summary>Goes up whenever a shader file is imported, so the classic inspector reads that shader's layout again.</summary>
        public static int Stamp { get; private set; }

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            List<string> shaders = null;
            foreach (var path in importedAssets)
                if (path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                    (shaders ??= new List<string>()).Add(path);
            if (shaders == null)
                return;
            Stamp++;
            if (ShaderInspectorMount.LiveViewCount > 0)
                ShaderInspectorMount.ShadersChanged(shaders);
        }
    }
}
