using UnityEngine;

namespace Game
{
    /// <summary>装配地图玩家镜头表现；模式与输入由 System 决定，不拥有共享主相机或玩家。</summary>
    public sealed class PlayerCameraCtrl : IPlayerCameraPort, System.IDisposable
    {
        private const float ArcadePitchDegrees = 72f;
        private const float ArcadeFollowDistance = 16f;
        private const float ArcadeFieldOfView = 45f;
        private readonly Camera camera;
        private readonly Transform target;
        private readonly TopDownCameraProfile topDownProfile;
        private readonly ShoulderCameraProfile shoulderProfile;
        private readonly System.Collections.Generic.Dictionary<GameObject, string> originalColliderTags = new();
        private readonly System.Collections.Generic.List<Collider> playerColliders = new();
        // 第一人称与交谈共用隐藏快照；两种原因都结束后恢复首次记录，不触碰碰撞和业务状态。
        private readonly System.Collections.Generic.Dictionary<Renderer, bool> renderingBeforeFirstPerson = new();
        private readonly System.Collections.Generic.Dictionary<Canvas, bool> canvasesBeforeFirstPerson = new();
        private readonly System.Collections.Generic.List<Renderer> localRenderers = new();
        private readonly System.Collections.Generic.List<Canvas> localCanvases = new();
        // 独立手臂随本机镜头创建；退出模式即释放，不能挂到被隐藏的玩家子树。
        private PlayerFirstPersonCtrl firstPersonPresentation;
        // 镜头架由地图玩家拥有；禁用虚拟相机自动更新，沿用现有 System 生命周期。
        private GameObject shoulderRig;
        private Transform shoulderTarget;
        private Cinemachine.CinemachineVirtualCamera shoulderCamera;
        private Cinemachine.Cinemachine3rdPersonFollow shoulderFollow;
        private int lastShoulderFrame = -1;
        /// <summary>仅表现层的目标与混合比例；0 为常态，1 为瞄准，不反向裁决输入资格。</summary>
        private bool shoulderAimRequested;
        private float shoulderAimBlend;
        private bool disposed;
        private Vector3 followVelocity;
        private bool arcadeMode;
        /// <summary>当前交谈的世界位置，仅由相机 System 在有效会话中更新，不持有 NPC Transform。</summary>
        private Vector3? dialogueFocus;
        /// <summary>当前会话的本机表现遮挡；与视角独立，镜头释放时清理，不写入存档。</summary>
        private bool dialoguePresentationHidden;
        /// <summary>最后实际应用的视角；结束交谈时据此保留第一人称对全身模型的隐藏。</summary>
        private PlayerCameraMode presentationMode;
        // 与地图玩家镜头同寿命，释放时恢复所有临时遮挡材质。
        private TopDownOcclusionCtrl occlusion;

        public PlayerCameraCtrl(Camera camera, Transform target, TopDownCameraProfile topDownProfile)
            : this(camera, target, topDownProfile, null) { }

        public PlayerCameraCtrl(Camera camera, Transform target, TopDownCameraProfile topDownProfile, ShoulderCameraProfile shoulderProfile)
        {
            this.camera = camera;
            this.target = target;
            this.topDownProfile = topDownProfile;
            this.shoulderProfile = shoulderProfile;
            occlusion = new TopDownOcclusionCtrl(camera, target);
        }

        public float AppliedPitch { get; private set; }
        /// <summary>会话进入或释放时立即更新表现；重复释放不覆盖第一人称已隐藏的状态。</summary>
        public void SetDialoguePresentationHidden(bool hidden)
        {
            if (!IsValid || dialoguePresentationHidden == hidden) return;
            dialoguePresentationHidden = hidden;
            if (hidden)
            {
                firstPersonPresentation?.Dispose();
                firstPersonPresentation = null;
                HideLocalPresentation();
            }
            else if (presentationMode != PlayerCameraMode.FirstPerson) RestoreLocalPresentation();
        }
        /// <summary>接管或释放临时镜头；正常释放重置阻尼，下一帧回到原相机模式。</summary>
        public bool TrySetDialogueFocus(Vector3? npcPosition)
        {
            if (!IsValid || npcPosition.HasValue && (!IsFinite(npcPosition.Value) || shoulderProfile == null)) return false;
            if (dialogueFocus.HasValue && !npcPosition.HasValue) lastShoulderFrame = -1;
            dialogueFocus = npcPosition;
            return true;
        }
        public float AppliedDistance { get; private set; }
        /// <summary>接收有效按压或释放；不改变镜头模式，实际构图在刷新时平滑应用。</summary>
        public bool TrySetShoulderAim(bool aiming)
        {
            if (!IsValid || aiming && shoulderProfile == null) return false;
            shoulderAimRequested = aiming;
            return true;
        }
        public float TopDownYawDegrees => topDownProfile != null ? topDownProfile.YawDegrees : 0f;
        public bool IsValid => Validate(out _);

