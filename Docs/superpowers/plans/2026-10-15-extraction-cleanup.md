# 街机项目搜打撤清理实施记录

**目标：** 按用户确认的功能范围清理；保留现有单人、双人街机及本局规则、分数、结果、恢复、设置和更新。
**依据：** `../specs/2026-10-15-extraction-cleanup-analysis.md` 与用户“可以 开始清理吧”。用户已选择直接实施，不再增加技术方案确认环节。
**约束：** 当前工作区、不创建 worktree、不提交推送、不 Player Build、不接触旧玩家数据、保留资源与序列化契约、配置协议走正式生成器。

## 工作范围

- [x] 客户端：先锁定独立街机入口，再裁剪启动、退出、暂停、地图事务、控制器和输入；清除纯旧业务代码与调用方。保留异步资源提交/回滚/释放和正式资源 GUID。
- [x] 服务端：在独立文件范围裁剪 Account/Player/ServerConfig，移除 OnlineWorld，保留街机账号房间聊天、掉线重连和结果处理；协议源保持保留消息编号后正式生成双端。
- [x] 配置：在独立文件范围删除专用表和 Provider 接口/适配/校验；保留街机实际依赖；修正生成器产物收敛，正式双端生成。
- [x] 配套：同步过时测试、Editor、工具、资源收集审计及文档入口，不删除原有美术资源。
- [x] 验证：源码编译、定向测试、GUID 检查、街机依赖检查、独立代码审查。失败必须在明确范围修复。

## 验证范围

Unity 定向首批最多 14 项（分析报告列出的真实类/完整方法），includePassingTests=true；新增清理契约夹具另声明具体短类和最大数量。无效或空过滤不得回退完整套件。服务端只运行有明确范围的街机事务检查程序。未运行的引擎/双人实机验证如实列出。

## 过程记录

- 基线：`dotnet build Client/Game.csproj --ignore-failed-sources -v:q`：0 errors，15 warnings。
- Unity 未检测到运行进程，可执行文件存在；会话无直接 Unity MCP 工具，未安装新工具或更改 MCP 配置。
- 分支创建因 .git 沙箱只读失败；按用户指定当前工作区实施，不绕过限制，不提交。
- 分工：客户端由根处理；服务端和协议、Luban 配置和 Provider 在互不重叠的文件范围并行处理，统一整合后验证。

## 实施与验证结果

- 已移除旧业务模型/系统/控制器/服务端处理器、专用配置与协议，裁剪混合入口；保留单人/双人街机本局规则、恢复、分数、结果、设置和更新。
- 独立存档共用类型移至 Model/Save/SaveReadResult.cs 与 Utility/Save/SaveClock.cs，保留原 meta；不保留基地/远征存档系统。
- Config/gen.bat 正式双端生成：客户端26表、服务端20表；源定义、Tables loader及bytes集合一致。协议经 Tools/gen-protocol.ps1 正式双端生成；保留外部56、内部86消息编号不变。
- Game运行程序集、Editor程序集、现存EditMode测试源码均编译0错误。测试源码编译15警告，主要为既有字段未使用/默认值和过时API。未触发Player Build或发布热更新DLL。
- 服务端Hotfix编译0警告0错误；事务14/14、宝藏21/21通过。宝藏5条旧预期在HEAD基线实际运行同样失败，仅更新过时夹具到现有规则，未改玩法。
- Unity MCP已确认 Application.dataPath=E:/GitHub/FirepowerLoop/Client/Assets；打开Boot且IsDirty=false。沙箱进程查询不能反映实际编辑器进程；批处理第二实例被项目占用拒绝，没有执行测试。随后经本工程21032端口MCP运行定向测试。
- Unity FirepowerLoopRunTests 9/9通过，Boundary 2/2通过；includePassingTests=true，逐项结果保存在Client/Logs/cleanup-mcp-*-results.json。插件Summary.TotalTests给出发现树总数1723，实际Results及PassedTests仅9/2；未执行完整套件。
- 资源GUID静态扫描：保留资源没有引用被删除脚本；未删除美术、Prefab、场景或其meta。保留旧UI生成View与必要无业务序列化脚本。
- 独立只读审查未发现本轮新增Critical/Important；审查覆盖地图事务、控制器、相机输入声音、初始化释放、联机结果和账号会话。
- 真实单人连续试玩、双人实机对局及异常资源迟到回调仍未验收。保留协议字段布局有删除，双端必须同步更新；账号schema4拒绝旧格式，不迁移历史数据。
- 未提交、未推送、未建worktree；开始前3个DLL meta改动未主动修改。

- Unity附加5个完整方法均1/1通过：双人房间锁、商店修订号、重连期限、街机Prefab反序列化绑定、Boot热更新字段；累计16项通过，未运行完整套件。

- 测试后MCP确认Boot仍打开且IsDirty=false；最近5分钟Console Error为空。Unity刷新按标准行为清除了已移除生成表/源码的孤立meta；美术、Prefab、场景及其meta未删除。配置产物移除来自正式schema生成，不属于美术资源清理。
