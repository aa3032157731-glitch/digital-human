Shader "Custom/InsideSpherePanorama"
{
    Properties
    {
        _MainTex ("Panorama Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Exposure ("Exposure", Range(0, 3)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Cull Off
        Lighting Off
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Tint;
            float _Exposure;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);

                // 水平翻转，避免人在球内看到的方向反了
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv.x = 1.0 - o.uv.x;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                col.rgb *= _Tint.rgb * _Exposure;
                return col;
            }

            ENDCG
        }
    }

    FallBack "Unlit/Texture"
}