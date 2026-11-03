using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game
{
    /// <summary>独立挑战现场的资源与表现所有者；不创建旧模式战斗、掉落或自动攻击。</summary>
    public sealed class ChallengeWorldCtrl : IDisposable
    {
        public const string MapAddress = "Art/Challenge/Crater/CraterMap";
        private readonly IResourceLoader loader;
        private readonly List<ResourceLoadHandle> handles = new();
        private GameObject root;
        private PlayerView player;
        private MeshCollider ground, road;
        private Transform supply;
        private Camera camera, previousCamera;
        private bool previousCameraEnabled, disposed;
        private int visibleSlot = -1;
        public long MapInstanceId { get; }
        public bool Ready => !disposed && root != null && player != null;
        public Vector3 Position => player != null ? player.transform.position : Vector3.zero;
        public Vector3 SupplyPosition => supply.position;

        /// <summary>每次入场持有独立实例编号与加载器，释放不依赖全局加载器是否已替换。</summary>
        public ChallengeWorldCtrl(IResourceLoader loader, long mapInstanceId)
        { this.loader=loader ?? throw new ArgumentNullException(nameof(loader)); MapInstanceId=mapInstanceId; }

        /// <summary>资源全部完成后才装配隐藏候选；取消和晚到回调不创建对象。</summary>
        public async UniTask PrepareAsync(string playerAddress, CancellationToken token)
        {
            var mapAsset=await LoadAsync(MapAddress,token);
            var playerAsset=await LoadAsync(playerAddress,token);
            token.ThrowIfCancellationRequested();
            if(disposed)throw new ObjectDisposedException(nameof(ChallengeWorldCtrl));
            root=new GameObject("ClassicChallengeWorld");root.SetActive(false);
            var map=Object.Instantiate(mapAsset,root.transform);
            ground=map.transform.Find("Ground")?.GetComponent<MeshCollider>();
            road=map.transform.Find("Road")?.GetComponent<MeshCollider>();
            supply=map.transform.Find("SupplyPoint");
            var spawn=map.transform.Find("PlayerSpawn");
            var gates=map.transform.Find("SpawnGates");
            if(ground==null||road==null||supply==null||spawn==null||gates==null||gates.childCount!=6)
                throw new InvalidOperationException("挑战地图缺少路面、补给或六个门入口");
            player=Object.Instantiate(playerAsset,root.transform).GetComponent<PlayerView>();
            if(player==null||!player.Validate(out var error))throw new InvalidOperationException("挑战角色资源不完整");
            player.transform.position=spawn.position;
            player.SetAlivePresentation(true,false);
            player.SetArcadePresentationActive(true);
            camera=new GameObject("ChallengeCamera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.09f,.12f);
            camera.nearClipPlane=.1f;camera.farClipPlane=250;camera.fieldOfView=45;
            var light=new GameObject("ChallengeDaylight",typeof(Light));light.transform.SetParent(root.transform);
            light.transform.rotation=Quaternion.Euler(45,-30,0);light.GetComponent<Light>().type=LightType.Directional;light.GetComponent<Light>().intensity=1.5f;
            SyncCamera();
        }

        private async UniTask<GameObject> LoadAsync(string address,CancellationToken token)
        {
            var completion=new UniTaskCompletionSource<GameObject>();
            var handle=loader.LoadAssetAsync<GameObject>(address,asset=>completion.TrySetResult(asset));
            handles.Add(handle);
            var asset=await completion.Task.AttachExternalCancellation(token);
            token.ThrowIfCancellationRequested();
            if(disposed)throw new ObjectDisposedException(nameof(ChallengeWorldCtrl));
            if(asset==null)throw new InvalidOperationException("挑战资源加载失败："+address);
            return asset;
        }

        /// <summary>仅在领域开局成功后切换镜头；失败前菜单镜头保持原状。</summary>
        public void Activate()
        {
            if(!Ready)throw new InvalidOperationException("挑战现场尚未准备完成");
            previousCamera=Camera.main;
            if(previousCamera!=null){previousCameraEnabled=previousCamera.enabled;previousCamera.enabled=false;}
            root.SetActive(true);Physics.SyncTransforms();
        }

        /// <summary>只接受邻近高度的真实路面，拒绝跨层吸附和走向地图外。</summary>
        public static bool TryFindSupport(MeshCollider ground,MeshCollider road,Vector3 point,out float height)
        {
            height=0;bool found=false;float closest=float.MaxValue;
            var ray=new Ray(point+Vector3.up*.7f,Vector3.down);
            foreach(var surface in new[]{ground,road})
                if(surface!=null&&surface.Raycast(ray,out var hit,1.4f))
                {
                    float delta=Mathf.Abs(hit.point.y-point.y);
                    if(delta<closest){closest=delta;height=hit.point.y;found=true;}
                }
            return found;
        }

        /// <summary>实际角色碰撞负责车辆阻挡，落脚检测防止越过高层路缘。</summary>
        public void Move(Vector2 intent,float speed,float seconds)
        {
            if(!Ready||seconds<=0)return;
            var direction=new Vector3(intent.x,0,intent.y);direction=Vector3.ClampMagnitude(direction,1);
            var before=Position;float distance=speed*seconds;
            int steps=Mathf.Max(1,Mathf.CeilToInt(distance/.15f));
            for(int i=0;i<steps;i++)
            {
                var next=Position+direction*(distance/steps);
                if(!TryFindSupport(ground,road,next+direction*.35f,out _))break;
                if(!TryFindSupport(ground,road,next,out float y))break;
                player.CharacterController.Move(new Vector3(direction.x*distance/steps,y-Position.y-.025f,direction.z*distance/steps));
            }
            var motion=Position-before;player.SetMovementSpeed(new Vector2(motion.x,motion.z).magnitude/seconds);
            player.SetMovementDirection(motion);SyncCamera();
        }

        public void Aim(Vector2 screenPosition)
        {
            if(!Ready)return;
            var ray=camera.ScreenPointToRay(screenPosition);
            var plane=new Plane(Vector3.up,Position+Vector3.up);
            if(plane.Raycast(ray,out float distance))
            {var direction=ray.GetPoint(distance)-Position;direction.y=0;if(direction.sqrMagnitude>.01f)player.transform.rotation=Quaternion.LookRotation(direction);}
        }

        /// <summary>沿用现有人物枪械表现，不接入其射击裁决。</summary>
        public void Present(int slot,float deltaTime,bool fired)
        {
            if(!Ready)return;
            if(visibleSlot!=slot){visibleSlot=slot;player.SetArcadeWeaponPresentation(slot==0?"ArcadeBasicRifle":"ArcadePistol",true,new Vector3(0,0,.575f),slot==0?PlayerWeaponGripPose.Rifle:PlayerWeaponGripPose.Pistol);}
            if(fired)player.PlayProjectileFireFeedback();
            player.TickCombatFeedback(deltaTime);
        }
        private void SyncCamera()
        {camera.transform.rotation=Quaternion.Euler(72,0,0);camera.transform.position=Position-camera.transform.forward*20;}

        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(root!=null){root.SetActive(false);Object.Destroy(root);}root=null;player=null;
            if(previousCamera!=null)previousCamera.enabled=previousCameraEnabled;
            foreach(var handle in handles)if(handle.IsValid)loader.Release(handle);
            handles.Clear();
        }
    }
}
