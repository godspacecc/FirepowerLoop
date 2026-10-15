using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;
namespace Game
{
    /// <summary>服务器弹体的纯表现端口；长整型身份与离线、街机弹体隔离，不参与命中或回收。</summary>
    public interface IOnlineWorldProjectilePresentationPort:IUtility
    {
        bool TrySetProjectile(long runtimeId,long mapInstanceId,string prefabAddress,Vector3 position,Vector3 direction,out string error);
        void RemoveProjectile(long runtimeId);
        void ClearOnlineProjectiles();
    }
    public sealed partial class ProjectileCtrl
    {
        private readonly Dictionary<long,ProjectileView> onlineViews=new Dictionary<long,ProjectileView>();
        private string candidateIndexJson,candidateIndexMapId,onlineIndexMapId;
        /// <summary>候选索引仅暂存文本，提交前不改活动目录；取消或迟到回调都由本次调用释放 Handle。</summary>
        private async UniTask<MapControllerResult> PrepareOnlineIndex(MapPreparationContext context,CancellationToken token,int preparation)
        {
            ResourceLoadHandle handle=default;
            try
            {
                var completion=new UniTaskCompletionSource<TextAsset>();
                handle=ResourceLoader.LoadAssetAsync<TextAsset>(context.MapId+".json",asset=>completion.TrySetResult(asset));
                var asset=await completion.Task.AttachExternalCancellation(token);
                token.ThrowIfCancellationRequested();
                if(preparation!=preparationGeneration||released)return MapControllerResult.Fail("联机弓箭准备已过期");
                var index=asset==null?null:JsonUtility.FromJson<WorldProjectileCollisionIndex>(asset.text);
                if(index==null||index.Version!=WorldProjectileCollisionData.CurrentVersion||index.MapId!=context.MapId||!WorldProjectileCollisionValidation.IsContentHash(index.ContentHash)||index.ContentHash!=context.OnlineWorld.CollisionContentHash)
                    return MapControllerResult.Fail("联机弓箭碰撞索引与服务器版本不一致");
                candidateIndexJson=asset.text;candidateIndexMapId=context.MapId;return MapControllerResult.Ok();
            }
            catch(OperationCanceledException){return MapControllerResult.Fail("联机弓箭准备已取消");}
            catch(Exception e){return MapControllerResult.Fail("联机弓箭索引读取失败："+e.Message);}
            finally{if(handle.IsValid)ResourceLoader.Release(handle);}
        }
        public bool TrySetProjectile(long runtimeId,long mapInstanceId,string prefabAddress,Vector3 position,Vector3 direction,out string error)
        {
            error="联机弹体表现尚未准备";
            if(released||currentScene==null||mapInstanceId!=currentMapInstanceId||runtimeId<=0||onlineIndexMapId==null)return false;
            if(!onlineViews.TryGetValue(runtimeId,out var view)||view==null)
            {
                if(!committedPrefabs.TryGetValue(prefabAddress,out var entry)||entry.Prefab==null)return false;
                view=this.GetUtility<IViewInstanceProvider>().Instantiate(entry.Prefab.GetComponent<ProjectileView>(),currentScene.DynamicObjectRoot,position,Quaternion.identity);
                if(view==null)return false;
                view.Bind(0,null);view.TriggerCollider.enabled=false;
                onlineViews[runtimeId]=view;
            }
            view.ApplyPose(position,direction);error=null;return true;
        }
        public void RemoveProjectile(long runtimeId)
        {if(!onlineViews.TryGetValue(runtimeId,out var view))return;onlineViews.Remove(runtimeId);if(view!=null)this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);}
        public void ClearOnlineProjectiles(){foreach(var id in new List<long>(onlineViews.Keys))RemoveProjectile(id);}
        private void ReleaseOnlineState(){ClearOnlineProjectiles();WorldProjectileCollisionCatalog.Release(onlineIndexMapId);onlineIndexMapId=null;}
    }
}
