"""把本项目小龙临时模型帧编码为可播放预览，并验证帧数、尺寸及完整解码。"""
import argparse
import csv
import hashlib
import json
import subprocess
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser()
parser.add_argument('--ffmpeg', type=Path, required=True)
parser.add_argument('--formal', action='store_true')
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
folder = root / ('Backup/Artifacts/FinalDragonApplied-20261026' if args.formal else 'Backup/Artifacts/FinalDragonModel-v1-20261026')
frames = sorted((folder / 'frames').glob('[0-9][0-9][0-9].png'))
assert len(frames) == 240, f'需要240帧，实际{len(frames)}'
assert [p.stem for p in frames] == [f'{i:03}' for i in range(240)]
assert (folder / 'capture-state.txt').read_text().startswith('complete:240/240')
rows = list(csv.DictReader((folder / 'motion-timeline.csv').open(encoding='utf-8')))
assert len(rows) == 240
assert len({hashlib.sha256(p.read_bytes()).hexdigest() for p in frames}) > 230
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 25)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 18)
phase_labels = {'idle': '呼吸待机', 'walk': '四足行走', 'windup': '压低蓄力', 'lunge': '短距前扑', 'settle': '落稳恢复', 'jaw pose': '张口姿态', 'wing settle': '展翼恢复'}
audit = []
for name, fps in [('normal', 20), ('slow', 10)]:
    output = folder / f'{name}.mp4'
    command = [str(args.ffmpeg), '-y', '-hide_banner', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', '1280x720', '-r', str(fps), '-i', '-', '-an', '-c:v', 'libx264', '-preset', 'fast', '-crf', '19', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(output)]
    process = subprocess.Popen(command, stdin=subprocess.PIPE)
    try:
        for index, path in enumerate(frames):
            with Image.open(path) as source:
                assert source.size == (1280, 720)
                frame = source.convert('RGB')
            draw = ImageDraw.Draw(frame)
            draw.rectangle((0, 0, 1280, 74), fill='#1c282c')
            draw.text((32, 10), ('终局小龙 · 正式资源回读' if args.formal else '终局小龙 · 临时模型与动作 v1'), font=font, fill='white')
            draw.text((32, 45), ('Unity正式Prefab与动画 · 主角原尺寸同框 · 已采用并接入' if args.formal else 'Unity实际渲染 · 正式主角原尺寸同框 · 模型与动作待采用'), font=small, fill='#b9d3cc')
            draw.rectangle((0, 672, 1280, 720), fill='#1c282c')
            label = phase_labels[rows[index]['phase']]
            speed = '原速' if fps == 20 else '0.5倍慢放'
            draw.text((32, 684), f'{speed} / {label}    |    '+('正式片段隔离回放，不是整局实战录像' if args.formal else '隔离动作采样，无AI或伤害判定，未正式接入'), font=small, fill='#b9d3cc')
            if name == 'normal' and index == 40:
                frame.save(folder / 'poster.png')
            process.stdin.write(frame.tobytes())
    finally:
        process.stdin.close()
    assert process.wait() == 0
    checked = subprocess.run([str(args.ffmpeg), '-hide_banner', '-loglevel', 'error', '-i', str(output), '-progress', 'pipe:1', '-f', 'null', '-'], capture_output=True, text=True, check=True)
    final_count = [int(line.split('=')[1]) for line in checked.stdout.splitlines() if line.startswith('frame=')][-1]
    assert final_count == 240
    audit.append({'file': output.name, 'frames': final_count, 'fps': fps, 'seconds': final_count / fps, 'full_decode': 'passed', 'sha256': hashlib.sha256(output.read_bytes()).hexdigest()})

# 动作抽帧保留真实像素，仅缩放排列用于检查姿态差异，不替代可播放视频。
sheet = Image.new('RGB', (1280, 800), '#1c282c')
draw = ImageDraw.Draw(sheet)
for cell, index in enumerate([5, 55, 100, 119, 165, 215]):
    with Image.open(frames[index]) as source:
        shot = source.convert('RGB').resize((640, 360), Image.Resampling.LANCZOS)
    x, y = (cell % 2) * 640, (cell // 2) * 266
    shot.thumbnail((640, 238), Image.Resampling.LANCZOS)
    sheet.paste(shot, (x + (640 - shot.width) // 2, y))
    draw.text((x + 20, y + 239), f'{index / 20:.2f}s · {phase_labels[rows[index]["phase"]]}', font=small, fill='white')
sheet.save(folder / 'motion-contact-sheet.jpg', quality=92)
(folder / 'video-audit.json').write_text(json.dumps(audit, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(audit, ensure_ascii=False))
