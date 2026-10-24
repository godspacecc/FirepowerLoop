"""标注Unity隔离视觉演示，输出可播放候选；不修改正式素材。"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
source = root / 'Client/Temp/WeaponEvolutionPreview-v2'
out = root / 'Backup/Artifacts/WeaponEvolution-v2-20261023'
out.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 27)
small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 20)
titles = {3: '喷火器进化 · 低焰、余烬与烟层', 4: '穿透枪进化 · 蓄能收束与分层宽束', 5: '榴弹进化 · 分裂拖尾与二次爆炸'}
notes = {3: '停火后保留燃烧，最后火苗衰减、余烟消散', 4: '前三发普通弹，第四发白芯蓝晕，紫色流线环绕', 5: '主弹先爆，三枚子榴弹分散，随后各自爆开'}
for kind in (3, 4, 5):
    paths = sorted((source / str(kind)).glob('*.png'))
    assert len(paths) == 160, (kind, len(paths))
    frames = []
    for n, path in enumerate(paths[:160 if kind == 3 else 104]):
        canvas = Image.new('RGB', (960, 754), (10, 16, 23))
        canvas.paste(Image.open(path).convert('RGB'), (0, 80))
        d = ImageDraw.Draw(canvas)
        d.text((20, 8), titles[kind], font=font, fill=(236, 235, 255))
        d.text((20, 47), notes[kind], font=small, fill=(190, 207, 219))
        d.text((20, 726), 'v2视觉候选 · 正式角色同框 · 演示时序 / 半速 · 尚未接入正式战斗', font=small, fill=(171, 191, 208))
        frames.append(canvas)
    target = out / f'{kind}.gif'
    frames[0].save(target, save_all=True, append_images=frames[1:], duration=50, loop=0, optimize=False)
    with Image.open(target) as check:
        duration = sum(check.seek(i) or check.info.get('duration', 0) for i in range(check.n_frames))
        assert check.n_frames > 80 and duration == len(frames)*50, (kind, check.n_frames, duration)
        print(f'{target.name}: {check.n_frames} frames / {duration} ms / {check.size}')
    frames[45 if kind == 3 else 64 if kind == 4 else 51].save(out / f'{kind}-still.png')
(out / 'README.md').write_text('''# 紫级进化视觉候选 v2

用户拒绝v1第二组简单模型表现后制作，三项均尚待采用。第一组与双无人机的采用保持。

- 使用正式主角/丧尸克隆，独立Unity PreviewScene中动态渲染。
- 火焰图集、烟纹理复用已有素材的内存材质；软光遮罩在内存创建。没有改动正式Prefab、贴图或材质。
- 这是按确认玩法编排的视觉演示时序，非实际战斗规则步进或自然对局录像。
- 3.gif：粒子火焰、低层热光、向上余烬及余烟；160源帧，半速8秒。
- 4.gif：三发普通弹后收束蓄能，白色内芯、蓝色束体、柔和外晕与流动细线；104帧，半速5.2秒。
- 5.gif：主弹拖尾、主爆、三枚子榴弹分散拖尾、二次火光/火星/烟尘；104帧，半速5.2秒。
- 视频帧960×640，带说明GIF为960×754。正式接入等待具体视觉采用。
''', encoding='utf-8')
