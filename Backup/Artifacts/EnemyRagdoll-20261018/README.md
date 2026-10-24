# 敌人局部回弹与死亡布娃娃验证

日期：2026-10-18。覆盖正式Basic、Archer、Brute、BossArcher四种人形Prefab；无骨架特殊外观沿用原有死亡表现。

存活命中叠加胸部回弹，不开启物理或改变战斗根。死亡启用11刚体／10关节；普通1.4秒、爆炸1.8秒后冻结，2.2秒回池。手机最多4具、桌面8具，超预算提前归还最早一具。尸体只碰专用地面，不碰玩家、弹丸或其它尸体。死亡移除独立挂载武器，复用通过原初始化重新绑定。

单人与服务器区分普通范围伤害和真正爆炸，覆盖炸弹、火箭／榴弹、地面桶、载桶连锁及环境爆炸。联机只传方向、爆炸标记及轮代次，最多24条事实；不传骨骼姿态，重复快照、换轮和重连基线不补播历史死亡。

## 定向验证

| 范围 | 最终结果 |
| --- | --- |
| EnemyPhysicalReactionTests | 11/11 |
| EnemyDeathPresentationTests | 8/8 |
| ArcadeEnemyVisualTests | 13项原通过，契约单项修复后1/1，共14 |
| HeavyEnemyVisualTests | 4/4 |
| ArcadeBombPresentationTests | 6/6 |
| ArcadeOpeningFlowTests | 24/24 |
| Tools/EnemyPhysicalFeedbackChecks | 6/6 |
| DotNet.Hotfix.csproj编译 | 0警告、0错误 |

EditMode累计67项，均通过真实非空类／完整方法过滤，includePassingTests=true。工具Summary.TotalTests是发现总量，实际数量以Results和PassedTests／FailedTests为准；未运行全套。[结果与契约失败后修复记录](test-results.json)。服务端检查执行 `dotnet run --project Tools/EnemyPhysicalFeedbackChecks/EnemyPhysicalFeedbackChecks.csproj --no-restore -- E:\GitHub\FirepowerLoop`，不启动服务器或操作存档。

## 物理观察

通过Tools/Unity/EnemyPhysicalReactionPreview.cs在独立Editor PreviewScene中手动模拟8具／88刚体，共120个固定步。正常组与爆炸组各包含四种敌人。爆炸最大抬升0.719，普通0.006；2秒时最大髋骨高度0.166，随后20步保持冻结；回池刚体、碰撞和骨骼复位检查通过。[原始数值](verification.json)。

- [普通命中胸部回弹](01-local-hit-recoil.png)
- [死亡后0.30秒：爆炸组抬升](02-death-and-explosion-030.png)
- [死亡后0.80秒](03-death-and-explosion-080.png)
- [死亡后2秒：落地冻结](04-frozen-corpses-200.png)

before-fix-frozen-120.png只保留为修复前历史证据：旧冻结时机过早，独立武器尚未隐藏；不代表当前效果。当前预览释放RenderTexture前已解除相机引用，结束Console无新增Error、Boot未变脏。

以上是隔离物理观察与定向检查。手机真机帧率、长时间战斗和真实双客户端联机效果待验。未触发Player Build，未提交或推送。
