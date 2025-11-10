Shader "Custom/SpotlightBackground"
{
    Properties
    {
        _CenterColor ("Center Color", Color) = (1, 1, 1, 1)
        _EdgeColor ("Edge Color", Color) = (0, 0, 0, 1)
        _CenterPosition ("Center Position", Vector) = (0.5, 0.5, 0, 0)
        _Radius ("Light Radius", Range(0, 2)) = 1.0
        _Falloff ("Falloff", Range(0.1, 10)) = 2.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" }
        LOD 100

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

            fixed4 _CenterColor;
            fixed4 _EdgeColor;
            float4 _CenterPosition;
            float _Radius;
            float _Falloff;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 center = _CenterPosition.xy;
                float2 uv = i.uv;
                
                float dist = distance(uv, center);
                float normalizedDist = dist / _Radius;
                
                float falloffFactor = pow(saturate(1.0 - normalizedDist), _Falloff);
                
                fixed4 col = lerp(_EdgeColor, _CenterColor, falloffFactor);
                
                return col;
            }
            ENDCG
        }
    }
}

