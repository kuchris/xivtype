"""Draws images/icon.png (512x512): white key cap, amber bolt, text cursor on charcoal."""
from pathlib import Path
from PIL import Image, ImageDraw

S = 4  # supersampling factor
N = 512 * S
BG, KEY, BOLT = (30, 33, 38), (244, 245, 247), (245, 166, 35)

img = Image.new("RGB", (N, N), BG)
d = ImageDraw.Draw(img)
u = lambda v: round(v * S)

# Key cap with a slightly taller lower lip, flat two-tone.
d.rounded_rectangle((u(92), u(128), u(372), u(408)), radius=u(52), fill=KEY)
d.rounded_rectangle((u(116), u(148), u(348), u(364)), radius=u(36), fill=(222, 225, 230))

# Lightning bolt centred on the key face.
cx, cy = 232, 256
bolt = [(cx + 22, cy - 86), (cx - 44, cy + 10), (cx - 4, cy + 10),
        (cx - 22, cy + 86), (cx + 44, cy - 10), (cx + 4, cy - 10)]
d.polygon([(u(x), u(y)) for x, y in bolt], fill=BOLT)

# Text cursor to the right of the key.
d.rounded_rectangle((u(404), u(150), u(430), u(386)), radius=u(13), fill=KEY)

out = Path(__file__).resolve().parents[1] / "images" / "icon.png"
img.resize((512, 512), Image.LANCZOS).save(out)
print(out)
