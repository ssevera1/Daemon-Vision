// ThreatOutline.shader — Red pulsing outline for hostile targets
// In the Daemon, hostiles glow red. This shader creates a pulsing
// outline effect around threat-flagged targets in D-Space.

Shader "DaemonVision/ThreatOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 0, 0, 0.8)
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.02
        _PulseSpeed ("Pulse Speed", Float) = 2.0
        _PulseMin ("Pulse Min Intensity", Range(0, 1)) = 0.3
        _PulseMax ("Pulse Max Intensity", Range(0, 2)) = 1.5
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" }

        // Pass 1: Outline (extruded along normals)
        Pass
        {
            Name "OUTLINE"
            Cull Front
            ZWrite Off
            ZTest Always  // Visible through walls (D-Space threat vision)
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            float4 _OutlineColor;
            float _OutlineWidth;
            float _PulseSpeed;
            float _PulseMin;
            float _PulseMax;

            v2f vert(appdata v)
            {
                v2f o;
                // Extrude along normal for outline
                float3 norm = normalize(v.normal);
                v.vertex.xyz += norm * _OutlineWidth;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Pulsing intensity
                float pulse = lerp(_PulseMin, _PulseMax,
                    (sin(_Time.y * _PulseSpeed) * 0.5 + 0.5));

                fixed4 col = _OutlineColor;
                col.rgb *= pulse;
                col.a = _OutlineColor.a * pulse;
                return col;
            }
            ENDCG
        }
    }

    FallBack Off
}
