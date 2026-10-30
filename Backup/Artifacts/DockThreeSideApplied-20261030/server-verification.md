# 三方向海路服务端验证

本次仅验证码头三方向海路接入；未启动监听、未读取玩家存档、未操作Unity、未触发Player Build。

- RED：`server-red.log`，旧实现8/9通过，`OtherPassagesAlsoClimb`因真实左／上出生没有海路状态失败。
- GREEN：`server-sea-green.log`，DockSeaEntryOnline 9/9通过。三方向真实出生、四轮三车道、活动期禁止攻击、眩晕／牛撞／风暴／断线／加载／过渡冻结、完成帧与恢复追击、保护和推挤、背桶、方向与年龄／车道深复制及MemoryPack往返、真实出生与解码状态合法性。
- GREEN：`server-passage-green.log`，DockPassageEntryOnline 9/9通过。旧短类名保留以兼容脚本；全部旧步行预期替换为三路海中攀爬，覆盖三路四轮保护、普通／强化弹丸穿过、背桶护甲、5.75秒可受击与6.75秒追击边界、远程攻击恢复、冻结、推挤、炸弹。
- 协议：仅执行`Tools/gen-protocol.ps1`生成双端，日志`server-protocol-generation.log`。方向追加为35号字段，原字段顺序不变；服务端快照构建、深复制和客户端映射已接入。
- 源码限定`git diff --check`通过。客户端运行验证和正式视觉验证由主任务另行记录。

执行命令（严格限定真实短类名、最大数量、显示成功项，不回退其他套件）：

```powershell
dotnet run --project Tools/DockSeaEntryOnlineChecks -- testClass=DockSeaEntryOnline maxTests=9 includePassingTests=true
dotnet run --project Tools/DockPassageEntryOnlineChecks -- testClass=DockPassageEntryOnline maxTests=9 includePassingTests=true
```
