# Ocean guide: drop-in FFT ocean for Godot 4.7 (C#)

This file is the complete brief. Build the project it describes in **this folder**, which already holds an empty Godot 4.7 project. Everything you need is in this file or downloadable from the URLs it gives. **The only inputs are this folder and those URLs.** Don't read, search or reference anything else on this machine. The one exception is locating the Godot executable (section 5.3).

Every technical choice and value below was verified in an earlier build: the simulation, foam and shading are proven. The parts that are new in this build are marked **(new)**: the architecture (renderer / demo / dev split), JSON configuration, the parameter-driven UI, and the in-engine tooling. Where an early assumption turned out wrong, the guide says so.

`docs/reference.webp` is the target picture: an evening sky, the sun ahead with a glint path, a rock island right of the glint, foam on near crests, a hazy horizon, 1169 fps. Match its look and composition.

Placeholders: `<godot>` = the Godot 4.7 **mono** console executable; `<project>` = this folder.

`docs/ocean_showcase_img.png` and `docs/ocean_showcase.mp4` show what the finished build looks like (breeze preset, the demo's default view). Sources and inspirations are listed in section 20.

## Findings (read these first)

What building and refining this ocean taught, each with where it is specified. Every one of them was a visible bug or a wasted iteration once.

**Look**
- **Foam follows Sea of Thieves:** a progressively blurred foam field plus a sharp crest texture that blends to a softer version as foam ages (9 intro, 9.2, 9.3).
- **The foam body must stay coherent:** the tail is the crest texture at the same place, only blurred; age starts late (0.6); the tail is fainter (0.6); the blur is moderate (1.5 / 0.3 m²/s). A separate low-frequency texture, early age or strong blur made blobs and "too much foam all at once" (9 intro, 9.3, 7).
- **Use exactly the specified lighting recipe:** small deviations made a dark "shadow" in the foam veil, blue streaks along lace edges and embossed foam. Fix the lighting, never hide the lace relief (9.3, 9.4).
- **Anisotropic filtering on the sharp crest texture only:** without it foam smears into streaks; on every tap it costs 0.05 ms for nothing (9.3).
- **Hex tiling needs an integer hash:** a `sin` hash gave hard straight seams with sheared foam. Don't add a luminance-weighted blend or halve the rotation range either (4, 9.3).
- **Shore depth is measured along the view ray:** a vertical depth made every crest above the horizon transparent (10).
- **Displacement is mip-filtered to each vertex's spacing:** point-sampled, the outer rings alias and the far sea swims as the camera moves, with fake crests that get no foam (8.2, 8.3, 10).
- **Distant crests keep foam:** `foam_fade_range` 400 m; 220 m left crests beyond ~300 m bare (9.5).
- **Verify sky sun positions by capture:** automatic measurement from the image was up to 13° off (11).

**Process**
- **Compare A/B on the same frame:** captures restart the simulation, so the same pose gives the same sea (13).
- **Isolate the layer before changing anything:** debug-render field, detail texture, coverage and normals on one frame (14).
- **Check heavy foam, not just calm:** judge foam under `ocean_breeze` at 1:1, plus a low eye-level pose; calm hides texture defects (14).
- **Measure every GPU cost:** 3 bench runs each way, and justify it with a same-frame capture (1, 19).
- **Port verified recipes as written:** "improving" one from memory of a paper caused regressions (19).
- **Streak anisotropy is report-only:** it stays near 1 because crest lines are as long as trails (9.6).

**Engine and tooling**
- **Ship builds strip `dev/`:** rebuild normally before the next dev run, or Godot hangs (13).
- **Exports skip `.json` by default:** add `*.json` to the export filter or presets, skies and configs go missing (4).
- **Godot imports every folder:** `docs/`, `tests/` and `.scratch/` carry a `.gdignore` (3).

---

## 0. Working rules

- **Work autonomously to the end.** Don't stop to ask, confirm or summarise midway. Make the call yourself and record it in the final report.
- **Write only inside this folder.** Scratch output (captures, logs, downloads) goes into `.scratch/` (git-ignored, with an empty `.gdignore`).
- **No machine-specific paths in committed files.** The Godot executable path lives only in a git-ignored local file (section 5.3).
- **Don't change system settings**: no global environment variables, no machine-wide installs.
- **Never kill or capture a process you didn't start.**
- **Verify every step by running it**: build, test, launch, capture, look at the image. Each milestone (section 15) has a gate; never stack unverified steps.
- **Judge visuals from the whole angle set and in motion** (section 14). Things that look fine from 40 m can look broken at 5 m, and stills hide flicker. Change one visual thing at a time and compare against the previous capture from the same pose.
- **Edit text with exact replacements, not regex over docs.** One regex replace of a Markdown table (`a\|b\|c`, where `\|` means "or") once rewrote words all over the docs.
- **Git:** `git init` if needed. Commit after every milestone gate with a message starting `M<n>:`. Never push.

### If you were interrupted
A session may stop mid-build (usage limits, crashes) and a fresh one may continue. Before doing anything else: run `git log --oneline`, find the last `M<n>:` commit, run `git status` to see uncommitted work, re-read the section for milestone n+1, and continue from there. Uncommitted work from an interrupted milestone may be half-done: check it builds, then keep or discard it on its merits. Never redo a committed milestone.

## 1. Hard constraints

- Godot 4.7, .NET 10 (`Godot.NET.Sdk/4.7.0`, `net10.0`), Forward+, `rendering_device/driver.windows="d3d12"`. Windows desktop is the target, but nothing outside `dev/` may be Windows-specific.
- All project code is C#. The only third-party code is Calinou's Debug Menu (GDScript, MIT), used in the demo for an FPS readout.
- Wave simulation runs in GLSL compute through the **main** `RenderingDevice`. No CPU FFT at runtime and no per-frame GPU→CPU readback (readback only in `dev/` and tests).
- Keep the existing `project.godot` name and `dotnet/project/assembly_name="AI-Ocean_blueprint"`. The csproj must be `AI-Ocean_blueprint.csproj`.

### Performance (non-negotiable)
1920×1080, 3D scale 100%, vsync off, glow on, no other Godot game running, camera at the hero pose (horizon and glint in view):
- 3 cascades at 512²: **≥ 1000 fps** (≤ 1.0 ms/frame).
- 3 cascades at 1024²: **≥ 700 fps** (≤ 1.43 ms/frame).

The previous build measured 1125–1169 fps at 512² and 978 fps at 1024² on a current high-end desktop GPU; glow costs ~0.12 ms. The texture size is one config value.

**The decisive win:** rendering a frame does not need a simulation step. Ticks run at a fixed rate (60 Hz) on staggered phases (section 8), so at 1000+ fps most frames record no compute at all.

**Where the rest of the frame goes** (last build: 0.76 ms at 512² with glow; the breeze demo view 0.57 ms with glow off; a realistic floor is sub-0.5 ms without glow). Measured items are marked; the rest are estimates and the order to try them in:
- **Glow** (measured): 0.1–0.2 ms. The single largest post cost. Fewer glow levels or a lower `glow_hdr_threshold` range trades directly.
- **Anisotropic filtering on foam** (measured): 0.05 ms when applied to every foam tap; 0 when only on the sharp crest layer (section 9.3). Same lesson for any new texture: measure with and without.
- **Vertices outside the view** (estimate, likely the biggest remaining win): the surface is one 263k-vertex mesh, so it is never frustum-culled and every vertex runs its three displacement taps even behind the camera (~60–70 % of the mesh at 70° fov). Splitting the ring grid into ~8 angular sectors (separate surfaces or `MeshInstance3D`s with tight AABBs, still snapped together) lets Godot cull them. Expect roughly 0.05–0.1 ms.
- **Fewer rings** (estimate): R = 192 instead of 256 is 44 % fewer vertices; the outer rings are already sub-pixel at the horizon. Re-check `low_away` and `high_side` for faceting.
- **Screen and depth texture copies** (estimate): `hint_screen_texture` and `hint_depth_texture` each cost a full-screen copy (~0.02–0.04 ms each at 1080p). They are needed for refraction and shore depth; a variant without refraction for open ocean could drop the screen copy.
- **Fragment foam** (estimate): up to 12 detail taps per pixel near the camera (3 crest, 3 tail, 6 relief). The relief taps are the cheapest to drop (bicubic range only).
- **Engine floor**: measure an empty scene with the same sky, tonemap and fog to know what is not the ocean's. Run every comparison 3× with `--bench=8`; differences under ~0.02 ms are noise.

---

## 2. Art direction

- **Cinematic, soft, painterly, sky-driven.** Not "physically correct", not "high detail". Photo-real foam detail reads as wrong as artefacts do.
- **The environment colours the water.** Body colour stays dark and desaturated; sky reflection, fog and sun do the colouring. A teal body colour makes the water look raw.
- Distant water calms into a soft sky mirror: normals and foam fade with distance.
- **Foam:** mostly clear water. Small bright whitecaps at the crest age into thin, lacy, streaky, translucent trails that fade within a few seconds. No flat white sheets, no wire mesh, no visible tiling, no specks, no flicker. Foam is never brighter than the glint, and it takes light: darker on slopes away from the sun, warm under a sunset.
- No pitch-black facets: water facing away from the sun at grazing angles still reflects sky.

---

## 3. Architecture **(new)**

Three layers. Each one uses only the public surface of the layer below, and the layer below never knows about the one above.

| Layer | Folder | What it is | Ships? |
|---|---|---|---|
| **Renderer** | `ocean/` | The drop-in water: one `Ocean` node, its shaders, the config model, the parameter model. No UI, no lights, no sky, no camera, no command line. Copy the folder into any Godot 4.7 C# project and it works. | yes |
| **Demo** | `demo/` | The showcase scene: sky presets, sun, fog, rock, camera, the debug UI, JSON presets. Uses only the renderer's public API. Delete the folder and the renderer still builds. | yes |
| **Dev** | `dev/` | AI-side tooling: captures, frame bursts, benchmark, GPU self-test. Has its own entry scene. Nothing outside `dev/` references it. | no (stripped before release) |
| Tests | `tests/` | Small TUnit project for the pure maths. | optional |

```
<project>/
  project.godot                    main scene = res://demo/main.tscn; Debug Menu autoload
  AI-Ocean_blueprint.csproj / .sln
  README.md, LICENSE, THIRD_PARTY_NOTICES.md
  ocean/                           THE RENDERER
    Ocean.cs                       the node: public API (section 6), lifetime, tick scheduling
    ocean.tscn                     Ocean (Node3D) > Surface (MeshInstance3D + material)
    core/                          pure C#, no Godot types (compiled into the game AND linked into tests)
      Spectrum/  Fft/  Scheduling/  Foam/  Mesh/  Gpu/  Config/  Parameters/
    simulation/                    OceanPipelines, OceanCascade, RdResources; shaders/ spectrum, fft, mipmap .glsl
    foam/                          OceanFoamField, FoamDetailTexture (procedural fallback); shaders/foam_field.glsl
    surface/                       OceanMesh, SurfaceUniforms, ocean.gdshader, include/ sampling, lighting, foam_composite
    assets/                        foam textures + their READMEs
  demo/
    main.tscn                      WorldEnvironment, Sun, Camera, Rock, Ocean instance, two ParameterPanels, DemoRoot
    DemoRoot.cs                    wires the demo: loads presets, hands parameter sets to the panels
    camera/FreeFlyCamera.cs
    environment/                   SkyPreset (plain C#), SkyRig.cs (applies a preset to WorldEnvironment + Sun)
    props/rock.tscn, rock.gdshader
    ui/                            ParameterPanel.tscn/.cs (generic presenter) + rows/ FloatRow, IntRow, BoolRow, EnumRow, ColorRow, ActionRow, ReadoutRow (.tscn/.cs), demo_theme.tres
    presets/                       ocean_*.json, skies.json
    skies/                         panoramas + READMEs
  addons/debug_menu/               Calinou's Debug Menu v1.2.0
  dev/
    dev_main.tscn                  instances demo/main.tscn + DevHarness
    DevHarness.cs, Captures.cs, GpuChecks.cs, FoamStats.cs, AssetPrep.cs
  tests/Ocean.Tests/               TUnit project (links ocean/core/**)
```

Rules:
- **`ocean/core/` is pure C#** (`System.Numerics`, `System.Text.Json`, plain floats). It compiles into the game assembly like any other file, and the TUnit project links the same files (`<Compile Include="../../ocean/core/**/*.cs" />`). That's what keeps the renderer a single folder to copy: no extra csproj to reference. If a core file touches Godot, the test build fails, which is the guard.
- File-scoped namespaces: `Ocean.*` for the renderer (`Ocean.Core.Spectrum`, `Ocean.Simulation`, …), `OceanDemo.*`, `OceanDev.*`.
- Compact code and comments, no dead code, no Manager/Service layers, no speculative systems. The scope of the parameter system is fixed in section 12; don't grow it.
- **Ship check:** the csproj has `<Compile Remove="dev/**" Condition="'$(Ship)'=='true'" />`. `dotnet build -p:Ship=true` must succeed, and the demo must run without `dev/`.
- Godot imports every folder under `res://`: put an empty `.gdignore` in `tests/`, `.scratch/` and `docs/` (screenshots and videos must not be imported into the game). The game csproj needs `<Compile Remove="tests/**;.scratch/**" />`.

---

## 4. Hard facts (Godot 4.7 / D3D12)

Don't "correct" these from Godot 4.0–4.2 memory.

| Fact | Consequence |
|---|---|
| `RenderingServer.GetRenderingDevice()` + `ComputeListBegin()` from `_Process` works; results are visible the same frame | No local device, no `Submit/Sync` |
| Shaders: `GD.Load<RDShaderFile>(path).GetSpirV()` → `ShaderCreateFromSpirV` → `ComputePipelineCreate`. `.glsl` starts with `#[compute]` then `#version 450` | Check `CompileErrorCompute` and log it |
| `Texture2Drd { TextureRdRid = rid }` exposes a compute texture to a material, no copy | Clear `TextureRdRid` before freeing the texture |
| D3D12 groupshared limit is **32 KB** | FFT shared array `vec4[1024]` (16 KB); in-place Stockham |
| Storage images bind **one mip** | One `TextureCreateSharedFromSlice` view per mip for compute; the material gets the full texture |
| Compute can sample with `UniformType.SamplerWithTexture` + `RDSamplerState` | The foam field samples cascade partials with `textureLod`, and its own history bilinearly |
| Workgroup size via specialization constants is unreliable on D3D12 | Fixed `local_size_x = 256`, loop over N/256 |
| Push-constant byte array length must **equal** the GLSL block size | Explicit pad fields so blocks are multiples of 16 bytes; a test checks sizes and offsets |
| `.tscn` `Transform3D(...)` lists the basis **row-major** | Camera pitch sign flips if you write columns |
| Reversed Z (4.3+) | Linearise with `INV_PROJECTION_MATRIX * vec4(uv*2-1, depth, 1)`, divide by w |
| `hint_screen_texture` + `filter_linear_mipmap` blurs a mip chain every frame | Use `filter_linear` and `textureLod(..., 0)` |
| `LIGHT_COLOR` includes energy × π | Divide by π |
| `DIFFUSE_LIGHT` is multiplied by `ALBEDO` after `light()` | Water scatter into `SPECULAR_LIGHT`; diffuse as `radiance · mix(n·l, wrapped, foam)` for the whole surface (section 9.4): `ALBEDO` already blends water and foam, so don't weight diffuse by coverage again |
| `fract(sin(dot(p, k)) · 43758)` hashes are not reproducible: large `sin` arguments plus per-call-site FMA fusion give different results for the same input | Any per-cell random value that several code paths read (hex-tiling vertices, cells) must use an integer hash (the PCG `hash` of 8.1) |
| Varyings written in `fragment()` are readable in `light()` | Foam coverage, crest factor, bubbles |
| `dFdx/dFdy` inside non-uniform control flow are undefined | Take UV gradients before branching and pass them in |
| Per-pixel rotated UVs break mip selection | `textureGrad` with gradients rotated by the same matrix |
| **Panorama orientation:** a panorama's sun at image azimuth `a` (`u = (atan2(x, −z) + π) / 2π`) appears at world azimuth `a − 180° − yaw` for `Environment.SkyRotation.y = yaw`; Godot's panorama is half a turn from that formula, and the sky turns by −yaw | To put a panorama's sun straight ahead (−Z): `yaw = wrap(a + 180°)`. The light points along the same world direction |
| `OS.GetCmdlineUserArgs()` returns args after `--`; a scene path before `--` overrides the main scene | `<godot> --path . res://dev/dev_main.tscn -- --capture=hero` |
| `GetViewport().GetTexture().GetImage()` reads the rendered frame back (after `await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw)`) | In-engine captures; no OS window grabbing |
| `dotnet new sln` on .NET 10 makes `.slnx` | Use `dotnet new sln --format sln` |
| Imported textures default to no mipmaps and "detect 3D" compression | Foam texture `.import`: `mipmaps/generate=true`, `compress/mode=0`, `detect_3d/compress_to=0`. Panoramas: `mipmaps/generate=true` |
| `Shader.GetShaderUniformList(getGroups: true)` returns `group_uniforms` entries (usage has `Group`/`Subgroup`) | Surface parameter groups come from the shader's own groups |
| `rd.TextureGetData` reads back synchronously; `rd.TextureUpdate` needs `CanUpdateBit` | dev/ only |
| Write files without a BOM | Godot dislikes BOMs in `.tscn`/`.tres` |
| Exports include only resources by default | Add `*.json` to the export preset's non-resource filter, or presets, `skies.json` and `ConfigPath` files are missing from builds |
| A copied addon can keep logging "invalid UID" after import | Run `<godot> --headless --path . --import`; if the warnings remain, delete the addon's `.uid` files and import again |

---

## 5. Setup (milestone M0)

### 5.1 Project
- `project.godot`: add `"C#"` to `config/features`; `run/main_scene="res://demo/main.tscn"`; `config/version="1.0.0"` (the Debug Menu wants it); window 1920×1080, stretch `canvas_items`/`expand`; `window/vsync/vsync_mode=0`; `anti_aliasing/quality/use_debanding=true`; autoload `DebugMenu="*res://addons/debug_menu/debug_menu.tscn"` and its plugin enabled.
- `AI-Ocean_blueprint.csproj`: `Godot.NET.Sdk/4.7.0`, `net10.0`, `EnableDynamicLoading`, `RootNamespace` `Ocean`, `Nullable disable`, the `Compile Remove` lines from section 3. No test packages in the game project.
- `.gitignore`: `.godot/`, `bin/`, `obj/`, `.scratch/`, `local.settings.json`.
- Solution: `dotnet new sln --format sln`, add the game and the test project.

### 5.2 TUnit project (`tests/Ocean.Tests/`)
- `Microsoft.NET.Sdk`, `net10.0`, `OutputType Exe`, `Nullable enable`, `ImplicitUsings enable`, `PackageReference TUnit` (1.75 or newer), `<Compile Include="../../ocean/core/**/*.cs" LinkBase="core" />`.
- **TUnit only**: no `xunit*`, `NUnit*`, `MSTest*`, `coverlet.collector` or `Microsoft.NET.Test.Sdk`. Run with `dotnet run --project tests/Ocean.Tests`. Style:
  ```csharp
  public class JonswapTests
  {
      [Test]
      [Arguments(10f, 150_000f)]
      public async Task PeakFrequencyMatchesFormula(float wind, float fetch)
      {
          var expected = 22.0 * Math.Pow(9.81 * 9.81 / (wind * fetch), 1.0 / 3.0);
          await Assert.That(Jonswap.PeakAngularFrequency(wind, fetch)).IsEqualTo(expected).Within(1e-6);
      }
  }
  ```
  Check the assertion API against the installed version.

### 5.3 Local Godot path
`dev/` needs the Godot executable to launch itself. Ask nothing: look for it in this order and use the first hit: `$env:GODOT_BIN`, `local.settings.json` (`{"godot": "<path>"}`, git-ignored), then search common locations (`Program Files`, the user's Downloads, any `*Godot*4.7*mono*` folder on local drives, limited depth) for `Godot_v4.7*_mono_*console.exe`. Write what you find to `local.settings.json` so later runs reuse it. If you can't find it, stop and say so in your report; that is the one blocker worth stopping for.

### 5.4 Assets (download; never copy from elsewhere on the machine)
Download into `.scratch/downloads/`, process, and commit only the result. Each committed asset gets a README next to it: source URL, author, licence, download date, and the processing applied.

| Asset | Download | Licence | Processing → result |
|---|---|---|---|
| Foam texture (default) | `https://raw.githubusercontent.com/wave-harmonic/crest/master/crest/Assets/Crest/Crest/Textures/foam.png` (751,949 bytes, 512²) | MIT, "Copyright (c) 2019 Wave Harmonic and contributors" (`https://raw.githubusercontent.com/wave-harmonic/crest/master/LICENSE`); keep the full notice in its README | none → `ocean/assets/foam_crest.png` |
| Foam texture (alternative) | `https://ambientcg.com/get?file=Foam001_2K-JPG.zip` (ambientCG "Foam001", Lennart Demes), map `Foam001_2K-JPG_Opacity.jpg` | CC0 | red channel; stretch levels from the 1st..99th percentile to 0..1; gamma so the mean lands at 0.41 (Crest's mean; its p10/p90 are 0.06/0.73); save greyscale PNG → `ocean/assets/foam_ambientcg_001.png` |
| Sky: Evening Road 01 (start sky) | `https://dl.polyhaven.org/file/ph-assets/HDRIs/extra/Tonemapped%20JPG/evening_road_01_puresky.jpg` (8192×4096) | CC0 (Poly Haven; Sergej Majboroda original, Jarod Guest sky edits) | downscale to 4096×2048 (high-quality filter), PNG → `demo/skies/evening_road_01_puresky.png` |
| Skies: stylised sunsets | `https://opengameart.org/sites/default/files/sbs_-_cloudy_skyboxes_-_panorama_0.zip` ("Cloudy Skyboxes", Screaming Brain Studios) | CC0 (the pack's License.txt) | unmodified 2048×1024 PNGs: `Panorama/Panorama_Sky_07…png` → `sbs_cloudy_sky_07.png` (pastel), `…_03…` → `sbs_cloudy_sky_03.png` (amber), `…_17…` → `sbs_cloudy_sky_17.png` (violet); match the files by number |
| Debug Menu | `https://github.com/godot-extended-libraries/godot-debug-menu/archive/refs/tags/v1.2.0.zip` | MIT (Hugo Locurcio and contributors) | copy its `addons/debug_menu/` folder, keep its LICENSE |

Image processing runs once from `dev/AssetPrep.cs` (`--prepare-assets`), using Godot's `Image` API, so it needs no external tools. The Poly Haven API (`https://api.polyhaven.com/files/<asset_id>`, "tonemapped" entry) gives current URLs if a link moves. If a download fails, retry; if a source is gone, use the procedural fallback (foam) or the procedural sky, and report it.

**Gate M0:** game builds and runs the empty demo scene; one TUnit test passes; `dotnet build -p:Ship=true` passes; assets downloaded, processed and documented; `local.settings.json` written; `LICENSE` and `THIRD_PARTY_NOTICES.md` exist (section 16).

---

## 6. Renderer public API **(new)**

This is the whole contract between the renderer and anything that uses it.

```csharp
public partial class Ocean : Node3D
{
    [Export(PropertyHint.File, "*.json")] public string ConfigPath { get; set; }  // optional; loaded in _Ready
    public OceanConfig Config { get; }                         // the live config (a copy is returned by Snapshot())
    public void Apply(OceanConfig config);                     // replaces the whole config; regenerates only what changed
    public void LoadConfig(string path);                       // res:// or user:// or absolute; System.Text.Json
    public void SaveConfig(string path);
    public IReadOnlyList<ParameterSet> Parameters { get; }     // live knobs for UIs/tools (section 12); built in _Ready
    public event Action ConfigChanged;
}
```
- With no `ConfigPath`, the ocean uses `OceanConfig.Default` (the "calm" values in section 7). Dropping `ocean.tscn` into any scene with a camera, a light and an environment gives you water with no setup.
- `Apply` diffs against the current config: changed spectrum fields regenerate that cascade's spectrum; changed resolution rebuilds GPU resources; everything else is picked up on the next tick or written to the material.
- The ocean uses the active viewport camera (`GetViewport().GetCamera3D()`), the scene's light and environment. It never searches the tree for anything else.

## 7. Configuration **(new: plain C# + JSON)**

Plain classes in `ocean/core/Config/`, serialised with `System.Text.Json` (camelCase, enums as strings, indented, invariant culture, unknown fields ignored, missing fields keep defaults).

```csharp
public sealed class OceanConfig
{
    public int Resolution { get; set; } = 512;                 // 512 or 1024
    public float SimulationRate { get; set; } = 60f;
    public CascadeConfig[] Cascades { get; set; } = /* 3 entries, below */;
    public FoamConfig Foam { get; set; } = new();
    public string FoamTexture { get; set; } = "crest";        // "crest" | "ambientcg_001" | "procedural"
    public Dictionary<string, JsonElement> Surface { get; set; } = new();   // overrides of ocean.gdshader uniform defaults by name
}
public sealed class CascadeConfig { PatchSize, WindSpeed, WindDirection (deg), Fetch (m), Suppression (m), TimeScale, HeightScale, Choppiness }
public sealed class FoamConfig { Bias, Gain, InjectRate, Decay, FreshDecay, DiffuseAlong, DiffuseAcross, StreakAngleOffset, WeightSwell, WeightChop, WeightRipples }
```
- Ranges, steps and `exp` flags for the UI live next to the fields as attributes or a static table in core (e.g. `[Range(4, 2000, 0.1, Exp = true)]`). The parameter model (section 12) reads them.
- `Surface` holds only what differs from the shader defaults: floats as numbers, colours as `"#rrggbb"` or `[r, g, b]`.
- `OceanConfig.Default` = the calm preset below. The demo ships `demo/presets/ocean_calm.json` and `ocean_breeze.json`. The demo's ocean instance sets `ConfigPath` to breeze (more dramatic, the first impression); the dev foam targets are defined on calm.

**Calm (default):**

| Cascade | Patch | Wind | Dir | Fetch | Suppression | TimeScale | Height | Choppiness |
|---|---|---|---|---|---|---|---|---|
| 0 swell | 400 | 12 | 20° | 200 km | 0.5 | 1 | 1.0 | 0.9 |
| 1 chop | 73 | 8 | 35° | 60 km | 0.1 | 1 | 0.7 | 0.9 |
| 2 ripples | 13.7 | 5 | 0° | 30 km | 0.02 | 1 | 0.6 | 0.7 |

Foam: Bias 0.6, Gain 5, InjectRate 1.2, Decay 0.35, FreshDecay 2.5, DiffuseAlong 1.5, DiffuseAcross 0.3, StreakAngleOffset 0, weights 1 / 1 / 0.3. (Diffusion was 0.35 / 0.06 in the previous build: too weak to blur foam within its ~3 s life, so trails kept the crest's shape. 3 / 0.8 was too much: a wide faint skirt, "too much foam all of a sudden". Section 9.)

**Breeze:** cascade 0 wind 16, fetch 300 km, height 1.3; cascade 1 wind 11, height 0.85; cascade 2 as calm; foam Bias 0.64, rest as calm.

Ranges: PatchSize 4–2000 exp; WindSpeed 0.5–40; WindDirection 0–360; Fetch 1000–1,000,000 exp; Suppression 0–2; TimeScale/HeightScale 0–3; Choppiness 0–2; Bias −1–2; Gain 0.1–40; InjectRate 0.1–60; Decay 0–5; FreshDecay 0–20; DiffuseAlong 0–20; DiffuseAcross 0–4; StreakAngleOffset −90–90; weights 0–2.

Spectrum lessons:
- Patch sizes ~5.5× apart (400 / 73 / 13.7). Near-integer or close ratios tile visibly from above.
- **Fetch moves the spectral peak**: longer fetch = longer waves. A small cascade needs a short fetch so its peak lands near its own tile size; with a long fetch it only carries the faint tail and looks blobby.
- Calm water comes from lower swell amplitude, not from suppressing small waves.
- Seeds: cascade i uses `i * 7919 + 17`.

---

## 8. Simulation and scheduling (milestone M1)

### 8.1 `spectrum.glsl` (16×16 threads; runs only when a spectrum field changed)
- Wrapped index `m = id − (id ≥ N/2 ? N : 0)`, `k = 2πm/L`. With wrapped indexing the inverse FFT needs **no** (−1)^(x+y) sign fix.
- JONSWAP: `α = 0.076 (U²/(F g))^0.22`, `ωp = 22 (g²/(U F))^(1/3)`, γ = 3.3, σ = 0.07 (ω ≤ ωp) / 0.09; `S(ω) = α g²/ω⁵ · exp(−1.25 (ωp/ω)⁴) · γ^r`, `r = exp(−(ω−ωp)²/(2σ²ωp²))`.
- Donelan-Banner spreading: `β = 2.61 (ω/ωp)^1.3` below 0.95, `2.28 (ω/ωp)^−1.3` below 1.6, else `10^(−0.4 + 0.8393 exp(−0.567 ln((ω/ωp)²)))`; `D(θ) = β / (2 tanh(βπ)) · sech²(βθ)`, θ = wave angle − wind direction wrapped to [−π, π].
- `S(kx, kz) = S(ω) D(θ) (g / 2ω) / k · exp(−k² l²)` (l = Suppression in metres); amplitude `sqrt(2S) · dk`, `dk = 2π/L`; `h0 = gaussian · amp / √2`.
- Store `(h0(k), conj(h0(−k)))` in RGBA32F. **Zero the Nyquist row and column** (`id == N/2`): no Hermitian partner, and it would pollute the packed real FFTs.
- Gaussian (Box-Muller) from a nested PCG hash `s = hash(x + hash(y + hash(seed)))`, `hash(x)`: `state = x·747796405 + 2891336453; word = ((state >> ((state >> 28) + 4)) ^ state) · 277803737; return (word >> 22) ^ word`. **Never** hash `x + 256·y`: it collides at 512².

### 8.2 `fft.glsl` (256 threads, `shared vec4 line[1024]`)
- Stockham radix-2 inverse, in place with barriers: per stage `span = 1, 2, …, N/2`, thread j (< N/2, up to two per thread) reads `u = line[j]`, `v = line[j + N/2]`, `k = j & (span − 1)`, twiddle `w = e^{+iπk/span}`; after a barrier writes `line[((j−k)<<1)+k] = u + v·w` and `line[that + span] = u − v·w`. Twiddles computed inline.
- Packing (two real fields per complex FFT), `u = k/|k|`, `h(k,t) = h0 e^{iωt} + conj(h0(−k)) e^{−iωt}`, `ω = sqrt(g|k|)`:
  - A.xy = Dx + i·Dz: `i·ux·h − uz·h`
  - A.zw = Dy + i·dDy/dx: `h − kx·h`
  - B.xy = dDy/dz + i·dDx/dx: `i(kz·h − kx·ux·h)`
  - B.zw = dDz/dz + i·dDx/dz: `−kz·uz·h + i(−kx·uz·h)`
  - Sign check: `Dx hat = +i·ux·h` with `x' = x + λDx` sharpens crests.
- Pass 0, dispatch `(1, N, 2)`: evolve h0 to time t **while loading** each row, inverse-FFT the row; z selects pair A or B; write to `spectrum_a/b` (RGBA32F images: tiled, so column reads stay cache-friendly).
- Pass 1, dispatch `(N, 1, 1)`: inverse-FFT pair A's column, keep the results in registers, then pair B's, then assemble (no separate dispatch):
  - `h = HeightScale`, `l = Choppiness · h`; displacement `(l·Dx, h·Dy, l·Dz, J)` → RGBA16F **with full mips** (section 10: the outer mesh rings need them).
  - `Jxx = 1 + l·dDx/dx`, `Jzz = 1 + l·dDz/dz`, `Jxz = l·dDx/dz`, `J = Jxx·Jzz − Jxz²`.
  - Slope `h·(dDy/dx, dDy/dz) / max((Jxx, Jzz), 0.25)` (sharpens compressed crests), `.z = 0`, `.w = |slope|²` → RGBA16F with full mips.
  - **Jacobian partials `(Jxx−1, Jzz−1, Jxz)`** → their own RGBA16F with full mips. Store partials, not J: the Jacobian of a sum is not the sum of Jacobians.

### 8.3 `mipmap.glsl`
2×2 box downsample, one dispatch per level, for the displacement, slope and partials chains (8×8 threads). The fft pass writes mip 0 through a single-mip view, as for the other chains.

### 8.4 C# side
- `OceanPipelines` loads and frees all compute shaders and pipelines (pipelines before shaders).
- `OceanCascade`: textures (usage `StorageBit | SamplingBit | CanCopyFromBit`, `CanCopyToBit` where cleared, `CanUpdateBit` on h0), mip views, uniform sets created once, push-constant byte arrays preallocated and written through `Ocean.Core.Gpu.PushConstantLayout` (offsets by field name; no `BitConverter.GetBytes` per tick). Free order: `Texture2Drd.TextureRdRid = default` → uniform sets → mip views → textures. A few `internal` members (texture RIDs, h0 upload) for `dev/GpuChecks`.
- `Ocean._ExitTree` frees foam fields, cascades, pipelines.

### 8.5 Scheduling (`Ocean.Core.Scheduling.TickScheduler`)
- `SimulationRate` 60 Hz. Cascade i's first tick at `i · interval / 3`; the two foam fields share their own phase slot after the last cascade. At high fps a frame records at most one cascade or the fields.
- Per frame, a client is due if `clock ≥ NextTick` (or its spectrum is dirty). On tick: `dt = clock − LastTick`, `LastTick = clock`, `NextTick = max(NextTick + interval, clock)`. That keeps the phase, and when frames are slower than the tick rate everything due ticks every frame.
- Cascade time advances by `dt · TimeScale`; foam fields get the real `dt`. Everything is frame-rate independent.
- All due work for a frame goes into **one** compute list, opened only if something is due.

**Gate M1:** TUnit spectrum/FFT/scheduler/push-constant tests pass; `dev` GPU checks "spectrum vs CPU" and "single wave" pass; waves visibly move.

---

## 9. Foam (milestone M3)

### 9.1 What doesn't work (don't repeat)
| Attempt | Result |
|---|---|
| Per-cascade foam history, summed and thresholded | Swell cascade's large texels → blobs; ripple cascade → specks |
| Ridged noise × foam | Leopard spots |
| Domain-warped, wind-streaked lace texture | Marbled "melted plastic", ignores the wave shape |
| Cellular/Worley texture eroding foam | Repetition; wire mesh / spider webs where foam thins |
| Ripple slopes thresholded into stripes | Streaks from 40 m, shredded paper at 5 m |
| Density foam broken up by ripple slopes | Heavy flicker (the ripple cascade changes fastest) |
| Foam field updated with `max(old, injected)` | The per-tick selection jitters → flicker |
| Photo foam texture unblurred, single tiling | "Too real", repetition everywhere |
| Camera-local field plus per-cascade foam outside it | A foam-free square visible from above |
| Bias 0.66 with InjectRate 5 | ~60 % coverage: flat white sheets |
| Marble textures as foam detail | Tried; the foam photo and soap-foam opacity map read far better |

Rules: foam shape comes from a **camera-local field fed by the combined Jacobian** at the field's own resolution; never binarise; never drive visible detail with fast-changing fields; accumulate at a rate. **The detail texture decides most of the look**; hex-tile it and sample it softer than the screen needs.

**The model this follows: Sea of Thieves** ("The Technical Art of Sea of Thieves", SIGGRAPH 2018 talk). In the speaker's words, they "progressively blur the render targets", so "where you have the peaks of the waves, you still get these kind of sharp crests, but then that foam gradually dissipates over the progressive blur frame by frame". The foam is then blended with **two** artist textures: "a high-frequency foam texture at the crest of the wave, and then we blend to a lower-frequency texture as it blends out", and finally with the water colour. Here that is: the freshness layer stays sharp (crest), the density layer is blurred every tick (section 9.2, `DiffuseAlong/Across` 1.5 / 0.3 m²/s), and the surface blends the crest texture to a blurred version of itself by foam age (section 9.3). Weak blur and no age blend give old foam the crest's bubbly detail, so trails read as hard white bars or lace instead of soft dissipating veils.

What makes the foam body read as **one coherent thing** (all three were tried the other way and rejected by eye):
- **The tail is the crest texture at the same place, only blurred** (+2 mips), not a different low-frequency texture. A separate noise texture made blobs that developed beside the crest instead of out of it.
- **Age starts late** (`foam_age_start` 0.6): the crest keeps its sharp texture long enough to develop before it starts to dissolve.
- **The tail is fainter than the crest** (`foam_tail_opacity` 0.6) and **the blur is moderate**: strong blur plus full-opacity tails gave too much foam all at once.

### 9.2 Fields (`foam_field.glsl`, `OceanFoamField.cs`)
Two camera-centred fields: **near** 1024² over 256 m (25 cm texels), **far** 1024² over 2048 m (2 m texels). Same shader and settings; each is snapped to whole texels and ping-pongs between two **RG16F** textures:
- **R = density**: accumulated foam, slow decay, spreads: the trail layer. This is Sea of Thieves' "progressive blur": with `DiffuseAlong` 1.5 and `DiffuseAcross` 0.3 m²/s a trail widens over its life, which is what makes it dissipate softly. Foam targets (9.6) pass at these values (calm: caps 1.4 %, trails 11.3 %).
- **G = freshness**: same injection, fast decay, barely spreads: the whitecap layer. Always `G ≤ R`.

Per texel, per tick (8×8 threads):
1. World xz = origin + texel centre. Sum each cascade's partials sampled there × weight, each at `lod = max(0, log2(fieldTexel / (patch / N)))`. `J = (1+ΣJxx')(1+ΣJzz') − (ΣJxz)²`.
2. `injected = clamp((Bias − J) · Gain, 0, 1)`.
3. History is bound as a **sampler** (linear, `ClampToBorder`, transparent black): `hist(p, off) = texture(history, (p + 0.5 + shift + off) / size)`. `shift` is the integer camera move in texels; the first tick shifts everything out (`shift = size`).
4. Anisotropic diffusion along the streak direction `d` (the swell's wind direction + `StreakAngleOffset`) and across it `e`:
   ```glsl
   vec2 c  = hist(p, vec2(0.0));
   vec2 la = hist(p, streak) + hist(p, -streak) - 2.0 * c;   // streak = d * step (texels)
   vec2 lc = hist(p, across) + hist(p, -across) - 2.0 * c;   // across = e (one texel)
   vec2 spread = max(c + vec2(k_along, 0.2 * k_along) * la + vec2(k_across, 0.2 * k_across) * lc, 0.0);
   ```
   On the CPU: `k = D · dt / spacing²` clamped to **0.24** (explicit diffusion stability). Along the streak, widen the tap spacing instead of clamping: `step = max(1, sqrt(D_along · dt / 0.24) / texel)` texels, `k_along = D_along · dt / (step · texel)²`.
5. Update:
   ```glsl
   float density = min(spread.x * exp(-decay * dt) + injected * inject_rate * dt, 1.0);
   float fresh   = min(spread.y * exp(-fresh_decay * dt) + injected * inject_rate * 2.0 * dt, density);
   ```
Why streaks appear: in grid space the foam stays with the water while the crest that injects it moves on, leaving a trail; spreading mostly along the wave travel direction stretches trails instead of growing blobs. Measured anisotropy tops out around 1.6 (crest lines run across the wind), so most of the streaky look comes from the stretched detail texture.

Push block: origin vec2, texel_size, dt, shift ivec2, size, bias, gain, inject_rate, decay, fresh_decay, streak vec2, k_along, k_across, then vec4 patch_size, vec4 weight, vec4 lod; pad to a multiple of 16.

### 9.3 Surface composite (`foam_composite.gdshaderinc`)
**Lookup:** `field_uv = (v_grid − origin) / extent` for both fields, with `v_grid` the **undisplaced** xz (foam rides with the water). Near weight `1 − smoothstep(0.5, 0.95, max|2uv − 1|)`; far weight fades over 0.8–1.0 of its extent; branch so most pixels read only the near field. Multiply by `exp(−dist / foam_fade_range)`. Shore foam `1 − smoothstep(0, shore_foam_distance, shore_depth)` adds to density and half of it to freshness.

**Detail texture:** greyscale, red channel, mipmapped, set from `OceanConfig.FoamTexture`: `crest` (default), `ambientcg_001`, or `procedural`. `FoamDetailTexture.Generate()` is the fallback, used whenever a file is missing: 512², tileable, domain-warped value-noise fbm (warp: two 3-octave fbms at 3 base cells, ±0.09 UV; clouds: 5 octaves from 4 cells, amplitude ×0.55 per octave), plus soft round bubble spots at 14 and 31 cells per tile (max radius 0.45 / 0.4 cells, smoothstep falloff, the second ×0.8). Bubbles only add where the clouds are bright (`clouds + bubbles · clouds · 0.35`); stretch the result to 0..1; R8 with mipmaps. All three textures use the same look values (tile 14, blur 1.2, softness 0.3, dense shade 0.45). **Any detail texture must match Crest's brightness distribution** (mean 0.41, 10th–90th percentile 0.06–0.73) before use, because the erosion thresholds assume it. Without that, replacements came out as flat white sheets or nearly invisible foam. The ambientCG processing in 5.4 does exactly this, and so must any future texture.

**Tail texture (aged foam):** the same detail texture bound a second time as `foam_detail_soft` (plain `filter_linear_mipmap`), sampled at the same uv `foam_tail_blur` (2) mips softer. Tried and rejected: a separate procedural low-frequency texture (soft warped clouds, any tile from 14 to 40 m) made blobs that developed apart from the crest; ambientCG Foam001 as the tail stays bubbly and mottled.

**Hex tiling** (Mikkelsen 2022, "Practical Real-Time Hex-Tiling"): skew the UV into a triangle grid (`2√3 · density` cells per tile), take the three hex vertices around the pixel, sample once per vertex with a random rotation (`(rand·2 − 1) · foam_hex_rotation`, so 180° = any) about the cell centre plus a random offset, and blend by barycentric weights raised to `foam_hex_contrast` and renormalised. Use `textureGrad` with the screen gradients rotated per copy and × `2^blur`; compute the gradients **before** any branch.

- **Sample the sharp lace layer with `filter_linear_mipmap_anisotropic`.** The texture is stretched along the streak and seen at grazing angles; with isotropic filtering the GPU picks the mip from the long screen axis and the foam smears into long streaks instead of reading as wispy lace. Anisotropic filtering costs ~0.05 ms at 1080p across all foam taps, so use it only on `foam_detail` (crest texture: `d1` and the two relief taps); the blurred tail reads the same texture through `foam_detail_soft` with plain `filter_linear_mipmap`. (Measured: anisotropic on every foam tap cost 0.05 ms; on the sharp layer only, nothing measurable.)
- Don't add Mikkelsen's luminance-weighted blend here: it was tried and made the foam harder and less wispy. Don't halve the rotation range either.

- **The per-vertex random rotation/offset must come from an integer hash** (`hash(uint(v.x) + hash(uint(v.y)))`, two floats from `h >> 8` and `hash(h) >> 8`). A vertex is reached from three code paths (it is slot 1, 2 or 3 depending on the triangle); with a `sin` hash each path rounds differently, the same vertex gets a different copy on each side of an edge, and the foam shows **hard straight seams with sheared patches** between them. This shipped once and was only caught by a user at mid distance.
- Verify the tiling in isolation (section 14, layer debug): render `foam_hex(...)` alone as emission. It must be continuous; then set rotation and offset to 0 and it must equal plain `textureGrad` sampling exactly.

**Layers:**
```glsl
float body = smoothstep(foam_trail_low, foam_trail_high, density + shore);
float cap  = smoothstep(foam_cap_low,   foam_cap_high,   fresh + 0.5 * shore);
vec2  uv   = streak_uv(v_grid, streak_angle);   // rotate to the streak direction, divide the along axis by foam_detail_stretch, all by foam_detail_tile
float lod  = smoothstep(foam_lod_start, foam_lod_end, dist);
float foam = body * foam_far_opacity;           // far: soft coverage only
if (body + cap > 0.001 && lod < 0.999) {
    float age      = smoothstep(foam_age_start, 1.0, clamp(1.0 - fresh / max(density, 1e-3), 0.0, 1.0));  // 0 crest, 1 old
    float d1       = hex(foam_detail, uv, foam_detail_blur);                          // sharp crest texture
    float d_tail   = hex(foam_detail_soft, uv, foam_detail_blur + foam_tail_blur);    // same texture, same place, blurred
    float tail_mix = mix(foam_detail_mix, 1.0, age);                                  // crest: 35 % blurred (breaks uniformity)
    float softness = mix(foam_softness, foam_age_softness, age);                      // erosion softens with age
    float detail   = mix(d1, d_tail, tail_mix);
    float lace  = foam_lace(max(body, cap), detail, softness);   // caps erode too, at their own higher density
    float white = lace * mix(foam_trail_opacity, 1.0, cap) * mix(1.0, foam_tail_opacity, age);
    float veil  = body * foam_veil * clamp(d_tail * 1.6, 0.0, 1.0);                   // reuses d_tail: no extra taps
    foam = mix(max(white, veil), foam, lod);
    // relief (below): same two crest taps, mixed with d_tail by tail_mix, × (1 − age): old foam is smooth
}

float foam_lace(float body, float detail, float softness) {   // Crest-style erosion: thin foam keeps only the brightest parts
    float t = mix(1.0 + softness, -softness, body * foam_max_cover);   // max_cover < 1 keeps holes in saturated foam
    return smoothstep(t - softness, t + softness, detail) * mix(foam_dense_shade, 1.0, detail);
}
```
`foam_max_cover` exists because a big crest under the camera otherwise turns into one flat opaque sheet. Tap count: 3 (crest) + 3 (tail) + 6 (relief, near only), the same as before the age blend: the tail taps replace the old coarse octave and veil taps. Measured 0.76 ms at 512² (hero pose), no cost over the single-texture version.

**Milk** (turbid water around foam): `smoothstep(0, milk_range, density + shore) · milk_strength` tints the body towards `milk_color`.

**Foam relief:** within the bicubic range, two extra lace samples at `uv + (ε, 0)` and `uv + (0, ε)` (ε = 0.01) give the lace gradient in texture space; map it back to grid space through the streak rotation, stretch and tile, and apply it as `normal = normalize(normal − (relief.x, 0, relief.y) · coverage)` with `relief = gradient · foam_relief · bicubic_weight · (1 − age)`, after flattening (aged foam is smooth). No extra gating: blue streaks along lace edges and foam that looks embossed with its own shadows are symptoms of the lighting deviating from section 9.4 (missing bubble scatter, foam-only diffuse, sharp water roughness under foam), not of the relief. Fix the lighting, don't hide the relief.

### 9.4 Foam lighting
- Albedo `foam_color` (0.82, 0.84, 0.84), × `mix(0.85, 1, saturate(v_height / 1.5 + 0.5))` so foam darkens in troughs.
- `ROUGHNESS → foam_roughness`; `SPECULAR → 0` under foam (smoothstep over 0..0.4 of coverage); water GGX specular × (1 − foam).
- Normal: flatten the water normal by `foam · foam_flatten`, then add the relief.
- `light()`, exactly this (it is the verified recipe; every deviation tried so far made foam darker, bluer or embossed):
  - `alpha = ROUGHNESS²` (the blended water/foam roughness from `fragment()`, not the water's own alpha); GGX specular with Fresnel from `n·v`, × `(1 − foam)`.
  - `DIFFUSE_LIGHT += radiance · mix(n·l, wrapped, foam)` with `wrapped = saturate((n·l + w)/(1 + w))`, `w = foam_wrap`: Lambert for water, wrapped Lambert for foam, one line for the whole surface (the engine multiplies by `ALBEDO`).
  - `bubbles = max(milk, foam)` (a varying), and `SPECULAR_LIGHT += radiance · ((body · (1 − foam) + sss_bubbles · bubbles · bubble_color) · (1 − F) + sss_color · (1 − white) · foam)`. The bubble term lights milky water and foam alike; it is what keeps the veil between the lace from reading as a dark shadow. Don't restrict it to fresh foam or multiply it by `(1 − foam)`.
- `fragment()`: `transmittance *= (1 − milk)`; `ROUGHNESS = mix(sqrt(alpha), foam_roughness, foam)`; `SPECULAR = 0.354 · (1 − smoothstep(0, 0.4, foam))`.

### 9.5 Foam uniform defaults
| Uniform | Default | Uniform | Default |
|---|---|---|---|
| foam_trail_low / high | 0.03 / 0.45 | foam_cap_low / high | 0.1 / 0.45 |
| foam_detail_tile | 14 m | foam_detail_stretch | 2.2 |
| foam_detail_blur | 1.2 | foam_detail_mix | 0.35 |
| foam_age_start | 0.6 | foam_age_softness | 0.35 |
| foam_tail_blur | 2 mips | foam_tail_opacity | 0.6 |
| foam_softness | 0.3 | foam_max_cover | 0.6 |
| foam_dense_shade | 0.45 | foam_trail_opacity | 0.7 |
| foam_veil | 0.25 | foam_hex_rotation / density / contrast | 180° / 1 / 3 |
| foam_far_opacity | 0.35 | foam_lod_start / end | 120 / 220 m |
| foam_fade_range | 400 m | foam_roughness / flatten | 0.8 / 0.6 |
| foam_relief | 0.3 | foam_wrap | 0.35 |
| milk_color / strength / range | (0.5,0.55,0.56) / 0.15 / 1.5 | shore_foam_distance / shore_fade_distance | 0.8 / 0.3 |

### 9.6 Measured targets (`dev/FoamStats`)
After 8 s at the calm preset from the hero pose, read back the near field once; over its central 128 m × 128 m:
- `fresh > 0.3` (whitecaps): **0.3–3 %** (previous build: 1.3 %).
- `density > 0.15` (trails): **3–15 %** (previous build: 14.8 %).
- 60 Hz vs 120 Hz tick rate: density coverage within **±15 %** relative (previous build: 4 %).
- Streak anisotropy (0.5-crossing autocorrelation length of density along `d` vs across `e`): **report only**, not a gate. The last build measured 1.05 with these parameters (the trails visibly smear along `d`, but crest lines across the wind are as long); 10× `DiffuseAlong`, the trail layer alone and the integral length scale all stayed between 0.8 and 1.2. `--selftest` prints it as WARN.
- Measure from a restarted simulation (cascade time 0, foam fields emptied) so 60 Hz and 120 Hz see the same sea state; otherwise the comparison measures different waves.
These are a safety net; the captures decide.

**Gate M3:** foam targets met; captures show small bright caps, streaky lacy trails, mostly clear water, no square from above, no tiling; `foam_hex` alone is continuous (section 9.3 check); breeze `mid_close` and `close` at 1:1 show no seams or straight edges in the foam; bursts show no flicker; FPS still met.

---

## 10. Surface shader (milestone M2; foam layered on in M3)

`render_mode world_vertex_coords, blend_mix, depth_draw_always, cull_back, shadows_disabled`. Includes: `sampling` (bicubic, cascade sampling, slope variance, view position), `lighting` (rough Fresnel, Smith, GGX, Atlas scatter, horizon clamp), `foam_composite`. Group the uniforms with `group_uniforms` (geometry, colour, surface, distance, scattering, foam): the UI's surface sections come from these groups.

- **Mesh** (`Ocean.Core.Mesh.RingLayout` → `OceanMesh`): a `(2R+1)²` grid, R = 256 rings. Chebyshev ring n has half-size `n · 0.25 m` for the first 32 rings, then grows geometrically to 4000 m at ring 256 (~263k vertices). Indices sorted by ring so near water draws first (early-z with `depth_draw_always`). Clockwise seen from above. Huge `CustomAabb`; `cast_shadow` off. The mesh snaps to the camera's xz in 0.25 m steps.
- **Vertex:** sum the three displacement samples, each × `1 − smoothstep(0.25·d, d, dist)` with d = (3000, 800, 200) m. `v_grid = VERTEX.xz` before displacement; `v_height = d.y`.
- **Displacement LOD (no swimming far waves):** each vertex samples the displacement at `lod = max(0, log2(spacing / max(texel, 0.25 m)) + displacement_lod_bias)`, with its own spacing `spacing = max(0.25, chebyshev_offset · RingLayout.Growth())` (growth ≈ 0.0281: the outer rings are 28 m apart at 1 km). Without it the geometric rings point-sample waves far shorter than their spacing; since the whole mesh slides with the camera in 0.25 m steps, those vertices land on different parts of the waves every step and the far sea turns "very wavy", reshaping as you move, with fake crests that get no foam (the foam field, built from properly filtered partials, knows they aren't there). The inner rings snap to exactly their own spacing, so they never swim; the `max(texel, 0.25 m)` keeps them at mip 0 and the near look unchanged.
- **Normals:** cascade slopes × `normal_swell / chop / ripples` (1 / 0.8 / 0.45). Bicubic (B-spline, 4 bilinear taps) within `bicubic_distance` 60 m, blended out over 70–100 % of it; trilinear-anisotropic beyond.
- **Distance calming:** normal strength × `mix(normal_fade_floor 0.12, 1, exp(−d / normal_fade_range 220))`; chop fades out by 600 m, ripples by 120 m. Don't turn the removed slope into roughness: the rough-surface Fresnel drops and the far field goes dark brown.
- **Roughness:** LEAN-style variance `Σ strength² · max(slope.w − |slope.xy|², 0)` with the un-calmed strengths, **only beyond the bicubic range** (up close it makes dark blotches); `α = sqrt(roughness⁴ + variance)`, roughness 0.12, `variance_roughness` 1.
- **Horizon clamp** (no black facets): before writing `NORMAL`, if `r = reflect(−v, n)` has `r.y < horizon_min` (0.03), return the closed-form half vector `normalize(v + (r̂.x·c, horizon_min, r̂.z·c))` with `r̂` the horizontal direction of `r` and `c = sqrt(1 − horizon_min²)`. Continuous at the threshold; no iterative search (a 6-step bisection was tried: slower, same look).
- **Depth and refraction:** `straight` = view position of the depth sample at `SCREEN_UV`; `shore_depth = distance(straight, VERTEX)`; refraction offset in **screen UV**: `SCREEN_UV + view_normal.xy · refraction_strength (0.04) · saturate(shore_depth)`; reject the sample if it lies in front of the surface; thickness = distance to the refracted sample; shore depth for shore foam and `ALPHA = smoothstep(0, shore_fade_distance, shore_depth)`. **Shore depth is the distance along the view ray** from the surface to the opaque point behind it (`distance(behind_view_pos, VERTEX)`), never the vertical drop `surface.y − behind.y`: with sky behind, the "behind" point is on the far plane, which for a crest seen against the sky lies *above* the water, so a vertical depth clamps to 0 and every crest above the horizon turns transparent (the water looks cut flat at the horizon line).
- **Colour:** scatter = mix(shallow (0.12,0.2,0.24), deep (0.03,0.05,0.07), 1 − exp(−thickness / 6)) × body_tint (1,1,1); mix towards height tint (0.1,0.22,0.25) by `smoothstep(0, 2.5, v_height) · 0.1`; milk; transmittance `pow(water_transmittance (0.35,0.75,0.8), thickness / absorption_distance 4)`. `ALBEDO = scatter(1−T)(1−F)`, `EMISSION = refracted · T · (1−F)`, `SPECULAR = 0.354` (f0 = 0.02).
- **`light()`:** GGX with height-correlated Smith `1/(1+Λv+Λl)` and the Atlas rough-surface Fresnel `f0 + (1−f0)·(1−NdotV)^(5·e^(−2.69α)) / (1 + 22.7·α^1.5)`, × radiance/π × `specular_strength` (1) × (1 − foam) into `SPECULAR_LIGHT`. Atlas scatter (GDC 2019): k1 crest towards the sun `sss_crest 2 · crest · pow(saturate(dot(L, −V)), 4) · pow(saturate(0.5 − 0.5·dot(L, N)), 3)` with crest = `max(v_height, 0) · 0.6`; k2 view-facing `sss_view 0.4 · pow(saturate(dot(V, N)), 2)`; k3 sun-facing `sss_sun 0.1 · NdotL`; k4 bubbles `sss_bubbles 0.35 · bubbles · bubble_color (0.6,0.66,0.66)`; (k1 + k2)/(1+Λl) + k3, all × `sss_color (0.08,0.22,0.24)` × radiance/π × (1−F) into `SPECULAR_LIGHT`. Keep these modest or crests go neon green.
- `SurfaceUniforms.cs` holds every uniform name C# sets; C# uses only these constants.

**Gate M2:** waves, glint, sky reflection and colour look right in the captures without foam; `low_away` has no black facets; FPS targets met.

---

## 11. Demo scene (milestone M2/M4)

- **`demo/main.tscn`** holds WorldEnvironment, Sun (`DirectionalLight3D`, shadows off), Camera, Rock, an `ocean.tscn` instance, `DemoRoot`, and two `ParameterPanel` instances (Ocean: docked left, F1; Environment: bottom-right, F2).
- **Environment:** sky background; ambient and reflections from sky; ACES tonemap (`tonemap_mode` 3), `tonemap_white` 6; depth fog with `fog_light_color` (0.9, 0.82, 0.76), `fog_density` 0.0005 (0.0008 washes out high views), `fog_aerial_perspective` 1, `fog_sky_affect` 0; glow on with `glow_hdr_threshold` 1.2 (other glow settings at engine defaults). No SSR, SSAO, SDFGI, volumetric fog or shadows.
- **Skies** (`demo/presets/skies.json`, loaded by `SkyRig`; `Sky.RadianceSize = Size256`, sky energy 1; panoramas yawed per section 4 so the sun is straight ahead of the default camera):

  | Id | File | Sun azimuth in image | Elevation | Sun colour | Sun energy |
  |---|---|---|---|---|---|
  | `evening_road` (**start**) | `evening_road_01_puresky.png` | 44.6 | 15.4 | (1, 0.9, 0.8) | 1.6 |
  | `procedural_sunset` | none: ProceduralSkyMaterial top (0.22,0.29,0.48), horizon (0.93,0.62,0.48), curve 0.12, ground bottom (0.36,0.3,0.36), ground horizon (0.93,0.62,0.48), sun_angle_max 20, sun_curve 0.08; yaw 0 | 0 | 10 | (1, 0.78, 0.6) | 1.6 |
  | `pastel_sunset` | `sbs_cloudy_sky_07.png` | 89.5 | 21.6 | (1, 0.85, 0.75) | 1.5 |
  | `amber_sunset` | `sbs_cloudy_sky_03.png` | 89.9 | 9.2 | (1, 0.8, 0.62) | 1.5 |
  | `violet_sunset` | `sbs_cloudy_sky_17.png` | 89.8 | 14.2 | (1, 0.75, 0.6) | 1.5 |

  All five rows are verified by capture. Automatic measurement from the image (brightest pixel, or warmth × brightness) is unreliable on these tonemapped sunsets: it lands on haze below the disc and was off by up to 13° of elevation, so don't build it. **Verify by capture instead**: camera at `0,15,0,0,0` (level, yaw 0), find the painted disc's pixel (x, y), then azimuth error = `atan((x − 960)/960 · tan(hfov/2))` (hfov/2 from vertical fov 70° at 16:9: `tan = 0.7 · 16/9`), elevation = `atan((540 − y)/540 · tan(35°))`. Add the azimuth error to the table value; the glint must sit under the disc. Keep the procedural sky's ground non-black.
- **Rock** (`demo/props/rock.tscn`): `SphereMesh` radius 40, height 30, 96 × 32 segments, at (120, −8, −160). `rock.gdshader`: vertex displacement along the normal by 3-octave value-noise fbm (`noise_scale` 0.07, `amplitude` 9); heights above `flat_top` 13.5 compressed ×0.25 into a flattish top; albedo (0.22, 0.2, 0.18) with lighter strata (0.34, 0.31, 0.27); a darker wet band within 1.5 m of y = 0; roughness 0.9; faceted shading (`facets` 0.7: blend towards flat face normals from screen derivatives). A composition anchor and a shore-foam test, not terrain.
- **`FreeFlyCamera`:** WASD, Q/E down/up, hold RMB to look (start it in `_UnhandledInput` so UI clicks never start it, release in `_Input`), Shift ×5, wheel ×1.2 speed. Default pose = `overview` (0, 52, −40, pitch −33°, yaw −24°: rock and glint in view, breeze crests in front; mind the row-major basis), `fov` 70, `far` 8000.

## 12. Parameters and the generic UI (milestone M4) **(new)**

Systems describe their state; one generic presenter shows whatever it is given. The UI never knows what an ocean is.

**Model (`ocean/core/Parameters/`, pure C#):**
- `Parameter`: `Id`, `Label`, `Group`, `Order`, `Kind`, plus kind-specific metadata, a getter/setter pair and an event `Changed`. The parameter is the **single source of truth**: rows never cache values. Code changes, preset loads and UI edits all raise `Changed`.
- `ParameterKind` is **closed**: `Float` (min, max, step, exp), `Int`, `Bool`, `Enum` (options), `Color`, `Action` (a button), `Readout` (read-only live value, refreshed a few times per second). Don't add kinds.
- `ApplyMode`: `Live` (every change) or `OnRelease` (only when a drag ends). Spectrum fields use `OnRelease`, since each change regenerates a spectrum.
- `ParameterSet`: `Title` plus ordered parameters. Nothing else: no registry, no plugin system.

**What the ocean exposes (`Ocean.Parameters`):** "Cascade 0/1/2" (the `CascadeConfig` fields), "Foam field" (`FoamConfig` fields plus `FoamTexture` as an Enum), "Surface" (built at runtime from `GetShaderUniformList(getGroups: true)`: hinted floats → Float, `source_color` → Color, `hint_enum` → Enum, grouped by `group_uniforms`), and "Ocean" (Resolution as an Enum, plus Readouts: tick rate, cascades ticked per second, foam coverage estimate if cheap; otherwise skip it). Setters write the live config and call the same paths as `Apply`.

**Presenter (`demo/ui/ParameterPanel`):**
- A panel scene (title, scroll container, VBox) styled by `demo_theme.tres`, and one row scene per kind in `demo/ui/rows/`. The editor owns how rows look; code never builds or styles controls beyond instancing a row scene.
- `ParameterPanel.Show(IEnumerable<ParameterSet>)`: one foldable section per set, sub-sections per group, one row per parameter, bound both ways. Rows unsubscribe on exit.
- `MouseFilter = Stop` so dragging a slider never moves the camera. Toggle key per panel (F1, F2). **Open when the demo starts** (new users won't find hidden panels); the title says which key hides it. Only in debug builds.
- **`DemoRoot`** is the only place that knows both sides: it gets the ocean via an exported `NodePath`, builds the panels' sets, and adds the demo's own sets:
  - "Presets" set: an Enum of `demo/presets/ocean_*.json`, Load (Action), and Save to `user://ocean_custom.json` (Action);
  - "Environment" set (F2 panel): sky Enum, Sky Yaw, Sun Elevation, Sky Energy, Sun Energy, glow on/off.

## 13. Dev tooling (milestone M1 onwards) **(new)**

Everything here lives in `dev/` and runs from `dev/dev_main.tscn`, which instances `demo/main.tscn` and adds `DevHarness`. Command: `<godot> --path . res://dev/dev_main.tscn -- <args>`. No external scripts, no OS window capture, no Movie Maker.

| Arg | Does |
|---|---|
| `--cam=x,y,z,pitch,yaw` | camera pose in degrees |
| `--sky=<id>` / `--preset=<json name>` | start sky / ocean preset |
| `--set=<param id>=<value>` | sets any parameter by id (e.g. `--set=foam.bias=0.62`, `--set=surface.foam_veil=0.4`) |
| `--capture=<pose,…>` | for each named pose (section 14): place the camera, **restart the simulation** (so the same pose gives the same waves in every run, which A/B comparisons need), let it settle 3 s (`--settle=<s>`), save `.scratch/captures/<tag>_<pose>.png` from the viewport, then a 2-column contact sheet (`Image.BlitRect`), then quit; `--tag=` names the run; pose `current` keeps the `--cam` pose |
| `--panels` | shows the parameter panels unfolded, for UI captures |
| `--burst=<n>` | at the current pose, save n consecutive frames (crop 640×360 from the centre) and a strip image; the flicker check, takes about a second |
| `--bench=<seconds>` | average and 1 %-low frame time after a 3 s warm-up at the hero pose (or `--cam`); prints one line, quits |
| `--selftest` | runs `GpuChecks` (section 15.2), prints PASS/FAIL per check, exits 0/1 |
| `--foam-stats` | prints the 9.6 numbers after 8 s on the calm preset (or `--preset`), quits |
| `--prepare-assets` | the asset processing from 5.4 |

Look at the full-resolution captures, not only the contact sheet: a downscaled sheet can invent speckle — and it hides seams. Crop regions at 1:1 (a small script under `.scratch/` is fine).

`dotnet build -p:Ship=true` strips `dev/` from the assembly; rebuild normally before the next dev run, or `dev_main.tscn` cannot instance `DevHarness` and Godot never quits.

## 14. Visual verification

| Pose | (x,y,z,pitch,yaw) | Checks |
|---|---|---|
| hero | 0,12,0,−14,0 | the reference composition |
| high_sun | 0,150,0,−30,0 | glint, tiling, foam square |
| high_side | 0,150,0,−30,70 | tiling, colour |
| low_sun | 0,15,0,−10,0 | crests, scatter |
| mid_close | 0,40,0,−25,15 | foam shape |
| low_away | 0,15,0,−10,180 | **no black facets** |
| top_down | 0,250,0,−60,30 | nothing white from 250 m, no square |
| close | 0,4,0,−30,20 | lace, no wire mesh |
| close_down | 0,7,0,−62,40 | foam texture, relief |
| shore | 60,10,−110,−15,−50 | shore foam at the rock |
| overview | 0,52,−40,−33,−24 | the demo's default camera (breeze): crest foam, far waves calm and stable |

Yaw 0 faces the sun on every panorama preset. Bursts at `close_down` and `hero`: foam drifts with the water, caps age into trails, nothing pops between frames. Compare the hero capture against `docs/reference.webp`.

**Also capture under `ocean_breeze`**, at least `mid_close`, `close` and a low eye-level pose (`--cam=0,3,0,2,0 --capture=current`: crests must rise above the horizon, not be cut flat), and inspect 1:1 crops. Calm has so little foam that foam-texture defects (seams, visible repetition, sheared patches) stay invisible there; heavy foam at 30–100 m shows them at once.

**When something looks wrong, isolate the layer before changing anything.** On one deterministic frame, temporarily write each layer to `EMISSION` (zero `ALBEDO`, `SPECULAR` and the light-path varyings): the foam field (`foam_field_lookup`), the detail texture (`foam_hex` alone, then plain `textureGrad`), coverage/white, the normal. The layer where the artefact first appears is the cause. Change one thing at a time and diff against the same frame. Don't guess at causes from a single beauty shot: the seam bug above first looked like mesh undersampling, then like culled fold triangles, and was neither.

## 15. Tests

### 15.1 TUnit (`tests/Ocean.Tests`), the minimum set
- **Spectrum:** JONSWAP α and ωp against the formulas; Donelan-Banner integrates to 1 over [−π, π] (±1e-3) for several ω/ωp; wrapped index; `InitialSpectrum` (CPU port of spectrum.glsl) zeroes Nyquist and pairs `h0(k)` with `conj(h0(−k))`; no Gaussian hash collisions over 512² for one seed.
- **FFT:** `ReferenceFft` (CPU Stockham with fft.glsl's exact indexing) equals a naive inverse DFT for N = 8, 64, 512; real-field packing recovers two fields from one complex transform.
- **Scheduling:** at 1400 fps over 10 s each cascade ticks 600 ± 1 times and no frame records more than one cascade or the fields; at 30 fps everything ticks every frame; simulated time = elapsed × TimeScale.
- **Foam math:** shift and snapping, the `k` clamp and stencil widening, a CPU reference step (steady state ≈ `inject_rate/decay` capped at 1, `fresh ≤ density`, 60 vs 120 Hz within 5 %).
- **Push constants:** parse the `push_constant` block of every `.glsl` under `ocean/` (std430: scalars 4, vec2 8 aligned 8, vec4 16 aligned 16), assert sizes and offsets match the C# layouts field by field and are multiples of 16. This catches the most common silent bug.
- **Config:** JSON round-trip of `OceanConfig` (incl. colours and enums, invariant culture under `de-DE`); a file missing fields keeps defaults; the config diff reports exactly the changed cascade spectra.
- **Parameters:** setting a value raises `Changed` once and not for the same value; `OnRelease` parameters apply only on commit.

### 15.2 GPU checks (`dev/GpuChecks.cs`, via `--selftest`)
- GPU h0 at 512² equals `InitialSpectrum` at 64 random texels (relative 1e-3).
- A single uploaded mode, ticked to t = 0.37 s and read back, matches the analytic single wave (displacement 1e-3 m, slope 1e-3, J 2e-3; `Dx`'s sign sharpens crests; J minimum `1 − λka`).
- Mip chain of a constant input equals the constant.
- Foam targets (9.6).

## 16. Licences and release files

- `LICENSE`: MIT, `Copyright (c) 2026 <AUTHOR>`. Leave `<AUTHOR>` as a literal placeholder for the owner (the published repository carries the owner's name) to fill in.
- `README.md` credits the sources in section 20 (Tessendorf, Sea of Thieves, Atlas, Acerola, 2Retr0, Crest, Mikkelsen, JONSWAP/Horvath) and embeds the showcase video.
- `THIRD_PARTY_NOTICES.md`: Crest foam texture (URL + full MIT notice); ambientCG Foam001 (CC0, credit Lennart Demes); Evening Road 01 (Poly Haven, CC0, Sergej Majboroda and Jarod Guest); Cloudy Skyboxes (Screaming Brain Studios, CC0); Debug Menu (Hugo Locurcio and contributors, MIT).
- `README.md`: what it is; drop-in usage (copy `ocean/`, instance `ocean.tscn`, optional JSON config, `Apply`/`LoadConfig`/`Parameters`); the demo keys (F1 ocean panel, F2 environment panel, F3 Debug Menu; WASD, Q/E, hold RMB, Shift, wheel); config format with an example; how per-frame GPU work is scheduled; the foam pipeline in one paragraph; how to run the tests (`dotnet run --project tests/Ocean.Tests`, `<godot> --path . res://dev/dev_main.tscn -- --selftest`); measured FPS; asset credits. Keep `dev/` documented in its own short `dev/README.md`.

## 17. Milestones (commit after each gate: `M<n>: …`)

| M | Content | Gate |
|---|---|---|
| M0 | project, solution, TUnit, Godot path, assets, licences | 5.4 |
| M1 | core maths, spectrum, FFT, mipmaps, cascades, pipelines, scheduler, config model, `Ocean` API, dev harness with `--selftest` | 8.5 |
| M2 | mesh, surface shader without foam, demo scene (environment, skies, rock, camera), `--capture` | 10 |
| M3 | foam fields, composite, lighting, foam textures, `--burst`, `--foam-stats` | 9.6 |
| M4 | parameter model, ocean parameter sets, generic panel + row scenes, DemoRoot, JSON presets, F1/F2 | every UI control changes the ocean or scene live (spot-check with `--set` captures); preset save → load round-trips |
| M5 | taste pass against section 2 and `docs/reference.webp`; `--bench` at 512 and 1024. The colour, glow and foam defaults were tuned under a warmer painted sunset sky, so re-check them under `evening_road` (especially `low_away`, `shore` and foam brightness) and adjust where needed; then capture `hero` under every sky | captures, bursts, FPS |
| M6 | README, notices, cleanup (no dead code or unused uniforms), `dotnet build -p:Ship=true`, all tests and `--selftest` green | all green |

## 18. Done when

1. TUnit and `--selftest` pass; `dotnet build -p:Ship=true` passes and the demo runs without `dev/`.
2. The demo runs with no errors or warnings; deleting the ocean instance from `demo/main.tscn` leaves a working scene; `ocean/` copied into an empty Godot 4.7 C# project and instanced works with no extra setup (verify in `.scratch/dropin_test/`).
3. Ocean configuration is JSON-loadable; no tuning lives in `.tscn`/`.tres` files.
4. All three cascades contribute; foam forms bright caps that age into streaky lacy trails and fade; foam targets met; no flicker, tiling, squares or black facets.
5. FPS targets met under the measurement conditions.
6. Final report: what was built, FPS table, test counts, capture paths, deviations from this guide and why, open look questions.

## 19. Pitfalls already paid for

- Slope variance applied near the camera → dark blotches. Calmed slope turned into roughness → far field dark brown.
- Scatter too strong → neon green crests; scatter belongs in `SPECULAR_LIGHT`.
- Fog density 0.0008 → washed-out high views.
- `x + 256·y` hash → collisions at 512². The same seed for every cascade → correlated phases.
- `filter_linear_mipmap` on the screen texture → a blurred mip chain built every frame.
- A push-constant array not matching its GLSL block → silent garbage or a validation error.
- Panorama yaw from the naive formula → the glint 37° away from the painted sun.
- Movie Maker for flicker checks → minutes per clip; use `--burst`.
- Measuring FPS while another Godot game runs → halved numbers.
- Judging from one distance or from stills only.
- `sin`-based hash for the hex-tiling vertices → hard straight seams and sheared foam patches (section 9.3). Use the integer hash.
- Foam detail without anisotropic filtering → foam smeared into stretched streaks instead of wispy lace (section 9.3).
- Shore depth as a vertical drop → crests above the horizon turn transparent; use the distance along the view ray (section 10).
- Point-sampled displacement on the outer rings → the far sea swims and reshapes as the camera moves, with foam-less fake crests. Displacement mips + LOD by vertex spacing (section 10).
- `foam_fade_range` 220 m → crests beyond ~300 m showed no foam at all; 400 m.
- Lighting that deviates from section 9.4 (bubble scatter only on fresh foam, foam-only diffuse, water roughness under foam) → a dark "shadow" in the veil, blue streaks along lace edges, embossed foam. Gating the relief hid the streaks but not the cause.
- Anisotropic filtering on every foam tap → +0.05 ms; only the sharp layer needs it (section 9.3).
- Every GPU cost added has to be measured (3 bench runs each way) and justified; "the look needs it" is checked by an A/B capture of the same frame.
- "Improving" a verified shader recipe from memory of a paper (luminance blend, halved rotation) → a look regression. Port the recipe as written; change one thing at a time against same-frame captures.
- Foam diffuse weighted by coverage on top of the coverage-blended `ALBEDO` → coverage² lighting, grey-blue foam. Use the one-line `mix(n·l, wrapped, foam)` of section 9.4.
- Judging foam only under the calm preset → texture defects stay hidden; check breeze at mid distance too.
- Sky sun positions from automatic image measurement → up to 13° off; verify every sky by capture.
- Captures at different sim times compared as A/B → the "difference" is just different waves; captures restart the sim.
- A dev run right after a `-p:Ship=true` build → `DevHarness` missing, Godot hangs instead of quitting.

## 20. References

The work this ocean is built on. Credit them in the README of any build made from this guide.

- **Jerry Tessendorf, "Simulating Ocean Water"** (SIGGRAPH course notes, 2001). This is the FFT ocean of section 8: statistical spectrum, time evolution, choppy displacement, and the Jacobian as the foam signal.
- **Sea of Thieves, "The Technical Art of Sea of Thieves"** (https://www.youtube.com/watch?v=y9BOz2dFZzs). The techniques and overall direction: the progressively blurred foam render target, the crest texture blending to a softer one with age, foam blended into the water colour, and the stylised, sky-driven look (sections 2, 9).
- **Atlas, "Wakes, Explosions and Lighting: Interactive Water Simulation in Atlas"** (GDC 2019, https://www.youtube.com/watch?v=Dqld965-Vv0). The subsurface scatter term (k1 crest, k2 view-facing, k3 sun-facing, k4 bubbles) and the rough-surface Fresnel fit (sections 9.4, 10).
- **Acerola, "I Tried Simulating The Entire Ocean"** (https://www.youtube.com/watch?v=yPfagLeUa7k). The multi-cascade FFT ocean approach and practical shading intuition.
- **2Retr0, GodotOceanWaves** (https://github.com/2Retr0/GodotOceanWaves). An FFT ocean in Godot with compute on the main `RenderingDevice`; a reference for structuring it in Godot (section 4).
- **Crest, Wave Harmonic** (https://github.com/wave-harmonic/crest). The default foam texture (MIT, section 5.4) and the lace-erosion idea (section 9.3).
- **Thomas Mikkelsen, "Practical Real-Time Hex-Tiling"** (2022). The foam texture tiling (section 9.3).
- **JONSWAP** (Hasselmann et al. 1973) with **Donelan-Banner** spreading, as collected for graphics in Christopher J. Horvath, "Empirical directional wave spectra for computer graphics" (2015). Section 8.1.
