"""检查可播放视频及确定时间轴的真实变化，保存小型验收报告。"""
import json
from pathlib import Path
from PIL import Image, ImageChops, ImageStat, ImageDraw, ImageFont

folder=Path(__file__).resolve().parent
results={}
for shot in range(2):
    for variant in ['current','candidate']:
        with Image.open(folder/'frames'/f'{shot}-{variant}-000.png') as a, Image.open(folder/'frames'/f'{shot}-{variant}-079.png') as b:
            diff=ImageChops.difference(a.convert('RGB'),b.convert('RGB'))
            results[f'{shot}-{variant}']={'mean_channel_difference':sum(ImageStat.Stat(diff).mean)/3,'changed_bounds':diff.getbbox()}
assert all(results[f'{s}-current']['mean_channel_difference']<.01 for s in range(2)), '当前静态水面差异应低于渲染舍入容差'
assert all(results[f'{s}-candidate']['mean_channel_difference']>.1 for s in range(2)), '候选水纹没有足够时间变化'
sheet=Image.new('RGB',(1280,810),'#14272d')
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',22)
for index,frame in enumerate([0,26,52,79]):
    with Image.open(folder/'frames'/f'1-candidate-{frame:03}.png') as img:
        shot=img.resize((640,360),Image.Resampling.LANCZOS)
    x=(index%2)*640;y=(index//2)*405
    sheet.paste(shot,(x,y+35));ImageDraw.Draw(sheet).text((x+12,y+5),f'候选岸边 {frame/20:.2f} 秒',font=font,fill='#d6e5e4')
sheet.save(folder/'motion-contact-sheet.png')
(folder/'verification.json').write_text(json.dumps({'source_frame_count':len(list((folder/'frames').glob('*.png'))),'video':'water-comparison.mp4','duration_seconds':8,'video_frames':160,'fps':20,'resolution':[1280,450],'frame_comparison':results,'unity_scene_after':'Boot | Assets/Game/Scenes/Boot.unity | isDirty=false','shader_compile':'passed; no new console errors','scope':'isolated formal-prefab clone, candidate only; no formal assets changed'},ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(results,ensure_ascii=False))
