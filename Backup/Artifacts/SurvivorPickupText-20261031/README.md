# 拾取强化卡实际半径
- 用户确认文案：拾取半径：2 米 → 2.25 米；本次增加 0.25 米。沿用现有卡片布局。
- System提供实际基础半径+累计加成，Controller仅对拾取卡渲染实际半径、下一层半径和本次增量。
- Config/Localization/survivor.json中英文权威源增加pickup_detail，Config/gen.bat已成功发布两语言。
- 真实升级状态限定测试覆盖0至11层、累计12次选择到5米；RED缺方法失败，GREEN通过。
- 正式卡片Prefab与实际发布JSON限定测试覆盖中文/英文、首次/末次文案的区域容纳检查，通过。两个完整testMethod各最多1项，includePassingTests=true。
- 独立审查无阻断。未改Prefab、布局、资源地址或玩法参数，未Player Build、未整局人工试玩。
- 编辑态文字测量直接读取已发布JSON，不依赖未初始化的YooAsset包；不据此宣称发布包加载已验。另一任务新测试缺少FirepowerLoop.Arcade引用已补充，不混入本次提交。
