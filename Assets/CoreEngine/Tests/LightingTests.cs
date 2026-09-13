// ==========================================
// Assets/CoreEngine/Tests/LightingTests.cs
//
// PHASE 7, STAGE 2. Proves the four things Stage 2 built, each against the
// spec clause that required it:
//
//   §3.8  the global cap AND its degradation rule (furthest-first, and a new
//         torch over the cap is NOT silently ignored)
//   A.6   the LightSource layout, C# and HLSL, field for field
//   §6.5  the day/night cycle's horizon behaviour
//   §6.6  emissives enqueue through the ONE §8.3 edit path
//   §8.1  LightingConfig's malformed-file behaviour (the PlayerConfig contract)
//
// MUTATION-CHECKED. The one the phase prompt names as mandatory:
//
//   M3  SelectActive's comparator inverted (scratchDist[j] > d  ->  < d), so
//       the sort runs descending and the NEAREST lights drop instead of the
//       furthest
//       -> Cap_FurthestLightsDropFirst and Cap_ANearerTorchDisplacesAFartherOne
//          must go red
//
// Applied, observed, reverted; the result is recorded in
// PHASE_7_COMPLETION.md. A mutation check that was reasoned about rather than
// run proves nothing.
// ==========================================

using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Rendering;

public class LightingTests
{
    private LightSourceBuffer _lights;

    [SetUp]
    public void Setup()
    {
        _lights = new LightSourceBuffer();
        // Every test runs against SHIPPED defaults, not whatever a previous
        // test left in the static. LightingConfig is a live-reloaded singleton
        // and order-dependent tests are the standing hazard with those.
        LightingConfig.DebugSetActive(new LightingConfig());
        SunCycle.DebugReset(12f);
    }

    [TearDown]
    public void Teardown() => _lights.Dispose();

    // =====================================================================
    // A.6 -- the struct layout, in both languages
    // =====================================================================

    [Test]
    public void A6_LightSourceIsExactlyEightFloats()
    {
        Assert.AreEqual(32, System.Runtime.InteropServices.Marshal.SizeOf<LightSource>(),
            "A.6 is eight 32-bit floats = 32 B. A different size means a field was added, " +
            "removed, or padded -- and the GPU buffer stride is computed from it.");
        Assert.AreEqual(32, LightSource.SizeBytes,
            "LightSource.SizeBytes disagrees with the real marshalled size");
    }

    /// The C#/HLSL twin check, in the shape PaletteSyncTests already uses for
    /// the palette: parse the other language and compare, because a silent
    /// layout mismatch between the two reads as "lighting is subtly wrong" for
    /// a month rather than as an error.
    [Test]
    public void A6_HlslAndCSharpDeclareTheSameFieldsInTheSameOrder()
    {
        string[] expected = { "x", "y", "z", "r", "g", "b", "range", "k" };

        string hlsl = File.ReadAllText("Assets/CoreEngine/Memory/StructHeaders.hlsl");
        int s = hlsl.IndexOf("struct LightSource");
        Assert.Greater(s, 0, "LightSource is missing from StructHeaders.hlsl");
        int e = hlsl.IndexOf("};", s);
        string body = hlsl.Substring(s, e - s);

        var names = new System.Collections.Generic.List<string>();
        foreach (Match m in Regex.Matches(body, @"float\s+([A-Za-z_][A-Za-z0-9_,\s]*);"))
            foreach (string part in m.Groups[1].Value.Split(','))
                names.Add(part.Trim());

        CollectionAssert.AreEqual(expected, names,
            "StructHeaders.hlsl's LightSource fields differ from Appendix A.6's order. " +
            "Appendix A's opening line requires the C# mirror and the HLSL to be identical in order.");

        var fields = typeof(LightSource).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var csNames = new System.Collections.Generic.List<string>();
        foreach (var f in fields) csNames.Add(f.Name);
        CollectionAssert.AreEqual(expected, csNames,
            "StructHeaders.cs's LightSource fields differ from Appendix A.6's order");
    }

    // =====================================================================
    // §3.8 -- the cap, and the degradation rule that makes it acceptable
    // =====================================================================

    [Test]
    public void Cap_IsTheSpecValueAndIsReadFromEngineConfig()
    {
        Assert.AreEqual(32, EngineConfig.MAX_ACTIVE_LIGHTS,
            "§0.2's table gives MAX_ACTIVE_LIGHTS a v1 starting value of 32. " +
            "Changing it is a re-derivation against §11.3, not a tweak.");
    }

    /// Registration is uncapped. This is the half of §3.8 that makes "does not
    /// silently ignore new torches" true: the 33rd torch is a real placement
    /// that really happened.
    [Test]
    public void Cap_RegistrationItselfIsNeverCapped()
    {
        for (int i = 0; i < EngineConfig.MAX_ACTIVE_LIGHTS * 3; i++)
            Assert.IsTrue(_lights.Register(new int3(i * 4, 20, 0), Materials.Torch),
                $"torch {i} was refused registration -- the cap must bound SELECTION, not placement");

        Assert.AreEqual(EngineConfig.MAX_ACTIVE_LIGHTS * 3, _lights.RegisteredCount);
        Assert.IsTrue(_lights.CapIsBinding, "with 96 lights and a cap of 32, the cap is binding");
    }

