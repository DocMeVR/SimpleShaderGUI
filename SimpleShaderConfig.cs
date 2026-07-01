using System;
using System.Collections.Generic;
using UnityEngine;

namespace DocMe
{
    // Branding and link data for a shader inspector, edited in the Inspector (drag a banner
    // texture, add links to the lists). A shader points at one of these by name with the
    // [Config(AssetName)] attribute. Create via Assets > Create > DocMe > Shader UI Config.
    [CreateAssetMenu(fileName = "New Shader UI Config", menuName = "DocMe/Shader UI Config")]
    public class SimpleShaderConfig : ScriptableObject
    {
        [Serializable]
        public class Link
        {
            public string label;
            public string url;
            [Tooltip("Optional. Drag an icon texture, or leave empty for text only.")]
            public Texture2D icon;
            [Tooltip("Optional built-in editor icon name (e.g. web). Used only if Icon is empty.")]
            public string builtinIcon;
        }

        public enum LinkPlacement { Bottom, Top }

        [Serializable]
        public class LinkGroup
        {
            public string title = "Links";
            public bool startCollapsed;
            [Tooltip("Fallback position, used only when After Section is empty.")]
            public LinkPlacement placement = LinkPlacement.Bottom;
            [SectionName]
            [Tooltip("Optional. Places this box right after the chosen section, overriding Top/Bottom.")]
            public string afterSection;
            public List<Link> links = new List<Link>();
        }

        [Serializable]
        public class SectionLink
        {
            [SectionName]
            [Tooltip("The [Foldable(...)] section title to pin this link icon to.")]
            public string section;
            public string url;
            [Tooltip("Optional. Drag an icon texture; defaults to a web/globe icon.")]
            public Texture2D icon;
            public string builtinIcon = "web";
        }

        public enum BannerFit { Fit, Stretch, Crop }

        [Header("Banner")]
        public Texture2D banner;
        [Tooltip("Optional. Opens when the banner image itself is clicked.")]
        public string bannerUrl;
        [Tooltip("Fit keeps aspect (letterbox). Stretch fills width and height. Crop fills and crops to the height.")]
        public BannerFit bannerFit = BannerFit.Fit;
        [Tooltip("Banner height in pixels, used by Stretch and Crop.")]
        public float bannerHeight = 80f;

        [Header("Banner Buttons")]
        [Tooltip("Max icon size (width, height) for the banner link icons.")]
        public Vector2 bannerIconSize = new Vector2(16f, 16f);
        public List<Link> bannerLinks = new List<Link>();

        [Header("Link Sections")]
        public List<LinkGroup> linkBoxes = new List<LinkGroup>();

        [Header("Section Header Links")]
        public List<SectionLink> sectionLinks = new List<SectionLink>();
    }

    // Marks a string field as a shader section title, so the config Inspector can offer
    // a dropdown of the [Foldable(...)] sections found in the assigned shader.
    public class SectionNameAttribute : PropertyAttribute { }
}
