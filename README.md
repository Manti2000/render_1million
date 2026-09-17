# Million Objects

Demo project for the Budapest Game Dev Day 2026 talk **"How to render 1 million objects in Unity without a hitch"**.

![One million cubes in the whirlpool, step 5 (GPU indirect + compute)](Media/whirlpool_1m.jpg)

Five backends render the same animated cloud of cubes: a jittered cubic lattice pulled into a whirlpool, a swirl with an open eye and a travelling wave on top. Each one is a self-contained step with its own scene and scripts, so you can read them side by side and run them against each other. The only variable between steps is the rendering architecture: same mesh, same shader, same wave, same palette, same camera.

| Step | Folder | How objects are driven |
|---|---|---|
| 1 | `Assets/Steps/01_MonoBehaviour` | One GameObject and one `MonoBehaviour.Update` per object |
| 2 | `Assets/Steps/02_Manager` | One GameObject per object, one manager with a Burst `IJobParallelForTransform` |
| 3 | `Assets/Steps/03_Ecs` | Entities, Entities Graphics, an `ISystem` with an `IJobEntity` writing the render matrix directly |
| 4 | `Assets/Steps/04_Instanced` | No GameObjects; Burst job writes matrices, `Graphics.RenderMeshInstanced` per palette bucket |
| 5 | `Assets/Steps/05_Indirect` | Compute shader owns all state, one `Graphics.RenderMeshIndirect` call |

Steps 1 to 3 pay in ergonomics and ecosystem while per-object features stay cheap. From step 4 on nothing becomes impossible, but every per-object feature costs infrastructure, and behaviour moves into shaders.

## Requirements

Unity 6000.4, URP 17, Entities 6.4. Open the project and let the packages resolve. Builds are tested with IL2CPP on Windows and Linux (Steam Deck).

## Running it

Open `Assets/Common/Scenes/Bootstrap.unity` and press Play. A start menu lets you pick a step and an object count, or launch the automatic benchmark or the recording script. The step scenes are loaded additively on top of `Bootstrap`, which owns the camera, light, HUD and controls.

| Key | Action |
|---|---|
| `Esc` or the corner button | Back to the menu |
| `1` to `5` | Load a step |
| `+` / `-` | Step one rung up or down the recording ladder and respawn |
| `Up` / `Down` | Zoom in and out along the camera path |
| `Left` / `Right` | Orbit the camera |
| `R` | Respawn |
| `A` | Toggle the attractor sphere (pushes cubes, they spring back) |
| `Left click` | Recolour the cube under the cursor |
| `C` | Pause or resume the camera path |
| `T` | Start or stop the automatic count ramp: climbs the recording ladder from the count on screen upward, a few seconds per count, stopping on the first count that measures under 4 fps |
| `H` or the corner button | Cycle the HUD: off, framerate and object count, full |
| `Space` | Freeze the field |

## Benchmarking

Build a player and run it with `-benchmark`, or press one of the benchmark buttons in the start menu. It applies a fixed display state (vsync off, uncapped frame rate, render scale 1, a 1920-wide frame at the display's own aspect on PC, 1920x1200 on 16:10 laptops, or native 1280x800 on Steam Deck), walks every step, and writes one JSON report to a `Benchmarks` folder next to the executable, then quits.

For every step it runs two measurements:

- a fixed-count sweep at 1k, 10k, 100k and 1M objects, each measured for 8 seconds at the wide shot after a 2 second warm-up, and
- a search for the largest count that holds a *stable* 30 fps: average frame time at or under 33.3 ms **and** the 99th percentile frame (the slowest 1%) at or under 50 ms, so hitches count even when the average looks fine. It starts from the sweep results, doubles until a count fails (up to 16M) and bisects until the answer is within 10%.

Windows whose average drops under 2 fps are aborted. A full run takes about two minutes per step, roughly ten minutes for the ladder. The quick mode takes about four.

Statistics are gaming-benchmark style: average, 1% low and 0.1% low, stored as frame time in milliseconds with the raw samples kept. Draw calls are read every frame and stored as min, average and max; the count is a property of the technique, not the hardware, and in a player it is stable within a call or two.

```
-benchmark                                   full run
-benchmark-out <dir>                         output directory
-benchmark-backends mono,manager,ecs,instanced,indirect
-benchmark-quick                             short windows, smoke test
-benchmark-resolution 1280x800               override the per-device default
-record                                      recording script: HUD on, camera path, count ramp per step
```

Timings come from `FrameTimingManager` (GPU time is averaged over the frames where the driver reported it, and is unavailable on Mesa), draw calls and memory from `ProfilerRecorder` counters that exist in release players. Nothing is measured while recording video; those are separate runs.

## Charts

```
uv run Tools/plot_bench.py --out charts --png --labels "High-end laptop,Mid-range laptop,Steam Deck" blade.json strix.json deck.json
```

(`uv` installs matplotlib from the script header; `pip install matplotlib` and plain `python` work too.) One report per device, in the order the labels are given. Writes SVGs, plus PNGs with `--png`, into:

- `charts/summary`: objects at a stable 30 fps and average fps at one million (one bar per device per step), draw calls at one million on a log axis, and frame time against count (one line per step per device).
- `charts/steps/<step>/`: one row per device with average and 1% low fps at one million, the 30 fps count, and the step's frame time curve.

`--bare` drops titles, direction lines and footnotes so the slide can carry them, and scales the type up for projection. `--draw-calls-from <report>` names the report whose per-frame draw call counts feed the draw-calls chart; by default the first report that measured a step is used.

## Layout

```
Assets/Common     bootstrap scene, backend contract, shared field maths, shader, HUD, benchmark runner
Assets/Steps      one folder per step: scene + scripts + step-only assets
Tools             chart generator
Media             screenshots used by this README
```

The shared maths lives in `Assets/Common/Core/ObjectField.cs` and is mirrored line for line in `Assets/Steps/05_Indirect/ObjectField.hlsl`, which is what keeps the picture identical across steps.
