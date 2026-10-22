"""编码Unity隔离预览帧；字幕仅说明确认事项，不写入游戏HUD。"""
from pathlib import Path
import sys
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
formal = "--formal" in sys.argv
source = root / ("Client/Temp/ConfirmedDynamicPreview/drone-formal" if formal else "Client/Temp/ConfirmedDynamicPreview/drone")
target = root / "Docs/Plans/Artifacts/ConfirmedDynamic-20261022"
target.mkdir(parents=True, exist_ok=True)
font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 23)
small = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 16)
frames = []
for index, file in enumerate(sorted(source.glob("*.png"))):
    image = Image.open(file).convert("RGB")
    draw = ImageDraw.Draw(image)
    time = index / 10
    label = ("普通：跟随玩家自动攻击" if time < 2 else
             "红强化：玩家与无人机同步增强" if time < 4 else
             "紫强化：同步增强；强化结束同步恢复" if time < 6 else
             "强化结束：恢复普通状态" if time < 7 else
             "临近到期有目标：冲撞后自毁爆炸一次" if time < 8.5 else
             "另一种情况——没有范围内目标：到期消失，不爆炸")
    draw.rectangle((0, 0, image.width, 52), fill=(16, 28, 32))
    draw.text((25, 13), label, font=font, fill="white")
    draw.rectangle((0, image.height - 36, image.width, image.height), fill=(16, 28, 32))
    draw.text((25, image.height - 29), ("正式资源隔离实拍 · 时序演示，非正式对局 · 展示时间已压缩" if formal else "独立临时动态预览 · 非正式对局 · 展示时间已压缩"), font=small, fill=(189, 204, 210))
    frames.append(image.quantize(colors=192))
frames[0].save(target / ("drone-applied.gif" if formal else "drone-v1.gif"), save_all=True, append_images=frames[1:], duration=100, loop=0, disposal=2)
print(target / ("drone-applied.gif" if formal else "drone-v1.gif"))
