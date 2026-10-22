from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
src=Path('Client/Temp/PurpleWeaponProposal-v1');dst=Path('Docs/Plans/Artifacts/RemainingStages-v2-20261022')
f=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',24);small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',19)
titles=['紫级火箭筒｜三联火箭追踪移动敌人','紫级射线枪｜碰壁反射 + 命中后触电连到附近敌人','紫级榴弹枪｜快速连续发射，保留原有爆炸']
frames=[]
for k,title in enumerate(titles):
 for p in sorted(src.glob(str(k)+'-*.png')):
  im=Image.open(p).convert('RGB');out=Image.new('RGB',(960,692),(15,22,28));out.paste(im,(0,92));d=ImageDraw.Draw(out);d.text((20,12),title,font=f,fill='white');d.text((20,51),'临时动态方案 v1｜正式主角 / 枪械 / 弹体，手动采样；非实际战斗',font=small,fill=(190,207,217));frames.append(out)
frames[0].save(dst/'purple-weapons-v1.gif',save_all=True,append_images=frames[1:],duration=67,loop=0,optimize=False)
print('Encoded 225 frames, 15 seconds.')
