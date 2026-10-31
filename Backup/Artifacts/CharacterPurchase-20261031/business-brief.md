# 阶段2：商品配置、业务与开局外观

总体需求见Docs/Plans/角色购买与开局选择-20261031.md；阶段1报告见wallet/report.md。本阶段不制作UI、不修改正式视觉资源，只实现配置和业务/开局衔接及必要测试。全部需求和两页视觉已经确认，不再提问技术审批。

- 权威Config定义稳定商品ID、外观编号、价格及本地化名称key。1=50，7=100，3=150，4=200，9=300，顺序1/7/3/4/9。默认0免费，不作为收费商品。只改Config权威源并gen.bat双端生成；使用适用luban-config、code-role、resource-load-check技能。
- 接入既有SurvivorSystem/Model，UI后续只传商品ID或选择ID，不可信任UI价格。提供商品+余额+所有权快照、购买和选择验证入口；只在保存成功后发布成功状态。未知/不足/失败明确可区分，不重置坏档。账本同文件事务已由阶段1实现，阅读其API和报告再复用。
- 每次主菜单进入将由阶段3先展示选择页；业务允许默认0或已拥有的有效候选，开始时重验；商店与选角关闭后的回调失效由UI处理。失败时不开始对局。
- 为本局记录外观编号，玩家创建成功后应用现有ClothingAppearanceView.ApplyWholeBody，不改变骨架/尺寸/武器/属性。重开当前局沿用选择；返回菜单后下一次仍先选角。其他模式保持原逻辑。重点检查SurvivorSceneFlowSystem.RestartAsync先Stop对状态的影响，及现有外观刷新是否会覆盖选择。
- Config源生成若附带其他工作变化，保留当前基线后仅报告本任务增量，不操作index，不提交推送，不派生agent。路径以实际项目为准，不套用技能AnotherDay地址。
- 测试先枚举真实非空短class/fullmethod，声明范围与最大数(含参数化)，includePassingTests=true；仅临时钱包，不动玩家真实金币。必要覆盖未知/未拥有拒绝、默认免费、价格配置、成功购买/重开保留选择及外观应用。不能只测替身即宣称真实模型应用通过；正式UI与资源整体实测归阶段3。
- 不打包、不模拟构建。Unity调用先确认项目，避免与root并发；Boot脏可重载，其余不任意丢弃。
- 证据放Backup/Artifacts/CharacterPurchase-20261031/business/：开始基线、report.md、changes.json、review.patch、原始测试回执。及时告知root供阶段3使用的业务API。完成后只交回本阶段，不把总任务写成完成。
