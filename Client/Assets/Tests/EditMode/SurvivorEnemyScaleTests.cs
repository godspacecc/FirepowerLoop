using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>共用角色临时实例：体型重复投影不累乘，回池恢复源比例。</summary>
    public sealed class SurvivorEnemyScaleTests
    {
        [Test] public void ScaledEnemyRestoresOriginalSizeBeforePooling()
        {
            var method=typeof(EnemyCtrl).GetMethod("SetPresentationScale");Assert.That(method,Is.Not.Null);
            var root=new GameObject("EnemyScaleTest");root.SetActive(false);
            try
            {
                var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieBloated.prefab"),root.transform);
                var original=go.transform.localScale;var controller=new EnemyCtrl(new Game());
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                var type=typeof(EnemyCtrl).GetNestedType("ArcadeEnemyPresentationEntry",BindingFlags.NonPublic);
                var entry=Activator.CreateInstance(type,1,"enemy.arcade.zombie.bloated",go.GetComponent<EnemyView>(),100,Vector3.zero,0,false);
                ((IDictionary)typeof(EnemyCtrl).GetField("arcadeEnemies",flags).GetValue(controller)).Add(1,entry);
                var scale=new Vector3(1.5f,1.2f,1.4f);method.Invoke(controller,new object[]{1,scale});method.Invoke(controller,new object[]{1,scale});
                Assert.That(go.transform.localScale,Is.EqualTo(Vector3.Scale(original,scale)));
                typeof(EnemyCtrl).GetMethod("RecycleArcadeEnemy",flags).Invoke(controller,new[]{entry});
                Assert.That(go.transform.localScale,Is.EqualTo(original));
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
