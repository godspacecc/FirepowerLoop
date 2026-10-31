# 阶段2独立审查

## Spec结论：通过本阶段实现范围

CSV顺序及权威价格正确：1/50、7/100、3/150、4/200、9/300，默认0不属于收费商品。System只接受商品ID并从配置取得价格；未知商品、未拥有外观及余额不足明确失败。选角在StartAsync进图前及TryStart内重验，RunCharacter从已验证选择记录。购买失败直接返回，不发布成功事件；账本原子事务沿用已审阶段1。

## Quality结论：通过静态及当前限定验证，无Critical/Important确定问题

- 所有权和余额来自同一次TryReadSnapshot；对外商品实例不可变，数组是独立生成的展示快照，不泄漏配置目录。余额成功后更新Model，购买事件仅在事务成功后发布。
- PlayerCtrl在构造注册同步应用事件，ReleaseAll成对注销；Game生命周期创建和释放该Controller。当前活动幸存者刷新优先RunCharacter，非幸存者仍使用既有测试外观逻辑。应用失败抛错由TryStart回滚。没有修改骨架、碰撞、武器或属性代码。
- Stop未清SelectedCharacter/RunCharacter，RestartAsync先返回菜单并Stop再Start，因此静态上可保留选择；测试只证实Stop保留，不能替代真实完整重开验证。
- Config源、双端新增生成表及Tables注册一致；generation.log记录生成完成。tests-result.json 3/3通过：目录与购买选角、真实GrayboxPlayer六种整身应用、真实BeginCollect精确表地址唯一且startup。未重跑测试或操作Unity。

## 阶段3必须接续的验证边界

完整StartAsync→人物创建→SurvivorCharacterApplyEvent→实际造型→RestartAsync保留尚未实测；必须在最终交付前检查。现有ApplyWholeBody测试绕过正式PlayerCtrl事件链，仅证明Prefab绑定可用和根尺寸未变，不证明运行加载、动作兼容或无明显穿模。新配置表的源收集通过也不代表运行包已包含；应在正常运行环境验证加载。UI回调失效、开始期间门禁与错误码本地化属于后续阶段，不列本阶段缺陷。

## 基线真实性及提交边界

Tables和发布语言的开始快照是事后反推，无法独立证明它们是实际开工时文件；应明确保持这个证据限制。当前任务patch所列Tables只有角色表新增注册，语言只有角色名key，内容与本阶段Config源一致。直接查看HEAD差异发现两端Tables另有TbSurvivorMapLayout增量，属于共享工作区其他任务，不能整文件暂存。最终提交需按角色成员增量选取，并核对本阶段生成入口表数更改与其他未提交表的依赖，不能以反推基线当作不存在其他差异的证明。

未改生产代码、未运行Unity、未扩大到后续UI阶段。本报告不宣称整体角色购买与选角功能已经交付。
