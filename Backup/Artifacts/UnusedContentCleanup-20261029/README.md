# 未使用内容清理与验证（2026-10-29）

用户在排查报告后确认“可以 开始吧”。本次只移除确定退役的内容，不改变当前游戏画面、玩法、配置、协议或资源收集规则。

## 已移出 Unity

- 36个旧UI Prefab：背包、仓库、制造、远征、文明、旧商店、对话、临时建筑及地图标记；QuickBarHud兼容空壳保留。
- 35个配套旧View脚本，包括旧装备格。测试中唯一引用其WeaponSlot枚举的无调用、非接口空方法同步移除。
- CivilisationValleyMap、SurvivalSampleMap、ValleyArtSample三张退役场景。
- 5个Unity教程／Readme文件及85个已空目录。
- 合计79个资源／脚本及其meta、85个目录meta，共243个Git文件删除，约5.44 MiB原内容移出工程。

原件及.meta配对保存在[retired-originals.zip](retired-originals.zip)，243项归档SHA256核对全部一致，GUID可追溯；Zip另含迁移测试修改前的原件。清单见[removed-files.json](removed-files.json)、[空目录记录](empty-directories-removed.json)和[归档校验](archive-validation.json)。归档位于Unity工程外，不参与导入或发布。

## 已核对并保留

- QuickBarHud的原资源及GUID，旧Boot兼容约束仍有测试；本次仅确认资源可加载，不宣称所有旧APK地址均已发布。
- Input System示例源包：正式ArcadeHud与MobileJoystick依赖原Stick.prefab，保留其源包、meta与导入信息。
- AppearanceGallery及其正式姿态调试资源、RuntimeSample和何家村预览；BaseMap仍被ShoulderAimPreview工具读取。
- item／weapon／loot／外观共用身份及资源：共用提供器、造型调试、旧装备回退及诊断路径仍有依赖。此次无Config差异，无须生成配置；不恢复已删的近战战斗参数。
- 旧路线兼容资源、备用美术和制作源文件。原报告1247项仅是当前收集依赖范围外候选，不是无引用清单。进一步扫描4313项Assets的直接依赖：指定Henge与TenStage程序网格没有可直接删除的零入边项；例如Henge网格仍被ArcadeHengeVisual.prefab持有。
- 原工作区海岛、字体及ProjectSettings三份差异不属于此次任务。

## 检查与测试

- Unity连接E:/GitHub/FirepowerLoop/Client；Boot编辑态未脏；最终脚本编译失败标记false。
- 184个正式Prefab检查Missing Script及悬空序列化引用，结果0；见[prefab-validation.txt](prefab-validation.txt)。独立审查另核对165个退役GUID，无现存序列化资源引用。
- 最终58个不同的限定EditMode用例全部通过，见[最终逐项结果](tests-final-summary.json)。范围为迁移3、现役绑定5、街机UI12、启动回退23、本地化归属9、资源审计2，以及加载本地化／手机HUD／相机生命周期／QuickBar可加载各1。每次均使用真实短类名或完整方法名、includePassingTests=true，并声明上限。
- 基线迁移检查原3项中2项失败（已删BagUICtrl、Common目录过时快照）；现役18个GUID、绑定和资源收集覆盖校验保留，退役契约移除。
- 验证中暴露并修正旧测试中的加载背景字段遗漏、联机背景层级／账号文案／Room退役字段、公告固定标签例外、大厅空快照夹具、旧相机事件及expedition审计标签；不为旧测试恢复退役业务，不修改正式Prefab或运行时代码。
- 历史失败与诊断记录保留在tests-first-pass、tests-final-pass、tests-final-recheck等日志。曾因编辑测试时误替换诊断辅助方法造成编译阻塞，已修正；tests-recheck记录5次未能执行的请求，不计为测试通过。最终结果以tests-final-summary.json为准。
- 独立只读审查P2“保留现役资源校验”已补回并3/3验证，复审无新的阻塞发现。
- 未运行完整套件、PlayMode、Player Build或资源打包；未进行真人单双人对局验收。