    /// THE DEGRADATION RULE. §3.8: "furthest-from-player lights drop first."
    /// M3 (comparator inverted) must break this.
    [Test]
    public void Cap_FurthestLightsDropFirst()
    {
        // Lights in a line along +X at 1 m spacing. The player sits at the origin,
        // so distance order is just index order and the expected survivors are
        // unambiguous.
        const int total = 64;
        for (int i = 0; i < total; i++)
            _lights.Register(new int3(i * 10, 0, 0), Materials.Torch);   // 10 voxels = 1 m

        var all = _lights.SnapshotRegistered();
        var scratch = new LightSourceBuffer.Registered[total];
        var dist = new float[total];
        var dest = new LightSource[EngineConfig.MAX_ACTIVE_LIGHTS];

        int n = LightSourceBuffer.SelectActive(all, all.Length, float3.zero, scratch, dist, dest);

        Assert.AreEqual(EngineConfig.MAX_ACTIVE_LIGHTS, n, "selection must fill exactly the cap");

        // The survivors are the 32 NEAREST, i.e. x in {0.05, 1.05, ... 31.05} m.
        // Assert on the SET, and assert the dropped ones are genuinely absent.
        var survivorX = new System.Collections.Generic.HashSet<int>();
        for (int i = 0; i < n; i++) survivorX.Add(Mathf.RoundToIntSafe(dest[i].x));

        for (int i = 0; i < EngineConfig.MAX_ACTIVE_LIGHTS; i++)
            Assert.IsTrue(survivorX.Contains(i),
                $"light at {i} m survived? it is among the {EngineConfig.MAX_ACTIVE_LIGHTS} nearest and must be kept");

        for (int i = EngineConfig.MAX_ACTIVE_LIGHTS; i < total; i++)
            Assert.IsFalse(survivorX.Contains(i),
                $"light at {i} m was kept while nearer lights were dropped -- " +
                "§3.8 requires the FURTHEST to drop first");
    }

    /// The behavioural statement of the same rule, phrased the way a player
    /// would hit it: you are at the cap, you place one more torch right next to
    /// you, and it lights up -- something far away goes dark instead of your new
    /// torch doing nothing. M3 must break this too.
    [Test]
    public void Cap_ANearerTorchDisplacesAFartherOne()
    {
        for (int i = 0; i < EngineConfig.MAX_ACTIVE_LIGHTS; i++)
            _lights.Register(new int3(500 + i * 10, 0, 0), Materials.Torch);   // all 50 m+ away

        var scratch = new LightSourceBuffer.Registered[128];
        var dist = new float[128];
        var dest = new LightSource[EngineConfig.MAX_ACTIVE_LIGHTS];

        var before = _lights.SnapshotRegistered();
        int n0 = LightSourceBuffer.SelectActive(before, before.Length, float3.zero, scratch, dist, dest);
        Assert.AreEqual(EngineConfig.MAX_ACTIVE_LIGHTS, n0, "precondition: we are exactly at the cap");

        // One more, right at the player's feet -- over the cap.
        _lights.Register(new int3(1, 0, 0), Materials.Torch);
        var after = _lights.SnapshotRegistered();
        int n1 = LightSourceBuffer.SelectActive(after, after.Length, float3.zero, scratch, dist, dest);

        Assert.AreEqual(EngineConfig.MAX_ACTIVE_LIGHTS, n1, "still capped");
        Assert.AreEqual(EngineConfig.MAX_ACTIVE_LIGHTS + 1, _lights.RegisteredCount,
            "the over-cap torch must still be REGISTERED -- it is a real voxel");

        bool nearestPresent = false;
        float farthest = 0f;
        for (int i = 0; i < n1; i++)
        {
            if (dest[i].x < 0.5f) nearestPresent = true;
            if (dest[i].x > farthest) farthest = dest[i].x;
        }
        Assert.IsTrue(nearestPresent,
            "the torch placed at the player's feet is not lit -- this is exactly §3.8's " +
            "\"silently ignore new torches\" failure");
        Assert.Less(farthest, 500f + EngineConfig.MAX_ACTIVE_LIGHTS * 1f,
            "the displaced light should have been the FURTHEST one");
    }

    /// Selection is by distance to the PLAYER, not to the world origin, so the
    /// active set has to follow the player. Without this the cap would be a
    /// fixed set chosen once and "furthest-from-player" would be meaningless.
    [Test]
    public void Cap_ActiveSetFollowsThePlayer()
    {
        for (int i = 0; i < 64; i++) _lights.Register(new int3(i * 10, 0, 0), Materials.Torch);

        var all = _lights.SnapshotRegistered();
        var scratch = new LightSourceBuffer.Registered[64];
        var dist = new float[64];
        var dest = new LightSource[EngineConfig.MAX_ACTIVE_LIGHTS];

        LightSourceBuffer.SelectActive(all, all.Length, new float3(63f, 0f, 0f), scratch, dist, dest);

        // Standing at the far end, the survivors must be the far-end lights.
        float nearest = float.MaxValue;
        for (int i = 0; i < EngineConfig.MAX_ACTIVE_LIGHTS; i++) nearest = math.min(nearest, dest[i].x);
        Assert.Greater(nearest, 30f,
            "standing at x = 63 m, a light at x < 30 m is in the active set while nearer ones are not -- " +
            "selection is not relative to the player");
    }

    [Test]
    public void Cap_UnregisterRemovesTheLight()
    {
        var v = new int3(10, 20, 30);
        Assert.IsTrue(_lights.Register(v, Materials.Torch));
        Assert.IsTrue(_lights.IsRegistered(v));
        Assert.IsTrue(_lights.Unregister(v));
        Assert.IsFalse(_lights.IsRegistered(v));
        Assert.AreEqual(0, _lights.RegisteredCount);
    }

    [Test]
    public void Cap_RegisteringTheSameVoxelTwiceIsOneLight()
    {
        var v = new int3(10, 20, 30);
        _lights.Register(v, Materials.Torch);
        _lights.Register(v, Materials.Torch);
        Assert.AreEqual(1, _lights.RegisteredCount,
            "re-placing the same emissive over itself stacked a second light in one cell");
    }

