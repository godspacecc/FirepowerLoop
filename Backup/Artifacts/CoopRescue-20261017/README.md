# 双人救援与捐命验证产物

2026-10-17。产品规则、分段提交与审查修正见[实施记录](../../../Docs/Plans/合作救援与捐命实施验证-20261017.md)。仅 FirepowerLoop 自有临时环境，无 AnotherDay 或正式玩家档。

## 最新定向范围

全部使用真实 testClass、下列最大数量及 includePassingTests=true；没有完整套件或 Player Build。

| 环境 | testClass | 最大／实际通过 |
| --- | --- | --- |
| 服务端进程 | CoopRescueOnline | 27／27 |
| 服务端进程 | CoopDonationOnline | 20／20 |
| 服务端进程 | SlayerMartProtocolGate | 10／10 |
| 服务端进程 | SlayerMartRoundOnline | 17／17 |
| 服务端进程 | SlayerMartRpcOnline | 8／8 |
| 服务端进程 | SlayerCheckpointOnline | 8／8 |
| Unity EditMode | CoopRescueClientTests | 10／10 |
| Unity EditMode | OnlineArcadeRunTests | 18／18 |
| Unity EditMode | HotKeySettingsTests | 10／10 |
| Unity EditMode | SlayerMartOnlineFlowTests | 20／20 |

结果在 Checks。Unity Summary.TotalTests 是发现总数，不是执行数量；实际范围以 Results 及 PassedTests 为准。服务端检查含真实临时轮节点写盘及捐命候选回滚身份，不能代替两台真实客户端实连。

## HUD 隔离预览

使用正式 Controller、Prefab 与语言表投影示例权威帧，不连接服务器、不进入 Play、不读写玩家存档。HudPreview 中三图已查看；对应 txt 记录新倒地状态与捐命按钮文案未溢出。

- 中文 1920×1080：本人存活，可向倒地队友捐命。
- 中文 2340×1080：本人倒地，显示救援中和剩余量。
- 英文 1280×720：英文提示及 R 键按钮。

实际双客户端倒地、靠近、主动捐命、跨场准备、断线／重启，以及实体触屏与手柄仍待验收。已实现功能从“尚未实现”移至“已实现待实际验收”，不勾选上述验收。
