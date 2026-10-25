# 十关候选与巨石加载事务独立只读复核

2026-10-25，路线子代理按根代理要求审查路线以外实现。没有执行Unity、烘焙、构建或测试，没有修改被审查源码。

## 发现

1. **P2：重烘焙验证失败前已经覆盖正式资源。** `Tools/Unity/HengePolishBake.cs:149、152`在Combine每个合并组中立即Save持久Mesh/Material；`Run:42`随后才判定合并前后几何是否等效。若已接入版本重烘焙到一半发生hash失败或其他异常，旧Prefab引用的前面若干Mesh已经被覆盖，而其余轮资源和Prefab仍是旧状态；finally只释放临时对象，不恢复被覆盖资源。这是编辑器工具的失败事务问题，独立于当前正在诊断的hash不等原因。已通知apply_henge与根代理，建议先在内存完成四轮组合和等效验证，再统一持久化；若需保证写盘本身失败也无半套输出，应备份本批既有资源并在失败时恢复。

## 已核实边界

- TenStageEnvironmentCandidates只创建临时GameObject／Mesh／Material，不写AssetDatabase、不修改正式Prefab或共享Mesh／Material；输出仍是未采用候选。
- TenStageEnvironmentPreviewCapture在隔离additive场景中实例化正式主角和现版几何，候选只消费真实布局；finally归还RenderTexture.active、关闭临时场景并释放本批动态资源。主角Prefab原比例保留。静态工具无玩法计时或自动动态效果。
- 候选逐障碍顶点检查投影边界，外景逐顶点检查位于39米场地之外；没有创建Collider或新增业务阻挡。
- 巨石运行时只新增环境资源与四轮可见性选择；客户端／服务端业务EarlyStage明确限制21—25，保留巨石旧出生点、移动及传送门规则。
- LoadConfirmedEarlyStage在实例化前检查四个Round节点完整且各有Renderer；缺轮时沿ReleaseRoundContent清除轮根并归还句柄，保留原错误和布局供重试。
- EarlyStageLoadIsCurrent检查独占state、Released、owner root和round root；释放后迟到回调不能挂回旧图。ReleaseEarlyStageLoad将句柄归还一次并清空；换轮按同一生命周期释放旧state。
- HengeAppliedTests源码包含四轮持久足迹、每轮可见性、旧业务规则边界、迟到回调与缺轮回滚／重试检查。本次仅审核测试覆盖意图，未宣称这些测试已经新运行。

当前几何hash不等由apply_henge处理中，本报告未把未通过的正式烘焙或实拍记为完成。上面的P2需修正或明确回滚措施后再关闭。

## 最终复核：原P2关闭

根代理报告完成修复与验证后，本子代理再次只读审查当前HengePolishBake.cs及bake.txt／readback.txt／green.json；没有执行Unity或测试。

- **原P2已关闭。** Combine现在只生成owned清单中的临时合并Mesh并引用临时材料，不调用Save。Run先完成四轮全部合并与原精度几何hash检查，循环结束后才在`:57`调用Persist；因此任何构造／合并／hash失败均发生在覆盖正式资源之前。Persist随后保存网格、去重材质并把临时根引用切到持久资源，最后保存Prefab。
- Run的临时场景拥有全部候选GameObject；临时源与合并网格、持久化副本均登记owned。finally恢复原活动场景、关闭临时场景，再销毁非持久owned对象；已交给AssetDatabase的持久资源不由临时清理销毁。检查的失败边界是“验证失败不写正式资源”，没有把逐项AssetDatabase持久化声明成文件系统原子提交。
- Capture从持久Prefab实例回读，与采用源逐轮hash比较后才出图。失败时临时accepted/applied/hero/light/camera均由additive场景关闭释放；markerView.End沿正式清理入口归还辅助材料，camera.targetTexture断开，RenderTexture.active恢复，RenderTexture与Texture2D显式释放，owned临时网格／材质销毁；未观察到把持久Mesh/Material当作自有资源释放的路径。
- 相对局部TRS累乘避免worldInverse取消造成末位差异；Point仍采用原每米10000格精度，没有放宽hash。bake.txt中四轮hash逐一匹配readback.txt，Renderer数69／69／69／79，主角localScale为1；green.json记录HengeAppliedTests 8/8通过。以上为根代理执行后的现存证据，不是本子代理新跑结果。

最终在本报告范围内未发现尚未关闭的代码阻断项。原“hash处理中／P2待修”段落保留为首次审查历史，由本最终复核状态替代。
