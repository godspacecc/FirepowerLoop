# 第二关码头水体候选（未采用）

目的：使现有低多边形码头水面有清晰深浅与自然缓慢水纹，避免刺眼白块；以正式码头同机位对照，等待用户采用后才修改正式资源。

只读证据：`Client/Assets/Game/Art/Arcade/Docks/DockWater.shader` 的岸距 `d` 恒为8，`foam` 恒为0，水纹未使用时间；高光强度乘3.2。当前水面没有动态水纹及平台附近深浅层次。正式Prefab为 `Client/Assets/Game/Prefabs/SceneObjects/ArcadeDocksVisual.prefab`。

预览策略：克隆正式Prefab，保持原模型与布局。候选Shader仅通过 ShaderUtil.CreateShaderAsset 内存编译。仅从正式模型中接近地面的朝上水平三角形提取平台轮廓，生成内存岸距纹理；近平台偏青绿、远水深蓝。细水纹双向缓慢流动，弱化高光，在实际平台边加入少量断续灰绿泡沫。无正式资源写入。

父代理执行顺序：

1. 确认Unity连接本仓库Client且编辑器不在Play/编译/测试中。
2. 用现有Unity MCP编译执行本目录 `DockWaterPreview.cs`，入口 `DockWaterPreview.Run()`。脚本按Unity工作目录Client寻址，同步输出到本目录。约320张1280×720帧，避免与其它Unity动作并发。
3. 通过已安装Python/Pillow运行 `encode.py --ffmpeg <本机ffmpeg绝对路径>`。
4. 核对 `overview-comparison.png`、`close-comparison.png` 与8秒 `water-comparison.mp4`。若岸线提取或Shader检查失败，脚本会停止，不退化为任意平面水。
5. 向用户展示 `index.html` 或图片和视频，明确是隔离候选，正式资源未变。只有用户采用后才实施。

验证状态：已在本项目空闲编辑态串行执行两轮，最终版Shader编译通过。已查看全场/近景对照及四个时间点的动态接触表，修掉首次候选偏大的柔斑高光。视频完整解码通过：H.264/yuv420p、1280×450、20fps、8秒、160帧。候选首尾帧平均通道差异全场2.938、近景2.240；当前对应0.00138与0（轻微渲染舍入容差），证明变化来自候选明确时间轴。Unity归还时仅Boot打开、isDirty=false，Console未新增Error。没有触发测试、Play或Build。详见 verification.json。

建议交付与纳入提交：本目录代码、Shader、README、index.html、render-report.txt、verification.json、两张comparison.png、motion-contact-sheet.png、四张原始对照图、water-comparison.mp4。320张原始中间帧已在完整编码、解码与时间变化检查后删除，删除前逐一验证仅位于本批frames目录；可用Run重新生成，不纳入提交。

已知限制：这是正式环境克隆的候选演示，尚无正式水体接入、真实对局测试或性能验证。岸距为512格平台轮廓近似值；水面不透明，没有反射、折射或真实波浪位移。预览以_PreviewTime驱动，未来采用后需正式动画时钟及岸距资产方案。泡沫刻意收敛，在全场镜头不突出。视频首4秒全场、后4秒近景，两个镜头的时间轴各从0秒开始。
