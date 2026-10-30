# 删除 Android 独立模块测量菜单

- 删除菜单属性和 BuildAndroidMeasurementPackageMenu 包装方法，共 6 行；与修改前 HEAD 精确比较，余下源码完全一致。
- 保留 PC 测量入口、公共构建方法、报告分析入口、模块统计和标签检查；不清理历史产物或 .meta。
- 使用说明第二十三项同步，说明替代入口及历史测量包仍可能影响最新版本选择。
- 独立 Roslyn C#9 语法解析结果见 syntax-check.json，复现脚本为 check-syntax.ps1；提交前差异检查通过。
- 未操作 Unity、运行测试、构建资源/Player、分析实际报告或发布服务。
