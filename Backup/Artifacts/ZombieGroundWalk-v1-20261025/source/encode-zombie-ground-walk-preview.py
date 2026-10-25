"""合成原尺寸丧尸步态候选，验证临时落脚记录和正式资源哈希。"""
import argparse
import csv
import hashlib
import json
import subprocess
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser()
parser.add_argument('--ffmpeg', required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
batch = root / 'Backup/Artifacts/ZombieGroundWalk-v1-20261025'
frames = batch / 'frames'
ffmpeg = Path(args.ffmpeg)
assert ffmpeg.is_file()
hashes = json.loads((batch / 'formal-source-hashes.json').read_text(encoding='utf-8-sig'))
changed = [p for p, expected in hashes.items() if hashlib.sha256(Path(p).read_bytes()).hexdigest().upper() != expected]
assert not changed, changed
motion = list(csv.DictReader((frames / 'motion.tsv').read_text().splitlines(), delimiter='|'))
assert len(motion) == 720, len(motion)
stats = {}
for kind in ['Shambler', 'Gaunt', 'Bloated']:
    current = [r for r in motion if r['kind'] == kind and r['candidate'] == 'False'][:60]
    candidate = [r for r in motion if r['kind'] == kind and r['candidate'] == 'True'][:60]
    maximum_error = max(float(r[k]) for r in candidate for k in ['leftError', 'rightError'])
    assert maximum_error < .003, (kind, maximum_error)
    result = {'max_target_error_m': maximum_error, 'feet': {}}
    for side in ['left', 'right']:
        data = [float(r[side + 'Y']) for r in candidate]
        base = [float(r[side + 'Y']) for r in current]
        grounded = [r for r in candidate if float(r[side + 'Phase']) < .62]
        slip = []
        for a, b in zip(grounded, grounded[1:]):
            if 0 < float(b['time']) - float(a['time']) < .051 and float(b[side + 'Phase']) > float(a[side + 'Phase']):
                slip.append(abs(float(b[side + 'X']) - float(a[side + 'X'])))
        knee = [float(r[side + 'Knee']) for r in candidate]
        assert max(data) - min(data) > .11, (kind, side, data)
        assert max(slip) < .003, (kind, side, max(slip))
        assert max(knee) - min(knee) > 20, (kind, side, knee)
        result['feet'][side] = {'current_vertical_range_m': round(max(base) - min(base), 4),
            'candidate_vertical_range_m': round(max(data) - min(data), 4),
            'max_stance_frame_drift_m': round(max(slip), 5), 'knee_bend_degrees': [min(knee), max(knee)]}
    stats[kind] = result

font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 25)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 19)
title = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 28)
names = ['普通丧尸', '瘦弱丧尸', '重型丧尸']
video = batch / 'zombie-ground-walk-comparison.mp4'
command = [str(ffmpeg), '-y', '-hide_banner', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24',
    '-s', '1280x720', '-r', '20', '-i', 'pipe:0', '-an', '-c:v', 'libx264', '-preset', 'medium',
    '-crf', '18', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(video)]
process = subprocess.Popen(command, stdin=subprocess.PIPE)
shots = []
for group in range(3):
    for i in range(120):
        frame = Image.new('RGB', (1280, 720), '#15232a')
        for col, side in enumerate(['before', 'after']):
            source = frames / f'{group}-{side}-{i:03d}.png'
            with Image.open(source) as shot:
                assert shot.size == (640, 600)
                frame.paste(shot, (col * 640, 86))
        d = ImageDraw.Draw(frame)
        d.text((640, 5), f'地面行走改良候选 · {names[group]}' + (' · ½速低机位' if i >= 60 else ' · 常用俯视角'), font=title, fill='white', anchor='mt')
        d.text((320, 46), '现版正式移动片段', font=font, fill='#d3ddde', anchor='mt')
        d.text((960, 46), '候选：交替迈步、屈膝抬脚、落脚停稳', font=font, fill='#9fe1bb', anchor='mt')
        d.line((640, 86, 640, 686), fill='#15232a', width=4)
        d.text((640, 690), '正式主角同框 / 原尺寸 / 相同演示移速0.9米每秒 / 临时骨架候选，尚未接入游戏', font=small, fill='#bfd2d8', anchor='mt')
        process.stdin.write(frame.tobytes())
        if i in [19, 29, 39, 49, 79, 89, 99, 109]:
            if i == 29:
                frame.save(batch / f'{group}-comparison.png')
                shots.append(frame.copy())
            if group == 0:
                frame.save(batch / f'phase-{i:03d}.png')
process.stdin.close()
assert process.wait() == 0, '视频编码失败'
subprocess.run([str(ffmpeg), '-hide_banner', '-loglevel', 'error', '-i', str(video), '-f', 'null', '-'], check=True)
metadata = subprocess.run([str(ffmpeg), '-hide_banner', '-i', str(video), '-map', '0:v:0', '-c', 'copy', '-f', 'null', '-'], capture_output=True, text=True)
assert '1280x720' in metadata.stderr and '20 fps' in metadata.stderr and 'frame=  360' in metadata.stderr, metadata.stderr
sheet = Image.new('RGB', (1280, 2160), '#15232a')
for i, shot in enumerate(shots):
    sheet.paste(shot, (0, i * 720))
sheet.save(batch / 'three-types-comparison.png')
subprocess.run([str(ffmpeg), '-y', '-hide_banner', '-loglevel', 'error', '-i', str(video), '-t', '6',
    '-filter_complex', 'fps=12,scale=960:-1,split[a][b];[a]palettegen[p];[b][p]paletteuse', str(batch / 'shambler-comparison.gif')], check=True)
audit = {'scope': 'temporary cloned actors, manual motion sampling, no product changes or tests',
    'frames': 360, 'fps': 20, 'seconds': 18, 'width': 1280, 'height': 720, 'full_decode': 'passed',
    'formal_files_checked': len(hashes), 'formal_file_changes': changed, 'motion': stats}
(batch / 'audit.json').write_text(json.dumps(audit, ensure_ascii=False, indent=2), encoding='utf-8')
(batch / 'index.html').write_text('''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>丧尸地面行走候选</title><style>body{background:#15232a;color:#d8e7e5;font:17px system-ui;max-width:1280px;margin:24px auto;padding:12px}video,img{width:100%}a{color:#9fe1bb}</style><h1>丧尸地面行走改良 · 临时候选</h1><p>左为现版正式片段，右为候选；三种丧尸依次展示常用俯视角和半速低机位。正式主角原尺寸同框，相同演示速度。尚未接入正式游戏，也不代表码头攀爬已采用。</p><video controls autoplay muted loop playsinline src="zombie-ground-walk-comparison.mp4" poster="0-comparison.png"></video><p>候选增加交替迈步、屈膝和抬脚，着地阶段脚掌保持原处；上身沿用原有移动片段。人工轨迹与临时骨架演示，非实战录像。</p><a href="audit.json">检查记录</a></html>''', encoding='utf-8')
print(json.dumps(audit, ensure_ascii=False))
print(video)
