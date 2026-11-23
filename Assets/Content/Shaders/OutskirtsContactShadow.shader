Shader "Client/OutskirtsContactShadow"
{
 Properties { _Color ("Shadow", Color) = (.08,.12,.07,.2) }
 SubShader
 {
  Tags { "Queue"="Transparent-20" "RenderType"="Transparent" }
  Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
  Pass
  {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   fixed4 _Color;
   struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
   v2f vert(appdata_base v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.texcoord.xy*2-1; return o; }
   fixed4 frag(v2f i):SV_Target { fixed4 c=_Color; c.a*=pow(saturate(1-dot(i.uv,i.uv)),2); return c; }
   ENDCG
  }
 }
}
