# 两项目共同网络与账号基础验收

基础版3DBase已推送dfce2649；本目录为FirepowerLoop目标接入证据。范围是TCP/RPC、账号注册登录、Mongo身份、持久会话及本项目schema4离线迁移；目标玩法与局保存规则保留。

- Unity EditMode：传输6、分帧4、登录及预制体确认流程46、心跳16、移动布局3，共75项、0跳过。UI按钮为自动调用，网络替身与真实Socket测试分别记录，不代表人工设备体验。
- 独立TCP/RPC：12项；使用目标正式传输/协议源码与真实回环Socket，保留离线包禁止联网策略。
- 最终独立服务验收：AccountFoundation-20261030-125141-8900eb1b，Mongo25项、真实ET账号链路8项及真实服务重启2项。ET子进程和随机数据库正常清理。诊断HTTP缺URLACL权限，未记作通过。
- 迁移CLI：构造schema4数据，dry-run、apply、重复apply以及源文件SHA256保持通过。无真实玩家数据迁移。
- 独立审核：全新上下文firepowerloop_account_review；首轮R1指出跨账号PlayerId冲突，增加Mongo非零PlayerId唯一索引及Actor归属检查。第二轮R1关闭、0必修；第三轮仅文档核对。最终62文件清单account-review-hashes-final.json，源码相对R1复核未变。

保留失败记录：并行玩法编译阻塞、测试请求超时/占用、旧首页尺寸断言、首次Mongo序列化兼容问题及服务启动时序超时；修复后对应链路已重跑。没有Player Build、人工真机或完整双人玩法验收，没有真实PR合并/发布。

Mongo进程已通过数据库正常关闭命令退出。临时URI文件删除被自动审批拒绝，仍在本地忽略目录，未纳入提交；具体状态见cleanup-final.txt。数据库目录和复制的服务运行目录不提交。
