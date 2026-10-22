// Unlit particle / stamp shader for the v0.6 weather effects (rain streaks, snow, splashes, breath) and the field wear stamps.
// Output is written as final screen colour (no exposure scaling), so brightness is tuned per preset by the colour on the material / particles.
Shader "PIX/FxUnlit"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _Color("Color", Color) = (1,1,1,1)
        _Intensity("Intensity", Float) = 1
        [HideInInspector] _SrcBlend("Src", Float) = 5
        [HideInInspector] _DstBlend("Dst", Float) = 10
        [HideInInspector] _ZTest("ZTest", Float) = 4
        [HideInInspector] _Additive("Additive", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "FxForward"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off
            ZTest [_ZTest]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Intensity;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                fixed4 c = t * i.color;
                c.rgb *= _Intensity;
                return c;
            }
            ENDCG
        }
    }
}
