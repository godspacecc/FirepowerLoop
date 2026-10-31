# PC幸存者暂停按钮

用户采用candidate.png：PC隐藏暂停按钮，移动端保留，Esc沿用既有行为。current/candidate为正式Prefab临时副本预览，非对局实拍。

SurvivorHudUICtrl.RefreshJoystick使用既有MobileInputSystem控制按钮显示。真实Controller、正式Prefab实例及编辑器输入模式切换验证PC→移动模拟→PC：隐藏→显示→隐藏通过，见runtime-check.txt。未修改Prefab、资源地址或快捷键逻辑。

限定暂停回归：Game.Tests.PauseUiFlowTests.DirectPauseAction_UsesSamePauseMenuRouteAsKeyboard，最多1项，includePassingTests=true。首次被另一测试运行占用，随后两次请求均180秒超时，不能声称通过。独立只读代码审查无重要问题。未打包，未提交推送，待Unity恢复后完成暂停回归。

用户要求重试后，Unity确认连接本项目Boot、非运行且无编译；同一限定暂停回归1/1通过，见pause-test.json。此前阻塞解除，未打包；仅本项提交推送，结果以Git回执为准。
