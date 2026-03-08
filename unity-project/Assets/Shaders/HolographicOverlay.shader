// HolographicOverlay.shader — The signature D-Space holographic look
// Translucent, glowing panels with scan-line effects and edge glow.
// This is the visual language of the Daemon's AR overlay.

Shader "DaemonVision/HolographicOverlay"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (0, 0.75, 1, 0.7)
        _EdgeColor ("Edge Color", Color) = (0, 1, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.7
        _ScanLineSpeed ("Scan Line Speed", Float) = 1.0
        _ScanLineDensity ("Scan Line Density", Float) = 100.0
        _ScanLineIntensity ("Scan Line Intensity", Range(0, 1)) = 0.15
        _EdgeGlow ("Edge Glow Width", Range(0, 0.5)) = 0.05
        _Fresnel ("Fresnel Power", Range(0, 5)) = 2.0
        _GlitchIntensity ("Glitch Intensity", Range(0, 1)) = 0.02
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        LOD 200
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 worldNormal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                UNITY_FOG_COORDS(4)
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float4 _EdgeColor;
            float _Opacity;
            float _ScanLineSpeed;
            float _ScanLineDensity;
            float _ScanLineIntensity;
            float _EdgeGlow;
            float _Fresnel;
            float _GlitchIntensity;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(WorldSpaceViewDir(v.vertex));
                o.screenPos = ComputeScreenPos(o.vertex);
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            float random(float2 st)
            {
                return frac(sin(dot(st, float2(12.9898, 78.233))) * 43758.5453);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Base texture and color
                fixed4 tex = tex2D(_MainTex, i.uv);
                fixed4 col = tex * _Color;

                // Fresnel edge glow — brighter at glancing angles
                float fresnel = pow(1.0 - saturate(dot(i.worldNormal, i.viewDir)), _Fresnel);
                col.rgb += _EdgeColor.rgb * fresnel * _EdgeGlow * 5.0;

                // Scan lines — horizontal lines scrolling down
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float scanLine = sin((screenUV.y * _ScanLineDensity + _Time.y * _ScanLineSpeed) * 3.14159) * 0.5 + 0.5;
                col.rgb -= _ScanLineIntensity * (1.0 - scanLine);

                // Subtle glitch effect
                float glitch = step(0.99 - _GlitchIntensity, random(float2(_Time.y * 0.1, screenUV.y)));
                col.rgb += glitch * 0.3;

                // Final opacity
                col.a = _Opacity * _Color.a * (0.7 + fresnel * 0.3);

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }

    FallBack "Transparent/Diffuse"
}
