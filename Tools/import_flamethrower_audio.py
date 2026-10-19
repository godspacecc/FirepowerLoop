"""从已授权的本地备用资源生成独立单声道喷火声，不导入资源库的工程数据或 meta。"""
import argparse
import array
from pathlib import Path
import sys
import wave


def convert(source, destination, duration=None):
    """循环保留完整四秒；点火裁成短声并在两端淡化，避免频繁点射拖尾。"""
    with wave.open(str(source), "rb") as clip:
        if clip.getsampwidth() != 2 or clip.getnchannels() != 2:
            raise ValueError("输入必须为双声道 PCM16")
        rate = clip.getframerate()
        samples = array.array("h", clip.readframes(clip.getnframes()))
    if sys.byteorder != "little":
        samples.byteswap()
    count = len(samples) // 2 if duration is None else min(len(samples) // 2, int(rate * duration))
    mono = array.array("h")
    for i in range(count):
        gain = 1 if duration is None else min(1, i / (rate * .006), (count - 1 - i) / (rate * .025))
        mono.append(round((samples[i * 2] + samples[i * 2 + 1]) * .5 * gain))
    # 备用录音较轻，峰值统一到 0.6，再配合运行时 0.65 增益接近现有枪声的音量。
    peak = max(map(abs, mono), default=0)
    if peak:
        mono = array.array("h", (round(sample * (.6 * 32767 / peak)) for sample in mono))
    if sys.byteorder != "little":
        mono.byteswap()
    with wave.open(str(destination), "wb") as clip:
        clip.setparams((1, 2, rate, count, "NONE", "not compressed"))
        clip.writeframes(mono.tobytes())


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="Gun 07 (Flamethrower) 目录")
    args = parser.parse_args()
    output = Path(__file__).resolve().parents[1] / "Client/Assets/Game/Audio/Sound"
    convert(args.source / "Gun 7 Loop.wav", output / "ArcadeFireFlameLoop.wav")
    convert(args.source / "Gun 7_1.wav", output / "ArcadeFireFlameStart.wav", .22)
