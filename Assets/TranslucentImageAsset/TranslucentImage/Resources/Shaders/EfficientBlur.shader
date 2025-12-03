// Copyright (c) Le Loc Tai <leloctai.com> . All rights reserved. Do not redistribute.

Shader "Hidden/EfficientBlur"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off

        Pass
        {
            CGPROGRAM
            // #pragma enable_d3d11_debug_symbols
            #pragma vertex VertBlur
            #pragma fragment FragBlur
            #pragma multi_compile_local BACKGROUND_FILL_NONE BACKGROUND_FILL_COLOR
            #pragma multi_compile_local _ USE_EXTRA_SAMPLE

            #include "interop_birp.cginc"

            UNITY_DECLARE_SCREENSPACE_TEXTURE(_MainTex);

            #include "EfficientBlur.hlsl"
            ENDCG
        }
    }

    FallBack Off
}
