# 积分商店正式接入与验证

2026-10-23。用户采用v2视觉和价格后实施；原物品效果不增强，刷新固定500积分。

## 玩家实际流程

普通整关结束后，每人获得三个不同的现有空投商品。最多购买一件，也可跳过；购买或刷新扣除当前积分。商品到下一普通关首次入场领取，使用原空投时长；购买及加载时不消耗时长。双方各自完成后才可准备下一关或挑战。首关开场和最终胜利不额外出商店，旧规则存档不追加商店。

已得生命保留；扣分推迟下一次加命。最高已处理档位随分数保存，跨回旧档不重复加命。刷新、购买和交付均先保存再公布，失败保留原节点；重开不免费刷新，网络未知结果复用原操作。

## Unity实际界面

以下是正式Prefab、生成View和真实Controller报价在隔离Unity场景中的实拍，使用临时存档，不是网页概念图，也不代表自然整关实玩：

- [中文实际界面](unity-applied.png)
- [英文实际界面](unity-applied-en.png)
- [100积分不可购买、仍可跳过](unity-applied-low-score.png)

复现工具：`Tools/Unity/ScoreShopAppliedCapture.cs`。临时场景和存档结束后释放，没有修改Boot正式场景。

## 限定验证

完整结果见[Unity验证原始记录](unity-verification.json)。仅运行真实短testClass，全部includePassingTests=true，没有运行完整套件或Player Build。

| EditMode类 | 上限 | 通过 |
|---|---:|---:|
| ScoreShopRulesTests | 7 | 7 |
| ScoreShopLifeTests | 3 | 3 |
| ScoreShopRunTests | 8 | 8 |
| ScoreShopDeliveryTests | 3 | 3 |
| ScoreShopUiTests | 3 | 3 |
| ScoreShopControllerTests | 4 | 4 |
| ScoreShopOnlineAdapterTests | 2 | 2 |
| ArcadeTreasureRoomRulesTests | 40 | 32 |
| RoundCheckpointTests | 20 | 19 |
| ArcadeRoundTransitionTests | 5 | 5 |

合计86项通过。覆盖正式全配置开局、真实单人文件重载、购买/刷新失败不变账、旧请求不重扣、首轮效果和Delivered同存、待领取不能被轮节点跳过、终局、旧规则兼容、按钮重开不叠加、低分跳过、固定语言键及实际序列化引用。

联机39项本任务及相邻限定检查、编译与共享工作区额外路线检查见[联机记录](online-verification.md)。最终从68cd6351基线与本任务独立暂存内容导出无Git、无Library的临时源码副本，重新执行Config/gen.bat与Tools/gen-protocol.ps1，正式生成协议及配置；Rebuild成功、0警告0错误。严格限定ScoreShopOnline最多16、SlayerMartProtocolGate最多10、RoundRewardOnline最多7、SlayerMartReleaseGateOnline最多6，均includePassingTests=true，分别16/16、10/10、7/7、6/6通过。保留基线已提交的联机反馈修复，未包含其他会话未提交的路线、进化或额外无人机代码。

最后核对Unity连接路径为本项目Client，Boot未脏、未编译中、compilationFailed=false。Console三条历史错误均来自其他会话ZombieUnarmedActionPreview的Avatar为空，没有把Console说成清洁。Git手写源文件差异检查通过；Unity序列化Prefab、meta及Luban生成代码保留生成器输出的空值尾空格／尾空行。

## 独立审查与修复

一次独立只读审查，无Critical、无Minor，发现一项Important：购买超时但服务器已完成时，旧pending可锁住同局下一关报价。两个adapter用例先0/2失败；记录访问关卡，并在本人完成或新访问报价到达时清理旧意图后2/2通过。断线本身仍保留未知请求，不会借断线重新购买。UI明确调用原意图重试入口。

审查未评价其他会话地图/武器进化/额外无人机/路线玩法、正式画面实际运行、真人双端延迟和价格实战。前两项相关UI由本任务上述实拍和EditMode补证；其他玩法维持独立任务；未执行真人双端或自然整关平衡实玩。既有单人保留跨整关临时效果、服务端清理旧效果的差异维持现状，商店均沿用各自原ApplyDrop，不新增跨整关效果继承规则。

## 平衡判断与实施取舍

- 保留用户采用的3000/4000/5000/6000积分及原14～25秒时长；每关最多一项，作为偏友好的首版。500刷新在高倍率时很便宜，当前不擅自涨价。离线收入/DPS估计见balance-audit.json，不能当实测收入或最终平衡结论。
- 依用户规则沿用当前工作区，未创建worktree；代价是提交必须逐hunk隔离并验证。
- 共享规则稳定后将联机域独立分工，最终集成和Unity串行检查由主实施者完成；代价是需核对边界和生成协议。
- 领取单元测试只读取正式Drop目录，避开其他会话临时路线校验故障；完整开局另由ScoreShopRunTests的正式provider验证，最终8/8通过。代价是不能把领取单测单独视为整局通过。
- 全配置验证时修复固定主题配置编号被错误按Boss序号取模的问题：主题关按独立轮布局校验，避免24号配置误要求Boss；普通编号原校验保留。代价是固定主题Boss结构仍须由其轮布局验证。
- 新正式休闲局明确记录商店版本；旧档与旧顺序模式不追加新玩法，避免旧模式没有轮前保存而丢失临时枪。代价是旧局不会中途出现新商店。

未留待处理的重要发现或Minor；没有把离线模型和自动检查说成真人实玩。
