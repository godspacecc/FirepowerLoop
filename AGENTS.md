# FirepowerLoop Codex Notes

本项目只开发街机，英文名 FirepowerLoop，中文名未定。工作目录 E:\GitHub\FirepowerLoop，Unity 工程根为 Client。单人、现有双人联机及本局强化规则保留；首轮只保存分数和本局结果，不开发基地、远征、局外奖励或成长钱包。不读取、迁移或导入 AnotherDay 的玩家数据。

Unity 已打开时先用 Unity MCP 查询场景与 Console，确认连接的项目路径后操作。不得默认创建 worktree。不得触发 Unity 打包或 Player Build。测试必须使用真实、非空的短 testClass 或完整 testMethod，声明范围与最大数量，includePassingTests=true；严禁完整套件或无效过滤回退。

Boot 场景脏状态可按测试需要从磁盘重载，丢弃临时未保存变化。新项目不共用另一项目的 MCP 本地配置、Library、UserSettings、服务器数据或发布目录。

保留资源及 .meta。配置只改 Config 权威源，通过 Config/gen.bat 双端生成，不手改生成代码或 bytes。协议通过 Tools/gen-protocol.ps1 生成。核心手写类、函数和字段补中文注释；使用现有 QFramework 职责、Unity UI 和 Luban 业务技能。项目内技能中的 AnotherDay 背景不构成搜打撤实施授权。

对两个工程的公共修复分别审查和应用，不建立自动同步或共享框架。提交需要用户明确要求；提交信息用中文，默认提交后推送。

制作或替换角色模型前，先提供视觉预览图，待用户确认外观后再制作正式模型。角色尺寸需与正式主角同框比较；概念图与正式资源实拍应明确区分。
