using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rutin.GunShop.Tests.EditMode
{
    public sealed class DualHandPrototypeSceneTests
    {
        private const string ScenePath = "Assets/GunShop/Scenes/DualHandWorkbenchPrototype.unity";

        [Test]
        public void PrototypeScene_ContainsPlayableDualHandRigAndWorkbench()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Not.Null);

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var motors = roots.SelectMany(root =>
                    root.GetComponentsInChildren<PhysicsHandMotor>(true)).ToArray();

                Assert.That(motors, Has.Length.EqualTo(2));
                Assert.That(roots.SelectMany(root =>
                    root.GetComponentsInChildren<AssemblyDualHandInputSource>(true)), Is.Not.Empty);
                Assert.That(roots.SelectMany(root =>
                    root.GetComponentsInChildren<DualHandRigController>(true)), Is.Not.Empty);
                Assert.That(roots.SelectMany(root =>
                    root.GetComponentsInChildren<WorkbenchCameraController>(true)), Is.Not.Empty);
                Assert.That(roots.SelectMany(root =>
                    root.GetComponentsInChildren<HandArmVisualizer>(true)).Count(), Is.EqualTo(2));
                Assert.That(roots.Any(root => root.name == "Workbench"), Is.True);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
