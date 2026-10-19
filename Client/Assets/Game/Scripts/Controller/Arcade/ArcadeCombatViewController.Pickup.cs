using System;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>当前地图专有的合并状态及声音句柄，不与通用通知共享生命周期。</summary>
        private readonly ArcadePickupFeedbackBuffer pickupFeedback = new();
        private AudioPlaybackHandle pickupSound;

        /// <summary>积分与金币分别呈现真实增量；零增量不占用提示或声音。</summary>
        public void PlayTreasurePickup(long score, long gold)
        {
            if (score > 0 || gold > 0) PlayTreasurePickupFlash();
            if (score > 0) ShowPickup("score", score, total => Loc.T("ui.arcade.hud.score", "+" + total));
            if (gold > 0) ShowPickup("gold", gold, total => Loc.T("ui.arcade.hud.gold", "+" + total));
        }

        /// <summary>通过正式产品 ID 解析现有名称，不从美术表现猜测道具。</summary>
        public void PlayProductPickup(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId)) return;
            PlayTreasurePickupFlash();
            ShowPickup("product:" + productId, 1,
                total => Loc.T("tip.item_gained", Loc.T($"ui.arcade.product.{productId}.name"), total));
        }

        /// <summary>成功数值先合并，再更新对应提示；音频经过正式音量、地图归属及加载期限控制。</summary>
        private void ShowPickup(string key, long amount, Func<long, string> format)
        {
            if (!IsActive || !pickupFeedback.TryAdd(key, amount, Time.unscaledTimeAsDouble, out var tipKey, out var total)) return;
            Notify.ShowFloatTips(format(total), ArcadePickupFeedbackBuffer.LifetimeSeconds, tipKey, true);
            if (!pickupFeedback.TrySound(Time.unscaledTimeAsDouble)) return;
            var audio = Game.Interface.GetSystem<AudioSystem>();
            if (audio == null || mapRoot == null || !TryGetPlayerPosition(out var position)) return;
            audio.Stop(pickupSound);
            pickupSound = audio.PlaySoundAt("FoleyCollectItem", mapRoot.TransformPoint(new Vector3(position.x, 0, position.y)),
                18f, 5, new SoundPlaybackOptions(.65f, 1f, .12f));
        }

        /// <summary>Begin、End、重试与离图统一取消可见及待显示提示、加载中和播放中的音效。</summary>
        private void ClearPickupFeedback()
        {
            ClearTreasurePickupFlash();
            pickupFeedback.Clear();
            UIHelper.GetUICtrl<FloatUICtrl>()?.RemoveTipsByPrefix("arcade.pickup.");
            if (pickupSound.IsValid) Game.Interface.GetSystem<AudioSystem>()?.Stop(pickupSound);
            pickupSound = AudioPlaybackHandle.Empty;
        }
    }
}
