using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rutin.GunShop.Editor
{
    public static class DualHandPrototypeSceneBuilder
    {
        private const string SceneFolder = "Assets/GunShop/Scenes";
        private const string MaterialFolder = SceneFolder + "/Materials";
        private const string ScenePath = SceneFolder + "/DualHandWorkbenchPrototype.unity";

        [MenuItem("Gun Shop/Build Dual Hand Prototype Scene")]
        public static void Build()
        {
            EnsureAssetFolder(MaterialFolder);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "DualHandWorkbenchPrototype";

            var neutral = GetOrCreateMaterial("Neutral", new Color(0.22f, 0.25f, 0.3f));
            var workbench = GetOrCreateMaterial("Workbench", new Color(0.28f, 0.16f, 0.08f));
            var leftHandMaterial = GetOrCreateMaterial("LeftHand", new Color(0.15f, 0.48f, 0.95f));
            var rightHandMaterial = GetOrCreateMaterial("RightHand", new Color(0.95f, 0.32f, 0.18f));
            var metal = GetOrCreateMaterial("Metal", new Color(0.5f, 0.54f, 0.6f));

            CreateEnvironment(neutral, workbench, metal);
            CreateLighting();
            CreateCameraRig();
            CreateDualHandRig(leftHandMaterial, rightHandMaterial);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Dual hand prototype scene saved to {ScenePath}");
        }

        public static void BuildFromCommandLine()
        {
            Build();
        }

        private static void CreateEnvironment(Material neutral, Material workbench, Material metal)
        {
            CreatePrimitive(
                PrimitiveType.Cube,
                "Floor",
                new Vector3(0f, -0.08f, 0.8f),
                new Vector3(6f, 0.15f, 6f),
                neutral);

            CreatePrimitive(
                PrimitiveType.Cube,
                "Workbench",
                new Vector3(0f, 0.58f, 0.85f),
                new Vector3(3f, 0.18f, 1.8f),
                workbench);

            CreatePrimitive(
                PrimitiveType.Cube,
                "WorkbenchLeftLeg",
                new Vector3(-1.15f, 0.24f, 0.85f),
                new Vector3(0.16f, 0.7f, 1.35f),
                workbench);

            CreatePrimitive(
                PrimitiveType.Cube,
                "WorkbenchRightLeg",
                new Vector3(1.15f, 0.24f, 0.85f),
                new Vector3(0.16f, 0.7f, 1.35f),
                workbench);

            CreatePrimitive(
                PrimitiveType.Cube,
                "AssemblyPartCube",
                new Vector3(-0.25f, 0.78f, 1.05f),
                new Vector3(0.25f, 0.25f, 0.25f),
                metal);

            var cylinder = CreatePrimitive(
                PrimitiveType.Cylinder,
                "AssemblyPartCylinder",
                new Vector3(0.35f, 0.81f, 1.05f),
                new Vector3(0.16f, 0.22f, 0.16f),
                metal);
            cylinder.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
        }

        private static void CreateLighting()
        {
            var directional = new GameObject("Directional Light");
            directional.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            var directionalLight = directional.AddComponent<Light>();
            directionalLight.type = LightType.Directional;
            directionalLight.intensity = 1.1f;

            var workLightObject = new GameObject("Workbench Light");
            workLightObject.transform.position = new Vector3(0f, 2.8f, 0.8f);
            var workLight = workLightObject.AddComponent<Light>();
            workLight.type = LightType.Point;
            workLight.range = 5f;
            workLight.intensity = 4f;
        }

        private static void CreateCameraRig()
        {
            var yawPivot = new GameObject("WorkbenchCameraYaw").transform;
            yawPivot.position = new Vector3(0f, 1.65f, -2.2f);

            var pitchPivot = new GameObject("WorkbenchCameraPitch").transform;
            pitchPivot.SetParent(yawPivot, false);
            pitchPivot.localRotation = Quaternion.Euler(8f, 0f, 0f);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(pitchPivot, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.05f;
            cameraObject.AddComponent<AudioListener>();

            var controller = yawPivot.gameObject.AddComponent<WorkbenchCameraController>();
            controller.Configure(yawPivot, pitchPivot);
        }

        private static void CreateDualHandRig(Material leftMaterial, Material rightMaterial)
        {
            var rig = new GameObject("DualHandRig");
            var workspaceOrigin = new GameObject("WorkspaceOrigin").transform;
            workspaceOrigin.SetParent(rig.transform, false);
            workspaceOrigin.position = new Vector3(0f, 1.25f, 0.85f);

            var input = rig.AddComponent<AssemblyDualHandInputSource>();

            var leftShoulder = CreateShoulder(
                "LeftShoulder",
                new Vector3(-0.82f, 1.25f, -0.1f),
                leftMaterial,
                rig.transform);
            var rightShoulder = CreateShoulder(
                "RightShoulder",
                new Vector3(0.82f, 1.25f, -0.1f),
                rightMaterial,
                rig.transform);

            var leftHand = CreateHand(
                "LeftHand",
                new Vector3(-0.42f, 1.25f, 0.72f),
                leftMaterial,
                workspaceOrigin);
            var rightHand = CreateHand(
                "RightHand",
                new Vector3(0.42f, 1.25f, 0.72f),
                rightMaterial,
                workspaceOrigin);

            CreateArm("LeftArm", leftShoulder, leftHand.transform, leftMaterial);
            CreateArm("RightArm", rightShoulder, rightHand.transform, rightMaterial);

            var controller = rig.AddComponent<DualHandRigController>();
            controller.Configure(input, leftHand, rightHand, workspaceOrigin);
        }

        private static Transform CreateShoulder(
            string name,
            Vector3 position,
            Material material,
            Transform parent)
        {
            var shoulder = CreatePrimitive(
                PrimitiveType.Sphere,
                name,
                position,
                Vector3.one * 0.16f,
                material);
            shoulder.transform.SetParent(parent, true);
            Object.DestroyImmediate(shoulder.GetComponent<Collider>());
            return shoulder.transform;
        }

        private static PhysicsHandMotor CreateHand(
            string name,
            Vector3 position,
            Material material,
            Transform workspaceOrigin)
        {
            var hand = CreatePrimitive(
                PrimitiveType.Cube,
                name,
                position,
                new Vector3(0.24f, 0.14f, 0.34f),
                material);

            var body = hand.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var motor = hand.AddComponent<PhysicsHandMotor>();
            motor.Configure(
                workspaceOrigin,
                1.35f,
                120f,
                35f,
                4f,
                10f);
            return motor;
        }

        private static void CreateArm(
            string name,
            Transform shoulder,
            Transform hand,
            Material material)
        {
            var arm = CreatePrimitive(
                PrimitiveType.Cylinder,
                name,
                Vector3.zero,
                Vector3.one,
                material);
            Object.DestroyImmediate(arm.GetComponent<Collider>());
            var visualizer = arm.AddComponent<HandArmVisualizer>();
            visualizer.Configure(shoulder, hand, 0.075f);
        }

        private static GameObject CreatePrimitive(
            PrimitiveType type,
            string name,
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            var gameObject = GameObject.CreatePrimitive(type);
            gameObject.name = name;
            gameObject.transform.position = position;
            gameObject.transform.localScale = scale;
            gameObject.GetComponent<Renderer>().sharedMaterial = material;
            return gameObject;
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            var path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader)
                {
                    name = name
                };
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureAssetFolder(string path)
        {
            var current = "Assets";
            foreach (var segment in path.Split('/').Skip(1))
            {
                var next = $"{current}/{segment}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segment);
                }

                current = next;
            }
        }

        private static void AddSceneToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.All(entry => entry.path != ScenePath))
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }
    }
}
