// Copyright (c) Le Loc Tai <leloctai.com> . All rights reserved. Do not redistribute.

#include "./common.hlsl"
#include "./fullscreen.hlsl"

float2 _Offset;
float2 _TargetSize;
float4 _CropRegion;
half3  _BackgroundColor;

int _IsLast;
// int _IsUp;

#define BlurVertexInput FullScreenVertexInput

struct BlurVertexOutput
{
    float4 position : SV_POSITION;
    float2 texcoord : TEXCOORD0;
    UNITY_VERTEX_OUTPUT_STEREO
};

BlurVertexOutput VertBlur(BlurVertexInput v)
{
    BlurVertexOutput o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

    float4 posCS;
    float2 screenUV;
    GetFullScreenVertexData(v, posCS, screenUV);

    o.position = half4(posCS.xy, 0.0, 1.0);

    o.texcoord = UnCropUV(screenUV, _CropRegion);

    return o;
}

half4 FragBlur(BlurVertexOutput i) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

    half2 dirs[] = {
        half2(-1, -1),
        half2(1, -1),
        half2(1, 1),
        half2(-1, 1),
    };
    #if !USE_EXTRA_SAMPLE
    half weight0 = 1.0h / 4.0h;
    #else
    half weight1 = 1.0h / 5.0h;
    half weight0 = (1.0h - weight1) / 4.0h;
    #endif

    half4 o = 0;
    for (int j = 0; j < 4; ++j)
        o += SAMPLE_SCREEN_TEX(_MainTex, i.texcoord + dirs[j] * _Offset) * weight0;

    #if USE_EXTRA_SAMPLE
    o += SAMPLE_SCREEN_TEX(_MainTex, i.texcoord) * weight1;
    #endif

    #if BACKGROUND_FILL_COLOR
    o.rgb = lerp(_BackgroundColor, o.rgb, o.a);
    o.a = 1.0h;
    #endif

    // dithering every pass is more correct, but combined with the low sample rate and cheap pattern introduces too much error
    if (_IsLast)
        o = dither(o, i.texcoord * _TargetSize);

    return o;
}