    [Test]
    public void NonEmissiveMaterialsAreNeverLights()
    {
        foreach (byte m in new[] { Materials.Stone, Materials.Water, Materials.Sand,
                                   Materials.Air, Materials.Snow, Materials.Obsidian })
            Assert.IsFalse(_lights.Register(new int3(0, 0, 0), m),
                $"material {m} registered as a light source but is not emissive");
        Assert.AreEqual(0, _lights.RegisteredCount);
    }

    /// §10 flag 3's default, PINNED so it cannot drift back silently.
    ///
    /// This is a DEFAULT, not a law: C.9's literal grouping attaches
    /// max(0, N.L) to the sun term only, and `false` still reproduces that
    /// exactly. The flip to `true` is recorded in RaymarchFeature's own comment
    /// with the screenshot evidence behind it. The test exists so that reverting
    /// it is a deliberate act with a failing test attached, rather than
    /// something that happens by accident during a merge.
    [Test]
    public void PointLightNdotL_DefaultsToOn_AndIsStillAToggle()
    {
        Assert.IsTrue(RaymarchFeature.PointLightNdotL,
            "§10 flag 3's default was flipped to ON on 2026-09-13 because without N.L every face " +
            "of a torch-lit chamber renders identically regardless of orientation. If this is being " +
            "reverted, do it deliberately and update PHASE_7_COMPLETION.md §10 flag 3 -- do not just " +
            "delete this assertion.");
    }

    // =====================================================================
    // STAGE A -- the per-frame upload that cost 32 ms/frame
    // =====================================================================

    /// THE PHASE 7 STUTTER, AS AN ASSERTION ON CALL COUNT RATHER THAN ON TIME.
    ///
    /// LightSourceBuffer.Upload() used to issue a GraphicsBuffer.SetData EVERY
    /// FRAME, even with zero lights placed. Measured cost, cooled, moving camera
    /// with live fluid: 43.46 ms/frame with it, 11.25 ms/frame without
    /// (PHASE_7_COMPLETION §A). A frame-time test would be unrunnable in
    /// EditMode and unstable anywhere; the CALL COUNT is the thing that actually
    /// changed, it is deterministic, and it is what this pins.
    ///
    /// MUTATION CHECK: remove the `if (n == 0)` fast path or the
    /// `StagingDiffers()` guard in Upload and this goes red.
    [Test]
    public void Upload_DoesNotReUploadWhenNothingChanged()
    {
        // No lights at all -- the overwhelmingly common case, and the one that
        // was costing a full SetData per frame for a buffer of zeroes.
        for (int frame = 0; frame < 50; frame++)
            _lights.Upload(new float3(frame * 0.5f, 12f, frame * 0.25f));

        Assert.AreEqual(1, _lights.UploadsPerformed,
            "with ZERO lights placed, the buffer must be initialised exactly once and then left " +
            "alone. Re-uploading it every frame is the Phase 7 stutter: SetData on a buffer the " +
            "raymarch kernel is reading serialises against in-flight GPU work, and under camera " +
            "motion that stall dominated the frame (43.46 ms -> 11.25 ms when suspended).");

        // Now place a light: that IS a change and must upload exactly once.
        _lights.Register(new int3(100, 20, 100), Materials.Torch);
        _lights.Upload(float3.zero);
        Assert.AreEqual(2, _lights.UploadsPerformed, "placing a light must upload once");

        // Standing still with an unchanged set: no further uploads.
        for (int frame = 0; frame < 30; frame++) _lights.Upload(float3.zero);
        Assert.AreEqual(2, _lights.UploadsPerformed,
            "a stationary player with an unchanged light set re-uploaded the buffer");
    }

    /// The other half: a change the player CAN cause must still reach the GPU.
    /// A dirty-check that never re-uploads is worse than no dirty-check, because
    /// the lighting silently stops tracking the world.
    [Test]
    public void Upload_StillUploadsWhenTheActiveSetActuallyChanges()
    {
        for (int i = 0; i < EngineConfig.MAX_ACTIVE_LIGHTS + 4; i++)
            _lights.Register(new int3(i * 40, 20, 0), Materials.Torch);

        _lights.Upload(float3.zero);
        long afterFirst = _lights.UploadsPerformed;

        // MOVING far enough re-orders the nearest-N selection (§3.8's
        // furthest-first rule is relative to the player), so the bytes change
        // even though no light moved. This is the case the original comment
        // correctly identified -- and then wrongly concluded made a dirty-check
        // pointless.
        _lights.Upload(new float3(400f, 20f, 0f));
        Assert.Greater(_lights.UploadsPerformed, afterFirst,
            "the player moved far enough to change which lights are nearest, but the buffer " +
            "was not re-uploaded -- the active set is now stale on the GPU");

        // Digging out a torch that is IN THE ACTIVE SET must reach the GPU.
        //
        // THE FIRST VERSION OF THIS ASSERTION WAS WRONG AND THE CODE WAS RIGHT.
        // It removed the light at voxel (0,20,0) while the player stood at
        // x = 400 m -- which is the FURTHEST of the 36, and therefore was not in
        // the active 32 at all (§3.8 drops furthest-first). Removing a light the
        // GPU was never told about correctly changes no bytes and correctly
        // triggers no upload. The test was asserting that a no-op must cause
        // work. It now removes the NEAREST light instead, which is
        // unambiguously in the active set.
        long afterMove = _lights.UploadsPerformed;
        var nearest = new int3(35 * 40, 20, 0);          // x = 1400 voxels = 140 m
        Assert.IsTrue(_lights.IsRegistered(nearest), "precondition: the nearest light exists");
        Assert.IsTrue(_lights.Unregister(nearest));
        _lights.Upload(new float3(400f, 20f, 0f));
        Assert.Greater(_lights.UploadsPerformed, afterMove,
            "a light INSIDE the active set was removed but the buffer was not re-uploaded -- " +
            "the GPU is now drawing a torch that no longer exists");

        // And the complement, which is the behaviour the first version mistook
        // for a bug: removing a light OUTSIDE the active set changes nothing on
        // the GPU and must NOT cost an upload.
        long afterRemove = _lights.UploadsPerformed;
        Assert.IsTrue(_lights.Unregister(new int3(0, 20, 0)));   // the furthest
        _lights.Upload(new float3(400f, 20f, 0f));
        Assert.AreEqual(afterRemove, _lights.UploadsPerformed,
            "removing a light that was never in the active set caused a pointless re-upload");
    }

