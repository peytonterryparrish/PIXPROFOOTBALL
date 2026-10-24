// PIX glass UI surface (UGUI): a resolution-independent rounded rectangle that refracts a blurred copy of the 3D scene.
// Everything is analytic (signed distance field), so edges, corners and the silver hairline stay crisp at 1080p, 1440p, 2160p and under any canvas scale: nothing is a stretched bitmap.
//
// Per-vertex data (written by PixFootball.UI.Glass.GlassGraphic; all four UV channels are float4):
//   color : rgb = tint multiplier (kind 0/3) or fill colour (kind 2); a = fade (CanvasGroup / Graphic alpha)
//   uv0   : xy position inside the rect in canvas units from the rect centre, zw half size of the rect
//   uv1   : x corner radius, y edge width, z focus amount 0..1 (orange edge + glow), w drop shadow amount 0..1
//   uv2   : x sheen strength, y noise strength, z backdrop strength 0..1 (0 = flat frosted fallback), w kind
//   uv3   : x density (tint opacity multiplier), y brighten (hover / press), z glow scale (1 = full focus glow), w unused
// uv3.w (Tile look) = focus edge width override (negative = theme)
// kind: 0 glass, 1 action (silver gradient, dark text), 2 solid tint (vertex colour is the fill), 3 inset well (dark, low opacity), 4 accent fill (orange)
// Tile look (material _Flat = 1, see fragFlat): 0 tile body, 1/4 gold plate, 2 solid, 3 well, 5 panel, 6 list row, 7 tab, 8 active tab; kinds 5-8 draw as 0 / 4 in the glass look
//
// Globals (set by PixFootball.UI.Glass.GlassBackdrop): _PixGlassBackdrop (blurred scene), _PixGlassBackdropScale (valid uv extent of that texture), _PixGlassBackdropOn (1 when live blur is available).
Shader "PIX/UI/Glass"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _SurfTop ("Surface top (sRGB rgba)", Vector) = (0.4, 0.4, 0.416, 0.48)
        _SurfMid ("Surface mid (sRGB rgba)", Vector) = (0.141, 0.141, 0.149, 0.40)
        _SurfBot ("Surface bottom (sRGB rgba)", Vector) = (0.333, 0.333, 0.345, 0.38)
        _EdgeA ("Edge A (sRGB)", Vector) = (0.859, 0.859, 0.871, 1)
        _EdgeB ("Edge B (sRGB)", Vector) = (0.467, 0.467, 0.482, 1)
        _EdgeC ("Edge C (sRGB)", Vector) = (0.878, 0.878, 0.89, 1)
        _ActionA ("Action A (sRGB)", Vector) = (0.953, 0.953, 0.953, 1)
        _ActionB ("Action B (sRGB)", Vector) = (0.78, 0.788, 0.8, 1)
        _Accent ("Accent (sRGB)", Vector) = (0.957, 0.604, 0.322, 1)
        _FrostLow ("Fallback frost dark (sRGB)", Vector) = (0.085, 0.088, 0.1, 1)
        _FrostHigh ("Fallback frost light (sRGB)", Vector) = (0.2, 0.205, 0.225, 1)
        _Dim ("Backdrop dim", Range(0, 1)) = 0.5
        _Desat ("Backdrop desaturation", Range(0, 1)) = 0.35
        _Refraction ("Edge refraction (uv)", Range(0, 0.05)) = 0.012
        _EdgeAlpha ("Edge opacity", Range(0, 1)) = 0.9
        _Flat ("Flat (Tile) look", Float) = 0
        _FlatFill ("Tile fill (sRGB)", Vector) = (0.145, 0.153, 0.173, 1)
        _PanelFill ("Panel fill (sRGB)", Vector) = (0.125, 0.133, 0.157, 1)
        _PanelHead ("Panel head (sRGB)", Vector) = (0.082, 0.09, 0.106, 1)
        _RowFill ("Row fill (sRGB)", Vector) = (0.102, 0.11, 0.125, 1)
        _RowFocus ("Row focus fill (sRGB)", Vector) = (0.153, 0.165, 0.192, 1)
        _TabOff ("Tab off fill (sRGB)", Vector) = (0.114, 0.122, 0.141, 1)
        _GoldHi ("Gold highlight (sRGB)", Vector) = (1, 0.812, 0.478, 1)
        _FocusA ("Focus edge, ring width, ring alpha, glow alpha", Vector) = (4, 2, 0.35, 0.55)
        _FocusB ("Glow radius, sheen alpha, shadow alpha", Vector) = (46, 0.16, 0.5, 0)
        _KeyA ("Key cap top (sRGB)", Vector) = (0.467, 0.467, 0.467, 1)
        _KeyB ("Key cap bottom (sRGB)", Vector) = (0.235, 0.235, 0.235, 1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float4 uv3 : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float4 pos : TEXCOORD0;
                float4 params1 : TEXCOORD1;
                float4 params2 : TEXCOORD2;
                float4 params3 : TEXCOORD3;
                float4 worldPosition : TEXCOORD4;
                float4 screenPos : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _SurfTop, _SurfMid, _SurfBot, _EdgeA, _EdgeB, _EdgeC, _ActionA, _ActionB, _Accent, _FrostLow, _FrostHigh;
            float _Dim, _Desat, _Refraction, _EdgeAlpha, _Flat;
            float4 _FlatFill, _PanelFill, _PanelHead, _RowFill, _RowFocus, _TabOff, _GoldHi, _FocusA, _FocusB, _KeyA, _KeyB;
            float4 _ClipRect;
            sampler2D _MainTex;

            sampler2D _PixGlassBackdrop;
            float4 _PixGlassBackdropScale;   // xy: valid uv extent of the (scaled) blur texture
            float _PixGlassBackdropOn;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPosition);
                o.color = v.color;
                o.pos = v.uv0;
                o.params1 = v.uv1;
                o.params2 = v.uv2;
                o.params3 = v.uv3;
                o.screenPos = ComputeScreenPos(o.vertex);
                return o;
            }

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            float hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float3 toG(float3 c) { return pow(max(c, 0.0), 1.0 / 2.2); }
            float3 toL(float3 c) { return pow(max(c, 0.0), 2.2); }

            float3 gradient3(float3 a, float3 b, float3 c, float t)
            {
                return t < 0.55 ? lerp(a, b, t / 0.55) : lerp(b, c, (t - 0.55) / 0.45);
            }

            // ---------------------------------------------------------------------------------- Tile (Direction 16) flat branch: solid charcoal surfaces, 2px border, gold focus (4px edge + ring + glow + sheen). No backdrop is sampled.
            // kind: 0 tile body (160deg #2C2F35 -> #1D1F24; density < 0.9 = list-row fill), 1 / 4 gold plate, 2 solid tint (vertex colour), 3 well (panel head), 5 panel, 6 list row (focus = row focus fill + 4px gold bar), 7 tab (off), 8 tab (on: gold + glow)
            fixed4 fragFlat(v2f i)
            {
                float2 p = i.pos.xy;
                float2 half2 = i.pos.zw;
                float radius = min(i.params1.x, min(half2.x, half2.y));
                float edgeW = i.params1.y;
                float focus = i.params1.z;
                float shadowAmt = i.params1.w;
                float sheenAmt = i.params2.x;
                float noiseAmt = i.params2.y;
                int kind = (int)(i.params2.w + 0.5);
                float density = i.params3.x;
                float bright = i.params3.y;
                float glowScale = i.params3.z;
                float focusEdgeOv = i.params3.w;

                float d = sdRoundBox(p, half2, radius);
                float aa = max(fwidth(d), 1e-4);
                float cover = saturate(0.5 - d / aa);
                float fillCover = cover;
                float2 uv = p / (half2 * 2.0) + 0.5;          // 0..1, y up
                float yd = 1.0 - uv.y;                        // y down, like CSS

                float3 rgb;
                float3 edgeCol = _EdgeA.rgb;
                bool goldPlate = (kind == 1 || kind == 4 || kind == 8);
                if (goldPlate) rgb = lerp(_GoldHi.rgb, _Accent.rgb, saturate(yd));
                else if (kind == 2) rgb = toG(i.color.rgb);
                else if (kind == 3) { rgb = _PanelHead.rgb; edgeCol = _EdgeC.rgb; }
                else if (kind == 5) { rgb = _PanelFill.rgb; edgeCol = _EdgeB.rgb; }
                else if (kind == 6) { rgb = lerp(_RowFill.rgb, _RowFocus.rgb, focus); edgeCol = _EdgeC.rgb; }
                else if (kind == 7) { rgb = _TabOff.rgb; }
                else if (kind == 9) { rgb = lerp(_KeyA.rgb, _KeyB.rgb, saturate(yd)); edgeCol = min(_KeyA.rgb * 1.25, 1.0); }
                else if (kind == 10)    // frame: transparent inside (only the border, ring, glow and the focus sheen draw)
                {
                    rgb = float3(1.0, 0.86, 0.59);
                    float ts10 = saturate((uv.x * 0.866 + yd * 0.5) / 1.366);
                    fillCover = cover * _FocusB.y * focus * glowScale * saturate(1.0 - ts10 / 0.4);
                }
                else
                {
                    float tg = saturate((uv.x * 0.342 + yd * 0.940) / 1.282);    // CSS 160deg
                    rgb = density < 0.9 ? _RowFill.rgb : lerp(_SurfTop.rgb, _SurfBot.rgb, tg);
                }
                if (kind != 2) rgb *= toG(i.color.rgb);

                // focus sheen (120deg gold wash fading by 40%), tile-sized surfaces only
                if (focus > 0.001 && (kind == 0 || kind == 5) && half2.y > 50.0)
                {
                    float ts = saturate((uv.x * 0.866 + yd * 0.5) / 1.366);
                    float sh = _FocusB.y * focus * glowScale * saturate(1.0 - ts / 0.4) * sheenAmt;
                    rgb = lerp(rgb, float3(1.0, 0.86, 0.59), sh);
                }
                rgb += bright.xxx;
                rgb += (hash12(i.vertex.xy) - 0.5) * noiseAmt;

                // row: gold bar on the left while focused, 1px hairline along the bottom
                if (kind == 6)
                {
                    float barW = (focusEdgeOv >= 0.0 ? focusEdgeOv : _FocusA.x);
                    float bar = 1.0 - saturate((p.x + half2.x - barW) / aa);
                    rgb = lerp(rgb, _Accent.rgb, bar * saturate(focus * 4.0));
                    if (edgeW > 0.0) { float hl = 1.0 - saturate((p.y + half2.y - edgeW) / aa); rgb = lerp(rgb, edgeCol, hl); }
                }

                // border: 2px at rest, wider and gold when focused (not on rows, tabs and gold plates)
                float ew = edgeW;
                if (goldPlate || kind == 6 || kind == 7) ew = 0.0;
                else if (ew > 0.0)
                {
                    float fe = focusEdgeOv >= 0.0 ? focusEdgeOv : _FocusA.x;
                    ew = lerp(ew, max(ew, fe), focus);
                }
                float edgeMask = 0.0;
                if (ew > 0.0) { float inner = saturate(0.5 - (d + ew) / aa); edgeMask = saturate(cover - inner); }
                edgeCol = lerp(edgeCol, _Accent.rgb, focus);

                float3 col = rgb * fillCover;
                float a = fillCover;
                col = col * (1.0 - edgeMask) + edgeCol * edgeMask;
                a = a * (1.0 - edgeMask) + edgeMask;

                float outside = saturate(d / aa);
                // focus: outer ring (2px, 35%) and glow (46px, 55%)
                if (focus > 0.001 && kind != 6 && kind != 7)
                {
                    float ring = (1.0 - saturate((d - _FocusA.y) / aa)) * outside * _FocusA.z;
                    float gt = saturate(1.0 - max(d, 0.0) / max(_FocusB.x, 1.0));
                    float g = gt * gt * gt * _FocusA.w * 0.5 * outside;
                    float gg = (ring + g) * focus * glowScale;
                    col += _Accent.rgb * gg; a += gg;
                }
                if (kind == 8)    // active tab: static gold glow (24px, 50%)
                {
                    float gt = saturate(1.0 - max(d, 0.0) / 24.0);
                    float gg = gt * gt * gt * 0.5 * glowScale * outside;
                    col += _Accent.rgb * gg; a += gg;
                }
                if (shadowAmt > 0.001)
                {
                    float sd = sdRoundBox(p - float2(0.0, -8.0), half2, radius);
                    float sh = exp(-max(sd, 0.0) / 11.0) * _FocusB.z * shadowAmt * outside;
                    a += sh * (1.0 - a);
                }

                float4 c = float4(toL(col / max(a, 1e-4)) * a, saturate(a));
                c *= i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                c *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a - 0.001);
                #endif
                return c;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_Flat > 0.5) return fragFlat(i);
                float2 p = i.pos.xy;
                float2 half2 = i.pos.zw;
                float radius = min(i.params1.x, min(half2.x, half2.y));
                float edgeW = i.params1.y;
                float focus = i.params1.z;
                float shadowAmt = i.params1.w;
                float sheenAmt = i.params2.x;
                float noiseAmt = i.params2.y;
                float backdropAmt = i.params2.z;
                int kind = (int)(i.params2.w + 0.5);
                if (kind >= 5) kind = (kind == 8 ? 4 : 0);   // Tile kinds drawn with the glass material
                float density = i.params3.x;
                float bright = i.params3.y;
                float glowScale = i.params3.z;

                // signed distance to the rounded rectangle (negative inside), in canvas units; the anti-alias width follows the screen pixel size
                float d = sdRoundBox(p, half2, radius);
                float aa = max(fwidth(d), 1e-4);
                float cover = saturate(0.5 - d / aa);

                // diagonal parameter 0 (top left) .. 1 (bottom right), like the pack's gradients (x2=1, y2=0.6)
                float2 uv = p / (half2 * 2.0) + 0.5;                  // 0..1, y up
                float t = saturate((uv.x + (1.0 - uv.y) * 0.6) / 1.6);

                float3 rgb;
                if (kind == 1)            // action: silver gradient
                {
                    rgb = lerp(_ActionA.rgb, _ActionB.rgb, saturate(uv.x));
                }
                else if (kind == 2)       // solid tint
                {
                    rgb = toG(i.color.rgb);
                }
                else if (kind == 4)       // accent fill
                {
                    rgb = lerp(_Accent.rgb * 1.08, _Accent.rgb * 0.9, saturate(1.0 - uv.y));
                }
                else
                {
                    // backdrop: the blurred scene, refracted a little near the rim, desaturated and dimmed ("frosted")
                    float2 suv = i.screenPos.xy / max(i.screenPos.w, 1e-5);
                    float rim = saturate(1.0 + d / 18.0);              // 1 at the edge, 0 deeper than 18 units inside
                    float2 gdir = float2(ddx(d), ddy(d));
                    float gl = max(length(gdir), 1e-5);
                    float2 off = -(gdir / gl) * rim * rim * _Refraction;
                    float2 buv = (suv + off) * _PixGlassBackdropScale.xy;
                    float3 bd = toG(tex2D(_PixGlassBackdrop, buv).rgb);   // the blurred frame is sampled linear; everything below composes in gamma space like the pack art
                    float lum = dot(bd, float3(0.299, 0.587, 0.114));
                    bd = lerp(bd, lum.xxx, _Desat);
                    bd *= (1.0 - _Dim);
                    // flat frosted fallback (Low tier / no blur available): a quiet graphite gradient instead of the live blur
                    float3 frost = lerp(_FrostHigh.rgb, _FrostLow.rgb, saturate(t * 1.4));
                    float live = _PixGlassBackdropOn * backdropAmt;
                    bd = lerp(frost, bd, live);

                    float3 tint = gradient3(_SurfTop.rgb, _SurfMid.rgb, _SurfBot.rgb, t);
                    float ta = t < 0.55 ? lerp(_SurfTop.a, _SurfMid.a, t / 0.55) : lerp(_SurfMid.a, _SurfBot.a, (t - 0.55) / 0.45);
                    ta = saturate(ta * density);
                    if (kind == 3) { tint = float3(0.106, 0.106, 0.114); ta = saturate(0.6 * density); }
                    tint *= toG(i.color.rgb);
                    rgb = bd * (1.0 - ta) + tint * ta;
                }

                // diagonal reflection highlights (pack: .13 at the start, a second faint band near the far corner)
                {
                    float band1 = smoothstep(0.0, 0.02, t) * (1.0 - smoothstep(0.02, 0.12, t)) * 0.13 + smoothstep(0.12, 0.165, t) * (1.0 - smoothstep(0.165, 0.18, t)) * 0.09;
                    float band2 = smoothstep(0.73, 0.86, t) * (1.0 - smoothstep(0.86, 1.0, t)) * 0.06;
                    float s = (band1 + band2) * sheenAmt * (kind == 1 ? 0.0 : 1.0);
                    rgb += s.xxx;
                    rgb += (kind == 0 ? 0.02 : 0.0) * saturate(1.0 - t * 1.4) * sheenAmt;
                }
                rgb += bright.xxx;

                // subtle noise (kills banding of the dark gradient)
                rgb += (hash12(i.vertex.xy) - 0.5) * noiseAmt;   // gamma-space dither

                // edge hairline (inside the shape) with the pack's silver gradient, orange when focused
                float edgeMask = 0.0;
                if (edgeW > 0.0)
                {
                    float inner = saturate(0.5 - (d + edgeW) / aa);
                    edgeMask = saturate(cover - inner);
                }
                float3 edgeCol = gradient3(_EdgeA.rgb, _EdgeB.rgb, _EdgeC.rgb, t);
                edgeCol = lerp(edgeCol, _Accent.rgb, focus);
                float edgeA = lerp(_EdgeAlpha, 1.0, focus);
                if (kind == 1) { edgeCol = lerp(float3(1, 1, 1), _Accent.rgb, focus); edgeA = 0.7; }
                if (kind == 2 || kind == 4) edgeA *= 0.55;

                // composite (premultiplied): opaque fill, then the edge over it
                float3 col = rgb * cover;
                float a = cover;
                col = col * (1.0 - edgeMask * edgeA) + edgeCol * (edgeMask * edgeA);
                a = a * (1.0 - edgeMask * edgeA) + edgeMask * edgeA;

                // outer glow (focus) and soft drop shadow, outside the rounded rectangle
                float outside = saturate(d / aa);
                if (focus > 0.001)
                {
                    float g = exp(-max(d, 0.0) / 7.0) * focus * 0.36 * glowScale * outside;
                    col += _Accent.rgb * g;
                    a += g;
                }
                if (shadowAmt > 0.001)
                {
                    float sd = sdRoundBox(p - float2(0.0, -7.0), half2, radius);
                    float sh = exp(-max(sd, 0.0) / 14.0) * 0.38 * shadowAmt * outside;
                    a += sh * (1.0 - a);
                }

                float4 c = float4(toL(col / max(a, 1e-4)) * a, saturate(a));   // back to linear for the sRGB framebuffer (colour is premultiplied)
                c *= i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                c *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a - 0.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