        /// <summary>
        /// 切换街机镜头构图。街机模式始终以本机玩家为中心，避免移动端手指遮挡偏离中心的角色。
        /// </summary>
        public void SetArcadeMode(bool enabled)
        {
            arcadeMode = enabled;
            followVelocity = Vector3.zero;
        }

        public bool Validate(out string error)
        {
            if (disposed) { error = "相机端口已释放"; return false; }
            if (camera == null) { error = "缺少 Main Camera"; return false; }
            if (target == null) { error = "缺少相机跟随目标"; return false; }
            if (topDownProfile == null) { error = "缺少固定俯视相机配置"; return false; }
            error = null;
            return true;
        }

        public bool SupportsMode(PlayerCameraMode mode, out string error)
        {
            if (!Validate(out error)) return false;
            if (mode != PlayerCameraMode.TopDown && mode != PlayerCameraMode.Shoulder && mode != PlayerCameraMode.FirstPerson)
            {
                error = "不支持的相机模式";
                return false;
            }
            if (mode != PlayerCameraMode.TopDown && shoulderProfile == null)
            {
                error = "缺少越肩相机配置";
                return false;
            }
            if (mode == PlayerCameraMode.FirstPerson && target.TryGetComponent<PlayerView>(out var view))
            {
                var prefab = view.FirstPersonArmsPrefab;
                var arms = prefab != null ? prefab.GetComponent<Animator>() : null;
                if (view.Animator == null || !view.Animator.isHuman || arms == null || arms.avatar == null ||
                    !arms.avatar.isValid || !arms.avatar.isHuman ||
                    prefab.GetComponentInChildren<SkinnedMeshRenderer>() == null)
                {
                    error = "缺少有效的第一人称手臂或 Humanoid 骨架";
                    return false;
                }
            }
            error = null;
            return true;
        }

        public void ApplyProfile() => ApplyMode(PlayerCameraMode.TopDown, 0f, 0f);

        public void ApplyMode(PlayerCameraMode mode, float shoulderYawDegrees, float shoulderPitchDegrees)
        {
            if (!SupportsMode(mode, out _)) return;
            presentationMode = mode;
            followVelocity = Vector3.zero;
            if (mode != PlayerCameraMode.Shoulder) ResetShoulderAim();
            if (mode == PlayerCameraMode.FirstPerson)
            {
                UpdateFirstPersonCamera(shoulderYawDegrees, shoulderPitchDegrees);
                return;
            }
            RefreshLocalPresentation();
            if (mode == PlayerCameraMode.TopDown)
            {
                ApplyTopDownSettings();
                camera.transform.position = CalculateTopDownDesiredPosition();
            }
            else
            {
                occlusion.Dispose();
                shoulderAimBlend = shoulderAimRequested && !dialogueFocus.HasValue ? 1f : 0f;
                ApplyShoulderSettings(shoulderYawDegrees, shoulderPitchDegrees);
                UpdateShoulderCamera(shoulderYawDegrees, -1f);
                lastShoulderFrame = -1;
            }
        }

        public void LateUpdate() => LateUpdate(PlayerCameraMode.TopDown, 0f, 0f);

