# 前期空手僵尸

2026-10-19 按用户确认的视觉预览，正式模型改为与主角等高的人形便服丧尸。基于本工程 `Player/OutfitPrototype/PlayerModularOutfit.fbx` 的人体骨架及身体网格制作独立资源，保存在 `Civilian/`，不修改主角资源。灰白皮肤、短发、凹眼、旧衬衫和牛仔裤区分感染者身份，蓝灰、米黄、暗红三种配色全部空手。保留既有三种 Prefab 和控制器 GUID。

使用实际蒙皮顶点将三种模型高度统一为主角默认身体高度约 1.666 米，头部本体宽度约为主角的 87%–100%；头宽比较排除帽子、头发。三个历史资源名继续用于配置，但不再对应巨型或夸张胖瘦的怪物外形。

- `ArcadeZombieShambler`：首岛四轮；普通基础近战数值。
- `ArcadeZombieGaunt`：第二、三场原弓手槽位，保留原生命、伤害、移动速度及冷却，改为 1.35 距离抓击，移除射击与弹药。
- `ArcadeZombieBloated`：第二、三场的部分普通敌人槽位，数值与普通僵尸相同。

只替换前 3 场敌群引用；原敌人及第 4 场起的配置保留。敌群数量、间隔、同时存活上限及倍率保持原值。`weapon.zombie-claw` 仅为近战身份，不是玩家掉落武器。

使用人体 Generic 骨架与独立 `Civilian|Idle_Attack` 扑抓、`Walk`/`Run_Arms` 移动、`HitReact` 受击、`Death` 死亡动画。动画只驱动可见骨骼，不移动游戏根节点或裁决伤害。保留原敌人的燃烧、火星、死亡烟雾、受击回弹与 11 刚体死亡反馈；火焰定位到新骨架腹部，避免沿用旧骨架偏移落到腿上。脚骨在死亡物理及复用后保持正确层级。

在 Unity MCP 编辑态执行 `Tools/Unity/ArcadeZombieAuthoring.cs` 可重制三个预制体，保留它们已有的 GUID。配置必须修改 Config/DataCsv 后通过 Config/gen.bat 双端生成。`Tools/Unity/ArcadeZombiePreview.cs` 在隔离场景拍摄正式模型，输出到 Client/Temp/ZombieRosterPreview。

旧版备用源保留：`Source/ZombieBasic.fbx`、`ZombieChubby.fbx`、`ZombieRibcage.fbx`、`ZombieAtlas.png`，以及原材质和 `.meta`。这些卡通资源不再用于当前三个正式丧尸。来源：[Quaternius — Zombie Apocalypse Kit](https://quaternius.com/packs/zombieapocalypsekit.html)，授权 CC0 1.0；原始 License.txt 随资源保留。2026-10-19 从作者页面链接的官方 Drive 获取，未导入第三方工程、脚本或玩家数据。

旧版原始文件 SHA-256：

| 文件 | SHA-256 |
| --- | --- |
| ZombieBasic.fbx | cb4b90e0dc093be9676aee4fcfa2ef7881ab22a3b8c39f0111c910735244236a |
| ZombieChubby.fbx | 4df1c2920c723325fe9eb9604487159d85253567cc1903595b5f2b392f40ba2b |
| ZombieRibcage.fbx | 58f2191ac2d0cd5b8fca236c72a2323ef16608bfc45530f34a6fff4e84e4225c |
| ZombieAtlas.png | 8803e5543d5e6b6f66aa41a5ef93e6c2ffb77b5476d0202296d2f4c8f4eb9e8d |
