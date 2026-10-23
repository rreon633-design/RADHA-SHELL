// RADHA SHELL — rounded-rectangle SDF with morphable corner radii.
// Drives the workspace pills, the dashboard sheet outline and the OSD capsule.
// The four radii are animated independently on the CPU (Composition) and
// uploaded per-frame, so a pill can morph into a circle and back.
//
// Sign convention: negative inside, positive outside (Inigo Quilez SDF).

cbuffer MorphConstants : register(b0)
{
    float2 Size;         // target size in DIPs
    float4 Radii;        // top-left, top-right, bottom-right, bottom-left
    float2 Center;       // centre in the same coordinate space
    float  Feather;      // 1.0 / deviceScaleFactor for crisp edges
    float  Fill;         // 0 = outline only, 1 = filled
};

static float RoundedRectSdf(float2 p, float2 halfSize, float4 radii)
{
    // Choose the radius for the quadrant the point lies in.
    float r = (p.x > 0.0f)
        ? ((p.y > 0.0f) ? radii.z : radii.y)
        : ((p.y > 0.0f) ? radii.w : radii.x);

    float2 q = abs(p) - halfSize + r;
    return min(max(q.x, q.y), 0.0f) + length(max(q, 0.0f)) - r;
}

float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET
{
    float2 p = uv * Size - Center;
    float d = RoundedRectSdf(p, Size * 0.5f, Radii);

    // Filled shape with 1px analytic antialiasing.
    float coverage = saturate(0.5f - d / Feather);

    // Thin inner outline for the "outlined pill" state.
    float outline = saturate(1.0f - abs(d + 1.0f) / Feather);

    float4 color = float4(1.0f, 1.0f, 1.0f, coverage * Fill);
    color.rgb = lerp(1.0f, 1.0f, outline);
    return color;
}
