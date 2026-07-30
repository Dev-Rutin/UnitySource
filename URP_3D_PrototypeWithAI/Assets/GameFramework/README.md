# Rutin Game Framework

This folder contains the allocation-conscious foundation for modular gameplay.

## Composition

- Add `GameplayEntity` to a PC, NPC, or world object.
- Implement each capability as an `EntityFeature` and add/remove that component as needed.
- Features receive deterministic initialize, activate, deactivate, and shutdown callbacks.
- Avoid per-feature `Update()`. Implement `IGameTickable` and register it once with `ITickScheduler`.
- Size `frameBudgetMilliseconds` and `maxVisitedItemsPerFrame` for the full registered
  population. Disabled tickables still consume one visit from the item budget.
- `TickSchedulerService` quarantines only repeated failures, rate-limits exception logs,
  and exposes the session total through `TotalQuarantinedCount`.
- Scheduled features log unexpected registration loss and automatically bind to a replacement
  scheduler when the default host's service registry changes.
- Scheduler clear removes registrations before observer callbacks, so recovery callbacks can
  safely register a new scheduling session without mutating a live iteration.

## Management

- Add one `GameManagerHost` to the bootstrap scene.
- Add `GameServiceBehaviour` components, such as `TickSchedulerService` and
  `PooledObjectFactory`, to the same object.
- Resolve contracts once through `GameManagerHost.Services` and cache the result in hot paths.
- The registry never scans the scene and does not use LINQ.

## Playable character

- Add `CharacterController`, `GameplayEntity`, `PlayerCommandFeature`, and
  `PlayerCharacterMotorFeature` to the player object.
- Add `PlayerLookFeature` when the entity owns yaw/pitch transforms. Look commands are angular
  deltas in degrees and are latched until consumed; jump presses are also latched.
- For local control, add `InputSystemPlayerCommandSource` from the separate
  `Rutin.GameFramework.InputSystem` assembly and assign move, look, and jump actions.
  `PlayerCommandFeature` discovers the source once during initialization. Enable
  `lookValueIsAngularRate` for stick-style look actions; mouse-delta actions should leave it off.
  This adapter uses one local `Update()` to latch frame-only Input System edges and deltas so a
  budget-delayed simulation tick cannot lose them. Ownership, simulation, deactivation, and
  scheduler-loss transitions discard the adapter's buffered input so stale edges cannot replay.
- For remote/server control, call `SetLocallyControlled(false)` and submit immutable
  `PlayerCommand` snapshots through `SubmitCommand`. Movement and view components do not depend
  on the Unity Input System and can use network, replay, or AI command sources. Non-zero sequence
  values reject duplicate/out-of-order packets, and remote movement becomes neutral after the
  configured command timeout while gravity and other neutral simulation continue.
- Call `SetSimulationEnabled(false)` when despawning or suspending authority. Ownership changes
  clear held input and reset all command consumers.
- Only `PlayerCommandFeature` inherits `ScheduledEntityFeature`. It pushes one command snapshot
  to sorted motor/view consumers, making each player stack atomic even when the global scheduler
  is budget-limited or swap-removes other entities. The motor integrates accumulated time using
  bounded fixed substeps and buffers jump input briefly so a landing later in the same batch does
  not lose the edge.
- `PlayerCharacterMotorFeature` automatically uses `PlayerLookFeature.MovementReference` when no
  explicit movement space is configured, keeping view and locomotion axes aligned. Look consumers
  run before the motor so movement uses the current command's yaw without a one-tick delay.
  Runtime `SetViewTransforms` changes update the automatic motor reference and preserve view
  offsets; an explicit `SetMovementSpace` remains authoritative.
- Inject a different `ITickScheduler` with `SetTickScheduler` for multi-world/server simulations.
  Explicit injection, including `SetTickScheduler(null)` for detachment, never falls back to the
  default world. Call `UseDefaultTickScheduler()` to opt back into default-host resolution.

## Factory and pooling

- Configure integer-keyed pools on the host's `PooledObjectFactory`.
- Resolve `IPooledObjectFactory` through `GameManagerHost.Services` and cache it.
- Use `TryRent` in gameplay hot paths so capacity exhaustion does not throw.
- Return objects through `PooledInstance.ReturnToPool()` or the factory.
- Implement `IPoolable` for deterministic state reset. Callback lists are cached per instance.
- `OnRentFromPool()` always precedes `OnEnable`, but a clone's first rent can precede `Awake`.
  Lazily initialize any callback dependency that would otherwise be cached only in `Awake`.
- Call `PooledInstance.RefreshCallbacks()` only after changing a pooled hierarchy at runtime.

## Performance test

`FrameworkStressBenchmark` can be attached to a benchmark scene. The default profile:

- 5,000 prewarmed objects
- 10 activation/return cycles
- 1,000 ms initial activation budget
- 250 ms average steady-state cycle budget
- 1 MiB measured managed-allocation budget

The PlayMode test uses 5,000 objects by default and writes a `POOL_STRESS` line to the test log.
CI or local hardware can override its thresholds:

- `RUTIN_POOL_STRESS_OBJECTS`
- `RUTIN_POOL_STRESS_BUDGET_MS`
- `RUTIN_POOL_STRESS_ALLOC_BYTES`

The first activation of a newly instantiated Unity object is intentionally excluded from the
steady-state measurement. Production scenes should prewarm expected populations during loading.

### Verified baseline

Unity `6000.3.9f1`, Windows Editor, batch mode on 2026-07-30:

| Suite | Result | Duration / measurement |
| --- | --- | --- |
| EditMode | 26 passed, 0 failed | 0.157 s test duration |
| PlayMode | 37 passed, 0 failed | 0.898 s test duration |
| 1,000 PC command/look ticks | Passed | 0 managed bytes |
| 5,000-object pooled rent/return | Passed | 84.755 ms, 0 managed bytes |

The 5,000-object figure is a bulk upper-bound measurement, not a per-frame target.
At 60 FPS, gameplay code should distribute activation work across frames and use the
central `ITickScheduler` rather than activating the entire population in one frame.