        public void LateUpdate(PlayerCameraMode mode, float shoulderYawDegrees, float shoulderPitchDegrees)
        {
            if (!SupportsMode(mode, out _)) return;
            presentationMode = mode;
            if (mode != PlayerCameraMode.Shoulder) ResetShoulderAim();
            if (mode == PlayerCameraMode.FirstPerson)
            {
                UpdateFirstPersonCamera(shoulderYawDegrees, shoulderPitchDegrees);
                return;
            }
            RefreshLocalPresentation();
            if (mode == PlayerCameraMode.TopDown)
            {
                ApplyTopDownSettings();
                MoveToDesiredPosition(CalculateTopDownDesiredPosition());
                if (arcadeMode) occlusion.Dispose();
                else occlusion.Tick(Time.unscaledDeltaTime);
                return;
            }
            occlusion.Dispose();
            var deltaTime = lastShoulderFrame == Time.frameCount ? 0f : Time.deltaTime;
            AdvanceShoulderAim(deltaTime);
            ApplyShoulderSettings(shoulderYawDegrees, shoulderPitchDegrees);
            if (dialogueFocus.HasValue)
            {
                var direction = dialogueFocus.Value - target.position;
                direction.y = 0f;
                if (direction.sqrMagnitude > .0001f) shoulderYawDegrees = Quaternion.LookRotation(direction).eulerAngles.y;
                AppliedPitch = 0f;
                ApplyLens(shoulderProfile.DialogueFieldOfView, shoulderProfile.NearClipPlane, shoulderProfile.FarClipPlane);
            }
            // 同帧输入与帧末刷新只推进一次跟随阻尼。
            UpdateShoulderCamera(shoulderYawDegrees, deltaTime);
            lastShoulderFrame = Time.frameCount;
        }

        public bool TryGetScreenPointRay(Vector2 screenPosition, out Ray ray)
        {
            if (!IsValid) { ray = default; return false; }
            ray = camera.ScreenPointToRay(screenPosition);
            return IsFinite(ray.origin) && IsFinite(ray.direction);
        }

        public bool TryGetViewportCenterRay(out Ray ray)
        {
            if (!IsValid) { ray = default; return false; }
            ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return IsFinite(ray.origin) && IsFinite(ray.direction);
        }

        public bool TryGetCameraPose(out Vector3 position, out Vector3 up)
        {
            if (!IsValid) { position = Vector3.zero; up = Vector3.up; return false; }
            position = camera.transform.position;
            up = camera.transform.up;
            return true;
        }

        public static Vector3 CalculateShoulderDesiredPosition(Vector3 targetPosition, float targetHeight, float shoulderOffset, float distance, float yawDegrees, float pitchDegrees)
        {
            var targetPoint = targetPosition + Vector3.up * targetHeight;
            var yaw = Quaternion.Euler(0f, yawDegrees, 0f);
            var view = Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
            return targetPoint + yaw * Vector3.right * shoulderOffset - view * Vector3.forward * distance;
        }

        /// <summary>模式进入及每帧末刷新；直接跟随眼睛高度，避免动画与第三人称阻尼带来晕动。</summary>
        private void UpdateFirstPersonCamera(float yawDegrees, float pitchDegrees)
        {
            occlusion.Dispose();
            AppliedPitch = Mathf.Clamp(pitchDegrees, -80f, 80f);
            AppliedDistance = 0f;
            ApplyLens(shoulderProfile.FirstPersonFieldOfView, shoulderProfile.FirstPersonNearClipPlane, shoulderProfile.FarClipPlane);
            camera.transform.SetPositionAndRotation(target.position + Vector3.up * Mathf.Max(.1f, shoulderProfile.FirstPersonEyeHeight),
                Quaternion.Euler(AppliedPitch, yawDegrees, 0f));
            HideLocalPresentation();
            if (dialoguePresentationHidden) return;
            if (firstPersonPresentation == null && target.TryGetComponent<PlayerView>(out var view))
                firstPersonPresentation = new PlayerFirstPersonCtrl(camera, view, arcadeMode);
            firstPersonPresentation?.Refresh();
        }

        /// <summary>逐帧接管新增装备和 HUD，保留首次原值；只隐藏当前玩家子树，不停用对象。</summary>
        private void HideLocalPresentation()
        {
            // 武器可能在模式切换后异步创建，所以刷新时也捕获新增表现，仍只处理当前玩家子节点。
            target.GetComponentsInChildren(true, localRenderers);
            foreach (var renderer in localRenderers)
            {
                if (!renderingBeforeFirstPerson.ContainsKey(renderer)) renderingBeforeFirstPerson.Add(renderer, renderer.forceRenderingOff);
                renderer.forceRenderingOff = true;
            }
            target.GetComponentsInChildren(true, localCanvases);
            foreach (var canvas in localCanvases)
            {
                if (!canvasesBeforeFirstPerson.ContainsKey(canvas)) canvasesBeforeFirstPerson.Add(canvas, canvas.enabled);
                canvas.enabled = false;
            }
        }

        private void RefreshLocalPresentation()
        {
            if (dialoguePresentationHidden) HideLocalPresentation();
            else RestoreLocalPresentation();
        }

