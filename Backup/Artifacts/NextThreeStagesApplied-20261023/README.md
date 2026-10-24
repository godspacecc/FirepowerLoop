# 后续三关正式资源与表现

用户已采用三关布局v1、神庙/集市美术v1，以及墓园静态美术和墓穴爬出v2。正式地图接在农场后，顺序为墓园、神庙庭院、集市，新局13关52轮；已有40轮存档继续原路线。

本目录静态图从已保存的正式Prefab重新加载拍摄。动态采用正式资源和正式表现入口的隔离场景采样，主角原尺寸同框；它用于核对已采用外观与动作，不是自然对局或真人双端录像。真实战斗、伤害门禁、一次重生、清场奖励、出口和保存另由限定业务检查验证。

三关可播放动态见[正式Controller实拍](../NextStagesControllerApplied-20261023/README.md)。

| 场景 | 全景 | 主角同框近景 |
| --- | --- | --- |
| 墓园 | [全景](graveyard/overview.png) | [墓碑](graveyard/landmark.png)、[场地](graveyard/detail.png) |
| 神庙庭院 | [全景](temple/overview.png) | [石狮](temple/landmark.png)、[场地](temple/detail.png) |
| 集市 | [全景](marketplace/overview.png) | [摊位](marketplace/landmark.png)、[场地](marketplace/detail.png) |

墓穴起身后越过低墓沿再追击；为容纳原敌人0.95米碰撞半径，落脚比预览向前增加0.35米，保留墓碑、墓基、阻挡、方向和时长。暗影敌人先以乌鸦现身，首次打倒换位重生一次，第二次才最终消灭。集市保留八巷口分段来敌和重叠围攻，不新增方向提示。

持久化引用及候选对比见`persistent-assets-check.json`、`pixel-comparison.json`和`verification.txt`。没有运行资源打包、Player Build或完整测试套件。状态与最后检查以[执行总清单](../../../Docs/Plans/执行总清单.md)对应分区为准。
