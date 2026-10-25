"""将已采用v2与正式Animator回放同框合成，并核对全部帧及本项允许的正式资源差异。"""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser()
parser.add_argument('--ffmpeg', required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
batch = root / 'Backup/Artifacts/ZombieGroundWalkApplied-20261025'
accepted = root / 'Backup/Artifacts/ZombieGroundWalk-v2-20261025'
ffmpeg = Path(args.ffmpeg)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 25)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 19)
large = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 28)
video = batch / 'formal-ground-walk.mp4'
command = [str(ffmpeg), '-y', '-hide_banner', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24',
    '-s', '1280x720', '-r', '20', '-i', 'pipe:0', '-an', '-c:v', 'libx264', '-preset', 'medium',
    '-crf', '18', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(video)]
process = subprocess.Popen(command, stdin=subprocess.PIPE)
comparisons = []
for group, kind in enumerate(['Shambler', 'Gaunt', 'Bloated']):
    max_mean, max_pixels, max_channel = 0, 0, 0
    for i in range(120):
        paths = [accepted / 'frames' / f'{group}-after-{i:03d}.png', batch / 'frames' / f'{group}-after-{i:03d}.png']
        images = [Image.open(p).convert('RGB') for p in paths]
        difference = np.abs(np.asarray(images[0], dtype=np.int16) - np.asarray(images[1], dtype=np.int16))
        max_mean = max(max_mean, float(difference.mean()))
        max_pixels = max(max_pixels, int(np.any(difference != 0, axis=2).sum()))
        max_channel = max(max_channel, int(difference.max()))
        assert float(difference.mean()) < .08, (kind, i, float(difference.mean()))
        frame = Image.new('RGB', (1280, 720), '#15232a')
        for col, image in enumerate(images):
            frame.paste(image, (col * 640, 86))
        d = ImageDraw.Draw(frame)
        name = ['普通丧尸', '瘦弱丧尸', '重型丧尸'][group]
        d.text((640, 5), f'地面步态正式接入核对 · {name}' + (' · ½速低机位' if i >= 60 else ' · 常用俯视角'), font=large, fill='white', anchor='mt')
        d.text((320, 46), '已采用v2候选', font=font, fill='#d4dedf', anchor='mt')
        d.text((960, 46), '正式Animator / Run状态回放', font=font, fill='#9fe1bb', anchor='mt')
        d.line((640, 86, 640, 686), fill='#15232a', width=4)
        d.text((640, 690), '原尺寸正式主角同框 / 同机位同时间 / 正式资源隔离回放，非自然整局录像', font=small, fill='#bfd2d8', anchor='mt')
        process.stdin.write(frame.tobytes())
        if i == 29:
            frame.save(batch / f'{group}-formal-comparison.png')
    comparisons.append({'kind': kind, 'frames': 120, 'max_mean_channel_difference': max_mean,
        'max_different_pixels': max_pixels, 'max_channel_difference': max_channel})
process.stdin.close()
assert process.wait() == 0
subprocess.run([str(ffmpeg), '-hide_banner', '-loglevel', 'error', '-i', str(video), '-f', 'null', '-'], check=True)
hashes = json.loads((accepted / 'formal-source-hashes.json').read_text(encoding='utf-8-sig'))
changed = [str(Path(p).relative_to(root)) for p, expected in hashes.items() if hashlib.sha256(Path(p).read_bytes()).hexdigest().upper() != expected]
allowed = {f'Client/Assets/Game/Art/Enemies/Zombies/ArcadeZombie{k}.controller' for k in ['Shambler', 'Gaunt', 'Bloated']}
assert {p.replace('\\', '/') for p in changed} == allowed, changed
result = {'scope': 'formal Animator Run sampled at adopted phase in isolated cloned scene',
    'frames': 360, 'fps': 20, 'seconds': 18, 'full_decode': 'passed', 'image_comparisons': comparisons,
    'existing_formal_files_checked': len(hashes), 'expected_controller_changes': changed,
    'other_existing_formal_changes': [], 'runtime_ik': False}
(batch / 'formal-capture-audit.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
(batch / 'index.html').write_text('''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>丧尸地面步态正式接入</title><style>body{background:#15232a;color:#d8e7e5;font:17px system-ui;max-width:1280px;margin:24px auto;padding:12px}video{width:100%}</style><h1>地面步态v2正式接入核对</h1><p>左侧为已采用候选，右侧为正式Animator的Run状态，三种体型同机位同时间、原尺寸正式主角同框。隔离资源回放，非自然整局或真人双端录像。</p><video controls autoplay muted loop playsinline src="formal-ground-walk.mp4" poster="0-formal-comparison.png"></video></html>''', encoding='utf-8')
print(json.dumps(result, ensure_ascii=False))
