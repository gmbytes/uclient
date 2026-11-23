Shader "Client/OutskirtsSurface"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _DirtTex ("Meadow soil", 2D) = "white" {}
        _TerrainMask ("HYGD soil mask", 2D) = "white" {}
        _NormalMap ("Surface detail", 2D) = "bump" {}
        _MacroNormal ("HYGD grass blade normal", 2D) = "bump" {}
        _MacroNormalStrength ("HYGD grass blade normal scale", Range(0,1)) = .877
        _Layer2Tex ("HYGD terrain layer 2", 2D) = "white" {}
        _Layer2Dirt ("HYGD terrain soil 2", 2D) = "white" {}
        _Layer3Tex ("HYGD terrain layer 3", 2D) = "white" {}
        _Layer3Dirt ("HYGD terrain soil 3", 2D) = "white" {}
        _TerrainLayers ("HYGD painted terrain mask", 2D) = "black" {}
        _BaseLayerByGon ("HYGD base layer uses green channel", Float) = 0
        _WaterColor ("HYGD water tint", Color) = (.285048,.8446,.96226,0)
        _WorldTerrainOverlay ("Apply world roads and biome mask", Range(0,1)) = 0
        _LocalGrassland ("Local beginner grassland", Float) = 0
        _OwnedHome ("Local owned home XZ", Vector) = (2,3.4641,0,0)
        _Layer2Normal ("HYGD terrain normal 2", 2D) = "bump" {}
        _Layer3Normal ("HYGD terrain normal 3", 2D) = "bump" {}
        _Layer2NormalStrength ("HYGD terrain normal scale 2", Range(0,1)) = .611
        _Layer3NormalStrength ("HYGD terrain normal scale 3", Range(0,1)) = .719
        _ColorMix2 ("HYGD terrain tint 2", Color) = (1,1,1,1)
        _ColorMix3 ("HYGD terrain tint 3", Color) = (1,1,1,1)
        _NormalStrength ("Normal strength", Range(0,1)) = 0
        _TextureBright ("Terrain brightness", Float) = 1
        _Color ("Tint", Color) = (1,1,1,1)
        _VegetationTint ("Time-of-day vegetation tint", Color) = (1,1,1,1)
        _ReferenceSun ("HYGD daylight radiance", Vector) = (1.8,1.37,.86,0)
        _ReferenceAmbient ("HYGD daylight ambient", Vector) = (.262,.392,.631,0)
        _DirtMaskSize1 ("HYGD dirt scale 1", Float) = 735
        _DirtMaskSize2 ("HYGD dirt scale 2", Float) = 645.8
        _dirtsize ("HYGD dirt falloff", Float) = .33
        _dirtpower ("HYGD dirt strength", Float) = 1.5
        _ColorMixMaskSize1 ("HYGD meadow color scale", Float) = 300
        _ColorMixMaskSize2 ("HYGD meadow color scale 2", Float) = 150
        _colorlightsize ("HYGD color falloff", Float) = .2
        _ColorMix1 ("HYGD meadow color", Color) = (.87,.906,.698,1)
        _Terrain ("Continuous terrain", Float) = 0
        _TerrainFlattenScale ("Terrain vertical flattening", Float) = 1
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0
        _BakedAO ("HYGD architecture AO", 2D) = "white" {}
        _UseBakedAO ("Use HYGD architecture AO", Float) = 0
        _AOScale ("HYGD AO scale", Range(0,1)) = 0
        _BakedLightmap ("HYGD night lightmap", 2D) = "black" {}
        _BakedLightmapScale ("HYGD night lightmap scale", Range(0,1)) = 0
        _UseBakedLightmap ("Use HYGD lightmap", Float) = 0
        _EmissiveStrength ("HYGD emissive strength", Float) = 0
        _EmissionFromAlbedoAlpha ("HYGD emission mask channel", Float) = 0
        _HeightLightEnabled ("HYGD height light enabled", Float) = 0
        _HeightLightBright ("HYGD upper model brightness", Float) = 1
        _HeightLightDark ("HYGD lower model brightness", Float) = 1
        _HeightLightHeight ("HYGD reference model height", Float) = 1
        _HeightLightRange ("HYGD height falloff", Float) = 1
        _MapOriginY ("Isolated map origin", Float) = 0
        _NativeHeightScale ("Native model units", Float) = 5
        _NativeHeightOffset ("Native model height offset", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Face culling", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull [_Cull]
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            sampler2D _DirtTex;
            sampler2D _TerrainMask;
            sampler2D _NormalMap;
            sampler2D _MacroNormal;
            float4 _MacroNormal_ST;
            half _MacroNormalStrength;
            sampler2D _BakedAO;
            float4 _BakedAO_ST;
            half _UseBakedAO, _AOScale;
            sampler2D _BakedLightmap;
            float4 _BakedLightmap_ST;
            half _BakedLightmapScale, _UseBakedLightmap, _EmissiveStrength, _EmissionFromAlbedoAlpha;
            half _HeightLightEnabled, _HeightLightBright, _HeightLightDark, _HeightLightRange;
            float _HeightLightHeight, _MapOriginY, _NativeHeightScale, _NativeHeightOffset;
            sampler2D _Layer2Tex, _Layer2Dirt, _Layer3Tex, _Layer3Dirt, _TerrainLayers, _Layer2Normal, _Layer3Normal;
            float4 _Layer2Tex_ST, _Layer2Dirt_ST, _Layer3Tex_ST, _Layer3Dirt_ST, _TerrainLayers_ST, _Layer2Normal_ST, _Layer3Normal_ST;
            fixed4 _ColorMix2, _ColorMix3;
            half _WorldTerrainOverlay, _BaseLayerByGon;
            fixed4 _WaterColor;
            half _LocalGrassland;
            float4 _OwnedHome;
            float4 _MainTex_ST, _DirtTex_ST, _TerrainMask_ST, _NormalMap_ST;
            fixed4 _Color, _VegetationTint;
            float4 _ReferenceSun, _ReferenceAmbient;
            float _Terrain, _TerrainFlattenScale;
            float _DirtMaskSize1, _DirtMaskSize2, _dirtsize, _dirtpower, _ColorMixMaskSize1, _ColorMixMaskSize2, _colorlightsize;
            fixed4 _ColorMix1;
            float _Cutoff;
            half _NormalStrength;
            half _Layer2NormalStrength, _Layer3NormalStrength;
            half _TextureBright;

            struct VertexOutput
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 world : TEXCOORD1;
                half3 normal : TEXCOORD2;
                SHADOW_COORDS(3)
                half4 tangent : TEXCOORD4;
                float2 uv2 : TEXCOORD5;
            };

            VertexOutput vert(appdata_full v)
            {
                VertexOutput o = (VertexOutput)0;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.uv2 = v.texcoord1;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 authoredNormal = v.normal;
                float3 authoredTangent = v.tangent.xyz;
                if (_Terrain > .5)
                {
                    // Preserve HYGD's authored shading basis while fitting its height to the local grid.
                    authoredNormal.y *= _TerrainFlattenScale;
                    authoredTangent.y /= max(.000001, _TerrainFlattenScale);
                }
                o.normal = UnityObjectToWorldNormal(authoredNormal);
                o.tangent = half4(UnityObjectToWorldDir(authoredTangent), v.tangent.w * unity_WorldTransformParams.w);
                TRANSFER_SHADOW(o);
                return o;
            }

