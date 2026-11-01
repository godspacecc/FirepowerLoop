from pathlib import Path
p=Path('Client/Assets/Game/Scripts/Controller/Projectile/ProjectileCtrl.cs');s=p.read_text(encoding='utf-8-sig');old='            return view.Validate(out error);';new='''            // 特殊敌人的依赖随短弹载体预载；缺失时回滚入口，不能到开战后静默丢表现。
            if (prefabAddress == "Prefabs/Combat/GrayboxBullet" &&
                (view.SurvivorHostileFireballTemplate == null || view.SurvivorEnemyBlastTemplate == null))
            {
                error = "投射物准备失败：缺少特殊敌人火球或爆燃模板";
                return false;
            }
            return view.Validate(out error);''';assert old in s;s=s.replace(old,new);p.write_text(s,encoding='utf-8')
