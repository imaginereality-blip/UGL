"""Generates the Replay icon (PNG + multi-size ICO). Run: python scripts/make_replay_icon.py"""
import math, os
from PIL import Image, ImageDraw

S = 2048
out = os.path.join(os.path.dirname(__file__), "..", "src", "UGL.App", "Assets", "Icon")

def lerp(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))

def gradient(c1, c2, diag=True):
    g = Image.new("RGB", (S, S))
    px = g.load()
    for y in range(S):
        for x in range(S):
            px[x, y] = lerp(c1, c2, (x + y) / (2 * S))
    return g

def mask_of(fn):
    m = Image.new("L", (S, S), 0)
    fn(ImageDraw.Draw(m))
    return m

cx = cy = S / 2
R, W = S * 0.30, S * 0.075
gap_start, gap_end = 235, 285  # degrees, PIL convention (clockwise from east)

def pt(deg, r=R):
    t = math.radians(deg)
    return cx + r * math.cos(t), cy + r * math.sin(t)

def ring(d):
    Ro = R + W / 2
    box = [cx - Ro, cy - Ro, cx + Ro, cy + Ro]
    d.arc(box, gap_end, gap_start + 360, fill=255, width=int(W))
    for deg in (gap_start,):  # round cap on the tail
        x, y = pt(deg)
        d.ellipse([x - W / 2, y - W / 2, x + W / 2, y + W / 2], fill=255)
    # arrowhead at the other end, pointing across the gap
    t = math.radians(gap_end)
    px, py = pt(gap_end)
    dx, dy = math.sin(t), -math.cos(t)
    nx, ny = -dy, dx
    L, hw = W * 2.1, W * 1.55
    d.polygon([(px + dx * L, py + dy * L), (px + nx * hw, py + ny * hw), (px - nx * hw, py - ny * hw)], fill=255)

def play(d):
    s = S * 0.17
    ox = S * 0.02  # optical centering
    d.polygon([(cx - s * 0.62 + ox, cy - s), (cx - s * 0.62 + ox, cy + s), (cx + s * 1.1 + ox, cy)], fill=255)

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
bg = gradient((34, 24, 72), (10, 10, 20)).convert("RGBA")
img.paste(bg, mask=mask_of(lambda d: d.rounded_rectangle([S * 0.04, S * 0.04, S * 0.96, S * 0.96], radius=S * 0.22, fill=255)))
img.paste(gradient((40, 220, 255), (170, 90, 255)).convert("RGBA"), mask=mask_of(ring))
img.paste(gradient((255, 255, 255), (255, 200, 120)).convert("RGBA"), mask=mask_of(play))

os.makedirs(out, exist_ok=True)
img.resize((512, 512), Image.LANCZOS).save(os.path.join(out, "replay.png"))
sizes = [16, 24, 32, 48, 64, 128, 256]
img.resize((256, 256), Image.LANCZOS).save(os.path.join(out, "replay.ico"), sizes=[(s, s) for s in sizes])
print("done")

# --- GitHub social preview (1280x640) ---
from PIL import ImageFont
W_, H_ = 1280, 640
ban = Image.new("RGB", (W_, H_))
bp = ban.load()
for y in range(H_):
    for x in range(W_):
        bp[x, y] = lerp((34, 24, 72), (10, 10, 20), (x + y) / (W_ + H_))
icon = img.resize((400, 400), Image.LANCZOS)
ban.paste(icon, (130, 120), icon)
d = ImageDraw.Draw(ban)
fb = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", 150)
fr = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 52)
d.text((570, 190), "Replay", font=fb, fill=(255, 255, 255))
d.text((578, 372), "Ultimate Game Launcher", font=fr, fill=(150, 215, 255))
docs = os.path.join(os.path.dirname(__file__), "..", "docs")
os.makedirs(docs, exist_ok=True)
ban.save(os.path.join(docs, "replay-social-preview.png"))
