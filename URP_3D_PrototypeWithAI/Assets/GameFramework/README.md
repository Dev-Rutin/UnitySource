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
- Time budgets are checked between tickables, so size them for the worst-case bounded work of one
  player stack. `LastFrameStats.ClampedTickCount` / `DiscardedDeltaTimeSeconds` and
  `TotalDiscardedDeltaTimeSeconds` expose the aggregate of per-tickable simulation time discarded
  by the scheduler cap; this aggregate can exceed wall-clock time when many tickables clamp.
  `LastFrameStats.MaximumDiscardedDeltaTimeSeconds` exposes the worst single tickable for the frame.
  `PlayerCharacterMotorFeature.TotalDiscardedSimulationTimeSeconds` separately exposes time
  discarded by its fixed-substep cap, including configurations where the motor cap is lower.
- Scheduled features use an allocation-conscious dense observer registry to invalidate cached
  default services and bind to a replacement scheduler, including while the feature is inactive.
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
  deltas in degrees and are latched until consumed; jump presses are also latched. Look is a
  dispatch-domain, unscaled view update and can continue while simulation time is paused. Disable
  the command/view feature or its source when a game pause must also freeze the camera.
- For local control, add `InputSystemPlayerCommandSource` from the separate
  `Rutin.GameFramework.InputSystem` assembly and assign move, look, and jump actions.
  `PlayerCommandFeature` discovers the source once during initialization. Enable
  `lookValueIsAngularRate` for stick-style look actions; mouse-delta actions should leave it off.
  This adapter uses one local `Update()` to latch frame-only Input System edges and deltas so a
  budget-delayed simulation tick cannot lose them. Ownership, simulation, deactivation, and
  scheduler-loss transitions discard the adapter's buffered input so stale edges cannot replay.
- Custom `IPlayerCommandSource` components that sample frame input in `Update()` must execute
  after `GameplayEntity` initialization and before `TickSchedulerService`; use an execution order
  between `-9000` and `-8990`. Frame-latched sources should implement
  `IBufferedPlayerCommandSource` so ownership and scheduler transitions can discard stale edges.
- For remote/server control, call `SetLocallyControlled(false)` and submit immutable
  `PlayerCommand` snapshots through `SubmitCommand`. Movement and view components do not depend
  on the Unity Input System and can use network, replay, or AI command sources. Non-zero sequence
  values reject duplicate/out-of-order packets, and remote movement becomes neutral after the
  configured command timeout while gravity and other neutral simulation continue. Commands
  submitted while the feature is inactive or locally controlled are rejected instead of
  accumulating stale edges or mixing remote input into the local command stream.
- Replay/server commands can set `SimulationDeltaTimeSeconds` to enter command-owned time mode.
  Explicit durations received before one dispatch are accumulated; empty dispatches then advance
  zero simulation time instead of mixing in the scheduler visit delta. Commands constructed
  without the duration retain live-input timing. Preserve fixed-step settings, initial state,
  collision world, and command order for deterministic replay. Use an `IPlayerCommandSource` when
  every recorded movement transition must be consumed in a separate scheduler dispatch. Network
  serialization must round-trip `MoveSpace`, `HasSimulationDeltaTime`, and
  `SimulationDeltaTimeSeconds`; use the seven-argument `PlayerCommand` constructor when
  reconstructing a transported command. Commands created through the shorter constructors use
  relative movement for backward-compatible local-player behavior.
  The payload normalizes non-finite move/look components and non-finite or negative durations to
  zero. Yaw deltas are reduced modulo one turn and pitch deltas are saturated to one half-turn,
  preventing a malformed packet from poisoning transforms. Finite duration budgets are enforced
  once by the motor's observable command-backlog limit, after durations submitted within one
  dispatch have been accumulated without a smaller intermediate clamp.
  A remote timeout exits command-owned time and resumes live-timed neutral gravity simulation.
  Set `remoteCommandTimeout` (or call `SetRemoteCommandTimeout`) to zero for deterministic streams
  that must never fall back to wall-clock time; the default positive timeout remains safer for
  network-owned players that should recover to neutral simulation after a disconnect. Timed
  backlog preservation is guaranteed only while command-owned mode remains active, so streams
  that must drain every recorded interval must disable the wall-clock timeout. Remote
  streams may change timing mode after a dispatch; mixing timed and live commands inside one
  pending dispatch returns `RetryAfterDispatch` from `SubmitCommandDetailed` to avoid silently
  discarding either timing contract. Producers must retry that same immutable command after the
  next dispatch so its look and jump edges are preserved. The legacy `SubmitCommand` boolean
  wrapper returns `false` for every rejection category.
