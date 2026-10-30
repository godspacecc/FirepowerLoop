# 幸存者武器槽与常驻无人机

用户确认：3槽，步枪占1槽；首次升级优先无人机，已有只出现升级，满级/满槽过滤。参考Steam Demo历史官方公告，具体5级数值为本项目调参，不宣称原作数值。最终经验条按用户确认贴底且不显示等级/经验文字。

## 交付

- 独立武器等级表 `Config/DataCsv/survivor_weapon_level.csv`：步枪与无人机各5级，伤害/间隔/射程/弹速/寿命。
- `survivor_rule.csv` 的 WeaponSlots；`survivor_upgrade.csv` 的 Weight；Config/gen.bat双端生成成功。
- 同一武器只占一槽，升级不重新获取；无人机常驻本局并优先攻击玩家附近可攻击目标；与步枪共用弹丸预算及击杀经验入口。
- 新增外部武器槽Box及CustomListView，官方生成器生成绑定；原摇杆层级与位置保留。
- UI末版：底边满宽纯经验条，删除旧经验文本/面板，三个武器槽不遮挡。

## 验证

- Unity EditMode限定：SurvivorWeaponTests 7、SurvivorConfigTests 5、SurvivorRuntimeTests原13加首次生成位置回归1、SurvivorRulesTests 13、SurvivorUiTests末版7，共46项通过。每次非空真实过滤，includePassingTests=true，未运行完整套件。
- 独立C#规则检查先0/7（缺失武器槽与加权抽取）后7/7；这是额外规则检查，不混计Unity数量。
- 独立审查发现首次获取无人机从开局位置飞来的P2，新增测试观察失败位置(-20,20)与期望(12.85,-7.65)，修复为首次获取瞬间在当前玩家身旁创建；回归通过，复审无其他明确P1/P2。
- 实际Play：通过菜单进入草地，注入经验以直接验收三选一；点击“获得无人机”后自然持续射击，观察88.127秒、71击杀；注入下一等级经验后卡片成为“提升无人机等级”，实际点击到2级仍占2槽。暂停期间有效时间不变。返回菜单重开为步枪1级、无人机0级、占1槽，见live-check.txt。
- 首轮结算存储重定向到本目录isolated-save。用于快速覆盖UI的经验注入不作为自然升级节奏验证；未实测真机触摸或完整5级长期平衡，不进行Player Build。
- 最终UI测试检查1440/1920/2340宽度下经验条贴底、满宽且与槽位无重叠，验证外部Box和生成引用。bottom-xp-preview.png为已确认的临时预览；最终正式布局与该预览一致。末次补拍时编辑器已不在运行状态，因此没有把该次失败的诊断调用计为实玩通过。

## 素材说明

drone-preview.gif：现有正式主角/无人机原比例，临时地面和弹道的候选演示，非正式实玩录像。
acquire-live.png / upgrade-live.png / drone-live.png：正式资源实际游玩截图（经验条最终调整前）。
WeaponPreview.cs：隔离场景预览制作工具。Tools/Unity/SurvivorWeaponUiAuthoring.cs：先MakeBox及绑定，再MakeHud及绑定，最后MakeBottomExperience及绑定。
