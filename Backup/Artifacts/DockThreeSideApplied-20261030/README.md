# 三方向海中攀爬正式接入

用户采用 DockThreeSideEntry-20261030 的30秒候选后接入。左、上、右三路均使用正式敌人、已采用的游近／抓边／抬腿登台动作；左上旧通道正式撤除，主角尺寸不变。出生预算、三种体型和背桶不自动卸下保持原规则。

- [正式资源回放](three-side-entry-applied.mp4)：30秒，600帧，20fps，1280×720，完整解码通过。隔离场景采样正式地图、正式角色、共享权威路径与EnemyView；登台后续步是参考演示，**不是自然对局或真人双端录像**。
- [关键帧](contact-sheet.png)、[回放来源](replay-source.cs)、[视频检查](replay-audit.json)。三路近景包含正式主角同尺寸参照。
- DocksMesh1／6分别删除566／112个三角，共678；只裁除已采用左上外伸几何。独立审查确认顶点数据未变、无新增三角，原.meta保留。
- 四轮码头配置更新三路落点及左上仅挡移动边界；通过Config/gen.bat双端生成。方向字段35通过Tools/gen-protocol.ps1生成，覆盖快照构建、深复制、序列化及客户端映射。

## 验证范围

Unity EditMode：DockPassageEntryTests 9/9、DockSeaEntryFlowTests 15/15、DockSeaEntryVisualTests 13/13、ArcadeRoundLayoutTests 5/5、ArcadeDocksVisualTests 3/3，共45项。每次明确短类名、对应数量上限、includePassingTests=true。前三组结果见client-*.json；后两组见supplemental-checks.json。无完整套件、Player Build或打包。

服务端：DockSeaEntryOnline 9/9、DockPassageEntryOnline 9/9，共18项，命令及记录见server-verification.md。后续在独立导出的本次提交源码副本再次运行相同两组，以防共享工作区并发改动污染交付；结果见snapshot-server-*.log。最终独立副本配置哈希与首次生成一致：4065183D2A500F4768284C9C1806D3C2ABA4469CD874FDE3CE5623750EEE3B62。

总计63个不同定向用例。独立审查发现候选暂存误包含另一任务OutsideEntry引用，已只从候选删除并复测；共享工作文件未回滚。源码快照无HitGeometry、OutsideEntry、Account改动。

## 并发工作边界

另一会话随后按用户新要求实现“身体露出且弹道命中即可受伤”、普通敌人场外入场及网络改动。本次提交只包含三方向路径、地图、方向同步和相应回归，不夹带其未完成实现，也不覆盖其工作区文件。此前入场保护检查只证明本次提交对应版本；不将其当作并发空间命中版本的最终验收。Unity最终编译失败标记false，Boot干净；Console保留历史错误，不宣称历史日志为空。

正式资源回放与独立服务器检查覆盖接入所需部分；尚未进行两台真人客户端整局实玩。
