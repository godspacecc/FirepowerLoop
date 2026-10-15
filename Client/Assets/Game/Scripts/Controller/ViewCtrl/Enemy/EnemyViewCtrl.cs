using System;
using System.Text;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 接管场景内敌人表现对象，使战斗和 AI 系统通过端口访问敌人表现。
    /// </summary>
    public sealed class EnemyViewCtrl : IViewCtrl, IEnemyNavigationPort
    {
        private readonly ViewCtrlRegistry registry;
        private readonly IViewInstanceProvider instanceProvider;
        private EnemyView view;

        public EnemyViewCtrl(
            EnemyView view,
            int unitId,
            long mapInstanceId,
            ViewCtrlRegistry registry,
            IViewInstanceProvider instanceProvider)
        {
            this.view = view != null
                ? view
                : throw new ArgumentNullException(nameof(view), "敌人表现控制器创建失败：View 不能为空");
            if (unitId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(unitId), "敌人表现控制器创建失败：UnitId 必须大于 0");
            }

            if (mapInstanceId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(mapInstanceId), "敌人表现控制器创建失败：MapInstanceId 必须大于 0");
            }

            this.registry = registry ?? throw new ArgumentNullException(nameof(registry), "敌人表现控制器创建失败：Registry 不能为空");
            this.instanceProvider = instanceProvider ?? throw new ArgumentNullException(nameof(instanceProvider), "敌人表现控制器创建失败：InstanceProvider 不能为空");
            if (!registry.TryRegister(this, out var viewCtrlId, out var error))
            {
                throw new InvalidOperationException(error);
            }

            ViewCtrlId = viewCtrlId;
            UnitId = unitId;
            MapInstanceId = mapInstanceId;
            this.view.ConfigureRuntime(unitId, mapInstanceId);
        }

        public int ViewCtrlId { get; }
        public int UnitId { get; }
        public long MapInstanceId { get; }
        public bool IsDisposed { get; private set; }
        public bool IsReleased => IsDisposed;
        public bool IsValid => TryGetLiveView(out _);
        public Vector3 Position => TryGetLiveView(out var liveView) ? liveView.transform.position : Vector3.zero;
        public Quaternion Rotation => TryGetLiveView(out var liveView) ? liveView.transform.rotation : Quaternion.identity;

        public IArchitecture GetArchitecture()
        {
            return Game.Interface;
        }

        public void SetVisible(bool visible)
        {
            if (TryGetLiveView(out var liveView) && liveView.gameObject.activeSelf != visible)
            {
                liveView.gameObject.SetActive(visible);
            }
        }

        public void ConfigureNavigation(float moveSpeed, float stoppingDistance)
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.ConfigureNavigation(moveSpeed, stoppingDistance);
            }
        }

        public void SetHealthDisplay(int current, int maximum)
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.SetHealthDisplay(current, maximum);
            }
        }

        public void SetHealthBarVisible(bool visible)
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.SetHealthBarVisible(visible);
            }
        }

        public void PlayHitFeedback()
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.PlayHitFeedback();
            }
        }

        public void PlayAttackAnimation()
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.PlayAttackAnimation();
            }
        }

        public void ShowDamage(int damage)
        {
            if (TryGetLiveView(out var liveView)) liveView.ShowDamage(damage);
        }

        public void MarkDead()
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.MarkDead();
            }
        }

        public void MarkOnlineDead(){if(TryGetLiveView(out var liveView))liveView.MarkOnlineDead();}
        public void ReviveOnline(){if(TryGetLiveView(out var liveView))liveView.ReviveOnline();}
        public void SetNavigationEnabled(bool enabled){if(TryGetLiveView(out var liveView))liveView.SetNavigationEnabled(enabled);}
        public void Snap(Vector3 position,float yaw){if(TryGetLiveView(out var liveView)){liveView.transform.position=position;liveView.transform.rotation=Quaternion.Euler(0f,yaw,0f);}}
        /// <summary>离散定位后清除本帧位移基准；连续插值 Snap 不调用。</summary>
        public void ResetMovementSampling(){if(TryGetLiveView(out var liveView))liveView.ResetMovementSampling();}

        public void SetAlerted(bool alerted)
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.SetAlerted(alerted);
            }
        }

        public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin)
        {
            if (TryGetLiveView(out var liveView))
            {
                return liveView.TryGetProjectileOrigin(direction, out origin);
            }

            origin = Vector3.zero;
            return false;
        }

        public bool IsOnNavigableSurface(float sampleRadius)
        {
            return TryGetLiveView(out var liveView) &&
                   liveView.IsOnNavigableSurface(sampleRadius);
        }

        /// <summary>转发只读导航诊断；已释放或丢失表现时明确报告缺失，不重建实例。</summary>
        internal void AppendNavigationDiagnostic(StringBuilder text, float sampleRadius)
        {
            var valid = TryGetLiveView(out var liveView);
            text.AppendLine($"viewExists={liveView != null} viewCtrlValid={valid}");
            if (valid) liveView.AppendNavigationDiagnostic(text, sampleRadius);
        }

        public bool TrySetRetreatDestination(Vector3 requestedPosition, float sampleRadius, out Vector3 destination)
        {
            if (TryGetLiveView(out var liveView))
            {
                return liveView.TrySetRetreatDestination(requestedPosition, sampleRadius, out destination);
            }

            destination = Vector3.zero;
            return false;
        }

        public void SetTelegraphVisible(bool visible)
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.SetTelegraphVisible(visible);
            }
        }

        public void SetActionProgressVisible(bool visible, float progress01 = 0f, string iconAddress = null)
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.SetActionProgressVisible(visible, progress01, iconAddress);
            }
        }

        public void FaceWorldHuds(Vector3 cameraPosition, Vector3 cameraUp)
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.FaceWorldHuds(cameraPosition, cameraUp);
            }
        }

        public void SetDestination(Vector3 position)
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.SetDestination(position);
            }
        }

        public void Stop()
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.Stop();
            }
        }

        public void TickCombatFeedback(float deltaTime)
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.TickCombatFeedback(deltaTime);
                liveView.TickCorpseSearch();
            }
        }

        public void Release()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            var currentView = view;
            view = null;
            IsDisposed = true;
            registry.Unregister(ViewCtrlId);
            if (currentView != null)
            {
                currentView.SetTelegraphVisible(false);
                currentView.SetActionProgressVisible(false);
                instanceProvider.Release(currentView.gameObject);
            }
        }

        private bool TryGetLiveView(out EnemyView liveView)
        {
            liveView = view;
            return !IsDisposed && liveView != null;
        }
    }
}
