"""压缩仓库里的截图与图标，减小仓库体积。"""
import os
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOCS = os.path.join(ROOT, "docs")
ASSETS = os.path.join(ROOT, "src", "UwpManager", "Assets")


def optimize_screenshot(path, target_width=1200):
    im = Image.open(path).convert("RGB")
    if im.width > target_width:
        h = round(im.height * target_width / im.width)
        im = im.resize((target_width, h), Image.LANCZOS)
    # 量化到 256 色并抖动，UI 截图肉眼几乎无差别
    q = im.quantize(colors=256, method=Image.MEDIANCUT, dither=Image.FLOYDSTEINBERG)
    before = os.path.getsize(path)
    q.save(path, format="PNG", optimize=True)
    after = os.path.getsize(path)
    print(f"{os.path.basename(path):26} {before/1024:7.1f} KB -> {after/1024:7.1f} KB")


def main():
    total_before = total_after = 0
    for name in sorted(os.listdir(DOCS)):
        if not name.lower().endswith(".png"):
            continue
        p = os.path.join(DOCS, name)
        total_before += os.path.getsize(p)
        optimize_screenshot(p)
        total_after += os.path.getsize(p)

    # 图标源文件：仅压 1024 原图
    big = os.path.join(ASSETS, "app-1024.png")
    if os.path.exists(big):
        im = Image.open(big).convert("RGBA")
        before = os.path.getsize(big)
        im.save(big, format="PNG", optimize=True)
        print(f"{'app-1024.png':26} {before/1024:7.1f} KB -> {os.path.getsize(big)/1024:7.1f} KB")
        total_before += before
        total_after += os.path.getsize(big)

    print(f"\ndocs total: {total_before/1024:.0f} KB -> {total_after/1024:.0f} KB")


if __name__ == "__main__":
    main()
