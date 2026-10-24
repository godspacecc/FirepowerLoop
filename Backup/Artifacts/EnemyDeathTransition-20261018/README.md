# 普通死亡失衡与躯干驱动布娃娃

2026-10-18。用户确认优化现有死亡表现：普通死亡先短暂失衡，爆炸主要推动躯干，避免全身僵硬地同时飞出，保持手机性能。沿用EnemyView／EnemyCtrl表现生命周期和原骨架，不改伤害、死亡裁决或协议。

普通死亡保留Animator约0.16秒，停止开火与行走，复用Hit反馈，并在动画求值后叠加胸部前倾、骨盆下沉。每帧撤销上一帧叠加，避免累计。最后一次LateUpdate直接把当前失衡姿态交给物理，不先恢复站姿。随后模拟1.4秒后冻结，仍在死亡后2.2秒回池。

爆炸死亡立即进入物理，主要速度只赋给骨盆和胸部，二者旋转也不同；四肢初始速度为零，由关节带动。保留1.8秒物理模拟窗口。普通死亡也不再统一赋速或整具向上抛。

手机仍最多4具／44刚体、桌面8具／88刚体；每具11刚体／10关节，求解次数不变。没有新增逐敌人Update、FixedUpdate或运行时骨架制作；碰撞体只缓存一次。测试预热后连续50轮普通过渡、爆炸与回池的托管分配为0字节；这不代替手机物理帧耗测量。

## 定向验证

| 真实过滤范围 | 结果 |
| --- | ---: |
| EnemyPhysicalReactionTests | 24/24 |
| ArcadeEnemyVisualTests | 14/14 |
| HeavyEnemyVisualTests | 4/4 |
| EnemyDeathPresentationTests | 8/8 |

累计50项，includePassingTests=true，未运行完整套件。先看到新增测试中10项因首帧动画被关闭、全身同速而失败，再实现并回归。覆盖四种正式Prefab、交接姿态、暂停／重复死亡、中途回池、长帧收尾、真实控制器LateUpdate、尸体预算与回收、托管分配。完整结果见[test-results.json](test-results.json)。

## 隔离物理观察

Tools/Unity/EnemyDeathTransitionPreview.cs使用独立PreviewScene，四种Prefab各演示普通／爆炸死亡，共8具88刚体、120个固定步。手动按Tick→Animator.Update→LateTick→PhysicsScene.Simulate顺序推进。普通死亡首帧保持动画、爆炸仅躯干带初速度均检查通过；爆炸最大抬升约0.827，2秒时最高髋骨约0.157，数值有限、冻结稳定、回池休眠。[数值](verification.json)。

- [0.08秒：普通死亡保留失衡过渡](02-normal-stagger-080.png)
- [0.3秒：躯干带动倒地／击飞](03-death-and-explosion-300.png)
- [0.8秒：关节带动的翻倒姿态](04-death-and-explosion-800.png)
- [2秒：落地并冻结](05-frozen-corpses-2000.png)

旧EnemyPhysicalReactionPreview工具也补上Animator与LateTick的实际推进，以兼容新的过渡阶段；初版证据保留不覆盖。只读审查未发现阻断问题。最终隔离观察后Boot保持未修改，未出现新的运行错误。

手机真机帧率、自然长时间战斗与真实双客户端观感待验。未进行Player Build或提交推送。
