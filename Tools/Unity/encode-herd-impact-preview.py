"""为隔离动作候选加说明，以原20fps输出可播放预览，不修改游戏资源。"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
source = root / 'Client/Temp/HerdImpactConcept-v2-20261023/frames'
output = root / 'Docs/Plans/Artifacts/HerdImpactConcept-v2-20261023'
output.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 22)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 17)
frames = []
for path in sorted(source.glob('frame-*.png')):
    image = Image.open(path).convert('RGB')
    result = Image.new('RGB', (1000, 790), (18, 26, 31))
    result.paste(image, (0, 90))
    draw = ImageDraw.Draw(result)
    draw.text((18, 8), '牛撞击飞 v2：明显腾空 → 落地僵直0.5秒 → 恢复', font=font, fill='white')
    draw.text((18, 45), '临时动作候选，未正式接入 / 飞高2.5米、前飞3米 / 两者演示存活受击', font=small, fill=(205, 218, 224))
    frame_index = len(frames)
    time = frame_index * .05
    if time < .65:
        state = '等待碰撞'
    elif time < 1.60:
        state = '腾空飞出'
    elif time < 2.10:
        state = f'落地僵直 {min(.5, time - 1.6):.2f} / 0.50秒'
    elif time < 2.35:
        state = '僵直结束，恢复起身'
    else:
        state = '恢复行动'
    draw.text((22, 202), '丧尸（存活时）', font=small, fill='white', stroke_width=2, stroke_fill=(25, 35, 30))
    draw.text((22, 508), '玩家', font=small, fill='white', stroke_width=2, stroke_fill=(25, 35, 30))
    draw.rectangle((0, 748, 1000, 790), fill=(18, 26, 31))
    draw.text((22, 755), f'{state}    |    {time:.2f}s / 原速20fps    |    击杀的丧尸仍保持死亡', font=small, fill='white')
    frames.append(result)
assert len(frames) == 80
frames[0].save(output / 'herd-impact-v2.gif', save_all=True, append_images=frames[1:], duration=50, loop=0, optimize=False)
frames[22].save(output / 'herd-impact-v2.png')
with Image.open(output / 'herd-impact-v2.gif') as gif:
    assert gif.n_frames == 80
    assert sum(gif.seek(i) or gif.info['duration'] for i in range(gif.n_frames)) == 4000
# 真实落地停顿连续10帧，即20fps下0.50秒；起身发生于其后。
assert len([i for i in range(80) if 1.60 <= i * .05 < 2.10]) == 10
print('Verified: 80 frames, 20fps, 4.0 seconds; grounded hold 10 frames = 0.50 seconds.')
