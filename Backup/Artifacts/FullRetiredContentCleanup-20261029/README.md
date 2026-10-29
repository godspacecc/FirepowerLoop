# 退役内容完整清理（2026-10-29）

用户在首轮清理后收到六类剩余清单，明确“那就全部清理掉吧”。本批已完成该范围的依赖解除、删除、生成、限定验证与独立审查。原首轮清理已于8311ea8c提交；本记录取代已删除的两份首轮审计／归档入口。

## 清理结果

- 删除1,446个原已跟踪文件，约200.3 MiB历史内容；包含资源本体、配对meta、旧代码、工具、资料和旧日志。详细汇总见[final-summary.json](final-summary.json)。没有重写Git历史，也没有重新压缩一份退役原件副本。
- 从Unity移除573个退役资源文件：首批76个旧依赖链资源、闭包核查后的493个资源、4个旧HUD／Box。删除只对已验证的文件逐个执行，未递归删除候选目录；随后清理108个实际为空的目录。
- 旧NPC、文明交互、撤离、临时建筑、火把、旧天气、河谷／何家村制作入口、搜尸接口及无调用布局代码已移除。10份敌人Prefab剥离旧搜尸节点，保留战斗碰撞和攻击进度。
- 旧普通地图MobileJoystick、PlayerConditionHud、GameplayGuideHud及PocketSlotBox不存在现役加载者或序列化入边，已连同生成View及专属旧测试退役；现役ArcadeHud与Input System原Stick保留。
- Pause清掉返营按钮及由旧控制器强制隐藏、没有绑定的重复主菜单占位，通过正式UICodeSpawner重生View。四个实际入口继续为继续、返回主菜单、设置、退出，返回入口由既有BtnOnline及动态文案承担。
- 配置删除19个旧物品、7套旧武器／外观、相关掉落及4种旧敌人；街机表现载体从旧金属身份改为loot.arcade.drop／物品49，拾取外观与战斗规则保持。采集效率、火把燃烧、能量／饮水恢复及空MapDisplay查询契约退役。
- Config/gen.bat完成双端生成，客户端35／服务端30张表。525个旧文案键由Config/Localization/retired-keys.json权威清单发布删除，两语言各888键，正式本地化生成器重生Texts。生成前强制导入刚发布的JSON，避免读取旧TextAsset缓存。
- 删除81个旧资料文件，并实质清除存续UI／联机文档中旧库存、远征、World和局外钱包章节；保留当前街机、第一人称、账号房间聊天、发布和素材来源记录。之前退役原件Zip、过时初轮审计及根目录旧日志已清理。

删除依据与范围分别见[资源清单](asset-removal-manifest.json)、[无现役根资源清单](unreachable-removal-manifest.json)、[旧HUD清单](legacy-player-ui-removals.json)、[代码审计](runtime-audit.md)、[配置记录](config-cleanup.md)、[资料清单](docs-cleanup.json)。

## 必须保留的现役内容

当前单人／双人、本局强化、旧街机存档回退和旧路线继续均保留；不读取或迁移AnotherDay玩家数据。备用Art原始素材库、实际使用的模型／动作／Shader、来源与许可证说明、现役作者工具输入、已确认方案和有效预览／验证记录不按“旧”字删除。QuickBar的旧Boot兼容身份继续保留。

资源根同时覆盖当前收集入口、配置动态地址、运行时裸资源名、作者／诊断／测试输入、项目设置GUID与场景；闭包审查补齐了拼接路径的进化武器、丧尸动作、动态Shader及现役来源文档。独立反向检查：保留资源指向最终493项候选的依赖为0。

造型库选择及Inspector刷新现按稳定武器ID读取当前角色自己的已采用目录，缺失明确拒绝；未改变模型、动作、材质或姿态数值。个别既有试装握点有不可达间隙，本批不重做其美术，验证保留可达误差、理论可达边界、骨长及锁骨约束，不声称所有试装握持无缝。

## 最终验证

- 243个不同的限定EditMode用例最新执行结果全部通过，详见[逐项汇总](tests-final-summary.json)。使用真实短类名或完整方法范围、逐项声明上限、includePassingTests=true；参数化旧近战回退只选择同一真实方法的5个用例。
- [服务端拾取检查](server-pickup-tests.log)限定PickupFeedbackServerChecks，7/7通过；[服务端编译](server-compile.log)0警告、0错误，验证使用本项目新生成配置，不启动常驻服务器或读取玩家数据。
- [最终资源引用检查](reference-validation-final.txt)：138份正式Prefab、1,949个YAML资源，Missing Script、缺失外部GUID及内部fileID均为0；编译失败标记false、TimeScale=1，Boot编辑态未脏。
- 原海岛、字体和ProjectSettings差异均不进入提交，开工前后SHA256一致。Unity保存UI时曾改写字体图集，本次通过已存在的Git对象找回精确原件并核对基线，见[恢复记录](font-baseline-recovery.json)。未将字体改写混入本任务。
- 中途失败均保留在分轮日志：包括旧AK／弓／专属角色目录夹具、退役UI断言、发布清单数组封装、旧TextAsset缓存，以及EditMode测试过早恢复文件。Pause最终等待实际事务完成才恢复文件和重试，保留SafeCheckpoint与遮罩终态断言，连续两次整类21/21通过；没有改生产场景流来迎合测试。一次同时传query和普通筛选参数的请求被工具拒绝、未执行；随后维持同一5用例范围，仅改为工具支持的选择方式，没有扩大范围。
- 独立静态审查及最后两项增量复审未发现未解决P1／P2／P3问题。
- 未运行完整套件、PlayMode、资源打包或Player Build，未进行真人双端／真机试玩。

按项目既有授权，仅本任务差异使用中文提交信息提交并推送origin/main，完成状态以Git实际结果为准。

## 追问补查与补漏

2026-10-29复核发现上轮遗漏Tools/Unity中的旧火把预览图及InventoryCharacterPreview旧背包／装备截图和日志，共19文件、2,146,016字节。删除前全仓库文本引用搜索为0，已逐文件补删；详见[补漏清单](followup-preview-removals.json)。此前1,446文件为原批统计，不含本次19文件。本次仅涉及Unity工程外的历史产物和记录，验证删除路径不存在、差异范围和三份原有修改SHA256保持，不重跑Unity测试。当前备用素材、有效预览／验证证据、受支持兼容代码及本地缓存不是这批退役产物。