- Call `SetSimulationEnabled(false)` when despawning or suspending authority. Ownership changes
  clear held input and reset all command consumers.
- Only `PlayerCommandFeature` inherits `ScheduledEntityFeature`. It pushes one command snapshot
  to sorted motor/view consumers, making each player stack atomic even when the global scheduler
  is budget-limited or swap-removes other entities. The motor integrates accumulated time using
  bounded fixed substeps and buffers jump input briefly so a landing later in the same batch does
  not lose the edge. Live wall-clock input discards excess accumulated time at the substep cap;
  command-owned replay time retains excess as backlog and drains it across later zero-duration
  dispatches, keeping the final replay state independent of command batching within the configured
  `maximumCommandBacklogSeconds`. `PendingSimulationTimeSeconds` exposes current replay latency,
  while excess beyond the finite backlog limit contributes to
  `TotalDiscardedSimulationTimeSeconds`.
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

## NPC

- Build an NPC from `GameplayEntity`, `PlayerCommandFeature`, and `NpcBrainFeature`. Add
  `PlayerCharacterMotorFeature` (and its required `CharacterController`) when the NPC uses the
  shared character locomotion pipeline.
- `NpcBrainFeature` is an `IPlayerCommandSource`, not another scheduled tickable. The sibling
  `PlayerCommandFeature` remains the NPC stack's only central-scheduler registration, so sensing,
  decisions, and command consumers are visited atomically under a frame budget.
- Derive sensors from `NpcSensorFeature` and policies from `NpcDecisionProviderFeature`, or
  register lightweight `INpcSensor` / `INpcDecisionProvider` implementations at runtime. Both
  contracts are ordered; the first decision provider returning `true` wins. Active feature
  components automatically register and unregister as they are attached, disabled, or removed.
- `TransformTargetSensorFeature` consumes a target assigned by gameplay or interest management
  without scene or physics scans. `IdlePatrolChaseDecisionFeature` is the basic idle/patrol/chase
  example and can be replaced by more specialized policies.
- The value-type `NpcBlackboard` clears perception before every sensing pass. Pooling,
  deactivation, command ownership changes, simulation suspension, and scheduler replacement also
  reset decision state and buffered jump edges, preventing stale targets or commands from leaking
  into a new authority session. Repeated ticks while decision input is unavailable are idempotent
  and do not rescan/reset every registered participant.
- `decisionIntervalSeconds` reduces expensive decision frequency while movement commands remain
  held between decisions. The default negative initial delay staggers the first decision over that
  interval. Its fallback instance-ID seed is stable only within the running process; call
  `SetStaggerSeed` with a stable spawn/network identifier for replay, migration, or cross-process
  repeatability. Call `ConfigureDecisionCadence(interval, 0)` only when immediate, synchronized
  evaluation is required.
- NPC decisions emit absolute `World` movement commands. The motor therefore follows the same
  world direction even if a remote proxy loses an earlier orientation update or uses a rotated
  local movement reference. Attach the optional `NpcFacingFeature` to rotate a configured yaw
  root toward each absolute movement snapshot; the next received snapshot repairs visual facing
  after packet loss without coupling movement correctness to consumer order.
- World-space decision movement is sanitized and transported without managed allocation.
  Authoritative server NPCs keep the brain enabled and the command feature locally sourced;
  remote proxies disable decision-making and submit replicated `PlayerCommand` snapshots instead.
  Network/server sensors can populate the blackboard directly and do not depend on the Unity
  Input System.

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
- `RUTIN_NPC_STRESS_BUDGET_MS` (default: 250 ms for 10,000 decision/command ticks across
  1,000 NPCs)

The first activation of a newly instantiated Unity object is intentionally excluded from the
steady-state measurement. Production scenes should prewarm expected populations during loading.

### Verified baseline

Unity `6000.3.9f1`, Windows Editor, batch mode on 2026-08-03:

| Suite | Result | Duration / measurement |
| --- | --- | --- |
| EditMode | 28 passed, 0 failed | 0.390 s test duration |
| PlayMode | 68 passed, 0 failed | 1.800 s test duration |
| 1,000 PC command/look ticks | Passed | 0 managed bytes |
| 1,000 NPCs × 10 decision/command ticks | Passed | 52.713 ms, 0 managed bytes |
| 5,000-object pooled rent/return | Passed | 117.129 ms, 0 managed bytes |

The 5,000-object figure is a bulk upper-bound measurement, not a per-frame target.
At 60 FPS, gameplay code should distribute activation work across frames and use the
central `ITickScheduler` rather than activating the entire population in one frame.
