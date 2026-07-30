using NUnit.Framework;
using Rutin.GameFramework.Management;

namespace Rutin.GameFramework.Tests.EditMode
{
    public sealed class ServiceRegistryTests
    {
        private interface IProbeService
        {
        }

        private sealed class ProbeService : IProbeService
        {
        }

        [Test]
        public void RegisterAndGet_UsesExplicitContract()
        {
            ServiceRegistry registry = new();
            ProbeService expected = new();

            registry.Register<IProbeService>(expected);

            Assert.That(registry.TryGet<IProbeService>(out IProbeService actual), Is.True);
            Assert.That(actual, Is.SameAs(expected));
            Assert.That(registry.Count, Is.EqualTo(1));
        }

        [Test]
        public void Register_DuplicateContractWithDifferentInstance_Throws()
        {
            ServiceRegistry registry = new();
            registry.Register<IProbeService>(new ProbeService());

            Assert.Throws<System.InvalidOperationException>(
                () => registry.Register<IProbeService>(new ProbeService()));
        }

        [Test]
        public void Unregister_RequiresMatchingInstance()
        {
            ServiceRegistry registry = new();
            ProbeService registered = new();
            registry.Register<IProbeService>(registered);

            Assert.That(
                registry.Unregister<IProbeService>(new ProbeService()),
                Is.False);
            Assert.That(registry.Unregister<IProbeService>(registered), Is.True);
            Assert.That(registry.TryGet<IProbeService>(out _), Is.False);
        }
    }
}
