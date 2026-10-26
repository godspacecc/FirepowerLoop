from pathlib import Path
import subprocess, json, re, hashlib, sys
from PIL import Image, ImageDraw
root = Path(__file__).resolve().parents[2]
folder = root / 'Backup/Artifacts/TenStageCreaturesApplied-20261026'
ffmpeg = root / 'Backup/Artifacts/FinalDragonModel-v1-20261026/_tools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
selected = sys.argv[1:] or ['Spider','Ghoul','RedMangler','ZRex']
assert set(selected) <= {'Spider','Ghoul','RedMangler','ZRex'}
results = json.loads((folder/'video-audit.json').read_text()) if (folder/'video-audit.json').exists() else []
results = [r for r in results if r['name'] not in selected]
for name in selected:
    out = folder / name
    frames = sorted((out/'frames').glob('*.png'))
    assert len(frames) == 160, (name,len(frames))
    for frame in frames:
        with Image.open(frame) as im: im.verify()
    assert len({hashlib.sha256(f.read_bytes()).hexdigest() for f in frames[:140]}) > 30
    assert len({hashlib.sha256(f.read_bytes()).hexdigest() for f in frames[140:]}) == 1, name+' pause drift'
    sheet=Image.new('RGB',(1280,420),(24,30,34)); draw=ImageDraw.Draw(sheet)
    for slot,index in enumerate([0,12,24,35,44,54,96,145]):
        im=Image.open(frames[index]).resize((320,180));x=slot%4*320;y=slot//4*210
        sheet.paste(im,(x,y+30));draw.text((x+8,y+8),f'{name}  {index/20:.2f}s',fill='white')
    sheet.save(out/'contact-sheet.jpg',quality=92)
    for variant,rate in [('normal',20),('slow',10)]:
        target=out/(variant+'.mp4')
        cmd=[str(ffmpeg),'-y','-hide_banner','-loglevel','error','-framerate',str(rate),'-i',str(out/'frames/%04d.png'),'-c:v','libx264','-preset','medium','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(target)]
        subprocess.run(cmd,check=True)
        decoded=subprocess.run([str(ffmpeg),'-hide_banner','-v','error','-i',str(target),'-progress','pipe:1','-f','null','-'],check=True,capture_output=True,text=True)
        count=int(re.findall(r'frame=(\d+)',decoded.stdout)[-1]); assert count==160
        results.append({'name':name,'variant':variant,'frames':count,'durationSeconds':160/rate,'bytes':target.stat().st_size,'decode':'passed'})
(folder/'video-audit.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
cards=[]
for name,title in [('Spider','蜘蛛'),('Ghoul','食尸鬼'),('RedMangler','红色重甲怪'),('ZRex','僵尸暴龙')]:
    cards.append(f'<section><h2>{title}</h2><video controls preload="metadata" poster="{name}/poster.png" src="{name}/normal.mp4"></video><p><a href="{name}/slow.mp4">半速回读</a> · <a href="{name}/contact-sheet.jpg">动作分帧</a> · <a href="{name}/timeline.csv">权威输入记录</a></p></section>')
html='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>四种角色 · 正式资源回读</title><style>body{max-width:1000px;margin:40px auto;padding:0 20px;background:#172126;color:#e3e9e5;font:17px/1.7 system-ui}video{width:100%;background:#000}a{color:#a8d8dd}section{margin:45px 0}h1{font-size:30px}p{color:#bccac7}</style><h1>四种角色 · 正式资源回读</h1><p>已采用模型与动作接入。正式主角原尺寸同框，使用正式Controller及Prefab在隔离场景中接受手工权威输入；未运行AI、伤害或实际对局，不代表自然通关或真人双端验证。</p><p>每条8秒原速，末尾1秒重复同一权威年龄验证暂停。暴龙回读按三个真实周期取样，跳过中间等待，不改变正式技能周期。红重甲只回读开炮姿态，不生成预览子弹或伤害。</p><p><a href="README.md">验证说明</a> · <a href="readback.txt">采用源对照</a> · <a href="video-audit.json">视频解码记录</a></p>'''+''.join(cards)+'</html>'
(folder/'index.html').write_text(html,encoding='utf-8')
print(json.dumps(results,indent=2))
