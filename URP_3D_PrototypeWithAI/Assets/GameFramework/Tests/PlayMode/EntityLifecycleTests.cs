using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Rutin.GameFramework.Core;
using Rutin.GameFramework.Factory;
using Rutin.GameFramework.Management;
using Rutin.GameFramework.Ticking;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rutin.GameFramework.Tests.PlayMode
{
    public sealed class EntityLifecycleTests
    {
        private static readonly List<string> InitializationLog = new();
        private static readonly List<string> ShutdownLog = new();

        private sealed class ProbeFeature : EntityFeature
        {
            public int InitializeCount { get; private set; }
            public int ActivateCount { get; private set; }
            public int DeactivateCount { get; private set; }
            public int ShutdownCount { get; private set; }

            protected override void OnFeatureInitialized()
            {
                InitializeCount++;
            }

            protected override void OnFeatureActivated()
            {
                ActivateCount++;
            }

            protected override void OnFeatureDeactivated()
            {
                DeactivateCount++;
            }

            protected override void OnFeatureShutdown()
            {
                ShutdownCount++;
            }
        }

        private sealed class LateFeature : EntityFeature
        {
            public override int InitializationOrder => 100;

            protected override void OnFeatureInitialized()
            {
                InitializationLog.Add(nameof(LateFeature));
            }
        }

        private sealed class EarlyFeature : EntityFeature
        {
            public override int InitializationOrder => -100;

            protected override void OnFeatureInitialized()
            {
                InitializationLog.Add(nameof(EarlyFeature));
            }
        }

        private sealed class LateService : GameServiceBehaviour
        {
            public override int InitializationOrder => 100;

            protected override void OnServiceInitialized()
            {
                InitializationLog.Add(nameof(LateService));
            }
        }

        private sealed class EarlyService : GameServiceBehaviour
        {
            public override int InitializationOrder => -100;

            protected override void OnServiceInitialized()
            {
                InitializationLog.Add(nameof(EarlyService));
            }
        }

        private interface IConflictingService
        {
        }

        private sealed class FirstContractService : GameServiceBehaviour, IConflictingService
        {
            public override int InitializationOrder => -100;

            protected override void RegisterServiceContracts()
            {
                RegisterContract<IConflictingService>();
            }
        }

        private sealed class ConflictingService : GameServiceBehaviour, IConflictingService
        {
            protected override void RegisterServiceContracts()
            {
                RegisterContract<IConflictingService>();
            }
        }

        private sealed class FollowingService : GameServiceBehaviour
        {
            public override int InitializationOrder => 100;
        }

        private sealed class FailingFeature : EntityFeature
        {
            protected override void OnFeatureInitialized()
            {
                throw new System.InvalidOperationException("Feature initialization failure");
            }
        }

        private sealed class FollowingFeature : EntityFeature
        {
            public override int InitializationOrder => 100;
        }

        private sealed class FollowingShutdownFeature : EntityFeature
        {
            protected override void OnFeatureShutdown()
            {
                ShutdownLog.Add(nameof(FollowingShutdownFeature));
            }
        }

        private sealed class ThrowingShutdownFeature : EntityFeature
        {
            public override int InitializationOrder => 100;

            protected override void OnFeatureShutdown()
            {
                ShutdownLog.Add(nameof(ThrowingShutdownFeature));
                throw new System.InvalidOperationException("Feature shutdown failure");
            }
        }

        private sealed class ThrowingActivationFeature : EntityFeature
        {
            public override int InitializationOrder => -100;

            protected override void OnFeatureActivated()
            {
                throw new System.InvalidOperationException("Feature activation failure");
            }
        }

        private sealed class ThrowingDeactivationFeature : EntityFeature
        {
            public override int InitializationOrder => 100;

            protected override void OnFeatureDeactivated()
            {
                throw new System.InvalidOperationException("Feature deactivation failure");
            }
        }

        [DefaultExecutionOrder(-9001)]
        private sealed class PreEntityFeature : EntityFeature
        {
            public int InitializeCount { get; private set; }

            protected override void OnFeatureInitialized()
            {
                InitializeCount++;
            }
        }

        [DefaultExecutionOrder(-10001)]
        private sealed class PreHostService : GameServiceBehaviour
        {
            public int InitializeCount { get; private set; }

            protected override void OnServiceInitialized()
            {
                InitializeCount++;
            }
        }

        private sealed class ThrowingActivationService : GameServiceBehaviour
        {
            public override int InitializationOrder => -100;

            protected override void OnServiceActivated()
            {
                throw new System.InvalidOperationException("Service activation failure");
            }
        }

        private sealed class ThrowingDeactivationService : GameServiceBehaviour
        {
            public override int InitializationOrder => 100;

            protected override void OnServiceDeactivated()
            {
                throw new System.InvalidOperationException("Service deactivation failure");
            }
        }

        private sealed class ThrowingShutdownService : GameServiceBehaviour
        {
            protected override void OnServiceShutdown()
            {
                throw new System.InvalidOperationException("Service shutdown failure");
            }
        }

        private sealed class ThrowingShutdownPathDeactivationService : GameServiceBehaviour
        {
            public int ShutdownCount { get; private set; }

            protected override void OnServiceDeactivated()
            {
                throw new System.InvalidOperationException(
                    "Shutdown path deactivation failure");
            }

            protected override void OnServiceShutdown()
            {
                ShutdownCount++;
            }
        }

        private sealed class ProbeTickable : IGameTickable
        {
            public bool IsTickEnabled => true;

            public int TickCount { get; private set; }

            public void Tick(float deltaTime)
            {
                TickCount++;
            }
        }

        [UnityTest]
        public IEnumerator Feature_FollowsEntityActivationLifecycle()
        {
            GameObject entityObject = new("Entity Lifecycle Test");
            entityObject.SetActive(false);
            GameplayEntity entity = entityObject.AddComponent<GameplayEntity>();
            ProbeFeature feature = entityObject.AddComponent<ProbeFeature>();

            entityObject.SetActive(true);
            yield return null;

            Assert.That(entity.FeatureCount, Is.EqualTo(1));
            Assert.That(feature.InitializeCount, Is.EqualTo(1));
            Assert.That(feature.ActivateCount, Is.EqualTo(1));

            feature.enabled = false;
            yield return null;

            Assert.That(feature.DeactivateCount, Is.EqualTo(1));

            Object.Destroy(entityObject);
            yield return null;

            Assert.That(feature == null, Is.True);
        }

        [UnityTest]
        public IEnumerator Features_InitializeInDeclaredOrder()
        {
            InitializationLog.Clear();
            GameObject entityObject = new("Ordered Entity");
            entityObject.SetActive(false);
            entityObject.AddComponent<GameplayEntity>();
            entityObject.AddComponent<LateFeature>();
            entityObject.AddComponent<EarlyFeature>();

            entityObject.SetActive(true);
            yield return null;

            Assert.That(
                InitializationLog,
                Is.EqualTo(new[] { nameof(EarlyFeature), nameof(LateFeature) }));

            Object.Destroy(entityObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Services_InitializeInDeclaredOrder()
        {
            InitializationLog.Clear();
            GameObject hostObject = new("Ordered Host");
            hostObject.SetActive(false);
            GameManagerHost host = hostObject.AddComponent<GameManagerHost>();
            hostObject.AddComponent<LateService>();
            hostObject.AddComponent<EarlyService>();

            hostObject.SetActive(true);
            yield return null;

            Assert.That(
                InitializationLog,
                Is.EqualTo(new[] { nameof(EarlyService), nameof(LateService) }));
            Assert.That(host.ServiceCount, Is.EqualTo(2));

            Object.Destroy(hostObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FailedServiceInitialization_RollsBackAndContinues()
        {
            GameObject hostObject = new("Failure Isolation Host");
            hostObject.SetActive(false);
            GameManagerHost host = hostObject.AddComponent<GameManagerHost>();
            FirstContractService first = hostObject.AddComponent<FirstContractService>();
            ConflictingService conflicting = hostObject.AddComponent<ConflictingService>();
            FollowingService following = hostObject.AddComponent<FollowingService>();
            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex(
                    "A service is already registered.*IConflictingService"));

            hostObject.SetActive(true);
            yield return null;

            Assert.That(first.IsServiceInitialized, Is.True);
            Assert.That(conflicting.IsServiceInitialized, Is.False);
            Assert.That(following.IsServiceInitialized, Is.True);
            Assert.That(host.ServiceCount, Is.EqualTo(2));
            Assert.That(host.Services.TryGet<ConflictingService>(out _), Is.False);

            Object.Destroy(hostObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FailedFeatureInitialization_RollsBackAndContinues()
        {
            GameObject entityObject = new("Feature Failure Isolation Entity");
            entityObject.SetActive(false);
            GameplayEntity entity = entityObject.AddComponent<GameplayEntity>();
            FailingFeature failing = entityObject.AddComponent<FailingFeature>();
            FollowingFeature following = entityObject.AddComponent<FollowingFeature>();
            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex("Feature initialization failure"));

            entityObject.SetActive(true);
            yield return null;

            Assert.That(failing.IsFeatureInitialized, Is.False);
            Assert.That(following.IsFeatureInitialized, Is.True);
            Assert.That(entity.FeatureCount, Is.EqualTo(1));

            Object.Destroy(entityObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EarlyAwakeRegistrations_AreNotInsertedTwice()
        {
            GameObject entityObject = new("Early Feature Registration Entity");
            entityObject.SetActive(false);
            GameplayEntity entity = entityObject.AddComponent<GameplayEntity>();
            PreEntityFeature feature = entityObject.AddComponent<PreEntityFeature>();

            entityObject.SetActive(true);
            yield return null;

            Assert.That(entity.FeatureCount, Is.EqualTo(1));
            Assert.That(feature.InitializeCount, Is.EqualTo(1));

            Object.Destroy(entityObject);
            yield return null;

            GameObject hostObject = new("Early Service Registration Host");
            hostObject.SetActive(false);
            GameManagerHost host = hostObject.AddComponent<GameManagerHost>();
            PreHostService service = hostObject.AddComponent<PreHostService>();

            hostObject.SetActive(true);
            yield return null;

            Assert.That(host.ServiceCount, Is.EqualTo(1));
            Assert.That(service.InitializeCount, Is.EqualTo(1));

            Object.Destroy(hostObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FeatureShutdownFailure_DoesNotSkipRemainingFeatures()
        {
            ShutdownLog.Clear();
            GameObject entityObject = new("Feature Shutdown Isolation Entity");
            entityObject.SetActive(false);
            entityObject.AddComponent<GameplayEntity>();
            entityObject.AddComponent<FollowingShutdownFeature>();
            entityObject.AddComponent<ThrowingShutdownFeature>();
            entityObject.SetActive(true);
            yield return null;

            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex("Feature shutdown failure"));
            Object.Destroy(entityObject);
            yield return null;

            Assert.That(
                ShutdownLog,
                Is.EqualTo(new[]
                {
                    nameof(ThrowingShutdownFeature),
                    nameof(FollowingShutdownFeature)
                }));
        }

        [UnityTest]
        public IEnumerator FeatureActivationFailures_AreIsolatedAndStateRemainsConsistent()
        {
            GameObject entityObject = new("Feature Activation Isolation Entity");
            entityObject.SetActive(false);
            entityObject.AddComponent<GameplayEntity>();
            ThrowingActivationFeature throwingActivation =
                entityObject.AddComponent<ThrowingActivationFeature>();
            ProbeFeature following = entityObject.AddComponent<ProbeFeature>();
            ThrowingDeactivationFeature throwingDeactivation =
                entityObject.AddComponent<ThrowingDeactivationFeature>();

            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex("Feature activation failure"));
            entityObject.SetActive(true);
            yield return null;

            Assert.That(throwingActivation.IsFeatureActive, Is.False);
            Assert.That(following.IsFeatureActive, Is.True);
            Assert.That(throwingDeactivation.IsFeatureActive, Is.True);

            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex("Feature deactivation failure"));
            entityObject.SetActive(false);
            yield return null;

            Assert.That(following.IsFeatureActive, Is.False);
            Assert.That(throwingDeactivation.IsFeatureActive, Is.False);

            Object.Destroy(entityObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TickScheduler_PreservesRegistrationMadeBeforeHostInitialization()
        {
            GameObject hostObject = new("Early Tick Registration Host");
            hostObject.SetActive(false);
            hostObject.AddComponent<GameManagerHost>();
            TickSchedulerService scheduler = hostObject.AddComponent<TickSchedulerService>();
            ProbeTickable tickable = new();

            Assert.That(scheduler.Register(tickable), Is.True);

            hostObject.SetActive(true);
            yield return null;

            Assert.That(scheduler.Count, Is.EqualTo(1));

            Object.Destroy(hostObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TickScheduler_StopsWhileHostServiceLifecycleIsInactive()
        {
            GameObject hostObject = new("Tick Lifecycle Host");
            hostObject.SetActive(false);
            GameManagerHost host = hostObject.AddComponent<GameManagerHost>();
            TickSchedulerService scheduler = hostObject.AddComponent<TickSchedulerService>();
            ProbeTickable tickable = new();
            scheduler.Register(tickable);

            hostObject.SetActive(true);
            yield return null;
            int activeTickCount = tickable.TickCount;
            Assert.That(activeTickCount, Is.GreaterThan(0));

            host.enabled = false;
            yield return null;
            yield return null;
            Assert.That(tickable.TickCount, Is.EqualTo(activeTickCount));

            host.enabled = true;
            yield return null;
            Assert.That(tickable.TickCount, Is.GreaterThan(activeTickCount));

            Object.Destroy(hostObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ServiceLifecycleFailures_AreIsolatedAndDestroyedServiceIsRemoved()
        {
            GameObject hostObject = new("Service Lifecycle Isolation Host");
            hostObject.SetActive(false);
            GameManagerHost host = hostObject.AddComponent<GameManagerHost>();
            ThrowingActivationService throwingActivation =
                hostObject.AddComponent<ThrowingActivationService>();
            FollowingService following = hostObject.AddComponent<FollowingService>();
            ThrowingDeactivationService throwingDeactivation =
                hostObject.AddComponent<ThrowingDeactivationService>();
            ThrowingShutdownService throwingShutdown =
                hostObject.AddComponent<ThrowingShutdownService>();
            ThrowingShutdownPathDeactivationService shutdownPath =
                hostObject.AddComponent<ThrowingShutdownPathDeactivationService>();

            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex("Service activation failure"));
            hostObject.SetActive(true);
            yield return null;

            Assert.That(throwingActivation.IsServiceActive, Is.False);
            Assert.That(following.IsServiceActive, Is.True);
            Assert.That(throwingDeactivation.IsServiceActive, Is.True);

            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex("Service shutdown failure"));
            Object.Destroy(throwingShutdown);
            yield return null;
            Assert.That(host.ServiceCount, Is.EqualTo(4));

            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex(
                    "Shutdown path deactivation failure"));
            Object.Destroy(shutdownPath);
            yield return null;
            Assert.That(shutdownPath.ShutdownCount, Is.EqualTo(1));
            Assert.That(host.ServiceCount, Is.EqualTo(3));

            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex("Service deactivation failure"));
            host.enabled = false;
            yield return null;

            Assert.That(following.IsServiceActive, Is.False);
            Assert.That(throwingDeactivation.IsServiceActive, Is.False);

            Object.Destroy(hostObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PooledFactory_IsResolvableAsManagerService()
        {
            GameObject hostObject = new("Pooled Factory Host");
            hostObject.SetActive(false);
            GameManagerHost host = hostObject.AddComponent<GameManagerHost>();
            PooledObjectFactory factory = hostObject.AddComponent<PooledObjectFactory>();

            hostObject.SetActive(true);
            yield return null;

            Assert.That(
                host.TryGetService<IPooledObjectFactory>(out IPooledObjectFactory resolved),
                Is.True);
            Assert.That(resolved, Is.SameAs(factory));

            Object.Destroy(hostObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DuplicateDefaultHost_IsRejected()
        {
            GameObject firstObject = new("First Default Host");
            firstObject.SetActive(false);
            GameManagerHost first = firstObject.AddComponent<GameManagerHost>();
            firstObject.SetActive(true);
            yield return null;

            GameObject duplicateObject = new("Duplicate Default Host");
            duplicateObject.SetActive(false);
            GameManagerHost duplicate = duplicateObject.AddComponent<GameManagerHost>();
            PreHostService duplicateService = duplicateObject.AddComponent<PreHostService>();
            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "Duplicate default GameManagerHost.*was rejected"));

            duplicateObject.SetActive(true);
            yield return null;

            Assert.That(GameManagerHost.Default, Is.SameAs(first));
            Assert.That(duplicateObject, Is.Not.Null);
            Assert.That(duplicate.enabled, Is.False);
            Assert.That(duplicateService.enabled, Is.False);
            Assert.That(duplicateService.IsServiceInitialized, Is.False);
            Assert.That(duplicate.ServiceCount, Is.Zero);

            Object.Destroy(duplicateObject);
            Object.Destroy(firstObject);
            yield return null;
        }
    }
}
