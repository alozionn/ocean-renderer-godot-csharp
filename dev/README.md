# dev/ — development tooling

Not shipped: `dotnet build -p:Ship=true` excludes this folder, and nothing outside it references it.
`dev_main.tscn` instances `demo/main.tscn` and adds `DevHarness`, which reads the arguments after `--`:

```bash
<godot> --path . res://dev/dev_main.tscn -- --capture=hero,close --tag=look
```

`<godot>` is the Godot 4.7 mono console executable; its path is kept in the git-ignored `local.settings.json`
(`{"godot": "<path>"}`). Output goes to `.scratch/` (git-ignored).

| Argument | Does |
|---|---|
| `--cam=x,y,z,pitch,yaw` | camera pose (degrees) |
| `--sky=<id>` | start sky from `demo/presets/skies.json` |
| `--preset=<name or path>` | ocean preset (`calm`, `breeze`, or a `res://`/`user://` path) |
| `--set=<id>=<value>` | sets any parameter the demo shows, e.g. `--set=foam.bias=0.62`, `--set=surface.foam_veil=0.4`, `--set=env.sky=amber_sunset`, `--set=presets.save` (actions take no value); repeatable |
| `--capture=<pose,...>` | per pose: place the camera, settle 3 s (`--settle=<s>`), save `.scratch/captures/<tag>_<pose>.png`, then a contact sheet. `current` keeps the `--cam` pose |
| `--tag=<name>` | file name prefix |
| `--burst=<n>` | n consecutive frames (centre 640×360) and a strip: the flicker check |
| `--interval=<s>` | with `--burst`: one frame every s seconds (a time lapse) |
| `--boat=x,z[,heading]` | puts the demo boat there, stopped; with `--burst`, prints its heave, pitch and roll per frame against the sampler and the read-back GPU surface |
| `--no-float` | the boat ignores the water (bench A/B for the sampler) |
| `--bench=<s>` | average and 1 %-low frame time after a 3 s warm-up |
| `--selftest` | GPU checks (incl. the water sampler against read-back swell and chop) + foam targets, PASS/FAIL per check, exit code 0/1 |
| `--foam-stats` | foam field numbers at 60 Hz and 120 Hz from the hero pose |
| `--panels` | shows both parameter panels unfolded (for UI captures) |
| `--shore=x,z[;x,z...]` | prints what the shore shader reads at world points (depth, distance, open, shelter), bodies folded in; combine with any mode |
| `--hide=<name,...>` | hides demo nodes by name, e.g. `--hide=Island,WaterProps` (cost A/B) |
| `--no-bodies` | unregisters every water body: the props stay but get only the depth-buffer contact foam (A/B) |
| `--hide-panels` | hides both parameter panels (clean scene captures) |
| `--prepare-assets` | one-off asset processing from `.scratch/downloads/` (see the asset READMEs) |

Poses: hero, high_sun, high_side, low_sun, mid_close, low_away, top_down, close, close_down, shore, overview (the demo's default camera), coast, windward, lee_coast, beach, props, moored (`Captures.cs`). `--bench` measures at hero unless `--cam` is given; `--selftest` and `--foam-stats` measure the foam targets on the calm preset unless `--preset` is given (the demo starts on breeze).
Look at the full-resolution captures, not only the contact sheet.
