# 特殊枪弹与爆炸证据

before.png/after.png：同镜头同尺度隔离资源预览，列顺序镭射/穿透/火箭/榴弹，上排能量爆炸/火箭/榴弹爆炸。真实正式资源，非自然游戏截图。

visual-three-green=3，style-five-green=5，presentation-six-green=6，三个完整CombatSystem方法各1；合计17个不同限定EditMode测试，通过数以Summary.PassedTests为准。全部includePassingTests=true、显式最大数3/5/6/1，未跑全套。red-two、piercing-red与intermediate-one-failed保留失败过程。

config-proof：权威CSV只增加镭射表现资源名，双端生成bytes哈希相同，无战斗数值修改。

actual-seven：真实Boot进入临时档正式新局，每6秒经测试掉落入口切枪，不是自然拾取；虚拟手柄走正式输入规则，42秒fireSequence163、敌人110→29、掉落16，七种在途视觉。blast主镜头存在画外爆心，只有粒子日志不能代替清晰视觉验收。临时局身份核对后正式放弃，地图句柄null、TimeScale1并退出Play。

no-fire：前轮错误输入观察，射击序号0，不计成功。special-owner-*证明新MCP调用可分派到原程序集，读取初始化状态并清理原事务。

真实手机触摸、双客户端网络与移动端性能未现场验证，未执行Player Build。

actual-detail-and-cleanup：修订工具第二轮至41秒后主动回菜单，fireSequence159/敌人33；三张blast-detail为额外俯视诊断镜头的真实爆心，逐张检查可见。三类Console近2分钟均空。跨调用清理后input=False、run空、地图null，store恢复正式路径并与expected一致，然后退出Play。
