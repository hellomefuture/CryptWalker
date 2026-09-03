# -*- coding: utf-8 -*-
"""
批量抠图：直接调 rembg Python API（不依赖 CLI 启动脚本）
- 输入：F:/Unity/UnityProject/CryptWalker/Assets/Art/**/*.jpg
- 输出：同名 .png（透明底）
- 已处理过的不再处理
- 用 u2netp 模型（轻量、约 4.4MB、首次会下载）
"""

import os
import sys
from pathlib import Path

# 关键：把 rembg 的模型缓存目录改到工程内（沙箱可写）
os.environ.setdefault("U2NET_HOME", str(Path("F:/Unity/UnityProject/CryptWalker/Tools/.u2net")))
os.environ.setdefault("XDG_CACHE_HOME", str(Path("F:/Unity/UnityProject/CryptWalker/Tools/.cache")))

from PIL import Image
from rembg import remove, new_session

ROOT = Path("F:/Unity/UnityProject/CryptWalker/Assets/Art")
# 输出到 Tools/output_pngs/，再由用户复制到 Assets/Art/
OUT_DIR = Path("F:/Unity/UnityProject/CryptWalker/Tools/output_pngs")
SESSION = new_session("u2netp")  # 全局 session 复用，避免每张图都加载模型

def process_one(jpg_path: Path) -> str:
    # 保持原文件夹结构输出到 OUT_DIR
    rel = jpg_path.relative_to(ROOT)
    out_path = OUT_DIR / rel.with_suffix(".png")
    out_path.parent.mkdir(parents=True, exist_ok=True)
    if out_path.exists():
        return "SKIP"
    try:
        with Image.open(jpg_path) as img:
            img = img.convert("RGBA")
            out = remove(img, session=SESSION)  # 关键：用 session=，不是 model_name=
            out.save(out_path, "PNG", optimize=True)
        size = out_path.stat().st_size
        return f"OK ({size//1024} KB)"
    except Exception as e:
        return f"FAIL: {e}"

def main():
    if not ROOT.exists():
        print(f"[ERROR] 目录不存在: {ROOT}")
        sys.exit(1)

    jpgs = sorted(ROOT.rglob("*.jpg"))
    if not jpgs:
        print(f"[INFO] {ROOT} 下没找到 .jpg")
        return

    print(f"[INFO] 找到 {len(jpgs)} 张 jpg，开始处理（u2netp）\n")
    ok = skip = fail = 0
    for jpg in jpgs:
        rel = jpg.relative_to(ROOT)
        folder = rel.parent if rel.parent != Path(".") else "ROOT"
        print(f"[{folder}] {jpg.name:<30}", end="  ")
        result = process_one(jpg)
        if result == "SKIP":
            print("[SKIP] 已有 png")
            skip += 1
        elif result.startswith("OK"):
            print(f"-> {jpg.stem}.png  {result}")
            ok += 1
        else:
            print(f"[{result}]")
            fail += 1

    print(f"\n[结果] 成功 {ok} / 跳过 {skip} / 失败 {fail} / 总计 {len(jpgs)}")

if __name__ == "__main__":
    main()
