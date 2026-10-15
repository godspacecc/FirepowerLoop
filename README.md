# FirepowerLoop

独立街机游戏：单人本局与现有双人联机竞技场。启动进入街机主界面，使用本项目的配置、资源与服务，不需要基地或搜打撤存档。

- Unity 工程：`Client`。打开该目录后使用 `Assets/Game/Scenes/Boot.unity` 启动。
- 配置源：`Config/Defines`、`Config/DataCsv`；唯一生成入口：`Config/gen.bat`。
- 协议源：`Config/Proto`；生成入口：`Tools/gen-protocol.ps1`。
- 游戏服务：`Server/GameServer`，本地 GameGate 默认 `31011`，诊断服务 `31012`。
- 资源发布：本项目 `Server/ResourceServer/ResourceFiles`；默认资源服务 `8001`。
- 应用标识：`com.anotherday.firepowerloop`；产品名：`FirepowerLoop`，存档使用本应用的 persistentDataPath，不扫描旧项目。

第一轮保留分数、本局结果、局内金币与强化；没有局外奖励或成长钱包。不迁移旧街机或基地数据。

`Docs/Plans` 中街机工作材料是历史上下文，是否完成以当前代码和验证记录为准。从复制工程保留的公共代码、配置与工具按本项目分别维护，不自动同步另一工程。搜打撤任务不属于本项目路线图。

当前只完成源码和配置验证。连续单人试玩、双人实机对局与新工程实际画面仍需在本工程 Unity 中验收；Unity Player 打包由用户手动执行。复制的旧热更新 DLL 不作为本项目可发布产物，发布前必须由正式流程重新生成。