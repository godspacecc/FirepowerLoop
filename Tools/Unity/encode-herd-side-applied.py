"""编码正式侧飞隔离录制，同时校验五牛通过期间的权威轨迹、生命与落地停顿。"""
import csv
from math import sqrt
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root=Path(__file__).resolve().parents[2]
source=root/'Client/Temp/HerdImpactSideApplied-20261023'
output=root/'Backup/Artifacts/HerdImpactSideApplied-20261023'
output.mkdir(parents=True,exist_ok=True)
trace=list(csv.DictReader((source/'authority-trace.csv').open(encoding='utf-8-sig')))
assert len(trace)==100
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',24)
small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',18)
frames=[]
hold=[]
for i,path in enumerate(sorted((source/'frames').glob('frame-*.png'))):
    row=trace[i];remaining=float(row['remaining'])
    if remaining>.75+1e-5:phase='斜向侧飞，离开后续牛的路线'
    elif remaining>.25+1e-5:phase='落地僵直0.5秒';hold.append(row)
    elif remaining>0:phase='起身恢复'
    elif int(row['health'])==100:phase='牛队接近'
    elif float(row['tailX'])>float(row['playerX'])+2:phase='后续四牛已通过，玩家恢复操作'
    else:phase='侧边已恢复操作，后牛继续通过'
    frame=Image.new('RGB',(1000,810),(18,26,31));frame.paste(Image.open(path).convert('RGB'),(0,90));draw=ImageDraw.Draw(frame)
    draw.text((18,8),'玩家侧飞已接入：后续牛从旁边通过',font=font,fill='white')
    draw.text((18,47),'正式五牛接触与权威时间驱动 / 正式主角View / 隔离录制',font=small,fill=(210,220,225))
    draw.rectangle((0,765,1000,810),fill=(18,26,31))
    draw.text((18,776),f'{i*.05:.2f}s | {phase} | 生命 {row["health"]}/100',font=small,fill='white')
    frames.append(frame)
assert len(frames)==100 and len(hold)==10
assert min(int(row['health']) for row in trace)==75 and int(trace[-1]['health'])==75
assert float(trace[-1]['remaining'])==0 and float(trace[-1]['tailX'])>float(trace[-1]['playerX'])+2
assert abs(float(trace[-1]['playerX'])-float(trace[0]['playerX'])-1.2)<.001
assert abs(float(trace[-1]['playerZ'])-float(trace[0]['playerZ'])-3*sqrt(.84))<.001
for field in ('playerX','playerZ'):
    assert max(float(row[field]) for row in hold)-min(float(row[field]) for row in hold)<.0001
frames[0].save(output/'herd-side-applied.gif',save_all=True,append_images=frames[1:],duration=50,loop=0,optimize=False)
frames[42].save(output/'herd-side-applied.png')
with Image.open(output/'herd-side-applied.gif') as result:
    assert result.n_frames==100
    assert sum(result.seek(i) or result.info['duration'] for i in range(result.n_frames))==5000
(output/'authority-trace.csv').write_text((source/'authority-trace.csv').read_text(encoding='utf-8-sig'),encoding='utf-8')
print('Verified 100 frames / 5s; forward 1.2m, lateral 2.75m; grounded 10 frames; health stays75 and tail passes; recovered.')
