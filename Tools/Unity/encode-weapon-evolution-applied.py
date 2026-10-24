"""标注正式模板隔离采样；保留候选原件，不冒充自然对局。"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
out = root / 'Backup/Artifacts/WeaponEvolution-Applied-20261023'
out.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 27)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 20)
for kind, name, title, length, still in (
    (3, 'flame', '正式残火 · 已采用 v2 粒子、余烬与烟层', 160, 45),
    (4, 'piercing', '正式宽束 · 已采用 v2 白芯、蓝晕与流线', 104, 64),
    (5, 'grenade', '正式集束榴弹 · 已采用 v3 主爆与三枚子爆', 128, 53),
):
    paths = sorted((root / f'Client/Temp/WeaponEvolutionPreview-applied/{kind}').glob('*.png'))
    assert len(paths) == 160, (kind, len(paths))
    frames = []
    for path in paths[:length]:
        canvas = Image.new('RGB', (960, 754), (10, 16, 23))
        canvas.paste(Image.open(path).convert('RGB'), (0, 80))
        draw = ImageDraw.Draw(canvas)
        draw.text((20, 12), title, font=font, fill=(239, 235, 255))
        draw.text((20, 49), '正式主角同框 · 正式 Prefab 和表现时钟', font=small, fill=(195, 211, 222))
        draw.text((20, 726), '隔离手动采样 / 半速 · 演示时序，非自然对局录像', font=small, fill=(177, 193, 209))
        frames.append(canvas)
    target = out / f'{name}.gif'
    frames[0].save(target, save_all=True, append_images=frames[1:], duration=50, loop=0, optimize=False)
    frames[still].save(out / f'{name}-still.png')
    with Image.open(target) as im:
        duration = sum(im.seek(i) or im.info.get('duration', 0) for i in range(im.n_frames))
        assert duration == length * 50
        print(name, im.n_frames, duration, im.size)
