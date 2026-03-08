// NameplateShader.shader — Floating nameplate rendering
// Always faces camera, with glow border and threat-level color coding.

Shader "DaemonVision/Nameplate"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Background Color", Color) = (0.05, 0.05, 0.1, 0.75)
        _BorderColor ("Border Color", Color) = (0, 0.75, 1, 0.9)
        _BorderWidth ("Border Width", Range(0, 0.1)) = 0.02
        _GlowColor ("Glow Color", Color) = (0, 0.75, 1, 0.5)
        _GlowIntensity ("Glow Intensity", Range(0, 3)) = 1.0
        _CornerRadius ("Corner Radius", Range(0, 0.5)) = 0.05
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always  // Render on top of everything (HUD element)
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _Color;
            float4 _BorderColor;
            float _BorderWidth;
            float4 _GlowColor;
            float _GlowIntensity;
            float _CornerRadius;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float roundedBox(float2 p, float2 b, float r)
            {
                float2 d = abs(p) - b + r;
                return length(max(d, 0.0)) - r;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv - 0.5; // Center coordinates
                float2 size = float2(0.5, 0.5);

                // Rounded rectangle SDF
                float dist = roundedBox(uv, size - _BorderWidth, _CornerRadius);

                // Background fill
                float bgMask = 1.0 - smoothstep(-0.001, 0.001, dist);
                fixed4 col = _Color * bgMask;

                // Border
                float borderDist = abs(dist) - _BorderWidth;
                float borderMask = 1.0 - smoothstep(-0.002, 0.002, borderDist);
                col = lerp(col, _BorderColor, borderMask);

                // Outer glow
                float glowDist = dist;
                float glow = exp(-glowDist * 20.0) * _GlowIntensity;
                col.rgb += _GlowColor.rgb * glow;

                // Clip outside rounded rect (with glow margin)
                float outerMask = 1.0 - smoothstep(0.0, 0.1, dist);
                col.a *= outerMask;

                return col;
            }
            ENDCG
        }
    }
}
