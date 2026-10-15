# 钢刀挥砍连招

- 来源：用户于 2026-10-11 从 Mixamo 下载的 `Remy@One Hand Sword Combo.fbx`，动作名 **One Hand Sword Combo**。
- 下载选项：FBX for Unity、Without Skin、30 FPS、Keyframe Reduction none；完整 0–136 帧，未在网页裁剪或镜像。
- `SteelBladeCombo.fbx` 保留完整片段 `SteelBladeFullCombo`，并由 ModelImporter 切出四段：0–44、44–72、72–104、104–136 帧。片段不循环，根旋转与位移烘焙到姿态，玩家位移仍由原移动系统负责。
- `SteelBladeSlash1` 至 `SteelBladeSlash4` 动作定义和 `SteelBlade.controller` 只绑定目录里的 `weapon.han.blade`（物品 34）；普通攻击槽使用第一刀，连招状态由武器系统逐段驱动，没有自动接刀过渡。
- 每段播放时长取钢刀现有武器冷却配置；离线落刀点对应源片段 32、60、92、114 帧。伤害、范围、扇形角度和采集效率仍从原配置读取。
- Unity 角色离屏预览与定向 EditMode 测试用于检查接线；游戏内操作手感和联机实机效果仍需验收。

## 单次单手劈砍（2026-10-14）

- `SingleMeleeSlash` 复用 `SteelBladeSlash1` 的 0–44 帧片段；独立定义只绑定短刀、战斧、战镐、精锻斧和精锻镐的普通攻击槽，没有钢刀控制器或连招数组。
- 普通攻击默认把该片段压到 0.35 秒，给现有 0.45 秒攻击间隔保留收刀混合余量；街机仍按其实际间隔适配。该时长只缩放 Attack 状态，不修改 Animator 全局速度、玩法冷却、输入或命中时机。
- 单刀起手采用 0.02 秒混合，覆盖尚未结束的移动混合；切装、受击、死亡与失活清理攻击速度及触发，收刀后恢复速度。没有动画命中事件。
- `MeleeAttack` 前刺、双手攻击、钢刀四段连击和所有未列出的武器动作定义均保持原资源引用。模型及默认握点保留；已用正式人物骨架采样检查五种武器的举起、挥落、挥切和收刀。
- 重建接线使用 `Tools/Unity/SingleMeleeSlashAuthoring.cs`，离屏候选和静止/移动攻击采样使用 `Tools/Unity/SingleMeleeSlashPreview.cs`。结果位于 `Assets/Tests/Results~/SingleMeleeSlash/`；这些是正式资源与 Animator 的编辑器采样，不是玩家连续操控实机验收。
