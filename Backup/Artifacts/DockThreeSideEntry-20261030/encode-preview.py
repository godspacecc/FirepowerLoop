"""编码三路海中攀爬候选，保持正式资源不变。"""
from pathlib import Path
import argparse,json,subprocess,hashlib
from PIL import Image,ImageDraw,ImageFont
parser=argparse.ArgumentParser();parser.add_argument('--ffmpeg',required=True);args=parser.parse_args()
batch=Path(__file__).resolve().parent
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',24)
small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',19)
segments=[('三路同时入场 · 临时全景',sorted((batch/'frames').glob('[0-9][0-9][0-9].png')))]
for i,name in enumerate(['右侧','左侧','上方']):segments.append((name+'水中入场 · 动作近景',sorted((batch/'frames').glob('detail-'+str(i)+'-*.png'))))
assert [len(files) for _,files in segments]==[180,140,140,140]
video=batch/'three-side-entry-preview.mp4'
cmd=[args.ffmpeg,'-y','-hide_banner','-loglevel','warning','-f','rawvideo','-pix_fmt','rgb24','-s','1280x720','-r','20','-i','-','-an','-c:v','libx264','-preset','medium','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(video)]
process=subprocess.Popen(cmd,stdin=subprocess.PIPE)
shots=[]
for si,(name,files) in enumerate(segments):
 for i,path in enumerate(files):
  frame=Image.open(path).convert('RGB');draw=ImageDraw.Draw(frame)
  draw.rectangle((0,0,1280,71),fill='#11252b');draw.text((640,7),'三方向海中攀爬 · 候选预览',font=font,fill='white',anchor='mt')
  draw.text((640,42),name+'｜正式主角同尺寸参照｜尚未接入正式地图',font=small,fill='#b6d9db',anchor='mt')
  time=i/20
  stage='水面靠近，头肩露出水面' if time<2.1 else '双手抓边、收腿准备攀爬' if time<2.7 else '双臂撑起身体、抬膝越过平台' if time<4.45 else '前脚落地支撑、后腿蹬起补步' if time<5.75 else '双脚登台、站稳后接入地面行走'
  draw.rectangle((0,664,1280,720),fill='#11252b');draw.text((640,680),stage,font=font,fill='#a4e6cc',anchor='mt')
  if (si==0 and i in [25,85,135]) or (si>0 and i==80):
   frame.save(batch/('key-'+str(si)+'-'+str(i)+'.png'));shots.append(frame.copy())
  process.stdin.write(frame.tobytes())
process.stdin.close();assert process.wait()==0
subprocess.run([args.ffmpeg,'-hide_banner','-loglevel','error','-i',str(video),'-f','null','-'],check=True)
sheet=Image.new('RGB',(1280,1080),'#11252b')
for i,frame in enumerate(shots):sheet.paste(frame.resize((640,360)),((i%2)*640,(i//2)*360))
sheet.save(batch/'contact-sheet.png')
root=batch.parents[2]
before=json.loads((batch/'before-hashes.json').read_text(encoding='utf-8'))
changed=[f for f,digest in before.items() if hashlib.sha256((root/f).read_bytes()).hexdigest()!=digest]
audit={'frames':600,'fps':20,'seconds':30,'width':1280,'height':720,'decode':'passed','formal_files_checked':len(before),'formal_files_changed':changed,'source':'temporary scene clones and reused approved animations; three route transforms are preview only, not natural gameplay'}
(batch/'audit.json').write_text(json.dumps(audit,ensure_ascii=False,indent=2),encoding='utf-8')
assert not changed,changed
(batch/'index.html').write_text('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>三方向海中攀爬候选</title><style>body{max-width:1280px;margin:24px auto;background:#11252b;color:#e6eff0;font:18px system-ui}video,img{width:100%}</style><h1>三方向海中攀爬 · 待采用</h1><p>左侧、上方临时撤去木通道。复用已采用的游近、抓边、抬腿登台动作，正式主角原尺寸同框。前9秒为三路全景，随后分别展示三个方向。临时演示，不是已接入实战录像；正式资源未改。</p><video controls muted playsinline src="three-side-entry-preview.mp4" poster="key-0-85.png"></video><img src="contact-sheet.png"></html>',encoding='utf-8')
print(json.dumps(audit,ensure_ascii=False));print(video)
