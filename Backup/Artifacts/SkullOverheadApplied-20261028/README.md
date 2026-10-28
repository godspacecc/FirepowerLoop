# 拾取后取消头顶骷髅：正式接入与验证

2026-10-28。用户查看[采用预览](../SkullOverheadPreview-20261028/comparison.png)后回复“可以”，确认取消红紫骷髅拾取后的头顶显示。

- 仅清空 `Config/DataCsv/arcade_powerup.csv` 红紫两行的生效模型引用；通过 `Config/gen.bat` 双端生成成功，客户端36表、服务端30表。变化产物只有双端 `tbarcadepowerup.bytes`，两者SHA256相同，见[generation.log](generation.log)、[hashes.json](hashes.json)。
- Provider校验和转换同步允许且仅允许WeaponSkull省略生效模型；其他强化继续必填，非法非空文件名仍拒绝。现有空地址显示入口会释放旧条目而不再加载，无需修改战斗、HUD、掉落Prefab或meta。
- [正式配置隔离回读截图](applied.png)与采用候选仅9个像素不同（最大通道差50），人物姿态、比例和无头顶模型效果一致。由[Capture.cs](Capture.cs)读取正式生成表后拍摄临时实例，非自然对局实拍。
- [正式入口回读](readback.txt)：双端红紫生效引用均空；本机0及玩家1/2各两种强化，共6次正式SetPowerup入口均无视觉条目或模型；两种地面掉落引用存在。只读脚本见[Readback.cs](Readback.cs)。未启动服务器或宣称真人双端验收。

定向Unity EditMode检查，均includePassingTests=true，无完整套件、PlayMode或Player Build：

| 真实范围 | 声明上限 | 实际结果 |
| --- | --- | --- |
| ArcadeSkullPowerTests | 24 | 20/20通过 |
| ArcadeCompactHudTests.SkullStatusUsesColoredIconWithoutTextFrameOrProgress，红紫各精确带参数方法 | 合计2 | 各1/1通过 |
| ConfirmedWeaponPresentationTests.ApprovedSkullDropMatchesCoinAndKeepsSizeAfterReuse | 1 | 1/1通过 |

规则检查首次20项因Provider仍拒绝空模型而失败，见[rules-red.json](rules-red.json)；修正必填边界后[rules-green.json](rules-green.json)20项通过。HUD无参数方法未匹配用例；一次同时传query与filter被工具拒绝，没有执行测试或回退完整套件。最终明确参数的[红图标](hud-red.json)、[紫图标](hud-purple.json)均通过。[地面复用检查](ground-green.json)通过。Console最终仅保留上述工具参数拒绝日志，无编译或产品异常；Boot未脏、未Play。

独立只读审查通过，无Critical/Important/Minor问题：确认双端二进制只有两项字符串变化、旧视觉释放及异步回调不会重新挂载、地面和HUD/强化保持。真实双客户端运行和无关布局不在本批自动验证范围。

开始时46项无关文件SHA256全部保持（[基线](unrelated-before.json)）；其他会话暂存的Boot资源及总清单段落继续保留，仅选择本任务提交。
