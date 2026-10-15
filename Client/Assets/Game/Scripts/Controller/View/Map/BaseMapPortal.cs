using System;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Game
{
    [RequireComponent(typeof(Collider))]
    public sealed class BaseMapPortal : MonoBehaviour, IController
    {
        [SerializeField]
        private string targetMapId = MapIds.SurvivalSample;

        private bool entering;

        public IArchitecture GetArchitecture()
        {
            return Game.Interface;
        }

        private void Reset()
        {
            var trigger = GetComponent<Collider>();
            if (trigger != null)
            {
                trigger.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (entering || other.GetComponentInParent<PlayerView>() == null)
            {
                return;
            }

            EnterAsync().Forget();
        }

        private async UniTask EnterAsync()
        {
            entering = true;
            var expeditionModel = this.GetModel<ExpeditionModel>();
            var requestedMapId = targetMapId;
            try
            {
                var result = await this.GetSystem<BaseMapSelectionSystem>()
                    .EnterMapAsync(requestedMapId);
                if (result.Success && expeditionModel.IsActive)
                {
                    UIHelper.ShowUI<ExpeditionHudUICtrl>();
                    return;
                }

                if (this == null)
                {
                    return;
                }

                if (!result.Success)
                {
                    Log.Error($"[BaseMapPortal] 进入远征失败：{result.Error}");
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                if (this != null)
                {
                    Log.Error($"[BaseMapPortal] 进入远征异常：mapId={requestedMapId} exception={exception}");
                }
            }
            finally
            {
                if (this != null)
                {
                    entering = false;
                }
            }
        }
    }
}
