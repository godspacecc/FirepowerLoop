# 幸存者金币掉率

已按最终封顶概率乘0.1接入，封顶50%降为5%；Config survivor_rule新增GoldDropMultiplier=0.1，Runtime边界SurvivorConfiguration验证有限0到1并映射。只修改幸存者TryDropGold判定，不修改共享ArcadeEnemyDropRules、经验或每份1币、账本。gen.bat双端成功，generation.log保存输出。

测试：先枚举真实完整方法Game.Tests.SurvivorConfigTests.GoldRateScalesAfterCapWithoutChangingExperienceOrArcade，最大1，无参数展开，includePassingTests=true，1/1通过。验证真实bytes倍率与经验、共享规则50%封顶及缩放后5%、非法配置拒绝；源码检查确认倍率位于共享Chance调用之后。非随机大量击杀统计、不声称实机统计验证。无红灯运行，不声明完整TDD红绿循环。

baseline保存任务前文件，changes.json和review.patch相对基线，仅本任务增量；二进制差异见changes。未提交、未操作index、未打包。等待root独立审查。

## 主键审查修复

GoldDropMultiplier已移至Id之后，gen.bat双端完成（generation-fix.log），两端TbSurvivorRule恢复Dictionary<string,...>及Get(string)，新增真实ID查找断言。修复后测试暂被并行夜间任务编译错误SetSurvivorVisibility接口缺失阻断（test-fix-blocked.json），此前1/1不能当作本修复后验证。review.patch已剔除回血/夜间文本增量并归一换行；生成bytes当前同时含夜间任务NightAimRange，须联合审查，不可将该二进制归为仅金币任务。工作区其他任务内容保留。

## 修复后验证完成

只读确认Unity isCompiling=false、scriptCompilationFailed=false。等待并行测试结束后，同一限定完整方法再次执行，最大1、includePassingTests=true，1/1通过；tests-fix-passed.json为原始回执。包含Get("survivor.default")的真实字符串主键查询断言。前述编译阻断已解除。未提交，提交隔离由root处理。

## 隔离提交材料已准备

基于HEAD 9de708f1fad5b0b9403ba27910093f93265d3c9d，prepare-isolated.py从git archive提取Config和本地化依赖，只添加金币倍率，执行快照正式gen.bat，双端成功（isolated-generation.log）。verify-isolated.py生成最终10文件commit-manifest-final.json及commit-review.patch并统一HEAD文本换行，验证两端字符串Id主键、倍率存在且无NightAimRange。isolated/产物不改正式工作区。commit-manifest.json是生成器全输出的中间比较，含换行噪声，不用于提交；必须使用commit-manifest-final.json。未操作任何index、未commit。该隔离bytes已由正式生成器验证，Unity测试是在联合工作区执行，未声称隔离快照经过Unity运行。
