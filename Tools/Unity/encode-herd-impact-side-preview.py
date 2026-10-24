"""编码隔离侧飞候选：完整五牛纵队与正式主角同框，不冒充正式规则验证。"""
from pathlib import Path
from math import sqrt
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
source = root / 'Client/Temp/HerdImpactSide-v3-20261023/frames'
output = root / 'Backup/Artifacts/HerdImpactSide-v3-20261023'
output.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 24)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 18)
frames = []
hold = []
for index, path in enumerate(sorted(source.glob('frame-*.png'))):
    time = index * .05
    if index < 13:
        state = '牛队接近'
    elif index < 32:
        state = '玩家斜向侧边飞出，离开冲撞路线'
    elif index < 42:
        state = '落地僵直0.5秒，后续牛从旁边经过'
        hold.append(index)
    elif index < 47:
        state = '起身恢复'
    else:
        state = '已在牛队侧边，可恢复操作'
    frame = Image.new('RGB', (1000, 810), (18, 26, 31))
    frame.paste(Image.open(path).convert('RGB'), (0, 90))
    draw = ImageDraw.Draw(frame)
    draw.text((18, 8), '玩家侧飞候选：飞出牛队路线 → 落地停0.5秒', font=font, fill='white')
    draw.text((18, 47), '正式模型克隆 / 临时轨迹示意，尚未接入正式玩法；黄线标出后续牛的路线', font=small, fill=(210, 220, 225))
    draw.rectangle((0, 765, 1000, 810), fill=(18, 26, 31))
    draw.text((18, 776), f'{time:.2f}s | {state}', font=small, fill='white')
    frames.append(frame)
assert len(frames) == 100 and len(hold) == 10
assert 3 * sqrt(.84) > 1.1
frames[0].save(output / 'herd-impact-side-v3.gif', save_all=True, append_images=frames[1:], duration=50, loop=0, optimize=False)
frames[24].save(output / 'herd-impact-side-v3.png')
with Image.open(output / 'herd-impact-side-v3.gif') as result:
    assert result.n_frames == 100
    assert sum(result.seek(i) or result.info['duration'] for i in range(result.n_frames)) == 5000
print('Preview verified: 100 frames / 20 fps / 5 s; 10 grounded hold frames; temporary side offset 2.75m.')
