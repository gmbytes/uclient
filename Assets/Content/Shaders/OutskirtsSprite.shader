Shader "Client/OutskirtsSprite"
{
 Properties
 {
  [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
  _Color ("Tint", Color) = (1,1,1,1)
  _ReferenceSun ("Environment sun radiance", Vector) = (1,1,1,0)
  _ReferenceAmbient ("Environment ambient radiance", Vector) = (.3,.3,.3,0)
  _ReferenceDiffuse ("Ground-facing sun response", Range(0,1)) = .5
  [HideInInspector] _MapOriginY ("Map origin", Float) = 0
  [HideInInspector] _BakedLightmapScale ("Lighting phase", Float) = 0
  [HideInInspector] _RendererColor ("Renderer Color", Color) = (1,1,1,1)
  [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
  [PerRendererData] _AlphaTex ("Alpha", 2D) = "white" {}
  [PerRendererData] _EnableExternalAlpha ("External Alpha", Float) = 0
 }
 SubShader
 {
  Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
  Cull Off ZWrite Off ZTest Always
  Blend One OneMinusSrcAlpha
  Pass
  {
   CGPROGRAM
   #pragma vertex SpriteVert
   #pragma fragment EnvironmentSpriteFrag
   #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
   #include "UnitySprites.cginc"
   float4 _ReferenceSun, _ReferenceAmbient;
   float _ReferenceDiffuse;
   half4 EnvironmentSpriteFrag(v2f input) : SV_Target
   {
    fixed4 texel = SampleSpriteTexture(input.texcoord) * input.color;
    float3 albedo = texel.rgb;
    #if defined(UNITY_COLORSPACE_GAMMA)
     albedo = GammaToLinearSpace(albedo);
    #endif
    // Pre-rendered PNG shading remains intact; its overall radiance follows the same
    // ambient and ground-facing directional light as the surrounding terrain.
    float3 result = albedo * (_ReferenceAmbient.rgb + _ReferenceSun.rgb * _ReferenceDiffuse);
    #if defined(UNITY_COLORSPACE_GAMMA)
     result = LinearToGammaSpace(result);
    #endif
    return half4(result * texel.a, texel.a);
   }
   ENDCG
  }
 }
}
