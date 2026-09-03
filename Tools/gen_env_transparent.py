# -*- coding: utf-8 -*-
"""
针对性抠图：只处理 environment/raw/ 下的 4 张新道具图。
输出到 Assets/Art/environment/（与 tile_ground/tile_wall 同级），与已有 PNG 命名一致。
阈值/羽化与 remove_bg_v2.py 保持一致。
"""
import math
from pathlib import Path
from PIL import Image

RAW = Path(r"F:\Unity\UnityProject\CryptWalker\Assets\Art\environment\raw")
OUT = Path(r"F:\Unity\UnityProject\CryptWalker\Assets\Art\environment")

# 背景统一为纯黑 (0,0,0)：与任何前景像素的 RGB 距离都很大，二值化抠图极稳。
# 注意：生成端必须用 BG_BLACK 作为画布底色。
THRESHOLD = 30.0
FEATHER_RANGE = 5.0


def sample_bg_color(img: Image.Image):
    w, h = img.size
    rgb = img.convert("RGB")
    px = rgb.load()
    rs = gs = bs = n = 0
    for x in range(5):
        for y in range(5):
            for (X, Y) in [(x, y), (w-1-x, y), (x, h-1-y), (w-1-x, h-1-y)]:
                r, g, b = px[X, Y]
                rs += r; gs += g; bs += b; n += 1
    return (rs // n, gs // n, bs // n)


def color_distance(r1, g1, b1, r2, g2, b2):
    return math.sqrt((r1-r2)**2 + (g1-g2)**2 + (b1-b2)**2)


def process(jpg: Path) -> Path:
    out = OUT / (jpg.stem + ".png")
    with Image.open(jpg) as raw:
        bg = sample_bg_color(raw)
        img = raw.convert("RGBA")
        w, h = img.size
        src = img.load()
        kept = removed = 0
        for y in range(h):
            for x in range(w):
                r, g, b, _ = src[x, y]
                d = color_distance(r, g, b, *bg)
                if d > THRESHOLD + FEATHER_RANGE:
                    new_a = 255; kept += 1
                elif d < THRESHOLD:
                    new_a = 0;   removed += 1
                else:
                    new_a = int(255 * (d - THRESHOLD) / FEATHER_RANGE)
                src[x, y] = (r, g, b, new_a)
        img.save(out, "PNG", optimize=True)
    pct = 100.0 * kept / (w*h)
    print(f"  {jpg.name:<24} bg={bg} 前景={pct:.0f}% -> {out.name} ({out.stat().st_size//1024} KB)")
    return out


def main():
    if not RAW.exists():
        print(f"[ERROR] {RAW} 不存在")
        return
    OUT.mkdir(parents=True, exist_ok=True)
    jpgs = sorted(RAW.glob("*.jpg"))
    print(f"[gen_env_transparent] 处理 {len(jpgs)} 张\n")
    for jpg in jpgs:
        process(jpg)
    print("\n完成。下一步：Unity 菜单 Tools/CryptWalker/Import Transparent Sprites（把 PNG 复制到工程并删除 raw/.jpg）。")


if __name__ == "__main__":
    main()
