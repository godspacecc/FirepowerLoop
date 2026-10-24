# 掉落与HUD尺寸候选 v1

2026-10-24，使用本工程正式Prefab的Unity隔离实例渲染。候选静态图不代表正式资源已经接入，不是概念图或运行中对局截图。

- [骷髅与正式主角、金币同框](skulls-comparison.png)：上当前、下候选；金币保持原尺寸，红紫骷髅分别按实体最长边等比对齐金币。正式拾取根缩放后金币最长边0.349581米，红骷髅乘1.406055、紫骷髅乘1.086497。原外观、旋转和拾取逻辑不变。
- [无人机与正式主角同框](drones-comparison.png)：上当前、下候选；每幅主角右侧依次为空投展示和随身无人机。空投由1.7米缩为调整前随身的0.85米；随身各轴乘用户选定的0.7，最长边0.595米。无人机悬浮位置保留，没有接入新的飞行动作。
- [完整PC HUD](hud-pc-zero-comparison.png)、[完整手机HUD](hud-phone-zero-comparison.png)：上当前、下候选；红紫骷髅各轴乘0.9，零倍率进度隐藏整槽。HUD使用正式Controller投影示例快照，纯色背景用于看整体HUD比例；不是实玩场景。
- 有倍率进度时的完整PC HUD：`hud-pc-progress-current.png`与`hud-pc-progress-candidate.png`，原进度条保持。

模型预览由 `Tools/Unity/DropHudScalePreview.cs` 生成，完整HUD由 `Tools/Unity/DropHudFullPreview.cs` 生成。所有尺寸候选仅调整临时克隆；正式模型、图标、Prefab未修改。正式主角与基础AK比例不变。

第4项用户已明确确认是“倍率进度为0就隐藏整条”。已独立实现PC/手机共享的倍率显示逻辑：零显示进度隐藏底槽及填充，进度恢复即显示；原最高倍率的有效显示进度为1，继续显示满条，不改变计分规则。

验证记录：仅Unity EditMode真实类`ArcadeMultiplierHudTests`，每次最多6项，`includePassingTests=true`。新增桌面/手机复用回归先失败于零进度底槽仍可见（4通过/2失败），修改后6/6通过。覆盖归零、恢复、刚升档零余量、最高倍率和原单人/双人投影。未运行完整套件、Player Build或自然整局验收。

历史候选等待状态已由2026-10-24用户“采用这组预览，正式接入（推荐）”替代；本组具体视觉均已采用。正式接入实拍、尺寸核验与57项限定回归见[采用版接入记录](../DropHudScaleApplied-20261024/README.md)。本目录保留原候选，不以重拍覆盖已采用图片。
