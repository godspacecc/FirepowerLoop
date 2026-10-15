using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class BatchThreeClosureTests
    {
        [Test]
        public void PlayerHealthDomainHasMovedIntoCombatModel()
        {
            var root = ProjectRoot();
            var source = AllProjectSource(root);
            Assert.That(source, Does.Not.Contain("Player" + "HealthModel"));
            Assert.That(source, Does.Not.Contain("Player" + "HealthChangedEvent"));
            Assert.That(source, Does.Not.Contain("Player" + "DiedEvent"));
            Assert.That(
                File.ReadAllText(Path.Combine(root, "Assets/Game/Scripts/Model/Combat/CombatModel.cs")),
                Does.Contain("another-day.player-health"));
        }

        [Test]
        public void CombatSystemDoesNotDependOnConcreteEnemyView()
        {
            var root = ProjectRoot();
            var source = File.ReadAllText(Path.Combine(
                root,
                "Assets/Game/Scripts/System/Combat/CombatSystem.cs"));

            Assert.That(source, Does.Not.Contain("EnemyView"));
            Assert.That(source, Does.Not.Contain("IEnemy" + "CombatPort"));
            Assert.That(source, Does.Not.Contain("IEnemyNavigationPort"));
            Assert.That(source, Does.Contain("UnitModel"));
        }

        [Test]
        public void EnemyViewNoLongerOwnsArchitectureRegistration()
        {
            var root = ProjectRoot();
            var source = File.ReadAllText(Path.Combine(
                root,
                "Assets/Game/Scripts/Controller/View/Map/EnemyView.cs"));

            Assert.That(source, Does.Not.Contain("IController"));
            Assert.That(source, Does.Not.Contain("GetArchitecture"));
            Assert.That(source, Does.Not.Contain("GetSystem<CombatSystem>"));
            Assert.That(source, Does.Not.Contain("GetSystem<EnemySystem>"));
        }

        [Test]
        public void EnemySystemDoesNotDependOnConcreteEnemyView()
        {
            var root = ProjectRoot();
            var source = File.ReadAllText(Path.Combine(
                root,
                "Assets/Game/Scripts/System/Combat/EnemySystem.cs"));

            Assert.That(source, Does.Not.Contain("EnemyView"));
            Assert.That(source, Does.Contain("IEnemyNavigationPort"));
        }

        [Test]
        public void CombatContractDoesNotCarryLootOrDeathPosition()
        {
            var root = ProjectRoot();
            var types = File.ReadAllText(Path.Combine(
                root,
                "Assets/Game/Scripts/Model/Combat/CombatTypes.cs"));
            var eventsSource = File.ReadAllText(Path.Combine(
                root,
                "Assets/Game/Scripts/System/Combat/CombatEvents.cs"));

            Assert.That(types, Does.Contain("CombatFaction"));
            Assert.That(types, Does.Contain("AttackDamage"));
            Assert.That(types, Does.Not.Contain("AttackPower"));
            Assert.That(types, Does.Contain("TargetUnitId"));
            Assert.That(types, Does.Not.Contain("TestResourceDrop"));
            Assert.That(eventsSource, Does.Contain("CombatDamageRequestedEvent"));
            Assert.That(eventsSource, Does.Contain("CombatDamageAppliedEvent"));
            Assert.That(eventsSource, Does.Contain("CombatHealthChangedEvent"));
            Assert.That(eventsSource, Does.Contain("CombatantDiedEvent"));
            Assert.That(eventsSource, Does.Not.Contain("Vector3"));
            Assert.That(eventsSource, Does.Not.Contain("TestResource" + "Amount"));
            Assert.That(eventsSource, Does.Not.Contain("Position"));
            Assert.That(eventsSource, Does.Contain("KillerUnitId"));
            Assert.That(
                ExtractStructSource(eventsSource, "CombatantDiedEvent"),
                Does.Not.Contain("CombatFaction"));
        }

        [Test]
        public void EnemyNavigationPortOnlyOwnsMovement()
        {
            var root = ProjectRoot();
            var source = File.ReadAllText(Path.Combine(
                root,
                "Assets/Game/Scripts/Controller/ViewCtrl/Enemy/EnemyRuntimePorts.cs"));

            Assert.That(source, Does.Not.Contain("AggroRange"));
            Assert.That(source, Does.Not.Contain("AttackRange"));
            Assert.That(source, Does.Not.Contain("TouchDamage"));
            Assert.That(source, Does.Not.Contain("AttackCooldownSeconds"));
            Assert.That(source, Does.Not.Contain("SetHealthDisplay"));
            Assert.That(source, Does.Not.Contain("PlayHitFeedback"));
            Assert.That(source, Does.Not.Contain("MarkDead"));
        }

        [Test]
        public void WorldControllersUseConfiguredMapSpawns()
        {
            var root = ProjectRoot();
            var mapContext = File.ReadAllText(Path.Combine(
                root,
                "Assets/Game/Scripts/Controller/View/Map/MapContext.cs"));
            var enemyCtrl = File.ReadAllText(Path.Combine(
                root,
                "Assets/Game/Scripts/Controller/Enemy/EnemyCtrl.cs"));
            var lootCtrl = File.ReadAllText(Path.Combine(
                root,
                "Assets/Game/Scripts/Controller/Loot/LootCtrl.cs"));
            var extractionCtrl = File.ReadAllText(Path.Combine(
                root,
                "Assets/Game/Scripts/Controller/Extraction/ExtractionCtrl.cs"));

            Assert.That(mapContext, Does.Not.Contain("EnemySpawns"));
            Assert.That(mapContext, Does.Not.Contain("LootSpawns"));
            Assert.That(mapContext, Does.Not.Contain("ExtractionPoints"));
            Assert.That(mapContext, Does.Not.Contain("extractionPoints"));
            Assert.That(enemyCtrl, Does.Contain("MapSpawnObjectType.Enemy"));
            Assert.That(enemyCtrl, Does.Not.Contain("MapContext.EnemySpawns"));
            Assert.That(lootCtrl, Does.Contain("MapSpawnObjectType.Loot"));
            Assert.That(extractionCtrl, Does.Contain("MapSpawnObjectType.Extraction"));
            Assert.That(extractionCtrl, Does.Not.Contain("Scene.ExtractionPoints"));
            Assert.That(enemyCtrl, Does.Not.Contain("GetComponentsInChildren<EnemyView"));
            Assert.That(lootCtrl, Does.Not.Contain("GetComponentsInChildren<ResourcePickupView"));
            Assert.That(extractionCtrl, Does.Not.Contain("GetComponentsInChildren<TaskExitView"));
        }

        [Test]
        public void LootCollectRangeUsesPlanDistance()
        {
            var root = ProjectRoot();
            var source = File.ReadAllText(Path.Combine(
                root,
                "Assets/Game/Scripts/System/Loot/LootSystem.cs"));

            Assert.That(source, Does.Contain("CollectRange = 1.25f"));
            Assert.That(source, Does.Not.Contain("CollectRange = 1.75f"));
        }

        [Test]
        public void LootDomainOwnsPickupLifecycle()
        {
            var root = ProjectRoot();
            Assert.That(
                File.Exists(Path.Combine(root, "Assets/Game/Scripts/System/Map/Resource" + "PickupPresentationSystem.cs")),
                Is.False);
            var lootSystem = File.ReadAllText(Path.Combine(
                root,
                "Assets/Game/Scripts/System/Loot/LootSystem.cs"));
            var pickupView = File.ReadAllText(Path.Combine(
                root,
                "Assets/Game/Scripts/Controller/View/Map/ResourcePickupView.cs"));

            Assert.That(lootSystem, Does.Not.Contain("Prefab"));
            Assert.That(lootSystem, Does.Not.Contain("Object.Instantiate"));
            Assert.That(lootSystem, Does.Contain("TryRegisterLoot"));
            Assert.That(lootSystem, Does.Contain("TryRemoveLoot"));
            Assert.That(lootSystem, Does.Contain("LootCreatedEvent"));
            Assert.That(lootSystem, Does.Not.Contain("CreateMapPlaced"));
            Assert.That(lootSystem, Does.Not.Contain("CreateDropped"));
            Assert.That(lootSystem, Does.Not.Contain("UnregisterLoot"));
            Assert.That(pickupView, Does.Not.Contain("IController"));
            Assert.That(pickupView, Does.Not.Contain("GetSystem<InventorySystem>"));
            Assert.That(pickupView, Does.Not.Contain("Resource" + "PickupPresentationSystem"));
        }

        [Test]
        public void ExtractionViewNoLongerOwnsReturnFlow()
        {
            var root = ProjectRoot();
            var source = File.ReadAllText(Path.Combine(
                root,
                "Assets/Game/Scripts/Controller/View/Map/TaskExitView.cs"));
            var systemSource = File.ReadAllText(Path.Combine(
                root,
                "Assets/Game/Scripts/System/Extraction/ExtractionSystem.cs"));
            var eventsSource = File.ReadAllText(Path.Combine(
                root,
                "Assets/Game/Scripts/System/Extraction/ExtractionEvents.cs"));

            Assert.That(source, Does.Not.Contain("IController"));
            Assert.That(source, Does.Not.Contain("GetArchitecture"));
            Assert.That(source, Does.Not.Contain("ReturnToBaseAsync"));
            Assert.That(source, Does.Not.Contain("RegisterEvent<ExpeditionChangedEvent>"));
            Assert.That(systemSource, Does.Not.Contain("CreateMapPlaced"));
            Assert.That(systemSource, Does.Contain("TryRegisterExtraction"));
            Assert.That(systemSource, Does.Contain("TryRemoveExtraction"));
            Assert.That(systemSource, Does.Contain("TrySetAvailable"));
            Assert.That(eventsSource, Does.Contain("ExtractionCreatedEvent"));
            Assert.That(eventsSource, Does.Contain("ExtractionAvailabilityChangedEvent"));
            Assert.That(eventsSource, Does.Contain("ExtractionReturnStartedEvent"));
        }

        private static string AllProjectSource(string root)
        {
            var scriptsRoot = Path.Combine(root, "Assets/Game/Scripts");
            var testsRoot = Path.Combine(root, "Assets/Tests/EditMode");
            return string.Join(
                "\n",
                Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
                    .Select(File.ReadAllText));
        }

        private static string ExtractStructSource(string source, string typeName)
        {
            var start = source.IndexOf("struct " + typeName, System.StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            var next = source.IndexOf("public readonly struct", start + 1, System.StringComparison.Ordinal);
            return next < 0
                ? source.Substring(start)
                : source.Substring(start, next - start);
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }
    }
}
