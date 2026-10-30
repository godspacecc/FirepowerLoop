"""编码三路海中攀爬候选，保持正式资源不变。"""
from pathlib import Path
import argparse,json,subprocess,hashlib
from PIL import Image,ImageDraw,ImageFont
parser=argparse.ArgumentParser();parser.add_argument('--ffmpeg',required=True);args=parser.parse_args()
batch=Path(__file__).resolve().parent
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',24)
small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',19)
segments=[('三路同时入场 · 全景',sorted((batch/'frames').glob('[0-9][0-9][0-9].png')))]
for i,name in enumerate(['右侧','左侧','上方']):segments.append((name+'水中入场 · 动作近景',sorted((batch/'frames').glob('detail-'+str(i)+'-*.png'))))
assert [len(files) for _,files in segments]==[180,140,140,140]
video=batch/'three-side-entry-applied.mp4'
cmd=[args.ffmpeg,'-y','-hide_banner','-loglevel','warning','-f','rawvideo','-pix_fmt','rgb24','-s','1280x720','-r','20','-i','-','-an','-c:v','libx264','-preset','medium','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(video)]
process=subprocess.Popen(cmd,stdin=subprocess.PIPE)
shots=[]
for si,(name,files) in enumerate(segments):
 for i,path in enumerate(files):
  frame=Image.open(path).convert('RGB');draw=ImageDraw.Draw(frame)
  draw.rectangle((0,0,1280,71),fill='#11252b');draw.text((640,7),'三方向海中攀爬 · 正式资源回放',font=font,fill='white',anchor='mt')
  draw.text((640,42),name+'｜正式主角同尺寸参照｜隔离场景采样，并非自然对局',font=small,fill='#b6d9db',anchor='mt')
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
audit={'frames':600,'fps':20,'seconds':30,'width':1280,'height':720,'decode':'passed','source':'formal map, characters and authoritative three-direction paths in isolated scene; reference ground continuation; not natural gameplay'}
(batch/'replay-audit.json').write_text(json.dumps(audit,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(audit,ensure_ascii=False));print(video)
