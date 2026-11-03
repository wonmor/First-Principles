// First Principles 3D: vertex-coloured, two-sided, half-Lambert lit surfaces with optional
// level curves (contour lines every _Contour units of height, world Y = math z).
Shader "FirstPrinciples/Space3DLit"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _LightDir ("Light direction", Vector) = (0.35, 0.9, -0.45, 0)
        _Ambient ("Ambient", Range(0,1)) = 0.5
        _Contour ("Contour step", Float) = 1
        _ContourAlpha ("Contour strength", Range(0,1)) = 0
        _ContourColor ("Contour colour", Color) = (0.08,0.08,0.08,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _Color;
            float4 _LightDir;
            float _Ambient;
            float _Contour;
            float _ContourAlpha;
            fixed4 _ContourColor;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float3 wn : TEXCOORD0; float3 wp : TEXCOORD1; fixed4 color : COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                float3 n = normalize(i.wn) * (facing > 0 ? 1.0 : -1.0);
                float3 l = normalize(_LightDir.xyz);
                float d = dot(n, l) * 0.5 + 0.5;
                float3 col = i.color.rgb * _Color.rgb * (_Ambient + (1.0 - _Ambient) * d);
                if (_ContourAlpha > 0.001)
                {
                    float h = i.wp.y / max(_Contour, 1e-3);
                    float w = max(fwidth(h), 1e-4);
                    float dist = abs(frac(h + 0.5) - 0.5) / w;
                    float lineMask = 1.0 - saturate(dist - 0.35);
                    col = lerp(col, _ContourColor.rgb, lineMask * _ContourAlpha);
                }
                return fixed4(col, _Color.a * i.color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
