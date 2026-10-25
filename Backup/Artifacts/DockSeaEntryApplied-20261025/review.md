# 独立审查记录

基线be319c387bc8165fde4b7dd5f81a7bb82411a4d7；审查者/root/review_dock_sea_entry，独立上下文只读审查本项工作区差异，未执行Unity、dotnet或测试。

发现Important：StopDockSeaEntryForDeath恢复Animator后，普通死亡的0.16秒失衡期间旧Run/Hit可能覆盖抓边和屈膝姿势，再交给物理。以完整参数方法实际RED复现。

修复：保留唯一三参数BeginDeathWithImpact供既有地面及反射调用；新增明确BeginSampledPoseDeath，共用私有死亡事务，仅海路普通失衡阶段保持Animator禁用。原失衡时间、叠加和物理交接不变。View死亡前记录dockSampling后选择入口。强化真实死亡成功、Animator禁用、视觉根现场位置/旋转及回池恢复断言，Visual10/10、相邻武器死亡4/4、普通死亡指定1/1通过。

最终审查结论：原Important已解决，无剩余Critical/Important；预算属性、活动时间、伤害推移、协议复制、完成位置、乱序、背桶、水纹资源、配置与重制入口未发现问题。最终兼容拆分再次复核通过，地面及牛撞死亡不受影响。
