# 巨石环阵采用版正式接入

来源：会话 `01a1724e-41f9-7070-947d-f12a36abdc10` 在完整四轮预览后获用户“可以”采用；本批按十关目标正式接入配置5，不包含已暂缓火山或其他关卡。

采用视觉：[完整四轮画廊](../HengeVolcanoPolish-v1-20261025/Henge/index.html)。正式源：[HengePolishPreview.cs](../../../Tools/Unity/HengePolishPreview.cs)，SHA256 `3668B137F1D0B39085146BE0A7083A8975447DC228F75F3F05D03300D2C0F2F5`。共享初稿不是采用源。

预计效果：四轮分别显示各自的风化石门、断梁和青苔碎石；39米活动范围、17处障碍足迹、四向出入口及所有玩法规则沿用原配置。正式主角原尺寸同框回读；只做静态场景接入。

实施状态：主会话取得 `HengeAppliedTests` 8/8真实RED（4项缺正式Prefab，4项目录缺5），现已正式烘焙、双端生成并取得8/8 GREEN。配置源、白名单、目录校验及轮次选择已接入；客户端与服务器业务查询仍仅接纳21—25，保持巨石原出生、移动和传送门。本切片由总会话统一提交推送。

定向检查：`HengeAppliedTests`，EditMode最多8项，`includePassingTests=true`；不得完整套件或无效过滤回退。范围为配置绑定、四轮持久资源/17足迹、轮次选择、晚回调和失败重试。结果以本目录实际测试输出为准。

资源路线：沿用 `ArcadeEarlyStageParameters` 与现有轮根资源句柄；环境Prefab四个 `Round_1..4` 子树只启用当前一轮，禁止附加脚本/碰撞。烘焙只保存本项持久Mesh/Material/Prefab及meta，不全局保存场景或Asset。合并保留顶点/法线/材质与阴影设置，按障碍父节点保留可回读足迹。

工具：[HengePolishBake.cs](../../../Tools/Unity/HengePolishBake.cs) 与采用源同一编译单元（包括后者的 `HengeCandidateGeometry`），静态 `HengePolishBake.Run` 烘焙，`HengePolishBake.Capture` 从持久Prefab回读拍摄。两份源拼接时仅保留一个using区，包含 `System.Security.Cryptography`。`Run` 写 `bake.txt`，`Capture` 写 `readback.txt`、四轮采用/正式全景及首轮采用/正式近景10张PNG；均需主会话实际执行后才构成验证证据。

验证边界：未进行Player Build、资源打包或真人双客户端整局验收；不得将待执行检查记为通过。

正式持久化四轮分别69/69/69/79个Renderer，保留原17个障碍足迹；[bake.txt](bake.txt)与[readback.txt](readback.txt)的逐面几何/法线/材质摘要四轮一致。五组采用/正式图片逐像素差异见[回读审计](image-readback-audit.json)，正式主角原尺寸同框。

首次工具因导入尚忙被Guard拒绝；随后原Combine使用worldToLocal*localToWorld取消变换，在3622面中有22面量化末位不同。逐面日志证实只是坐标/法线末位舍入，改为真实局部TRS链后仍保留原1e-4比较精度，四轮摘要全部通过，没有改采用几何或放宽门槛。失败诊断保留merge-mismatch.txt及面记录。独立审查指出校验前覆盖持久资源的P2，已改为四轮全部内存验证通过再保存，最终复审关闭，见[independent-transaction-review.md](independent-transaction-review.md)。
