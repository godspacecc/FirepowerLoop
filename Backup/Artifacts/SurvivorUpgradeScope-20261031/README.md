# 主武器强化范围调整

用户最终授权：取消主武器独立升级，暂不做伤害/射速加成；保留弹夹、换弹、生命、移速、拾取与无人机独立升级。2026-10-31明确“开始做吧”。

实现：权威CSV中Rifle、Damage、FireRate权重为零，保留配置顺序避免数字本地化键错位；系统抽取与选择同时过滤零权重。配置允许停用行及至少一个启用无限强化。加权选择在仅剩1或2项时仍稳定返回并完成选择，避免后期暂停卡死。未改Prefab、视觉、基础战斗数值或敌人参数。

验证：本项目Unity MCP确认Application.dataPath为E:/GitHub/FirepowerLoop/Client/Assets，Boot未脏；初始Console为其他Boot测试警告。Config/gen.bat成功完成双端生成。

- red.json：精确ConfigurationAllowsTwoUnlimitedChoicesAndNoRifleUpgrade，最多1项，首次按预期失败。
- two-red.json：精确TwoRemainingChoicesStayStableAndCanBeSelected，最多1项，首次返回空候选而失败。
- config.json：SurvivorConfigTests，声明最多20项，15/15通过。
- runtime.json：SurvivorRuntimeTests，声明最多45项，42/42通过；首次被其他测试占用，等待后成功重试，未放宽过滤。
- weapon.json：SurvivorWeaponTests，声明最多8项，8/8通过。

全部为EditMode、includePassingTests=true，未运行完整套件。独立只读代码审查未发现阻断问题。保留项、无人机升级、禁用项拒绝及后期二选一恢复游戏均有覆盖。

边界：未完整人工实玩、未验证后期战斗平衡，未Player Build或发布包验证。本次不自动补偿被移除的主武器成长数值。共享工作区其他任务差异不纳入提交。
