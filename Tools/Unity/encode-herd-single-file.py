"""按真实0.1秒采样间隔编码正式纵队隔离录制；不改变模型与游戏资源。"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
source = root / 'Client/Temp/HerdSingleFile-20261023'
output = root / 'Docs/Plans/Artifacts/HerdSingleFile-20261023'
output.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 22)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 17)

def caption(path):
    frame = Image.open(path).convert('RGB')
    result = Image.new('RGB', (frame.width, frame.height + 82), (18, 26, 31))
    result.paste(frame, (0, 82))
    draw = ImageDraw.Draw(result)
    draw.text((18, 9), '第三关已接入：五牛前后纵队，间距3米，速度只快10%', font=font, fill='white')
    draw.text((18, 46), '正式模型隔离录制：牛由实际活动循环推进；主角按基础6.5m/s同框对照', font=small, fill=(201, 215, 221))
    return result

frames = [caption(path) for path in sorted((source / 'frames').glob('herd-*.png'))]
assert len(frames) == 65, f'Expected 65 frames, got {len(frames)}'
frames[0].save(output / 'herd-single-file.gif', save_all=True, append_images=frames[1:], duration=100, loop=0, optimize=False)
caption(source / 'farm-integration.png').save(output / 'herd-single-file.png')
with Image.open(output / 'herd-single-file.gif') as check:
    assert check.n_frames == 65
    assert sum(check.seek(i) or check.info['duration'] for i in range(check.n_frames)) == 6500
print('65 frames verified; playback 6.5 seconds at original speed.')
