"""把正式配置、真实TickEnemies和正式EnemyView采样录像编码为可播放验收。"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import csv
import hashlib
import json
import shutil

root = Path(__file__).resolve().parents[2]
source = root / 'Client/Temp/ZombieContactGrabApplied-20261024'
output = root / 'Backup/Artifacts/ZombieContactGrabApplied-20261024'
output.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 23)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 18)
rows = list(csv.DictReader((source / 'timeline.csv').open(encoding='utf-8-sig')))
report = {'scope': 'Formal spawned enemies with real TickEnemies and EnemyView.SetMeleeAttack/LateTick; isolated render harness, not complete live battle or two-device capture.', 'clips': {}}
hit_frames = []
for kind, (name, title) in enumerate([('shambler-claw', '普通丧尸'), ('gaunt-claw', '疾行丧尸'), ('bloated-claw', '重型丧尸')]):
    timeline = [r for r in rows if int(r['kind']) == kind]
    assert len(timeline) == 100
    hits = [r for r in timeline if r['hit'] == 'True']
    cancels = [r for r in timeline if r['cancel'] == 'True']
    assert len(hits) == len(cancels) == 1
    assert int(hits[0]['health']) == 90
    start = next(r for r in timeline if r['windup'] == 'True')
    assert abs(float(hits[0]['time']) - float(start['time']) - .4) < .001
    assert 0 <= float(hits[0]['handToBody']) <= .025
    assert all(r['windup'] == 'False' for r in timeline if r['inRange'] == 'False')
    frames = []
    hit_at = float(hits[0]['time'])
    cancel_at = float(cancels[0]['time'])
    for index, row in enumerate(timeline):
        t = float(row['time'])
        frame = Image.new('RGB', (900, 740), (18, 26, 31))
        frame.paste(Image.open(source / name / f'frame-{index:03d}.png').convert('RGB'), (0, 90))
        draw = ImageDraw.Draw(frame)
        draw.text((18, 8), f'{title} 正式接入：贴身等待0.4秒，抓到身体再掉血', font=font, fill='white')
        draw.text((18, 47), '正式模型原尺寸 / 真实敌人规则与正式动作入口 / 隔离验证录像 / 20fps', font=small, fill=(205, 218, 224))
        if row['hit'] == 'True':
            state, color = f'挥出接触：手指距主角身体约{float(row["handToBody"]) * 100:.1f}厘米', (255, 160, 140)
        elif cancel_at <= t < cancel_at + .25:
            state, color = '玩家跑出范围：取消前摇，收手再追击', (147, 232, 170)
        elif row['windup'] == 'True':
            state, color = f'已贴近，准备出手：{.4-float(row["remaining"]):.2f} / 0.40秒', (255, 216, 115)
        elif hit_at < t < 2.5:
            state, color = '接触后向内下撕，随后收招；第二次演示跑开取消', (210, 225, 235)
        else:
            state, color = '还没够到：继续追近，不开始攻击', (210, 225, 235)
        draw.rectangle((0, 665, 900, 740), fill=(18, 26, 31))
        draw.text((18, 674), state, font=small, fill=color)
        draw.text((18, 707), f'中心距离 {float(row["distance"]):.2f}米 / 正式距离0.90米 / 生命{row['health']} / {t:.2f}s', font=small, fill='white')
        frames.append(frame)
    gif = output / f'{name}.gif'
    frames[0].save(gif, save_all=True, append_images=frames[1:], duration=50, loop=0, optimize=False)
    hit_index = int(hits[0]['frame'])
    frames[hit_index].save(output / f'{name}-contact.png')
    hit_frames.append(hit_index)
    with Image.open(gif) as image:
        count = image.n_frames
        duration = sum(image.seek(i) or image.info['duration'] for i in range(count))
        assert count == 100 and duration == 5000
    report['clips'][name] = {'frames': count, 'durationMs': duration, 'start': float(start['time']),
        'hit': hit_at, 'windupSeconds': .4, 'outsideStarts': 0, 'cancels': 1, 'healthBefore': 100, 'healthAfter': int(hits[0]['health']),
        'handToBodyMetres': float(hits[0]['handToBody']), 'sha256': hashlib.sha256(gif.read_bytes()).hexdigest()}
for name in ['timeline.csv', 'inspection.txt']:
    shutil.copyfile(source / name, output / name)
(output / 'verification.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
(output / 'index.html').write_text('''<!doctype html><meta charset="utf-8"><title>丧尸抓挠正式接入验证</title>
<style>body{margin:0;background:#121a1f;color:#e2e9ed;font:16px system-ui}main{max-width:1000px;margin:auto;padding:24px}h1{font-size:26px}p{line-height:1.7}img{display:block;width:100%;margin:20px auto;border-radius:12px}button{padding:9px 18px;cursor:pointer}small{color:#bdcbd3}</style>
<main><h1>正式接入：贴近后抓挠，命中再掉血</h1><p>三类丧尸都在中心距离0.9米停步，等待完整0.4秒再前抓；玩家提前跑开会取消。大幅后拉、前抓、内下撕沿用已确认版本。</p>
<button onclick="document.querySelectorAll('img').forEach(i=>{i.src=i.dataset.src+'?t='+Date.now()})">重新播放</button>
<h2>普通丧尸</h2><img src="shambler-claw.gif" data-src="shambler-claw.gif">
<details><summary>疾行与重型丧尸</summary><img src="gaunt-claw.gif" data-src="gaunt-claw.gif"><img src="bloated-claw.gif" data-src="bloated-claw.gif"></details>
<p><small>正式配置出生，真实TickEnemies推进位置、起手、取消与生命100→90，正式EnemyView叠加骨骼动作。原模型与主角同框、保持原尺寸。每段5秒/20fps，后半段演示跑开取消；为观察一次完整交互，首击收招结束后保持画面。属于隔离场景规则与表现验证，不是完整闯关或双机联网录屏。</small></p></main>''', encoding='utf-8')
print(json.dumps(report, ensure_ascii=False))
