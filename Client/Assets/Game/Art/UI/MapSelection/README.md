# 地图选择章节插画

`ValleyChapter.png` 是文明河谷章节的示意插画，不是实际地图截图。由内置 image_gen 工具生成，用于用户确认的地图选择视觉方案。正式 Prefab 引用本目录的 Sprite；原始生成图保留在 Codex 本机生成目录，不作为项目依赖。

生成提示：

> Create one clean landscape 16:9 game environment illustration used as the river valley chapter thumbnail in the approved FirepowerLoop game UI. Stylized low-poly 3D illustration: a clear winding river from middle distance down to lower right, green rolling hills, scattered conifer and deciduous trees, distant blue gray mountains, a tiny rough prehistoric shelter made of branches and hides near a small campfire on left foreground, rocks on river bank, warm daylight. Muted natural colors, tasteful quality matching a low-poly Unity exploration game. Clear readable composition at thumbnail sizes. No humans, no text, no lettering, no logos, no UI, no frames, no modern equipment, no metal weapons, no buildings. This is illustrative chapter artwork, not a screenshot. Full bleed illustration.

Unity 按单 Sprite 导入，关闭 mipmap，最大导入尺寸 1024；弹窗和卡片通过序列化的遮罩与 AspectRatioFitter 裁切，保持图片比例。