    /// THE TEST THAT WOULD HAVE CAUGHT THE WHOLE STAGE-A REGRESSION.
    ///
    /// Lava is `IsFluid | IsEmissive`. Before this predicate existed, every lava
    /// CELL registered as a point light through §6.6's enqueue on the §8.3 edit
    /// path -- and the Playground's lava vent budget alone is 12,000 voxels.
    /// §3.8 sizes the light system for "a handful of placed torches, not a lit
    /// city" and §6.5 calls v1 lights "genuinely sparse"; a fluid registering
    /// itself violates both, and cost 32 ms/frame.
    ///
    /// MUTATION CHECK: delete `if (MaterialRules.IsMobile(material)) return
    /// false;` from Register and this goes red.
    [Test]
    public void MobileEmissivesDoNotRegisterAsPointLights()
    {
        // Lava: emissive AND mobile. It must NOT become a point light.
        Assert.IsTrue(MaterialRules.IsEmissiveMaterial(Materials.Lava),
            "precondition: lava is still emissive");
        Assert.IsTrue(MaterialRules.IsMobile(Materials.Lava),
            "precondition: lava is still a fluid");
        Assert.IsFalse(_lights.Register(new int3(5, 5, 5), Materials.Lava),
            "a lava cell registered as a point light. §3.8 sizes this subsystem for " +
            "'a handful of placed torches, not a lit city' -- a flowing fluid registers " +
            "thousands, which is exactly the Phase 7 stutter.");
        Assert.AreEqual(0, _lights.RegisteredCount);

        // A torch: emissive and STATIC. It must still work.
        Assert.IsTrue(_lights.Register(new int3(6, 6, 6), Materials.Torch));
        Assert.AreEqual(1, _lights.RegisteredCount);
    }

    /// STAGE H's seam, both ways. The DEFAULT must stay closed -- the test
    /// above proves that -- and the seam must actually open, or the H1
    /// measurement would be comparing a condition against itself and reporting
    /// "no cost" for a path that never ran. That failure mode has already
    /// happened once in this phase (§F's rung-1 counters showed the mechanism
    /// never engaged), so the seam gets its own assertion.
    [Test]
    public void MobileEmissives_RegisterOnlyWhenTheStageHSeamIsOpened()
    {
        try
        {
            Assert.IsFalse(LightSourceBuffer.AllowMobileEmissives,
                "the seam must default CLOSED -- shipping behaviour is unchanged by §H");
            Assert.IsFalse(_lights.Register(new int3(5, 5, 5), Materials.Lava));

            LightSourceBuffer.AllowMobileEmissives = true;
            Assert.IsTrue(_lights.Register(new int3(5, 5, 5), Materials.Lava),
                "the seam is open and lava still refused to register -- the H1 arms " +
                "would then measure nothing and report it as free");
            Assert.AreEqual(1, _lights.RegisteredCount);
        }
        finally
        {
            LightSourceBuffer.AllowMobileEmissives = false;   // static: never leak into another test
        }
    }

    /// STAGE G. The three coloured torches must be real lights, must be
    /// SEPARABLE from the warm torch by colour, and must differ from it in
    /// NOTHING ELSE -- because G1 attributes any measured difference between
    /// them to the colour path, and that attribution is only valid if colour is
    /// genuinely the only variable.
    [Test]
    public void ColouredTorches_AreLightsThatDifferFromTheWarmTorchInTintAlone()
    {
        byte[] coloured = { Materials.TorchBlue, Materials.TorchGreen, Materials.TorchPurple };

        Assert.IsTrue(MaterialRules.TryGetLight(Materials.Torch, out float wr, out float wg, out float wb,
                                                out float wRange));
        foreach (byte m in coloured)
        {
            Assert.IsTrue(MaterialRules.TryGetLight(m, out float r, out float g, out float b, out float range),
                $"material {m} is not a light source");
            Assert.IsFalse(MaterialRules.IsMobile(m), $"material {m} must be static, or §A's exclusion eats it");
            Assert.AreEqual(MaterialRules.Emissive(Materials.Torch), MaterialRules.Emissive(m), 1e-6f,
                $"material {m} differs from the warm torch in STRENGTH as well as tint");
            Assert.AreEqual(wRange, range, 1e-6f,
                $"material {m} differs from the warm torch in RANGE as well as tint");

            float d = math.abs(r - wr) + math.abs(g - wg) + math.abs(b - wb);
            Assert.Greater(d, 0.25f,
                $"material {m}'s colour is too close to the warm torch to read as a different light");

            // No channel at zero: a fully saturated primary multiplies two of a
            // surface's albedo channels to nothing and flattens it to a
            // silhouette. This is the reason the tints are what they are.
            Assert.Greater(math.min(r, math.min(g, b)), 0.05f,
                $"material {m} has a channel at zero and will flatten surface detail");
        }

        // And they must be distinguishable from EACH OTHER, or the mixed-colour
        // premise of G1 collapses into one colour repeated.
        for (int i = 0; i < coloured.Length; i++)
            for (int j = i + 1; j < coloured.Length; j++)
            {
                MaterialRules.TryGetLight(coloured[i], out float ar, out float ag, out float ab, out _);
                MaterialRules.TryGetLight(coloured[j], out float br, out float bg, out float bb, out _);
                Assert.Greater(math.abs(ar - br) + math.abs(ag - bg) + math.abs(ab - bb), 0.25f,
                    $"materials {coloured[i]} and {coloured[j]} are near-identical colours");
            }
    }

