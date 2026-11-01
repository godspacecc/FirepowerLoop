# 幸存者金币再次降低九成

用户2026-11-01明确要求“还是概率过高 再降低到目前的十分之一吧”。已有GoldDropMultiplier从0.1改0.01，最终封顶概率仍在共享公式后乘倍率，最高0.5%。仅Config权威CSV数值改变，Config/gen.bat双端成功，两个tbsurvivorrule.bytes重生；无schema/生成C#修改。

枚举并指定Game.Tests.SurvivorConfigTests.GoldRateScalesAfterCapWithoutChangingExperienceOrArcade，最大1项，无参数，includePassingTests=true，1/1通过。验证真实bytes倍率0.01、字符串ID读取、经验原值、最高0.005、非法倍率拒绝。不是随机大量击杀统计。未新增测试或修改生产C#，原检查预期同步到新值。

Unity正确工程，Boot干净非Play，历史工具Console错误不作为新编译故障；无Player Build。当前四个任务文件相对HEAD仅本次倍率及测试预期变化，提交时独立index保留其他暂存工作。审查与Git回执另计。
