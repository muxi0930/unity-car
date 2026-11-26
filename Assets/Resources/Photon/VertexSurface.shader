Shader "Photon Fox/Vertex Surface"
{
    Properties
    {
        _BaseMap("Texture", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.1
        _Metallic("Metallic", Range(0,1)) = 0
        _Unlit("Unlit", Float) = 0
        _Cull("Cull", Float) = 2
        _SrcBlend("Source blend", Float) = 1
        _DstBlend("Destination blend", Float) = 0
        _ZWrite("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull [_Cull]
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Smoothness, _Metallic, _Unlit;
            CBUFFER_END

            struct Attributes
            {
                float3 position : POSITION;
                half3 normal : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 clip : SV_POSITION;
                float3 world : TEXCOORD0;
                half3 normal : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fog : TEXCOORD3;
                half4 color : COLOR;
            };
            Varyings Vertex(Attributes v)
            {
                VertexPositionInputs p = GetVertexPositionInputs(v.position);
                Varyings o;
                o.clip=p.positionCS;
                o.world=p.positionWS;
                o.normal=TransformObjectToWorldNormal(v.normal);
                o.uv=v.uv;
                o.fog=ComputeFogFactor(p.positionCS.z);
                o.color=v.color;
                return o;
            }
            half4 Fragment(Varyings i) : SV_Target
            {
                half4 base=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*_BaseColor*i.color;
                if (_Unlit>0.5) return half4(MixFog(base.rgb,i.fog),base.a);
                half3 n=normalize(i.normal);
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
                half direct=saturate(dot(n,sun.direction))*sun.shadowAttenuation;
                half3 ambient=max(SampleSH(n),half3(.13,.13,.13));
                half3 reflection=SafeNormalize(sun.direction+GetWorldSpaceNormalizeViewDir(i.world));
                half highlight=pow(saturate(dot(n,reflection)),lerp(6,100,_Smoothness))*_Smoothness*direct;
                half3 lit=base.rgb*(ambient+direct*sun.color*.75)+highlight*lerp(.06,base.rgb,_Metallic);
                return half4(MixFog(lit,i.fog),base.a);
            }
            ENDHLSL
        }
    }
}
