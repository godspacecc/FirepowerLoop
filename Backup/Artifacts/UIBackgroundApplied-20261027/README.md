# 十二张已采用背景正式接入

- 采用来源：原会话 01a176c0-3524-7e31-8336-8d062baf8fd2 中用户对 v2 十二图回复“可以 就这样”；本会话 01a17c16-bb61-7af2-92ec-26d44b941617 中明确接续完成。
- [正式界面回读](index.html)：主菜单、联机大厅、十关加载页，合计18张（包含1440×1080、1672×941、2340×1080）。从正式Prefab隔离渲染，45%为示例进度，不是实玩/设备截图。
- 12张源图原样保存到 `Client/Assets/Game/Art/UI/ArcadeBackgrounds`，保留旧资源及meta。主菜单与大厅使用直接纹理引用，加载页序列化十关Sprite映射；生成器生成LoadingArtwork字段并绑定全部引用。
- 完整加载页读取单人休闲路线场地配置编号或活动联机快照配置编号，未知/缺失编号、非休闲旧局、非街机及下载回退海岛图。普通局内切轮仍使用黑幕，不增加弹窗或改玩法。

## 验证

- `tests-final.json`：UiBackgroundTests 7/7；真实配置编号（非序号）、联机优先、未知/下载重置、旧局回退、新局、正式图片及生成绑定。
- `curtain-tests.json`：ArcadeRoundTransitionTests 5/5。
- `flow-0-result.json` 至 `flow-3-result.json`：四个完整方法名限定检查各1/1，覆盖入口遮挡、失败重试信息、销毁重建、地图进度与入口释放。
- 全部为EditMode、includePassingTests=true，分别上限7/5/1；没有全套、PlayMode、资源打包或Player Build。真实实玩/双人/设备验收按用户指示暂缓。
- `verification.json`：12张采用图SHA256一致，18张PNG校验/解码及非空检查通过，无TMP文字溢出；开始时记录的9份实质本机差异SHA256不变（技能文件另外存在仅行尾差异，未纳入提交）。
- `bind.json`：三份Prefab的全部生成引用通过Unity反序列化核验。
- `final-state.json`：本工程Boot、未脏、非Play/编译、无编译失败。Console保留编辑器重启时的3条MCP连接失败及既有警告；后续连接已恢复，以上限定测试均真实通过，不将历史日志宣称清零。

## 修复与审查记录

- 原6项背景检查真实RED见 `tests-red-results.json`。初次生成后的字段未绑定，1/6通过；使用项目生成器既有绑定方法后6/6。
- 独立审查发现旧顺序局第25关误选集市。`legacy-red-retry.json` 真实复现；改为只读真实配置编号/未知回退后最终7/7通过，独立复审确认P2关闭，无新增实质问题。
- 隔离截图初次未激活Prefab，出现全黑；工具改为显式激活克隆并写图前拒绝全黑，重拍后的18张均有效。未修改正式布局来补偿拍摄问题。
- MCP在编译重载时出现请求占用/响应丢失，保留中间诊断记录；以最终结果为准。编辑器关闭后仅重开本工程，未操作Rift2D。
- 独立审查为静态只读，未运行Unity或扩大范围。其余未属于本任务的材质、字体、设置及技能差异全部留在工作区，不夹带提交。

## 制作入口

`Tools/Unity/UiBackgroundApply.cs`：Run导入/接入，Generate逐个生成，编译完成后BindReady调用现有生成器绑定并验证。

`Tools/Unity/UiBackgroundCapture.cs`：编辑态隔离场景渲染，退出恢复原场景与RenderTexture，产物统一本目录。
