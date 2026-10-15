using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>拥有单个房间成员的外观舞台和渲染纹理；仅展示默认造型，不创建玩家实体或同步装备。</summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class OnlineRoomMemberPreview : MonoBehaviour
    {
        [SerializeField, Tooltip("仅含默认玩家网格、待机动画、摄像机和局部灯光的正式展示 Prefab。")]
        private GameObject stagePrefab;

        // 槽位只隔离展示摄像机的视锥；不是玩家或房间身份，隐藏时即可归还。
        private static readonly HashSet<int> OccupiedSlots = new HashSet<int>();
        private int stageSlot = -1;
        private GameObject stageInstance;
        private Camera stageCamera;
        private RenderTexture texture;
        private RawImage image;

        private void OnEnable()
        {
            if (Application.isPlaying) BeginPreview();
        }

        private void OnDisable() => ReleasePreview();
        private void OnDestroy() => ReleasePreview();

        /// <summary>可见时创建一次独立外观舞台；重复调用复用资源，创建失败则回滚全部已分配资源。</summary>
        public void BeginPreview()
        {
            if (stageInstance != null || !isActiveAndEnabled) return;
            image = GetComponent<RawImage>();
            if (stagePrefab == null)
                throw new InvalidOperationException("房间成员展示缺少正式舞台 Prefab。");
            try
            {
                stageSlot = 0;
                while (OccupiedSlots.Contains(stageSlot)) stageSlot++;
                OccupiedSlots.Add(stageSlot);
                // 舞台独立于 Canvas 缩放；远离地图且槽位相隔 20 米，摄像机只绘制近处 UI 层模型。
                stageInstance = Instantiate(stagePrefab);
                stageInstance.transform.position = new Vector3(stageSlot * 20f, -10000f, 0f);
                stageCamera = stageInstance.GetComponentInChildren<Camera>(true);
                if (stageCamera == null)
                    throw new InvalidOperationException("房间成员展示舞台缺少摄像机。");
                // 384×512 像素、无 HDR/MSAA；容量最多四人的小型预览不需要全屏纹理。
                texture = new RenderTexture(384, 512, 16, RenderTextureFormat.ARGB32)
                {
                    name = "OnlineRoomMemberPreview",
                    hideFlags = HideFlags.DontSave,
                    antiAliasing = 1
                };
                if (!texture.Create()) throw new InvalidOperationException("房间成员展示纹理创建失败。");
                stageCamera.targetTexture = texture;
                image.texture = texture;
                stageInstance.SetActive(true);
            }
            catch
            {
                ReleasePreview();
                throw;
            }
        }

        /// <summary>隐藏、回收和销毁时幂等释放；先停止摄像机，再解除纹理引用，最后归还隔离槽位。</summary>
        public void ReleasePreview()
        {
            if (stageCamera != null)
            {
                stageCamera.enabled = false;
                stageCamera.targetTexture = null;
            }
            if (image != null) image.texture = null;
            if (stageInstance != null)
            {
                stageInstance.SetActive(false);
                Dispose(stageInstance);
            }
            if (texture != null)
            {
                texture.Release();
                Dispose(texture);
            }
            stageInstance = null;
            stageCamera = null;
            texture = null;
            if (stageSlot >= 0) OccupiedSlots.Remove(stageSlot);
            stageSlot = -1;
        }

        private static void Dispose(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
