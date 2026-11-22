Shader "Hidden/DepthNormalPP/Edges"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask RGB

        // The original-material pass marks covered camera samples. Keep the line inside
        // the mesh even with MSAA or a reduced-resolution depth/normal texture.
        Stencil
        {
            Ref 1
            ReadMask 1
            Comp Equal
            Pass Keep
        }

        HLSLINCLUDE
        // Each operator uses a distinct pass index. Select the real pass on its first editor draw.
        #pragma editor_sync_compilation

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "DepthNormalPacking.hlsl"

        TEXTURE2D_X(_OutlineDepthNormalTexture);
        SAMPLER(sampler_PointClamp);
        TEXTURE2D_X(_OutlineCoverageTexture);
        TEXTURE2D_X(_OutlineSmaaEdges);
        TEXTURE2D_X(_OutlineSmaaWeights);
        SAMPLER(sampler_LinearClamp);
        float2 _OutlineCoverageStep;
        float4 _OutlineCoverageGrid; // width, height, 1/width, 1/height
        float2 _OutlineSampleStep;
        float2 _OutlineThresholds;
        half4 _OutlineColor;

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        struct Attributes
        {
            uint vertexID : SV_VertexID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        Varyings Vert(Attributes input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            Varyings output;
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            // DepthNormalRenderer composites into an intermediate RT. Display pre-rotation
            // belongs to the camera's final blit to the Android swapchain.
            float2 corner = float2((input.vertexID << 1) & 2, input.vertexID & 2);
            output.positionCS = float4(corner * 2.0 - 1.0, UNITY_NEAR_CLIP_VALUE, 1.0);
            output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
            return output;
        }

        float4 Tap(float2 uv, float2 offset)
        {
            float2 sampleUV = UnityStereoTransformScreenSpaceTex(uv + offset * _OutlineSampleStep);
            return DecodeOutlineSample(SAMPLE_TEXTURE2D_X(_OutlineDepthNormalTexture, sampler_PointClamp, sampleUV));
        }

        half4 EdgeColor(float normal, float depth, float centerDepth, float minDepth)
        {
            float normalRatio = normal / max(_OutlineThresholds.x, 0.001);
            float depthRatio = depth / max(centerDepth * _OutlineThresholds.y, 2.0 / 65534.0);
            float edge = minDepth < 0 ? 1 : smoothstep(0.8, 1.2, max(normalRatio, depthRatio));
            return half4(_OutlineColor.rgb, _OutlineColor.a * edge);
        }

        half4 GradientColor(float4 gx, float4 gy, float centerDepth, float minDepth)
        {
            float normal = sqrt(dot(gx.xyz, gx.xyz) + dot(gy.xyz, gy.xyz));
            return EdgeColor(normal, length(float2(gx.w, gy.w)), centerDepth, minDepth);
        }

        half4 Roberts(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float4 center = Tap(input.uv, 0);
            float4 topLeft = Tap(input.uv, float2(-1, -1));
            float4 topRight = Tap(input.uv, float2(1, -1));
            float4 bottomLeft = Tap(input.uv, float2(-1, 1));
            float4 bottomRight = Tap(input.uv, float2(1, 1));
            float minDepth = min(center.w, min(min(topLeft.w, topRight.w), min(bottomLeft.w, bottomRight.w)));
            return GradientColor(
                (bottomRight - topLeft) * 0.707107, (topRight - bottomLeft) * 0.707107, center.w, minDepth);
        }

        half4 Gradient3x3(Varyings input, float cornerWeight, float axisWeight, float scale)
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float4 topLeft = Tap(input.uv, float2(-1, -1));
            float4 top = Tap(input.uv, float2(0, -1));
            float4 topRight = Tap(input.uv, float2(1, -1));
            float4 left = Tap(input.uv, float2(-1, 0));
            float4 center = Tap(input.uv, 0);
            float4 right = Tap(input.uv, float2(1, 0));
            float4 bottomLeft = Tap(input.uv, float2(-1, 1));
            float4 bottom = Tap(input.uv, float2(0, 1));
            float4 bottomRight = Tap(input.uv, float2(1, 1));
            float4 gx = (cornerWeight * (topRight - topLeft)
                + axisWeight * (right - left)
                + cornerWeight * (bottomRight - bottomLeft)) * scale;
            float4 gy = (cornerWeight * (bottomLeft - topLeft)
                + axisWeight * (bottom - top)
                + cornerWeight * (bottomRight - topRight)) * scale;
            float minDepth = min(min(topLeft.w, top.w), min(topRight.w, left.w));
            minDepth = min(minDepth, min(min(center.w, right.w), min(bottomLeft.w, min(bottom.w, bottomRight.w))));
            return GradientColor(gx, gy, center.w, minDepth);
        }

        half4 Sobel(Varyings input) : SV_Target
        {
            return Gradient3x3(input, 1.0, 2.0, 0.25);
        }

        half4 Scharr(Varyings input) : SV_Target
        {
            // Match Sobel's response to an axis-aligned unit step.
            return Gradient3x3(input, 3.0, 10.0, 1.0 / 16.0);
        }

        struct CrossSamples
        {
            float4 center;
            float4 left;
            float4 right;
            float4 top;
            float4 bottom;
            float minDepth;
        };

        CrossSamples SampleCross(float2 uv)
        {
            CrossSamples samples;
            samples.center = Tap(uv, 0);
            samples.left = Tap(uv, float2(-1, 0));
            samples.right = Tap(uv, float2(1, 0));
            samples.top = Tap(uv, float2(0, -1));
            samples.bottom = Tap(uv, float2(0, 1));
            samples.minDepth = min(samples.center.w,
                min(min(samples.left.w, samples.right.w), min(samples.top.w, samples.bottom.w)));
            return samples;
        }

        float2 NormalDepthDifference(float4 difference)
        {
            return float2(dot(difference.xyz, difference.xyz), abs(difference.w));
        }

        half4 CrossMax(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            CrossSamples samples = SampleCross(input.uv);
            float2 horizontal = max(
                NormalDepthDifference(samples.left - samples.center),
                NormalDepthDifference(samples.right - samples.center));
            float2 vertical = max(
                NormalDepthDifference(samples.top - samples.center),
                NormalDepthDifference(samples.bottom - samples.center));
            float2 strongest = max(horizontal, vertical);
            return EdgeColor(sqrt(strongest.x), strongest.y, samples.center.w, samples.minDepth);
        }

        half4 Laplacian(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            CrossSamples samples = SampleCross(input.uv);
            float4 curvature = (samples.left - samples.center) + (samples.right - samples.center)
                             + (samples.top - samples.center) + (samples.bottom - samples.center);
            return EdgeColor(length(curvature.xyz), abs(curvature.w), samples.center.w, samples.minDepth);
        }

        half CoverageAt(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(_OutlineCoverageTexture, sampler_PointClamp,
                UnityStereoTransformScreenSpaceTex(uv), 0).r;
        }

        float4 SurfaceAt(float2 uv)
        {
            return DecodeOutlineSample(SAMPLE_TEXTURE2D_X_LOD(_OutlineDepthNormalTexture,
                sampler_PointClamp, UnityStereoTransformScreenSpaceTex(uv), 0));
        }

        bool SameSurface(float4 center, float4 other)
        {
            // Keep AA conservative even when the user raises the edge detector thresholds.
            float depthTolerance = max(2.0 / 65534.0, min(center.w, other.w) * min(_OutlineThresholds.y, 0.01));
            float normalTolerance = min(_OutlineThresholds.x, 0.5);
            return other.w >= 0 && abs(other.w - center.w) <= depthTolerance
                && dot(center.xyz, other.xyz) >= 1.0 - 0.5 * normalTolerance * normalTolerance;
        }

        // FXAA uses coverage as its contrast signal. The span search estimates geometry
        // only. The final blend uses the two local, surface-checked coverage samples.
        half FxaaCoverage(float2 uv, half center, half left, half right, half top, half bottom)
        {
            half low = min(center, min(min(left, right), min(top, bottom)));
            half high = max(center, max(max(left, right), max(top, bottom)));
            half range = high - low;
            if (range < max(0.03125h, high * 0.125h))
            {
                return center;
            }

            float2 stepUV = _OutlineCoverageGrid.zw;
            half tl = CoverageAt(uv - stepUV);
            half tr = CoverageAt(uv + float2(stepUV.x, -stepUV.y));
            half bl = CoverageAt(uv + float2(-stepUV.x, stepUV.y));
            half br = CoverageAt(uv + stepUV);
            half horizontal = 2.0h * abs(top + bottom - 2.0h * center)
                + abs(tl + bl - 2.0h * left) + abs(tr + br - 2.0h * right);
            half vertical = 2.0h * abs(left + right - 2.0h * center)
                + abs(tl + tr - 2.0h * top) + abs(bl + br - 2.0h * bottom);
            bool alongX = horizontal >= vertical;
            half negative = alongX ? top : left;
            half positive = alongX ? bottom : right;
            bool towardsNegative = abs(negative - center) >= abs(positive - center);
            half neighbor = towardsNegative ? negative : positive;
            half edgeMean = 0.5h * (center + neighbor);
            half stopThreshold = 0.25h * abs(center - neighbor);
            float2 tangent = alongX ? float2(stepUV.x, 0) : float2(0, stepUV.y);
            float2 normal = alongX ? float2(0, stepUV.y) : float2(stepUV.x, 0);
            float2 origin = uv + normal * (towardsNegative ? -0.5 : 0.5);

            float2 distance = 0.0;
            half2 delta = 0.0h;
            bool2 found = false;
            [loop]
            for (int i = 1; i <= 6; ++i)
            {
                if (!found.x)
                {
                    distance.x = i;
                    delta.x = SAMPLE_TEXTURE2D_X_LOD(_OutlineCoverageTexture, sampler_LinearClamp,
                        UnityStereoTransformScreenSpaceTex(origin - tangent * i), 0).r - edgeMean;
                    found.x = abs(delta.x) >= stopThreshold;
                }
                if (!found.y)
                {
                    distance.y = i;
                    delta.y = SAMPLE_TEXTURE2D_X_LOD(_OutlineCoverageTexture, sampler_LinearClamp,
                        UnityStereoTransformScreenSpaceTex(origin + tangent * i), 0).r - edgeMean;
                    found.y = abs(delta.y) >= stopThreshold;
                }
                if (all(found)) break;
            }

            bool nearerLeft = distance.x <= distance.y;
            half endpoint = nearerLeft ? delta.x : delta.y;
            half spanBlend = 0.0h;
            if ((nearerLeft ? found.x : found.y) && ((endpoint < 0) != (center < edgeMean)))
            {
                spanBlend = 0.5h - min(distance.x, distance.y) / (distance.x + distance.y);
            }

            half mean = (2.0h * (left + right + top + bottom) + tl + tr + bl + br) / 12.0h;
            half subpixel = smoothstep(0.0h, 1.0h, saturate(abs(mean - center) / range));
            return lerp(center, neighbor, max(spanBlend, 0.75h * subpixel * subpixel));
        }

        // SMAA 1x neighborhood blending, expressed on scalar coverage. At a texel
        // center each linear tap is a lerp with one of the four validated neighbors.
        half SmaaCoverage(float2 uv, half center, half left, half right, half top, half bottom)
        {
            float2 dx = float2(_OutlineCoverageGrid.z, 0);
            float2 dy = float2(0, _OutlineCoverageGrid.w);
            half4 weights = SAMPLE_TEXTURE2D_X_LOD(_OutlineSmaaWeights, sampler_PointClamp,
                UnityStereoTransformScreenSpaceTex(uv), 0);
            half rightWeight = SAMPLE_TEXTURE2D_X_LOD(_OutlineSmaaWeights, sampler_PointClamp,
                UnityStereoTransformScreenSpaceTex(uv + dx), 0).a;
            half bottomWeight = SAMPLE_TEXTURE2D_X_LOD(_OutlineSmaaWeights, sampler_PointClamp,
                UnityStereoTransformScreenSpaceTex(uv + dy), 0).g;
            bool alongX = max(rightWeight, weights.b) > max(bottomWeight, weights.r);
            half2 pair = alongX ? half2(rightWeight, weights.b) : half2(bottomWeight, weights.r);
            half total = pair.x + pair.y;
            if (total == 0.0h) return center;
            half2 neighbors = alongX ? half2(right, left) : half2(bottom, top);
            return center + dot(pair * pair, neighbors - center) / total;
        }

        // filterMode is constant per fragment entry point; the unused filters compile out.
        half SurfaceCoverage(float2 screenUV, int filterMode)
        {
            float2 cell = screenUV * _OutlineCoverageGrid.xy;
            float2 offset = frac(cell) - 0.5;
            float2 uv = (floor(cell) + 0.5) * _OutlineCoverageGrid.zw;
            float2 dx = float2(_OutlineCoverageGrid.z, 0);
            float2 dy = float2(0, _OutlineCoverageGrid.w);
            float4 center = SurfaceAt(uv);
            // The camera stencil passed. A missing single-sample mask texel here is
            // partial geometry coverage at a silhouette (MSAA or a reduced-size RT).
            if (center.w < 0)
            {
                return 1.0h;
            }

            half centerCoverage = CoverageAt(uv);
            float4 left = SurfaceAt(uv - dx);
            float4 right = SurfaceAt(uv + dx);
            float4 top = SurfaceAt(uv - dy);
            float4 bottom = SurfaceAt(uv + dy);
            // Give a stencil-covered silhouette its full stroke even when a coarse
            // mask texel or a fractional detector step missed the edge.
            if (min(min(left.w, right.w), min(top.w, bottom.w)) < 0)
            {
                return 1.0h;
            }

            // Lock the unfiltered coverage at silhouettes, depth steps and hard creases.
            // Testing after hardware bilinear sampling would already have mixed surfaces.
            bool4 continuous = bool4(SameSurface(center, left), SameSurface(center, right),
                SameSurface(center, top), SameSurface(center, bottom));
            if (filterMode == 0 && !all(continuous))
            {
                return centerCoverage;
            }

            half leftCoverage = CoverageAt(uv - dx);
            half rightCoverage = CoverageAt(uv + dx);
            half topCoverage = CoverageAt(uv - dy);
            half bottomCoverage = CoverageAt(uv + dy);
            // Ignore taps across creases. Smooth along the edge on the current surface.
            leftCoverage = continuous.x ? leftCoverage : centerCoverage;
            rightCoverage = continuous.y ? rightCoverage : centerCoverage;
            topCoverage = continuous.z ? topCoverage : centerCoverage;
            bottomCoverage = continuous.w ? bottomCoverage : centerCoverage;
            half correction;
            if (filterMode == 1)
            {
                correction = FxaaCoverage(uv, centerCoverage, leftCoverage, rightCoverage,
                    topCoverage, bottomCoverage) - centerCoverage;
            }
            else if (filterMode == 2)
            {
                correction = SmaaCoverage(uv, centerCoverage, leftCoverage, rightCoverage,
                    topCoverage, bottomCoverage) - centerCoverage;
            }
            else
            {
                half horizontalChange = abs(leftCoverage - centerCoverage) + abs(rightCoverage - centerCoverage);
                half verticalChange = abs(topCoverage - centerCoverage) + abs(bottomCoverage - centerCoverage);
                half corner = min(horizontalChange, verticalChange) / max(max(horizontalChange, verticalChange), 0.001h);
                correction = ((leftCoverage + rightCoverage + topCoverage + bottomCoverage) * 0.25h - centerCoverage)
                    * (0.5h * corner);
            }

            // At a depth/normal discontinuity never lighten an existing stroke.
            if (!all(continuous)) correction = max(correction, 0.0h);

            float2 scale = _OutlineCoverageStep * _OutlineCoverageGrid.xy;
            if (all(scale >= 0.9999))
            {
                return centerCoverage + correction;
            }

            // At reduced resolution, rebuild the bilinear footprint from point samples.
            // Its diagonal texel can be on a different surface even if both axes passed.
            float2 diagonalUV = uv + (offset.x < 0 ? -dx : dx) + (offset.y < 0 ? -dy : dy);
            if (!SameSurface(center, SurfaceAt(diagonalUV)))
            {
                return centerCoverage;
            }

            float2 fraction = abs(offset);
            half alongX = offset.x < 0 ? leftCoverage : rightCoverage;
            half alongY = offset.y < 0 ? topCoverage : bottomCoverage;
            half baseCoverage = lerp(
                lerp(centerCoverage, alongX, fraction.x),
                lerp(alongY, CoverageAt(diagonalUV), fraction.x),
                fraction.y);
            // Fade the nearest texel's corner correction to zero at its Voronoi boundary.
            half weight = (1.0 - 2.0 * max(fraction.x, fraction.y)) * min(scale.x, scale.y);
            if (!all(continuous)) baseCoverage = max(baseCoverage, centerCoverage);
            return saturate(baseCoverage + correction * weight);
        }

        half4 FilterCoverage(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return half4(_OutlineColor.rgb, _OutlineColor.a * SurfaceCoverage(input.uv, 0));
        }

        half4 FilterFXAA(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return half4(_OutlineColor.rgb, _OutlineColor.a * SurfaceCoverage(input.uv, 1));
        }

        half4 FilterSMAA(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return half4(_OutlineColor.rgb, _OutlineColor.a * SurfaceCoverage(input.uv, 2));
        }

        // Threshold and local contrast adaptation on the unmodified, linear coverage.
        half4 SmaaEdges(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 dx = float2(_OutlineCoverageGrid.z, 0);
            float2 dy = float2(0, _OutlineCoverageGrid.w);
            half center = CoverageAt(input.uv);
            half2 previous = half2(CoverageAt(input.uv - dx), CoverageAt(input.uv - dy));
            half2 delta = abs(center - previous);
            half2 edges = step(0.1h, delta);
            if (!any(edges)) return 0;
            half2 next = half2(CoverageAt(input.uv + dx), CoverageAt(input.uv + dy));
            half2 distant = half2(CoverageAt(input.uv - 2.0 * dx), CoverageAt(input.uv - 2.0 * dy));
            half2 contrast = max(delta, max(abs(center - next), abs(previous - distant)));
            edges *= step(max(contrast.x, contrast.y), 2.0h * delta);
            return half4(edges, 0, 0);
        }
        ENDHLSL

        Pass
        {
            Name "RobertsCross"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Roberts
            ENDHLSL
        }

        Pass
        {
            Name "Sobel"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Sobel
            ENDHLSL
        }

        Pass
        {
            Name "CrossMax"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment CrossMax
            ENDHLSL
        }

        Pass
        {
            Name "Scharr"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Scharr
            ENDHLSL
        }

        Pass
        {
            Name "Laplacian"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Laplacian
            ENDHLSL
        }

        Pass
        {
            Name "FilterCoverage"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FilterCoverage
            ENDHLSL
        }

        Pass
        {
            Name "FilterFXAA"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FilterFXAA
            ENDHLSL
        }

        Pass
        {
            Name "SmaaEdges"
            Blend Off
            ColorMask RGBA
            Stencil { Comp Always Pass Keep WriteMask 0 }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment SmaaEdges
            ENDHLSL
        }

        Pass
        {
            Name "SmaaWeights"
            Blend Off
            ColorMask RGBA
            Stencil { Comp Always Pass Keep WriteMask 0 }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment SmaaWeights

            #define SMAA_HLSL_4_1
            #define SMAA_PRESET_MEDIUM
            #define SMAA_RT_METRICS (_OutlineCoverageGrid.zwxy)
            #define SMAA_AREATEX_SELECT(s) s.rg
            #define SMAA_SEARCHTEX_SELECT(s) s.a
            #define GAMMA_FOR_EDGE_DETECTION 1
            #define LinearSampler sampler_LinearClamp
            #define PointSampler sampler_PointClamp
            TEXTURE2D(_OutlineSmaaArea);
            TEXTURE2D(_OutlineSmaaSearch);
            #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/SubpixelMorphologicalAntialiasing.hlsl"

            float4 SmaaWeights(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = UnityStereoTransformScreenSpaceTex(input.uv);
                float2 pixel;
                float4 offsets[3];
                SMAABlendingWeightCalculationVS(uv, pixel, offsets);
                return SMAABlendingWeightCalculationPS(uv, pixel, offsets,
                    _OutlineSmaaEdges, _OutlineSmaaArea, _OutlineSmaaSearch, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "FilterSMAA"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FilterSMAA
            ENDHLSL
        }
    }
}
