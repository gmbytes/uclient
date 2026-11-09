Shader "Hidden/Client/OutskirtsPostProcess"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _Threshold ("HYGD bloom threshold", Float) = .8
        _Intensity ("HYGD bloom intensity", Float) = .5
        _Scatter ("HYGD bloom scatter", Float) = .5
        _Vignette ("HYGD vignette center, intensity, smoothness", Vector) = (.5,.65,.168,.28)
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex, _LowMip, _BloomTex;
    float4 _MainTex_TexelSize, _BloomParams, _Vignette;
    float _Intensity, _Aspect;

    float3 FromScene(float3 color)
    {
        #if defined(UNITY_COLORSPACE_GAMMA)
        return GammaToLinearSpace(color);
        #else
        return color;
        #endif
    }

    float4 Prefilter(v2f_img input) : SV_Target
    {
        float3 color = min(_BloomParams.y, FromScene(tex2D(_MainTex, input.uv).rgb));
        float brightness = max(color.r, max(color.g, color.b));
        float softness = clamp(brightness - _BloomParams.z + _BloomParams.w, 0, 2 * _BloomParams.w);
        softness = softness * softness / (4 * _BloomParams.w + 1e-4);
        color *= max(brightness - _BloomParams.z, softness) / max(brightness, 1e-4);
        return float4(max(color, 0), 1);
    }
    float4 Horizontal(v2f_img input) : SV_Target
    {
        float2 step = float2(_MainTex_TexelSize.x * 2, 0);
        float3 color = tex2D(_MainTex, input.uv).rgb * .22702703;
        color += (tex2D(_MainTex, input.uv - step).rgb + tex2D(_MainTex, input.uv + step).rgb) * .19459459;
        color += (tex2D(_MainTex, input.uv - 2 * step).rgb + tex2D(_MainTex, input.uv + 2 * step).rgb) * .12162162;
        color += (tex2D(_MainTex, input.uv - 3 * step).rgb + tex2D(_MainTex, input.uv + 3 * step).rgb) * .05405405;
        color += (tex2D(_MainTex, input.uv - 4 * step).rgb + tex2D(_MainTex, input.uv + 4 * step).rgb) * .01621622;
        return float4(color, 1);
    }
    float4 Vertical(v2f_img input) : SV_Target
    {
        float2 step = float2(0, _MainTex_TexelSize.y);
        float3 color = tex2D(_MainTex, input.uv).rgb * .22702703;
        color += (tex2D(_MainTex, input.uv - step * 1.38461538).rgb + tex2D(_MainTex, input.uv + step * 1.38461538).rgb) * .31621622;
        color += (tex2D(_MainTex, input.uv - step * 3.23076923).rgb + tex2D(_MainTex, input.uv + step * 3.23076923).rgb) * .07027027;
        return float4(color, 1);
    }
    float4 Upsample(v2f_img input) : SV_Target
    {
        return float4(lerp(tex2D(_MainTex, input.uv).rgb, tex2D(_LowMip, input.uv).rgb, _BloomParams.x), 1);
    }
    float4 Composite(v2f_img input) : SV_Target
    {
        float3 color = FromScene(tex2D(_MainTex, input.uv).rgb) + tex2D(_BloomTex, input.uv).rgb * _Intensity;
        float2 dist = abs(input.uv - _Vignette.xy) * (_Vignette.z * 3);
        dist.x *= _Aspect;
        color *= pow(saturate(1 - dot(dist, dist)), _Vignette.w * 5);
        #if defined(UNITY_COLORSPACE_GAMMA)
        color = LinearToGammaSpace(color);
        #endif
        return float4(color, 1);
    }
    ENDCG
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Prefilter
        ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Horizontal
        ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Vertical
        ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Upsample
        ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Composite
        ENDCG
        }
    }
}
