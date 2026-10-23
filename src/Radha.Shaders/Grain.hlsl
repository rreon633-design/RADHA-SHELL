// RADHA SHELL — animated film grain + subtle vignette for glass panels.
// Adds the "material" feel that flat acrylic lacks. Computed in screen space so the
// pattern stays stable while panels move (no crawling noise).

cbuffer GrainConstants : register(b0)
{
    float2 Resolution;
    float  Time;         // seconds, drives the noise evolution
    float  Intensity;    // 0 .. 0.12
    float  Vignette;     // 0 .. 0.6
};

float Hash(float2 p)
{
    p = frac(p * float2(443.897f, 441.423f));
    p += dot(p, p.yx + 19.19f);
    return frac((p.x + p.y) * p.x);
}

float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET
{
    float2 pixel = uv * Resolution;

    // Two octaves of value noise, scrolling slowly in opposite directions.
    float n1 = Hash(pixel + Time * 13.0f);
    float n2 = Hash(pixel * 1.9f - Time * 7.0f);
    float grain = (n1 * 0.62f + n2 * 0.38f - 0.5f) * Intensity;

    float2 centred = uv - 0.5f;
    float vignette = 1.0f - dot(centred, centred) * Vignette;

    float3 rgb = saturate(grain + vignette);
    return float4(rgb, 1.0f);
}
