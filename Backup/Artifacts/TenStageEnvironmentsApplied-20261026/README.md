# 三场四轮正式接入：任务A

用户已选择“七项全部采用并正式接入”。本批只处理 TenStageEnvironments-v1-20261025 的配置7要塞庭院、10锦鲤桥庭、20终局巢穴，每场四轮；小龙与角色资源不归本任务。

[查看30张同框对照](index.html)。左侧是已采用源的临时几何，右侧是正式持久资源经生产 `SetRoundLayout` / `LoadConfirmedEarlyStage` 路径加载后的隔离编辑态实拍，均与正式主角、正式武器和待机同框。资源获取边界由回读工具连接 AssetDatabase；选轮、校验、实例化和清理使用生产代码。没有把此次隔离回读记作自然完整通关或真实双端联机验收。

## 实现与不变量

- 已添加3个正式四轮Prefab：`ArcadeFortVisual`、`ArcadeKoiVisual`、`ArcadeFinalNestVisual`；静态网格和材质在 `Client/Assets/Game/Art/Arcade/TenStageEnvironments/`。旧资源与meta保留。
- `TenStageEnvironmentCandidates.cs` 原样采用，SHA256=`DF1DF570926D6DF510A3CD602D930A2441B34630E4C9A08F6776DAD266E51A44`。新烘焙工具在十二轮全部通过通路、障碍足迹、场外地标及几何校验后才写持久资源；没有修改候选源。
- 原逐面顶点/法线/材质/阴影摘要精度1e-4、足迹精度1e-3不变。第一次静态合并发现32面法线末位不同，在写入前中止；根因为 Unity CombineMeshes 二次归一化。改为直接保留源世界矩阵计算出的顶点和逆转置法线，不追加归一化，十二轮全部通过；详见 `merge-diagnosis.txt`、`bake.txt`。
- 环境加载复用既有轮根和异步句柄事务。5/7/10/20使用四轮选择，其余环境保持原单体路径；缺轮在实例化前失败，支持重试，释放后迟到回调不能重挂旧图。
- 唯一权威配置改动为 `Config/DataCsv/arcade_early_stage.csv` 新增7/10/20映射，半宽/半高19.5，牛群为0。仅运行 `Config/gen.bat` 双端生成（36/30表），客户端/服务端 `tbarcadeearlystage.bytes` 同源一致；未手改任何生成C#或bytes。
- 共享参数白名单和双端目录验证均纳入已采用九场精确集合 `{5,7,10,20,21,22,23,24,25}`。现有玩法的EarlyStage分支仍只接受21—25，三个环境没有引入牛群、短步移动、传送门或出生规则变化。
- 各轮Renderer数量：要塞21/21/21/25，锦鲤37/61/73/61，终局40/40/40/40。没有Collider、MonoBehaviour或Animator。

## 已执行证据

1. `probe.json`：实际项目F盘FirepowerLoop/Client，Boot，干净、非Play、非编译，errors0/warnings28，timeScale0。未更改timeScale。
2. `tests-red.json`：真实短类 `TenStageEnvironmentAppliedTests`、EditMode、maxTests15、includePassingTests=true；15/15因正式Prefab或目录映射尚缺失而失败。
3. `tests-green.json`：同类限定maxTests17，17/17通过；包括12轮持久资源、实际选轮切换、释放后迟到回调、缺轮失败及重试、原玩法边界、目录缺项/重复拒绝。后两项为补充目录边界测试，其余15项完成RED→GREEN。
4. `config-generation.log`：正式双端生成成功。
5. `bake.txt`：十二轮在写持久资源之前完成校验；烘焙调用返回在资源保存后遇到MCP10次重试失败，见 `bake-response.json`。因此不以该响应声明烘焙调用成功，而由后续17项持久资源检查和逐场正式回读独立确认产物。
6. `readback-Fort-response.json` / `readback-Koi-response.json` / `readback-Nest-response.json`：三次分场回读工具均成功，每场10张图。`layout-footprint-checks-7.tsv`、`-10.tsv`、`-20.tsv`含十二个正式回读摘要，全部与采用源及烘焙摘要一致。
7. `pixel-comparison.json`：全部30张PNG可解码，1600×1200；15对中7对逐像素一致，其余最多120个像素有差异（1920000像素/张）。几何摘要一致不等于逐像素全等，未宣称后者。
8. 服务端限定 `TenStageEnvironmentOnline`、maxTests3、includePassingTests=true：3/3通过，结果见 `server-environment-checks.log`。第一次测试断言把原ConfigError的Exception误限定成InvalidOperationException，实际拒绝行为正确；已修正测试异常判据，初次记录保留在 `server-check-harness-diagnostic.log`。