    /// The selection must stay bounded no matter how many are registered, so a
    /// future emissive material cannot reintroduce the quadratic blow-up.
    /// This is the defence-in-depth half; the predicate above is the cause.
    [Test]
    public void SelectActive_IsBoundedAndStillPicksTheNearest_AtLargeN()
    {
        const int n = 4000;
        var all = new LightSourceBuffer.Registered[n];
        for (int i = 0; i < n; i++)
            all[i] = new LightSourceBuffer.Registered
            {
                voxel = new int3((n - i) * 10, 0, 0),   // index 0 is FURTHEST
                material = Materials.Torch,
                r = 1f, g = 1f, b = 1f, range = 8f,
            };

        var scratch = new LightSourceBuffer.Registered[n];
        var dist = new float[n];
        var dest = new LightSource[EngineConfig.MAX_ACTIVE_LIGHTS];

        int count = LightSourceBuffer.SelectActive(all, n, float3.zero, scratch, dist, dest);
        Assert.AreEqual(EngineConfig.MAX_ACTIVE_LIGHTS, count);

        // The nearest 32 are the LAST 32 entries, at x = 10..320 voxels = 1..32 m.
        float farthestKept = 0f;
        for (int i = 0; i < count; i++) farthestKept = math.max(farthestKept, dest[i].x);
        Assert.LessOrEqual(farthestKept, 32.05f,
            "partial selection kept a light further than the nearest 32 -- §3.8's " +
            "furthest-first rule is not being honoured at large n");
    }

    // =====================================================================
    // STAGE C -- the presets, and the snow-clipping ceiling they respect
    // =====================================================================

    /// THE OWNER'S "BLINDINGLY BRIGHT IN SNOW" COMPLAINT, AS AN ASSERTION.
    ///
    /// An up-facing snow voxel at noon must not saturate, or the terrain shape
    /// disappears into flat white. This is arithmetic, not taste, which is why
    /// it is a test and not a preset comment: see LightingPresets' header for
    /// the derivation.
    ///
    /// The SHIPPED config is deliberately EXCLUDED and separately asserted to
    /// clip below -- it is kept as a preset for A/B, and a test that quietly
    /// passed it would erase the finding.
    [Test]
    public void Presets_DoNotClipSnowAtNoon()
    {
        foreach (var p in LightingPresets.All)
        {
            if (p.Name == "Shipped") continue;
            var c = p.Build();
            float peak = LightingPresets.SnowNoonPeak(c);
            Assert.Less(peak, 1.0f,
                $"preset '{p.Name}' drives an up-facing snow voxel to {peak:F3} at noon, which " +
                "saturates to flat white and loses the terrain shape -- the exact symptom reported. " +
                "Lower sunIntensity (not just ambientIntensity: the sun term alone reaches the " +
                "ceiling). See LightingPresets' header for the constraint.");
            Assert.LessOrEqual(c.sunIntensity, LightingPresets.MaxSunIntensityForSnow(c.ambientIntensity) + 1e-3f,
                $"preset '{p.Name}' exceeds the derived sunIntensity ceiling for its ambient");
        }
    }

    /// The finding itself, pinned so it cannot be quietly lost: the values this
    /// phase shipped DO clip. If someone "fixes" this by lowering the snow
    /// palette instead, this test still passes and MORNING_REPORT item 1's
    /// earlier correction gets undone by accident -- so the palette is asserted
    /// unchanged alongside it.
    [Test]
    public void ShippedConfig_ClipsSnowAtNoon_AndThatIsWhyThePresetsExist()
    {
        float peak = LightingPresets.SnowNoonPeak(new LightingConfig());
        Assert.Greater(peak, 1.0f,
            "the shipped lighting config no longer clips snow at noon. If that was fixed " +
            "deliberately, update LightingPresets' header and PHASE_7_COMPLETION §C -- this " +
            "assertion records a measured defect, not a requirement.");

        var snow = MaterialPalette.Of(Materials.Snow);
        Assert.AreEqual(0.760f, snow.B, 0.0005f,
            "the snow palette moved. MORNING_REPORT.md item 1 already lowered snow 0.86 -> 0.72 " +
            "for this same clipping class; the Phase 7 fix belongs in the LIGHTING constants, " +
            "not in the palette. Lowering snow again would treat the symptom in the wrong file.");
    }

    [Test]
    public void Presets_AreDistinctAndAllParseBackFromJson()
    {
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (var p in LightingPresets.All)
        {
            Assert.IsTrue(seen.Add(p.Name), $"duplicate preset name '{p.Name}'");
            string err;
            var back = LightingConfig.Parse(p.Build().ToJson(), new LightingConfig(), out err);
            Assert.IsNull(err, $"preset '{p.Name}' does not round-trip through its own JSON: {err}");
            Assert.AreEqual(p.Build().kSoft, back.kSoft, 1e-6f);
            Assert.AreEqual(p.Build().sunIntensity, back.sunIntensity, 1e-6f);
        }
        Assert.GreaterOrEqual(LightingPresets.All.Length, 4, "Stage C asked for 3-4 distinct presets");
    }

    // =====================================================================
    // §6.6 / A.7 -- the emissive data that had never existed
    // =====================================================================

