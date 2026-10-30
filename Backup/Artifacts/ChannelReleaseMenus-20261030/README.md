# 渠道发行菜单整理

- 用户要求独立渠道发行分组，随后确认删除渠道包/单机包两个手动检查入口。
- AndroidBuildUtility 仅修改两处菜单路径为 Tools/渠道发行/Android；AndroidChannelBuildUtility 仅删除两个菜单包装及无其他调用的手动文件选择/报告提示方法。
- 与修改前 HEAD 精确比较通过：其余代码完全一致，打包后 InspectAndSave/InspectOfflineAndSave、失败阻断、报告输出均保持；旧报告和APK未删除。
- 使用说明第十二/十三项改为已移除状态，第十/十一项及渠道使用说明同步新入口和自动检查、失败重试方式。
- 独立 C#9 语法解析两份源码；提交前差异检查。未Unity编译、运行测试、实际APK检查或打包。
