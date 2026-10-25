# 十关新局路线与已采用内容收尾

用户于2026-10-25明确“先凑齐10关，其他关卡先不放”，并选定后四关：巨石环阵、要塞庭院、锦鲤桥庭、终局巢穴，火山继续暂缓。

新局固定顺序：**海岛1 → 码头21 → 雪地农场22 → 墓园23 → 神庙庭院24 → 集市25 → 巨石环阵5 → 要塞庭院7 → 锦鲤桥庭10 → 终局巢穴20**。版本2，十场四十普通轮，主题Boss与终局沿原规则，不把Boss战额外算普通轮。其余关卡不再由新局路线进入，原资源与配置保留；v0旧十场、v1旧十三场仍可继续原局。

本批完成的是固定路线、旧局兼容、已采用巨石场景与两种已采用怪物的正式接入收尾。**十关精修总目标仍未完成**：要塞、锦鲤、终局的新场景和对应五种角色正在提供候选，具体视觉未采用，未正式替换。

## 实现与验证

- Config权威源只调整5→7、7→10、10→20三条后继及指纹；巨石新增环境目录5。由Config/gen.bat生成双端36/30表，三张变化bytes逐字一致。没有手改生成输出。
- ArcadeRouteRules保存三版本身份，旧0在农场转5、旧0/1保留5→6、7→9/10、10→11三条旧边。后段Slayer挑战仍接受已发布十三场旧局；新存档/胜利/轮前事务沿既有入口，未知版本和未知指纹仍拒绝。
- 巨石正式环境目录与业务查询分开：5读取采用版四轮静态场景，但原出生点、移动、传送门和旧顺序模式保持。详见[巨石正式回读](../HengePolishApplied-20261025/README.md)。
- 潜地怪和恶犬由原会话获用户两次明确采用/正式接入授权，本地实现后交总会话复核；原P2/P3已关闭，核心文件与交接manifest一致。详见[正式演员回放](../HengeVolcanoPolish-v1-20261025/Creatures/Applied/README.md)及[复核](creature-prior-findings-review.md)。

本批由总会话新执行**41个不同客户端用例、22个服务端用例**，全部限定范围、includePassingTests=true：

|范围|通过/上限|证据|
|---|---|---|
|TenStageRouteTests|11/12|[GREEN](client-green.json)、[RED](client-red.json)|
|HengeAppliedTests|8/8|[GREEN](../HengePolishApplied-20261025/green.json)|
|CreaturePresentationTests|14/14|[怪物相邻检查](creature-regression.json)|
|ArcadeRoundContentViewTests|7/7|[场景加载/释放](view-regression.json)|
|ArcadeEarlyStageRouteTests|1/1|[旧十三场实际文件恢复](legacy-route-regression.json)|
|TenStageRouteOnline|8/8|[服务端新十关真实流程](server-green.log)|
|NextRouteOnline|10/10|[旧路线相邻检查](server-legacy-regression.log)|
|CreatureOnline|4/4|[快照/Codec/地下免伤](server-creature-regression.log)|

客户端真实RED为5/11通过，新局版本仍1、后继仍含旧关、v2保存/结算被拒绝；服务端RED为4/8通过，其中Victory用例被工具未注册Logger的空引用遮挡，不把该夹具问题当业务RED，已修夹具后获得真实8/8。无完整套件或过滤回退。

新十关检查从实际出生/四轮推进、金币箱、出口、轮前文件保存恢复、关底与终局Boss、结果保存入口验证，不以遍历表或直接写胜利替代；这仍是隔离入口检查，不是完整自然实玩或真人双客户端录像。

[资源审计](resource-audit.json)回查预览前3230个正式文件，变化仅演员聚合模板及本次三张配置bytes，原meta均保持；新的巨石/演员资源另有各自manifest。独立只读审查路线与跨任务边界无Critical/Important；巨石烘焙先覆盖再验证的P2已改为四轮内存验证后再持久化并复审关闭。未Player Build、未资源打包，未宣称真机或真人双端整局验收。

## 尚待具体视觉采用

- [要塞、锦鲤、终局场景四轮对照](../TenStageEnvironments-v1-20261025/index.html)：静态候选，正式主角同框，30张图完整解码；足迹/外景保护检查通过。
- [三种特殊怪与两个Boss动态](../TenStageCreatures-v1-20261025/index.html)：五种各原速8秒/慢放16秒，临时模型与原尺寸正式主角同框；10条视频完整解码。没有实际AI或伤害结算，未正式接入。

上述待采用范围不能随本批提交被视为正式视觉已获授权；火山暂停要求继续有效。