    [Test]
    public void Emissive_TorchAndLavaAreTheEmissiveMaterials()
    {
        // STAGE G WIDENED THIS LIST, and the widening is the point of the test:
        // it went red the moment the three coloured torches were added, which is
        // exactly what a closed-world assertion is for. Adding an id here is a
        // deliberate act; inheriting one by accident is what this prevents.
        byte[] emissive =
        {
            Materials.Torch, Materials.Lava,
            Materials.TorchBlue, Materials.TorchGreen, Materials.TorchPurple,
        };

        foreach (byte m in emissive)
            Assert.IsTrue(MaterialRules.IsEmissiveMaterial(m), $"material {m} should be emissive");

        for (int i = 0; i < 256; i++)
        {
            if (System.Array.IndexOf(emissive, (byte)i) >= 0) continue;
            Assert.IsFalse(MaterialRules.IsEmissiveMaterial((byte)i),
                $"material {i} is emissive and nothing says it should be");
        }
    }

    /// The flag bit and the strength table are two separate tables and could
    /// drift. IsEmissiveMaterial requires BOTH, so a material with the Appendix
    /// B bit but no strength (or the reverse) is dark -- which would be a silent
    /// content bug. This pins that they agree.
    [Test]
    public void Emissive_FlagBitAndStrengthTableAgree()
    {
        for (int i = 0; i < 256; i++)
        {
            bool flagged = (MaterialRules.Flags((byte)i) & MaterialRules.IsEmissive) != 0u;
            bool hasStrength = MaterialRules.Emissive((byte)i) > 0f;
            Assert.AreEqual(flagged, hasStrength,
                $"material {i}: Appendix B's IsEmissive bit is {flagged} but its emissive strength is " +
                $"{MaterialRules.Emissive((byte)i)} -- the two tables have drifted and the material is " +
                "either a light that looks dark or dark thing that claims to be a light");
        }
    }

    [Test]
    public void Emissive_TorchIsNotMobileAndNotSelfLit()
    {
        // A torch must not get a fluid slot (§7.3) -- it is a static solid.
        Assert.IsFalse(MaterialRules.IsMobile(Materials.Torch));
        // And it must NOT take the shader's self-lit shading cheat, which skips
        // AO and the hemisphere term. A torch gets a REAL emissive term via C.9
        // instead; taking both would double-count it and flatten it.
        Assert.IsFalse(MaterialPalette.IsSelfLit(Materials.Torch));
    }

    /// D.2's `contentVersionHash` is what decides whether an existing
    /// `world.meta` is stale. Adding a material that world GENERATION can never
    /// place must not invalidate every saved world -- which is the stated
    /// reason Lava, Honey and Obsidian are already absent from it, and Torch
    /// gets the same treatment for the same reason.
    ///
    /// WHAT THIS TEST CAN AND CANNOT ESTABLISH, because the distinction
    /// matters: it proves the PROPERTY that makes the exclusion correct -- that
    /// no biome can produce a torch, so generated output is genuinely
    /// independent of it -- and that the hash is a pure function. It does NOT
    /// prove the hash's numeric value is unchanged from before Phase 7; that is
    /// established by ContentVersionHash's body being untouched in this phase's
    /// diff, which is stronger evidence than a literal pinned here would be
    /// (a pinned literal would have to be produced by running the new code,
    /// which is circular).
    [Test]
    public void Emissive_TorchCannotBeProducedByGenerationSoItIsOutsideTheHash()
    {
        foreach (var b in Biomes.Table)
        {
            Assert.AreNotEqual(Materials.Torch, b.surfaceMaterial, $"biome {b.name} generates torches");
            Assert.AreNotEqual(Materials.Torch, b.bulkMaterial, $"biome {b.name} generates torches");
            Assert.AreNotEqual(Materials.Torch, b.deepMaterial, $"biome {b.name} generates torches");
        }
        Assert.AreEqual(WorldGenConstants.ContentVersionHash(), WorldGenConstants.ContentVersionHash(),
            "ContentVersionHash is not a pure function");
    }

    // =====================================================================
    // §6.5 -- the cycle's horizon behaviour
    // =====================================================================

    [Test]
    public void Sun_RisesAtSixSetsAtEighteenAndIsDownAtMidnight()
    {
        Assert.Greater(SunCycle.ElevationDegreesAt(12f), 45f, "noon must be high");
        Assert.Greater(SunCycle.ElevationDegreesAt(9f), 0f, "mid-morning must be up");
        Assert.Greater(SunCycle.ElevationDegreesAt(15f), 0f, "mid-afternoon must be up");
        Assert.AreEqual(0f, SunCycle.ElevationDegreesAt(6f), 1e-3f, "06:00 is sunrise, elevation 0");
        Assert.AreEqual(0f, SunCycle.ElevationDegreesAt(18f), 1e-3f, "18:00 is sunset, elevation 0");
        Assert.Less(SunCycle.ElevationDegreesAt(0f), -45f, "midnight must be well below the horizon");
        Assert.Less(SunCycle.ElevationDegreesAt(3f), 0f);
        Assert.Less(SunCycle.ElevationDegreesAt(21f), 0f);
    }

    /// §6.5: "Below the horizon: skip shadow rays (sun term = 0), ambient
    /// carries the night." The sun colour must be EXACTLY zero below the
    /// horizon, not merely small -- the shader's "is the sun up" branch and
    /// this colour must never disagree about whether it is night, or the world
    /// gets lit from underneath while every shadow ray is being skipped.
    [Test]
    public void Sun_ColourIsExactlyZeroBelowTheHorizon()
    {
        foreach (float h in new[] { 0f, 1f, 3f, 5.9f, 18.1f, 21f, 23.5f })
        {
            float3 c = SunCycle.ColorAt(h);
            Assert.AreEqual(0f, math.lengthsq(c), 0f,
                $"at {h}:00 the sun is below the horizon but its colour is {c}");
        }
        Assert.Greater(math.lengthsq(SunCycle.ColorAt(12f)), 0f, "noon must have a sun colour");
    }

