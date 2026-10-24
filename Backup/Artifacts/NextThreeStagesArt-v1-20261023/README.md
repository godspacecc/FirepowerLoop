# 墓园、神庙庭院、集市：正式美术候选 v1

用户已采用 NextThreeStages-v1 的三关布局与特色动作，随后确认追加在农场之后，保留旧后续，整局13关52轮。本页展示可供正式制作采用的具体外观，尚未接入正式资源。

**Unity独立临时场景实拍，使用正式主角、AK及丧尸原尺寸作对照。不是自然对局录像，也不是正式接入效果。** 所有新网格和材质仅在内存创建；正式Prefab、Config和运行时代码没有由本工具修改。

| 关卡 | 全场外观 | 主角同框物件近景 | 动态 |
| --- | --- | --- | --- |
| 墓园 | [全场](graveyard/overview.png) | [墓碑与主角](graveyard/landmark.png)、[场地近景](graveyard/detail.png) | [出土追击](graveyard/motion.gif) |
| 神庙庭院 | [全场](temple/overview.png) | [石狮与主角](temple/landmark.png)、[场地近景](temple/detail.png) | [乌鸦现身、蓝影换位](temple/motion.gif) |
| 集市 | [全场](marketplace/overview.png) | [摊位与主角](marketplace/landmark.png)、[场地近景](marketplace/detail.png) | [巷口围攻](marketplace/motion.gif) |

- 墓园：冷灰旧石、几种碑头与浅刻纹、铁栏石墙、旧小教堂和柏树；外围岩层连续封边，中央十字通路及外圈绕行保持。
- 神庙：风化石砖、少量青绿镶边、雕刻门柱与带鬃毛/前爪的低面数石狮；黑色乌鸦扇翼进入，蓝色碎光显形与换位，最终消散。暗影敌人沿用现有丧尸体型的深色候选，不另换角色尺寸。
- 集市：暖色灰泥房、坡瓦顶、铁窗、红/青绿条纹棚与蔬果木台；建筑在外侧，主要摊位坐标和八处巷口沿用已采用布局，中央通路与外圈保留。

动作是手工采样示意，尚未运行真实出土伤害门禁、暗影复活/计分、寻路或随机批次业务。集市24个角色和顺序是展示抽样，不是正式人数/固定方向。正式规则依已采用v1，不增加方向HUD、箭头、专属Boss或竞速。

## 生成与核查

- `Tools/Unity/NextThreeStagesArtPreview.cs`：只创建附加临时场景，关闭并释放自建材质/网格，恢复原活动场景和RenderTexture；不会保存Prefab/场景、修改正式材质或TimeScale。角色仅为拍物件近景移动位置，拍完恢复；尺寸保持。
- `Tools/Unity/encode-next-three-stages-art-preview.py`：仅复制9张Unity原图与编码原始帧，不重绘。原始帧在忽略目录`Client/Temp/NextThreeStagesArt-v1`。
- 出土84源帧/68 GIF帧、7秒；神庙及集市各96源帧/96 GIF帧、8秒，平均12fps、960×600、无限循环。
- 已核对三张全场、主角同框物件近景与关键动态帧。渲染后本工程Boot未脏、编辑态、无编译失败、仅1个场景，TimeScale仍为0。没有执行测试套件、Player Build或打包，不声明真实玩法验收。
- 只读审查发现新乌鸦翼面/尾部未分到预览相机层；已在Form设置29层并重录神庙，实际关键帧可见完整翼面和尖尾。屋顶朝外三角面也已修正并重录墓园/集市。

采用记录：三关布局/特色玩法及13关52轮路线已确认；用户已采用神庙、集市本页具体美术并授权正式接入。墓园沿用本页静态外观，空地出土被[墓穴爬出v2](../GraveyardTombs-v2-20261023/README.md)替代，用户已明确“采用，正式接入”。正式接入证据集中在[正式资源实拍](../NextThreeStagesApplied-20261023/README.md)，实施状态以执行总清单本任务分区为准。
