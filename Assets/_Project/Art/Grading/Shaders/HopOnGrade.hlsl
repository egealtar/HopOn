// Hop On's colour grade, applied inside the world's own shaders instead of a post-processing pass.
//
// It does what URP's UberPost did with our Volume (Color Adjustments + Vignette), in the same order:
//   1. vignette, before the grade, in linear colour;
//   2. post exposure (2^EV), then clamp to 0..1 (tonemapping None);
//   3. the LDR colour-grading LUT, baked from the Volume by Hop On > Bake Color Grade with URP's own
//      LutBuilderLdr shader, so every Volume setting (contrast, saturation, white balance, curves...)
//      comes out the same.
// The post pass cost ~11 ms a frame on the Realme C55 not for this maths but for its full-screen
// round trip through memory (the scene written out to a texture, read back, written to the screen);
// here each pixel is graded while it is still in the GPU's tile memory, in the pass that draws it.
//
// The values are globals set at start-up by ColorGrade (Resources/ColorGrade.asset); until they are set
// (_HopOnGradeLutParams.w is 0) colours pass through untouched.
#ifndef HOPON_GRADE_INCLUDED
#define HOPON_GRADE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

TEXTURE2D(_HopOnGradeLut);
SAMPLER(sampler_HopOnGradeLut);
float4 _HopOnGradeLutParams;    // x: 1 / LUT width, y: 1 / LUT height, z: LUT height - 1, w: post exposure (linear)
float4 _HopOnVignette;          // x: intensity * 3, y: smoothness * 5, z: rounded (0 or 1), w: unused
float4 _HopOnVignetteCenter;    // xy: centre on the screen, 0..1
float4 _HopOnVignetteColor;     // rgb: linear colour

half3 HopOnGrade(half3 color, float4 positionCS)
{
    if (_HopOnGradeLutParams.w <= 0.0)
    {
        return color;
    }

    // URP ApplyVignette: rounded uses the screen's aspect so the vignette is a circle.
    float2 uv = GetNormalizedScreenSpaceUV(positionCS);
    float2 dist = abs(uv - _HopOnVignetteCenter.xy) * _HopOnVignette.x;
    dist.x *= lerp(1.0, _ScaledScreenParams.x / _ScaledScreenParams.y, _HopOnVignette.z);
    float vfactor = pow(saturate(1.0 - dot(dist, dist)), _HopOnVignette.y);
    color *= lerp(_HopOnVignetteColor.rgb, (1.0).xxx, vfactor);

    // URP ApplyColorGrading, LDR path, tonemapping None.
    color = saturate(color * _HopOnGradeLutParams.w);
    return ApplyLut2D(TEXTURE2D_ARGS(_HopOnGradeLut, sampler_HopOnGradeLut), color, _HopOnGradeLutParams.xyz);
}

#endif
