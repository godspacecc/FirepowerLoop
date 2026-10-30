from PIL import Image, ImageDraw, ImageFont
from pathlib import Path
p=Path('Backup/Artifacts/SurvivorEnemyRoster-20261030')
f=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',22)
small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',16)
frames=[]
labels=['正式主角（尺寸参照）','普通僵尸｜开局','瘦削僵尸｜2分钟','弓箭手｜4分钟','重型精英｜6分钟']
for index,path in enumerate(sorted((p/'frames').glob('*.png'))):
 im=Image.open(path).convert('RGB');d=ImageDraw.Draw(im)
 d.text((30,24),'幸存者候选：共用现有模型与动作',font=f,fill='#e8f6ff')
 d.text((30,61),'临时隔离场景渲染 · 非正式对局 · 原Prefab尺寸 · 前3秒移动 / 后2秒近战动作（弓箭手待射）',font=small,fill='#99b2c7')
 for x,label in zip([180,390,600,810,1020],labels):
  box=d.textbbox((0,0),label,font=small);d.text((x-(box[2]-box[0])/2,470),label,font=small,fill='#e1eaf0')
 d.text((30,550),'此预览用于确认外观、同框比例和既有动作；远程弹道与混合刷怪仍待接入验证。',font=small,fill='#99b2c7')
 frames.append(im)
frames[24].save(p/'comparison.png')
frames[0].save(p/'roster-preview.gif',save_all=True,append_images=frames[1:],duration=83,loop=0,optimize=False)
print('frames:',len(frames),'unique:',len({im.tobytes() for im in frames}))
try:
 import imageio_ffmpeg
 print('ffmpeg:',imageio_ffmpeg.get_ffmpeg_exe())
except ImportError: pass
