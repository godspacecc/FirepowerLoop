# 第二关水体正式接入（2026-10-29）

用户查看[已采用动态候选](../DockWaterPreview-20261028/water-comparison.mp4)后回复“可以”，授权正式接入右侧青蓝深浅、缓慢细水纹和少量岸边泡沫。正式水面已接入；码头Prefab、七份网格、其它材质和既有水中不可攻击规则未修改。

[正式动态回读](index.html)：左侧采用候选的同时刻参考，右侧正式Prefab材质；同机位同灯光，先全场后近景。视频约26秒，按测得拍摄间隔播放，非真实对局录像。

## 实现与验证

- 沿用候选全部配色、水纹速度和泡沫公式，以Unity内建 `_Time.y` 驱动。岸距与水纹使用地图局部坐标，水面关闭动态合批，随所属地图移动。
- 岸距由采用预览的同一真实平台算法烘焙，512×512 PNG以RG高低字节保存0..32米。线性、无压缩、无mip、Clamp/Bilinear，避免依赖浮点纹理过滤。最大量化误差0.000245米。
- 正式材质持有纹理引用；保留原Shader与材质meta。旧码头制作入口不再覆盖Shader，重建材质也会恢复岸距引用。连续再次烘焙后PNG、meta和材质SHA256均一致，见bake-idempotence.json。
- `ArcadeDocksVisualTests`限定EditMode、上限3、includePassingTests=true。新增岸距持久化约束先出现真实2通过/1失败（缺少岸距入口），接入后最终3/3通过。见tests-red.json与tests-green.json。第一次运行使用了未刷新的旧测试程序集，主动重新编译后才记录真实RED，不把旧程序集通过作为证据。
- 80张正式帧与80张候选同时刻参考，真实Unity编辑器时钟；全场/近景每通道平均差异约0.00094/0.00066（0..255），正式首尾动态差异2.67/2.07。整体平移后差异0.49/0.39，包含连续时钟造成的轻微水纹相位变化。H.264/yuv420p视频完整解码通过，见verification.json与times.csv。
- 一次独立只读审查无Critical/Important/Minor发现；核对shader公式、编码/线性插值、所属坐标、导入设置与旧制作入口。未运行会重建全部环境的旧入口，已静态审查该路径；烘焙入口实际重入已验证。真机驱动和性能没有实测证据，不将纹理格式审查等同真机验证。
- Boot回到编辑态、未脏；正式水面Shader无编译错误。Console启动时已有MCP连接错误；本轮一次动态编译请求漏加System.Reflection导致工具错误，补齐后成功；既有测试代码过时API警告与材质名/文件名不一致警告保留。曾出现的混合换行已统一修正，无新的产品运行异常，见errors-after.json。没有清空Console、修改存档、启动真实对局或Player Build。
- 本轮前后正式资源指纹比较只有DockWater.shader及Material0.mat改变，新添岸距PNG/meta；码头Prefab、网格及其meta保持。三份用户原有无关改动指纹保持，不夹带提交。

## 制作入口

`Tools/Unity/DockWaterApplied.cs`的Bake入口，与已采用目录的DockWaterPreview.cs一同动态编译（合并using并包含System.Reflection），在本工程空闲编辑态执行。仅烘焙岸距与绑定正式材质，不保存场景。动态回读以本目录DockWaterAppliedCapture.cs与相同预览类共同编译，调用Run后等待capture-status.txt完成，再运行verify_and_encode.py并明确传入本机ffmpeg路径。

原始中间帧只用于编码与比较；完整解码核验后清理请求被自动审批策略拦截，故保留在本批目录本机并排除Git。正式提交保留代码、时间记录、对照图与视频以便复核。运行期编辑器/MCP日志及重复工具参数同样留在本批目录本机，不纳入Git。