        /// <summary>隐藏原因结束或释放镜头时幂等恢复；已销毁的武器与 HUD 不再写入。</summary>
        private void RestoreLocalPresentation()
        {
            firstPersonPresentation?.Dispose();
            firstPersonPresentation = null;
            foreach (var entry in renderingBeforeFirstPerson)
                if (entry.Key != null) entry.Key.forceRenderingOff = entry.Value;
            foreach (var entry in canvasesBeforeFirstPerson)
                if (entry.Key != null) entry.Key.enabled = entry.Value;
            renderingBeforeFirstPerson.Clear();
            canvasesBeforeFirstPerson.Clear();
        }

        private void ApplyTopDownSettings()
        {
            AppliedPitch = arcadeMode ? ArcadePitchDegrees : Mathf.Clamp(topDownProfile.PitchDegrees, 65f, 75f);
            AppliedDistance = arcadeMode ? ArcadeFollowDistance : Mathf.Max(0.1f, topDownProfile.FollowDistance);
            ApplyLens(arcadeMode ? ArcadeFieldOfView : topDownProfile.FieldOfView, topDownProfile.NearClipPlane, topDownProfile.FarClipPlane);
            camera.transform.rotation = Quaternion.Euler(AppliedPitch, topDownProfile.YawDegrees, 0f);
        }

        private void ApplyShoulderSettings(float yawDegrees, float pitchDegrees)
        {
            AppliedPitch = Mathf.Clamp(pitchDegrees, shoulderProfile.MinPitchDegrees, shoulderProfile.MaxPitchDegrees);
            AppliedDistance = Mathf.Max(shoulderProfile.MinimumDistance,
                Mathf.Lerp(shoulderProfile.FollowDistance, shoulderProfile.AimFollowDistance, shoulderAimBlend));
            ApplyLens(Mathf.Lerp(shoulderProfile.FieldOfView, shoulderProfile.AimFieldOfView, shoulderAimBlend),
                shoulderProfile.NearClipPlane, shoulderProfile.FarClipPlane);
            camera.transform.rotation = Quaternion.Euler(AppliedPitch, yawDegrees, 0f);
        }

        /// <summary>沿当前混合比例推进，反向松开不会跳变；同帧输入与帧末只推进一次。</summary>
        private void AdvanceShoulderAim(float deltaTime)
        {
            var desired = shoulderAimRequested && !dialogueFocus.HasValue ? 1f : 0f;
            shoulderAimBlend = shoulderProfile.AimTransitionSeconds <= 0f
                ? desired
                : Mathf.MoveTowards(shoulderAimBlend, desired, Mathf.Max(0f, deltaTime) / shoulderProfile.AimTransitionSeconds);
        }

        private void ResetShoulderAim()
        {
            shoulderAimRequested = false;
            shoulderAimBlend = 0f;
        }

        private void ApplyLens(float fieldOfView, float near, float far)
        {
            near = Mathf.Max(0.01f, near);
            camera.orthographic = false;
            camera.fieldOfView = Mathf.Clamp(fieldOfView, 20f, 80f);
            camera.nearClipPlane = near;
            camera.farClipPlane = Mathf.Max(near + 0.1f, far);
        }

        private Vector3 CalculateTopDownDesiredPosition()
        {
            return target.position - Quaternion.Euler(AppliedPitch, topDownProfile.YawDegrees, 0f) * Vector3.forward * AppliedDistance;
        }

        /// <summary>
        /// 街机镜头必须直接跟随，避免平滑滞后让玩家偏离屏幕中心并进入移动端手指遮挡区；
        /// 普通俯视镜头仍使用配置的平滑时间。
        /// </summary>
        private void MoveToDesiredPosition(Vector3 desiredPosition)
        {
            var smoothTime = Mathf.Max(0f, topDownProfile.FollowSmoothTime);
            camera.transform.position = arcadeMode || smoothTime <= 0f
                ? desiredPosition
                : Vector3.SmoothDamp(camera.transform.position, desiredPosition, ref followVelocity, smoothTime);
        }

