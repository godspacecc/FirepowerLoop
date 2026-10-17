# 普通轮总量补齐证据

2026-10-17。current-route-queues.csv采集于修改前的v5实现，表示当时1653—1757个单人普通队列条目，不是修改后的当前总量。新实现沿用正式公式；此处不把独立Boss、增援、配置枚举或测试当作自然通关。

客户端CasualPopulationFlowTests有效3/9红→9/9绿；新增金币边界9/10红（预期56、实际22）→最终10/10绿。内容回归FullRouteContentFlowTests最终11/11。服务端CasualPopulationOnline有效3/9红→9/9绿，追加金币9/10红→10/10绿，FullRouteOnline最终28/28。上述每类均非空明确过滤、includePassingTests=true，上限依次10／11／10／28。最初服务器夹具把独立Boss错误交给普通PrepareWave的失败排除，改走BeginFinalBoss和PrepareRoundBoss后才记录有效红。

金币修复经新鲜只读评审复核关闭，其余所查边界未发现确定缺陷。Config/gen.bat双端生成成功，本段只改变正式规则指纹及对应规则bytes；哈希见JSON。NU1900为漏洞索引网络警告，所有服务端检查正常执行。发布仍0，不打包，真实完整路线、双端、设备及节奏／平衡待验。

## 后续实际场景观察

7924925之后用正式v6配置在明确隔离起点观察配置15与20的第一普通图，各12秒；队列加已生成序号在22个样本中分别保持190／400，实际射击、敌人生成与受伤已观察。后段两图已查看，临时局正式放弃后拥有网格／材质均0。见[实际观察](ActualLateMaps/README.md)。不计自然通关、完整Boss或移动性能验收。
