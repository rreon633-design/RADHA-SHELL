// RADHA SHELL — dual-Kawase blur (down/up sample pair).
// Used by the WinUI host as a Win2D custom effect on the panel backdrop, standing in
// for layershell blur on Wayland. Feed with CompositionEffectBrush and animate the
// blend radius with a spring for the "morph" feel.
//
// Usage (C# + Win2D): compile with dxc -T ps_6_0 (see README.md) and wrap the .cso
// in an EffectGraphManager before setting it as a CompositionEffectBrush.

Texture2D<float4> Source     : register(t0);
SamplerState      SourceSampler : register(s0);

cbuffer BlurConstants : register(b0)
{
    float2 TexelSize;    // 1/width, 1/height of the source
    float  Radius;       // 1.0 .. 6.0 in pixels
    float  Offset;       // half-pixel offset for the up-sample pass
};

static const float K = 0.5f;

float4 SampleOffset(float2 uv, float2 delta)
{
    return Source.Sample(SourceSampler, uv + delta * TexelSize);
}

float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET
{
    float2 o = Offset * TexelSize;

    float4 sum = SampleOffset(uv, float2( 1.0,  1.0) * K * Radius + o)
               + SampleOffset(uv, float2( 1.0, -1.0) * K * Radius + o)
               + SampleOffset(uv, float2(-1.0,  1.0) * K * Radius + o)
               + SampleOffset(uv, float2(-1.0, -1.0) * K * Radius + o);

    return sum * 0.25f;
}
