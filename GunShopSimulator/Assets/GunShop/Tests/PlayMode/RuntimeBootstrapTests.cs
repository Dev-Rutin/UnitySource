using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Rutin.GunShop.Tests.PlayMode
{
    public sealed class RuntimeBootstrapTests
    {
        [UnityTest]
        public IEnumerator RuntimeAssembly_LoadsInPlayMode()
        {
            Assert.That(ProjectIdentity.ProductName, Is.EqualTo("GunShopSimulator"));
            yield return null;
        }
    }
}
