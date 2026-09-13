#version 460

#include "common/utils.slang"

uniform float g_flOutlineSize = 1;
uniform float g_flOutlineSoftness = 0.8;
uniform float g_flOutlineIntensity = 2.25;
uniform float g_flOutlineFillAlpha = 0;

// Where the selection is seen, and where the scene hides it
uniform vec4 g_vOutlineColor;
uniform vec4 g_vOccludedColor;

// How much of each pixel the selection covers. Red: the selection wherever it is. Green: where the scene does not hide it.
layout (binding = 0) uniform sampler2D g_tOutlineMask;

out vec4 outputColor;

// The line reaches two pixels out of the silhouette and one into it, read from the rings of those sizes around a pixel
const int OuterRadius = 2;

// How sharply the line rises with how mixed the ring around a pixel is
const float OutlineGain = 3.0;

// Nothing is selected outside the image, so the outline closes along its border
vec2 LoadOutlineMask(ivec2 pixelCoords, ivec2 maskTextureSize)
{
    if (any(lessThan(pixelCoords, ivec2(0))) || any(greaterThanEqual(pixelCoords, maskTextureSize)))
    {
        return vec2(0.0);
    }

    return texelFetch(g_tOutlineMask, pixelCoords, 0).rg;
}

void main()
{
    ivec2 maskTextureSize = textureSize(g_tOutlineMask, 0);
    ivec2 pixelCoords = ivec2(gl_FragCoord.xy);

    int outerRadius = max(1, int(floor(float(OuterRadius) * g_flOutlineSize + 0.5)));
    int innerRadius = max(1, int(floor(g_flOutlineSize + 0.5)));
    float outerRingSize = float(4 * outerRadius * (outerRadius + 1));
    float innerRingSize = float(4 * innerRadius * (innerRadius + 1));

    vec2 outerRing = vec2(0.0);
    float innerRing = 0.0;

    for (int y = -outerRadius; y <= outerRadius; y++)
    {
        for (int x = -outerRadius; x <= outerRadius; x++)
        {
            if (x == 0 && y == 0)
            {
                continue;
            }

            vec2 mask = LoadOutlineMask(pixelCoords + ivec2(x, y), maskTextureSize);
            outerRing += mask;

            if (abs(x) <= innerRadius && abs(y) <= innerRadius)
            {
                innerRing += mask.r;
            }
        }
    }

    vec2 center = LoadOutlineMask(pixelCoords, maskTextureSize);

    // Outside the selection the line rises with how much of the wide ring is covered, inside it fades as the
    // narrow ring fills
    float outsideEdge = saturate(OutlineGain * outerRing.r / outerRingSize);
    float insideEdge = saturate(OutlineGain * (1.0 - innerRing / innerRingSize));
    float edge = mix(outsideEdge, insideEdge, center.r);
    edge = saturate(pow(edge, 0.8 / max(g_flOutlineSoftness, 0.01)) * (g_flOutlineIntensity / 2.25));
    edge = max(edge, center.r * g_flOutlineFillAlpha);

    if (edge < 0.001)
    {
        discard;
    }

    // How much of the selection around the pixel is seen
    vec2 coverage = outerRing + center;
    float visibility = coverage.g / max(coverage.r, 1e-4);

    vec4 color = mix(g_vOccludedColor, g_vOutlineColor, saturate(visibility));
    outputColor = vec4(color.rgb, color.a * edge);
}