    /// §6.5's other half: ambient must NOT go to zero at night, or "night" is
    /// a black screen rather than a time of day.
    [Test]
    public void Sun_AmbientCarriesTheNight()
    {
        float3 night = SunCycle.AmbientAt(0f);
        float3 day = SunCycle.AmbientAt(12f);
        Assert.Greater(math.length(night), 0f, "midnight ambient is exactly zero -- that is not night, that is no render");
        Assert.Less(math.length(night), math.length(day), "night must still be darker than day");

        var c = LightingConfig.Active;
        Assert.AreEqual(c.nightAmbientFraction, math.length(night) / math.length(day), 0.02f,
            "the night floor should land at nightAmbientFraction of the daylight ambient");
    }

    /// Golden hour is DERIVED from the arc, not typed, so it stays golden hour
    /// if the arc tilt changes. §13 asks for it to be captured specifically, so
    /// it has to actually be where it claims.
    [Test]
    public void Sun_GoldenHourIsWhereItSaysItIs()
    {
        float gh = SunCycle.GoldenHourHours;
        Assert.AreEqual(SunCycle.GoldenHourElevationDegrees, SunCycle.ElevationDegreesAt(gh), 0.05f,
            "GoldenHourHours does not put the sun at GoldenHourElevationDegrees");
        Assert.Greater(gh, 12f, "golden hour is the EVENING one -- §6.5's longest grazing rays");
        Assert.Less(gh, 18f, "golden hour must be before sunset");

        // And the inverse round-trips on the morning branch too.
        float morning = SunCycle.HoursForElevation(6f, morning: true);
        Assert.AreEqual(6f, SunCycle.ElevationDegreesAt(morning), 0.05f);
        Assert.Less(morning, 12f);
    }

    [Test]
    public void Sun_ClockWrapsAndCountsDays()
    {
        SunCycle.DebugReset(23f);
        SunCycle.SetHours(25f);
        Assert.AreEqual(1f, SunCycle.Hours, 1e-4f, "25:00 must wrap to 01:00");
        Assert.AreEqual(1, SunCycle.DaysElapsed);

        SunCycle.SetHours(-1f);
        Assert.AreEqual(23f, SunCycle.Hours, 1e-4f, "-1:00 must wrap to 23:00");
    }

    /// A scrubber is a slider a human drags; a NaN out of it must not be able
    /// to stop time or poison every downstream trig call.
    [Test]
    public void Sun_ScrubberCannotBreakTheClock()
    {
        SunCycle.DebugReset(10f);
        SunCycle.SetHours(float.NaN);
        Assert.AreEqual(10f, SunCycle.Hours, 1e-4f, "a NaN scrub moved the clock");
        SunCycle.SetHours(float.PositiveInfinity);
        Assert.AreEqual(10f, SunCycle.Hours, 1e-4f, "an infinite scrub moved the clock");
    }

    [Test]
    public void Sun_AdvanceCompletesExactlyOneDayInDayLengthSeconds()
    {
        var cfg = new LightingConfig { dayLengthSeconds = 60f };
        LightingConfig.DebugSetActive(cfg);
        SunCycle.DebugReset(0f);
        for (int i = 0; i < 600; i++) SunCycle.Advance(0.1f);   // 60 s of real time
        Assert.AreEqual(1, SunCycle.DaysElapsed, "60 s at dayLengthSeconds = 60 is exactly one day");
        Assert.AreEqual(0f, SunCycle.Hours, 0.05f);
    }

    // =====================================================================
    // §8.1's contract, transposed -- LightingConfig
    // =====================================================================

    [Test]
    public void Config_MalformedJsonKeepsTheRunningConfig()
    {
        var running = new LightingConfig { kSoft = 0.11f, lightRangeM = 13f };
        string err;
        LightingConfig result = LightingConfig.Parse("{ this is not json", running, out err);
        Assert.AreSame(running, result,
            "a malformed file must leave the RUNNING config in place -- snapping to defaults mid-edit " +
            "makes the screen jump every time the owner hits save");
        Assert.IsNotNull(err, "the failure must be reported, not swallowed");
        Assert.AreEqual(0.11f, running.kSoft, "the running config was mutated by a failed parse");
    }

    [Test]
    public void Config_EmptyOrTruncatedFileKeepsTheRunningConfig()
    {
        var running = new LightingConfig { kSoft = 0.07f };
        string err;
        Assert.AreSame(running, LightingConfig.Parse("", running, out err));
        Assert.IsNotNull(err);
        Assert.AreSame(running, LightingConfig.Parse("   \n  ", running, out err));
        Assert.IsNotNull(err);
    }

    /// The values a typo can actually produce, and what each would do if it got
    /// through: NaN kSoft blacks out every lit pixel; a zero day length divides
    /// by zero in the clock; a negative range makes the §3.8 cutoff reject every
    /// light so torches stop working with no error.
    [Test]
    public void Config_ValidateClampsTheValuesThatWouldBreakAFrame()
    {
        var c = new LightingConfig
        {
            kSoft = float.NaN,
            pointFalloffK = -5f,
            lightRangeM = -1f,
            dayLengthSeconds = 0f,
            sunRampTopDegrees = 0f,
            ambientIntensity = float.PositiveInfinity,
        };
        string notes = c.Validate();
        Assert.IsNotNull(notes, "Validate reported no changes despite six bad values");

        Assert.IsFalse(float.IsNaN(c.kSoft));
        Assert.Greater(c.pointFalloffK, 0f);
        Assert.Greater(c.lightRangeM, 0f);
        Assert.GreaterOrEqual(c.dayLengthSeconds, 1f);
        Assert.Greater(c.sunRampTopDegrees, 0f);
        Assert.IsFalse(float.IsInfinity(c.ambientIntensity));
    }

