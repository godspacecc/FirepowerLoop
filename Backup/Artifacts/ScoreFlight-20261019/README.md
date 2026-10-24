# 财宝飞入积分栏验证（2026-10-19）

领取财宝后，金币图标从财宝原始坐标飞向本人积分栏，约0.55秒到达。到达时积分栏显示真实增加后的总分，并在0.3秒内放大至约1.22倍后恢复；没有额外“+分数”文字。桌面与手机均取当前本人积分栏为目标，队友积分照常显示。

表现使用正式 `ArcadeHud.prefab` 中的 `CustomListView` 和外部 `ArcadeScoreFlightBox.prefab`，不阻挡输入。最多16个元素，超出后合并待显示增量；不修改实际奖励、倍率、加命或存档。隐藏、切图和回滚会取消旧动画、恢复数字比例及真实总分。空闲时不刷新动画列表或重复格式化分数。

单人普通掉落在归还对象池前保留坐标，宝藏房使用奖励账本坐标；联机使用权威拾取回执坐标，服务端创建及两次快照复制均保留位置。断线重连只建立基线，不重播历史拾取。

## 定向验证

每次均指定真实短 `testClass` 或完整 `testMethod`，声明数量上限并设置 `includePassingTests=true`，没有执行完整套件。

| 范围 | 最大数量 | 最终结果 |
| --- | ---: | ---: |
| `ArcadeScoreFlightTests`（EditMode） | 6 | 6/6 |
| `ArcadeSupplyPickupTests`（EditMode） | 16 | 16/16 |
| `ArcadeMultiplierHudTests`（EditMode） | 4 | 4/4 |
| `Game.Tests.ArcadeTreasureRoomFlowTests.TreasureScoreFeedbackPreservesRewardPosition`（EditMode） | 1 | 1/1 |
| `PickupFeedbackServerChecks` | 7 | 7/7 |

动画新增6项先失败后通过；补入真实单人／联机位置断言后2项失败，修复传参后16项通过。服务端快照位置断言先失败，补齐创建快照与克隆快照的位置复制后7项通过。服务端检查工程编译0警告、0错误，未启动服务器或进行 Player Build。

检查覆盖正式 Prefab 引用及无文字／无输入阻挡、桌面与手机落点、到达前后分数和回弹、32次密集拾取的上限及完整增量、隐藏／过图／重复清理、零值／无效输入、倍率及队友独立投影、一次领取／重复回执过滤，以及普通掉落和房间坐标。

## 画面证据

由 `Tools/Unity/ArcadeScoreFlightPreview.cs` 在隔离临时场景中使用正式 HUD 和正式 Box 渲染；原场景、架构和资源加载器均恢复。每套12帧，步长0.08秒，分辨率1920×1080。展示分数从10,200增加到10,450：

- [桌面飞行中（0.24秒）](pc-03.png)、[到达（0.56秒）](pc-07.png)、[回弹（0.72秒）](pc-09.png)、[恢复（0.88秒）](pc-11.png)。
- [手机飞行中（0.24秒）](phone-03.png)、[到达（0.56秒）](phone-07.png)、[回弹（0.72秒）](phone-09.png)、[恢复（0.88秒）](phone-11.png)。

这些是隔离 UI 预览与定向自动检查，不代表自然战斗路线、实体手机帧率或真实双客户端试玩已验收。
