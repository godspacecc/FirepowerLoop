# 已采用码头海中攀爬v2正式接入

用户确认：2026-10-25“这个也接入啊”，上下文为DockSeaEntry-v2-20261024，正式接入已授权。按项目规则直接执行，不再审批技术方案。

1. 用真实DockSeaEntryFlowTests先复现右路仍在旧通道出生、无抓边阶段及撤通道后水面仍可走。共享ArcadeDockSeaEntryState只保存Started/Age/Lane，已确认艺术路径由ArcadeDockSeaEntryRules提供；正常战斗只在活动帧推进，停步/暂停不由表现计时。
2. 单人与权威服务端在原右路出生预算内设定海中起点，进场期间保持既有可受伤规则、不攻击、不接受拥挤/击退位置纠正；完成攀爬并走进实体半径可站稳的边缘内后接回现有追击。另两路与原血量/数量/紫桶/电塔规则保持。
3. 只改Config权威CSV四张码头图的右入口和新增仅行走水域阻挡；轮前节点不含现场位置，保留布局身份/版本和账本。Config/gen.bat双端生成；协议追加Started/Age/Lane，由Tools/gen-protocol.ps1生成，客户端只读复制及服务端深复制齐全。
4. 将已采用抓边/抬腿/蹬起骨骼姿态烘焙为正式片段，三种丧尸由EnemyView在既有LateTick采样当前权威年龄；水面高度/倾斜归视觉根，不改业务位置。结束接已正式采用GroundWalk v2，受击/死亡/隐藏/回池复位。背桶附属物随视觉根，原5秒卸桶继续。
5. 码头正式网格按采用版只过滤右侧低位通道三角形，保留mesh/meta及其它资源；重制入口应用同过滤。正式回读与单人/服务端/协议/表现限定检查后独立审查，仅本项中文提交推送origin/main。原始预览、正式回放和日志均保存Backup/Artifacts独立批次；不打包或完整套件。

验证上限逐次执行前声明：初始DockSeaEntryFlowTests最多3项，扩展后最多10；DockSeaEntryVisualTests最多10；DockSeaEntryOnline最多10。相邻仅实际相关的DockBarrelFlowTests、GroundBarrelFlowTests、ArcadeZombieGroundWalkTests、ArcadeZombieGrabVisualTests及单个保存方法，先发现真实方法再限定。所有检查includePassingTests=true，过滤为空停止。
