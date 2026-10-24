# 其他七种手持枪械基础伤害各加18

2026-10-23用户在基础枪18→36已推送8d656d6f后明确“那其他的枪伤害也上调18吧”。本次修改Config/DataCsv/arcade_weapon.csv七个Damage字段：速射10→28、散弹每颗11→29、喷火每股5→23、穿透17→35、火箭38→56、榴弹30→48、镭射28→46。基础枪36、所有射速与颗数、红紫倍率及进化机制、其余武器/无人机参数均保持。无手写代码、协议、视觉资源或.meta修改。

## 验证

- Unity MCP确认项目E:/GitHub/FirepowerLoop/Client，Boot干净、未播放未编译，开始时Console错误0。
- 通过唯一入口Config/gen.bat完成双端生成，退出0；共享工作区其它配置不纳入提交。
- 双端tbarcadeweapon.bytes一致，均1272字节；相对任务开始前只有七处字节变化，每处恰好+18，见binary-diff.json。
- Unity MCP使用正式TbArcadeWeapon和Luban.ByteBuf解码双端bytes，15行Damage和射击间隔与权威CSV一致；明确核对基础枪36、间隔0.24，及其它七枪28/29/23/35/56/48/46，见dual-target-decode.json。
- 独立只读审查无发现：恰好七个Damage字段，全部其它字段及行保持；两端消费者直接读取武器配置，无需改代码。
- 本次为现有数值调整，以生成校验、正式解码和差异检查验证，未新增或运行测试，未Player Build，未进行真实双客户端联机验收。已运行服务器需重启读取新bytes，本次不声称服务器在线实例已重载。
