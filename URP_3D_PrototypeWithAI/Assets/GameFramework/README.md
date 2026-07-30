# Rutin Game Framework

This folder contains the allocation-conscious foundation for modular gameplay.

## Composition

- Add `GameplayEntity` to a PC, NPC, or world object.
- Implement each capability as an `EntityFeature` and add/remove that component as needed.
- Features receive deterministic initialize, activate, deactivate, and shutdown callbacks.
- Avoid per-feature `Update()`. Implement `IGameTickable` and register it once with `ITickScheduler`.
- `TickSchedulerService` quarantines only repeated failures, rate-limits exception logs,
  and exposes the session total through `TotalQuarantinedCount`.

## Management

- Add one `GameManagerHost` to the bootstrap scene.
- Add `GameServiceBehaviour` components, such as `TickSchedulerService` and
  `PooledObjectFactory`, to the same object.
- Resolve contracts once through `GameManagerHost.Services` and cache the result in hot paths.
- The registry never scans the scene and does not use LINQ.

## Factory and pooling

- Configure integer-keyed pools on the host's `PooledObjectFactory`.
- Resolve `IPooledObjectFactory` through `GameManagerHost.Services` and cache it.
- Use `TryRent` in gameplay hot paths so capacity exhaustion does not throw.
- Return objects through `PooledInstance.ReturnToPool()` or the factory.
- Implement `IPoolable` for deterministic state reset. Callback lists are cached per instance.
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
| EditMode | 21 passed, 0 failed | 0.185 s test duration |
| PlayMode | 15 passed, 0 failed | 1.028 s test duration |
| 5,000-object pooled rent/return | Passed | 97.865 ms, 0 managed bytes |

The 5,000-object figure is a bulk upper-bound measurement, not a per-frame target.
At 60 FPS, gameplay code should distribute activation work across frames and use the
central `ITickScheduler` rather than activating the entire population in one frame.
