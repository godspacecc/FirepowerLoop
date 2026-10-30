# 场外入场规则预览

用户要求：所有模式敌人在场外可见、可受击，不在入口突然出现。覆盖单人闯关、双人联机、幸存者。正常射程和实体遮挡仍生效，撤销码头入场无敌。

entry-rule-preview.gif为行为示意，不是正式游戏截图。圆点代表角色，不表示正式比例、模型、地形或相机布局。显示敌人从远处接近、在场外受到伤害或被击杀、存活者连续经过入口。

用户已采用规则示意并明确修正：码头水下不能被飞过身体上方的弹道打中；不使用入场计时无敌。普通敌人从场外进入，墓穴爬出、Boss登场保留。不开新模型、动作或镜头美术。

已实现：普通敌人沿配置的场外走廊连续进入，单人闯关、幸存者、服务器及普通增援均接入；特殊登场保留。挑战/兼容局按真实障碍寻找合法走廊，配置无路在开场拒绝；幸存者运行时异常沿用错误返回菜单流程。场外死亡的普通奖励落到入口内，避免无法拾取。原有海岛和码头入场方式保留。

三种正式码头敌人的既有11个骨骼胶囊已采样为权威配置，按弹道与身体相交处理；单人/服务器共用纯数学查询，各自接线。正式模型和动画未修改。命中体积是骨骼胶囊代理，不是皮肤逐三角形检测。

`VerifyDockHitPose.cs`以实际正式动画检查每两个样本之间的中点：3种角色×675个中点×11胶囊，共22,275次，0.01秒采样最大端点误差0.017913米，详见`pose-interpolation-audit.csv`。

## 验证

- Unity EditMode：DockSeaEntryFlowTests 15/15、DockBarrelFlowTests 9/9、OutsideEntryFlowTests 3/3、SurvivorRuntimeTests两个完整方法各1/1，共29项。每次非空过滤、明确数量上限、includePassingTests=true；最终结果见`unity-*-final.json`及两份`unity-survivor-*-results.json`、`unity-outside-flow-results.json`。
- 独立提交源码副本服务器检查：OutsideRoutes 10/10、ArcadeDockSpatialChecks 6/6、ArcadeSpatialContinuationChecks 5/5、DockSeaEntryOnline 9/9、DockBarrel 4/4，共34项，见`snapshot-*.log`。覆盖全部100布局目录、普通路线与fallback、空间接触、冻结、快照及同帧贯穿；未运行全套或Player Build。
- 独立审查发现并修复：三维接触后重复二维过滤、贯穿余程丢失、开场预检插错方法；重新编译与对应定向检查通过。
- 提交副本检查发现Git自动文本换行损坏新bytes，已在仓库与服务器两级属性中明确`*.bytes -text`。客户端/服务器生成结果及Git归档字节完全一致，见`binary-export-check.txt`；全部服务器定向检查在修正后副本通过。
- 实际Unity Play：从真实菜单进入幸存者，第一只在x=-34.39时已有活动模型和5个启用Renderer。为检查边界近景，将测试玩家位置设到(27,-14)，未修改射击/伤害；右侧敌人在x=31.90、31.68（场地半宽30.5，仍在场外）生命100→64→28后死亡。见`live-survivor-entry.csv`、`live-survivor-edge.csv`及`live-survivor-edge-1.png`。结束观察先Stop再退出Play，未保存结果。
- 服务器为真实规则入口和序列化检查，未进行两台真人客户端整局实玩。玩法证据与胶囊采样误差分开记录，不将概念示意称作实拍。
