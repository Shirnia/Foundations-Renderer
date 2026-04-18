Shader "Custom/PixelateReactiveUnlit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
            CBUFFER_END

            // Global variables provided by the PixelateFeature script
            TEXTURE2D(_PaletteLABTex);
            SAMPLER(sampler_PaletteLABTex);
            TEXTURE2D(_PaletteRGBTex);
            SAMPLER(sampler_PaletteRGBTex);
            int _PaletteSize;
            float _PixelateEnabled;

            inline float3 Quantize_FastSRGBToLinear(float3 srgb)
            {
                return srgb * (srgb * (srgb * 0.305306011 + 0.682171111) + 0.012522878);
            }

            float3 Linear_to_XYZ(float3 l)
            {
                return float3(
                    dot(l, float3(0.4124564, 0.3575761, 0.1804375)),
                    dot(l, float3(0.2126729, 0.7151522, 0.0721750)),
                    dot(l, float3(0.0193339, 0.1191920, 0.9503041))
                );
            }

            float3 XYZ_to_LAB(float3 xyz)
            {
                float3 reference = float3(0.95047, 1.00000, 1.08883);
                float3 v = xyz / reference;
                float3 t = v > 0.008856452 ? pow(v, 1.0/3.0) : (7.787037 * v) + (16.0/116.0);
                return float3((116.0 * t.y) - 16.0, 500.0 * (t.x - t.y), 200.0 * (t.y - t.z));
            }
            
            float3 RGB_to_LAB(float3 rgb) {
                return XYZ_to_LAB(Linear_to_XYZ(Quantize_FastSRGBToLinear(rgb)));
            }

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                // Sample texture and multiply by color
                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                
                // Only apply quantization if the Feature is active AND we have a valid palette
                if (_PixelateEnabled > 0.5 && _PaletteSize > 0)
                {
                    float3 labCol = RGB_to_LAB(col.rgb);
                    float minDistanceSq = 1e10;
                    int closestIndex = 0;
                    float invPaletteSize = rcp((float)_PaletteSize);

                    [loop]
                    for(int j = 0; j < _PaletteSize; j++)
                    {
                        float2 paletteUv = float2((j + 0.5) * invPaletteSize, 0.5);
                        float3 labPaletteColor = SAMPLE_TEXTURE2D_LOD(_PaletteLABTex, sampler_PaletteLABTex, paletteUv, 0).rgb;
                        float3 diff = labCol - labPaletteColor;
                        float distSq = dot(diff, diff);

                        if (distSq < minDistanceSq)
                        {
                            minDistanceSq = distSq;
                            closestIndex = j;
                        }
                    }

                    float2 closestUv = float2((closestIndex + 0.5) * invPaletteSize, 0.5);
                    float3 closestRgb = SAMPLE_TEXTURE2D_LOD(_PaletteRGBTex, sampler_PaletteRGBTex, closestUv, 0).rgb;
                    return half4(closestRgb, col.a);
                }
                
                return col;
            }
            ENDHLSL
        }
    }
}
