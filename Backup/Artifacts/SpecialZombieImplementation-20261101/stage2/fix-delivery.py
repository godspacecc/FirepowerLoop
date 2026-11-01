from pathlib import Path
import difflib,hashlib,json
out=Path('Backup/Artifacts/SpecialZombieImplementation-20261101/stage2');fixbase=out/'fix-baseline'
special='Client/Assets/Game/Scripts/System/Arcade/SurvivorSystem.SpecialEnemies.cs';dest=out/'baseline'/special;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes((fixbase/special).read_bytes())
p=out/'manifest.py';s=p.read_text(encoding='utf-8-sig').replace("'Client/Assets/Tests/EditMode/SurvivorSpecialVisualTests.cs']","'Client/Assets/Tests/EditMode/SurvivorSpecialVisualTests.cs','Client/Assets/Tests/EditMode/SurvivorSpecialFacingTests.cs']");p.write_text(s,encoding='utf-8')
p=out/'report.md';s=p.read_text(encoding='utf-8-sig').replace('本阶段44项路径','本阶段47项路径')
s+='''

## 最终统一修复轮（P2及法术可见样片）

最终审查指出非预警自爆/法术的追击方向被旧state.Facing覆盖。已在ProjectSpecialEnemy最小修正：Shield或Warning保留权威方向；其余目标距离非零时使用当前追击方向，零距离保留原方向。前摇仍锁点且盾牌仍限速，未修改平衡参数、攻击裁决或正式效果资源。

限定新增SurvivorSpecialFacingTests共5项（LateralChaseProjectsCurrentTarget两参数exploder/caster、CasterLocksWindupThenFacesNewTargetDuringCooldown、ShieldProjectionKeepsAuthorityTurnLimit、CoincidentTargetKeepsSafeFacing），EditMode真实短class筛选includePassingTests=true。先复现2通过3失败，修正后5/5通过，原始facing-red.json及facing-green.json；没有全套或无效过滤回退。测试直接消费真实系统投影端口，覆盖横向追击、施法锁向/锁定弹道、发射后冷却换位、盾牌12度/0.1秒及零距离。

原attack-1.gif没有拍出飞行火球，只可见手持球及抬手，不能作为火球可见质量证据。已用RuntimeSampleFireball.cs重新通过干净正常Boot ->真实YooAssetResourceLoader ->真实StartAsync，开局前注入同批runtime-save双账本；正式火球资源及正式运行时代码未为截图调整。唯一采样调整是在每次真实规则/视图投影后，以0.1秒年龄显式Simulate火球ParticleSystem再渲染，维持“真实正式资源/系统接口受控采样”来源，不冒充整局实录。

修正版sample-1/008.png、009.png、010.png清楚可见火球从施法者向主角飞行，009最清楚；011已命中移除。attack-1.gif已由修正版40帧覆盖，字幕仍标记controlled step sample。runtime-fireball.txt和start-fireball.txt记录本次真实loader/Start成功及Stop/End后敌弹、敌爆活动/池全部0；runtime-fireball-error.txt保留第一次紧接测试后的未初始化失败，后一次成功由上述无error文件明确记录，不混算。自爆预警有效帧仍为sample-0/000–009，爆燃010–012（011最清楚），024已经结束。盾牌样片维持原样。

fix.patch为本轮相对fix-baseline增量（最小正式代码、新5项测试及meta、纠正采样工具、报告）；changes.json和review.patch同步加入本阶段后续P2及测试，实际47文件。未改index、未提交、未构建或发布。Unity采样完成后已退出Play。
'''
p.write_text(s,encoding='utf-8')
paths=[special,'Client/Assets/Tests/EditMode/SurvivorSpecialFacingTests.cs','Client/Assets/Tests/EditMode/SurvivorSpecialFacingTests.cs.meta','Backup/Artifacts/SpecialZombieImplementation-20261101/stage2/RuntimeSampleFireball.cs','Backup/Artifacts/SpecialZombieImplementation-20261101/stage2/report.md'];patch=[]
for name in paths:
 p=Path(name);b=fixbase/name;old=b.read_text(encoding='utf-8-sig') if b.exists() else '';new=p.read_text(encoding='utf-8-sig');patch.extend(difflib.unified_diff(old.splitlines(True),new.splitlines(True),fromfile='a/'+name if b.exists() else '/dev/null',tofile='b/'+name))
(out/'fix.patch').write_text(''.join(patch),encoding='utf-8')
