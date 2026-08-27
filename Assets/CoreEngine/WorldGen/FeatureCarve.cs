// Assets/CoreEngine/WorldGen/FeatureCarve.cs
//
// Phase 3, file 5 of the spec's ordered list (§13 Phase 3): "per-anchor carve
// kernels (a mountain, a crater, a cave) as plain functions."
//
// Two kernel families, and the distinction matters for the sticky-note economy:
//
//   HEIGHT kernels (Mountain, Crater): pure per-column height DELTAS folded
//   into step 1's heightfield (§5.3). The terrain stays a heightfield surface,
//   so dense bricks stay confined to the surface skin — a mountain does NOT
//   force its whole bounding volume dense.
//
//   VOLUME kernels (Cave): genuinely 3D air carves. Only bricks whose AABB
//   intersects the cave's conservative AABB are forced dense (§5.3 step 3:
//   "a brick … intersected by a feature anchor … becomes dense").
//
// Everything here is a pure function of (anchor, position) — no statics, no
// state — which is what makes the 3a determinism tests possible.
using Unity.Mathematics;

namespace VoxelEngine.WorldGen
{
    public static class FeatureCarve
    {
        // Deterministic per-anchor noise offset, so two anchors of the same
        // kind don't warp identically. Derived from anchor.salt, not from
        // position alone — keeps HeightDelta a pure function of (anchor, vx,
        // vz), which the 3a determinism tests require.
        private static float2 SaltOffset(uint salt)
            => new float2((salt & 0xFFFF) * 0.073f, ((salt >> 16) & 0xFFFF) * 0.091f);

        // 2-octave signed fBm. NOT domain warping — see note on HeightDelta.
        // 2-octave signed noise, ~[-1.5, 1.5]. freq is cycles per voxel.
        private static float SignedFbm(uint salt, float vx, float vz, float freq)
        {
            float2 off = SaltOffset(salt);
            float2 p = new float2(vx, vz) * freq + off;
            return noise.cnoise(p) + 0.5f * noise.cnoise(p * 2.3f + off);
        }

        // RIDGED noise, 3 octaves, output ~[0,1] with sharp CRESTS rather than
        // smooth blobs. The 1-|n| fold is what creates ridgelines: |n| has a
        // sharp V-shaped minimum where the underlying noise crosses zero, so
        // 1-|n| becomes a sharp MAXIMUM there — a ridge, not a bump. Squaring
        // sharpens it further. This is the piece the first naturalism pass was
        // missing: warping a dome with smooth noise gives a lumpy dome, which
        // is still obviously a dome. Ridged noise gives it actual crest lines.
        private static float Ridged(uint salt, float vx, float vz, float freq)
        {
            float2 off = SaltOffset(salt);
            float2 p = new float2(vx, vz) * freq + off;
            float sum = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < 3; o++)
            {
                float n = noise.cnoise(p);
                float r = 1f - math.abs(n);
                sum += r * r * amp;
                norm += amp;
                p *= 2.17f;   // non-integer lacunarity: avoids octaves aligning into a grid
                amp *= 0.5f;
            }
            return sum / math.max(norm, 1e-6f);
        }

        // Anisotropic squash: rotates (vx,vz) into the anchor's own frame and
        // scales one axis, so a mountain's footprint is an irregular ELLIPSE
        // rather than a circle. A perfectly circular base is one of the two
        // things (with the smooth profile) that made these read as primitives.
        private static void Anisotropic(uint salt, float dx, float dz, out float ax, out float az)
        {
            float angle = ((salt >> 8) & 0xFF) / 255f * 6.2831853f;
            float stretch = 0.65f + ((salt >> 20) & 0xFF) / 255f * 0.45f; // 0.65..1.10
            float c = math.cos(angle), s = math.sin(angle);
            float rx = dx * c - dz * s;
            float rz = dx * s + dz * c;
            ax = rx / stretch;
            az = rz * stretch;
        }

