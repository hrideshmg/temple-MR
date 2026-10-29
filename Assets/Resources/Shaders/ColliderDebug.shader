// Translucent overlay for viewing invisible colliders (RoomColliders.CollidersVisible). A 10 cm world grid
// on top of the tint makes surface heights easy to compare against the real floor and furniture.
// Lives in Resources so builds include it.
Shader "TestHands/Collider Debug"
{
    Properties
    {
        _Color("Color", Color) = (0, 1, 0.8, 0.2)
        _LineColor("Line Color", Color) = (0, 1, 0.8, 0.9)
        _GridSize("Grid Size (m)", Float) = 0.1
        _LineWidth("Line Width (m)", Float) = 0.004
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _LineColor;
                float _GridSize;
                float _LineWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.vertex.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Distance to the nearest grid line along each world axis.
                float3 toLine = abs(frac(input.positionWS / _GridSize + 0.5) - 0.5) * _GridSize;
                float onLine = step(min(toLine.x, min(toLine.y, toLine.z)), _LineWidth);
                return lerp(_Color, _LineColor, onLine);
            }
            ENDHLSL
        }
    }
}
