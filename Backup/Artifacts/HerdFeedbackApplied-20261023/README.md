# 牛群声音与扬尘正式接入

用户在HerdDust-Unity-v1有声动态预览及“这版动态和音色是否采用并正式接入？”之后回复“可以”。本次按该版本正式接入，不重复请求开工确认。预览目录日期沿用2026-10-23，实施收尾跨至2026-10-24。

![正式表现类隔离回读](herd-feedback-applied.mp4)

视频使用正式牛／主角／草地农场与持久化尘土材质，直接调用正式ArcadeHerdFeedback，11.6秒／232帧／20fps。声音事件来自正式反馈类的落蹄／起势／撞击输出，再用同源片段离线混音；不是正式AudioSystem录音或自然对局。接触近景只为看尘团与闷撞，不更改独立任务的玩家侧向击飞。代表帧已目视查看，音视频两路完整解码成功，无削顶。

## 正式行为

- 牛腿与蹄声按权威相位交错，脚下产生低矮土褐薄尘；刨地／鼻息只在整队起势的对应时间点发出。
- 4段蹄声变体＋刨地、鼻息、闷撞共7段单声道素材，直接复用采用版合成样本；正式采集地址逐一验证。
- 实际牛撞掉血后才提交玩家接触事实，存活和致死都包含；敌人死亡沿用已有Herd类别，活敌沿已有权威受击上升沿消费。重复快照、重连首包、倒退或过旧状态不补播。
- 尘团最多256粒，寿命至多0.65秒；短声最多16条待播／播放句柄，资源晚到超过0.15秒静音释放。暂停、阻塞UI、地图隐藏或离场取消旧反馈；倒地仅停止落蹄，当前撞击短尾音允许自然结束。
- 保留牛的正式模型、尺寸、3米纵队、7.15米/秒、伤害、侧向玩家落点与原碰撞规则。音量取本机角色平面距离，避免俯视相机高度影响。

材质及64×64软边贴图持久化于Farm/HerdFeedback，通过原牛Prefab直接依赖加载，无新素材异步链；运行时网格由当前地图唯一持有、离场销毁。协议仅追加玩家牛撞接触的序列、位置和时间，已通过Tools/gen-protocol.ps1双端生成；Config CSV与Luban输出未改。

## 验证

所有检查均声明真实短类、最大数量及includePassingTests=true，没有完整套件或无效过滤回退。

| 范围 | 上限 | 通过 |
| --- | --- | --- |
| Unity ArcadeHerdFeedbackTests | 11 | 11 |
| Unity ArcadeHerdVisualTests | 3 | 3 |
| Unity ArcadeHerdNetworkTests | 4 | 4 |
| Unity ArcadeEarlyStageLoadingTests | 8 | 8 |
| Unity ArcadeHerdImpactTests | 5 | 5 |
| Server HerdContactOnline | 3 | 3 |
| Server HerdOnline | 15 | 15 |

合计49项通过，0失败／跳过。新增首轮为2通过／6预期失败；审查回归明确复现了恢复旧撞击和阻塞UI待播声。客户端最终11/11，服务器接触记录首次0/3（缺权威字段）后3/3。原始失败、测试占用／RPC失败与中间结果保留，最终成功证据以feedback-green-final.json、各限定类JSON及两个服务器green／regression日志为准。

中间测试曾因材质测试使用了不适用的mainTexture属性、以及反射误将协议属性视作字段而失败，已改为真实_BaseMap引用和生成协议属性；没有为迎合断言改变正式视觉。资源采集地址初查缺失，已实际补入7个精确地址并通过正式Collector检查。UI初版回归在完成加载之后断言会被过期截止遮掩，已改为OnUpdate后、加载回调前断言取消，再观察RED并修正。

独立只读审查发现的阻塞UI门禁、恢复基线、致死反馈，以及接触缓存清理／恢复首包／致死尾音问题均已修复；最终复审无未解决P1/P2/P3。协议、服务器快照／克隆、客户端不可变副本与正式播放入口均核对。

另对准备提交的隔离Git树（仅HEAD＋本项差异）做只读复审，确认未夹带其他会话功能、资源／meta和播放API依赖齐全；从该树提取临时服务器源码副本，重新运行正式Tools/gen-protocol.ps1，双端生成文件与拟提交内容一致，HerdContactOnline原3项再次全部通过。没有创建worktree或复制Unity Library／UserSettings／MCP配置。详见isolated-tree-verification.json及对应日志；不重复计入49项。

asset-audit.json确认四类基础声音的正式WAV与采用候选逐字节相同，其余蹄声变体来自同次固定种子合成；牛Prefab移除新增dustMaterial引用并规范序列化空白后，与HEAD原内容完全一致。最终Boot未脏、compilationFailed=false；Console最后1条为限定加载失败／重试检查中预期的资源错误日志，与8/8结果配对，未将其清空后冒称无历史错误。

正式回读capture.txt中的candidateStampEstimate只描述旧候选采样器估计，运行时粒子上限以正式类256限制为准；不将该字段用作性能测量。未Player Build，未自然整局或真人双客户端手感验收。Unity最终场景和编译状态见unity-final-state.json，历史Console另留console-history.json；不宣称其他会话工作全部验证完成。

相关代码、资产、采用版和本次证据按项目规则单独中文提交并推送origin/main；其他会话改动保留。