        // ---- Height kernels (voxel units in, voxel delta out) ----
        //
        // NO DOMAIN WARPING IS USED ANYWHERE IN THIS FILE. Worth stating
        // explicitly because "warp" appeared in earlier naming here and that
        // was misleading. Every noise call samples at a plain scale-and-offset
        // of the TRUE world coordinate (p = (vx,vz)*freq + constantOffset) and
        // returns a scalar that modulates HEIGHT. Noise output is never fed
        // back into the sample position, so (vx,vz) stays ground truth and a
        // column's height remains a pure, directly-invertible function of its
        // real world position — which is what keeps ColumnSampler usable as
        // the oracle the determinism and per-voxel tests depend on.
        //
        // The one coordinate transform present is Anisotropic() below: a fixed
        // per-anchor rotation + axis scale used ONLY to measure distance from
        // the anchor centre for the radial falloff. It is affine, deterministic
        // and invertible — it shapes the footprint into an ellipse, it does not
        // displace the sampling domain by noise.
        public static float HeightDelta(in FeatureAnchor a, float vx, float vz)
        {
            float dx = vx - a.cx, dz = vz - a.cz;

            // Mountains use an ANISOTROPIC (elliptical, rotated) footprint;
            // craters stay radial. Distance is measured in the anchor's own
            // frame for mountains so the base isn't a circle.
            float d;
            if (a.kind == FeatureKind.Mountain)
            {
                Anisotropic(a.salt, dx, dz, out float ax, out float az);
                d = math.sqrt(ax * ax + az * az);
            }
            else
            {
                d = math.sqrt(dx * dx + dz * dz);
            }
            if (d >= a.radius) return 0f;
            float s = d / a.radius; // 0 at center .. 1 at edge

            // WARP_AMPLITUDE raised 0.20 -> 0.55. The 0.20 value was chosen to
            // avoid touching GenerationTests' thresholds, and the resulting
            // screenshots showed it was simply too timid to change the
            // silhouette — the mountains still read as smooth protrusions.
            // Correcting the priority: the visual requirement drives the
            // constant, and the two affected test thresholds are updated to
            // match (see GenerationTests HeightAnchors_*). Worst-case swing is
            // re-derived in that test's comment rather than left implicit.
            const float RELIEF_AMPLITUDE = 0.55f;

            switch (a.kind)
            {
                case FeatureKind.Mountain:
                {
                    float t = 1f - s;
                    float smooth = t * t * (3f - 2f * t); // smoothstep
                    float profile = smooth * smooth;

                    // RIDGED component: crest lines running across the peak.
                    // Centered on 0 (ridged-0.5) so it both adds and subtracts,
                    // carving gullies as well as raising spurs, and multiplied
                    // by `profile` so it vanishes exactly at the boundary — the
                    // warp can never push influence past a.radius, which is
                    // what AnchorPlanner's non-overlap check relies on.
                    float ridge = Ridged(a.salt, vx, vz, 1f / (a.radius * 0.55f)) - 0.5f;

                    // Finer detail so flanks aren't smooth sheets at close range.
                    float detail = SignedFbm(a.salt ^ 0x5BD1u, vx, vz, 1f / (a.radius * 0.18f));

                    float shaped = profile
                                 * (1f + ridge * RELIEF_AMPLITUDE * 2f * smooth)
                                 + detail * 0.06f * profile;
                    return a.magnitude * math.max(0f, shaped);
                }
                case FeatureKind.Crater:
                {
                    // Bowl (depth at center, zero by s=0.8) + raised rim ring.
                    // Rim window narrowed to 0.87±0.10 (was 0.9±0.12, which
                    // reached s=1.02 — past the d>=radius cutoff above, so the
                    // outer ~2% of the rim was silently clipped every time;
                    // caught while adding the warp below, fixed alongside it).
                    float amp = SignedFbm(a.salt, vx, vz, 1f / (a.radius * 0.5f));
                    float reliefFactor = math.max(0f, 1f + amp * RELIEF_AMPLITUDE);

                    float bowl = 0f;
                    float sb = s / 0.8f;
                    if (sb < 1f) bowl = -a.magnitude * (1f - sb * sb) * reliefFactor;

                    float rim = 0f;
                    float sr = (s - 0.87f) / 0.10f;
                    if (sr > -1f && sr < 1f) rim = (a.magnitude * 0.3f) * (1f - sr * sr) * reliefFactor;

                    return bowl + rim;
                }
                default:
                    return 0f; // caves don't touch the heightfield
            }
        }

        // ---- Volume kernel: cave capsule (horizontal axis) ----
        // Voxel center convention: pass (wx+0.5, wy+0.5, wz+0.5).
        public static bool CaveContains(in FeatureAnchor a, float3 p)
        {
            float3 axis = new float3(a.dirX, 0f, a.dirZ);
            float3 c = new float3(a.cx, a.cy, a.cz);
            float3 s0 = c - axis * a.halfLength;
            float3 seg = axis * (2f * a.halfLength);
            float segLenSq = math.dot(seg, seg);
            float t = segLenSq > 0f ? math.clamp(math.dot(p - s0, seg) / segLenSq, 0f, 1f) : 0f;
            float3 closest = s0 + seg * t;
            float3 dv = p - closest;
            return math.dot(dv, dv) < a.radius * a.radius;
        }

        // Conservative AABB (voxel units) for brick-intersection tests. A brick
        // inside this box MAY intersect the capsule; a brick outside it CANNOT.
        public static void CaveAabb(in FeatureAnchor a, out float3 min, out float3 max)
        {
            float3 c = new float3(a.cx, a.cy, a.cz);
            float3 reach = new float3(
                math.abs(a.dirX) * a.halfLength + a.radius,
                a.radius,
                math.abs(a.dirZ) * a.halfLength + a.radius);
            min = c - reach;
            max = c + reach;
        }
    }
}