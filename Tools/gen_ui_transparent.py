# -*- coding: utf-8 -*-
"""
针对 ui/raw/*.jpg 批量抠图：背景为纯白 (255,255,255)，用近二值化阈值抠掉。
与 Tools/remove_bg_v2.py 不同：背景固定为白色，THRESHOLD/ FEATHER 拉开。
输出：Assets/Art/ui/<name>.png（与 raw/ 同级）
"""
import math
from pathlib import Path
from PIL import Image

RAW = Path(r"F:\Unity\UnityProject\CryptWalker\Assets\Art\ui\raw")
OUT = Path(r"F:\Unity\UnityProject\CryptWalker\Assets\Art\ui")

# 背景固定纯白，距离 (255,255,255) 越远越像前景
# 白色到中灰 (192,192,192) 的距离 ≈ sqrt(63²·3) ≈ 109
# 因此 THRESHOLD 取 30，可把几乎所有"灰白过渡"当成背景
THRESHOLD = 30.0
FEATHER_RANGE = 5.0


def color_distance(r1, g1, b1, r2, g2, b2):
    return math.sqrt((r1-r2)**2 + (g1-g2)**2 + (b1-b2)**2)


def process(jpg: Path) -> Path:
    out = OUT / (jpg.stem + ".png")
    with Image.open(jpg) as raw:
        img = raw.convert("RGBA")
        w, h = img.size
        src = img.load()
        kept = removed = 0
        for y in range(h):
            for x in range(w):
                r, g, b, _ = src[x, y]
                d = color_distance(r, g, b, 255, 255, 255)
                if d > THRESHOLD + FEATHER_RANGE:
                    new_a = 255; kept += 1
                elif d < THRESHOLD:
                    new_a = 0;   removed += 1
                else:
                    new_a = int(255 * (d - THRESHOLD) / FEATHER_RANGE)
                src[x, y] = (r, g, b, new_a)
        img.save(out, "PNG", optimize=True)
    pct = 100.0 * kept / (w*h)
    print(f"  {jpg.name:<28} {w}x{h} 前景={pct:.1f}% -> {out.name} ({out.stat().st_size//1024} KB)")
    return out


def main():
    if not RAW.exists():
        print(f"[ERROR] {RAW} 不存在")
        return
    OUT.mkdir(parents=True, exist_ok=True)
    jpgs = sorted(RAW.glob("*.jpg"))
    print(f"[gen_ui_transparent] 处理 {len(jpgs)} 张 (白色背景, 阈值 {THRESHOLD}±{FEATHER_RANGE})\n")
    for jpg in jpgs:
        process(jpg)
    print("\n完成。\n下一步：Unity 菜单 Tools/CryptWalker/设置 environment PNG 为 Sprite —— 但 ui 目录也得设，")
    print("      我把那个工具扩展成可指定子目录。")


if __name__ == "__main__":
    main()
