# 钻地新版正式接入

用户在本聊天查看 SurvivorGroundHeavy-v1 动态候选后回复“可以”，正式采用并授权实施。

[正式代码隔离回放](applied.gif) · [关键帧](contact-sheet.png)

分段爬出5.2秒，撑地停顿后再次发力；28块土与18团轻尘使用已确认预览的时间与轨迹。地图控制器独立持有效果，敌人死亡/回池停止后续喷发，旧土继续落地；暂停冻结，清场/离图释放。受击只使用Prefab登记的身体/外观渲染器。

验证：SurvivorGroundEntryTests 14/14通过；真实BeginCollect收集检查 SurvivorRosterDependenciesAreCollectedBeforeEntry 1/1通过，控制器回收精确回归1/1通过，合计16项，均includePassingTests=true。原受击测试先失败（绿色附属物变红），修复后通过。首次收尾检查选到了尚未发射的土块，改为检查1.85秒时正在飞行的土块后通过，未以修改产品行为掩盖失败。

280源帧与14秒GIF；正式EnemyView与独立效果类在临时场景回放，主角同框。后半段隐藏敌人模拟移除，仅验证表现独立，不冒充实际死亡对局。实际死亡姿态与移除后继续收尾由限定测试覆盖。截图蒙皮代理仅用于编辑器离线采样，不进入产品。

资源边界：未增加动态地址或正式美术资产；继续借用三种丧尸Prefab序列化土材质及攀爬片段。真实收集检查覆盖幸存者敌池地址及arcade标签、配置startup标签。EnemyCtrl.PrepareArcadeAsync读取独立敌池、预载Prefab并ValidateGroundEntry后交接；材质实例与网格在地图释放时销毁，源材质随原Prefab句柄管理。AssetDatabase依赖检查不是发布包依赖验证。未打包、未检查既有发布清单、未实机或自然对局验证。

独立代码审查未发现重要问题，未做高密度性能测试。未修改配置/协议/存档。