        /// <summary>惰性装配越肩管线；负时间步用于地图绑定及模式切换，清除旧目标阻尼历史。</summary>
        private void UpdateShoulderCamera(float yawDegrees, float deltaTime)
        {
            if (shoulderRig == null)
            {
                shoulderRig = new GameObject("PlayerShoulderCameraRig") { hideFlags = HideFlags.DontSave };
                shoulderRig.transform.SetParent(target, false);
                shoulderTarget = new GameObject("FollowTarget").transform;
                shoulderTarget.SetParent(shoulderRig.transform, false);
                var virtualObject = new GameObject("ShoulderVirtualCamera");
                virtualObject.SetActive(false);
                virtualObject.transform.SetParent(shoulderRig.transform, false);
                shoulderCamera = virtualObject.AddComponent<Cinemachine.CinemachineVirtualCamera>();
                shoulderCamera.enabled = false;
                shoulderCamera.Follow = shoulderTarget;
                shoulderFollow = shoulderCamera.AddCinemachineComponent<Cinemachine.Cinemachine3rdPersonFollow>();
                virtualObject.SetActive(true);
            }
            // Cinemachine 按标签排除自身，记录原标签以便释放时恢复，不改变物理层或碰撞状态。
            target.GetComponentsInChildren(true, playerColliders);
            foreach (var collider in playerColliders)
            {
                var owner = collider.gameObject;
                if (!originalColliderTags.ContainsKey(owner)) originalColliderTags.Add(owner, owner.tag);
                owner.tag = "Player";
            }
            var focusPoint = dialogueFocus.HasValue
                ? dialogueFocus.Value + Vector3.up * shoulderProfile.DialogueTargetHeight
                : target.position + Vector3.up * shoulderProfile.TargetHeight;
            shoulderTarget.SetPositionAndRotation(focusPoint,
                Quaternion.Euler(AppliedPitch, yawDegrees, 0f));
            var offset = dialogueFocus.HasValue ? shoulderProfile.DialogueShoulderOffset :
                Mathf.Lerp(shoulderProfile.ShoulderOffset, shoulderProfile.AimShoulderOffset, shoulderAimBlend);
            shoulderFollow.ShoulderOffset = new Vector3(offset, 0f, 0f);
            shoulderFollow.VerticalArmLength = 0f;
            shoulderFollow.CameraSide = 1f;
            shoulderFollow.CameraDistance = Mathf.Max(.01f, Mathf.Max(shoulderProfile.MinimumDistance,
                dialogueFocus.HasValue ? shoulderProfile.DialogueDistance :
                    Mathf.Lerp(shoulderProfile.FollowDistance, shoulderProfile.AimFollowDistance, shoulderAimBlend)));
            shoulderFollow.Damping = Vector3.Max(Vector3.zero, shoulderProfile.FollowDamping);
            shoulderFollow.CameraCollisionFilter = shoulderProfile.ObstructionLayers;
            shoulderFollow.IgnoreTag = "Player";
            shoulderFollow.CameraRadius = Mathf.Max(.001f, shoulderProfile.ObstructionRadius);
            // 遮挡收镜立即生效，离开遮挡平滑恢复，不再叠加旧 SmoothDamp。
            shoulderFollow.DampingIntoCollision = 0f;
            shoulderFollow.DampingFromCollision = Mathf.Max(0f, shoulderProfile.ObstructionRecoveryTime);
            shoulderCamera.m_Lens = Cinemachine.LensSettings.FromCamera(camera);
            shoulderCamera.UpdateCameraState(Vector3.up, deltaTime);
            camera.transform.position = shoulderCamera.State.FinalPosition;
            // Core 可能跳过同帧重复零时间步，射线方向仍须立即使用最新输入。
            camera.transform.rotation = shoulderTarget.rotation;
            AppliedDistance = Vector3.Distance(camera.transform.position,
                shoulderTarget.position + Quaternion.Euler(0f, yawDegrees, 0f) * Vector3.right * offset);
        }

        /// <summary>地图拥有者在销毁玩家前调用；幂等释放，不销毁共享主相机。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            dialogueFocus = null;
            dialoguePresentationHidden = false;
            ResetShoulderAim();
            RestoreLocalPresentation();
            localRenderers.Clear();
            localCanvases.Clear();
            occlusion.Dispose();
            foreach (var entry in originalColliderTags)
                if (entry.Key != null && entry.Key.CompareTag("Player")) entry.Key.tag = entry.Value;
            originalColliderTags.Clear();
            playerColliders.Clear();
            if (shoulderRig != null)
            {
                shoulderRig.SetActive(false);
                if (Application.isPlaying) Object.Destroy(shoulderRig);
                else Object.DestroyImmediate(shoulderRig);
            }
            shoulderRig = null;
            shoulderCamera = null;
            shoulderFollow = null;
            shoulderTarget = null;
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private static bool IsFinite(Vector2 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    }
}
