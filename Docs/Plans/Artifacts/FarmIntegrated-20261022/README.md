# 农场正式接入隔离录制

来源：FarmArt-v1、farm-v3和牛v1均获用户明确“采用这版”及正式接入确认。

- `farm-integration.png`：正式四区农场、正式主角/AK、实际单排牛群同框。
- `farm-herd-integrated.gif`：正式环境和ArcadeCombatViewController加载牛模板，由实际ArcadeCombatSystem.TickRoundContent推进，再将只读姿态交给View；不是手工指定牛的路径或动作时间。
- 录制工具：`Tools/Unity/ConfirmedFarmIntegrationCapture.cs`。使用隔离存档fixture和临时附加场景；玩家固定于(-13,-16)且临时无敌，先推进8秒生成，再每0.1活动秒录制。未自然刷怪/清场，未接真人双客户端，不是正式完整对局录像。
- 复现只在Editor编辑态运行Run；不保存场景、不修改正式资源，完成后释放View、还原资源加载器并删除本工具的隔离存档目录。`frames/`是可重新生成的中间PNG，不提交。

伤害、单次结算、死亡冻结不拆排、恢复、旧快照与异步资源由对应集成检查验证，不能用此动态画面代替。
