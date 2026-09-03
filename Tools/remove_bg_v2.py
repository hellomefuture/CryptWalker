# -*- coding: utf-8 -*-
"""
批量抠图 v2：chroma key（指定背景色 + 容差）
不依赖 rembg，对暗黑像素风/小尺寸/低对比素材更稳。

策略：把每个像素的 RGB 与"参考背景色"算欧氏距离，
距离 > 阈值 → 保留（前景）；距离 <= 阈值 → 去掉（背景 alpha=0）。
对每个 .jpg 自动取"四角平均色"作为背景色（你的素材背景是单一暗灰）。

输入：F:/Unity/UnityProject/CryptWalker/Assets/Art/**/*.jpg
输出：F:/Unity/UnityProject/CryptWalker/Tools/output_pngs/同名 .png
"""

import sys
import math
from pathlib import Path
from PIL import Image

ROOT = Path(r"F:\Unity\UnityProject\CryptWalker\Assets\Art")
OUT_DIR = Path(r"F:\Unity\UnityProject\CryptWalker\Tools\output_pngs")

# 容差：值越大，抠得越多（更多像素变透明）。值越小，抠得越少（保留更多细节但留背景色）
# 暗黑像素风素材背景是 #2a2a2a，角色高光可达 #d0d0d0
# 距离阈值 60 对应"灰度差 ~50"，基本能切分但保留边缘
THRESHOLD = 60.0

# 边缘羽化半径：让抠图边缘平滑 1~2 像素（避免硬边）
FEATHER_RANGE = 20.0  # 距离阈值±此范围 = 渐变 alpha


def sample_bg_color(img: Image.Image) -> tuple:
    """取四角各 5x5 像素的均值作为背景色参考"""
    w, h = img.size
    rgb = img.convert("RGB")
    pixels = rgb.load()
    r_sum = g_sum = b_sum = count = 0
    for x in range(0, 5):
        for y in range(0, 5):
            r, g, b = pixels[x, y]
            r_sum += r; g_sum += g; b_sum += b; count += 1
            r, g, b = pixels[w-1-x, y]
            r_sum += r; g_sum += g; b_sum += b; count += 1
            r, g, b = pixels[x, h-1-y]
            r_sum += r; g_sum += g; b_sum += b; count += 1
            r, g, b = pixels[w-1-x, h-1-y]
            r_sum += r; g_sum += g; b_sum += b; count += 1
    return (r_sum // count, g_sum // count, b_sum // count)


def color_distance(r1, g1, b1, r2, g2, b2) -> float:
    """欧氏距离（RGB 空间）"""
    return math.sqrt((r1-r2)**2 + (g1-g2)**2 + (b1-b2)**2)


def process_one(jpg_path: Path) -> str:
    rel = jpg_path.relative_to(ROOT)
    out_path = OUT_DIR / rel.with_suffix(".png")
    out_path.parent.mkdir(parents=True, exist_ok=True)
    if out_path.exists():
        return "SKIP"
    try:
        with Image.open(jpg_path) as img_raw:
            bg = sample_bg_color(img_raw)
            img = img_raw.convert("RGBA")
            w, h = img.size
            src = img.load()
            dst = img.load()  # 原地改 alpha
            kept = removed = feathered = 0
            for y in range(h):
                for x in range(w):
                    r, g, b, a = src[x, y]
                    d = color_distance(r, g, b, *bg)
                    if d > THRESHOLD + FEATHER_RANGE:
                        # 完全前景
                        new_a = 255
                        kept += 1
                    elif d < THRESHOLD:
                        # 完全背景
                        new_a = 0
                        removed += 1
                    else:
                        # 边缘羽化（按距离线性插值）
                        ratio = (d - THRESHOLD) / FEATHER_RANGE
                        new_a = int(255 * ratio)
                        feathered += 1
                    dst[x, y] = (r, g, b, new_a)
            img.save(out_path, "PNG", optimize=True)
            size = out_path.stat().st_size
        pct_kept = 100.0 * kept / (w*h)
        pct_bg = 100.0 * removed / (w*h)
        return f"OK ({size//1024} KB, bg=({bg[0]},{bg[1]},{bg[2]}), 前景{pct_kept:.0f}%, 背景{pct_bg:.0f}%)"
    except Exception as e:
        return f"FAIL: {e}"


def main():
    if not ROOT.exists():
        print(f"[ERROR] 目录不存在: {ROOT}")
        sys.exit(1)
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    jpgs = sorted(ROOT.rglob("*.jpg"))
    if not jpgs:
        print(f"[INFO] {ROOT} 下没找到 .jpg")
        return
    print(f"[INFO] 找到 {len(jpgs)} 张 jpg（chroma key, 阈值 {THRESHOLD}±{FEATHER_RANGE}）\n")
    ok = skip = fail = 0
    for jpg in jpgs:
        rel = jpg.relative_to(ROOT)
        folder = rel.parent if rel.parent != Path(".") else "ROOT"
        print(f"[{folder}] {jpg.name:<30}", end="  ")
        result = process_one(jpg)
        if result == "SKIP":
            print("[SKIP]")
            skip += 1
        elif result.startswith("OK"):
            print(result)
            ok += 1
        else:
            print(f"[{result}]")
            fail += 1
    print(f"\n[结果] 成功 {ok} / 跳过 {skip} / 失败 {fail} / 总计 {len(jpgs)}")
    print(f"[输出] {OUT_DIR}")
    print(f"\n[提示] 如果某张过度抠/欠抠，调脚本顶部 THRESHOLD：")
    print(f"  THRESHOLD 越大 → 越多像素被当背景（抠得越多）")
    print(f"  THRESHOLD 越小 → 越少像素被当背景（保留更多）")


if __name__ == "__main__":
    main()
