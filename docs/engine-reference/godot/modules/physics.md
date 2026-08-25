# Godot Physics — Quick Reference

Last verified: 2026-02-12 | Engine: Godot 4.6
**Partial re-verification 2026-08-25 against Godot 4.7.1** — see *Fixed-Timestep Settings* at
the end of this file. The Jolt/interpolation sections above remain 4.6-verified and were not
re-checked.

## What Changed Since ~4.3 (LLM Cutoff)

### 4.6 Changes
- **Jolt Physics is the DEFAULT 3D engine** for new projects
  - Existing projects keep their current physics engine setting
  - Better determinism, stability, and performance than GodotPhysics3D
  - Some HingeJoint3D properties (`damp`) only work with GodotPhysics3D
  - 2D physics UNCHANGED (still Godot Physics 2D)

### 4.5 Changes
- **3D physics interpolation rearchitected**: Moved from RenderingServer to SceneTree
  - User-facing API unchanged, but internal behavior may differ in edge cases

## Physics Engine Selection (4.6)

```
Project Settings → Physics → 3D → Physics Engine:
- Jolt Physics (DEFAULT for new projects)
- GodotPhysics3D (legacy, still available)
```

### Jolt vs GodotPhysics3D

| Feature | Jolt (default) | GodotPhysics3D |
|---------|---------------|----------------|
| Determinism | Better | Inconsistent |
| Stability | Better | Adequate |
| Performance | Better for complex scenes | Adequate |
| HingeJoint3D `damp` | NOT supported | Supported |
| Runtime warnings | Yes, for unsupported properties | No |
| Collision margins | May behave differently | Original behavior |

## Current API Patterns

### Basic Physics Setup (unchanged)
```gdscript
# CharacterBody3D movement — API unchanged across engines
extends CharacterBody3D

@export var speed: float = 5.0
@export var jump_velocity: float = 4.5

func _physics_process(delta: float) -> void:
    if not is_on_floor():
        velocity += get_gravity() * delta

    if Input.is_action_just_pressed("jump") and is_on_floor():
        velocity.y = jump_velocity

    var input_dir: Vector2 = Input.get_vector("left", "right", "forward", "back")
    var direction: Vector3 = (transform.basis * Vector3(input_dir.x, 0, input_dir.y)).normalized()
    velocity.x = direction.x * speed
    velocity.z = direction.z * speed

    move_and_slide()
```

### Raycasting (unchanged)
```gdscript
var space_state: PhysicsDirectSpaceState3D = get_world_3d().direct_space_state
var query := PhysicsRayQueryParameters3D.create(from, to)
query.collision_mask = collision_mask
var result: Dictionary = space_state.intersect_ray(query)
if result:
    var hit_point: Vector3 = result.position
    var hit_normal: Vector3 = result.normal
```

## Common Mistakes
- Assuming GodotPhysics3D is the default (Jolt since 4.6)
- Using HingeJoint3D `damp` property without checking physics engine (Jolt ignores it)
- Not testing collision edge cases when switching between physics engines

---

## Fixed-Timestep Settings — VERIFIED at 4.7.1 (2026-08-25)

