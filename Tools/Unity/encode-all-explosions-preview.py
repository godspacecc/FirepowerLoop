"""编码9类隔离爆炸对比，含两种补齐候选，未接入正式资源。"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root=Path(__file__).resolve().parents[2]
source=root/'Client/Temp/AllExplosions-v1-20261023'
out=root/'Docs/Plans/Artifacts/AllExplosions-v1-20261023'
out.mkdir(parents=True,exist_ok=True)
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',24)
small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',18)
entries=[
 ('energy','镭射能量爆炸','去掉放射火星，补回紧凑青蓝爆心；烟尾保持关闭'),
 ('barrel','紫桶爆炸','去掉放射火星与桶身碎片，保留紫色爆心、紫烟'),
 ('bomb','Q键清场炸弹','去掉飞散火星、碎片与放射光条，保留爆心、冲击波和地面薄尘'),
 ('drone','无人机自爆','去掉飞散火星，保留橙色爆心和烟雾'),
 ('cluster-main','紫色集束榴弹：主爆','去掉飞散火星，保留主爆、烟雾和扩张圈；使用当前工作区新模板'),
 ('cluster-child','紫色集束榴弹：子爆','去掉飞散火星，保留子爆、烟雾和扩张圈；使用当前工作区新模板'),
 ('enemy-death','敌人被炸死：附加死亡反馈','去掉飞散血肉碎屑，保留血雾和落地尘；此段单独展示粒子，不演示炸飞动作'),
 ('decoy','补齐候选：诱饵炸弹','当前没有独立爆心；候选复用已采用橙色主体，不加飞散火星，仅示意反馈'),
 ('orbit-barrel','补齐候选：玩家环绕爆桶','当前没有独立爆心；候选复用已采用橙色主体，触发后仍保留未耗尽桶体，仅示意反馈')]
clips={}
poster=Image.new('RGB',(1000,len(entries)*390),(14,23,26))
for index,(name,title,note) in enumerate(entries):
    frames=[]
    for frame in range(48):
        paths=[source/name/f'{variant}-{frame:03}.png' for variant in range(2)]
        assert all(p.exists() for p in paths),paths
        image=Image.new('RGB',(1200,545),(14,23,26));draw=ImageDraw.Draw(image)
        draw.text((18,6),f'{title}  |  同步对比 / 原速',font=font,fill='white')
        missing=name in ['decoy','orbit-barrel']
        draw.text((18,43),'左：当前无独立爆心' if missing else '左：当前效果',font=small,fill=(246,206,129))
        draw.text((618,43),'右：补齐爆心候选' if missing else '右：收掉外围飞散候选',font=small,fill=(137,238,198))
        for variant,path in enumerate(paths):image.paste(Image.open(path).convert('RGB'),(variant*600,74))
        draw.line((600,74,600,494),fill=(102,126,130),width=2)
        draw.text((18,502),note,font=small,fill=(220,229,229))
        draw.text((18,526),f'正式模板克隆 / 隔离场景候选 / 无声 / 未正式接入     {frame/20:.2f}s',font=small,fill=(159,182,186))
        frames.append(image.convert('P',palette=Image.Palette.ADAPTIVE,colors=192))
    clips[name]=frames
    frames[0].save(out/f'{name}.gif',save_all=True,append_images=frames[1:],duration=50,loop=0,optimize=False)
    frames[9].convert('RGB').save(out/f'{name}.png')
    thumb=frames[9].convert('RGB').resize((1000,390),Image.Resampling.LANCZOS);poster.paste(thumb,(0,index*390))
    with Image.open(out/f'{name}.gif') as check:
        assert check.n_frames==48
        assert sum(check.seek(i) or check.info['duration'] for i in range(check.n_frames))==2400
    print(f'{name}: 48 frames / 2.4 s',flush=True)
for group,names in [('weapons',['energy','drone','cluster-main','cluster-child']),('scene-and-death',['barrel','bomb','enemy-death']),('missing-effects',['decoy','orbit-barrel'])]:
    sequence=[f for name in names for f in clips[name]]
    sequence[0].save(out/f'{group}.gif',save_all=True,append_images=sequence[1:],duration=50,loop=0,optimize=False)
    with Image.open(out/f'{group}.gif') as check:
        assert check.n_frames==len(sequence)
        duration=sum(check.seek(i) or check.info['duration'] for i in range(check.n_frames))
        assert duration==len(sequence)*50
    print(f'{group}: {len(sequence)} frames / {duration/1000}s',flush=True)
poster.save(out/'overview.png')
(out/'index.html').write_text('<!doctype html><meta charset="utf-8"><title>其他爆炸精简候选v1</title><style>body{background:#0e171a;color:#e5f0f1;font:16px sans-serif;max-width:1200px;margin:24px auto}img{width:100%}p{color:#b1c4c8}section{margin-bottom:32px}</style><h1>其他爆炸：当前 / 收掉外围飞散</h1><p>正式模板克隆，隔离场景动态候选；相同相机、角色参照与随机种子。无声，不是真人对局，未修改正式资源。每项2.4秒循环。</p>'+''.join(f'<section><h2>{title}</h2><p>{note}</p><img src="{name}.gif"></section>' for name,title,note in entries),encoding='utf-8')
print(out,flush=True)
