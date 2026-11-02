# 附魔丧尸 A+B 独立整体审查

结论：Spec 通过，Quality 通过。未发现确定的 P1/P2 缺陷；最小必要修复：无。可由协调者完成正式样片目检与本批提交隔离后收尾。本结论不包含未执行的整局、真机、构建或发布验证。

## 范围与方法

使用 code-role、luban-config、resource-load-check，依据 brief.md、stageB-brief.md、A/B report、changes、review.patch、A review-report 与当前真实文件。用户已确认参数、三个具体视觉和正式接入，较早 brief 中“视觉尚未采用”不代表当前授权状态。

本次仅独立只读审查代码、配置、资源文本及既有原始回执；未操作 Unity、重跑测试、修改实现或 index、启动子代理、打包或发布。正式可见性由协调者独立查看 lineup、coexist 和三段有效帧，本审查不冒充重新进行视觉验收。

独立 SHA256 核对 A25+B41 共66项：64项与记录一致。SurvivorSystem.cs 和 ArcadeCombatViewController.cs 相对 A implementation 的后续差异为粘液瓶生命周期/端口及强化卡查询调整；已读取实际差异，未覆盖或破坏本批附魔调用与接口。上述其它任务变化应由协调者在独立 index 中排除，不能整文件夹带提交。客户端/服务端 tbenemy、tbsurvivorenemy、tbarcadedockhitpose bytes 三对逐一相同。

## Spec

- 源与目标均排除死亡及爬出态，按实体 ID 排除自己，其它支持者可接收；4秒周期、6米距离包含边界，源死不再授予，出范围或源死不撤销既有效果。TickFrame 的活动、暂停、选卡及 Playing 门禁在规则调用前，任务模式不推进该专用规则。
- 出生捕获基础最大生命和速度，完整投影不重建基础值。生命按基础值线性增加、五层封顶，只补本次上限增量，到顶不继续回血；速度按基础值线性增加，并继续沿既有冰霜时间折算移动。多源同帧逐次封顶，大数运算使用 double/long 后饱和。
- 附魔盾保存当前量，20/次、上限100，消费后可再补，没有终身五次限制。共享伤害入口先消耗附魔盾，破盾当次全部吸收，下次攻击才处理原生盾或生命；标识只消费剩余附魔盾。投影与补盾不会使破坏的原生钢板复原。
- 死亡、自毁、Stop 清理权威附魔；近远场切换仅归还表现实例。SyncEnemyPresentations 在恢复通用状态之后投影完整附魔和原生盾状态，不授层、不补盾。EnemyView 死亡、禁用、新用途与回池统一隐藏三环；复用后用当前快照恢复。
- 三独立身份及地址、七附魔字段、ID主键、六档比例与360秒起三种各3%一致；权威源和生成字段映射一致，gen.log 为正式双端生成完成。金币保留出生基础最大生命计算，未因生命附魔抬高既有收益。

## Quality 与资源

规则由既有 SurvivorSystem 生命周期持有；不可变快照和通知单向流经正式 Controller/EnemyCtrl/EnemyView。没有新业务 Update、通用 buff 框架或按波纹异步加载。三环只在非零快照首次创建，每个接收者最多三条；波纹独立24槽、1.3秒寿命，限额只替换表现，不限制权威授予。暂停/选卡/地图隐藏冻结年龄，源死亡可留残波，End/Restart/Release 清空池。

三个 Prefabs/Characters/ArcadeZombieEnchant{Health,Speed,Shield} 精确 collector 的 GUID 与 .meta 匹配，逐文件主资源、正式地址规则、arcade 标签一致。读取真实 BeginCollect dryRun 的唯一项记录及非 dryRun 的 DependAssets 记录，包含骨架/网格/动画、各颜色材质、共享 EnchantRing.prefab、EnchantRing.mat 和 Unlit.shader；没有把 dryRun 空依赖当成通过。ResourceBoot 的 StartupResourceTags 含 arcade，入口按既有地图资源门禁检查，PrepareArcadeAsync 将三个敌池身份经 config.Id 缓存及正式 PrefabAddress 提前准备，并对附魔模板缺失返回失败回滚。

新环借用已准备 Prefab 的序列化依赖，地图/角色实例负责销毁，不新增资源句柄所有者。读取原 LoadEnemyPrefabAsync loading/temporary/committed 事务及释放路径；本批没有另建异步请求、迟到回调或重复释放路径。real-async-load.txt 记录三个精确地址均由真实 YooAsset 加载成功且探针各自释放一次；正式准备句柄仍由 EnemyCtrl 持有。

## 验证证据与边界

独立解析原始回执：A 的 SurvivorEnchantTests 18/18、SurvivorSpecialEnemyTests 21/21、SurvivorEnemyRosterTests 24/24；B 最终 SurvivorEnchantPresentationTests 4/4，均零失败、零跳过。本审查未重复执行这些测试。

natural-restart.txt 的首轮自然注册 TickFrame 证据覆盖首次授予、三类共存、盾消费/重复投影不补盾与不复原钢板、自然补盾、源死保留、真实暂停与带活动波纹 Restart 清零。该证据早于最后属性块颜色、零快照懒创建及模板缺失门禁三项调整；这些调整不改变规则时钟及死亡重开事务，最终4项回归及正式受控样片补充相应表现证据。

后续 natural-error.txt 复跑受外部 Active=false/Paused/视图状态影响，没有首次授予，不能算再次成功；后来加入的休眠恢复探针也未执行。远场恢复目前以实际调用链及复用检查支持，没有远场实录。未将这些已声明边界推断为确定代码缺陷。

源收集、当前编辑器正常Boot真实加载已有证据；旧发布包、设备安装包、完整实际对局与发布未验证。本任务明确禁止构建，本审查没有要求通过额外构建来制造证据。现有边界应保留在最终交付说明中。
