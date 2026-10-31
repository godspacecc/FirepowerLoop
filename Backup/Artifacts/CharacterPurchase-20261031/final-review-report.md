# 最终独立整体审查

审查采用 code-role、unity-ui、ui-prefab-authoring、resource-load-check、luban-config。只读实际实现及现有报告/回执，未操作Unity、未重复测试、未修改生产代码。范围为本次角色购买及每次幸存者入口选角；共享工作树其它地图差异不属本任务。

## Spec结论

主体符合确认要求。CSV商品1/7/3/4/9与50/100/150/200/300一致，默认0免费；购买权威价格来自配置，未知商品拒绝；余额与永久所有权同文件原子替换，V1迁移保留金币，V2显式字段校验已修复，消费后领取水位/幂等保持。菜单每次打开选角，开始前和进图后复验所有权，重开沿用；PlayerCtrl同步事件应用整身外观。仍有下面一项错误出口本地化需补齐。

## Quality结论：一项 Important（P2），无 Critical

P2：开局再次验证失败时会向玩家暴露内部角色错误码。
位置：Client/Assets/Game/Scripts/System/Arcade/SurvivorSceneFlowSystem.cs:39、54；Client/Assets/Game/Scripts/Controller/UICtrl/Home/StartUICtrl.cs:228附近 StartSurvivorAsync 的 Notify.ShowFloatTips(result.Error)。
复现条件：玩家选择已拥有角色并点击开始后，在资源准备/场景异步加载期间所有权文件变坏、所有权失效或角色配置不可用。选择页的 Begin 已经通过且关闭，StartAsync/TryStart 的再次校验返回 wallet.read_failed / character.not_owned / character.config_failed 等，结果直接透传给浮动提示。选择页自身 ErrorText 已做本地化，但这条要求保留的二次校验出口绕过它。安全拒绝开局正确，玩家错误状态不符合已确认的本地化要求。
最小修复：在开局表现出口对本次新引入的 character.* / wallet.* 错误码统一映射已有 ui.character.* 文案；保留原有普通错误文本，不将内部码直接显示。以一个精确测试覆盖二次验证失败到玩家文本即可，不扩大重测。

## 已核对的质量与证据

UI购买仅传ID，展示使用一致快照；Box每次刷新移除监听并重设文本、头像、状态、交互。选角开始先消费回调再Hide；Start菜单实例、代次、可见性与startingRun门禁有效。事件由View销毁解绑。CustomListView承接列表而非Controller实例化；精确生成字段/外部Box反序列化契约补充仍由UI执行者完成，本报告不预先认定未到达的测试通过。

账本失败不Write，重复购买不扣款，余额不足及溢出拒绝。store ??=保留的SurvivorResultFileStore只有readonly路径，没有跨局缓存权威数据；正常运行仍每次TryRead，当前固定玩家1行为不变。

阶段账本5/5、业务3/3及UI报告两项限定测试提供既有证据。source-collection.json记录四个真实BeginCollect地址及startup/arcade标签；runtime.txt记录真实YooAsset加载四地址成功并释放、StartAsync/RestartAsync成功、实际PlayerCtrl appearance=1且scale=(1,1,1)，临时账本恢复。final-unity-state.json为正确项目Boot、非Play、非脏、0 errors（7 warnings）。未将AssetDatabase替身当作运行加载证据。

最终静态中英文截图由root目检；本审查未独立评判未查看的图片。没有逐帧动作穿模或额外宽高比证据；这属于明确验证边界，不能声明所有动作/设备都通过。未打包、未验证旧发布包是任务授权边界，并非缺陷。

## 非阻断维护建议与提交约束

SurvivorCharacterProduct/ShopSnapshot新增公开属性及构造函数仍缺逐项中文注释，StartUICtrl.OnShopClick的“只读商店”注释已过时；建议随最小修复补齐。无须改变业务设计。

生成日志及源CSV/配置适配器与新增表对应；早期Tables/语言基线有事后反推限制。提交必须隔离本次TbSurvivorCharacter及语言key、Config/gen.bat计数+1，不整文件夹带另一任务TbSurvivorMapLayout。等待上述P2关闭及UI正在进行的有限引用检查后再作最终交付判断。

## 最终修复复审（以本节结论为准）

Spec：通过已确认范围。Quality：通过；原P2关闭，无新增Critical/Important发现。

核对ui/fix.patch及当前StartUICtrl、SurvivorSystem：失败出口确实调用FormatSurvivorEntryError，角色与钱包码使用既有本地化，普通场景说明保留。默认0绕过购买所有权读取后仍在TryPrepare检查钱包，失败统一wallet.read_failed且不写文件，内部日志保存细节；没有把坏档转换为空钱包。原有结果账本读取顺序与失败语义保持。中文注释及过时商店说明已补齐。

读取fix-final-test.json确认精确RevalidationFailureUsesLocalizedEntryMessage为1/1通过；检查测试正文覆盖真实StartAsync中已购1与默认0坏钱包拒绝、中英文、未拥有与配置错误文本、普通错误保留及坏文件未覆盖。未重跑测试或操作Unity。

shop-contract-tests.json与entry-contract-tests.json各精确方法1/1通过。generated-contract.json确认正式两个宿主及外部Box共11+12+5=28个生成对象引用非空且与ReferenceCollector一致，关闭此前引用补证待办。

root报告隔离commit.index仅保留角色表，提交版本客户端40/服务端36表与gen计数一致，排除未提交地图表。本复审未操作或独立重验index，提交隔离事实由root负责；早期基线限制不因此改写。此前未验证额外宽高比、逐帧动作和旧发布包的边界继续有效，不阻断本次已限定交付。
