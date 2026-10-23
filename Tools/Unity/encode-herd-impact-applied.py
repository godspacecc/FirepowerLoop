"""编码正式活动循环与正式View的隔离录制，并核对活动时钟轨迹。"""
import csv
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root=Path(__file__).resolve().parents[2]
source=root/'Client/Temp/HerdImpactApplied-20261023'
output=root/'Docs/Plans/Artifacts/HerdImpactApplied-20261023'
output.mkdir(parents=True,exist_ok=True)
trace=list(csv.DictReader((source/'authority-trace.csv').open(encoding='utf-8-sig')))
assert len(trace)==100
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',22)
small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',17)
frames=[]

def phase(remaining):
    if remaining<=0:return '可行动'
    if remaining>.75+1e-5:return '腾空飞行'
    if remaining>.25+1e-5:return '落地僵直0.5秒'
    return '起身恢复'

for i,path in enumerate(sorted((source/'frames').glob('frame-*.png'))):
    frame=Image.open(path).convert('RGB');result=Image.new('RGB',(1000,790),(18,26,31));result.paste(frame,(0,90));draw=ImageDraw.Draw(result)
    draw.text((18,8),'牛撞击飞已接入：腾空 → 落地僵直0.5秒 → 起身',font=font,fill='white')
    draw.text((18,45),'真实接触与权威时间驱动正式View / 隔离分道对比，保留头牛单次接触',font=small,fill=(205,218,224))
    draw.text((22,202),'存活丧尸',font=small,fill='white',stroke_width=2,stroke_fill=(25,35,30))
    draw.text((22,508),'玩家',font=small,fill='white',stroke_width=2,stroke_fill=(25,35,30))
    draw.rectangle((0,748,1000,790),fill=(18,26,31))
    row=trace[i]
    draw.text((22,755),f'{i*.05:.2f}s | 丧尸：{phase(float(row["remainingEnemy"]))} | 玩家：{phase(float(row["remainingPlayer"]))}',font=small,fill='white')
    frames.append(result)
assert len(frames)==100
frames[0].save(output/'herd-impact-applied.gif',save_all=True,append_images=frames[1:],duration=50,loop=0,optimize=False)
frames[42].save(output/'herd-impact-applied.png')
with Image.open(output/'herd-impact-applied.gif') as gif:
    assert gif.n_frames==100
    assert sum(gif.seek(i) or gif.info['duration'] for i in range(gif.n_frames))==5000
for suffix in ('Player','Enemy'):
    started=[row for row in trace if float(row['remaining'+suffix])>0]
    assert abs(float(started[0]['remaining'+suffix])-1.7)<1e-4
    assert abs(float(trace[-1][suffix.lower()+'X'])-float(trace[0][suffix.lower()+'X'])-3)<1e-3
    hold=[row for row in trace if phase(float(row['remaining'+suffix]))=='落地僵直0.5秒']
    assert len(hold)==10,(suffix,len(hold))
    assert max(float(row[suffix.lower()+'X']) for row in hold)-min(float(row[suffix.lower()+'X']) for row in hold)<1e-4
(output/'authority-trace.csv').write_text((source/'authority-trace.csv').read_text(encoding='utf-8-sig'),encoding='utf-8')
print('Verified: 100 frames, 20fps, 5s; actual authority moves both actors 3m and holds grounded for 10 frames / 0.5s.')
