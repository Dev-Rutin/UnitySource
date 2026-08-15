using System.IO;
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

        [Test]
        public void ProjectSettings_DoNotContainLegacyMisspelledName()
        {
            var settings = File.ReadAllText("ProjectSettings/ProjectSettings.asset");

            Assert.That(settings, Does.Not.Contain("GunShopSimulatior"));
        }
    }
}
