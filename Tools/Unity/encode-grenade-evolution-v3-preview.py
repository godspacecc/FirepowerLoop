"""给榴弹体量修正版的Unity预览加说明。"""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
root=Path(__file__).resolve().parents[2]
out=root/'Backup/Artifacts/WeaponEvolution-v3-20261023'
out.mkdir(parents=True,exist_ok=True)
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',27)
small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',20)
paths=sorted((root/'Client/Temp/WeaponEvolutionPreview-v3/5').glob('*.png'))
assert len(paths)==160
frames=[]
for path in paths[:128]:
    canvas=Image.new('RGB',(960,754),(10,16,23));canvas.paste(Image.open(path).convert('RGB'),(0,80));d=ImageDraw.Draw(canvas)
    d.text((20,8),'集束榴弹 v3 · 增大主爆与子爆体量',font=font,fill=(239,235,255))
    d.text((20,47),'实体榴弹抛射 → 主爆分裂 → 三枚子榴弹再爆与烟尘',font=small,fill=(195,211,222))
    d.text((20,726),'视觉候选 · 正式主角同框 · 演示时序 / 半速 · 尚未正式接入',font=small,fill=(177,193,209))
    frames.append(canvas)
frames[0].save(out/'grenade.gif',save_all=True,append_images=frames[1:],duration=50,loop=0,optimize=False)
frames[53].save(out/'grenade-still.png')
with Image.open(out/'grenade.gif') as im:
    duration=sum(im.seek(i) or im.info.get('duration',0) for i in range(im.n_frames));assert duration==6400
    print(im.n_frames,duration,im.size)
(out/'README.md').write_text('用户认为v2子爆太小后制作的集束榴弹v3独立视觉演示。正式主角同框，实体榴弹、抛射弧线、主爆与子爆明显放大，增加烟尘。128源帧，20fps，半速6.4秒，960×754。不是实际伤害范围或实战录像；玩法伤害、半径与时长未改。具体视觉仍待采用。',encoding='utf-8')
