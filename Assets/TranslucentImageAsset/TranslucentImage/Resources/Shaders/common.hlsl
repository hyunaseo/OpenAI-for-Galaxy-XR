// Copyright (c) Le Loc Tai <leloctai.com> . All rights reserved. Do not redistribute.

#ifndef _TRANSLUCENTIMAGE_COMMON
#define _TRANSLUCENTIMAGE_COMMON

float2 UnCropUV(float2 uvRelativeToCropped, float4 cropRegion)
{
    return lerp(cropRegion.xy, cropRegion.zw, uvRelativeToCropped);
}

float2 CropUV(float2 uvRelativeToUnCropped, float4 cropRegion)
{
    return (uvRelativeToUnCropped - cropRegion.xy) / (cropRegion.zw - cropRegion.xy);
}

// https://github.com/michaldrobot/ShaderFastLibs/blob/master/ShaderFastMathLib.h
float sqrtApprox01(float inX)
{
    int x = asint(inX);
    x = 0x1FBD1DF5 + (x >> 1);
    return asfloat(x);
}

// https://github.com/michaldrobot/ShaderFastLibs/blob/master/ShaderFastMathLib.h
float rsqrtApprox01(float inX)
{
    int x = asint(inX);
    x = 0x5F341A43 - (x >> 1);
    return asfloat(x);
}

// https://en.wikibooks.org/wiki/Algorithms/Distance_approximations
float lengthApproxOctagon(float2 v)
{
    float ax = abs(v.x);
    float ay = abs(v.y);
    float l = max(ax, ay);
    float s = min(ax, ay);

    return .41 * s + .941246 * l;
}

// https://extremelearning.com.au/unreasonable-effectiveness-of-quasirandom-sequences/
inline float ggr21(float2 n)
{
    static const float g = 1.32471795724474602596;
    static const float a1 = 1.0 / g;
    static const float a2 = 1.0 / (g * g);
    return frac(a1 * n.x + a2 * n.y);
}

// https://www.shadertoy.com/view/4djSRW
float hash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * .1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

half3 sdfViz(float d)
{
    half3 col = (d > 0.0) ? half3(0.9, 0.6, 0.3) : half3(0.65, 0.85, 1.0);
    col *= 1.0 - exp(-12.0 * abs(d));
    col *= 0.8 + 0.2 * cos(150.0 * d);
    return lerp(col, 1.0, 1.0 - smoothstep(0.0, 0.01, abs(d)));
}

bool isGammaShim()
{
    #ifdef UNITY_COLORSPACE_GAMMA
    return true;
    #else
    return false;
    #endif
}

half3 toPerceptual(half3 color)
{
    return isGammaShim() ? color : sqrt(color);
}

half3 fromPerceptual(half3 color)
{
    return isGammaShim() ? color : color * color;
}

half4 dither(half4 color, float2 coord)
{
    half  noise = ggr21(coord) * 1. / 255. - .5 / 255.;
    half4 result = color;

    result.rgb = toPerceptual(result);
    result += noise;
    result.rgb = fromPerceptual(result);

    return result;
}

#endif
