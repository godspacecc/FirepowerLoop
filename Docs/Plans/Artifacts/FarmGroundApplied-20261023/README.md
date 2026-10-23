# 第三关农场草地正式接入（2026-10-23）

用户先采用草地＋泥土方向，再对“是否采用这版实际效果并正式接入？”回复“可以”。确认版本为[Unity候选v1](../FarmGround-Unity-v1-20261023/README.md)的全图与正式主角同框近景，已按同源配色和固定种子地面色斑正式接入。

## 实际效果

- `candidate-overview.png`、`candidate-detail.png`、`candidate-player-scale.png`：**本次正式Prefab回读实拍**，命名沿用候选工具，但没有再临时替换材质或叠加色斑。
- 三张正式回读图与用户采用版相应图片逐一SHA256相同。
- 草地、少量土斑、绿色树冠、棕色屋顶、金黄草垛和石色墙顶；原四区通路、石墙、棚屋、外围石块及角色尺寸保持。
- 同框的一头牛只作体量参照，不用本静态图证明牛队／速度／战斗规则。

## 资源与验证

通过`Tools/Unity/FarmGroundPreview.cs`的`ApplyConfirmed`持久化2个新网格、8个材质及meta，更新原`ArcadeFarmVisual.prefab`。原雪组FarmMesh0和其他旧素材、原meta均保留；9组非雪几何和引用不变，原雪组9032顶点／12684个有向三角形完整保留。新增130片无碰撞平面色斑，其中44片土色；无业务脚本、Animator、Collider或临时资源引用。

限定EditMode检查，全部`includePassingTests=true`：

| 真实短类 | 声明上限 | 实际通过 |
| --- | --- | --- |
| ArcadeFarmVisualTests | 3 | 3 |
| ArcadeEarlyStageLoadingTests | 8 | 8 |

共11项通过，0失败／跳过。证据为`farm-visual-tests.json`、`farm-loading-tests.json`。加载类第一次请求因MCP通信重试失败而没有测试结果，原错误留在`farm-loading-rpc-failure.json`；重新确认本项目Boot空闲编辑态后，仅重试原8项范围并通过，没有扩大套件或无效过滤回退。

另以正式ArcadeCombatViewController逐一核对22号四轮：环境与牛模板就绪后，唯一GroundPatches、无额外Collider、两个资源Handle各释放一次，重复结束不二次释放。见`four-round-load-release.txt`。这4轮是限定加载／释放核对，不冒充自然刷怪或完整对局验收。核验工具首次动态编译的类型命名错误已更正为global::Tables并重新运行通过，未改产品代码。

独立只读审查无P0–P3问题；审查者独立解码正式网格，核对原顶点／有向三角形、5材质分组、色斑固定种子、资源GUID、持久化、清理及重复调用保护。新增四轮核验代码也已复核无阻断问题。

最后MCP核对为本项目路径、Boot未脏、编辑态、compilationFailed=false、Console错误0，见`unity-final-context.json`。`console-history.json`保留工具历史中的占用、已修正探针编译、查询语法及其他会话历史记录，不将历史日志冒充当前编译错误。

本次没有修改配置、战斗／移动／牛群规则或场景文件；未打包，未进行自然完整对局或真人双客户端验收。确认、正式接入和必要验证已完成，仅提交推送本任务相关文件；其他会话改动保留。
