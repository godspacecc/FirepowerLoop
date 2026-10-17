using System;
using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>模板句柄由当前场地唯一持有；代次及根身份同时阻止离场后的晚回调提交。</summary>
        private ObstacleVisualLoadState contentLoad;
        private Transform contentTemplates;
        private GameObject contentEnvironment;
        private int contentGeneration, contentArena;
        private bool contentLoadFailed;
        private string contentEnvironmentIdentity;
        /// <summary>最近权威快照仅供异步加载后重绘；不计时、不领取、不保存。</summary>
        private ArcadeTreasureRoomParameters contentTreasure;
        private bool contentInRoom, contentDiscovered, contentOpen;
        private int[] contentRemaining = Array.Empty<int>(), contentDestinations = Array.Empty<int>();
        private float contentExitHeight;
        private bool contentVictory;
        private IReadOnlyList<ArcadeHazardParameters> contentHazards = Array.Empty<ArcadeHazardParameters>();
        private double contentHazardElapsed;

        private async UniTaskVoid LoadContentAsync(int arena)
        {
            contentLoadFailed = false;
            contentArena = arena;
            int generation = ++contentGeneration;
            var expectedRoot = root;
            var state = new ObstacleVisualLoadState(); contentLoad = state;
            var completion = new UniTaskCompletionSource<GameObject>();
            try
            {
                state.Handle = ResourceLoader.LoadAssetAsync<GameObject>(ObstacleVisualAddress,p=>completion.TrySetResult(p));
                var prefab = await completion.Task;
                if(state.Released || generation != contentGeneration || root == null || root != expectedRoot) return;
                var candidate = prefab != null ? prefab.transform.Find("Content") : null;
                string[] required={"Babylon","Forest","TreasureRoom","RouteGate","TreasureEntrance","GoldBar","Gem","ElectricTower"};
                if(candidate==null || required.Any(n=>candidate.Find(n)==null) || candidate.GetComponentsInChildren<Collider>(true).Length>0)
                    throw new InvalidOperationException("前两场 Content 模板缺失或携带碰撞体");
                // 命中反馈属于所有竞技场；后续场地不启用前三场的环境、出口或宝藏覆盖。
                bulletImpactTemplate = candidate.Find("BulletImpact")?.GetComponent<ParticleSystem>();
                if (bulletImpactTemplate == null) throw new InvalidOperationException("公共枪弹命中模板缺失");
                projectileBlastTemplate = candidate.Find("ProjectileBlast");
                if (projectileBlastTemplate == null) throw new InvalidOperationException("公共投射物爆炸模板缺失");
                contentTemplates=candidate;
                SyncContentEnvironment();
                treasureVisualIdentity=null; exitVisualIdentity=null;
                SetTreasureRoom(contentTreasure,contentInRoom,contentDiscovered,contentOpen,contentRemaining);
                SetExits(contentDestinations,contentExitHeight,contentVictory);
                hazardPresentation?.Dispose(); hazardPresentation=null;
                SetHazards(contentHazards,contentHazardElapsed);
            }
            catch(Exception e)
            {
                if(!state.Released) Log.Error("[ArcadeCombatViewController] 前两场内容加载失败："+e.Message);
                if(generation==contentGeneration)
                {
                    contentLoadFailed = true;
                    ClearTreasureVisuals(); foreach(var go in exitVisuals) DestroyObject(go); exitVisuals.Clear();
                    DestroyObject(contentEnvironment);contentEnvironment=null;contentTemplates=null;
                    bulletImpactTemplate=null;
                    projectileBlastTemplate=null;
                    hazardPresentation?.Dispose();hazardPresentation=null;
                    contentEnvironmentIdentity=null; RestoreIslandBaseArena();
                }
                ReleaseObstacleVisualLoad(state);if(ReferenceEquals(contentLoad,state))contentLoad=null;
            }
        }

        /// <summary>区域身份变化才替换环境；原场地碰撞保留，宝藏房仅覆盖可见地面。</summary>
        private void SyncContentEnvironment()
        {
            if(slayerMartRegionRoot!=null){HideIslandBaseArena();if(contentEnvironment!=null)contentEnvironment.SetActive(false);return;}
            if(contentTemplates==null || root==null)return;
            if (roundLayout != null && contentArena > 1)
            {
                if (contentEnvironment != null) contentEnvironment.SetActive(contentInRoom);
                if (contentInRoom && contentEnvironment == null)
                    contentEnvironment = CreateContent("TreasureRoom", Vector3.zero);
                HideIslandBaseArena();
                SyncRoundContentEnvironment();
                return;
            }
            string name=contentInRoom?"TreasureRoom":contentArena==2?"Babylon":contentArena==3?"Forest":null;
            if(contentEnvironmentIdentity==name)return;
            if(contentEnvironment!=null)contentEnvironment.SetActive(false);
            DestroyObject(contentEnvironment);contentEnvironment=null;contentEnvironmentIdentity=name;
            if(name!=null){contentEnvironment=CreateContent(name,Vector3.zero);HideIslandBaseArena();}
            else if(island==null)RestoreIslandBaseArena();
            if(island!=null)island.SetActive(!contentInRoom);
            SyncRoundContentEnvironment();
        }
        private GameObject CreateContent(string name,Vector3 position)
        {
            var template=contentTemplates?.Find(name);
            if(template==null)throw new InvalidOperationException("前两场表现模板未加载："+name);
            var go=UnityEngine.Object.Instantiate(template.gameObject,root.transform,false);
            go.transform.localPosition=position;go.SetActive(true);return go;
        }
        private static void SetContentLabel(GameObject go,string key)
        {
            var text=go.GetComponentInChildren<TextMeshPro>(true);
            if(text==null)throw new InvalidOperationException("出口缺少正式世界文字模板");
            text.text=Loc.T(key);
        }
        /// <summary>名称由实际场地配置身份绑定；4/15 仅描述现有蛮兽 Boss 与混合敌群，不实现后续场地。</summary>
        private static string DestinationKey(int number) => number >= 1 && number <= 20
            ? "ui.arcade.theme." + number : "ui.arcade.destination.next";
        private string RoundDestinationKey(int number) => roundLayout == null || contentArena == 1
            ? ArcadeIslandLayout.GetNameKey(number) : "ui.arcade." + roundLayout.ThemeKey + ".round." + number;
        private void ReleaseContent()
        {
            contentGeneration++;
            bulletImpactTemplate=null;
            projectileBlastTemplate=null;
            DestroyObject(contentEnvironment);contentEnvironment=null;contentTemplates=null;contentEnvironmentIdentity=null;
            ReleaseObstacleVisualLoad(contentLoad);contentLoad=null;
            RestoreIslandBaseArena();
            contentTreasure=null;contentInRoom=contentDiscovered=contentOpen=contentVictory=false;
            contentRemaining=contentDestinations=Array.Empty<int>();contentHazards=Array.Empty<ArcadeHazardParameters>();
        }
    }
}
