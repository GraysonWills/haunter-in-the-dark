Shader "Hidden/Haunter/OutlineComposite"
{
    Properties { _MainTex ("Echo masks", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex, _CameraDepthNormalsTexture;
            float4 _MainTex_TexelSize;
            float _LineWidth, _Brightness, _HaunterInterference, _EchoTime;

            float normalEdge(float4 a, float4 b)
            {
                float da, db;
                float3 na, nb;
                DecodeDepthNormal(a, da, na);
                DecodeDepthNormal(b, db, nb);
                return smoothstep(.20, .42, length(na - nb));
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float2 depthUV = uv;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0) depthUV.y = 1 - depthUV.y;
                #endif
                float2 pixel = abs(_MainTex_TexelSize.xy) * _LineWidth;
                float4 center = tex2D(_CameraDepthNormalsTexture, depthUV);
                float4 right = tex2D(_CameraDepthNormalsTexture, depthUV + float2(pixel.x, 0));
                float4 left = tex2D(_CameraDepthNormalsTexture, depthUV - float2(pixel.x, 0));
                float4 up = tex2D(_CameraDepthNormalsTexture, depthUV + float2(0, pixel.y));
                float4 down = tex2D(_CameraDepthNormalsTexture, depthUV - float2(0, pixel.y));
                float contour = max(max(normalEdge(center, right), normalEdge(center, left)),
                    max(normalEdge(center, up), normalEdge(center, down)));
                // Second differences reject ordinary depth slopes on grazing planar surfaces.
                // A first-difference threshold would incorrectly fill distant tabletops white.
                float depth = DecodeFloatRG(center.zw);
                float curvature = max(abs(DecodeFloatRG(left.zw) + DecodeFloatRG(right.zw) - 2 * depth),
                    abs(DecodeFloatRG(up.zw) + DecodeFloatRG(down.zw) - 2 * depth)) / max(.003, depth);
                contour = max(contour, smoothstep(.008, .035, curvature));
                float4 mask = tex2D(_MainTex, uv);
                float reveal = mask.r;
                reveal = max(reveal, tex2D(_MainTex, uv + float2(pixel.x, 0)).r);
                reveal = max(reveal, tex2D(_MainTex, uv - float2(pixel.x, 0)).r);
                reveal = max(reveal, tex2D(_MainTex, uv + float2(0, pixel.y)).r);
                reveal = max(reveal, tex2D(_MainTex, uv - float2(0, pixel.y)).r);
                float white = saturate((contour * reveal + mask.g) * _Brightness);
                if (_HaunterInterference > .01)
                {
                    // Preserve nearby contours and held props. Only the composite reveal
                    // samples drift, so geometry, navigation and interaction never shift.
                    float farEcho = smoothstep(3.1, 13.0, depth * _ProjectionParams.z);
                    float amount = _HaunterInterference * farEcho * (1.0 - saturate(mask.g));
                    float phase = 9.0 * uv.y + 7.0 * uv.x - _EchoTime * 1.7;
                    float2 drift = float2(sin(phase), cos(phase * .72)) * (.012 * amount);
                    float4 distant = tex2D(_CameraDepthNormalsTexture, saturate(depthUV + drift));
                    float4 next = tex2D(_CameraDepthNormalsTexture, saturate(depthUV + drift + float2(pixel.x, pixel.y)));
                    float delayedReveal = tex2D(_MainTex, saturate(uv + drift)).r;
                    float interference = .58 + .28 * sin(phase + delayedReveal * 4.4);
                    float ghost = normalEdge(distant, next) * delayedReveal * interference;
                    white = saturate(white * (1.0 - .35 * amount) + ghost * amount * .92);
                }
                // A subtle soft shoulder keeps narrow white lines comfortable on black.
                white = pow(white, .82);
                return float4(white, white, white, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
