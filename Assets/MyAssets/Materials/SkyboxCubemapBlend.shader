Shader "Custom/SkyboxCubemapBlend"
{
    Properties
    {
        _CubeA("Cubemap A", Cube) = "" {}
        _CubeB("Cubemap B", Cube) = "" {}

        _RotationA("Rotation A", Range(0, 360)) = 0
        _RotationB("Rotation B", Range(0, 360)) = 0

        _Blend("Blend", Range(0, 1)) = 0

        _Tint("Tint", Color) = (1,1,1,1)
        _Exposure("Exposure", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Background"
            "RenderType"="Background"
            "PreviewType"="Skybox"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            samplerCUBE _CubeA;
            samplerCUBE _CubeB;

            float4 _CubeA_HDR;
            float4 _CubeB_HDR;

            float _RotationA;
            float _RotationB;
            float _Blend;

            float4 _Tint;
            float _Exposure;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            float3 RotateAroundY(float3 dir, float degree)
            {
                float rad = radians(degree);

                float s = sin(rad);
                float c = cos(rad);

                float x = dir.x * c - dir.z * s;
                float z = dir.x * s + dir.z * c;

                return float3(x, dir.y, z);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 dirA = RotateAroundY(i.dir, _RotationA);
                float3 dirB = RotateAroundY(i.dir, _RotationB);

                half4 rawA = texCUBE(_CubeA, dirA);
                half4 rawB = texCUBE(_CubeB, dirB);

                half3 colA = DecodeHDR(rawA, _CubeA_HDR);
                half3 colB = DecodeHDR(rawB, _CubeB_HDR);

                half3 col = lerp(colA, colB, saturate(_Blend));

                col *= _Tint.rgb;
                col *= _Exposure;

                return half4(col, 1);
            }

            ENDCG
        }
    }

    FallBack Off
}