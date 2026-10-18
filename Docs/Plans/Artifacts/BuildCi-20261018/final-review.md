# 2026-10-18整批只读复核与修正

范围：7cd034d起模型24a9e03、加载99f68ef及CI2b1e0df／1fa7511／8903e1a与随后节点工具。复核者为独立只读agent review_remaining_delivery，未操作Unity或测试、未修改文件。ed975da音效、2a90c6cf出口以及其他会话工作区修改排除本次责任。

## 重要项：一轮修正完成

1. PC取锁在try内部，失败仍会清理固定PrepPlayer；action异常先释放锁再清理。改为RunExclusive包住RunWorkflowLocked，取锁失败不进入动作，锁覆盖平台切换、异常清理及设置恢复。临时独占文件和并发线程验证，红0/3→绿3/3（同类含链接项）。
2. 只检查Temp目录，漏检锁文件本身链接。改为打开之前检查完整lockPath。当前Unity账户实际不能创建文件symlink；首次权限失败明确保留，随后建立同名NTFS Junction，旧代码抛UnauthorizedAccess而不是按链接策略拒绝，修正后IOException拒绝且目标内容保留。验证的是实际Junction和通用重解析点检查，未声称文件symlink实测。
3. artifact.json原来存绝对路径，下载至其他目录会查旧副本。改为全部元数据与文件路径存受限相对路径，按当前清单根重定位且拒绝越界。旧代码搬迁测试红1项，最终CiBuildAdapterTests16/16；CI作用域PC/Android删除原输出后归档完整及失败恢复3/3。

回归范围上限：BuildCiTests16、HostBaselineArchiveTests11、AndroidHostBuildTests17、BuildWorkflowPolicyTests15，均实际全通过；PC两条完整testMethod各1/1。全部EditMode/includePassingTests=true；无整套、无Player Build。

## 次要项：保留说明

Loading观察工具在恢复正式store后尚未刷新菜单／恢复临时结果模型；若继续停留Play，可能仍显示刚放弃的临时局结果。正式存档未被写入，本次观察已退出Play。按最终审查规则作为minor暂缓，不能把它描述为可持续留在Play的完整观察会话。

## 复核未实测项与裁决

- 真实PC/Android首包、Host、安装器执行、设备和部署：AGENTS禁止Player Build／实际发布，本轮继续不执行，保留未验收，不将纯文件绿测等同构建通过。
- 自然加载耗时：1200ms等待只为截图，不报告为性能测量；实际UI可见与加载完成已有证据。
- 重型自然生成及实体设备：正式Prefab和五动作／俯视图已验，未宣称自然关卡生成实玩通过。
- 音效、出口、其他会话素材／运行代码：按提交及工作区精确排除，避免覆盖其修改。
- Jenkins节点安装、凭据、真实流水线：仅落地未部署模板／调用脚本，节点能力及取消恢复继续待实测。

## 工具与清理边界

另两个会话曾占用Unity测试，本轮被拒绝的请求不计执行；它们空闲后补跑上述实际绿测。一个失败Junction测试的临时目录清理命令被自动审批检查拒绝，工具仅给“blocked by policy”，未重新请求权限或改道删除。残留仅为系统Temp下FirepowerLock-f6730aa12a78462b8409ed1134631142测试夹具，非项目产物；已跳过清理继续完成工作。

提交后按技能尝试清理本轮`.superpowers/sdd/剩余模型加载与构建适配-20261018`只读审查缓存（已校验绝对目标与无链接）也被自动审批以同一“blocked by policy”拒绝。保留该Git忽略缓存，不继续尝试删除；功能验证、提交与推送均已完成，不需要用户审批才能交付。
