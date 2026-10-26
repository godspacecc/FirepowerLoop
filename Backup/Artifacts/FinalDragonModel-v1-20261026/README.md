# 终局小龙：实际模型与动作候选 v1

2026-10-26。用户已采用[造型概念](../FinalDragonConcept-v1-20261026/concept-v1.png)，授权继续制作，并启动 FirepowerLoop 编辑器。此次交付是临时模型与动作预览，尚未获实际模型/动作采用，未正式接入。

[打开可播放预览](index.html)；[原速12秒](normal.mp4)；[半速24秒](slow.mp4)。

## 本批制作

- 岩褐色龙身、暖色翼膜、四足、双翼、长颈长尾、角冠、可动下颌；3808个三角形、118个网格对象。这是未合批的临时制作结构，不宣称正式移动端性能。
- 正式主角来源 `Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab`，scale保持1/1/1，持正式基础武器，待机蒙皮按既有工具采样。尺寸读回见[model-audit.txt](model-audit.txt)。
- 静态有三分之四、侧面、正面、头部细节；动态240帧/20fps，演示呼吸、四足行走、蓄力、前扑、张口和展翼恢复。不运行AI/技能/伤害，不新增飞行或喷火玩法；路径和时序均为候选动作展示，不代表正式攻速和攻击距离。
- 独立PreviewScene内包含模型、主角、地面、相机和灯光，`camera.scene`明确绑定，避免Boot灯光混入。临时网格与材质仅在内存生成，拍摄后释放。

## 实际检查与边界

- [video-audit.json](video-audit.json)：两条视频均完整解码为240帧，原速12秒、半速24秒。
- [motion-timeline.csv](motion-timeline.csv)：逐帧根位置、下颌角度和两前脚高度；前扑根位移1.05米，两前脚最高Y=0.43米（静止脚掌中心Y=0.13米），有实际离地变化。不是战斗命中验证。
- [model-audit.txt](model-audit.txt)：遍历全段网格顶点，无低于地面容差的顶点；主角和模型实际同框，未创建候选碰撞器。
- [cleanup-verification.json](cleanup-verification.json)：实际使用FileShare.None同时锁住图片与错误日志，触发真实IOException后，临时场景及PreviewScene数量恢复、busy=false、Boot保持未脏。不是仅检查异常处理文本。
- [probe-after.json](probe-after.json)：路径为FirepowerLoop，Boot未脏、未Play、无编译进行中；原有TimeScale=0保持。Console原3错误/54警告计数不变，原错误来自启动时MCP连接失败，不称Console全空。
- [verification.json](verification.json)：对Client/Config/Server跟踪文件的完整二进制Git差异做前后指纹比对，一致；现有用户未提交改动保留。该指纹基线在本轮最终拍摄前保存，不声称覆盖整个会话开始前状态。
- 未运行玩法测试、未打包或Player Build，不宣称正式Boss战、真人双端或真机验收。

## 独立复审

第一轮发现3项Important：前扑四脚贴地滑行；输出及错误日志都失败时可能跳过清理；附加场景受Boot灯光影响。

已补前后脚错开的蹬地/离地/落地及身体腾空；目录创建放入try，日志失败不阻止finally清理；改用真实PreviewScene并迁入全部拍摄对象。重新拍摄/编码并实际故障注入后，独立只读复审无剩余Critical/Important。复审未操作Unity，仅核对代码、图片和证据。

## 重现

模型与拍摄源码位于仓库 `Tools/Unity/FinalDragonCandidate.cs`、`FinalDragonPreviewCapture.cs`；正式主角采样辅助沿用 `TenStageCreaturePreviewCapture.cs`。本项目空闲编辑态下运行 `Tools/Unity/Invoke-FinalDragonPreview.ps1 -Video`，等待capture-state完成后，用 `Tools/Unity/encode-final-dragon-preview.py --ffmpeg <本机ffmpeg路径>` 编码。

原始帧、临时调用输入和编码器依赖留在本批目录并被本地.gitignore排除；正式输出和源码提交。旧巨猿候选保留并加上已被小龙方向替代的提示，不改变其它四种候选角色状态。
