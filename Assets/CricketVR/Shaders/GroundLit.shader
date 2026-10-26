// CricketVR ground surfaces (plan.md §15): base texture x colour x vertex colour, lit by the main directional
// light plus ambient (spherical harmonics), with optional main-light shadow receiving and URP fog.
// Cheaper than URP Simple Lit (no specular, no additional lights) and it adds:
//   - vertex colours (baked AO and crowd/board colours, GD-10), which URP Lit/Simple Lit ignore;
//   - a depth offset, so markings a few millimetres above the grass never z-fight (camera near plane is 1 cm);
//   - a per-material fog amount, so the black sight screens stay black at 80 m.
// Supports single-pass instanced stereo (Quest) and the SRP Batcher.
Shader "CricketVR/GroundLit"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _DepthOffsetFactor ("Depth Offset Factor", Float) = 0
        _DepthOffsetUnits ("Depth Offset Units", Float) = 0
        _FogAmount ("Fog Amount", Range(0, 1)) = 1
        [Toggle(_CVR_RECEIVE_SHADOWS)] _ReceiveShadows ("Receive Shadows", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Offset [_DepthOffsetFactor], [_DepthOffsetUnits]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma shader_feature_local _CVR_RECEIVE_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _DepthOffsetFactor;
                float _DepthOffsetUnits;
                half _FogAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor * input.color;
                half3 normalWS = normalize(input.normalWS);
            #if defined(_CVR_RECEIVE_SHADOWS)
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadow = mainLight.shadowAttenuation;
            #else
                Light mainLight = GetMainLight();
                half shadow = 1.0h;
            #endif
                half diffuse = saturate(dot(normalWS, mainLight.direction)) * shadow;
                half3 color = albedo.rgb * (SampleSH(normalWS) + mainLight.color * diffuse);
                // _FogAmount < 1 keeps a surface (the black sight screens) from being washed out by distance fog.
                color = MixFog(color, lerp(1.0h, input.fogFactor, _FogAmount));
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
