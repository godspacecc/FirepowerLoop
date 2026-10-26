# 四种采用版角色正式接入

采用源：`Tools/Unity/TenStageCreatureCandidates.cs`，对应 TenStageCreatures-v1-20261025 的 Spider、Ghoul、RedMangler、ZRex；不含旧巨猿。小龙保持已交付版本。

规则核对：
- 蜘蛛（special 6）：加速、低半径追击；攻击仅来自真实近战前摇与收招，没有周期技能。
- 食尸鬼（special 10）：预警阶段仍可追击，活动阶段按权威锁向加速；贴身近战仍由原近战剩余时间驱动。
- 红重甲（special 17）：预警护甲，活动边界朝当时目标发出既有追踪炮；进入射程可停步，不增加新炮或伤害。
- 僵尸暴龙（boss 2）：三个周期分别冲锋、伤害区域、减速区域；预警停步，后两类活动保持原地。

姿态接入：正式模型与采用源逐顶点保持；动作分段烘焙。步态消费实际位置差，上身动作使用活动年龄及真实近战状态，剥离预览腿部周期和髋部升降，保持上下肢接口。正式根不包含动画曲线。地图资源继续由既有Content聚合事务拥有。

动态材料是正式Controller/Prefab的隔离权威输入回读，非自然对局或真人双端试玩。

已执行自动检查：
- Unity EditMode短类 `TenStageCreatureTests`，最大20项，`includePassingTests=true`：20项真实RED后20/20 GREEN。RED调用因MCP回包重试显示“已运行”，通过 `rql-query testResults` 回读并保存 `runner-results.json`；实际20项全部因缺少正式演员/Prefab/就绪门禁而失败。GREEN结构化响应见 `tests-green.json`，20通过、0失败、0跳过。
- 命令：`Tools/PickupFeedbackChecks/Invoke-UnityMcp.ps1 -Tool tests-run -ArgumentsFile Backup/Artifacts/TenStageCreaturesApplied-20261026/tests-input.json -OutputFile Backup/Artifacts/TenStageCreaturesApplied-20261026/tests-green.json -MaxTests 20`。
- 服务端：`dotnet run --project Tools/TenStageCreatureOnlineChecks/TenStageCreatureOnlineChecks.csproj -- testClass=TenStageCreatureOnline maxTests=4 includePassingTests=true`，真实RED 3/4后GREEN 4/4；覆盖食尸鬼/暴龙锁向、蜘蛛/炮口目标朝向、近战/预警目标朝向及克隆/序列化。
- `Tools/Unity/Invoke-TenStageCreatureBake.ps1 -Readback`：四种共251份网格的顶点/法线/索引、Renderer颜色与采用源完全相同；24片段865个采样点，最大局部位置差0.000000134米、旋转差0；没有演员根曲线。上身对照明确使用剥离预览髋位移后的局部姿态，原始网格和关节旋转保持。
- 烘焙执行结束后MCP响应显示10次重试失败，但正式Prefab、片段及 `bake.txt` 已写完；未因此重放烘焙，随后真实runner处于空闲、request/ticks/pending均空，再成功跑GREEN和资源回读。历史错误保留 `console-before.json`；域重载后的 `probe-compiled.json` 为Console错误0。

[正式可播放回读](index.html)已完成：4条8秒原速和4条16秒半速，全部1280×720、各160帧完整解码通过，见video-audit.json；640张源帧逐张验证，四组最后20帧哈希完全相同（暂停不漂移）。最终编辑器状态见下文修复后记录。用户最新要求“实际验证先不做”：未启动Play、自然对局、真人双端或实体设备测试，未Player Build。


独立复审与组合采样修复（最终状态）：
- fix1：复审发现步态中的髋部起伏没有传给固定髋高的上身片段。增加两个真实Controller组合用例（左右肩、多个非零移动相位），短类仍为 `TenStageCreatureTests`，明确上限22、`includePassingTests=true`。`review-red.json`为原20项通过、新2项失败（首个相位红重甲1.3076cm、暴龙2.5003cm），修正上身采样后的髋平移后 `review-green.json` 22/22通过。
- fix2：随后逐帧正式回读在红重甲48Hz关键点之间捕获0.5054mm插值间隙。保留0.3mm门槛，不放宽精度；将上述两项组合测试各扩展到160个时间/位移采样，`interpolation-red.json`为21/22通过、红重甲对应真实失败。最终View从正式Prefab缓存每侧Core局部肩锚；每次片段采样后仅把该侧整条手臂平移至当前肩锚，不改骨角、骨长、采用网格、根位移或规则，不新增IK。`interpolation-green.json` 22/22通过。
- 最终命令：`Tools/PickupFeedbackChecks/Invoke-UnityMcp.ps1 -Tool tests-run -ArgumentsFile Backup/Artifacts/TenStageCreaturesApplied-20261026/tests-input.json -OutputFile Backup/Artifacts/TenStageCreaturesApplied-20261026/interpolation-green.json -MaxTests 22`。两个RED/GREEN文件分别保留，不覆盖原20项证据。
- 865项记录仅证明烘焙片段关键点与采用源的一致性；不把它等同运行组合姿态验证。最终重读865项仍通过（最大位置差0.000000363米、旋转差0）；另对红重甲/暴龙各160帧实际Controller组合姿态和左右肩进行检查，最大肩锚间隙分别0.000000134米、0.000000481米，见各角色 `composition-audit.txt`。
- 受影响红重甲、暴龙的视频与分帧图已按最终View重新拍摄，原速/半速完整解码通过；`video-audit.json`和入口页面已更新。暂停20帧哈希均相同。
- 最终 `probe-after.json`：F工程Boot、未脏、非Play/编译，Console错误0、警告10。历史失败证据保留；未人为清空Console，编译域重载由Unity自行刷新。
- 本任务共涉及的正式资源/运行脚本/测试/工具/有效证据精确列于 `files.txt`；临时草稿、源帧与输入JSON不提交。未自行提交或推送，由主代理统筹。
