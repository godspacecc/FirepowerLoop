"""正式四足演员隔离快照回放的原速/半速编码及完整解码验证。"""
from pathlib import Path
import argparse, hashlib, json, subprocess
from PIL import Image, ImageDraw, ImageFont

parser=argparse.ArgumentParser();parser.add_argument('--ffmpeg',required=True,type=Path);args=parser.parse_args()
out=Path(__file__).resolve().parents[2]/'Backup/Artifacts/HengeVolcanoPolish-v1-20261025/Creatures/Applied'
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',25);small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',19)
report={'scope':'正式Controller与正式资源的隔离快照回放，不是真实对局或真人双端录像；阶段使用原配置7.5秒周期/1.2秒预警。','clips':{}}

def phase(kind,t):
    p=t%7.5
    if kind=='burrower' and p<1.2:
        return '收爪下潜' if p<.324 else '地下原位扰动' if p<.756 else '前爪抓地、撑身出土'
    if kind=='hound' and p<1.2:return '预警：接近、锁向蓄势'
    if 3.4<=t<3.8:return '真实近战进度示例：张口前摇'
    if 3.8<=t<4.2:return '近战收招进度示例'
    if kind=='hound' and p<2.94:return '活动段：扑咬、前足先落地、后足补落'
    return '实际位置驱动步态 / 停步待机'

def decorate(path,kind,i,slow):
    name='潜地怪' if kind=='burrower' else '恶犬'
    image=Image.new('RGB',(1280,840),(19,27,31));image.paste(Image.open(path).convert('RGB'),(0,74));d=ImageDraw.Draw(image)
    d.text((22,7),f'{name} · 正式资源接入回放 · {"0.5× 慢放" if slow else "1× 正常速度"}',font=font,fill=(243,226,180))
    d.text((22,43),'正式主角原尺寸 / 隔离快照采样 / 原技能计时与判伤保持 / 仅为看清动作隐藏环境与HUD',font=small,fill=(196,210,215))
    d.text((22,803),f'{phase(kind,i/24)}    |    权威年龄示例 {i/24:.2f}s',font=small,fill='white')
    return image

for kind in ['burrower','hound']:
    paths=sorted((out/kind).glob('*.png'));assert len(paths)==192
    r={}
    for slow,fps,suffix in [(False,24,'normal'),(True,12,'slow')]:
        video=out/f'{kind}-{suffix}.mp4'
        cmd=[str(args.ffmpeg),'-y','-hide_banner','-loglevel','error','-f','rawvideo','-pix_fmt','rgb24','-s','1280x840','-r',str(fps),'-i','-','-an','-c:v','libx264','-preset','medium','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(video)]
        proc=subprocess.Popen(cmd,stdin=subprocess.PIPE)
        try:
            for i,path in enumerate(paths):proc.stdin.write(decorate(path,kind,i,slow).tobytes())
        finally:proc.stdin.close()
        assert proc.wait()==0
        decoded=subprocess.run([str(args.ffmpeg),'-v','error','-i',str(video),'-progress','pipe:1','-nostats','-f','null','-'],check=True,capture_output=True,text=True)
        fields=dict(line.split('=',1) for line in decoded.stdout.splitlines() if '=' in line)
        assert int(fields['frame'])==192 and not decoded.stderr.strip()
        r[suffix]={'fps':fps,'framesDecoded':192,'seconds':192/fps,'sha256':hashlib.sha256(video.read_bytes()).hexdigest(),'bytes':video.stat().st_size,'fullDecode':True}
    images=[decorate(p,kind,i,False).resize((768,504),Image.Resampling.LANCZOS) for i,p in enumerate(paths) if i%2==0]
    images[0].save(out/f'{kind}-normal.gif',save_all=True,append_images=images[1:],duration=83,loop=0,optimize=False)
    indices=[0,12,22,29,38,57,85,92,120]
    sheet=Image.new('RGB',(1920,1250),(19,27,31));d=ImageDraw.Draw(sheet);d.text((20,8),f'{kind} / 正式资源接入关键帧 / 非真实对局',font=font,fill='white')
    for j,i in enumerate(indices):
        x,y=j%3*640,j//3*400+50;sheet.paste(Image.open(paths[i]).convert('RGB').resize((640,360),Image.Resampling.LANCZOS),(x,y));d.text((x+12,y+368),f'{i/24:.2f}s · {phase(kind,i/24)}',font=small,fill='white')
    sheet.save(out/f'{kind}-keyframes.jpg',quality=94)
    report['clips'][kind]=r
(out/'video-verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
(out/'index.html').write_text('''<!doctype html><meta charset="utf-8"><title>潜地怪与恶犬正式接入回放</title><style>body{background:#131b1f;color:#e4ecef;font:16px system-ui;margin:0}main{max-width:1280px;margin:auto;padding:28px}p{line-height:1.7}video,img{width:100%;display:block;margin:18px 0;border-radius:10px}h2{margin-top:40px}a{color:#a6d9eb}</style><main><h1>潜地怪与恶犬 · 正式接入回放</h1><p>采用版网格、材质和动画已进入正式Prefab，并从单人/联机共用的正式Controller按只读状态采样。正式主角保持原尺寸。这里是隔离场景手动快照回放，位置、近战剩余量由捕获工具安排；不是自然对局、实际AI追击或真人双端验收。为看清动作隐藏了环境与头顶HUD，技能仍沿用原配置7.5秒周期和1.2秒预警。</p><h2>潜地怪</h2><video controls loop playsinline src="burrower-normal.mp4"></video><details><summary>0.5× 慢放与关键帧</summary><video controls loop playsinline src="burrower-slow.mp4"></video><img src="burrower-keyframes.jpg"></details><h2>恶犬</h2><video controls loop playsinline src="hound-normal.mp4"></video><details><summary>0.5× 慢放与关键帧</summary><video controls loop playsinline src="hound-slow.mp4"></video><img src="hound-keyframes.jpg"></details><p><a href="capture.txt">采用姿态对照误差</a> · <a href="video-verification.json">四条视频完整解码</a> · <a href="README.md">验证及交接说明</a></p></main>''',encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