## 尚需统筹核实的编辑器收尾

- 最后提交的 `HengeAppliedTests`（EditMode、maxTests8、includePassingTests=true）响应为“another test run already in progress”，活动请求ID `70c66c64-800d-4552-a8a0-5b83075e2454`，详见 `henge-regression.json`；本任务不将它计作已通过，也未重发全套或扩大过滤。
- `final-probe.json` 连接10次重试失败，未据此声称最终Console干净/编辑器空闲。A已停止bridge并将租约交回主代理统一诊断。FirepowerLoop进程PID68348，projectpath为F盘本项目，离线Responding=True；同时存在其它Unity工程，因此共享 `editor-tail-after-bridge.txt` 只作参考，不作为本项目最终状态证明。
- 主代理负责独立审查、统一必要回归、最终状态核对、中文提交和推送。A没有自行commit/push。
- 初始33项本机差异未编辑。编辑器额外导入差异（FinalDragon/Material0..6.mat、URPProjectSettings.asset等）不属于A交付，由主代理核对处理。
- 未运行Player Build、全测试套件、自然全局通关、真实双端联机或实体设备验收。

## 复现命令

```powershell
Config/gen.bat
Tools/PickupFeedbackChecks/Invoke-UnityMcp.ps1 -Tool tests-run -ArgumentsFile Backup/Artifacts/TenStageEnvironmentsApplied-20261026/tests-input.json -OutputFile Backup/Artifacts/TenStageEnvironmentsApplied-20261026/tests-green.json -MaxTests 17
dotnet run --project Tools/TenStageEnvironmentOnlineChecks/TenStageEnvironmentOnlineChecks.csproj -- testClass=TenStageEnvironmentOnline maxTests=3 includePassingTests=true
python Backup/Artifacts/TenStageEnvironmentsApplied-20261026/verify-pixels.py
```

测试输入JSON为 `{"testMode":"EditMode","testClass":"TenStageEnvironmentAppliedTests","includePassingTests":true,"includeStacktrace":true}`。动态烘焙编译合并 `TenStageEnvironmentCandidates.cs` 与 `TenStageEnvironmentBake.cs` 并去重顶层using，调用 `TenStageEnvironmentBake.Run()`；回读另加入 `TenStageEnvironmentReadback.cs`，分场调用 `RunFort/RunKoi/RunNest`。运行前必须由主代理取得本项目Unity独占租约、核对项目/场景/Console。输入JSON与完整失败面诊断为本机可重建中间文件，已忽略。

本批精确路径清单见 `files.txt`。

## 统筹收尾补证

以上历史Henge回归/连接响应问题已闭环：主代理在全部接入完成后限定`HengeAppliedTests`最多8项、includePassingTests=true，8/8通过，见[回归结果](../SevenVisualsApplied-20261026/HengeAppliedTests.json)。最终本项目Boot未脏、非Play/编译；Console唯一Error为该缺轮测试明确预期的日志，已逐条核对并保留，见[Console说明](../SevenVisualsApplied-20261026/console-analysis.json)。独立环境审查及最终集成审查均无未关闭问题，见[统筹记录](../SevenVisualsApplied-20261026/README.md)。
