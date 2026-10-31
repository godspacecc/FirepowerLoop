# 角色购买账本阶段独立审查

## Spec结论

原子扣款与所有权、重复购买不扣款、V1兼容且保留Sequence<=Balance旧约束、消费后领取幂等、水位以余额加支出校验、一致快照均符合本阶段设计。未知商品及价格由上层Config验证是明确边界，不把后续UI/config缺失列为缺陷。

仍有一项坏档拒绝边界应修复后通过。

## Quality结论：需修复一项Important（P2）

位置：Client/Assets/Game/Scripts/Utility/Save/SurvivorGoldFileStore.cs:22（新增Spent/OwnedProducts字段）及Read校验约第98—105行。

V2新增字段没有按版本强制存在，反序列化会把缺失Spent默认成0、缺失OwnedProducts默认成空列表。当V2文件同时缺少这两个字段且Sequence<=Balance时，Read会接受它为合法文件。例如 {"Version":2,"Balance":100,"ActiveRun":"run","Sequence":1} 被当作合法的无所有权钱包。V2正常只由成功购买产生，所有权和支出应同时存在；当前会静默接受丢失购买记录的坏档，下一次领取或购买会覆盖文件，无法再报告该格式损坏。

建议按版本校验字段存在性：V1允许省略新增字段；V2要求Spent和OwnedProducts都显式存在且类型正确，不用可缺省字段推断完整性。添加上述缺字段V2的读取/购买/领取均失败且文件保持不变的限定测试。无需修改本阶段之外的代码。

其余审查：TryPurchase在同一进程锁内读取验证、修改临时Document后原子替换；写失败不返回成功且原余额输出保留。重复所有权在扣款前直接返回。Spent使用checked累加，Balance在充足判断后减法；水位减法避免Balance+Spent溢出。TryReadSnapshot一次Read并复制数组，不暴露可变存储对象。

已阅读新增测试及tests-result.json；新增测试覆盖购买后同一receipt不重领、下一序号领取、重复购买文件不变、余额不足/参数错误、锁文件替换失败、非法旧水位。未重跑测试或操作Unity，未改生产文件。测试通过不覆盖上面的缺字段V2边界。

## P2修复复审（最终结论）

Spec：通过本阶段账本范围。Quality：通过，原P2关闭，无新的Important/Critical问题。

Read现在先保留JObject，在V2分支明确要求Spent为Integer、OwnedProducts为Array且元素全部为String，缺任一字段或类型错误均抛错，事务不会进入Write。ToObject先执行遇到不可转换值也会失败，同样不写盘；不存在默认空集合绕过。V1不受新增存在性要求影响，原水位及禁止消费记录约束仍保留。

MissingV2PurchaseFieldsNeverOverwriteLedger覆盖双字段缺失、分别缺失、字符串Spent、非字符串所有权元素，验证快照/购买/领取均失败且文件不变。review-fix-tests.json原始回执5/5通过，同时既有V1迁移、购买后领取等回归通过。未重跑测试、未操作Unity、未修改生产文件。仅复审既有P2与该修复影响，不将后续UI/config阶段纳入结论。
