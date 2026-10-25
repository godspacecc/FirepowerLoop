"""编码十关候选演员；只读取Unity临时帧，不修改游戏资源。"""
import argparse
import json
import subprocess
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser()
parser.add_argument('--ffmpeg', type=Path, required=True)
args = parser.parse_args()
assert args.ffmpeg.is_file()
root = Path(__file__).resolve().parents[2]
folder = root / 'Backup/Artifacts/TenStageCreatures-v1-20261025'
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 25)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 18)
kinds = [('Spider', '蜘蛛'), ('Ghoul', '食尸鬼'), ('RedMangler', '红色重甲怪'), ('ZRex', '僵尸暴龙'), ('Papaback', '终局巨猿')]
audit = []
for name, label in kinds:
    source = folder / 'frames' / name
    frames = sorted(source.glob('[0-9][0-9][0-9].png'))
    assert len(frames) == 160
    target = folder / name
    target.mkdir(exist_ok=True)
    for shot in ('static-old.png', 'static-candidate.png'):
        (target / shot).write_bytes((source / shot).read_bytes())
    commands = []
    for speed, fps in [('normal', 20), ('slow', 10)]:
        video = target / f'{speed}.mp4'
        command = [str(args.ffmpeg), '-y', '-hide_banner', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', '1280x720', '-r', str(fps), '-i', '-', '-an', '-c:v', 'libx264', '-preset', 'fast', '-crf', '20', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(video)]
        process = subprocess.Popen(command, stdin=subprocess.PIPE)
        for i, file in enumerate(frames):
            with Image.open(file) as original:
                frame = original.convert('RGB')
            draw = ImageDraw.Draw(frame)
            draw.rectangle((0, 0, 1280, 74), fill='#18282d')
            draw.text((640, 8), f'{label} · 临时外形与动作候选 v1', font=font, fill='white', anchor='mt')
            draw.text((640, 43), '正式主角原尺寸同框 · ' + ('原速' if speed == 'normal' else '0.5倍慢放') + ' · 尚未采用或正式接入', font=small, fill='#a8d8cf', anchor='mt')
            draw.rectangle((0, 670, 1280, 720), fill='#18282d')
            draw.text((640, 684), '隔离手动动作采样；没有实际AI、伤害或技能特效结算', font=small, fill='#c7d9d5', anchor='mt')
            if speed == 'normal' and i == 100:
                frame.save(target / 'poster.png')
            process.stdin.write(frame.tobytes())
        process.stdin.close()
        assert process.wait() == 0
        subprocess.run([str(args.ffmpeg), '-hide_banner', '-loglevel', 'error', '-i', str(video), '-f', 'null', '-'], check=True)
        audit.append({'kind': name, 'video': f'{name}/{speed}.mp4', 'frames': 160, 'fps': fps, 'seconds': 160 / fps, 'full_decode': 'passed'})
(folder / 'video-audit.json').write_text(json.dumps(audit, ensure_ascii=False, indent=2), encoding='utf-8')
html = ['<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>十关特殊怪与Boss候选v1</title><style>body{margin:24px auto;padding:0 20px;max-width:1280px;background:#18282d;color:#edf0e8;font:16px system-ui}p{line-height:1.7;color:#b7ceca}a{color:#9ce3ce}.pair{display:grid;grid-template-columns:1fr 1fr;gap:12px}img,video{width:100%}figure{margin:0}section{margin:48px 0}figcaption{padding:10px}nav{display:flex;gap:16px;position:sticky;top:0;padding:16px;background:#18282d;z-index:2}</style><h1>十关特殊怪与Boss候选 v1</h1><p>三种普通怪与两种Boss的新外形、关节动作候选。均为Unity隔离手动时间轴，正式主角原尺寸同框；保留现有能力方向，不代表已接入实际AI或判伤。当前新外观均未采用、未正式接入。近战部位接近主角的距离见sizes-and-contact-audit.tsv，不能把包围盒接近当作真实命中验证。</p><nav>' + ''.join(f'<a href="#{n}">{t}</a>' for n, t in kinds) + '</nav>']
for name, label in kinds:
    html.append(f'<section id="{name}"><h2>{label}</h2><div class="pair"><figure><img src="{name}/static-old.png"><figcaption>当前正式轮廓</figcaption></figure><figure><img src="{name}/static-candidate.png"><figcaption>临时候选 · 相同主角尺寸</figcaption></figure></div><p>原速</p><video controls muted loop playsinline preload="metadata" poster="{name}/poster.png" src="{name}/normal.mp4"></video><details><summary>0.5倍慢放</summary><video controls muted loop playsinline preload="none" src="{name}/slow.mp4"></video></details></section>')
(folder / 'index.html').write_text(''.join(html) + '</html>', encoding='utf-8')
print('10 videos fully decoded; 5 candidate galleries ready.')
