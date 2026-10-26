from pathlib import Path
import html,json
root=Path(__file__).parent
parts=['''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>三场四轮正式回读</title><style>body{margin:0;background:#17222b;color:#e6edf2;font:16px/1.6 system-ui,sans-serif}main{max-width:1500px;margin:auto;padding:28px}h1{font-size:28px}section{margin:36px 0}article{padding:16px;background:#24323d;border-radius:12px;margin:16px 0}.pair{display:grid;grid-template-columns:1fr 1fr;gap:12px}figure{margin:0}img{width:100%;display:block;border-radius:6px}figcaption{padding:6px 0}a{color:#b9d9fc}@media(max-width:700px){.pair{grid-template-columns:1fr}}</style><main><h1>三场四轮正式回读</h1><p>2026-10-26 · 要塞庭院、锦鲤桥庭、终局巢穴均已按确认版本接入。左侧为原采用源临时几何，右侧为从正式持久资源经过实际轮级加载流程读回的画面；都使用同一正式主角、待机与武器，同机位、同灯光。静态环境无新动画或玩法。</p><p>十二轮逐面几何、法线、面材质及阴影设置摘要一致；真实障碍足迹和外围保护通过。15对图片中7对逐像素一致，其余每张最多120/1,920,000个像素不同。<a href="README.md">验证说明</a> · <a href="pixel-comparison.json">像素明细</a></p>''']
for folder,label in [('Fort','要塞庭院 · 配置7'),('Koi','锦鲤桥庭 · 配置10'),('Nest','终局巢穴 · 配置20')]:
    parts.append(f'<section><h2>{label}</h2>')
    for stem,title in [('round-'+str(i),'第'+str(i)+'轮全景') for i in range(1,5)]+[('round-1-close','第一轮正式主角尺寸近景')]:
        parts.append(f'<article><h3>{title}</h3><div class="pair">')
        for kind,caption in [('accepted','原采用源'),('applied','正式持久资源回读')]:
            file=f'{folder}/{stem}-{kind}.png';parts.append(f'<figure><a href="{file}"><img loading="lazy" src="{file}" alt="{label} {title} {caption}"></a><figcaption>{caption}</figcaption></figure>')
        parts.append('</div></article>')
    parts.append('</section>')
parts.append('</main></html>');(root/'index.html').write_text(''.join(parts),encoding='utf-8')
