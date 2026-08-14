using NUnit.Framework;

namespace Rutin.GunShop.Tests.EditMode
{
    public sealed class ProjectIdentityTests
    {
        [Test]
        public void ProductName_MatchesUnityProjectName()
        {
            Assert.That(ProjectIdentity.ProductName, Is.EqualTo("GunShopSimulator"));
        }
    }
}
