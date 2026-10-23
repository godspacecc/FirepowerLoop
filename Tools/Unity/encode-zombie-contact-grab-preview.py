"""从距离门禁与正式前摇函数的采样记录生成贴身候选，不改正式配置或资源。"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import csv
import hashlib
import json
import shutil

root = Path(__file__).resolve().parents[2]
source = root / 'Client/Temp/ZombieContactGrab-v4-20261023'
output = root / 'Docs/Plans/Artifacts/ZombieContactGrab-v4-20261023'
output.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 23)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 18)
rows = list(csv.DictReader((source / 'timeline.csv').open(encoding='utf-8-sig')))
report = {'scope': 'Temporary range candidate 0.9m plus real windup helper; not full live combat.', 'clips': {}}
hit_frames = []
for kind, (name, title) in enumerate([('shambler-claw', '普通丧尸'), ('gaunt-claw', '疾行丧尸'), ('bloated-claw', '重型丧尸')]):
    timeline = [r for r in rows if int(r['kind']) == kind]
    assert len(timeline) == 100
    hits = [r for r in timeline if r['hit'] == 'True']
    cancels = [r for r in timeline if r['cancel'] == 'True']
    assert len(hits) == len(cancels) == 1
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
        draw.text((18, 8), f'{title} v4：够得到才起手，0.4秒后手爪触及玩家', font=font, fill='white')
        draw.text((18, 47), '左：空手丧尸 / 右：正式主角 / 贴身距离候选，未正式接入 / 20fps', font=small, fill=(205, 218, 224))
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
        draw.text((18, 707), f'中心距离 {float(row["distance"]):.2f}米 / 候选起手距离0.90米 / {t:.2f}s', font=small, fill='white')
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
        'hit': hit_at, 'windupSeconds': .4, 'outsideStarts': 0, 'cancels': 1,
        'handToBodyMetres': float(hits[0]['handToBody']), 'sha256': hashlib.sha256(gif.read_bytes()).hexdigest()}
compare = Image.new('RGB', (1440, 450), (18, 26, 31))
draw = ImageDraw.Draw(compare)
draw.text((18, 10), 'v3：相隔1.50米，手爪够不到', font=font, fill=(210, 218, 224))
draw.text((738, 10), 'v4：靠近到0.90米，手爪接触身体', font=font, fill=(255, 216, 115))
for col, path in enumerate([root / 'Client/Temp/ZombieWideGrab-v3-20261023/shambler-claw/frame-020.png', source / 'shambler-claw' / f'frame-{hit_frames[0]:03d}.png']):
    compare.paste(Image.open(path).convert('RGB').crop((100, 100, 820, 440)), (col * 720, 55))
draw.text((18, 412), '同相机、原尺寸 / 比较各自挥出时刻 / v4为距离候选，尚未接入正式战斗', font=small, fill='white')
compare.save(output / 'contact-comparison.png')
for name in ['timeline.csv', 'inspection.txt']:
    shutil.copyfile(source / name, output / name)
(output / 'verification.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
(output / 'index.html').write_text('''<!doctype html><meta charset="utf-8"><title>丧尸贴身抓挠 v4</title>
<style>body{margin:0;background:#121a1f;color:#e2e9ed;font:16px system-ui}main{max-width:1500px;margin:auto;padding:24px}h1{font-size:26px}p{line-height:1.7}img{display:block;width:100%;margin:20px auto;border-radius:12px}.single{max-width:900px}button{padding:9px 18px;cursor:pointer}small{color:#bdcbd3}h2{font-size:21px;margin-top:32px}.notice{border-left:4px solid #e9bf66;padding:10px 16px;background:#283035}</style>
<main><h1>已保留大幅动作；这次先贴近，挥出时碰到玩家</h1><p class="notice">旧预览在中心距离1.5米时起手，画面里手爪够不到。此版候选缩到0.9米才允许起手：先追近，再开始完整0.4秒前摇，挥出触及身体；玩家提前跑开则取消。大幅后拉、前抓、内撕方向保持。</p>
<button onclick="document.querySelectorAll('img').forEach(i=>{i.src=i.dataset.src+'?t='+Date.now()})">重新播放</button>
<h2>贴近、接触、跑开取消</h2><img class="single" src="shambler-claw.gif" data-src="shambler-claw.gif" alt="丧尸靠近后抓到玩家的动态候选">
<h2>挥出时刻的接触对比</h2><img src="contact-comparison.png" data-src="contact-comparison.png" alt="旧版隔空与新版接触对比">
<details><summary>另外两类丧尸的接触表现</summary><img class="single" src="gaunt-claw.gif" data-src="gaunt-claw.gif" alt="疾行丧尸贴身抓挠"><img class="single" src="bloated-claw.gif" data-src="bloated-claw.gif" alt="重型丧尸贴身抓挠"></details>
<p><small>本页是正式模型克隆的距离候选。前摇调用正式规则函数，但0.9米门禁及停步是临时预览输入，尚未修改正式攻击距离或接入正式动画，不是完整实战录像。每段5秒、20fps，角色保持原尺寸。三种丧尸接触时手指到主角网格最近距离约0.2～1.8厘米；范围外起手0次，第二段各取消1次。</small></p></main>''', encoding='utf-8')
print(json.dumps(report, ensure_ascii=False))
