"""给真实权威步进的隔离预览加说明；只处理临时帧与交付预览。"""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
root=Path(__file__).resolve().parents[2]
source=root/'Client/Temp/WeaponEvolutionPreview-v1'
out=root/'Docs/Plans/Artifacts/WeaponEvolution-v1-20261023'
out.mkdir(parents=True,exist_ok=True)
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',23)
small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',17)
titles=['基础AK：击杀贯穿连杀','速射枪：周期追加左右弹','霰弹枪：震退近敌','喷火器：停火后留下残火','穿透枪：周期宽贯穿束','榴弹：首爆后三枚子榴弹','无人机：紫级增加一架']
notes=['击杀前排后继续；遇到存活目标停弹','每3次成功开火追加两发侧弹','同次齐射对同一丧尸最多震退一次','地面保留2秒，同一来源重叠不叠伤','每4次成功开火发出一束宽贯穿弹','子榴弹延迟再爆，不继续分裂','沿用原无人机攻击，优先分担不同目标']
all_frames=[]
for kind in range(7):
 frames=[]
 paths=sorted((source/str(kind)).glob('*.png'))
 assert len(paths)==80,(kind,len(paths))
 for frame,path in enumerate(paths):
  image=Image.new('RGB',(720,582),(10,16,23));image.paste(Image.open(path).convert('RGB'),(0,70));d=ImageDraw.Draw(image)
  d.text((16,8),titles[kind],font=font,fill=(240,232,255));d.text((16,42),notes[kind],font=small,fill=(201,212,222))
  d.text((16,553),'正式角色同框 · 新效果为临时候选 · 实际战斗规则步进 / 0.5倍慢放',font=small,fill=(176,193,204))
  frames.append(image)
 frames[0].save(out/f'{kind}.gif',save_all=True,append_images=frames[1:],duration=50,loop=0,optimize=False)
 all_frames.append(frames)
 (out/f'{kind}-facts.txt').write_text((source/str(kind)/'facts.txt').read_text(encoding='utf-8'),encoding='utf-8')
for group,indices in [('weapons-a',[0,1,2]),('weapons-b',[3,4,5])]:
 frames=[]
 for f in range(80):
  img=Image.new('RGB',(1440,388))
  for column,kind in enumerate(indices):img.paste(all_frames[kind][f].resize((480,388),Image.Resampling.LANCZOS),(480*column,0))
  frames.append(img)
 frames[0].save(out/f'{group}.gif',save_all=True,append_images=frames[1:],duration=50,loop=0,optimize=False)
all_frames[6][30].save(out/'drone-still.png')
(out/'index.html').write_text('<!doctype html><meta charset="utf-8"><title>紫级武器进化 v1</title><style>body{background:#0a1017;color:#edf0ff;font:16px sans-serif;margin:24px}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(430px,1fr));gap:16px}img{width:100%;border-radius:10px}p{color:#b9c9d9}</style><h1>紫级武器进化 · 动态候选 v1</h1><p>正式主角、丧尸与无人机同框；弹丸、火带和宽束是临时候选。真实单人战斗规则的隔离步进，0.5倍慢放，不是自然对局录像。火箭三联追踪与射线连锁沿用现有效果。</p><main>'+''.join(f'<a href="{i}.gif"><img src="{i}.gif" alt="{titles[i]}"></a>' for i in range(7))+'</main>',encoding='utf-8')
print('7 clips + 2 grouped clips; each 80 frames, 20fps, 4 seconds')
