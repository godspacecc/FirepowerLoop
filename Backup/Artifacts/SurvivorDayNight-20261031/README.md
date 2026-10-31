# 幸存者昼夜接入验证（2026-10-31）

用户看过 [候选](day-night-preview.gif) 后回复“可以”，正式视觉与接入获授权。仅单人幸存者昼夜；天气暂缓，不修改刷怪、敌人或角色属性。

## 实现

六分钟一轮：白天→黄昏→夜晚→黎明。SurvivorSystem在本局启动时启用，在有效时间推进后投影ElapsedSeconds；暂停、强化选择和场地未就绪不推进。End归还原主光引用及方向光启用状态，重开从白天开始。仅创建本局拥有的两个Light，不新增动态资源地址，不触发配置生成或打包。

## 验证

- EditMode短类SurvivorDayNightTests：4项先因缺少投影入口失败，实现及独立启动入口调整后4/4通过；includePassingTests=true。
- 完整方法Game.Tests.SurvivorRuntimeTests.ReloadFreezesDuringPauseAndResetsForNewRun：限定1项，1/1通过；验证现有暂停与重开流程，非昼夜专属系统集成测试。
- [正式代码隔离渲染](implemented-day-night.gif)：80帧12秒加速演示，正式场地和角色临时实例，由正式Controller采样；白天/黄昏/夜晚/黎明四关键帧与采用预览平均像素差均为0。非实际完整对局录像，正式周期为360秒。
- 渲染结束后MCP确认Boot未脏，原Directional Light enabled=True、intensity=1。
- 独立只读代码复审两轮，无阻塞问题。已检查启动异常→Stop→End清理，以及暂停/UI/未就绪提前返回。

## 验证边界

未运行完整套件、Player Build或发布包；未完成完整战斗试玩、真机或各画质档实拍。Performant档URP不渲染附加灯光，天空补光可能不生效，本次隔离渲染不证明该档的最终可读性。弹丸密集与危险提示的动态可读性仍应在实际试玩中验收。
