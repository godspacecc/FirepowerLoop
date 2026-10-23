"""给隔离Unity采样加时间说明，输出原速动作候选，不修改正式资源。"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import hashlib
import json
import shutil

root = Path(__file__).resolve().parents[2]
source = root / 'Client/Temp/ZombieWideGrab-v3-20261023'
output = root / 'Docs/Plans/Artifacts/ZombieWideGrab-v3-20261023'
output.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 23)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 18)
report = {'scope': 'Temporary clone sampling, not live combat or multiplayer capture.', 'clips': {}}
for kind, title in [('shambler-claw', '大幅空手抓挠 v3'), ('gaunt-claw', '疾行丧尸大幅抓挠'), ('bloated-claw', '重型丧尸大幅抓挠')]:
    frames = []
    for index, path in enumerate(sorted((source / kind).glob('frame-*.png'))):
        frame = Image.new('RGB', (900, 720), (18, 26, 31))
        frame.paste(Image.open(path).convert('RGB'), (0, 90))
        draw = ImageDraw.Draw(frame)
        draw.text((18, 8), f'{title}：后拉 → 前抓 → 内撕；跑开取消', font=font, fill='white')
        draw.text((18, 47), '左：空手丧尸 / 右：正式主角（持枪） / 隔离候选，未接入 / 20fps原速', font=small, fill=(205, 218, 224))
        t = index * .05
        if t < .6:
            state, color = '第一次：靠近玩家', (210, 225, 235)
        elif t < 1:
            state, color = f'后拉张臂 → 快速前抓 {t - .6:.2f} / 0.40秒', (255, 216, 115)
        elif t < 1.4:
            state, color = '前抓到位 → 向内下撕扯 → 收招', (255, 160, 140)
        elif t < 1.7:
            state, color = '收招，随后重置演示第二种情况', (210, 225, 235)
        elif t < 2.3:
            state, color = '第二次：重新靠近玩家', (210, 225, 235)
        elif t < 2.55:
            state, color = f'准备出手 {t - 2.3:.2f} / 0.40秒', (255, 216, 115)
        elif t < 2.8:
            state, color = '玩家离开范围：收手取消，本次不判伤害', (147, 232, 170)
        else:
            state, color = '取消后恢复追击，再靠近需要重新准备', (147, 232, 170)
        draw.rectangle((0, 666, 900, 720), fill=(18, 26, 31))
        draw.text((18, 678), f'{state}   |   {t:.2f}s', font=small, fill=color)
        frames.append(frame)
    assert len(frames) == 80
    gif = output / f'{kind}-melee-action.gif'
    frames[0].save(gif, save_all=True, append_images=frames[1:], duration=50, loop=0, optimize=False)
    frames[17].save(output / f'{kind}-windup.png')
    frames[22].save(output / f'{kind}-strike.png')
    with Image.open(gif) as image:
        count = image.n_frames
        duration = sum(image.seek(i) or image.info['duration'] for i in range(count))
        assert count == 80 and duration == 4000
    report['clips'][kind] = {'frames': count, 'durationMs': duration, 'sha256': hashlib.sha256(gif.read_bytes()).hexdigest()}
shutil.copyfile(source / 'inspection.txt', output / 'inspection.txt')
(output / 'verification.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
# 同机位、同裁切与同时间刻度对比；只裁背景，不改变角色相对尺寸。
comparison=[]
old=root / 'Client/Temp/ZombieUnarmedAction-v2-20261023/shambler-claw'
for index in range(80):
 canvas=Image.new('RGB',(1440,450),(18,26,31))
 draw=ImageDraw.Draw(canvas)
 draw.text((18,10),'v2：原抓挠幅度',font=font,fill=(200,213,222))
 draw.text((738,10),'v3：大幅后拉、前抓、向内下撕',font=font,fill=(255,216,115))
 for col,folder in enumerate([old,source/'shambler-claw']):
  raw=Image.open(folder/f'frame-{index:03d}.png').convert('RGB').crop((100,100,820,440))
  canvas.paste(raw,(col*720,55))
 draw.text((18,412),f'同机位 / 正式主角同框 / 原尺寸 / 20fps原速 / {index*.05:.2f}s',font=small,fill='white')
 comparison.append(canvas)
comparison[0].save(output/'v2-v3-comparison.gif',save_all=True,append_images=comparison[1:],duration=50,loop=0,optimize=False)
comparison[18].save(output/'v2-v3-windup.png')
with Image.open(output/'v2-v3-comparison.gif') as gif:
 assert gif.n_frames==80
 assert sum(gif.seek(i) or gif.info['duration'] for i in range(gif.n_frames))==4000
(output / 'index.html').write_text('''<!doctype html><meta charset="utf-8"><title>丧尸大幅抓挠 v3</title>
<style>body{margin:0;background:#121a1f;color:#e2e9ed;font:16px system-ui}main{max-width:1500px;margin:auto;padding:24px}h1{font-size:26px}p{line-height:1.7}img{display:block;width:100%;margin:20px auto;border-radius:12px}.single{max-width:900px}button{padding:9px 18px;cursor:pointer}small{color:#bdcbd3}h2{font-size:21px;margin-top:32px}.notice{border-left:4px solid #e9bf66;padding:10px 16px;background:#283035}</style>
<main><h1>丧尸抓挠 v3：明显后拉，再向前抓、向内下撕</h1><p class="notice">本版只扩大空手抓挠：后拉张臂约0.3秒，随后快速前抓，在进入范围后的0.4秒处达到接触姿态，再向内下方撕扯并收手。中途跑开则取消。角色大小、镜头和现有伤害规则均保持。</p>
<button onclick="document.querySelectorAll('img').forEach(i=>{i.src=i.dataset.src+'?t='+Date.now()})">重新播放</button>
<h2>新版完整动作</h2><p>左侧是空手丧尸，右侧持枪者是正式主角。先演示持续贴近，再演示准备时跑开取消。</p>
<img class="single" src="shambler-claw-melee-action.gif" data-src="shambler-claw-melee-action.gif" alt="丧尸大幅抓挠候选v3">
<h2>同机位原速对照：v2 / v3</h2><img src="v2-v3-comparison.gif" data-src="v2-v3-comparison.gif" alt="旧版与大幅新版原速对照">
<details><summary>另外两类丧尸的同幅度动作</summary><img class="single" src="gaunt-claw-melee-action.gif" data-src="gaunt-claw-melee-action.gif" alt="疾行丧尸大幅抓挠"><img class="single" src="bloated-claw-melee-action.gif" data-src="bloated-claw-melee-action.gif" alt="重型丧尸大幅抓挠"></details>
<p><small>正式模型克隆的临时动作候选，尚未正式接入。每段4秒、20fps，骨长和模型缩放保持。此版不表示采用或更改撕咬草案，也不是实战录像。</small></p></main>''',encoding='utf-8')
print(json.dumps(report,ensure_ascii=False))
