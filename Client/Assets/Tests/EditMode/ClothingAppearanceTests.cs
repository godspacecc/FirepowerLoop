using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>验证正式角色部位与第一人称袖口，使用真实资产，不创建或修复产品文件。</summary>
    public sealed class ClothingAppearanceTests
    {
        [TestCase("")]
        [TestCase("Top_RedHoodie")]
        [TestCase("Top_OliveUtility")]
        public void FiveNewPartsSwitchIndependentlyAndGlovesReachFirstPerson(string top)
        {
            var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            var cameraObject=new GameObject("ClothingCamera",typeof(Camera));PlayerCameraCtrl camera=null;
            try
            {
                var view=player.GetComponent<PlayerView>();view.Animator.Rebind();view.Animator.Update(.1f);
                var appearance=view.ClothingAppearance;
                Assert.That(appearance.Apply(top,"Pants_TanCargo","Head_OliveCap","Hands_WorkGloves","Feet_BrownBoots","Back_CanvasPack","Armor_UtilityVest"),Is.True);
                var holder=view.Animator.transform.Find("ClothingParts");
                foreach(var name in new[]{"Head_OliveCap","Hands_WorkGloves","Feet_BrownBoots","Back_CanvasPack","Armor_UtilityVest"})
                {
                    var part=holder.Find(name);Assert.That(part.gameObject.activeSelf,Is.True,name);
                    var mesh=new Mesh();try{part.GetComponent<SkinnedMeshRenderer>().BakeMesh(mesh);Assert.That(mesh.bounds.size.magnitude,Is.InRange(.01f,3f),name);}finally{Object.DestroyImmediate(mesh);}
                }
                Assert.That(holder.Find("Base_Hands").gameObject.activeSelf,Is.False);
                Assert.That(holder.Find("Base_Feet").gameObject.activeSelf,Is.False);
                Assert.That(holder.Find("Base_HeadCover").gameObject.activeSelf,Is.False);
                var gloved=appearance.CurrentSleeves;Assert.That(gloved.name,Does.StartWith("GloveSleeves_"));
                Assert.That(appearance.Apply(top,"Pants_TanCargo","unknown"),Is.False);
                Assert.That(appearance.CurrentSleeves,Is.SameAs(gloved));Assert.That(holder.Find("Back_CanvasPack").gameObject.activeSelf,Is.True);
                camera=new PlayerCameraCtrl(cameraObject.GetComponent<Camera>(),player.transform,view.CameraProfile,view.ShoulderCameraProfile);
                camera.ApplyMode(PlayerCameraMode.FirstPerson,0,0);
                var skin=cameraObject.transform.Find("PlayerFirstPersonRig").GetComponentInChildren<SkinnedMeshRenderer>();Assert.That(skin.sharedMesh,Is.SameAs(gloved));
                Assert.That(appearance.Apply(top,"Pants_TanCargo"),Is.True);camera.LateUpdate(PlayerCameraMode.FirstPerson,0,0);
                Assert.That(skin.sharedMesh,Is.SameAs(appearance.CurrentSleeves));Assert.That(skin.sharedMesh,Is.Not.SameAs(gloved));
                Assert.That(holder.Find("Base_Hands").gameObject.activeSelf,Is.True);Assert.That(holder.Find("Base_Feet").gameObject.activeSelf,Is.True);
                Assert.That(holder.Find("Base_HeadCover").gameObject.activeSelf,Is.True);
                Assert.That(holder.Find("Pants_TanCargo").gameObject.activeSelf,Is.True);
            }
            finally{camera?.Dispose();Object.DestroyImmediate(player);Object.DestroyImmediate(cameraObject);}
        }
        [TestCase("", "")]
        [TestCase("Top_RedHoodie", "Pants_BlueDenim")]
        [TestCase("Top_OliveUtility", "Pants_TanCargo")]
        public void ClothingChangesVisiblePartsAndFirstPersonSleeves(string top,string pants)
        {
            var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            var cameraObject=new GameObject("ClothingCamera",typeof(Camera));PlayerCameraCtrl camera=null;
            try
            {
                var view=player.GetComponent<PlayerView>();view.Animator.Rebind();view.Animator.Update(.1f);
                Assert.That(view.ClothingAppearance.Apply(top,pants),Is.True);
                var expected=view.ClothingAppearance.CurrentSleeves;Assert.That(expected,Is.Not.Null);
                Assert.That(view.ClothingAppearance.Apply("unknown",pants),Is.False);Assert.That(view.ClothingAppearance.CurrentSleeves,Is.SameAs(expected));
                var holder=view.Animator.transform.Find("ClothingParts");
                Assert.That(holder.Find(top==""?"Base_Torso":top).gameObject.activeSelf,Is.True);
                Assert.That(holder.Find(pants==""?"Base_Legs":pants).gameObject.activeSelf,Is.True);
                camera=new PlayerCameraCtrl(cameraObject.GetComponent<Camera>(),player.transform,view.CameraProfile,view.ShoulderCameraProfile);
                camera.ApplyMode(PlayerCameraMode.FirstPerson,0,0);
                var skin=cameraObject.transform.Find("PlayerFirstPersonRig").GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(skin.sharedMesh,Is.SameAs(expected));
                view.ClothingAppearance.Apply("Top_OliveUtility",pants);camera.LateUpdate(PlayerCameraMode.FirstPerson,0,0);
                Assert.That(skin.sharedMesh,Is.SameAs(view.ClothingAppearance.CurrentSleeves));
                var mesh=new Mesh();try{skin.BakeMesh(mesh);Assert.That(mesh.bounds.size.magnitude,Is.InRange(.1f,5f));}finally{Object.DestroyImmediate(mesh);}
            }
            finally{camera?.Dispose();Object.DestroyImmediate(player);Object.DestroyImmediate(cameraObject);}
        }
    }
}
