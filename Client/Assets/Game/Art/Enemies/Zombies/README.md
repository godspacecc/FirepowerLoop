# 前期空手僵尸

来源：[Quaternius — Zombie Apocalypse Kit](https://quaternius.com/packs/zombieapocalypsekit.html)，授权 CC0 1.0。2026-10-19 从作者页面链接的官方 Drive 获取，原始 License.txt 随资源保留（文件标题沿用作者另一资源包的名称，授权正文为 CC0）。未导入第三方工程、脚本或玩家数据。

Source 原始资源：ZombieBasic.fbx、ZombieChubby.fbx、ZombieRibcage.fbx、ZombieAtlas.png。Ribcage 是仅有下半身的备用源，本次不用于敌人。普通与瘦型共用 Basic，瘦型调整比例及材质色；胖型使用 Chubby。三种外观均为空手。

- `ArcadeZombieShambler`：首岛四轮；普通基础近战数值。
- `ArcadeZombieGaunt`：第二、三场原弓手槽位，保留原生命、伤害、移动速度及冷却，改为 1.35 距离抓击，移除射击与弹药。
- `ArcadeZombieBloated`：第二、三场的部分普通敌人槽位，数值与普通僵尸相同。

只替换前 3 场敌群引用；原敌人及第 4 场起的配置保留。敌群数量、间隔、同时存活上限及倍率保持原值。`weapon.zombie-claw` 仅为近战身份，不是玩家掉落武器。

使用原生 Generic 骨架，原生 `Idle_Attack` 扑抓、`Walk`/`Run_Arms` 移动、`HitReact` 受击、`Death` 死亡动画。眼睑、舌头和身体是同时显示的组成网格。保留原敌人的燃烧、火星、死亡烟雾、受击回弹与 11 刚体死亡反馈；Generic 的独立脚骨仅在物理阶段挂到小腿，复用时恢复动画层级。

在 Unity MCP 编辑态执行 `Tools/Unity/ArcadeZombieAuthoring.cs` 可重制三个预制体，保留它们已有的 GUID。配置必须修改 Config/DataCsv 后通过 Config/gen.bat 双端生成。`Tools/Unity/ArcadeZombiePreview.cs` 在隔离场景拍摄正式模型，输出到 Client/Temp/ZombieRosterPreview。

原始文件 SHA-256：

| 文件 | SHA-256 |
| --- | --- |
| ZombieBasic.fbx | cb4b90e0dc093be9676aee4fcfa2ef7881ab22a3b8c39f0111c910735244236a |
| ZombieChubby.fbx | 4df1c2920c723325fe9eb9604487159d85253567cc1903595b5f2b392f40ba2b |
| ZombieRibcage.fbx | 58f2191ac2d0cd5b8fca236c72a2323ef16608bfc45530f34a6fff4e84e4225c |
| ZombieAtlas.png | 8803e5543d5e6b6f66aa41a5ef93e6c2ffb77b5476d0202296d2f4c8f4eb9e8d |
