# 第二关码头其他方向入场保护

用户确认：“入场过程一致：其他方向也先入场，进入战斗区域后才能被攻击”。本次针对第二关码头左侧栈桥与上方木通道（0/1）；右侧海路（2）保留既有登岸过程。其它关卡未扩展。

两个地面入口沿现有行走先进入中央平台，入场时不攻击、不受伤、不扣护甲和背桶耐久，也不阻挡子弹或成为追踪目标。身体完整进入平台后当帧恢复受伤，下一活动帧可追击/攻击；之后被击退回通道不会再次无敌。玩家堵住入口不会让敌人停在保护区攻击。暂停/加载/受控沿原活动门禁冻结。

平台边界依据正式制作源 `Tools/Unity/RemainingStagesPreview.cs` 的 `Deck(-1,0,26,23)`，即 X[-14,12]、Y[-11.5,11.5]，按敌人半径内缩落脚点。沿用现有导航和行走，无动画、美术、配置、协议或存档修改。瞬时保护状态由双方权威各自维护，客户端回池清零。

验证全部使用真实入口、非空限定过滤及 includePassingTests=true：

| 范围 | 最大数量 | 结果 |
| --- | ---: | ---: |
| Unity EditMode DockPassageEntryTests | 9 | 9/9 |
| Unity EditMode DockSeaEntryFlowTests | 15 | 15/15 |
| Unity EditMode DockBarrelFlowTests | 9 | 9/9 |
| 服务端 DockPassageEntryOnline | 9 | 9/9 |
| 服务端 DockSeaEntryOnline | 9 | 9/9 |

合计51项限定检查通过。客户端初始6项中5项RED（提前受伤/拦弹/攻击/未入场即解除），服务端初始8项中7项RED；对应GREEN均见日志。客户端后补强制位移保护，精确1项先失败后通过。独立评审发现炸弹直达死亡入口会绕过保护，两端补统一过滤，真实炸弹用例均先失败后通过，确保只清已入场敌人且只消耗一次。最终客户端9项以 `unity-entry-final.json` 为准，`unity-entry-green.json` 保留中间8项结果。

服务端命令：

```powershell
dotnet run --project Tools/DockPassageEntryOnlineChecks -- testClass=DockPassageEntryOnline maxTests=9 includePassingTests=true
dotnet run --project Tools/DockSeaEntryOnlineChecks -- testClass=DockSeaEntryOnline maxTests=9 includePassingTests=true
```

原海路客户端夹具修正真实出生函数新增的可选反射参数。独立复查通过，无剩余需修复项；最终9项客户端记录已保存。未跑完整套件、未打包、未进行真人双端试玩。开工Console有历史 EnemyView MissingReference 记录，本次限定检查全部通过，不声明清空历史Console。进度界面精简预览仍待采用，不属于本项。
