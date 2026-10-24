# 宝藏房奖励表现验证（2026-10-19）

> 此目录保留第一版模型和当时的验证记录。当前金属/宝石材质、散落小堆及最新验证见 [写实方向迭代](../TreasureRewardsRealistic-20261019/README.md)。下列动画参数和测试数量为旧版历史记录。

已实现：金条改为带倒角与压印的独立网格，宝石改为八向切面网格；沿用原资产 GUID 和模板节点。材质为独立 URP 资源，不修改其他物件共用的 Gold/Gem 材质。

金条和宝石在现有街机表现帧中轻浮 ±0.06 米、每秒旋转 18 度；动画只写模型实例，不修改奖励配置、水平落点、拾取判定或奖励数值。暂停时间不推进，剩余物品快照重绘保留视觉相位及权威渐隐。

房内武器、炸弹和冲刺的实际模型最长边校准为 1.1 米，底部对齐掉落根（地面上方 0.3 米）。只调整奖励房模型实例，不修改普通战场掉落、碰撞根、源 Prefab 或光柱。完整朝向包围盒记录见 [verification.txt](verification.txt)；旋转后的世界包围盒宽度可大于模型自身最长边。

领取成功时发射 6 颗短星芒（0.18—0.28 秒），全房共用一个实例，最多 32 颗粒子。沿用单人成功回调及联机个人成功回执；零奖励、满额拒收、重复回执、队友领取、快照物品移除或到期不会触发。离房、离图或加载失败释放。

## 验证

Unity EditMode 定向测试，均使用实际短 testClass，includePassingTests=true：

| 测试类 | 通过 |
| --- | --- |
| ArcadeTreasureRewardVisualTests | 5/5 |
| ArcadeTreasureRoomFlowTests | 29/29 |
| ArcadeTreasureRoomPresentationTests | 16/16 |
| ArcadeSupplyPickupTests | 16/16 |
| ArcadeObstacleVisualTests | 6/6 |
| ArcadeLootEffectsTests | 7/7 |

共 79 项通过。新增动画与闪光、尺寸校准检查均先观察到缺失行为的失败，再通过实现验证。未运行全套测试或 Player Build。测试结束恢复 Boot 场景及测试前时间倍率。

## 隔离资源预览

以下图像使用真实模板、配置和拾取 View，预览脚本位于 [ArcadeTreasureRewardPreview.cs](../../../Tools/Unity/ArcadeTreasureRewardPreview.cs)。用于检查模型、尺度、离地高度和特效遮挡，非实际双客户端游玩截图；手机帧率与真实双端游玩仍待验。

- [模型近景](models.png)
- [普通宝藏房](treasure-room.png)
- [军械库](armory.png)
- [领取闪光](pickup-flash.png)
