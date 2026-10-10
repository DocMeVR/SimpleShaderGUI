// Example shader for the Simple Shader UI framework.
// Open a material using it to see the generated inspector.
Shader "DocMe/Example/Showcase"
{
    Properties
    {
        [HideInInspector][Config(ExampleConfig)] _Config ("", Float) = 0

        [Foldable(Colors, open)]
        [Tip(The main surface tint)]
        _Color ("Base Color", Color) = (1, 0.6, 0.2, 1)
        [Tip(Shows the emission colour below)] [ToggleUI]
        _UseEmission ("Use Emission", Float) = 0
        [ShowIf(_UseEmission)]
        _Emission ("Emission", Color) = (0, 0, 0, 1)

        [Category(Surface)]
        [Foldable(Main)]
        [Help(Smoothness controls how glossy the surface looks.)]
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0

        [Foldable(Advanced)]
        [SectionToggle] [Tip(Cuts away the see through parts of the surface)] [ToggleUI]
        _UseCutoff ("Alpha Cutout", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(Hard,0,Soft,1)] _CutoffEdge ("Cutout Edge", Float) = 0
        [ShowIf(_CutoffEdge, 1)]
        _CutoffSoftness ("Edge Softness", Range(0,1)) = 0.1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };

            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
    CustomEditor "DocMe.SimpleShaderGUI"
}
