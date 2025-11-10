Shader "Custom/ShadowShader"
{
    Properties
    {
        _Color ("Shadow Color", Color) = (0, 0, 0, 1)
        _Intensity ("Shadow Intensity", Range(0, 1)) = 1.0
        _Softness ("Edge Softness", Range(0.1, 5)) = 2.0
        _EdgeBlur ("Edge Blur", Range(0, 1)) = 0.5
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        
        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On
            
            CGPROGRAM
            #pragma vertex vert_outline
            #pragma fragment frag_outline
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

            fixed4 _Color;
            float _OutlineWidth;

            v2f vert_outline (appdata v)
            {
                v2f o;
                float3 normal = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, v.normal));
                float2 offset = TransformViewToProjection(normal.xy);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.vertex.xy += offset * _OutlineWidth * o.vertex.w;
                return o;
            }

            fixed4 frag_outline (v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }

        Pass
        {
            Name "Main"
            Cull Off
            ZWrite On

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
                float3 worldNormal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            fixed4 _Color;
            float _Intensity;
            float _Softness;
            float _EdgeBlur;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = WorldSpaceViewDir(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 viewDir = normalize(i.viewDir);
                
                float fresnel = 1.0 - abs(dot(normal, viewDir));
                fresnel = pow(fresnel, 1.0 / _Softness);
                
                float edgeFactor = smoothstep(_EdgeBlur, 1.0, fresnel);
                float intensity = lerp(_Intensity * 0.3, _Intensity, edgeFactor);
                
                fixed4 col = _Color * intensity;
                col.a = 1.0;
                
                return col;
            }
            ENDCG
        }
    }
}

