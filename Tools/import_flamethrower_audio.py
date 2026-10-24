"""从已接入的纯火焰录音生成独立单声道喷火声，不修改原录音或 meta。"""
import argparse
import array
from pathlib import Path
import sys
import wave


def convert(source, destination, duration=None):
    """循环保留完整火焰录音；起声只裁短火焰，不混入喷气、机械或水声。"""
    with wave.open(str(source), "rb") as clip:
        if clip.getsampwidth() != 2 or clip.getnchannels() not in (1, 2):
            raise ValueError("输入必须为单声道或双声道 PCM16")
        channels = clip.getnchannels()
        rate = clip.getframerate()
        samples = array.array("h", clip.readframes(clip.getnframes()))
    if sys.byteorder != "little":
        samples.byteswap()
    full = [sum(samples[i:i + channels]) / channels for i in range(0, len(samples), channels)]
    # 起声和循环共用整段录音的增益，不能把短片段的安静燃烧强行放大成冲击声。
    peak = max(map(abs, full), default=0)
    scale = .6 * 32767 / peak if peak else 1
    count = len(full) if duration is None else min(len(full), int(rate * duration))
    mono = array.array("h")
    for i in range(count):
        gain = 1 if duration is None else min(1, i / (rate * .006), (count - 1 - i) / (rate * .025))
        mono.append(round(full[i] * scale * gain))
    if sys.byteorder != "little":
        mono.byteswap()
    with wave.open(str(destination), "wb") as clip:
        clip.setparams((1, 2, rate, count, "NONE", "not compressed"))
        clip.writeframes(mono.tobytes())


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    root = Path(__file__).resolve().parents[1]
    parser.add_argument("source", type=Path, nargs="?", default=root / "Client/Assets/Game/Audio/Sound/AmbienceFire.wav", help="纯火焰 PCM16 WAV")
    parser.add_argument("--output", type=Path, default=root / "Client/Assets/Game/Audio/Sound", help="输出目录；试听可写到 Backup/Artifacts")
    args = parser.parse_args()
    output = args.output
    output.mkdir(parents=True, exist_ok=True)
    convert(args.source, output / "ArcadeFireFlameLoop.wav")
    convert(args.source, output / "ArcadeFireFlameStart.wav", .22)
