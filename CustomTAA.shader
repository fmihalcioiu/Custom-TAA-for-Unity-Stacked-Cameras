Shader "Custom/CustomTAA"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend One Zero

        Pass
        {
            Name "CustomTAA"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D_X(_BlitTexture);
            TEXTURE2D_X(_HistoryTex);

            CBUFFER_START(UnityPerMaterial)
                float4x4 _PrevViewProj;
                float4x4 _CurrInvViewProj;
                float _HistoryWeight;
                float _NeighborhoodClamp;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                return output;
            }

            float2 GetScreenUv(float2 uv)
            {
                #if UNITY_UV_STARTS_AT_TOP
                if (_ProjectionParams.x < 0.0)
                    uv.y = 1.0 - uv.y;
                #endif

                return uv;
            }

            float3 SampleCurrent(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            float3 SampleHistory(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_HistoryTex, sampler_LinearClamp, uv).rgb;
            }

            float3 ClampNeighborhood(float2 uv, float3 historyColor)
            {
                float2 texel = rcp(_ScreenSize.xy);

                float3 c0 = SampleCurrent(uv + texel * float2(-1, -1));
                float3 c1 = SampleCurrent(uv + texel * float2(0, -1));
                float3 c2 = SampleCurrent(uv + texel * float2(1, -1));
                float3 c3 = SampleCurrent(uv + texel * float2(-1, 0));
                float3 c4 = SampleCurrent(uv);
                float3 c5 = SampleCurrent(uv + texel * float2(1, 0));
                float3 c6 = SampleCurrent(uv + texel * float2(-1, 1));
                float3 c7 = SampleCurrent(uv + texel * float2(0, 1));
                float3 c8 = SampleCurrent(uv + texel * float2(1, 1));

                float3 minC = min(min(min(c0, c1), min(c2, c3)), min(min(c4, c5), min(c6, min(c7, c8))));
                float3 maxC = max(max(max(c0, c1), max(c2, c3)), max(max(c4, c5), max(c6, max(c7, c8))));

                float3 clamped = clamp(historyColor, minC, maxC);
                return lerp(historyColor, clamped, saturate(_NeighborhoodClamp));
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // Current frame sampling should stay in the native blit orientation.
                float2 uv = input.uv;
                float3 currentColor = SampleCurrent(uv);

                float depth = SampleSceneDepth(uv);

                #if UNITY_REVERSED_Z
                if (depth <= 0.00001f)
                    return float4(currentColor, 1);
                #else
                if (depth >= 0.99999f)
                    return float4(currentColor, 1);
                #endif

                float3 worldPos = ComputeWorldSpacePosition(uv, depth, _CurrInvViewProj);
                float4 prevClip = mul(_PrevViewProj, float4(worldPos, 1.0));

                if (prevClip.w <= 0.00001f)
                    return float4(currentColor, 1);

                float2 prevUv = (prevClip.xy / prevClip.w) * 0.5f + 0.5f;

                // Only history reprojection UV gets platform flip correction.
                #if UNITY_UV_STARTS_AT_TOP
                if (_ProjectionParams.x < 0.0)
                    prevUv.y = 1.0 - prevUv.y;
                #endif

                if (any(prevUv < 0.0.xx) || any(prevUv > 1.0.xx))
                    return float4(currentColor, 1);

                float3 historyColor = SampleHistory(prevUv);
                historyColor = ClampNeighborhood(uv, historyColor);

                float historyWeight = saturate(_HistoryWeight);
                float3 taaColor = lerp(currentColor, historyColor, historyWeight);
                return float4(taaColor, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}