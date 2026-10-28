# 第二关水中敌人不可攻击

2026-10-28，用户明确要求第二关水里的敌人不能被攻击。正式第二关为码头配置21，其第三通道已有独立游近/抓边/攀爬状态。原实现允许全程受伤，导致水中提前击杀。

现改为：既有进场状态启动且活动年龄小于5.75秒时不能受攻击；到原双脚登台节点立即恢复受伤，不额外等待6.75秒动作末段结束。其它在木板上的通道不受影响。冻结、暂停及回池沿用原状态生命周期。

- 共享状态只提供可攻击资格，客户端和服务器分别在统一伤害入口拒绝生命/护甲/火焰标记变化，并保护背桶耐久。
- 普通弹丸、紫基础/穿透扫掠、紫榴弹、紫射线首目标/连锁及独立紫火箭碰撞跳过水中目标；紫火箭选敌与已有目标校验也排除水中目标，避免水中空爆或浪费弹体。
- 不改配置、协议、正式视觉资源、存档或机关规则；原上岸动作不改。核心规则见ArcadeDockSeaEntryRules.CanBeAttacked。

限定检查均includePassingTests=true，未运行完整套件/Player Build/真实双客户端实玩：

| 范围 | 上限与最终通过数 |
| --- | --- |
| DockSeaEntryFlowTests | 初版11、扩展后15；最终15/15 |
| ArcadePurpleRayTests | 7；7/7 |
| ArcadePurpleRocketTests | 8；8/8 |
| ArcadeWeaponEvolutionTests 的 PurpleBasicContinuesOnlyAfterKillAndStopsOnSurvivor、PurplePiercingFourthVolleyHitsWiderLine、PurpleGrenadeSplitsOnceAndChildrenDoNotSplitAgain、FuseFrameStillHitsBarrelBeforeEndpoint | 各1；4/4 |
| DockSeaEntryOnline（Tools/DockSeaEntryOnlineChecks） | 9；9/9 |

证据：[原始水中失败](client-red.json)、[独立紫火箭失败](rocket-red.json)、[最终15项](client-green.json)、[射线](ray-regression.json)、[火箭](rocket-regression.json)、[进化弹体](evolution-regression.json)、[服务器最终](server-green.log)。服务器初次8/9因测试仍假设满耐久桶必须有字典条目；正式逻辑无条目表示默认满血，测试更正后9/9。首次客户端因临时Boot脏状态被拒绝，没有执行测试；按AGENTS允许重载Boot后使用相同明确范围运行。

独立只读审查指出紫火箭独立路径遗漏，已真实RED→GREEN修复，复审通过无剩余问题。总计客户端34项、服务器9项通过。Unity结束Boot编辑态未脏；Console原有测试夹具/资源错误保留，不作为新产品异常。

水体是独立待采用的视觉任务，见[8秒动态候选预览](../DockWaterPreview-20261028/index.html)。本批仅制作和归档预览，没有正式接入候选水体。规则修复与预览材料按项目授权选择性中文提交推送origin当前分支，实际Git结果为准。
