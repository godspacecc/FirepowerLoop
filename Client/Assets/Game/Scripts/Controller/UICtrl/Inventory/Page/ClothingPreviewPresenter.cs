using System;
using QFramework;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>当前可见分页拥有的离屏人物与手持武器预览；只读取权威快照，不创建玩法角色，隐藏时完整释放。</summary>
    internal sealed class ClothingPreviewPresenter : IDisposable
    {
        readonly GameObject root;
        readonly Camera camera;
        readonly RenderTexture texture;
        readonly RawImage target;
        readonly ClothingAppearanceView appearance;
        readonly Transform actor;
        readonly ClothingPreviewDragView drag;
        bool disposed;
        readonly IUnRegister appearanceChanged;
        readonly CharacterWeaponPresenter weapon;
        readonly IUnRegister weaponChanged, onlineChanged, onlineInventoryChanged;
        /// <summary>最近展示的物品 ID；-1 表示尚未绑定，0 表示空手。</summary>
        int displayedWeaponId = -1;
        /// <summary>x 为水平朝向，y 为观察俯仰（度），z 为正交半高度（米）；仅保存本次 UI 会话。</summary>
        public static readonly Vector3 DefaultView = new Vector3(-12f, 0f, 1.08f);
        private Vector3 view;
        public Vector3 View => new Vector3(Yaw, view.y, view.z);
        /// <summary>人物相对预览根的水平角度，单位为度；由宿主在分页切换时保存。</summary>
        public float Yaw => actor.localEulerAngles.y;
        public ClothingPreviewPresenter(GameObject prefab,RawImage target) : this(prefab,target,DefaultView) { }
        public ClothingPreviewPresenter(GameObject prefab,RawImage target,float yaw) : this(prefab,target,new Vector3(yaw,0f,DefaultView.z)) { }
        public ClothingPreviewPresenter(GameObject prefab,RawImage target,Vector3 view)
        {
            this.target=target;
            root=new GameObject("ClothingPagePreview");root.transform.position=new Vector3(10000,-10000,10000);
            var actor=UnityEngine.Object.Instantiate(prefab,root.transform);actor.transform.localPosition=Vector3.zero;actor.transform.localRotation=Quaternion.Euler(0,-12,0);
            this.actor=actor.transform;
            foreach(var t in actor.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            // 页面可能在同一帧内连续切换部位，离屏渲染不能复用上一套衣物的蒙皮矩阵。
            foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))skin.forceMatrixRecalculationPerRender=true;
            appearance=actor.GetComponent<ClothingAppearanceView>();
            weapon=actor.GetComponent<CharacterWeaponPresenter>();
            weapon.ConfigurePoseSettings(Game.Interface.GetSystem<WeaponPoseSettingsSystem>());
            weapon.SetManualPoseSampling(true);
            appearanceChanged=Game.Interface.RegisterEvent<PlayerTestAppearanceChangedEvent>(_ => RefreshWholeBody());
            var animator=actor.GetComponentInChildren<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.Update(0);
            camera=new GameObject("PortraitCamera",typeof(Camera)).GetComponent<Camera>();camera.transform.SetParent(root.transform,false);
            camera.transform.localPosition=new Vector3(0,1,4);camera.transform.LookAt(root.transform.position+Vector3.up*.95f);
            camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1.08f;camera.nearClipPlane=.01f;camera.farClipPlane=10;camera.cullingMask=1<<31;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.15f,.16f,0);
            var light=new GameObject("PortraitLight",typeof(Light)).GetComponent<Light>();light.transform.SetParent(root.transform,false);light.type=LightType.Directional;light.cullingMask=1<<31;light.intensity=1.3f;light.transform.rotation=Quaternion.Euler(35,150,0);
            texture=new RenderTexture(512,1024,24);texture.Create();camera.targetTexture=texture;target.texture=texture;
            // 工具截图允许没有拖动组件；正式 UI Prefab 必须序列化该组件。
            drag=target.GetComponent<ClothingPreviewDragView>();
            if(drag!=null){drag.Orbit+=Orbit;drag.Zoom+=Zoom;}
            SetView(view);
            weaponChanged=Game.Interface.RegisterEvent<WeaponEquipmentChangedEvent>(_ => RefreshWeapon());
            onlineChanged=Game.Interface.RegisterEvent<OnlineWorldChangedEvent>(_ => RefreshWeapon());
            onlineInventoryChanged=Game.Interface.RegisterEvent<OnlineWorldInventoryChangedEvent>(_ => RefreshWeapon());
            RefreshWeapon();
            RefreshWholeBody();
        }
        private void Orbit(Vector2 delta) => SetView(new Vector3(Yaw+delta.x,view.y+delta.y,view.z));
        private void Zoom(float delta) => SetView(new Vector3(Yaw,view.y,view.z*Mathf.Exp(-Mathf.Clamp(delta,-4f,4f))));
        /// <summary>恢复分页朝向并重绘；释放后的迟到输入不再操作相机。</summary>
        public void SetYaw(float yaw)
        {SetView(new Vector3(yaw,view.y,view.z));}
        /// <summary>恢复整套视角并同步重绘；俯仰限制为 -10 到 75 度，缩放避免穿过模型或完全丢失人物。</summary>
        public void SetView(Vector3 value)
        {
            if(disposed)return;
            view=new Vector3(value.x,Mathf.Clamp(value.y,-10f,75f),Mathf.Clamp(value.z,.55f,1.5f));
            actor.localRotation=Quaternion.Euler(0,view.x,0);
            var focus=Vector3.up*.95f;
            camera.transform.localPosition=focus+Quaternion.Euler(-view.y,0,0)*Vector3.forward*4f;
            camera.transform.LookAt(root.transform.TransformPoint(focus));camera.orthographicSize=view.z;
            Render();
        }
        public void Refresh(string top,string pants)
        {if(appearance.Apply(top,pants))Render();}
        public void RefreshItems(IRuntimeConfigProvider config,int[] items)
        {if(appearance.ApplyItems(config,items))Render();}
        /// <summary>本地读取手持模型，联机只读取服务端确认快照；同物品不重复创建，失败清空旧表现。</summary>
        private void RefreshWeapon()
        {
            if(disposed)return;
            int itemId=Game.Interface.GetModel<MapSceneModel>().CurrentMode==MapPreparationMode.OnlinePlayersOnly
                ? Game.Interface.GetSystem<OnlineWorldSystem>().Snapshot.SelfInventory.Equipment.ItemId
                : Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId;
            if(itemId==displayedWeaponId)return;
            var entry=itemId>0?weapon.Catalog.FindByWorldItemId(itemId):null;
            bool bound=weapon.SetWeapon(entry);
            if(itemId>0&&(entry==null||!bound))
                Log.Error($"[ClothingPreviewPresenter] 手持武器预览失败：{itemId} / {weapon.LastError}");
            displayedWeaponId=itemId;
            Render();
        }
        /// <summary>离屏相机不逐帧渲染；每次重绘重新采样待机再约束副手，避免骨骼修正累计。</summary>
        private void Render()
        {
            if(disposed)return;
            var idle=weapon.Current!=null?weapon.Current.Animations.Idle:weapon.Profile.UnarmedAnimations.Idle;
            weapon.BodyAnimator.Rebind();
            weapon.BodyAnimator.Update(0f);
            idle.Clip.SampleAnimation(weapon.BodyAnimator.gameObject,0f);
            weapon.ApplyOffhandGrip(idle);
            camera.Render();
        }
        /// <summary>预览读取同一测试快照；联机预览保持正式装备，释放时退订。</summary>
        private void RefreshWholeBody()
        {
            if(disposed)return;
            int index=Game.Interface.GetModel<MapSceneModel>().CurrentMode==MapPreparationMode.OfflineFull
                ? Game.Interface.GetModel<TestCmdModel>().PlayerAppearanceIndex : 0;
            if(appearance.ApplyWholeBody(index))Render();
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            appearanceChanged?.UnRegister();
            weaponChanged?.UnRegister();onlineChanged?.UnRegister();onlineInventoryChanged?.UnRegister();
            if(drag!=null){drag.Orbit-=Orbit;drag.Zoom-=Zoom;}
            // PlayMode 的 Destroy 延迟到帧末，先隐藏旧模型，避免同帧切页时新相机拍到两个人物。
            if(root!=null)root.SetActive(false);
            if(target!=null)target.texture=null;
            if(camera!=null)camera.targetTexture=null;
            if(texture!=null){texture.Release();Destroy(texture);}Destroy(root);
        }
        static void Destroy(UnityEngine.Object value)
        {if(value==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
    }
}
