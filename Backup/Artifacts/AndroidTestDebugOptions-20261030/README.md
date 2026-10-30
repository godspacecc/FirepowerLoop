# Android测试包调试选项静态验证

- 测试作用域保持Development，并恢复进入构建前捕获的脚本调试和Profiler选项；实际BuildOptions按当前对应勾选添加AllowDebugging及ConnectWithProfiler。
- 正式channel分支提前返回压缩参数，正式选项关闭及结束恢复逻辑保持；单机正式包复用channel=true路径。
- 精确源码范围比较通过：仅修改ApplyTestBuildOptions与BuildOptionsFor，自动检查、正式策略、退出恢复和快速复用逻辑不变。现有测试断言仍适用，未运行测试。
- 独立Roslyn C#9语法解析两份源码，结果见syntax-check.json；提交前差异检查通过。
- 文档同步选项、生效时机、性能开销及基线不匹配时重建说明。本项限Android，不改PC。
- 未操作Unity、运行测试、编译工程、构建APK或连接Profiler/调试器；静态检查不代表设备连接验收通过。
