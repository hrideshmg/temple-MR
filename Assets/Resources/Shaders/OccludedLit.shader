// URP Lit (base map, colour, metallic, smoothness) that hides behind real-world geometry using Meta's
// Depth API. Without HARD_OCCLUSION / SOFT_OCCLUSION enabled it renders like a plain opaque Lit material.
// DepthOcclusion swaps URP Lit materials to this shader at runtime; it lives in Resources so builds include it.
Shader "TestHands/Occluded Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.5
        _Metallic("Metallic", Range(0, 1)) = 0
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        // Biases are fractions of the distance to the camera: real surfaces must be this much closer to occlude.
        _EnvironmentDepthBias("Environment Depth Bias", Float) = 0.06
        _FloorDepthBias("Depth Bias At Floor", Float) = 0.3
        _FloorBand("Floor Band Height (m)", Float) = 0.15
        // Soft occlusion blends a halo around real objects, which swamps small pieces; they cut off hard instead.
        [Toggle] _HardOcclusion("Hard Occlusion", Float) = 0
        [Toggle] _NoOcclusion("Never Occluded", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            // Occluded pixels fade out to passthrough, so blend like Meta's occlusion samples do.
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"

            struct Attributes
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Smoothness;
                half _Metallic;
                half _Cutoff;
                half _Cull;
                float _EnvironmentDepthBias;
                float _FloorDepthBias;
                float _FloorBand;
                float _HardOcclusion;
                float _NoOcclusion;
            CBUFFER_END

            // World height of the scanned floor, set by DepthOcclusion once the room loads.
            float _OcclusionFloorHeight;

            // Things lying on the floor sit at almost the floor's own depth, so depth noise (or a scan floor
            // a few centimetres off) lets the real floor hide them. Near the floor, require real surfaces to
            // be much closer before they occlude; below the floor counts as near it.
            float OcclusionBias(float3 positionWS)
            {
                float aboveFloor = positionWS.y - _OcclusionFloorHeight;
                return lerp(_FloorDepthBias, _EnvironmentDepthBias, saturate(aboveFloor / max(_FloorBand, 1e-4)));
            }

            // Meta's CalculateEnvironmentDepthOcclusion, but choosing hard or soft per material instead of
            // per scene. Under SOFT_OCCLUSION both the raw and the preprocessed depth textures are fed, so
            // either test works; under HARD_OCCLUSION only the raw one is, so everything goes hard.
            float Visibility(float3 positionWS)
            {
            #if defined(HARD_OCCLUSION) || defined(SOFT_OCCLUSION)
                if (_NoOcclusion > 0.5)
                {
                    return 1.0;
                }
                float4 depthSpace = mul(_EnvironmentDepthReprojectionMatrices[unity_StereoEyeIndex], float4(positionWS, 1.0));
                float2 uv = (depthSpace.xy / depthSpace.w + 1.0f) * 0.5f;
                float linearSceneDepth = (1.0f / ((depthSpace.z / depthSpace.w) + _EnvironmentDepthZBufferParams.y)) * _EnvironmentDepthZBufferParams.x;
                linearSceneDepth -= OcclusionBias(positionWS) * linearSceneDepth * UNITY_NEAR_CLIP_VALUE;
              #if defined(SOFT_OCCLUSION)
                if (_HardOcclusion < 0.5)
                {
                    return CalculateEnvironmentDepthSoftOcclusion(uv, linearSceneDepth);
                }
              #endif
                return CalculateEnvironmentDepthHardOcclusion(uv, linearSceneDepth);
            #else
                return 1.0;
            #endif
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionWS = TransformObjectToWorld(input.vertex.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normal);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo.rgb;
                surface.alpha = 1;
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.occlusion = 1;
                surface.normalTS = half3(0, 0, 1);

                // Rubble renders both faces; light the inside faces with their own normal.
                float3 normalWS = normalize(input.normalWS) * IS_FRONT_VFACE(face, 1, -1);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surface);
                // Premultiplied like Meta's META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY.
                return color * Visibility(input.positionWS);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    FallBack "Universal Render Pipeline/Lit"
}
