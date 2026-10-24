# 第二关电塔正式接入对照

2026-10-23 用户以“可以”确认 ElectricTower-v1 右侧外形、蓄能、分叉放电及正式接入。现在21号码头使用正式持久化 DocksElectricTower：金属底座、铜线圈、陶瓷绝缘盘与冠顶放电环；预警电流向顶部聚集，通电由塔顶分叉。保留1.2米判定范围、安全4秒、预警1.5秒、通电3秒及原伤害。旧arena2保持原电塔。

`tower-applied.gif` 是通过真实 ArcadeHazardPresentation 入口加载集合内正式嵌套 Prefab 的隔离临时场景录像。左侧旧模板，右侧已接入电塔，正式主角同框；相同镜头、时间与光照，非实际对局录像。170张Unity源帧、20fps；GIF合并静止帧为93帧，1280×720、8500ms、无限循环。三个PNG为安全、蓄能、通电代表画面；字幕仅用于对照说明。

验证：DockElectricTowerTests 6/6通过，覆盖正式材质/网格/Shader引用、无碰撞体、蓄能递增、相同权威时间冻结、分叉闪动、周期熄弧、重复快照不重复创建、释放不销毁借用资源、旧主题与异步加载后重绘。原机关幂等释放、真实码头接触扣命/复活、全布局加载无碰撞并释放资源三个完整方法各1/1通过。重复执行BakeApproved前后26个Mesh/Material的GUID及本地fileID一致。未运行完整套件、Player Build或打包。

生成：Unity MCP动态执行 `Tools/Unity/ElectricTowerPreview.cs` 的 `RunApplied`；Python运行 `Tools/Unity/encode-electric-tower-preview.py --applied`。制作资源使用 `BakeApproved`，只保存本塔资源和正式集合，不保存场景或其他脏资源。
