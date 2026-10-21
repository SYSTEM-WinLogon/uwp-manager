"""生成 UWP 管家 应用图标 (Fluent 风格)。"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter

OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "src", "UwpManager", "Assets")
os.makedirs(OUT_DIR, exist_ok=True)

S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

# ---- 背景：圆角方形 + 垂直渐变 ----
grad = Image.new("RGBA", (S, S))
gd = ImageDraw.Draw(grad)
top = (0x63, 0xC7, 0xFF)
bot = (0x00, 0x54, 0xA6)
for y in range(S):
    t = y / (S - 1)
    c = tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3))
    gd.line([(0, y), (S, y)], fill=c + (255,))

# 右上角柔光
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse([int(S * 0.35), int(-S * 0.45), int(S * 1.25), int(S * 0.45)],
                             fill=(255, 255, 255, 46))
glow = glow.filter(ImageFilter.GaussianBlur(S * 0.08))
grad = Image.alpha_composite(grad, glow)

mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=int(S * 0.225), fill=255)
img.paste(grad, (0, 0), mask)

# ---- 前景：2x2 应用磁贴 ----
fg = Image.new("RGBA", (S, S), (0, 0, 0, 0))
fd = ImageDraw.Draw(fg)

tile = int(S * 0.150)
gap = int(S * 0.058)
grid_w = tile * 2 + gap
ox = int((S - grid_w) / 2) - int(S * 0.055)
oy = int((S - grid_w) / 2) - int(S * 0.055)
r = int(tile * 0.26)
alphas = {(0, 0): 255, (0, 1): 232, (1, 0): 232}
for (row, col), alpha in alphas.items():
    x = ox + col * (tile + gap)
    y = oy + row * (tile + gap)
    fd.rounded_rectangle([x, y, x + tile, y + tile], radius=r, fill=(255, 255, 255, alpha))

# ---- 前景：放大镜（占据第四格）----
cx = ox + tile + gap + tile // 2 + int(S * 0.008)
cy = oy + tile + gap + tile // 2 + int(S * 0.008)
rad = int(tile * 0.46)
stroke = int(tile * 0.24)

# 放大镜底部阴影，让它与磁贴分离
sh = Image.new("RGBA", (S, S), (0, 0, 0, 0))
sd = ImageDraw.Draw(sh)
sd.ellipse([cx - rad - stroke // 2, cy - rad - stroke // 2, cx + rad + stroke // 2, cy + rad + stroke // 2],
           outline=(0, 30, 60, 70), width=stroke)
sh = sh.filter(ImageFilter.GaussianBlur(S * 0.010))
fg = Image.alpha_composite(fg, sh)
fd = ImageDraw.Draw(fg)

# 镜片内部挖空（半透明白色玻璃）
fd.ellipse([cx - rad + stroke // 2, cy - rad + stroke // 2, cx + rad - stroke // 2, cy + rad - stroke // 2],
           fill=(255, 255, 255, 36))
fd.ellipse([cx - rad, cy - rad, cx + rad, cy + rad], outline=(255, 255, 255, 255), width=stroke)

# 手柄
ang = math.radians(45)
x1 = cx + (rad + stroke * 0.15) * math.cos(ang)
y1 = cy + (rad + stroke * 0.15) * math.sin(ang)
x2 = cx + (rad + stroke * 1.55) * math.cos(ang)
y2 = cy + (rad + stroke * 1.55) * math.sin(ang)
fd.line([x1, y1, x2, y2], fill=(255, 255, 255, 255), width=stroke)
for (px, py) in [(x1, y1), (x2, y2)]:
    fd.ellipse([px - stroke / 2, py - stroke / 2, px + stroke / 2, py + stroke / 2], fill=(255, 255, 255, 255))

img = Image.alpha_composite(img, fg)

# ---- 输出 ----
img.save(os.path.join(OUT_DIR, "app-1024.png"))
img.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT_DIR, "app-256.png"))

sizes = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (96, 96), (128, 128), (256, 256)]
img.save(os.path.join(OUT_DIR, "app.ico"), format="ICO", sizes=sizes)
print("icon written:", os.path.join(OUT_DIR, "app.ico"))
print("size:", os.path.getsize(os.path.join(OUT_DIR, "app.ico")))
