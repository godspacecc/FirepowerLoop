# 武器死亡力度分层验证 · 2026-10-20

已接入单人和联机权威死亡来源，画面层只消费已裁决结果，不改伤害、积分、掉落、活体位置或尸体预算。

| 最终伤害来源 | 死亡表现 |
| --- | --- |
| 火箭、榴弹、炸弹、爆炸桶等 | 沿用强爆炸抛飞、翻滚与血雾 |
| 玩家射线枪爆炸 | 降低水平速度、抛升与翻滚，保留爆炸血雾 |
| 玩家霰弹枪，命中位置距开枪时玩家位置 ≤ 3 米 | 沿弹丸方向低幅后倒，不产生爆炸血雾 |
| 其余普通击杀 | 沿用原受击、失衡倒地规则和方向 |

霰弹枪距离按弹丸生成时玩家位置与命中时敌人位置计算，不因开枪后换枪、移动或另一名玩家武器改变。只有最终一击触发死亡表现，非致命命中不额外位移活体。

双端共享 `ArcadeDeathImpactRules`。联机死亡消息新增 `ImpactKind`（0 原规则、1 能量爆炸、2 近距霰弹），由 `Tools/gen-protocol.ps1` 生成双端协议；快照、克隆、客户端不可变投影和可见实体移除完整传递。未知或矛盾类型在画面层退回原规则。

## 真实资源预览

`comparison.gif`：左侧强爆炸、中间射线枪能量冲击、右侧近距霰弹后倒。64 帧，20fps，循环末帧停留。静帧分别为死亡后 0.15、0.45、1.70 秒。

由 `Tools/Unity/WeaponDeathImpactPreview.cs` 调用真实 `EnemyCtrl.RemoveEnemy`，使用现有 `ArcadeZombieShambler.prefab` 与正式死亡特效池，在独立编辑器物理场景中逐步模拟。预览使用临时 CPU 蒙皮网格读取当帧骨矩阵，避免单次编辑器同步调用内渲染缓存；这些网格和地面参照线不进入运行时代码或资源。采样骨盆位置证实三档均正常落地。

预览未保存场景、修改 Prefab 或执行 Player Build。预览结束清理控制器、场景及临时资源；Boot 场景保持干净，残留 ChestMist/DeathDust 实例为 0。此预览不是双机实战或手机性能测试。

## 验证结果

所有测试显式指定非空范围，`includePassingTests=true`，未运行完整套件。

| 范围 | 声明上限 | 通过 |
| --- | --- | --- |
| Unity EditMode `EnemyDeathPresentationTests` | 24 | 17/17 |
| Unity EditMode `WeaponDeathPhysicsTests` | 6 | 4/4 |
| Unity EditMode `EnemyPhysicalReactionTests` | 40 | 31/31 |
| Unity EditMode `EnemyExplosionFeedbackTests` | 12 | 9/9 |
| 服务端 `WeaponDeathImpactOnline` | 9 | 9/9 |
| 服务端 `FlamethrowerOnline` | 7 | 7/7 |
| 合计 | — | **77/77** |

覆盖在途弹丸换枪、开枪后移动、3 米及 3.01 米边界、非致命命中、另一玩家武器独立性、消息克隆与 MemoryPack 编解码、死亡消息去重、力度和回池复位，以及原倒地与爆炸特效回归。记录见 `unity-tests.json`、`server-weapon-tests.log`、`server-flame-tests.log`。

代码复核发现普通弹丸死亡方向曾被一并修改，已收窄为仅近距霰弹使用弹丸方向，并补充远距霰弹保持原方向的断言，随后重新通过上述测试。目标文件 `git diff --check` 通过。

末次编辑器检查：本工程路径正确、非运行、非编译、Boot 未脏。Console 中 4 条错误来自本次诊断工具的反射方法名错误及旧 MCP 工具名调用，已读取来源；不作为游戏运行错误或“Console 零错误”证据。

提交范围仅包含本次死亡力度分层、协议、测试与预览；混合文件中的掉落规则改动保留在工作区，不纳入本次提交。

提交前再次定向复跑 `EnemyDeathPresentationTests`（17/17）、`WeaponDeathPhysicsTests`（4/4）及 `WeaponDeathImpactOnline`（9/9），合计 30/30 通过。新建 `.meta` 仅规范空字段的行尾空格，GUID 保持不变。
