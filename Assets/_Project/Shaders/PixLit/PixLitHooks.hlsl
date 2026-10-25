// PIX/Lit hooks (v0.6 "Graphics pass 1"). Included by PixLitData.hlsl right before GetSurfaceAndBuiltinData.
// PIX/Lit is HDRP/Lit with a few extra material modes selected by _PixMode and a set of global weather parameters:
//   0 standard    stadium surfaces: wetness (darker, glossier, puddles, rain ripples) and snow cover on up-facing surfaces
//   1 turf        procedural mowing stripes, macro variation, blade micro detail, wear map (scuffs / mud / snow trails), natural or artificial
//   2 uniform     team colour masks, jersey numbers, region based dirt / grass stains, fabric weave + fuzz sheen, wet cloth
//   3 helmet      glossy shell with scuffs and scratches that accumulate
//   4 markings    painted lines with chipped paint, wear from play and snow cover
// Globals (Shader.SetGlobal*): _PixWeather (wet, snow, time, rain), _PixWeather2 (puddles, ripples, sheen illumination, 0), _PixWearRect, _PixWearTex.

#ifndef PIX_LIT_HOOKS_INCLUDED
#define PIX_LIT_HOOKS_INCLUDED

float4 _PixWeather;
float4 _PixWeather2;
float4 _PixWearRect;
float _PixDebug;
float _PixLegacy;   // 1 = render the v0.5 look (plain turf / markings), set by the gallery
TEXTURE2D(_PixWearTex);
SAMPLER(sampler_PixWearTex);

