# -*- coding: utf-8 -*-
"""
程序化生成环境道具：门、障碍物、装饰（地砖变体）
风格：与 tile_ground / tile_wall 一致的暗黑像素风（深色 + 暖色高光），纯 #808080 灰底，便于后续 chroma key 抠图。
输出：F:/Unity/UnityProject/CryptWalker/Assets/Art/environment/raw/*.jpg (RGB, 灰底)
"""
from PIL import Image, ImageDraw
import math
import os
import random

OUT = r"F:\Unity\UnityProject\CryptWalker\Assets\Art\environment\raw"
os.makedirs(OUT, exist_ok=True)

TILE = 64
BG = (0, 0, 0)  # 纯黑底，chroma key 用更稳的近二值化（0,0,0 离任意彩色都很远）

# 暗黑像素调色板（与已有 tile 系列同调）
WALL_DARK   = (58, 52, 46)
WALL_MID    = (78, 70, 60)
WALL_LIGHT  = (104, 92, 78)
MOSS        = (60, 90, 50)
WOOD_DARK   = (62, 42, 28)
WOOD_MID    = (96, 68, 44)
WOOD_LIGHT  = (138, 102, 72)
IRON_DARK   = (40, 42, 48)
IRON_MID    = (70, 72, 78)
IRON_LIGHT  = (110, 112, 118)
RUST        = (122, 64, 36)
SHADOW      = (24, 22, 20)


def new_canvas():
    img = Image.new("RGB", (TILE, TILE), BG)
    return img, ImageDraw.Draw(img)


def save(img, name):
    path = os.path.join(OUT, name + ".jpg")
    img.save(path, quality=92)
    print(f"  -> {path}")


# ===================== 1) door =====================
def make_door():
    """俯视木门：木板条 + 铁钉 + 顶视拱门石框。中心放置，便于贴 1 格 Tile。"""
    img, d = new_canvas()
    # 拱门石外框（俯视为 8x12 的矩形，门嵌在中间）
    # 顶视下，门是横置的长方形（player 通过的开口）
    # 我们画一块厚木板作"门体"，周围一圈石框阴影
    # 门体 36x52 居中
    body_x0, body_y0 = 14, 6
    body_x1, body_y1 = 50, 58
    # 阴影
    d.rectangle([body_x0+2, body_y0+2, body_x1+2, body_y1+2], fill=(40, 36, 32))
    # 门板底色
    d.rectangle([body_x0, body_y0, body_x1, body_y1], fill=WOOD_MID)
    # 木纹：纵向暗条
    for x in (18, 24, 30, 36, 42, 46):
        d.line([(x, body_y0+2), (x, body_y1-2)], fill=WOOD_DARK, width=1)
    # 高光（左上→右下）
    d.line([(body_x0+1, body_y0+1), (body_x1-1, body_y0+1)], fill=WOOD_LIGHT)
    d.line([(body_x0+1, body_y0+1), (body_x0+1, body_y1-1)], fill=WOOD_LIGHT)
    # 铁钉
    for (x, y) in [(body_x0+2, body_y0+2), (body_x1-3, body_y0+2),
                   (body_x0+2, body_y1-3), (body_x1-3, body_y1-3)]:
        d.rectangle([x, y, x+2, y+2], fill=IRON_DARK)
        d.point((x+1, y+1), fill=IRON_LIGHT)
    # 中央铁横条
    d.rectangle([body_x0+1, 30, body_x1-1, 34], fill=IRON_MID)
    d.rectangle([body_x0+1, 30, body_x1-1, 31], fill=IRON_LIGHT)
    # 门嵌在墙里：保留门体本身；四周用 1px 中灰描边示意门框，不画大块深色（避免被 chroma key 误判为前景/背景）
    for (x0, y0, x1, y1) in [(body_x0-1, body_y0-1, body_x1+1, body_y1+1)]:
        d.rectangle([x0, y0, x1, y1], outline=(96, 90, 80))
    save(img, "door")


# ===================== 2) prop_obstacle =====================
def make_obstacle():
    """可破坏木箱：俯视 4 角 + 中心铁钉。"""
    img, d = new_canvas()
    # 箱体 44x44 居中
    bx0, by0 = 10, 10
    bx1, by1 = 54, 54
    # 阴影
    d.rectangle([bx0+2, by0+2, bx1+2, by1+2], fill=SHADOW)
    # 主体
    d.rectangle([bx0, by0, bx1, by1], fill=WOOD_MID)
    # 板缝
    d.line([(bx0, by0+22), (bx1, by0+22)], fill=WOOD_DARK)
    d.line([(bx0, by0+23), (bx1, by0+23)], fill=WOOD_LIGHT)
    d.line([(32, by0+1), (32, by0+22)], fill=WOOD_DARK)
    d.line([(33, by0+1), (33, by0+22)], fill=WOOD_LIGHT)
    # 木纹
    d.line([(bx0+5, by0+5), (bx0+5, by1-5)], fill=WOOD_DARK)
    d.line([(bx1-6, by0+5), (bx1-6, by1-5)], fill=WOOD_DARK)
    # 高光
    d.line([(bx0+1, by0+1), (bx1-1, by0+1)], fill=WOOD_LIGHT)
    d.line([(bx0+1, by0+1), (bx0+1, by1-1)], fill=WOOD_LIGHT)
    # 中心铁钉
    d.rectangle([30, 30, 34, 34], fill=IRON_DARK)
    d.point((31, 31), fill=IRON_LIGHT)
    d.point((33, 33), fill=RUST)
    # 边缘磨损
    d.point((bx1-2, by1-2), fill=SHADOW)
    d.point((bx0+2, by1-2), fill=SHADOW)
    save(img, "prop_obstacle")


# ===================== 3) 顺手做几个可选：柱子、碎石、火把底座 =====================
def make_pillar():
    img, d = new_canvas()
    cx, cy = TILE//2, TILE//2
    # 圆形石柱
    r = 18
    d.ellipse([cx-r+2, cy-r+2, cx+r+2, cy+r+2], fill=SHADOW)
    d.ellipse([cx-r, cy-r, cx+r, cy+r], fill=WALL_MID)
    d.ellipse([cx-r+2, cy-r+2, cx+r-4, cy+r-4], fill=WALL_LIGHT)
    d.ellipse([cx-r+6, cy-r+6, cx+r-8, cy+r-8], fill=WALL_DARK)
    d.point((cx-r+1, cy), fill=SHADOW)
    d.point((cx+r-1, cy), fill=WOOD_LIGHT)
    save(img, "prop_pillar")


def make_rubble():
    img, d = new_canvas()
    # 散布的碎石
    rng = random.Random(7)
    stones = [(16, 20, 5), (40, 16, 4), (30, 36, 6), (48, 44, 5), (12, 48, 4), (52, 30, 3)]
    for (x, y, r) in stones:
        d.ellipse([x-r+1, y-r+1, x+r+1, y+r+1], fill=SHADOW)
        d.ellipse([x-r, y-r, x+r, y+r], fill=WALL_MID)
        d.ellipse([x-r+1, y-r+1, x+r-2, y+r-2], fill=WALL_LIGHT)
        d.point((x-r+1, y), fill=SHADOW)
    save(img, "prop_rubble")


if __name__ == "__main__":
    print("[gen_env_props] generating...")
    make_door()
    make_obstacle()
    make_pillar()
    make_rubble()
    print("done.")
