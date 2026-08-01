// Upgrade NOTE: replaced 'UNITY_INSTANCE_ID' with 'UNITY_VERTEX_INPUT_INSTANCE_ID'

Shader "Hidden/ProtoSprite/Outline"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _GrabPass("Texture", 2D) = "white" {}
        _SpriteRect ("Sprite Rect", Vector) = (0, 0, 0, 0)
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcAlphaBlend("__srcAlpha", Float) = 1.0
        [HideInInspector] _DstAlphaBlend("__dstAlpha", Float) = 0.0
        [HideInInspector] _BlendOp("__blendOp", Float) = 1.0
        [HideInInspector] _AlphaBlendOp("__alphaBlendOp", Float) = 0.0
    }
    SubShader
    {
        // #0
        Pass
        {
            // No culling or depth
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
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

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _MainTex;
            sampler2D _GrabPass;
            float4 _MainTex_TexelSize;

            fixed4 frag(v2f i) : SV_Target
            {
                half4 texColor = tex2D(_MainTex, i.uv);
                half4 grabPassColor = tex2D(_GrabPass, i.uv);
                half4 outlineColor = float4(0,0,0,0);

                if (texColor.a <= 0.01)
                {
                    for (float y = -1; y <= 1; y += 1) {
                        for (float x = -1; x <= 1; x += 1) {
                            float2 offset = float2(x, y) * _MainTex_TexelSize.xy;

                            outlineColor += step(0.01,tex2D(_MainTex, i.uv + offset).a) * float4(1, 1, 1, 1);
                        }
                    }
                }

                float greyscale = dot(grabPassColor.rgb, float3(0.2989, 0.5870, 0.1140));

                float middleGrey = 127.0 / 255.0;

#if !UNITY_COLORSPACE_GAMMA
                middleGrey = pow(middleGrey, 2.2);
#endif

                float val = step(greyscale, middleGrey);

                outlineColor.a = outlineColor.r;

                outlineColor.rgb = float3(val, val, val);

                //outlineColor.a = 1.0;

                return outlineColor;
        }
        ENDCG
        }

        // #1
        Pass
        {
            // No culling or depth
            Cull Off ZWrite Off ZTest Always

            Tags { "RenderType" = "Opaque" }

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

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv);

                return col;
            }
            ENDCG
        }

        // #2 overlay sprite render
            Pass
        {
            // No culling or depth
            //Cull Off
            //ZTest Always
            //Lighting Off
            //ZWrite On


            Tags { "RenderType" = "Opaque" }

            Cull Off
            Lighting Off
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ GAMMATOLINEAR

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float4 _SpriteRect;
            float4 _BGColor1;
            float4 _BGColor2;
            float _PixelAmount;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;// TRANSFORM_TEX(v.uv, _MainTex);
                o.uv2 = v.uv2;// TRANSFORM_TEX(v.uv2, _MainTex);

                return o;
            }

            float3 GammaToLinear3(float3 value)
            {
                value.r = GammaToLinearSpaceExact(value.r);
                value.g = GammaToLinearSpaceExact(value.g);
                value.b = GammaToLinearSpaceExact(value.b);
                return value;
            }

            float4 frag(v2f i) : SV_Target
            {
                //return _Color;
                //return pow(float4(0.5,0.5,0.5,1.0), 2.2);
                //float lightGrey = 192.0 / 255.0;
                //return _BGColor1;// pow(float4(lightGrey, lightGrey, lightGrey, 1.0), 2.2);
                float4 col = tex2D(_MainTex, i.uv);

                #if GAMMATOLINEAR
                    col.rgb = GammaToLinear3(col.rgb);
                #endif
                //return col;
                //col.rgb = pow(col.rgb, 1 / 2.2);
                col.rgb *= col.a;
                //return col;
                //float darkGrey = 128.0 / 255.0;
                //float4 checkerColorA = float4(darkGrey, darkGrey, darkGrey, 1.0);

                //float lightGrey = 192.0 / 255.0;
                //float4 checkerColorB = float4(lightGrey, lightGrey, lightGrey, 1.0);

                float2 pixelCoord = floor(i.uv2 * _SpriteRect.zw);

                float2 Pos = floor(pixelCoord / _PixelAmount);
                float PatternMask = (Pos.x + (Pos.y% 2.0)) % 2.0;

                float4 checkerColor = lerp(_BGColor1, _BGColor2, PatternMask);
                //return checkerColor;
                // gamma to linear

                //col.rgb = lerp(checkerColor.rgb, col.rgb, col.a);
                    col.rgb = col.rgb + checkerColor.rgb * (1.0 - col.a);// *1.0 + (1.0 - col.a) + col.rgb * col.a;
                col.a = 1.0;

                return col;
            }
            ENDCG
        }

        // #3
        Pass
        {
            // No culling or depth
            Cull Off
            ZTest Always
            Lighting Off
            ZWrite Off
            //Blend Off
            BlendOp [_BlendOp], [_AlphaBlendOp]
            Blend [_SrcBlend] [_DstBlend], [_SrcAlphaBlend] [_DstAlphaBlend]
            //ColorMask RGB
            //Blend One Zero// [_SrcBlend] [_DstBlend]
            //Blend One Zero// [_SrcBlend] [_DstBlend]

            Tags { "RenderType" = "Transparent" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ ALPHABLEND
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            sampler2D _DstTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float4 _SpriteRect;
            float4 _Color;
            int _SrcAlpha;
            int _DstAlpha;

            v2f vert(appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv2 = TRANSFORM_TEX(v.uv2, _MainTex);

                return o;
            }

            float LinearToSRGB(float linearValue)
            {
                const float threshold = 0.0031308f;
                const float a = 0.055f;
                const float maxValue = 1.0f;

                // Clamp the input value to the range [0, 1]
                linearValue = saturate(linearValue);

                // Apply inverse gamma encoding
                if (linearValue <= threshold)
                {
                    return 12.92f * linearValue;
                }
                else
                {
                    return (1.055f * pow(linearValue, 1.0f / 2.4f)) - a;
                }
            }

            float3 LinearToSRGB3(float3 linearValue)
            {
                linearValue.r = LinearToGammaSpaceExact(linearValue.r);
                linearValue.g = LinearToGammaSpaceExact(linearValue.g);
                linearValue.b = LinearToGammaSpaceExact(linearValue.b);
                return linearValue;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                float4 srcCol = tex2D(_MainTex, i.uv);
                //float4 dstCol = tex2D(_DstTex, i.uv2);

                if (srcCol.a == 0)
                    discard;

                if (i.uv2.x < _SpriteRect.x || i.uv2.y < _SpriteRect.y || i.uv2.x > _SpriteRect.z || i.uv2.y > _SpriteRect.w)
                    discard;

                //return _Color;


#if !UNITY_COLORSPACE_GAMMA
                /*srcCol.r = LinearToSRGB(srcCol.r);
                srcCol.g = LinearToSRGB(srcCol.g);
                srcCol.b = LinearToSRGB(srcCol.b);*/

                /*dstCol.r = LinearToSRGB(dstCol.r);
                dstCol.g = LinearToSRGB(dstCol.g);
                dstCol.b = LinearToSRGB(dstCol.b);*/
                srcCol.rgb = LinearToSRGB3(srcCol.rgb);
#endif

                srcCol *= _Color;
                
#if ALPHABLEND
                srcCol.rgb = srcCol.rgb * srcCol.a;
#endif
                //srcCol.rgb = srcCol.rgb / srcCol.a;
                //float3 dstCol_pre = dstCol.rgb * dstCol.a;

                //float3 result_RGB = srcCol_pre + dstCol_pre * (1.0 - srcCol.a);
                //float result_A = srcCol.a + dstCol.a * (1.0 - srcCol.a);

                //result_RGB /= (srcCol.a + dstCol.a * (1.0 - srcCol.a));

                //float4 result = float4(srcCol_pre, srcCol.a);// float4(result_RGB, result_A);

                return srcCol;
            }
            ENDCG
        }

        // #4 Blit, Apply premultiplied alpha
        Pass
        {
            // No culling or depth
            Cull Off
            ZTest Always
            Lighting Off
            ZWrite Off
            Blend Off
            //ColorMask RGB
            //Blend[_SrcBlend][_DstBlend]
            //Blend One Zero// [_SrcBlend] [_DstBlend]
            //Blend One Zero// [_SrcBlend] [_DstBlend]

            Tags { "RenderType" = "Transparent" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile ALPHA_BYPASS ALPHA_MULTIPLY ALPHA_DIVIDE
            #pragma multi_compile _ SRGBREAD

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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;

            float LinearToSRGB(float linearValue)
            {
                const float threshold = 0.0031308f;
                const float a = 0.055f;
                const float maxValue = 1.0f;

                // Clamp the input value to the range [0, 1]
                linearValue = saturate(linearValue);

                // Apply inverse gamma encoding
                if (linearValue <= threshold)
                {
                    return 12.92f * linearValue;
                }
                else
                {
                    return (1.055f * pow(linearValue, 1.0f / 2.4f)) - a;
                }
            }

            float3 LinearToSRGB3(float3 linearValue)
            {
                linearValue.r = LinearToGammaSpaceExact(linearValue.r);
                linearValue.g = LinearToGammaSpaceExact(linearValue.g);
                linearValue.b = LinearToGammaSpaceExact(linearValue.b);
                return linearValue;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 srcCol = tex2D(_MainTex, i.uv);


#if SRGBREAD
                srcCol.rgb = LinearToSRGB3(srcCol.rgb);// pow(srcCol.rgb, 1 / 2.2);
#endif


#if ALPHA_MULTIPLY
                return float4(srcCol.rgb * srcCol.a, srcCol.a);
#elif ALPHA_DIVIDE
                //return float4(1, 1, 0, 1);
                return float4(srcCol.rgb / srcCol.a, srcCol.a);
#else
                return float4(srcCol.rgb, srcCol.a);
#endif
            }
            ENDCG
        }

        // #5 Blur
            Pass
        {
            // No culling or depth
            Cull Off
            ZWrite Off
            ZTest Always
            //Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
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

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float2 _BlurDirection;
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            // 9-tap Gaussian kernel, that blurs green & blue channels,
            // keeps red & alpha intact.
            static const half4 kCurveWeights[9] = {
                half4(0,0.0204001988,0.0204001988,0),
                half4(0,0.0577929595,0.0577929595,0),
                half4(0,0.1215916882,0.1215916882,0),
                half4(0,0.1899858519,0.1899858519,0),
                half4(1,0.2204586031,0.2204586031,1),
                half4(0,0.1899858519,0.1899858519,0),
                half4(0,0.1215916882,0.1215916882,0),
                half4(0,0.0577929595,0.0577929595,0),
                half4(0,0.0204001988,0.0204001988,0)
            };

            fixed4 frag(v2f i) : SV_Target
            {
                float2 step = _MainTex_TexelSize.xy * _BlurDirection;
                float2 uv = i.uv - step * 4;
                half4 col = 0;
                for (int tap = 0; tap < 9; ++tap)
                {
                    col += tex2D(_MainTex, uv) * kCurveWeights[tap].bgbg;
                    uv += step;
                }

                //col.a = 0.1;
                //col.rgb = tex2D(_MainTex, i.uv).rgb;

                if (tex2D(_MainTex, i.uv).a == 0)
                {
                    //col.a = 0;
                }

                return col;
            }
        ENDCG
        }
    }
}
