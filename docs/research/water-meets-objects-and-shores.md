# Research: water meeting objects and shores

How shipped games and documented ocean systems handle shore swash, foam at objects, boat wakes,
screen-space refraction edges, and stylized versions of all of these. Research only, no code changes.
Written 2026-10-24.

**How to read this.** Every technique lists its sources. Anything marked **(unverified)** comes from
memory or from a secondary source I could not check against the original. "Our setup" means: 3 FFT
cascades, the camera-local world-space foam field (drifts, decays), one surface shader, `SampleWater` on
the CPU, the sea floor (depth, signed shore distance, sea vs. lake), and the water bodies list.

**What the screenshots show.**
- Screenshot 2 (rock and boat): the rings around the rock look like contour lines. Each is the same
  width all the way round and nothing on the water causes it. The dark smear at the rock's bottom right
  is most likely screen-space refraction picking up rock pixels (topic 4).
- Screenshot 3 (beach): the foam band follows the shore distance at a constant width. Real foam here
  would be patchy and would come and go with each wave.
- Screenshot 1 (storm): the crest foam already comes from the waves and looks right. Topics 1 to 3 try
  to give the shore and objects that same "caused by the water" quality.

---

## The one pattern most sources share

The best-documented approach for stylized games is Rare's, in **Sea of Thieves**. They found the
contact points (wave peaks, depth-buffer intersections with objects), **wrote those points into a foam
buffer**, let the buffer blur and fade over time, and only then textured it with artist foam. Object and
shore foam are never drawn straight from a distance function. Foam is put in at the contact point and
the simulation spreads it, so the shape comes from what the water is doing.
([Ang et al., *The Technical Art of Sea of Thieves*, SIGGRAPH 2018 Talks](https://history.siggraph.org/wp-content/uploads/2022/09/2018-Talks-Ang_The-Technical-Art-of-Sea-of-Thieves.pdf))

Our foam field already drifts and decays, so most of the recommendations below come down to one change:
**stop drawing shore and object foam in the surface shader, and inject it into the foam field instead**,
scaled by what the waves are doing at that point.

---

## 1. Shore push and pull (swash and backwash)

| # | Technique | Looks like | Cost | Art control | Source |
|---|---|---|---|---|---|
| 1a | Foam from water depth, damped waves in shallows | Foam band that is wider on gentle slopes and thinner on steep ones; waves lose energy as the bottom rises | Very low | Medium | Crest docs |
| 1b | Analytic shore wave trains along a shore field or spline | Waves that rise, break and lose energy along a set range | Low | High | Unity HDRP, Crest |
| 1c | Full 2D shallow-water simulation | Run-up, backwash and collisions all emerge; lacy foam; true wet line | Medium (GPU compute) | Low to medium | Godot RSW, Sea of Thieves (local use) |
| 1d | Boundary-aware procedural waves | FFT/procedural waves that reflect off terrain correctly | Research-grade | Medium | Jeschke et al. 2020 |
| 1e | Faked swash: masks, scrolling textures, vertex animation | A sheet sliding up and back on a timer | Very low | Very high | Tutorials only |
| 1f | Foam as GPU particles | Foam that clumps, stretches and leaves a lip at the end of a broken wave | Medium (about 300K particles) | Low | 80.lv / OMYOG |

**1a. Depth-threshold foam and damping in shallows (Crest).** Crest renders the seabed into a top-down
depth cache. It uses that depth for three things: damping large waves in shallow water, generating foam
near shorelines, and shallow-water shading. The rule of thumb they give is that a wave starts to feel the
bottom when the depth is less than half its wavelength. Shoreline foam is a threshold on water depth,
**not** on distance to shore, and the docs point out the result: "the width of the foam band can vary
based on terrain slope". They also warn that a sudden step in the seabed shows up as a line of foam.
Crest's own docs call efficient realistic shoreline waves "a challenging open problem".
[Crest: Shorelines and Shallows](https://crest.readthedocs.io/en/stable/user/shallows-and-shorelines.html) ·
[Crest 4.9 version of the same page](https://crest.readthedocs.io/en/4.9/user/shallows-and-shorelines.html)
- *Our setup:* we already have depth. Using depth (not shore distance) as the foam input on its own
  removes the constant-width band. Per-cascade damping by depth (scale cascade *i* down where
  `depth < λ_i / 2`) makes the swell visibly slow down and shrink near shore. Both are cheap changes to
  the surface shader and `SampleWater`.

**1b. Analytic shore waves (Unity HDRP Shore Wave deformer, Crest Gerstner on a spline).** Unity's water
system has a *Shore Wave* deformer with these parameters:
- *Skipped Waves*: the share of waves left out, so the train is irregular.
- *Breaking Range*: the wave peaks at the start of this range, makes surface foam inside it, and has lost
  70% of its amplitude by the end.
- *Deep Foam Range*, plus dimmers for surface and deep foam.

Unity's island sample uses a water mask to remove swells around the island, a deformer for the waves,
foam decals on the beach, and a foam generator behind the waves. Crest offers the same idea with
`ShapeGerstner` placed along a spline.
[Unity HDRP 17: Deform a water surface](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/water-deform-a-water-surface.html) ·
[Unity blog: HDRP water system in 2022 LTS / 2023.1](https://unity.com/blog/engine-platform/new-hdrp-water-system-in-2022-lts-and-2023-1) ·
[Crest: Waves](https://crest.readthedocs.io/en/stable/user/waves.html)
- *Our setup:* this is what our surf rings were, and the fixes Unity relies on (skipped waves, a breaking
  range, foam made only while breaking) explain why ours looked painted. Ours had a constant phase, no
  skips, and foam drawn instead of generated. If any version comes back, it should only drive swash on
  the sand (1e) and feed foam into the foam field. It should not draw rings on open water.

**1c. Full shallow-water simulation.** The *Godot Realistic Shoreline & Ocean Waves* asset is for Godot
4.7 Forward+, the same as ours. It runs the nonlinear shallow-water equations on the GPU with a
Kurganov–Petrova central-upwind scheme.
- What emerges: shoaling, breaking bores, run-up, backwash, and collisions with the next wave, all "All
  emergent, none scripted".
- Foam: carried along by the flow, rendered as lace that breaks up hole-first, and it leaves arcs on the
  sand.
- Wet sand: darkens wherever water reached and dries over about 45 seconds.
- Cost: on an RTX 4070 laptop, a beach grid of 608² cells at 5 cm runs at about 60 fps with the solver
  under 5% of the GPU. An island grid of 2048² at 1 m runs at about 28 fps.
- Deep water: a separate Gerstner layer.

Rare also used a GPU shallow-water simulation (Mei et al. 2007) for local effects such as water on a ship
deck.
[Godot RSW (itch.io)](https://reboot16.itch.io/godot-rsw) ·
[Godot Asset Store listing](https://store.godotengine.org/asset/reboot16/waves/) ·
[Sea of Thieves talk §1.2](https://history.siggraph.org/wp-content/uploads/2022/09/2018-Talks-Ang_The-Technical-Art-of-Sea-of-Thieves.pdf)
- *Our setup:* a small simulation grid that follows the camera, only along beaches the camera can see,
  with the FFT height at its outer edge as the input wave. This is the "real" answer to swash. Its cost
  is a new subsystem (a compute pass, a state texture, edge coupling to the FFT), and it is the hardest
  of these to art-direct. The RSW numbers suggest a beach-sized grid is affordable.

**1d. Boundary-aware procedural waves (Jeschke, Hafner, Chentanez, Macklin, Müller-Fischer, Wojtan,
SCA 2020).** This extends procedural ocean waves so they obey terrain boundary conditions. Together with
a particle system for breaking waves, foam and spray, it models waves meeting beaches and rocks at
interactive rates on a commodity PC.
[Eurographics Digital Library](https://diglib.eg.org/handle/10.1111/cgf14100) ·
[ISTA record](https://research-explorer.ista.ac.at/record/8766)
- *Our setup:* worth reading for ideas. It is too much to build for a cozy scene.

**1e. Faked swash.** The only documented examples are tutorials: vertex offset driven by a heightmap
scrolled back and forth with a sine
([Styly / Shader Forge](https://styly.cc/en/tips/shaderforge_wave01)), and a mask-based beach workflow
where a soft sea mask is warped by foam noise and its inverse is the dry sand
([Planetside forum](https://planetside.co.uk/forums/index.php?msg=1277)). **(Unverified as a
shipped-game technique.)** No studio talk I found documents a faked swash sheet in detail.
- *Our setup (my own design, unverified):* treat swash as **1D per shore texel**. For each beach point:
  1. Take the incoming wave from `SampleWater`-style FFT height and vertical velocity at a point a few
     metres offshore along the shore-distance gradient.
  2. Turn that into a run-up length that grows with wave height and shrinks with beach slope.
  3. Animate a thin sheet up the slope that slows down as it climbs (something like `sin` from 0 to π/2
     on the way up, then a slower drain back).
  4. Write foam at the sheet's leading edge into the foam field, so it lingers on the sand as the sheet
     drains.
  5. Store a "max reach" value that fades over time and use it for wet-sand darkening.

  Because each wave comes from the FFT, no two run-ups match. That is what the surf rings were missing.

**1f. Foam as particles.** Leonard Saalfrank's coastal renderer replaced advected foam textures with up
to 300K GPU particles drawn into a density map. Foam then collects in patches, stretches, breaks up
around waves, and forms a lip at the end of a broken wave.
[80.lv: Real-time shoreline simulation in a custom C++ coastal renderer](https://80.lv/articles/real-time-shoreline-simulation-in-custom-c-coastal-renderer)
- *Our setup:* not needed now. Our foam field gives most of this if injection is patchy and noise breaks
  it up.

---

## 2. Waves meeting objects (rocks, piles, hulls)

| # | Technique | Looks like | Cost | Art control | Source |
|---|---|---|---|---|---|
| 2a | Depth-buffer intersection written into a foam buffer, with blur feedback | Soft foam that builds up and drifts away from contacts | Low | High | Sea of Thieves |
| 2b | Whitecaps from pinched waves (Jacobian) | Foam where the surface folds | Already ours | Medium | Tessendorf, Crest |
| 2c | Dynamic wave simulation that bodies push into | Real ripples and reflections around objects | Medium | Medium | Crest, Unity |
| 2d | Obstacles as raised seabed in a shallow-water simulation | Diffraction, reflection, overtopping | Comes with 1c | Low | Godot RSW |
| 2e | Toon intersection line from depth difference | Clean white outline at every contact | Very low | High | Roystan, Wind Waker style |

**2a. Inject at contact points, let the foam buffer spread it (Sea of Thieves).** Rare's own description:
- foam is "added around objects that intersect the water surface within a camera centered window using
  depth buffer comparisons";
- they "progressively blur the result of the foam buffer with feedback" so it disperses and softens;
- the result is blended with artist-authored foam textures;
- in calm water, foam appears only around intersecting objects. Stormy water gets more foam everywhere.

[Ang et al. 2018](https://history.siggraph.org/wp-content/uploads/2022/09/2018-Talks-Ang_The-Technical-Art-of-Sea-of-Thieves.pdf)
- *Our setup:* this maps almost one to one. Our foam field is the camera-centred window, and the water
  bodies list (footprint, top, draft) is a cheaper contact test than a depth buffer. A good injection
  term (my own design, unverified) is
  `contact(sdf ≈ 0) × max(0, upward water velocity, or height above the local mean)`.
  Foam then appears **when a crest reaches the rock** and drifts and fades between waves, instead of a
  fixed ring.

**2b. Whitecaps from the Jacobian.** Rare made foam at wave peaks "using the method described in the
reference paper" (Tessendorf). Crest finds places where waves are 'pinched' and deposits foam there.
[Crest: Shorelines (foam section)](https://crest.readthedocs.io/en/4.9/user/shallows-and-shorelines.html)
- *Our setup:* we already do this (screenshot 1). One cheap boost: lower the Jacobian threshold near
  objects and in shallow water, so waves that fold against a rock make more foam.

**2c. Dynamic wave simulation that bodies push into.** Crest adds a small wave-equation simulation on top
of the FFT and feeds it with `SphereWaterInteraction` components. Hulls are approximated by several
spheres. The settings are simulation frequency (default 60 Hz), damping (default 0.05) and Courant number
(default 0.7). The docs warn that too much feedback produces ring artifacts. Unity's equivalent is water
decals, which in Unity 6 replaced deformers and foam generators. Yuksel's *Wave Particles* (2007) covers
the same ground with particles instead of a grid, and scales to several hundred objects.
[Crest: Waves / dynamic waves](https://crest.readthedocs.io/en/stable/user/waves.html) ·
[Crest 4.16 ocean simulation](https://crest.readthedocs.io/en/4.16/user/ocean-simulation.html) ·
[Unity discussions: Water Decal in Unity 6](https://discussions.unity.com/t/how-do-i-make-a-water-deformer-in-the-newest-unity-6-version-using-hdrp-water-system/1541767) ·
[Yuksel, House, Keyser, *Wave Particles*, SIGGRAPH 2007](https://history.siggraph.org/?p=115408)
- *Our setup:* a camera-local height texture (for example 256² over the same window as the foam field),
  stepped in compute, with stationary bodies as walls and moving bodies as sources. Its height adds to
  the FFT displacement in the shader and in `SampleWater`. That gives real rings around rocks and real
  wakes (topic 3). It is the main medium-cost upgrade. Do it after 2a if 2a alone is not enough.

**2d. Obstacles as raised seabed.** In Godot RSW, boulders, reefs, breakwaters and quay walls are just
raised seafloor, and diffraction, wakes, reflection and overtopping all come from the solver.
[Godot RSW](https://reboot16.itch.io/godot-rsw)
- *Our setup:* we already fold stationary bodies into the shore data, which is the same idea. It pays off
  fully only together with 1c or 2c.

**2e. Toon intersection line (what to avoid, or how to stylize it).** The common toon shader finds the
depth difference between the water and the scene behind it and draws foam where that difference is
small. Roystan's tutorial adds a noise cutoff so the line breaks up, and scales the line by the surface
normal so vertical objects get thinner lines. Wind Waker is widely said to use foam lines to hide water
edges **(unverified, community sources only)**.
[Godot port of Roystan toon water](https://godotshaders.com/shader/toon-water-shader/) ·
[OpenUPM package](https://openupm.com/packages/net.roystan.toonwatershader/) ·
[Terasology forum on Wind Waker foam](https://forum.terasology.org/threads/hide-water-edges-with-foam-effects.722/)
- *Our setup:* this is our current uniform ring. If we keep a contact line at all, it should be thin,
  broken up by noise, and its width should come from wave energy (2a), not a constant.

---

## 3. Boat wakes

| # | Technique | Looks like | Cost | Scales with speed or size by | Source |
|---|---|---|---|---|---|
| 3a | Foam stamps rendered into a foam texture | White trail that drifts and fades | Very low | Stamp size and strength | Sea of Thieves, Unigine/Godot forums |
| 3b | Dynamic wave simulation with hull spheres | Real V wake, bow wave, wakes crossing each other | Medium | Comes from the physics | Crest |
| 3c | Wave particles or wave packets | Expanding wavefronts; physically plausible V | Medium | Comes from the physics | Yuksel 2007, Naughty Dog, Jeschke & Wojtan 2017 |
| 3d | Analytic Kelvin pattern (decal or shader term) | Clean chevron of ripples behind the hull | Low | Length and amplitude parameters | NVIDIA Triton, Chan thesis |
| 3e | Bow wave deformer and spray particles | A bump at the bow, spray at speed | Low | Elevation from speed | Unity HDRP |

**3a. Foam trail written into the foam texture.** Two forum threads and Rare describe the same pattern:
- Unigine forum: particles never line up with waves, so the fix was to render foam decals into a texture
  every frame and project that texture in the water's fragment shader.
- Godot forum: the same idea in Godot, using a SubViewport or decal.
- Sea of Thieves: object foam goes into a foam buffer with feedback (2a).

[Unigine forum: ship trail and ocean waves](https://developer.unigine.com/forum/topic/1807-solved-problem-with-ship-trail-and-ocean-waves/) ·
[Godot forum: GPUParticles2D onto a 3D plane](https://forum.godotengine.org/t/rendering-gpuparticles2d-onto-a-3d-plane/81460) ·
[realtimevfx thread](https://realtimevfx.com/t/how-to-create-a-water-trail-for-a-ship-on-moving-water/14916)
- *Our setup:* for each moving body in the list, each frame, stamp foam into the foam field:
  - along the waterline footprint, more at the bow and stern;
  - plus two arms trailing behind at about ±19.5°, the Kelvin half-angle;
  - strength proportional to speed above a threshold, width proportional to beam.

  The field's existing drift and decay then do the rest. This is the best fit for the cozy look.

**3b. Dynamic wave simulation.** Crest makes wakes by putting spheres along the hull into its dynamic
wave simulation. The weight settings and *Velocity Offset* control the bow push.
[Crest: Waves](https://crest.readthedocs.io/en/stable/user/waves.html)
- *Our setup:* the same simulation as 2c. One system covers rock ripples and wakes.

**3c. Wave particles and wave packets.** Naughty Dog brought wave particles into games in Uncharted 3:
artist-tunable pulses instead of a full spectrum. Uncharted 4 later used them for river rapids. Jeschke &
Wojtan's *Water Wave Packets* (2017) moves packets of wave energy as particles. It is unconditionally
stable and real-time "for moderately challenging scenarios", and the same team published a separate wake
model that keeps only the visually dominant waves.
[GDC 2012: Water Technology of Uncharted](https://gdcvault.com/play/1015309/Water-Technology-of) ·
[Wave Particles with Interactive Vortices (reimplementation)](https://github.com/acskyline/wave-particles-with-interactive-vortices) ·
[Water wave packets (SIGGRAPH archive)](https://history.siggraph.org/?p=102417) ·
[ISTA news: Making waves](https://ista.ac.at/en/news/making-waves/)
- *Our setup:* an alternative to a grid for 2c/3b. With only a few boats, a few hundred wave particles
  splatted into a height texture is cheap and has no grid-resolution limit. It is harder to make work
  with shore boundaries.

**3d. Analytic Kelvin wake.** NVIDIA's Triton SDK renders "3D Kelvin wakes and propeller backwash" in
one pass. A 2024–25 ULiège thesis builds physically based Kelvin wakes in UE5 Niagara using Michell
thin-ship theory. On physics: the classic Kelvin half-angle is constant (about 19.47°), but measured wakes
narrow at high hull Froude number (Rabaud & Moisy).
[NVIDIA Triton Ocean SDK](https://developer.nvidia.com/triton-ocean-sdk) ·
[Chan thesis, ULiège](https://matheo.uliege.be/handle/2268.2/25042) ·
[*Ship wakes: Kelvin or Mach angle?* (arXiv 1304.2653)](https://arxiv.org/pdf/1304.2653)
- *Our setup:* a chevron normal/height term in the surface shader, evaluated in each boat's local frame,
  with a speed and length fade. It looks clean but stylized, and it does not interact with other waves.
  Fine for a cozy scene. A dynamic simulation is better if boats cross each other's wakes.

**3e. Bow wave and spray.** Unity's *Bow Wave* deformer is a single elevation parameter parented to the
boat. It is not reprojected each frame, unlike foam.
[Unity HDRP 17: Deform a water surface](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/water-deform-a-water-surface.html)
- *Our setup:* a small height bump at the bow scaled by speed. Spray only above a speed threshold, which
  matches the "rare, big hits only" decision.

**Keeping water out of hulls.**
- Unity uses a *Water Excluder* mesh, and it must cover the hold's floor, walls and ceiling or it fails
  when the camera is inside
  ([Unity discussions](https://discussions.unity.com/t/how-do-i-make-a-water-deformer-in-the-newest-unity-6-version-using-hdrp-water-system/1541767)).
- Crest has *Push Water Under Convex Hull* and a clip surface
  ([Crest: Waves](https://crest.readthedocs.io/en/stable/user/waves.html)).
- In Godot, the options are a stencil mask (Godot 4.5+ has material stencil support **(unverified)**), or
  a depth-only "excluder" mesh drawn before the water so the water fails the depth test inside the hull.
  For our footprint/top/draft bodies, a cheap clip in the surface shader also works: discard where the
  point is inside the hull footprint and below the deck.

---

## 4. Screen-edge artifacts of screen-space refraction

| # | Technique | What it fixes | Cost | Source |
|---|---|---|---|---|
| 4a | Refraction mask / depth check at the offset UV, fall back to the unoffset UV | Objects in front of the water bleeding into the refraction (the smear in screenshot 2) | 1 extra depth tap | GPU Gems 2 ch. 19 |
| 4b | Fade the offset near screen borders | Hard line where the offset UV leaves the screen | Trivial | Blender EEVEE, godotshaders |
| 4c | Scale the offset by water thickness; read depth after the offset | Shore halos; wrong fog at sharp depth changes | Trivial | OpenMW MR 2922 |
| 4d | Clamp UVs and shrink the offset with distance and at grazing angles | Swimming artifacts in the distance | Trivial | General practice **(unverified)** |

**4a. Refraction mask (Tiago Sousa, GPU Gems 2 ch. 19).** Sample the scene twice, once at the offset UV
and once without the offset. Mark where water is in a mask. If the offset sample is not water, use the
unoffset sample. The chapter notes this is not physically exact and replacing occluder colours can leave
slight artifacts. In Godot, the depth texture can stand in for the mask: if the scene depth at the offset
UV is **closer to the camera than the water surface**, the sample hit something in front of the water,
so fall back to the unoffset UV, or shrink the offset and resample.
[GPU Gems 2, ch. 19: Generic Refraction Simulation](https://developer.nvidia.com/gpugems/gpugems2/part-ii-shading-lighting-and-shadows/chapter-19-generic-refraction-simulation)

**4b. Edge fade.** Blender EEVEE fades refracted and reflected pixels near the screen edge, with the
width set as a percentage of the screen. Godot community water shaders do the same with an edge-fade
function.
[Blender manual: Screen Space Reflections](https://docs.blender.org/manual/nl/4.1/render/eevee/render_settings/screen_space_reflections.html) ·
[godotshaders: traced and simple refraction](https://godotshaders.com/shader/realistic-water-with-traced-and-simple-reflection-and-refraction-v2/)
- For refraction, fade the **offset** (not the colour) to zero near the edges, for example
  `offset *= smoothstep(0, 0.05, min(uv, 1-uv))` per axis. Then the sample never leaves the screen.

**4c. Thickness-scaled offset.** OpenMW changed refraction fog to depend on water depth between the
object and the camera, and read the depth after applying the offset. That fixed misalignment at sharp
depth changes, though the author says shore artifacts were only lessened.
[OpenMW MR 2922](https://gitlab.com/OpenMW/openmw/-/merge_requests/2922)
- *Our setup:* scale the offset by `saturate(waterThickness / k)` so the refraction fades to nothing
  where the water meets rocks and beaches. That also removes most of the halo at the rock contact line.

---

## 5. Stylized examples

- **Sea of Thieves (Rare).** FFT water with stylized colour and a wave-peak mask taken from choppiness
  offsets. Foam is generated physically (peaks, depth intersections), blurred with feedback, then
  replaced by artist textures. The amount of foam is set by the weather state; calm water has foam only at
  objects. Local shallow-water simulations handle splashes on deck.
  [SIGGRAPH 2018 talk](https://history.siggraph.org/wp-content/uploads/2022/09/2018-Talks-Ang_The-Technical-Art-of-Sea-of-Thieves.pdf) ·
  [MCV dev diary coverage](https://www.mcvuk.com/development-news/rares-sea-of-thieves-started-with-photorealistic-water/).
  This is the closest match to our project: physical at the base, stylized at the end.
- **Unity HDRP island sample.** A water mask removes swells around the island, foam decals sit on the
  beach, foam generators sit behind waves, and an excluder keeps water out of the boat.
  [Unity blog](https://unity.com/blog/engine-platform/new-hdrp-water-system-in-2022-lts-and-2023-1).
  The lesson: **calm the swell near the island on purpose**. Cozy shores read better when the big
  waves die out before reaching them.
- **Wind Waker style.** Flat shading, no reflections, white foam lines around shores and rocks
  **(unverified, community sources only)**.
  [godotshaders recreation](https://godotshaders.com/shader/wind-waker-water-no-textures-needed/) ·
  [80.lv: stylized water for Nimue](https://80.lv/articles/how-to-build-stylized-water-shader-design-implementation-for-nimue/)
- **Roystan toon water and its Godot ports.** Depth-difference shoreline, a noise cutoff for foam, and
  normal-based line width.
  [Godot port](https://godotshaders.com/shader/toon-water-shader/)
- **Godot stylized water asset.** Vertex colour paints foam islands, hidden water in caves, and areas
  where waves stop, such as under piers.
  [Godot Asset Store](https://store.godotengine.org/asset/emace-art/stylized-water-shader/).
  This is a useful manual override for art direction.
- **Not found:** no technical talks on water in Tchia, Ghost of Tsushima, Horizon, AC IV Black Flag
  (only press-level), or Far Cry 5 (the GDC abstract does not cover foam or shore).

---

## Ranked recommendation for a stylized small-island scene

1. **Fix the refraction edges (4a + 4b + 4c).** One extra depth tap, an offset fade near the screen
   edge, and an offset scaled by water thickness. This fixes a visible bug (the smear at the rock) at
   almost no cost. Do it first.
2. **Move shore and object foam into the foam field (2a, Sea of Thieves pattern).** Remove the
   constant-width band. Inject foam at contacts (body footprints from the bodies list, plus water
   depth below a threshold on beaches), scaled by **local wave energy**: upward velocity or height above
   the mean, from the FFT. The field's drift and decay break it up. Add a noise cutoff when shading.
   This gives "foam when a crest hits the rock" with no new systems.
3. **Damp the FFT cascades by depth (1a), and calm swell near islands on purpose (Unity sample).** Waves
   visibly slow and shrink toward the beach, and the shore reads as calm and cozy. Cheap, and it also
   makes `SampleWater` more believable for boats near shore.
4. **Boat wake as foam stamps (3a), plus a small bow bump (3e).** Stamp along the hull and two Kelvin
   arms at about ±19.5°, scaled by speed and beam, and let the foam field carry them. Spray only above a
   speed threshold. Add a hull clip (discard inside footprint, below deck).
5. **Faked 1D swash driven by the FFT (1e, own design).** Run-up length per beach texel from the
   incoming wave height and the slope. A sheet that slows as it climbs, foam injected at its edge, and a
   fading "max reach" for wet sand. This is the art-directable answer to swash without a fluid solver.
6. **Later, if 2–5 are not enough: a camera-local dynamic wave simulation (2c/3b)**, or for beaches a
   small shallow-water grid (1c; Godot RSW shows about 5% GPU at 608² on a laptop 4070). One simulation
   gives real rings around rocks, real wakes, and wakes that interact. It costs a new subsystem and is
   harder to steer artistically.
7. **Splashes:** particles only on big impacts (body vertical speed above a threshold), never along
   whole beaches. This matches what was already decided.

**Not recommended:**
- Analytic surf rings or shore wave trains drawn on open water (1b as we had it).
- Uniform toon intersection lines (2e).
- Full research methods (1d, wave packets) for this project's scope.