> **Why this section exists.** ADR-0001 self-flagged `max_physics_steps_per_frame`'s name,
> default and **runtime read-API** as *genuinely unverified in 4.7.1*
> (`architecture-review-2026-08-08.md` finding 3, technical-director OQ #9). Traceability row
> **TR-time-011** ("startup guard: engine step-clamp ≥ SubStepCap") is ⚠️ Partial for the same
> reason. This closes both.
>
> **Verification method**: read from the `4.7.1-stable` tag of `godotengine/godot`
> (`doc/classes/ProjectSettings.xml`, `doc/classes/Engine.xml`), not from training data.
> Hollowdeep's simulation does **not** use the physics engine — but it rides the physics
> *frame* (`TimeAuthorityRoot._PhysicsProcess`), so these two settings define its tick
> semantics.

### The two settings

| Project setting | Type | Default | Runtime accessor |
|---|---|---|---|
| `physics/common/physics_ticks_per_second` | `int` | **60** | `Engine.physics_ticks_per_second` |
| `physics/common/max_physics_steps_per_frame` | `int` | **8** | `Engine.max_physics_steps_per_frame` |

Both names and both defaults are **confirmed exactly as ADR-0001 assumed.** The ADR's
`SubStepDuration == 1/physics_ticks_per_second` arithmetic and its "Godot clamps at
`max_physics_steps_per_frame`, default 8" statement are correct at 4.7.1.

### ⚠️ The read-API answer: use `Engine`, NOT `ProjectSettings`

ADR-0001 asked whether the runtime surface is `ProjectSettings.GetSetting` or an `Engine`
singleton property. **It is `Engine`**, and the distinction is load-bearing rather than
stylistic. Both ProjectSettings entries carry this note verbatim:

> *"**Note:** This property is only read when the project starts. To change the maximum number
> of simulated physics steps per frame at runtime, set `Engine.max_physics_steps_per_frame`
> instead."*

So `ProjectSettings.GetSetting("physics/common/max_physics_steps_per_frame")` returns the
**authored** value and will **not** reflect any runtime change. A startup guard reading it
would validate a number the engine may no longer be using — checking the wrong value while
appearing to pass.

**Binding consequence for ADR-0001's startup guard (TR-time-011):** read
`Engine.MaxPhysicsStepsPerFrame` and `Engine.PhysicsTicksPerSecond` (C# property names on the
`Engine` singleton; underlying accessors `set_/get_max_physics_steps_per_frame` and
`set_/get_physics_ticks_per_second`). Do not read the ProjectSettings keys for the guard.

### Exact doc text — the spiral-of-death rationale

`max_physics_steps_per_frame`: *"Controls the maximum number of physics steps that can be
simulated each rendered frame. … the engine is only allowed to simulate a certain number of
physics steps per rendered frame. This snowballs into a situation where framerate keeps
dropping until it reaches a very low framerate (typically 1-2 FPS) and is called the
**physics spiral of death**. However, the game will appear to slow down if the rendering FPS
is less than `1 / max_physics_steps_per_frame` of `physics/common/physics_ticks_per_second`.
This occurs even if `delta` is consistently used in physics calculations."*

This is **exactly** the behaviour ADR-0001 relies on and the mode-switch spike regression-locked
("a 1-second frame clamps at 8 sub-steps and drops the backlog — the sim slows, no death
spiral"). Engine behaviour and ADR intent agree.

> **Interaction worth noting**: the engine clamp (8) and the project's own `SubStepCap`
> (ADR-0001 default 8) are two independent clamps that happen to share a value. ADR-0001's
> guard asserts engine-clamp ≥ SubStepCap, so they are *permitted* to differ — but anyone
> raising `SubStepCap` for higher game speeds must raise the engine setting too, or the engine
> silently becomes the binding constraint.

### Also present at 4.7.1 (not currently used, recorded to prevent surprise)

- `physics/common/physics_interpolation` — `bool`, default `false`. If enabled, the renderer
  interpolates transforms between physics ticks. **It automatically disables
  `physics_jitter_fix`.** Hollowdeep does its own presentation interpolation in `_Process`
  (ADR-0001), so this should stay `false`; enabling it would add a second, competing smoothing
  mechanism.
- `physics/common/physics_jitter_fix` — `float`, default `0.5`. Deviates the in-game clock from
  the real clock to smooth frame jitter. **Relevant to determinism**: the docs recommend `0`
  for network games "where clock synchronization matters", and note it should be `0.0` when
  using a custom interpolation solution. Hollowdeep is single-player, but it *is* a
  determinism-critical fixed-step simulation with its own accumulator — **evaluate setting
  this to `0` when `TimeAuthorityRoot` is built**, and record the choice in ADR-0001.
