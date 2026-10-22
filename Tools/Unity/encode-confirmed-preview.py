"""编码Unity隔离预览帧；字幕仅说明确认事项，不写入游戏HUD。"""
from pathlib import Path
import sys
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
kind = next((v for v in sys.argv if v in ("chest", "portal")), "drone")
formal = "--formal" in sys.argv
source = root / "Client/Temp/ConfirmedDynamicPreview" / (kind+"-formal" if formal else kind)
output_name = kind+"-applied.gif" if formal else kind + "-v1.gif"
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
    if kind == "chest":
        label = ("每轮清场：掉落一个金币宝箱，玩家仍可移动" if time < 1.5 else
                 "落地爆开：金币与金块散落，特效期间暂不可拾取" if time < 3.3 else
                 "散落效果结束后可以拾取；也可以直接离开放弃余下奖励")
    elif kind == "portal":
        label = ("整关尚未完成：中间没有传送门" if time < 1.5 else
                 "整关完成后：中部出现传送门（海岛是第四轮清场后）" if time < 4.2 else
                 "玩家走入传送门进入下一关；普通过轮仍使用地图通道")
    draw.rectangle((0, 0, image.width, 52), fill=(16, 28, 32))
    draw.text((25, 13), label, font=font, fill="white")
    draw.rectangle((0, image.height - 36, image.width, image.height), fill=(16, 28, 32))
    draw.text((25, image.height - 29), ("正式资源隔离实拍 · 时序演示，非正式对局 · 展示时间已压缩" if formal else "独立临时动态预览 · 非正式对局 · 展示时间已压缩"), font=small, fill=(189, 204, 210))
    frames.append(image.quantize(colors=192))
frames[0].save(target / output_name, save_all=True, append_images=frames[1:], duration=100, loop=0, disposal=2)
print(target / output_name)
