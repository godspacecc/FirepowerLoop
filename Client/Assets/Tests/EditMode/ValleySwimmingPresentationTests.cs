using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class ValleySwimmingPresentationTests
    {
        [Test] public void SwimmingPoseLeansHumanoidWithoutMovingRootAndRestoresOnLand()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            Assert.That(prefab,Is.Not.Null);
            var root=Object.Instantiate(prefab);
            try
            {
                var view=root.GetComponent<PlayerView>();var animator=view.Animator;
                Assert.That(animator.isHuman,Is.True);animator.Rebind();animator.Update(0);
                var position=root.transform.position;var rotation=root.transform.rotation;
                var handler=new HumanPoseHandler(animator.avatar,animator.transform);var before=new HumanPose();handler.GetHumanPose(ref before);
                var method=typeof(PlayerView).GetMethod("SetSwimmingPresentation");Assert.That(method,Is.Not.Null,"尚无游泳表现");
                method.Invoke(view,new object[]{true});
                var apply=typeof(PlayerView).GetMethod("ApplySwimmingPresentation",BindingFlags.Instance|BindingFlags.NonPublic);
                apply.Invoke(view,new object[]{.1f});var swimming=new HumanPose();handler.GetHumanPose(ref swimming);
                Assert.That(Quaternion.Angle(before.bodyRotation,swimming.bodyRotation),Is.GreaterThan(40));
                Assert.That(root.transform.position,Is.EqualTo(position));Assert.That(root.transform.rotation,Is.EqualTo(rotation));
                method.Invoke(view,new object[]{false});var after=new HumanPose();handler.GetHumanPose(ref after);
                Assert.That(Quaternion.Angle(before.bodyRotation,after.bodyRotation),Is.LessThan(1));
                handler.Dispose();
            }
            finally{Object.DestroyImmediate(root);}
        }
    }
}
