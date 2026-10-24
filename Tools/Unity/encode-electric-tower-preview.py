"""编码隔离电塔对比帧，并标明候选、正式资源与演示相位。"""
from pathlib import Path
import sys
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
applied = "--applied" in sys.argv
source = root / ("Client/Temp/ElectricTowerApplied" if applied else "Client/Temp/ElectricTowerPreview")
target = root / ("Backup/Artifacts/ElectricTowerApplied-20261023" if applied else "Backup/Artifacts/ElectricTower-v1-20261023")
target.mkdir(parents=True, exist_ok=True)
files = sorted(source.glob("[0-9][0-9][0-9].png"))
assert len(files) == 170, f"预期170帧，实际{len(files)}"
font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 25)
small = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 19)
frames = []
for i, file in enumerate(files):
    frame = Image.open(file).convert("RGB")
    draw = ImageDraw.Draw(frame)
    draw.text((315, 14), "旧电塔对照" if applied else "当前正式电塔", font=font, fill="white", anchor="mt")
    draw.text((965, 14), "已接入正式电塔" if applied else "新候选（未接入）", font=font, fill="white", anchor="mt")
    draw.text((640, 52), "Unity 临时场景实拍 · 中间为正式主角 · 非实际对局录像", font=small, fill="#b8cdd5", anchor="mt")
    if i < 80:
        text, color = "安全 4 秒：塔上熄弧，绿灯亮起", "#adf5bf"
    elif i < 110:
        text, color = "预警蓄能 1.5 秒：电流向上聚集，塔顶电团逐渐增强", "#ffba99"
    else:
        text, color = "通电 3 秒：塔顶分叉放电，危险范围沿用现有规则", "#9fe8ff"
    draw.rectangle((0, 642, 1280, 720), fill="#1c3039")
    draw.text((640, 657), text, font=font, fill=color, anchor="mt")
    if i in (40, 103, 135):
        frame.save(target / {40: "safe.png", 103: "charging.png", 135: "active.png"}[i])
    frames.append(frame.quantize(colors=192))
output = target / ("tower-applied.gif" if applied else "tower-comparison.gif")
frames[0].save(output, save_all=True, append_images=frames[1:], duration=50, loop=0, disposal=2)
with Image.open(output) as result:
    # GIF会合并安全阶段完全相同的帧；应核对总时长及全部原始画面。
    assert sum(result.seek(i) or result.info["duration"] for i in range(result.n_frames)) == 8500
    assert result.info.get("loop") == 0
    actual_frames = result.n_frames
print(f"{output}\n170源帧 / GIF {actual_frames}帧 / 8.5秒 / {output.stat().st_size} bytes")
