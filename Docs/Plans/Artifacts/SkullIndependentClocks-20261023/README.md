# 骷髅强化独立计时修复（2026-10-23）

用户在确认“临时武器到期只切回基础枪、强化继续自己的余时；换不同武器也保留强化”后明确要求“开始修复”。本次不修改新外观、HUD设计、配置、协议或存档格式。

单人与联机各移除两处强制清除：临时武器到期，以及领取不同武器。原强化自身到零移除、同枪升档与补时、红不降紫、伤害/射速/紫移速、军械库武器保时、等待出口禁火均保留。

## 验证证据

- RED：`solo-red.json` 为实际 ArcadeSkullPowerTests 16/20，换枪、红紫武器先到期和节点恢复共4项捕获旧行为；`online-red.txt` 为 SkullPowerOnline 6/9，对应换枪、武器先到期、节点恢复失败。
- GREEN：`solo-green.json` 20/20，`solo-upgrade-green.json` 7/7；`online-green.txt` 9/9，`online-upgrade-green.txt` 6/6，共42项定向检查全部通过。
- 所有 Unity 运行均精确短 testClass、EditMode、includePassingTests=true，各次上限20/7；服务器入口精确 testClass=SkullPowerOnline/WeaponUpgradeOnline、includePassingTests=true、maxTests=9/6。没有完整套件或无效过滤回退。
- 真实例子覆盖武器5秒/红紫强化15秒：武器到期后基础枪保持强化10秒，然后伤害、射速与弹体标记恢复。反向到期仍保留临时武器；换枪保留原有强化余时而不补时；已发弹丸不改写；队友不继承；轮级存档恢复继续扣自己的余时。
- 联机初次 GREEN 为8/9：测试在已进入 AwaitingExit 的禁火阶段再次调用 Fire，被既有规则拒绝。测试改为进入等待前检查强化到期后的普通弹，等待期间只检查时钟，重新9/9。业务禁火规则未改。另补正式快照→Clone→MemoryPack编解码，实际断言临时武器0秒、紫强化10秒和旧紫弹等级；同一限定类重新9/9。
- `unity-final-state.json` 核对 FirepowerLoop/Client、Boot未脏、编辑态、无编译与活动测试、TimeScale=1，当时Console错误0、警告27。未清除Console，未进行Player Build、资源打包或真人双客户端验收。

独立只读审查未发现P1/P2/P3，限定差异空白检查通过。提交仅包含本项；同一System文件中其他会话的近战前摇修改按独立差异保留。
