# 幸存者模式接入验证

2026-10-30。单人无尽生存可从原样菜单按钮进入正式草地农场 arena.22.round.1；玩家只移动，自动选择射程内最近无遮挡敌人射击。击杀产生经验，近距拾取后暂停三选一；连续升级逐次处理。移动端使用与闯关相同的移动控件设置。

## 已采用的表现

菜单 menu-v2、升级 upgrade-v2、正式草地农场、hud-v1、result-v2、xp-coin-prefab-v1 均获用户明确采用。mobile-hud-v1 的最终要求为“摇杆位置保持和闯关模式一致”。预览在相邻 SurvivorDesign-20261029 目录；本目录 PNG 为实际运行截图。

## 验证证据

- 49 个限定 EditMode 检查全部通过：SurvivorRuntimeTests 10、SurvivorUiTests 6、SurvivorNavigationTests 3、SurvivorEntryTests 2、SurvivorRulesTests 13、SurvivorConfigTests 5、SurvivorResultFileStoreTests 6，原菜单/暂停/退出四个完整方法各 1。每次 includePassingTests=true，JSON 记录在本目录。
- Config/gen.bat 双端生成退出码 0，日志在 SurvivorConfig-20261030/complete-generation.log；两张新表客户端和服务端 bytes 散列对应一致。
- 实际 Editor Play：菜单进入草地，正常刷怪自动射击约14.6秒击杀6、分数60、生命100；真实键盘输入移动5.51米，见 natural-run.txt 与 natural-play.png。
- 现场定向验证：将已有敌人移入玩家附近加速检查，真实开火/经验拾取触发升级窗口，点击正式卡片选择成功。暂停计时冻结；定向设置低生命并让真实敌人接触以触发阵亡，正式结果页出现。再来一局重置等级1/分数0/生命100；返回菜单后模式不再Active，见live-lifecycle.txt、auto-fire.png与result-play.png。这部分明确包含测试状态注入，不冒充自然完整生存时长。
- 现场验证结果存入本目录 isolated-save，未污染正式幸存者结果。正式闯关主文件、备份及已有联机结果散列全部保持，见 save-isolation.json。
- 农场边界、HUD填充越界、直接退出漏保存均观察到限定检查失败后修复通过；导航三个真实农场检查先2/3，修复连续线段碰撞后3/3。
- 独立只读审查发现的异步布局失败和卸载失败重试绕路已修复，最后复查无新增明确P1/P2。

## 接入时修复的问题

正式View通过ReferenceCollector及项目生成器生成并回绑。新增资源收集目录，移除指向已不存在Effects目录的失效收集项，恢复正常Editor资源初始化。草地使用自身30.5米半宽/半高；共享有界导航距离场解决局部绕障在围栏卡死，轮流入口生成仍保持安全距离。退出或关闭应用先保存独立结果，失败保留结果供重试。

未执行Player Build、完整测试套件、真人双客户端联机或真机触屏验收。移动端已验证正式Prefab和生命周期接线；既有闯关保留限定入口与退出回归。最终编辑器返回Boot编辑态，TimeScale恢复1。
