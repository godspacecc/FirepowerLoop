from PIL import Image, ImageDraw, ImageFont
from pathlib import Path
p=Path('Backup/Artifacts/SurvivorHeavyZombie-20261030')
f=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',22)
small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',16)
frames=[]
labels=['正式主角（尺寸参照）','普通丧尸｜开局','瘦削丧尸｜2分钟','笨重丧尸｜建议4分钟']
for index,path in enumerate(sorted((p/'frames').glob('*.png'))):
 im=Image.open(path).convert('RGB');d=ImageDraw.Draw(im)
 d.text((30,24),'笨重丧尸候选：共用现有模型与动作',font=f,fill='#e8f6ff')
 d.text((30,61),'临时隔离场景渲染 · 非正式对局 · 原Prefab尺寸 · 前3秒步态 / 后2秒抓挠姿态 · 右侧慢速',font=small,fill='#99b2c7')
 for x,label in zip([285,495,705,915],labels):
  box=d.textbbox((0,0),label,font=small);d.text((x-(box[2]-box[0])/2,470),label,font=small,fill='#e1eaf0')
 d.text((30,550),'建议：4分钟后少量混入 · 基础生命为普通丧尸3倍 · 基础移速65% · 本图非实际战斗',font=small,fill='#99b2c7')
 frames.append(im)
frames[24].save(p/'comparison.png')
frames[0].save(p/'heavy-preview.gif',save_all=True,append_images=frames[1:],duration=83,loop=0,optimize=False)
print('frames:',len(frames),'unique:',len({im.tobytes() for im in frames}))
try:
 import imageio_ffmpeg
 print('ffmpeg:',imageio_ffmpeg.get_ffmpeg_exe())
except ImportError: pass