static const float PIX_NUM_ADVANCE = 0.6667;   // width of one digit cell in height units (== NumberAtlas.Advance on the C# side)
float PixHash(float2 p)
{
    // sin-free hash (Dave Hoskins): no lattice artefacts like the straight-edged puddles a weaker hash gave
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float PixNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = PixHash(i), b = PixHash(i + float2(1, 0)), c = PixHash(i + float2(0, 1)), d = PixHash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float PixFbm(float2 p)
{
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 3; i++) { v += a * PixNoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
    return v;
}

// NOTE: HDRP Lit runs with surface gradient normal mapping (SURFACE_GRADIENT): "normalTS" is a surface gradient (zero = flat), not a unit normal.
// Gradients are added to combine bumps, scaled down to flatten. Never normalize them.
float3 PixGradFromTex(float4 packed, float scale, float3 T, float3 B) { return SurfaceGradientFromTBN(UnpackDerivativeNormalRGorAG(packed, scale), T, B); }

// rain ripples on wet horizontal surfaces: a sparse field of expanding rings, returned as a normal gradient
float2 PixRipple(float2 xz, float t, float rain)
{
    float2 p = xz * 4.5;
    float2 cell = floor(p);
    float2 f = frac(p) - 0.5;
    float h = PixHash(cell);
    float life = frac(t * (0.55 + 0.5 * h) + h * 7.0);
    float r = length(f);
    float ring = r - life * 0.5;
    float w = exp(-ring * ring * 220.0) * (1.0 - life) * step(h, 0.25 + 0.7 * rain);
    float2 dir = f / max(r, 1e-3);
    return dir * w * sin(ring * 55.0);
}

float4 PixSampleWear(float3 posWS)
{
    float2 uv = (posWS.xz - _PixWearRect.xy) * _PixWearRect.zw;
    float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
    return SAMPLE_TEXTURE2D_LOD(_PixWearTex, sampler_PixWearTex, uv, 0) * inside;
}

// wetness / snow shared by every mode. up = vertical normal component (1 on flat ground).
void PixWeatherSurface(float3 posWS, float up, float wetScale, float snowScale, float2 wearSoilTrail,
                       inout float3 albedo, inout float smooth, inout float metallic, inout float3 normalTS)
{
    float wet = _PixWeather.x * wetScale;
    float snow = _PixWeather.y * snowScale;
    float rain = _PixWeather.w;
    if (((int)_PixDebug & 16) != 0) return;
    float dielectric = 1.0 - saturate(metallic * 1.5);

    if (wet > 0.001)
    {
        if (((int)_PixDebug & 1) == 0) albedo *= lerp(1.0, 0.7, wet * dielectric);
        if (((int)_PixDebug & 2) == 0) smooth = lerp(smooth, max(smooth, 0.5), wet * saturate(0.35 + up) * dielectric);
        float pud = _PixWeather2.x * wet * smoothstep(0.55, 0.66, PixFbm(posWS.xz * 0.11 + 11.0)) * saturate(up * 4.0 - 2.4) * dielectric;
        smooth = lerp(smooth, 0.9, pud);
        albedo *= lerp(1.0, 0.72, pud);
        normalTS *= (1.0 - pud * 0.92);
        float2 rp = PixRipple(posWS.xz, _PixWeather.z, rain) * _PixWeather2.y * saturate(up * 3.0 - 1.4) * wet;
        normalTS += float3(rp.x, 0.0, rp.y) * 0.5;
    }

    if (snow > 0.001)
    {
        float breakup = (PixFbm(posWS.xz * 0.7) - 0.5) * 0.7 + (PixNoise(posWS.xz * 9.0) - 0.5) * 0.25;
        float cover = saturate(snow * 1.35 + breakup * saturate(1.0 - snow * 1.1)) * saturate((up - 0.45) * 3.0);
        cover *= 1.0 - saturate(wearSoilTrail.x * 1.2 + wearSoilTrail.y * 1.6);
        float3 snowCol = float3(0.80, 0.84, 0.90) * (0.92 + 0.08 * PixNoise(posWS.xz * 31.0));
        albedo = lerp(albedo, snowCol, cover);
        smooth = lerp(smooth, 0.28, cover);
        metallic = lerp(metallic, 0.0, cover);
        float2 sn = (float2(PixNoise(posWS.xz * 13.0), PixNoise(posWS.xz * 13.0 + 5.2)) - 0.5) * 0.18;
        normalTS = normalTS * (1.0 - cover * 0.85) + float3(sn.x, 0.0, sn.y) * cover;
    }
}

// ----------------------------------------------------------------------------------------------- jersey numbers
// placement = (centre u, centre v, height in uv units (negative = mirrored), rotation radians). Digits are laid out
// exactly like NumberAtlas.Layout: one digit centred, two digits at +/- advance / 2, atlas 5 columns x 2 rows (0..4 on the top row).
float2 PixNumberMask(float2 uv, float4 placement, float4 number)
{
    float h = abs(placement.z);
    float2 d = uv - placement.xy;
    float s, c;
    sincos(placement.w, s, c);
    float2 q = float2(c * d.x + s * d.y, -s * d.x + c * d.y) / max(h, 1e-5);
    q.x *= (placement.z < 0.0) ? -1.0 : 1.0;
    int count = (int)(number.z + 0.5);
    float2 mask = 0;
    [unroll] for (int i = 0; i < 2; i++)
    {
        if (i >= count) break;
        float centre = (count == 1) ? 0.0 : (i == 0 ? -0.5 : 0.5) * PIX_NUM_ADVANCE;
        float digit = (count == 1) ? number.y : (i == 0 ? number.x : number.y);
        float2 local = float2((q.x - centre) / PIX_NUM_ADVANCE + 0.5, q.y + 0.5);
        if (local.x < 0.0 || local.x > 1.0 || local.y < 0.0 || local.y > 1.0) continue;
        float col = fmod(digit, 5.0);
        float rowFromBottom = digit < 4.5 ? 1.0 : 0.0;
        float2 auv = float2((col + local.x) * 0.2, (rowFromBottom + local.y) * 0.5);
        float2 g = SAMPLE_TEXTURE2D(_PixNumbers, sampler_PixNumbers, auv).rg;
        mask = max(mask, g);
    }
    return mask;
}


// ----------------------------------------------------------------------------------------------- jersey text (player name, team wordmark)
// 16 character slots (atlas indices, -1 = unused), 7 x 5 letter atlas, cell aspect == TextAtlas.Advance; same layout as TextAtlas.Sample.
static const float PIX_TEXT_ADV = 0.62;
float PixTextMask(float2 uv, float4 place, float4 c0, float4 c1, float4 c2, float4 c3, float count, float maxW)
{
    float h = abs(place.z);
    float2 d = uv - place.xy;
    float s, c;
    sincos(place.w, s, c);
    float2 q = float2(c * d.x + s * d.y, -s * d.x + c * d.y) / max(h, 1e-5);
    q.x *= (place.z < 0.0) ? -1.0 : 1.0;
    float2 dq = fwidth(q);                                  // before any branch: derivatives must stay uniform
    float mip = max(0.0, log2(max(dq.y * 192.0, 1.0)));
    if (count < 0.5) return 0.0;
    float natural = count * PIX_TEXT_ADV * h;
    float squeeze = natural <= maxW ? 1.0 : max(0.35, maxW / natural);
    float a = PIX_TEXT_ADV * squeeze;
    float lx = (q.x + 0.5 * count * a) / a;
    if (lx < 0.0 || lx >= count || abs(q.y) > 0.5) return 0.0;
    int i = (int)floor(lx);
    float fu = lx - i, fv = q.y + 0.5;
    float4 cs[4] = { c0, c1, c2, c3 };
    float ch = cs[i >> 2][i & 3];
    if (ch < -0.5 || ch > 28.5) return 0.0;
    float col = fmod(ch, 7.0), row = floor(ch / 7.0);
    float2 auv = float2((col + fu) / 7.0, (4.0 - row + fv) / 5.0);
    return SAMPLE_TEXTURE2D_LOD(_PixLetters, sampler_PixLetters, auv, mip).r;
}
// Dilated copy of a text mask (outline ring around the letters): 8 taps at ~7 percent of the letter height. Used so lettering reads on any jersey colour.
float PixTextOutline(float2 uv, float4 place, float4 c0, float4 c1, float4 c2, float4 c3, float count, float maxW, float fill)
{
    if (count < 0.5) return fill;
    float w = abs(place.z) * 0.07, d = w * 0.7071;
    float o = fill;
    o = max(o, PixTextMask(uv + float2(w, 0), place, c0, c1, c2, c3, count, maxW));
    o = max(o, PixTextMask(uv - float2(w, 0), place, c0, c1, c2, c3, count, maxW));
    o = max(o, PixTextMask(uv + float2(0, w), place, c0, c1, c2, c3, count, maxW));
    o = max(o, PixTextMask(uv - float2(0, w), place, c0, c1, c2, c3, count, maxW));
    o = max(o, PixTextMask(uv + float2(d, d), place, c0, c1, c2, c3, count, maxW));
    o = max(o, PixTextMask(uv - float2(d, d), place, c0, c1, c2, c3, count, maxW));
    o = max(o, PixTextMask(uv + float2(d, -d), place, c0, c1, c2, c3, count, maxW));
    o = max(o, PixTextMask(uv + float2(-d, d), place, c0, c1, c2, c3, count, maxW));
    return o;
}
// ----------------------------------------------------------------------------------------------- the hook
// Runs after GetSurfaceData (before the normal is converted to world space and decals are applied).
void PixSurfaceHook(FragInputs input, LayerTexCoord ltc, float3 V, inout SurfaceData sd, inout float3 normalTS, inout float alpha, out float3 pixEmissive)
{
    pixEmissive = 0;
    int mode = (int)(_PixMode + 0.5);
    float3 posWS = GetAbsolutePositionWS(input.positionRWS);
    float3 vN = normalize(input.tangentToWorld[2]);
    float up = saturate(vN.y);
    float2 uv = ltc.base.uv;

    if (mode == 0)
    {
        if (_PixWeather.x > 0.001 || _PixWeather.y > 0.001)
            PixWeatherSurface(posWS, up, _PixWetAmount, _PixSnowAmount, 0, sd.baseColor, sd.perceptualSmoothness, sd.metallic, normalTS);
    }
    else if (mode == 1)   // ------------------------------------------------------------------ turf
    {
        if (_PixLegacy > 0.5) { sd.perceptualSmoothness = 0.18; sd.baseColor *= float3(0.95, 0.947, 0.97); return; }
        float4 wear = PixSampleWear(posWS);   // R soil / scuff, G mud, B snow trail, A paint wear
        float2 xz = posWS.xz;
        float artificial = _PixTurf.w;
        // 5 yard mowing stripes with a soft edge: the grass bends one way or the other
        float sIdx = posWS.z / (0.9144 * _PixTurf.x);
        float par = fmod(abs(floor(sIdx)), 2.0);
        float edge = abs(frac(sIdx) - 0.5);
        float stripe = lerp(1.0 - _PixTurf.y, 1.0 + _PixTurf.y, par);
        float macro = PixFbm(xz * 0.055);
        float macro2 = PixFbm(xz * 0.42 + 3.0);
        float3 macroTint = lerp(float3(0.86, 0.97, 0.78), float3(1.12, 1.04, 0.84), macro) * lerp(0.93, 1.07, macro2);
        sd.baseColor *= stripe * lerp(1.0, macroTint, _PixTurf.z);
        float2 mUV = xz * _PixMicro.x;
        float micro = SAMPLE_TEXTURE2D(_PixMicroTex, sampler_PixMicroTex, mUV).r;
        float micro2 = SAMPLE_TEXTURE2D(_PixMicroTex, sampler_PixMicroTex, mUV * 0.37 + 0.31).r;
        sd.baseColor *= lerp(1.0, 0.7 + 0.6 * micro * micro2 * 1.6, _PixMicro.z);
        float3 mn = PixGradFromTex(SAMPLE_TEXTURE2D(_PixMicroNormal, sampler_PixMicroNormal, mUV), _PixMicro.y, float3(1, 0, 0), float3(0, 0, 1));
        normalTS += mn;
        // v0.7 blade detail: fine blades stretched along the mowing direction with per-blade tone and a tiny normal ripple, fading out with distance (the macro stripes carry the far view)
        float bladeFade = (1.0 - smoothstep(24.0, 95.0, length(input.positionRWS))) * (1.0 - artificial);   // v0.9: the blade detail now reaches the 20-45 m the Classic camera looks at the players from (was 14-55 m)
        float bl = SAMPLE_TEXTURE2D(_PixMicroTex, sampler_PixMicroTex, float2(xz.x * 13.0, xz.y * 3.1)).r;
        float bl2 = SAMPLE_TEXTURE2D(_PixMicroTex, sampler_PixMicroTex, float2(xz.x * 29.0 + 0.2, xz.y * 6.3)).r;
        float blade = bl * 0.55 + bl2 * 0.45;
        sd.baseColor *= lerp(1.0, lerp(float3(0.74, 0.88, 0.58), float3(1.2, 1.12, 0.82), blade), 0.5 * bladeFade);
        normalTS += float3(bl - 0.5, bl2 - 0.5, 0.0) * 0.22 * bladeFade;
        // v0.9: one more scale of variation between the 18 m patches and the blades: 60 cm clumps of lighter / darker, thicker / thinner grass (breaks the tiling of the micro texture at mid distance)
        float clump = PixFbm(xz * 1.7 + 11.0);
        sd.baseColor *= lerp(0.93, 1.07, clump);
        normalTS += float3(clump - 0.5, PixFbm(xz * 1.9 + 31.0) - 0.5, 0.0) * 0.10 * (1.0 - artificial);
        // the sheen of the lay of the grass: stripes that bend away from the camera are lighter and glossier
        sd.perceptualSmoothness = lerp(0.14 + 0.05 * par, 0.34 + 0.06 * par, artificial);
        if (artificial > 0.5)
        {
            // artificial turf: regular fibre tufts with dark rubber infill between the rows
            float2 tuft = frac(xz * 24.0) - 0.5;
            float tuftMask = (1.0 - smoothstep(0.15, 0.5, length(tuft)));
            sd.baseColor *= lerp(0.78, 1.08, tuftMask);
        }
        // wear: scuffed grass / soil, mud
        float soil = smoothstep(0.12, 0.6, wear.r * 1.3 + (micro - 0.5) * 0.45);
        float3 soilCol = float3(0.085, 0.058, 0.035) * (0.7 + 0.7 * micro);
        sd.baseColor = lerp(sd.baseColor, soilCol, soil * 0.85);
        sd.perceptualSmoothness = lerp(sd.perceptualSmoothness, 0.1, soil);
        normalTS *= (1.0 - soil * 0.5);
        float mud = saturate(wear.g * 1.3);
        sd.baseColor *= lerp(1.0, 0.45, mud * (0.4 + 0.6 * _PixWeather.x));
        sd.perceptualSmoothness = lerp(sd.perceptualSmoothness, 0.75, mud * _PixWeather.x);
        sd.metallic = 0.0;
        PixWeatherSurface(posWS, 1.0, 1.0, 1.0, float2(soil, wear.b), sd.baseColor, sd.perceptualSmoothness, sd.metallic, normalTS);
    }
    else if (mode == 2)   // ------------------------------------------------------------------ uniform
    {
        float gray = SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, uv).r;      // grayscale shading of the jersey
        float3 mask = SAMPLE_TEXTURE2D(_PixColorMask, sampler_PixColorMask, uv).rgb;
        // sharpen the packed masks (the licensed graph runs them through a contrast of 3.3)
        float3 m = saturate((mask - 0.5) * 3.3 + 0.5);
        float3 team = _PixPrimary.rgb;
        team = lerp(team, _PixSecondary.rgb, m.g);
        team = lerp(team, _PixTertiary.rgb, m.b);
        float3 albedo = gray * team * 1.12;
        float smooth = sd.perceptualSmoothness;

        // numbers: fill + outline from the atlas on the four placements
        if (_PixNumber.w > 0.5)
        {
            float2 nm = PixNumberMask(uv, _PixNumFront, _PixNumber);
            nm = max(nm, PixNumberMask(uv, _PixNumBack, _PixNumber));
            nm = max(nm, PixNumberMask(uv, _PixNumShoulderL, _PixNumber));
            nm = max(nm, PixNumberMask(uv, _PixNumShoulderR, _PixNumber));
            float3 trim = _PixNumTrim.rgb * lerp(0.85, 1.0, gray);
            float3 fill = _PixNumColor.rgb * lerp(0.75, 1.0, saturate(gray * 1.25));
            albedo = lerp(albedo, trim, saturate(nm.g * _PixNumTrim.a));
            albedo = lerp(albedo, fill, saturate(nm.r));
            smooth = lerp(smooth, 0.2, saturate(max(nm.r, nm.g)));    // screen printed vinyl is flat
        }

        // player name on the back, team wordmark on the chest (block letters in the number colours)
        {
            float nameM = PixTextMask(uv, _PixNamePlace, _PixName0, _PixName1, _PixName2, _PixName3, _PixTextInfo.x, _PixTextInfo.y);
            float wordM = PixTextMask(uv, _PixWordPlace, _PixWord0, _PixWord1, _PixWord2, _PixWord3, _PixTextInfo.z, _PixTextInfo.w);
            float3 fillT = _PixNumColor.rgb * lerp(0.75, 1.0, saturate(gray * 1.25));
            float3 trimT = _PixNumTrim.rgb * lerp(0.85, 1.0, gray);
            // v0.6.0: lettering (name and wordmark) takes the number FILL colour (guaranteed contrast against the jersey) with the trim colour as an outline ring, so dark kits stay legible
            float nameO = PixTextOutline(uv, _PixNamePlace, _PixName0, _PixName1, _PixName2, _PixName3, _PixTextInfo.x, _PixTextInfo.y, nameM);
            float wordO = PixTextOutline(uv, _PixWordPlace, _PixWord0, _PixWord1, _PixWord2, _PixWord3, _PixTextInfo.z, _PixTextInfo.w, wordM);
            albedo = lerp(albedo, trimT, saturate(max(nameO, wordO)) * 0.95);
            albedo = lerp(albedo, fillT, saturate(max(nameM, wordM)) * 0.97);
            smooth = lerp(smooth, 0.2, saturate(max(nameO, wordO)));
        }
        // fabric weave: mesh jersey micro normal
        float2 wUV = uv * _PixFabric.x;
        float3 wn = PixGradFromTex(SAMPLE_TEXTURE2D(_PixMicroNormal, sampler_PixMicroNormal, wUV), _PixFabric.y, ltc.vertexTangentWS0, ltc.vertexBitangentWS0);
        normalTS += wn;

        // dirt, grass stains, mud by region
        float4 reg = SAMPLE_TEXTURE2D(_PixRegions, sampler_PixRegions, uv);
        float intensity = saturate(reg.r * _PixDirtA.x + reg.g * _PixDirtA.y + reg.b * _PixDirtA.z + reg.a * _PixDirtA.w);
        // v0.7: grass streaks and mud smears, not blotches (full coverage of a white kit read as camouflage): the stain is confined to the core of its region, drawn as soft
        // elongated smears (noise stretched along the limb, slightly skewed) with a few small mud splashes; fine grime stays a faint overall tint.
        float dn = SAMPLE_TEXTURE2D(_PixDirtTex, sampler_PixDirtTex, uv * float2(1.0, 1.0)).r;
        float dn2 = SAMPLE_TEXTURE2D(_PixDirtTex, sampler_PixDirtTex, uv * 3.1 + 0.37).r;
        float2 suv = float2(uv.x * 2.2 + uv.y * 0.9, uv.y * 11.0 - uv.x * 1.5);
        float streak = SAMPLE_TEXTURE2D(_PixDirtTex, sampler_PixDirtTex, suv).r * 0.7 + SAMPLE_TEXTURE2D(_PixDirtTex, sampler_PixDirtTex, suv * float2(0.45, 0.3) + 0.61).r * 0.45;
        float grime = _PixDirtB.z;
        float core = smoothstep(0.12, 0.8, intensity);                                        // only where this region really took the hits
        float thr = lerp(0.98, 0.5, pow(intensity, 0.8));
        float smear = smoothstep(thr, thr + 0.32, streak) * core;
        float splash = smoothstep(0.86 - intensity * 0.12, 0.93, dn2 * 0.6 + dn * 0.55) * core * saturate(_PixDirtB.y * 0.8 + 0.25);   // small mud splatter
        float dirtMask = saturate(max(smear, splash));
        dirtMask = saturate(dirtMask + grime * 0.12 * (0.5 + 0.5 * dn));
        float grassFrac = _PixDirtB.x;
        float3 soilCol = _PixDirtColor.rgb;
        float3 grassCol = float3(0.13, 0.20, 0.05);
        float3 stain = lerp(soilCol, grassCol, grassFrac * saturate(0.5 + smear));
        stain *= 0.8 + 0.4 * dn2;
        albedo = lerp(albedo, stain, dirtMask * 0.55);
        smooth = lerp(smooth, 0.12, dirtMask);
        normalTS *= (1.0 - dirtMask * 0.35);

        // wet cloth: darker, a little glossier
        float wetCloth = (((int)_PixDebug & 32) != 0) ? 0.0 : _PixWeather.x * 0.65;
        albedo *= lerp(1.0, 0.72, wetCloth);
        smooth = lerp(smooth, smooth + 0.18, wetCloth);

        // fuzz sheen: cotton / mesh fibres catch grazing light; approximated as a view dependent term scaled by the scene illumination
        float rim = pow(saturate(1.0 - saturate(dot(vN, V))), max(_PixFabric.w, 0.5));
        pixEmissive = albedo * rim * _PixFabric.z * _PixWeather2.z * (1.0 - dirtMask * 0.7);

        // snow settles on the shoulders
        float snowCover = _PixWeather.y * saturate((vN.y - 0.5) * 3.0) * saturate(reg.a * 1.5) * 0.55;
        albedo = lerp(albedo, float3(0.8, 0.84, 0.9), snowCover);

        if (((int)_PixDebug & 128) != 0) albedo = _PixTertiary.rgb;
        if (((int)_PixDebug & 256) != 0) albedo = float3(0, 1, 0);
        sd.baseColor = albedo;
        sd.perceptualSmoothness = smooth;
        sd.metallic = 0.0;
    }
    else if (mode == 3)   // ------------------------------------------------------------------ helmet shell scuffs
    {
        // team paint: R = shell, G / B = stripe channels, white = bare plastic / hardware (keeps the licensed base map)
        float3 mk = SAMPLE_TEXTURE2D(_PixColorMask, sampler_PixColorMask, uv).rgb;
        float shellW = saturate(mk.r * 1.2);
        float secW = saturate(mk.g * (1.0 - mk.r));
        float terW = saturate(mk.b * (1.0 - mk.r));
        float flake = saturate(sd.baseColor.r * 2.0);
        float3 paint = _PixPrimary.rgb * lerp(0.86, 1.14, flake);
        float3 albedo = lerp(sd.baseColor, paint, shellW);
        albedo = lerp(albedo, _PixSecondary.rgb, secW);
        albedo = lerp(albedo, _PixTertiary.rgb, terW);
        float3 hxD = 0, hnD = 0;
        // helmet frame (x right, y up, z forward; units of the shell half extents) from the object space axes the avatar measured at bind pose
        {
            float3 pW = GetAbsolutePositionWS(input.positionRWS);
            float3 hx = float3(dot(_PixHW0, float4(pW, 1.0)), dot(_PixHW1, float4(pW, 1.0)), dot(_PixHW2, float4(pW, 1.0)));   // helmet space: x right, y up, z forward, 1 = half extent of the shell
            float3 wn = input.tangentToWorld[2];
            float3 hn = normalize(float3(dot(_PixHN0.xyz, wn), dot(_PixHN1.xyz, wn), dot(_PixHN2.xyz, wn)));
            hxD = hx; hnD = hn;
            // centre stripe over the crown: secondary colour with a thin outline of the third
            float crown = smoothstep(0.1, 0.35, hn.y) * smoothstep(-0.55, -0.2, hx.y) * smoothstep(-1.0, -0.6, hx.z);   // fades out over the nape (QA-935)
            float core = (1.0 - smoothstep(0.085, 0.115, abs(hx.x))) * crown;
            float edge = (1.0 - smoothstep(0.17, 0.2, abs(hx.x))) * crown * (1.0 - core);
            albedo = lerp(albedo, _PixTertiary.rgb, edge);
            albedo = lerp(albedo, _PixSecondary.rgb, core);
            // team crest on both sides
            float side = hx.x >= 0.0 ? 1.0 : -1.0;
            float2 duv = float2((hx.z - _PixHelmetDecal.z) * side * _PixHelmetDecal.w, hx.y - _PixHelmetDecal.y) / max(_PixHelmetDecal.x, 1e-3) * 0.5 + 0.5;
            float4 cr = SAMPLE_TEXTURE2D(_PixDecalTex, sampler_PixDecalTex, duv);
            float inRect = step(0.0, duv.x) * step(duv.x, 1.0) * step(0.0, duv.y) * step(duv.y, 1.0);
            float dm = cr.a * inRect * smoothstep(0.45, 0.75, abs(hn.x)) * smoothstep(-0.3, -0.1, hx.z);   // never behind the ear (QA-936)
            albedo = lerp(albedo, cr.rgb, dm * 0.97);
            sd.perceptualSmoothness = lerp(sd.perceptualSmoothness, 0.55, dm);
            sd.metallic = lerp(sd.metallic, 0.0, max(dm, core));
        }
        if (((int)_PixDebug & 128) != 0) albedo = _PixTertiary.rgb;
        if (((int)_PixDebug & 256) != 0) albedo = float3(0, 1, 0);
        if (((int)_PixDebug & 512) != 0) albedo = saturate(float3(hxD.x, hxD.y, hxD.z) * 0.5 + 0.5);
        if (((int)_PixDebug & 1024) != 0) albedo = saturate(hnD * 0.5 + 0.5);
        sd.baseColor = albedo;
        sd.metallic = lerp(sd.metallic, 0.12, shellW);                         // v0.12.0 BODIES (cross-track edit, helmet branch only): satin like R1 (was metallic 0.35 / smoothness 0.9 = mirror plastic)
        sd.perceptualSmoothness = lerp(sd.perceptualSmoothness, 0.66, shellW);
        float scuff = _PixScuff.x;
        if (scuff > 0.001)
        {
            float s1 = SAMPLE_TEXTURE2D(_PixScuffTex, sampler_PixScuffTex, uv * _PixScuff.y).r;
            float s2 = SAMPLE_TEXTURE2D(_PixScuffTex, sampler_PixScuffTex, uv * _PixScuff.y * 2.7 + 0.5).g;
            float v = saturate((s1 * 0.8 + s2 * 0.5) * scuff * 1.5 - (1.0 - scuff) * 0.35);
            sd.baseColor = lerp(sd.baseColor, float3(0.55, 0.55, 0.52) * (0.7 + 0.6 * s2), v * 0.55);   // paint scraped to primer / grass smears
            sd.perceptualSmoothness = lerp(sd.perceptualSmoothness, 0.25, v);
            sd.metallic = lerp(sd.metallic, 0.0, v);
            normalTS += (ltc.vertexTangentWS0 * (s1 - 0.5) + ltc.vertexBitangentWS0 * (s2 - 0.5)) * 0.25 * v;
        }
        if (_PixWeather.x > 0.001 && (((int)_PixDebug & 64) == 0)) { sd.perceptualSmoothness = lerp(sd.perceptualSmoothness, 0.97, _PixWeather.x * 0.35 * (1.0 - _PixScuff.x)); }
        float snowH = _PixWeather.y * saturate((vN.y - 0.7) * 4.0) * 0.35;
        sd.baseColor = lerp(sd.baseColor, float3(0.8, 0.84, 0.9), snowH);
    }
    else if (mode == 4)   // ------------------------------------------------------------------ painted lines
    {
        if (_PixLegacy > 0.5) return;
        float4 wear = PixSampleWear(posWS);
        float2 xz = posWS.xz;
        float chip = smoothstep(0.58, 0.80, PixNoise(xz * 7.0) * 0.6 + PixNoise(xz * 29.0) * 0.4);
        float scuffed = saturate(wear.a * 1.1 + wear.r * 0.7);
        alpha *= saturate(1.0 - 0.18 * chip - 0.6 * scuffed);
        sd.baseColor *= lerp(1.0, 0.82, PixNoise(xz * 3.0));
        float wetM = _PixWeather.x;
        sd.baseColor *= lerp(1.0, 0.78, wetM);
        sd.perceptualSmoothness = lerp(0.1, 0.72, wetM);
        float snowCover = saturate(_PixWeather.y * 1.2) * (1.0 - saturate(wear.b * 1.5));
        alpha *= 1.0 - 0.8 * snowCover;
    }
}

#endif
