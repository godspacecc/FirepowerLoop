using System.Linq;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests
{
    /// <summary>使用正式基地和默认摆件验证木屋通行、围合、相机与室内姿态；不写玩家存档。</summary>
    public sealed class BaseCabinInteriorTests
    {
        private Scene scene;
        private MapContext context;
        private CharacterController player;
        private Transform cabin;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/BaseMap.unity", OpenSceneMode.Additive);
            context = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MapContext>(true)).Single();
            context.SetMapContentActive(true);
            cabin = context.MapContentRoot.transform.Find("BaseCampDecoration/LakesideArt/湖畔木屋");
            var provider = new LubanRuntimeConfigProvider(() => new Tables(n => new Luban.ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/" + n + ".bytes").bytes)));
            Assert.That(provider.TryGetBaseCampLayout(out var layout, out var error), Is.True, error);
            foreach (var row in layout)
            {
                Assert.That(provider.TryGetTempBuilding(row.BuildingId, out var definition, out error), Is.True, error);
                var prop = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + definition.PrefabAddress + ".prefab"), context.DynamicObjectRoot);
                prop.transform.SetPositionAndRotation(row.Position, Quaternion.Euler(0, row.Yaw, 0));
                prop.transform.localScale *= row.Scale;
                Assert.That(prop.GetComponent<TemporaryBuildingView>().SetCommitted(definition.BlocksNavigation, out error), Is.True, error);
            }
            var go = new GameObject("CabinPlayerProbe");
            SceneManager.MoveGameObjectToScene(go, scene);
            player = go.AddComponent<CharacterController>();
            var formal = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab").GetComponent<CharacterController>();
            player.height = formal.height; player.radius = formal.radius; player.center = formal.center;
            player.stepOffset = formal.stepOffset; player.skinWidth = formal.skinWidth;
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown() { if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true); }

        private void Place(Vector3 position)
        {
            player.enabled = false; player.transform.position = position; player.enabled = true;
            Physics.SyncTransforms();
        }

        private void Walk(Vector3 destination)
        {
            for (int i = 0; i < 350; i++)
            {
                var delta = destination - player.transform.position; delta.y = 0;
                if (delta.magnitude < .06f) break;
                player.Move(Vector3.ClampMagnitude(delta, .06f) + Vector3.down * .025f);
            }
            Assert.That(Vector2.Distance(new Vector2(player.transform.position.x, player.transform.position.z), new Vector2(destination.x, destination.z)), Is.LessThan(.12f), "通路被阻挡：" + player.transform.position + " → " + destination);
        }

        [Test]
        public void DefaultCampAllowsWalkingUpStepsThroughDoorAndBack()
        {
            Place(new Vector3(-11.2f, .05f, 10.8f));
            Walk(new Vector3(-11.2f, 0, 16));
            Walk(new Vector3(-9, 0, 16));
            Walk(new Vector3(-9, 0, 21));
            Assert.That(player.transform.position.y, Is.InRange(.85f, 1.0f), "室内脚底应落在正式地板上");
            Walk(new Vector3(-9, 0, 16));
            Walk(new Vector3(-11.2f, 0, 16));
            Walk(new Vector3(-11.2f, 0, 10.8f));
            Assert.That(player.transform.position.y, Is.InRange(-.05f, .12f));
        }

        [Test]
        public void InteriorSavePoseRestoresOntoFloorAndWallsBlockExit()
        {
            var source = new PlayerBasePoseModel();
            Assert.That(source.TrySet(new Vector3(-9, .92f, 22), 180, out _, out var error), Is.True, error);
            var restored = new PlayerBasePoseModel(); restored.Restore(source.Capture());
            Assert.That(restored.TryGet(out var pose), Is.True);
            Place(context.transform.TransformPoint(pose.LocalPosition));
            for (int i = 0; i < 50; i++) player.Move(Vector3.down * .03f);
            Assert.That(player.transform.position.y, Is.InRange(.85f, 1.0f));
            foreach (var direction in new[] { Vector3.left, Vector3.right, Vector3.forward })
            {
                Place(new Vector3(-9, .92f, 22));
                for (int i = 0; i < 160; i++) player.Move(direction * .1f + Vector3.down * .025f);
                Assert.That(player.transform.position.x, Is.InRange(-15.3f, -2.7f));
                Assert.That(player.transform.position.z, Is.LessThan(26.6f));
                Assert.That(player.transform.position.y, Is.GreaterThan(.8f));
            }
        }

        [Test]
        public void DoorIsOpenInRenderedMeshAndRoofRemainsOpaque()
        {
            player.enabled = false;
            // 临时将正式可见网格参与射线，防止只有碰撞门洞、画面仍封闭。
            foreach (var mesh in cabin.GetComponentsInChildren<MeshFilter>())
                mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
            Physics.SyncTransforms();
            var hits = Physics.RaycastAll(new Vector3(-9, 2, 16), Vector3.forward, 4, ~0, QueryTriggerInteraction.Ignore);
            Assert.That(hits.Where(h => h.transform.IsChildOf(cabin)), Is.Empty, "正式网格或碰撞仍封住门洞");
            Assert.That(cabin.GetComponentsInChildren<CameraOccluderView>(true), Is.Empty);
            Assert.That(cabin.GetComponentsInChildren<Renderer>().Where(r => r.name == "Roof").All(r => r.enabled && r.sharedMaterials.All(m => m.renderQueue < 3000)), Is.True);
        }

        [Test]
        public void FrontWindowsHaveClearViewsThroughWallsAndBlockingTransparentGlass()
        {
            player.enabled = false;
            // 对正式可见墙网格逐格射线：只改碰撞或贴一个内侧窗户都不能通过此检查。
            var body = cabin.GetComponentsInChildren<MeshFilter>().Single(m => m.name == "Body");
            var wallMesh = body.gameObject.AddComponent<MeshCollider>();
            wallMesh.sharedMesh = body.sharedMesh;
            Physics.SyncTransforms();
            foreach (float x in new[] { -2.75f, 2.75f })
                foreach (float dx in new[] { -.30f, .30f })
                    foreach (float dz in new[] { -.25f, .25f })
                    {
                        var origin = cabin.TransformPoint(new Vector3(-x + dx, 2.18f + dz, 2.60f));
                        var ray = new Ray(origin, cabin.TransformDirection(Vector3.forward));
                        Assert.That(wallMesh.Raycast(ray, out _, 1.4f), Is.False, "内外墙或窗底板封住视线：" + origin);
                        var hits = Physics.RaycastAll(ray, 1.4f, ~0, QueryTriggerInteraction.Ignore)
                            .Where(h => h.transform.IsChildOf(cabin)).ToArray();
                        Assert.That(hits.Length, Is.EqualTo(1), "窗洞仅由实体玻璃阻挡");
                        Assert.That(hits[0].transform.name, Does.StartWith("窗玻璃"));
                    }
            var panes = cabin.GetComponentsInChildren<Renderer>().Single(r => r.name == "Glass");
            Assert.That(panes.sharedMaterials.All(m => m.GetFloat("_Surface") == 1 &&
                m.GetColor("_BaseColor").a < .3f && m.renderQueue >= 3000), Is.True, "窗玻璃必须能透视室外");
        }

        [Test]
        public void InteriorFloorIsNotCoveredByLandscape()
        {
            player.enabled = false;
            var landscape = context.MapContentRoot.transform.Find("BaseCampDecoration/LakesideArt/湖岸与雪山");
            var mesh = landscape.GetComponentsInChildren<MeshFilter>().Single(m => m.name == "LakesideTerrain");
            mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
            Physics.SyncTransforms();
            for (float x = -14; x <= -4; x += 2)
                for (float z = 20; z <= 25; z += 1)
                {
                    Assert.That(Physics.Raycast(new Vector3(x, 4, z), Vector3.down, out var hit, 4, ~0, QueryTriggerInteraction.Ignore), Is.True);
                    Assert.That(hit.point.y, Is.InRange(.87f, .90f), "地形不能穿进室内：" + hit.point);
                }
        }

        [Test]
        public void ShoulderCameraRetractsAtInteriorBackWall()
        {
            Place(new Vector3(-9, .92f, 25.6f));
            var cameraGo = new GameObject("CabinCameraProbe", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraGo, scene);
            var camera = cameraGo.GetComponent<Camera>();
            var top = AssetDatabase.LoadAssetAtPath<TopDownCameraProfile>("Assets/Game/Config/DefaultCameraProfile.asset");
            var shoulder = AssetDatabase.LoadAssetAtPath<ShoulderCameraProfile>("Assets/Game/Config/ShoulderCameraProfile.asset");
            var ctrl = new PlayerCameraCtrl(camera, player.transform, top, shoulder);
            try
            {
                Assert.That(ctrl.SupportsMode(PlayerCameraMode.Shoulder, out var error), Is.True, error);
                ctrl.ApplyMode(PlayerCameraMode.Shoulder, 180, 0);
                ctrl.LateUpdate(PlayerCameraMode.Shoulder, 180, 0);
                Assert.That(camera.transform.position.z, Is.LessThan(26.5f), "镜头不能穿出后墙");
                Assert.That(camera.transform.position.z, Is.GreaterThan(25.6f), "镜头应位于玩家身后");
                ctrl.ApplyMode(PlayerCameraMode.FirstPerson, 0, 0);
                Assert.That(camera.transform.position.y, Is.EqualTo(player.transform.position.y + 1.6f).Within(.02f));
            }
            finally { ctrl.Dispose(); }
        }
    }
}