float3 mod3D289( float3 x ) { return x - floor( x / 289.0 ) * 289.0; }
			float4 mod3D289( float4 x ) { return x - floor( x / 289.0 ) * 289.0; }
			float4 permute( float4 x ) { return mod3D289( ( x * 34.0 + 1.0 ) * x ); }
			float4 taylorInvSqrt( float4 r ) { return 1.79284291400159 - r * 0.85373472095314; }
			float snoise( float3 v )
			{
				const float2 C = float2( 1.0 / 6.0, 1.0 / 3.0 );
				float3 i = floor( v + dot( v, C.yyy ) );
				float3 x0 = v - i + dot( i, C.xxx );
				float3 g = step( x0.yzx, x0.xyz );
				float3 l = 1.0 - g;
				float3 i1 = min( g.xyz, l.zxy );
				float3 i2 = max( g.xyz, l.zxy );
				float3 x1 = x0 - i1 + C.xxx;
				float3 x2 = x0 - i2 + C.yyy;
				float3 x3 = x0 - 0.5;
				i = mod3D289( i);
				float4 p = permute( permute( permute( i.z + float4( 0.0, i1.z, i2.z, 1.0 ) ) + i.y + float4( 0.0, i1.y, i2.y, 1.0 ) ) + i.x + float4( 0.0, i1.x, i2.x, 1.0 ) );
				float4 j = p - 49.0 * floor( p / 49.0 );  // mod(p,7*7)
				float4 x_ = floor( j / 7.0 );
				float4 y_ = floor( j - 7.0 * x_ );  // mod(j,N)
				float4 x = ( x_ * 2.0 + 0.5 ) / 7.0 - 1.0;
				float4 y = ( y_ * 2.0 + 0.5 ) / 7.0 - 1.0;
				float4 h = 1.0 - abs( x ) - abs( y );
				float4 b0 = float4( x.xy, y.xy );
				float4 b1 = float4( x.zw, y.zw );
				float4 s0 = floor( b0 ) * 2.0 + 1.0;
				float4 s1 = floor( b1 ) * 2.0 + 1.0;
				float4 sh = -step( h, 0.0 );
				float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
				float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
				float3 g0 = float3( a0.xy, h.x );
				float3 g1 = float3( a0.zw, h.y );
				float3 g2 = float3( a1.xy, h.z );
				float3 g3 = float3( a1.zw, h.w );
				float4 norm = taylorInvSqrt( float4( dot( g0, g0 ), dot( g1, g1 ), dot( g2, g2 ), dot( g3, g3 ) ) );
				g0 *= norm.x;
				g1 *= norm.y;
				g2 *= norm.z;
				g3 *= norm.w;
				float4 m = max( 0.6 - float4( dot( x0, x0 ), dot( x1, x1 ), dot( x2, x2 ), dot( x3, x3 ) ), 0.0 );
				m = m* m;
				m = m* m;
				float4 px = float4( dot( x0, g0 ), dot( x1, g1 ), dot( x2, g2 ), dot( x3, g3 ) );
				return 42.0 * dot( m, px);
			}
			
			float3 mod2D289( float3 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
			float2 mod2D289( float2 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
			float3 permute( float3 x ) { return mod2D289( ( ( x * 34.0 ) + 1.0 ) * x ); }
			float snoise( float2 v )
			{
				const float4 C = float4( 0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439 );
				float2 i = floor( v + dot( v, C.yy ) );
				float2 x0 = v - i + dot( i, C.xx );
				float2 i1;
				i1 = ( x0.x > x0.y ) ? float2( 1.0, 0.0 ) : float2( 0.0, 1.0 );
				float4 x12 = x0.xyxy + C.xxzz;
				x12.xy -= i1;
				i = mod2D289( i );
				float3 p = permute( permute( i.y + float3( 0.0, i1.y, 1.0 ) ) + i.x + float3( 0.0, i1.x, 1.0 ) );
				float3 m = max( 0.5 - float3( dot( x0, x0 ), dot( x12.xy, x12.xy ), dot( x12.zw, x12.zw ) ), 0.0 );
				m = m * m;
				m = m * m;
				float3 x = 2.0 * frac( p * C.www ) - 1.0;
				float3 h = abs( x ) - 0.5;
				float3 ox = floor( x + 0.5 );
				float3 a0 = x - ox;
				m *= 1.79284291400159 - 0.85373472095314 * ( a0 * a0 + h * h );
				float3 g;
				g.x = a0.x * x0.x + h.x * x0.y;
				g.yz = a0.yz * x12.xz + h.yz * x12.yw;
				return 130.0 * dot( m, g );
			}
			
			inline float noise_randomValue (float2 uv) { return frac(sin(dot(uv, float2(12.9898, 78.233)))*43758.5453); }
			inline float noise_interpolate (float a, float b, float t) { return (1.0-t)*a + (t*b); }
			inline float valueNoise (float2 uv)
			{
				float2 i = floor(uv);
				float2 f = frac( uv );
				f = f* f * (3.0 - 2.0 * f);
				uv = abs( frac(uv) - 0.5);
				float2 c0 = i + float2( 0.0, 0.0 );
				float2 c1 = i + float2( 1.0, 0.0 );
				float2 c2 = i + float2( 0.0, 1.0 );
				float2 c3 = i + float2( 1.0, 1.0 );
				float r0 = noise_randomValue( c0 );
				float r1 = noise_randomValue( c1 );
				float r2 = noise_randomValue( c2 );
				float r3 = noise_randomValue( c3 );
				float bottomOfGrid = noise_interpolate( r0, r1, f.x );
				float topOfGrid = noise_interpolate( r2, r3, f.x );
				float t = noise_interpolate( bottomOfGrid, topOfGrid, f.y );
				return t;
			}
			
			float SimpleNoise(float2 UV)
			{
				float t = 0.0;
				float freq = pow( 2.0, float( 0 ) );
				float amp = pow( 0.5, float( 3 - 0 ) );
				t += valueNoise( UV/freq )*amp;
				freq = pow(2.0, float(1));
				amp = pow(0.5, float(3-1));
				t += valueNoise( UV/freq )*amp;
				freq = pow(2.0, float(2));
				amp = pow(0.5, float(3-2));
				t += valueNoise( UV/freq )*amp;
				return t;
			}
			

			
            float3 SourceColor(float3 color)
            {
                #if defined(UNITY_COLORSPACE_GAMMA)
                    return GammaToLinearSpace(color);
                #else
                    return color;
                #endif
            }

            fixed4 TerrainLayers(VertexOutput input)
            {
                return tex2D(_TerrainLayers, TRANSFORM_TEX(input.uv, _TerrainLayers)) * _WorldTerrainOverlay;
            }

            half3 SourceTerrainNormal(sampler2D normalMap, float2 uv, half strength)
            {
                half3 normal = UnpackNormal(tex2D(normalMap, uv));
                normal.xy *= strength;
                // Match WN_Terrain_Base's UnpackNormalScale followed by its Z blend.
                normal.z = lerp(1, normal.z, saturate(strength));
                return normal;
            }

            half4 frag(VertexOutput input) : SV_Target
            {
                fixed3 albedo;
                if (_Terrain > .5)
                {
                    // Preserve the HYGD terrain material's UV noise and soft-light blend.
                    float2 uv = input.uv;
                    half mask = tex2D(_TerrainMask, TRANSFORM_TEX(uv, _TerrainMask)).r;
                    float perlin = snoise(uv * _DirtMaskSize1) * .5 + .5;
                    float noise = SimpleNoise(uv * _DirtMaskSize2);
                    float soil = pow(saturate(mask * pow(perlin, 2) * pow(noise, 3)), _dirtsize) * _dirtpower;
                    fixed3 grass = SourceColor(tex2D(_MainTex, TRANSFORM_TEX(uv, _MainTex)).rgb);
                    fixed3 dirt = SourceColor(tex2D(_DirtTex, TRANSFORM_TEX(uv, _DirtTex)).rgb);
                    fixed3 baseColor = lerp(grass, dirt, soil);
                    float colorMask = pow(saturate(pow(snoise(float3(uv, 0) * _ColorMixMaskSize2) * .5 + .5, 4.35) * (snoise(uv * _ColorMixMaskSize1) * .5 + .5)), _colorlightsize);
                    fixed3 blend = colorMask * SourceColor(_ColorMix1.rgb);
                    albedo = saturate(lerp(baseColor, 2 * baseColor * blend + baseColor * baseColor * (1 - 2 * blend), .5)) * _TextureBright;
                    fixed3 layer2 = lerp(SourceColor(tex2D(_Layer2Tex, TRANSFORM_TEX(uv, _Layer2Tex)).rgb), SourceColor(tex2D(_Layer2Dirt, TRANSFORM_TEX(uv, _Layer2Dirt)).rgb), soil);
                    fixed3 layer3 = lerp(SourceColor(tex2D(_Layer3Tex, TRANSFORM_TEX(uv, _Layer3Tex)).rgb), SourceColor(tex2D(_Layer3Dirt, TRANSFORM_TEX(uv, _Layer3Dirt)).rgb), soil);
                    fixed3 tint2 = colorMask * SourceColor(_ColorMix2.rgb);
                    fixed3 tint3 = colorMask * SourceColor(_ColorMix3.rgb);
                    layer2 = saturate(lerp(layer2, 2 * layer2 * tint2 + layer2 * layer2 * (1 - 2 * tint2), .5));
                    layer3 = saturate(lerp(layer3, 2 * layer3 * tint3 + layer3 * layer3 * (1 - 2 * tint3), .5)) * _TextureBright;
                    fixed4 layers = TerrainLayers(input);
                    // WN_Terrain_Base: retain its authored layer mask and exact mix order.
                    fixed3 baseLayer = _BaseLayerByGon > .5 ? layer2 : albedo;
                    albedo = lerp(baseLayer, albedo, layers.r);
                    albedo = lerp(albedo, layer3, layers.b);
                    albedo = lerp(albedo, layer2 * _TextureBright, layers.g);
                    albedo = lerp(albedo, SourceColor(_WaterColor.rgb), layers.a);
                }
                else
                {
                    fixed4 texel = tex2D(_MainTex, TRANSFORM_TEX(input.uv, _MainTex));
                    clip(texel.a - _Cutoff);
                    albedo = SourceColor(texel.rgb);
                }

                half3 normal = normalize(input.normal);
                if (_NormalStrength > 0 && (_Terrain > .5 || abs(input.tangent.w) > .5))
                {
                    half3 detail = UnpackNormal(tex2D(_NormalMap, TRANSFORM_TEX(input.uv, _NormalMap)));
                    if (_Terrain > .5)
                    {
                        fixed4 layers = TerrainLayers(input);
                        // HYGD uses layer 2's grass normal as the default; the red channel selects sand.
                        detail = SourceTerrainNormal(_NormalMap, TRANSFORM_TEX(input.uv, _NormalMap), _NormalStrength);
                        half3 grassDetail = SourceTerrainNormal(_Layer2Normal, TRANSFORM_TEX(input.uv, _Layer2Normal), _Layer2NormalStrength);
                        half3 thirdDetail = SourceTerrainNormal(_Layer3Normal, TRANSFORM_TEX(input.uv, _Layer3Normal), _Layer3NormalStrength);
                        detail = lerp(lerp(grassDetail, detail, layers.r), thirdDetail, layers.b);
                        half3 blade = SourceTerrainNormal(_MacroNormal, TRANSFORM_TEX(input.uv, _MacroNormal), _MacroNormalStrength);
                        detail = normalize(half3(blade.xy * detail.z + detail.xy * blade.z, blade.z * detail.z));
                    }
                    half3 tangent = dot(input.tangent.xyz, input.tangent.xyz) > .01 ? normalize(input.tangent.xyz) : half3(1, 0, 0);
                    half3 bitangent = cross(normal, tangent) * (input.tangent.w < 0 ? -1 : 1);
                    normal = normalize(normal * detail.z + (_Terrain > .5 ? 1 : _NormalStrength) * (tangent * detail.x + bitangent * detail.y));
                }
                half diffuse = saturate(dot(normal, _WorldSpaceLightPos0.xyz));
                half shadow = SHADOW_ATTENUATION(input);
                float3 light = _ReferenceAmbient.rgb + _ReferenceSun.rgb * diffuse * shadow;
                if (_UseBakedAO > .5)
                {
                    half ao = lerp(sqrt(saturate(tex2D(_BakedAO, TRANSFORM_TEX(input.uv2, _BakedAO)).r)), 1, _AOScale);
                    float3 sunAo = 1 - (1 - ao) * (1 - saturate(_ReferenceSun.rgb));
                    light *= lerp(ao.xxx, sunAo, diffuse * shadow);
                }
                float3 result = albedo * SourceColor(_Color.rgb) * SourceColor(_VegetationTint.rgb) * light;
                if (_HeightLightEnabled > .5)
                {
                    float nativeHeight = (input.world.y - _MapOriginY) * _NativeHeightScale + _NativeHeightOffset;
                    float gradient = pow(abs(nativeHeight / max(.0001, _HeightLightHeight)), _HeightLightRange);
                    result *= lerp(_HeightLightDark, _HeightLightBright, gradient);
                }
                if (_Terrain < .5)
                {
                    half4 aoMask = tex2D(_BakedAO, TRANSFORM_TEX(input.uv2, _BakedAO));
                    half emissionMask = lerp(aoMask.g,
                        tex2D(_MainTex, TRANSFORM_TEX(input.uv, _MainTex)).a, step(.5, _EmissionFromAlbedoAlpha));
                    half ao = lerp(sqrt(saturate(aoMask.r)), 1, _AOScale);
                    result += albedo * _EmissiveStrength * emissionMask * ao;
                    result += DecodeLightmap(tex2D(_BakedLightmap, TRANSFORM_TEX(input.uv2, _BakedLightmap))) * _BakedLightmapScale * _UseBakedLightmap;
                }
                #if defined(UNITY_COLORSPACE_GAMMA)
                    result = LinearToGammaSpace(result);
                #endif
                return half4(result, 1);
            }
            ENDCG
        }
        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            CGPROGRAM
            #pragma vertex shadowVertex
            #pragma fragment shadowFragment
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Cutoff;
            struct ShadowOutput { V2F_SHADOW_CASTER; float2 uv : TEXCOORD1; };
            ShadowOutput shadowVertex(appdata_base v)
            {
                ShadowOutput o = (ShadowOutput)0;
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }
            float4 shadowFragment(ShadowOutput input) : SV_Target
            {
                clip(tex2D(_MainTex, input.uv).a - _Cutoff);
                SHADOW_CASTER_FRAGMENT(input)
            }
            ENDCG
        }
    }
}
