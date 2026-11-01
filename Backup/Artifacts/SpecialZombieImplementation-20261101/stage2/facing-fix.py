from pathlib import Path
p=Path('Client/Assets/Game/Scripts/System/Arcade/SurvivorSystem.SpecialEnemies.cs');s=p.read_text(encoding='utf-8-sig');old='''            var state=SpecialState(enemy);
            view.SetSurvivorEnemySpecial(enemy.Id,shape.SpecialKind,state.Facing,state.Warning,state.Warning?Mathf.Clamp01(1-state.Remaining/shape.WindupSeconds):0,state.Target,shape.BlastRadius);''';new='''            var state=SpecialState(enemy);
            // 盾牌及前摇保留权威锁定方向；追击和施法冷却则朝当前目标，避免旧方向造成横向滑行。
            var direction=state.Facing;
            if(shape.SpecialKind!="Shield"&&!state.Warning&&(player-enemy.Position).sqrMagnitude>.000001f)
                direction=(player-enemy.Position).normalized;
            view.SetSurvivorEnemySpecial(enemy.Id,shape.SpecialKind,direction,state.Warning,state.Warning?Mathf.Clamp01(1-state.Remaining/shape.WindupSeconds):0,state.Target,shape.BlastRadius);''';assert old in s;s=s.replace(old,new);p.write_text(s,encoding='utf-8')
