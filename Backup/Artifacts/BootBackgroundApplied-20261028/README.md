# Boot独立背景正式接入

用户于2026-10-28确认独立背景v2及底部渐暗。正式图原样复制自已采用的boot-concept.png，SHA256一致；原Boot图片和.meta均保留。新增背景及渐变纹理都位于Assets/Boot/Art，启动界面无需等待热更资源即可显示。

场景仅更换背景引用并增加底部27%高度渐暗层，渐暗层在背景之后、其余UI之前，raycastTarget=false。没有更改加载逻辑、按钮、事件及初始显示状态。

## 正式场景回读

- [标准屏1600×900](formal-1600.png)
- [窄屏1440×1080](formal-1440.png)
- [宽屏2340×1080](formal-2340.png)

以上均从磁盘重新加载Boot后克隆拍摄，45%为截图展示状态，不代表真实加载测试。

## 验证

- Game.Tests.Flow.BootResourceServerEditorTests.EntrySurvivesBusyAndFailureAndOpensBoundInput：限定单方法、上限1项，includePassingTests=true，1/1通过，见test.json。
- 独立只读审查：无P0–P3问题；资源GUID/fileID正确唯一，无新增脚本依赖，按钮不被遮挡，原初始状态和绑定保持。
- 结束Boot未脏、非Play、非编译；Console错误0、警告38，数量与本轮开始一致，见before.json/after.json。
- 三个既有无关文件（地形asset、中文字体asset、ProjectSettings.asset）SHA256与开始一致，未纳入提交。
- git diff --check通过。未执行完整套件、真实下载整流程、设备测试或Player Build。
