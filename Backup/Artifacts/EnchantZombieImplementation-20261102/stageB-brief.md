# 阶段B：正式附魔丧尸表现与整体接入

先读brief.md及stageA/report.md与review-report.md。用户已对三段EnchantZombiePreview-20261102/preview-0/1/2.gif选择“采用这套预览并正式接入”，具体视觉和全部规则都已授权；无需再审批技术方案。所有阶段完成审查验证后root统一中文提交推送，不能只交资源。

## 视觉与数据

按Preview.cs临时实拍落正式独立ArcadeZombieEnchantHealth/Speed/Shield Prefab和.meta，身体保持Caster同骨架/控制器/根尺寸（A复用其hitpose）。生命红、迅捷绿、护盾蓝服装/肩标与手持球，同色从源扩散波纹，目标脚下同色标识；三类可同时存在且可辨。装备挂真实骨骼沿既有Caster，而非根固定。无需额外UI/层数数字/生命条新样式、碎片、音效或新攻击伤害。

状态接口StageA SetSurvivorEnemyEnchantment(id,SurvivorEnchantSnapshot)、PlaySurvivorEnchantPulse(id,kind,position,radius)只消费权威值。生命层与速度层非零持续标识；附魔盾标识只在ShieldRemaining>0，补盾后恢复标识。原生实体盾由原接口独立控制，附魔蓝标识不能让破损实体盾重新出现。普通/远程/盾牌等所有接收者都可有标识，不只新Prefab；优先用已有EnemyView准备持有的表现来源/模板，不能按每次波纹异步动态加载或漏掉新依赖。

波纹纯表现有独立有界池和持续时间，限额不得影响授予规则；暂停/选卡冻结表现年龄，死亡/休眠/远场复现/回池/End/重开均正确。快照复现不得补充权威层数或耐久；脉冲源死后残波可按寿命结束，但目标已得标识须保留到效果消费/目标死亡。地图和单位代际/迟到回调/句柄释放按既有事务，不新增MonoBehaviour.Update业务时钟。

## 资源与验证

阶段A新三地址目前真实collector为0，现有准备会拒绝进图且可能影响闯关，因此本阶段必须加精确收集、arcade标签、原敌池准备链/缓存键和依赖。按resource-load-check真实BeginCollect unique+tags+DependAssets，不只AssetDatabase存在。普通/幸存者/闯关实际准备行为沿当前GameLoadMode代码核对，不回退忽略资源。

正常Boot Play真实YooAsset异步加载三地址及新增动态效果地址（若有），明确句柄owner和release；不资源构建/模拟构建/Player Build。实际生命、速度、盾三授予→三可见标识及重复投影/盾消费/原盾不复原/源死保留/重开清理通过。真实地图验证仅临时金币/结果账本，不碰用户数据。

必要限定测试先枚举真实非空class/method，上限含参数展开，includePassingTests=true，无全套或无效回退；不要重复StageA所有63项，围绕新表现/生命周期/资源契约及实际调用链。有界自然Tick观察授予/脉冲，并在有活动波纹/标识时真实Restart验证全清零；不得只反射直接调用View并假称正常运行链。样片可系统接口受控采样但必须标来源，root会查看特效有效帧，不得再漏拍PS后用布尔存在替代可见质量。

所有预览/脚本/日志/报告都在本批stageB/，正式同框截图、3段可播放样片及三类共存样图要明确正式资源/受控采样。保存开始真实基线包括所有将变文件及meta；共享工作区勿覆盖他人改动/index。不要build、不spawn、不commit。交report.md changes.json review.patch 原始回执供root独立整体审查，之后修复并收尾。
