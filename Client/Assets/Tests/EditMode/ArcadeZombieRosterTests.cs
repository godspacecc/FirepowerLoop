using System.IO;
using System.Linq;
using Game;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>只验证正式前期僵尸目录、动画与旧持械资源隔离，不创建对局或修改配置。</summary>
    public sealed class ArcadeZombieRosterTests
    {
        private Tables Load()=>new Tables(name=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",name+".bytes"))));
        [Test] public void FirstThreeThemesUseOnlyUnarmedZombieRoster()
        {
            var tables=Load();
            foreach(var arena in tables.TbArcadeArena.DataList.Where(a=>a.ArenaNumber<=3))
            foreach(var groupId in arena.WaveGroupIds)
            foreach(var member in tables.TbArcadeEnemyGroup.Get(groupId).Members)
            {
                Assert.That(member.EnemyId,Does.StartWith("enemy.arcade.zombie."));
                var enemy=tables.TbEnemy.Get(member.EnemyId);
                Assert.That(enemy.AttackKind,Is.EqualTo(global::EnemyAttackKind.Melee));
                Assert.That(enemy.WeaponId,Is.EqualTo("weapon.zombie-claw"));Assert.That(enemy.ProjectileConfigId,Is.Null.Or.Empty);
            }
        }
        [Test] public void LaterThemesAndOriginalArmedModelsRemainAvailable()
        {
            var tables=Load();
            Assert.That(tables.TbEnemy.Get("enemy.arcade.basic").PrefabName,Is.EqualTo("ArcadeEnemyBasic"));
            Assert.That(tables.TbEnemy.Get("enemy.arcade.archer").AttackKind,Is.EqualTo(global::EnemyAttackKind.Projectile));
            Assert.That(tables.TbEnemy.Get("enemy.arcade.brute").WeaponId,Is.EqualTo("weapon.greatsword"));
            Assert.That(tables.TbArcadeArena.DataList.Where(a=>a.ArenaNumber>=4).SelectMany(a=>a.WaveGroupIds)
                .SelectMany(id=>tables.TbArcadeEnemyGroup.Get(id).Members).Any(m=>m.EnemyId=="enemy.arcade.archer"),Is.True);
        }
        [Test] public void RuntimeCatalogAcceptsUnarmedZombiesWithoutGuaranteedWeaponDrop()
        {
            var provider = new LubanRuntimeConfigProvider(Load);
            Assert.That(provider.TryGetEnemy("enemy.arcade.zombie.shambler", out var enemy, out var error), Is.True, error);
            Assert.That(enemy.GuaranteedWeaponLootConfigId, Is.Null.Or.Empty);
        }
        [Test] public void ZombieGroupsPreserveOriginalBudgetsAndTuning()
        {
            var tables=Load();
            foreach(var group in tables.TbArcadeEnemyGroup.DataList.Where(g=>g.Id.StartsWith("arcade.group.zombie.")))
            {
                var original=tables.TbArcadeEnemyGroup.Get(group.Id.Replace("arcade.group.zombie.","arcade.group."));
                Assert.That(group.Members.Sum(m=>m.Count),Is.EqualTo(original.Members.Sum(m=>m.Count)),group.Id);
                Assert.That(group.MaxAlive,Is.EqualTo(original.MaxAlive),group.Id);
                Assert.That(group.SpawnIntervalSeconds,Is.EqualTo(original.SpawnIntervalSeconds),group.Id);
                Assert.That(group.HealthMultiplier,Is.EqualTo(original.HealthMultiplier),group.Id);
                Assert.That(group.DamageMultiplier,Is.EqualTo(original.DamageMultiplier),group.Id);
                Assert.That(group.SpeedMultiplier,Is.EqualTo(original.SpeedMultiplier),group.Id);
                Assert.That(group.ClearGold,Is.EqualTo(original.ClearGold),group.Id);
            }
        }
        [TestCase("Shambler")][TestCase("Gaunt")][TestCase("Bloated")]
        public void NewZombiePrefabHasNativeClawAnimationAndNoHeldWeapon(string kind)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombie"+kind+".prefab");Assert.That(source,Is.Not.Null);
            var instance=Object.Instantiate(source);
            try
            {
                var view=instance.GetComponent<EnemyView>();Assert.That(view,Is.Not.Null);Assert.That(view.Animator,Is.Not.Null);
                Assert.That(instance.GetComponent<CharacterWeaponPresenter>(),Is.Null);
                Assert.That(instance.GetComponentsInChildren<Renderer>(true).Any(r=>r.name.StartsWith("HeldWeapon")||r.name=="Weapon"||r.name=="ArcherBow"),Is.False);
                Assert.That(view.Animator.runtimeAnimatorController.animationClips.Any(c=>c.name.Contains("Idle_Attack")),Is.True);
                Assert.That(instance.GetComponent<EnemyPhysicalReaction>().IsReady,Is.True);
                Assert.That(instance.GetComponentInChildren<EnemyFlameFeedback>(true),Is.Not.Null);
                view.BeginPresentationUse();view.SetPresentationVariant(5);view.TickCombatFeedback(.1f);
                Assert.That(instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r=>r.enabled),Is.True,"眼睑、舌头和身体不能被当成互斥外观关闭");
                view.Animator.Rebind();view.Animator.Update(0);
                var arm=instance.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="UpperArm.L");
                var idle=arm.localRotation;view.PlayAttackAnimation();view.Animator.Update(.1f);view.Animator.Update(.25f);
                Assert.That(view.Animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"),Is.True);
                Assert.That(Quaternion.Angle(idle,arm.localRotation),Is.GreaterThan(10),"抓击必须实际驱动原生骨架");
                var feet=instance.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Foot.L"||t.name=="Foot.R").ToArray();
                var parents=feet.Select(t=>t.parent).ToArray();
                Assert.That(feet.Length,Is.EqualTo(2));
                Assert.That(view.TryPlayPhysicalDeath(Vector3.forward,false,out _),Is.True);
                view.TickCombatFeedback(.17f);view.LateTickCombatFeedback();
                Assert.That(instance.GetComponent<EnemyPhysicalReaction>().IsSimulating,Is.True);
                view.ResetPhysicalFeedback();
                Assert.That(view.TryPlayPhysicalDeath(Vector3.forward,true,out _),Is.True);
                Assert.That(feet.All(t=>t.parent.name.StartsWith("LowerLeg")),Is.True);
                view.ResetPhysicalFeedback();
                for(int i=0;i<feet.Length;i++) Assert.That(feet[i].parent,Is.EqualTo(parents[i]));
                Assert.That(instance.GetComponentsInChildren<Rigidbody>().All(b=>b.isKinematic&&!b.detectCollisions),Is.True);
                view.SetFlameRemaining(.6f);Assert.That(instance.GetComponentInChildren<EnemyFlameFeedback>(true).IsBurning,Is.True);
                view.ResetPhysicalFeedback();Assert.That(instance.GetComponentInChildren<EnemyFlameFeedback>(true).IsBurning,Is.False);
            }
            finally{Object.DestroyImmediate(instance);}
        }
    }
}
