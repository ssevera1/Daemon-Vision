// QuestPath.shader — Glowing quest thread path in D-Space
// Quest objectives are connected by flowing, golden-glowing paths
// that guide operatives through the real world. Animated particles
// flow along the path direction.

Shader "DaemonVision/QuestPath"
{
    Properties
    {
        _Color ("Path Color", Color) = (1, 0.85, 0, 0.8)
        _GlowColor ("Glow Color", Color) = (1, 0.9, 0.3, 0.5)
        _Width ("Path Width", Float) = 0.1
        _FlowSpeed ("Flow Speed", Float) = 2.0
        _FlowDensity ("Flow Density", Float) = 10.0
        _GlowIntensity ("Glow Intensity", Range(0, 3)) = 1.5
        _AnimOffset ("Animation Offset", Float) = 0.0
        _DashLength ("Dash Length", Float) = 0.5
        _DashGap ("Dash Gap", Float) = 0.3
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+10"
            "RenderType" = "Transparent"
        }

        Blend SrcAlpha One  // Additive blending for glow
        ZWrite Off
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
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            float4 _Color;
            float4 _GlowColor;
            float _FlowSpeed;
            float _FlowDensity;
            float _GlowIntensity;
            float _AnimOffset;
            float _DashLength;
            float _DashGap;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Flow animation — particles moving along the path
                float flow = frac(i.uv.x * _FlowDensity - _Time.y * _FlowSpeed - _AnimOffset);

                // Dash pattern
                float dashPhase = frac(i.uv.x / (_DashLength + _DashGap));
                float dashMask = step(dashPhase, _DashLength / (_DashLength + _DashGap));

                // Center glow (brighter in the middle, fade at edges)
                float centerDist = abs(i.uv.y - 0.5) * 2.0;
                float centerGlow = 1.0 - centerDist;
                centerGlow = pow(centerGlow, 1.5);

                // Flowing particle brightness
                float particleBrightness = pow(flow, 3.0) * 2.0;

                // Combine
                fixed4 col = _Color;
                col.rgb *= centerGlow;
                col.rgb += _GlowColor.rgb * particleBrightness * _GlowIntensity * centerGlow;
                col.a = centerGlow * dashMask * _Color.a;

                return col * i.color;
            }
            ENDCG
        }
    }
}
