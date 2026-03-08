// DSpaceGrid.shader — The D-Space spatial grid overlay
// A subtle grid that shows the GPS coordinate system mapped to the real world.
// Becomes visible when the operative is in "scan mode" or near anchor points.

Shader "DaemonVision/DSpaceGrid"
{
    Properties
    {
        _GridColor ("Grid Color", Color) = (0, 0.75, 1, 0.15)
        _GridSize ("Grid Size", Float) = 1.0
        _LineWidth ("Line Width", Range(0.001, 0.05)) = 0.01
        _FadeDistance ("Fade Distance", Float) = 20.0
        _PulseSpeed ("Pulse Speed", Float) = 0.5
        _Opacity ("Opacity", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-10"
            "RenderType" = "Transparent"
        }

        Blend SrcAlpha OneMinusSrcAlpha
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
                float3 worldPos : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            float4 _GridColor;
            float _GridSize;
            float _LineWidth;
            float _FadeDistance;
            float _PulseSpeed;
            float _Opacity;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 worldUV = i.worldPos.xz / _GridSize;

                // Grid lines
                float2 grid = abs(frac(worldUV) - 0.5);
                float2 lineWidth = _LineWidth / _GridSize;
                float2 lines = smoothstep(lineWidth, lineWidth * 0.5, grid);
                float gridMask = max(lines.x, lines.y);

                // Distance fade
                float dist = length(i.worldPos - _WorldSpaceCameraPos);
                float fade = 1.0 - saturate(dist / _FadeDistance);

                // Subtle pulse
                float pulse = 0.8 + 0.2 * sin(_Time.y * _PulseSpeed);

                fixed4 col = _GridColor;
                col.a = gridMask * fade * _Opacity * pulse;

                return col;
            }
            ENDCG
        }
    }
}