    /// k_soft = 0 is MEANINGFUL -- §6.5's hard-shadow toggle -- and must survive
    /// validation unchanged. Clamping it up to a small positive number would
    /// quietly remove the toggle the spec requires.
    [Test]
    public void Config_ZeroKSoftSurvivesValidation()
    {
        var c = new LightingConfig { kSoft = 0f };
        c.Validate();
        Assert.AreEqual(0f, c.kSoft, 0f,
            "k_soft = 0 is §6.5's hard-shadow toggle and was clamped away");
    }

    [Test]
    public void Config_RoundTripsThroughJson()
    {
        var c = new LightingConfig { kSoft = 0.042f, lightRangeM = 11.5f, dayLengthSeconds = 300f };
        string err;
        LightingConfig back = LightingConfig.Parse(c.ToJson(), new LightingConfig(), out err);
        Assert.IsNull(err, "a config this file wrote itself failed to parse: " + err);
        Assert.AreEqual(0.042f, back.kSoft, 1e-6f);
        Assert.AreEqual(11.5f, back.lightRangeM, 1e-6f);
        Assert.AreEqual(300f, back.dayLengthSeconds, 1e-6f);
    }

    // =====================================================================
    // §6.6 through the ONE §8.3 edit path
    // =====================================================================

    [Test]
    public void EditPath_PlacingATorchRegistersALightAndDiggingItOutRemovesIt()
    {
        var pool = new BrickDataPool(4000);
        try
        {
            var allocator = new ChunkHandleAllocator(64);
            var store = new ChunkStore(pool, allocator);
            store.InsertChunk(new Chunk { coord = new int3(0, 0, 0), isUniform = true, uniformMaterial = 0 });

            var edits = new EditService();
            edits.AttachWorld(store, store, store, null);   // headless: no mirror
            edits.AttachLightBuffer(_lights);

            var v = new int3(40, 40, 40);

            Assert.IsTrue(edits.TrySetVoxel(v, Materials.Torch), "the placement itself failed");
            Assert.AreEqual(1, _lights.RegisteredCount,
                "§6.6: an emissive placed through the §8.3 path must enqueue into the light system");
            Assert.IsTrue(_lights.IsRegistered(v));
            Assert.AreEqual(1, edits.LightsRegistered);

            Assert.IsTrue(edits.TrySetVoxel(v, Materials.Air), "digging the torch out failed");
            Assert.AreEqual(0, _lights.RegisteredCount,
                "a torch that has been mined out is still lighting the room");
            Assert.AreEqual(1, edits.LightsUnregistered);

            // And a non-emissive edit must not touch the registry at all.
            edits.TrySetVoxel(new int3(41, 40, 40), Materials.Stone);
            Assert.AreEqual(0, _lights.RegisteredCount);
        }
        finally { pool.Dispose(); }
    }

    /// Replacing a torch with a different emissive must not leave TWO lights,
    /// and replacing it with a non-emissive must not leave one. The hook reads
    /// the PREVIOUS material to decide, so this is the case that proves it does.
    [Test]
    public void EditPath_OverwritingAnEmissiveUpdatesRatherThanStacking()
    {
        var pool = new BrickDataPool(4000);
        try
        {
            var allocator = new ChunkHandleAllocator(64);
            var store = new ChunkStore(pool, allocator);
            store.InsertChunk(new Chunk { coord = new int3(0, 0, 0), isUniform = true, uniformMaterial = 0 });

            var edits = new EditService();
            edits.AttachWorld(store, store, store, null);   // headless: no mirror
            edits.AttachLightBuffer(_lights);

            var v = new int3(50, 50, 50);
            edits.TrySetVoxel(v, Materials.Torch);
            edits.TrySetVoxel(v, Materials.Lava);
            Assert.AreEqual(1, _lights.RegisteredCount, "torch -> lava left two lights in one cell");

            edits.TrySetVoxel(v, Materials.Stone);
            Assert.AreEqual(0, _lights.RegisteredCount, "lava -> stone left a light behind");
        }
        finally { pool.Dispose(); }
    }

    /// An EditService with NO light buffer attached must behave exactly as it
    /// did before Phase 7. Every pre-Phase-7 test and rig runs that way, and a
    /// null here has to be a no-op rather than an error -- the same contract
    /// _fluids and _dirty already have.
    [Test]
    public void EditPath_NoLightBufferAttachedIsANoOpNotAnError()
    {
        var pool = new BrickDataPool(4000);
        try
        {
            var allocator = new ChunkHandleAllocator(64);
            var store = new ChunkStore(pool, allocator);
            store.InsertChunk(new Chunk { coord = new int3(0, 0, 0), isUniform = true, uniformMaterial = 0 });

            var edits = new EditService();
            edits.AttachWorld(store, store, store, null);   // headless: no mirror
            Assert.IsFalse(edits.HasLightBuffer);

            Assert.IsTrue(edits.TrySetVoxel(new int3(10, 10, 10), Materials.Torch));
            Assert.AreEqual(0, edits.LightsRegistered);
        }
        finally { pool.Dispose(); }
    }

    // Local helper: UnityEngine.Mathf is available, but rounding a float world
    // position back to an integer metre index is the only place this file needs
    // it and a named helper says what it is for.
    private static class Mathf
    {
        public static int RoundToIntSafe(float v) => (int)math.round(v);
    }
}
