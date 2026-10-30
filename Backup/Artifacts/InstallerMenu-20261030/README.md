# 安装器菜单整理静态检查

- PC快速打包含两种复用及生成安装器，共3项；PC全打包菜单6项，无重复或辅助工具残留。
- 手动与自动入口共用Release/WindowsInstaller批次路径及ISCC检查，保留已有游戏选择与脚本输入验证，不自动发布。
- 源码断言及本任务git diff --check通过；既有菜单合同已更新，未执行。
- 未操作Unity、编译、执行安装器或打包。尚无运行验证证据。
- 恢复验证时仅指定testMethod Game.Tests.FoundationFlowTests.PcPackagingMenusAreGrouped（上限1）及Game.Tests.FoundationFlowTests.QuickPackagingMenusExposeTwoReuseLevelsAndPcInstaller（上限2），分别执行，includePassingTests=true；不可全套或无效过滤回退。旧报告中的QuickPackagingMenusExposeOnlyTwoReuseLevels已被此名称替代。
