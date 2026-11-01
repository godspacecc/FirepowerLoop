# 再次降低金币掉率独立审查

Spec：通过。Quality：通过，无重要问题。

CSV按表头解析，GoldDropMultiplier列类型float，数值由0.1变0.01；其余配置含NightAimRange保持。实际TryDropGold仍在共享Chance最终封顶后乘独立倍率，因此相对当前再除10，封顶0.5%，不影响经验、闯关、每份1币或存档。

二进制独立核对HEAD：客户端及服务端tbsurvivorrule.bytes长度均保持200字节，唯一变化是偏移18—21的四字节float，从约0.1变约0.01，区间外完全一致。与现有生成读取器Id后读取GoldDropMultiplier对应；不存在并行字段混入或布局变更。generation.log记录双端成功。测试只同步三个预期，tests-result.json指定方法1/1通过，包含真实ID、bytes倍率、封顶与非法值检查。

未重跑测试、未操作Unity、生产代码或index。本审查不声称随机击杀实机统计；数值源、生成产物与调用关系足以支持本次纯倍率结论。
