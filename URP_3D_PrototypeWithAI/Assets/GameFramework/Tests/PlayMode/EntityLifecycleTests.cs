using System.Collections;
using NUnit.Framework;
using Rutin.GameFramework.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rutin.GameFramework.Tests.PlayMode
{
    public sealed class EntityLifecycleTests
    {
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
    }
}
