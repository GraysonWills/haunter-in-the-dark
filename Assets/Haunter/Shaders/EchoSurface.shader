Shader "Haunter/EchoSurface"
{
    Properties
    {
        _AlwaysVisible ("Persistent held outline", Float) = 0
        _Floor ("Floor ripple rings", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            ZWrite On
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; };
            float _AlwaysVisible, _Floor, _EchoTime;
            int _EchoCount;
            float4 _EchoOrigins[32];
            float4 _EchoSettings[32];

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float reveal = 0, ring = 0;
                [loop] for (int n = 0; n < _EchoCount; n++)
                {
                    float d = distance(i.world, _EchoOrigins[n].xyz);
                    float age = _EchoTime - _EchoOrigins[n].w;
                    float radius = _EchoSettings[n].x;
                    float speed = _EchoSettings[n].y;
                    float behind = age - d / speed;
                    float endFade = 1 - smoothstep(radius * .78, radius, d);
                    float fade = exp(-max(0, behind) * 3 / _EchoSettings[n].w);
                    float wave = smoothstep(-.025, .018, behind) * fade * endFade * _EchoSettings[n].z;
                    reveal = max(reveal, wave);
                    float front = (d - age * speed) / .14;
                    ring = max(ring, exp(-front * front) * endFade * _EchoSettings[n].z);
                }
                // The source image stores masks, not visible surface colour.
                // Depth/normal discontinuities become the final white outlines.
                float2 gridUV = i.world.xz / 1.5;
                float2 lines = abs(frac(gridUV - .5) - .5) / max(fwidth(gridUV), .0001);
                float grid = 1 - saturate(min(lines.x, lines.y));
                float floorLines = _Floor * (ring * .48 + grid * reveal * .10);
                return float4(max(reveal, _AlwaysVisible), floorLines, _AlwaysVisible, 1);
            }
            ENDCG
        }
        UsePass "Legacy Shaders/VertexLit/SHADOWCASTER"
    }
    Fallback Off
}
