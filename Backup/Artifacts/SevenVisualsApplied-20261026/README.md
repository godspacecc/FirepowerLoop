# 七项正式接入与自动检查

用户已明确“七项全部采用并正式接入”。本批正式采用要塞庭院7、锦鲤桥庭10、终局巢穴20三场四轮环境，以及蜘蛛6、食尸鬼10、红色重甲怪17、僵尸暴龙2。终局小龙保留525e828e已交付版本。实现、专项与集成检查均已完成，最终提交/推送以Git记录为准。

[统一查看入口](index.html) · [三场景记录](../TenStageEnvironmentsApplied-20261026/README.md) · [四怪物记录](../TenStageCreaturesApplied-20261026/README.md)

## 范围与行为

- 新环境保留十二轮原通路、障碍足迹与战斗边界；仅增加采用资源映射，Config/gen.bat双端生成。双方目录精确支持5/7/10/20/21—25九场映射。
- 四怪物使用正式持久网格和动画。步态跟随实际移动，攻击跟随既有真实近战/技能时间；重复/迟到快照不自行推进动作。没有新增伤害、技能、协议或局外规则。
- 原共享资源加载事务管理环境与角色，缺依赖不能发布就绪，离场/迟到回调保持原释放语义。
- 用户最新“实际验证先不做”：单人自然实玩、真实双人、实体设备验收暂缓；本批未进入Play、未Player Build。以下为自动检查和隔离正式资源回读，不代表实玩完成。

## 自动检查

客户端共88项，全部限定EditMode、真实短类、includePassingTests=true、0失败/跳过：

| 短类 | 明确最大数量 | 实际通过 | 证据 |
|---|---:|---:|---|
| TenStageEnvironmentAppliedTests | 17 | 17 | ../TenStageEnvironmentsApplied-20261026/tests-green.json |
| TenStageCreatureTests | 22 | 22 | ../TenStageCreaturesApplied-20261026/interpolation-green.json |
| HengeAppliedTests | 8 | 8 | HengeAppliedTests.json |
| FinalDragonPresentationTests | 9 | 9 | FinalDragonPresentationTests.json |
| CreaturePresentationTests | 14 | 14 | CreaturePresentationTests.json |
| ArcadeRoundContentViewTests | 7 | 7 | ArcadeRoundContentViewTests.json |
| TenStageRouteTests | 11 | 11 | TenStageRouteTests.json |

服务端共23项：TenStageEnvironmentOnline3、TenStageCreatureOnline4、TenStageRouteOnline8、FinalDragonOnline4、CreatureOnline4，全部通过且保留通过项输出。前两项新功能证据在各子目录；后三项回归日志在本目录。无全套或无效过滤回退。

客户端复现：`Tools/PickupFeedbackChecks/Invoke-UnityMcp.ps1 -Tool tests-run -ArgumentsFile <明确短类的输入JSON> -OutputFile <证据JSON> -MaxTests <表中该类上限>`；输入必须包含`testMode=EditMode`、真实`testClass`与`includePassingTests=true`。各回归按表串行运行。

服务端复现：`dotnet run --project Tools/<短类>Checks/<短类>Checks.csproj -- testClass=<短类> maxTests=<上述该类上限> includePassingTests=true`。使用隔离配置/数据，不连接真实玩家。

## 资源与复核

- 十二轮几何、法线、逐面材质/阴影摘要与采用源一致；足迹保持。三十张PNG全部解码，十五对图中七对像素全等，其余最多120/1920000像素有差异；详见environment-pixel-audit.json。
- 四种角色251份网格/法线/索引/颜色与采用源一致；24片段865关键点回读通过。组合肩接口问题经两轮真实RED→GREEN修复；最终红重甲/暴龙各160帧双肩间隙均小于0.000001米。片段关键点对照与运行组合检查分别记录，未混为同一证据。
- 八条原速/半速视频完整解码，四组暂停20帧哈希保持；源帧/草稿/输入JSON忽略，仅保留有效媒体、工具和报告。
- art-meta-audit.json：新美术170引用和七个正式Prefab1490外部引用均解析；URP包引用已明确追溯。
- 三场景独立审查无实现问题；怪物初审问题及插值自验问题已分别修复并复审关闭，见review-*.md。最终跨任务审查无新问题，见review-integration.md。
- 原33项本机差异按初始SHA256逐项保护。另14个本次编辑器导入副作用已备份并恢复（7个小龙材质、6个主角网格与URP设置），不夹带提交。

## 最终收尾

- 集成审查通过，无未关闭实现问题；49项旧内容回归补齐后合计客户端88/88、服务端23/23。
- `probe-final.json`确认本项目Boot、干净、非Play/编译，TimeScale仍0。Console原始计数1错误/10警告，未清空；`console-current.json`回读当前条目，唯一Error明确来自Henge缺轮测试的`LogAssert.Expect`预期失败路径，测试8/8通过。`console-analysis.json`记录对应关系，未发现未解释新增错误。没有把预期Error计数隐瞒成0。
- `preservation-audit.json`确认33项原有差异逐文件SHA256保持；两份采用源内容不变，工作副本与Git仅有既有CRLF/LF差别。184个展示链接全部存在，见gallery-link-audit.json。
- 发现一个56分钟前遗留、未持有打开句柄、无活动Git进程的空index.lock，已移至本机忽略的协调备份后恢复正常Git操作；未改索引内容。
- 按本项目授权只提交本批范围并推送origin/main。实玩与设备验收按用户最新指示暂缓，不能把本次自动检查称为发布级实玩完成。
