# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
# README などの見出しの画像 (ロゴに合わせたステッカー風の SVG) を作り直す。
# Markdown の中の <img src=".../images/headings/....svg" alt="見出し"> を探し、alt の文字で画像を作る
# (見出しの文言は Markdown の alt を直せばよい)。ファイル名が title で始まるものは文書の題名用の大きな画像、
# s と数字で始まるもの (s01.svg) は小見出し用の小さな画像にする。
#   pip install fonttools
#   curl -L -o mplus.ttf https://cdn.jsdelivr.net/gh/google/fonts@main/ofl/mplusrounded1c/MPLUSRounded1c-ExtraBold.ttf
#   python3 docs/images/make-headings.py README.md CODE_OF_CONDUCT.md CONTRIBUTING.md THIRD-PARTY-NOTICES.md
# 文字は M PLUS Rounded 1c (SIL Open Font License 1.1) の字形を図形にして埋め込む (見る人の PC のフォントに左右されない)。
import math, os, re, sys
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen

font = TTFont(os.environ.get("MPLUS_FONT", "mplus.ttf"))
gs = font.getGlyphSet()
cmap = font.getBestCmap()
upm = font["head"].unitsPerEm
hmtx = font["hmtx"]

THEMES = {
    "blue": ("#3aaee8", "#7fd0f5"),
    "pink": ("#ff6f9f", "#ffa6c6"),
}
OUTLINE = "#7391ee"
DASH = "#ff8ab4"

def text_path(text, size, x0, baseline):
    s = size / upm
    pen = SVGPathPen(gs)
    x = x0
    for ch in text:
        g = cmap.get(ord(ch))
        if g is None:
            continue
        gs[g].draw(TransformPen(pen, (s, 0, 0, -s, x, baseline)))
        x += hmtx[g][0] * s
    return pen.getCommands(), x

def snowflake(cx, cy, r, color):
    parts = []
    for k in range(6):
        a = math.radians(90 + 60 * k)
        ex, ey = cx + r * math.cos(a), cy - r * math.sin(a)
        parts.append(f"M{cx:.1f} {cy:.1f}L{ex:.1f} {ey:.1f}")
        for t, b in ((0.55, 0.38),):
            bx, by = cx + r * t * math.cos(a), cy - r * t * math.sin(a)
            for d in (-1, 1):
                aa = a + d * math.radians(45)
                parts.append(f"M{bx:.1f} {by:.1f}L{bx + r * b * math.cos(aa):.1f} {by - r * b * math.sin(aa):.1f}")
    d = "".join(parts)
    return (f'<path d="{d}" stroke="{OUTLINE}" stroke-width="9" stroke-linecap="round" fill="none"/>'
            f'<path d="{d}" stroke="#ffffff" stroke-width="6" stroke-linecap="round" fill="none"/>'
            f'<path d="{d}" stroke="{color}" stroke-width="3" stroke-linecap="round" fill="none"/>')

def heading(text, theme, out, width=760, height=84, size=42):
    top, bottom = THEMES[theme]
    baseline = 58
    x0 = 66
    d, x_end = text_path(text, size, x0, baseline)
    width = max(width, int(x_end) + 120)
    dash_y = 62
    dash = (f'<path d="M{x_end + 18:.1f} {dash_y} H{width - 24}" stroke="{OUTLINE}" stroke-width="9" stroke-linecap="round"/>'
            f'<path d="M{x_end + 18:.1f} {dash_y} H{width - 24}" stroke="#ffffff" stroke-width="6" stroke-linecap="round"/>'
            f'<path d="M{x_end + 22:.1f} {dash_y} H{width - 28}" stroke="{DASH}" stroke-width="3" stroke-linecap="round" stroke-dasharray="10 9"/>')
    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-label="{text}">
<title>{text}</title>
<defs><linearGradient id="g" x1="0" y1="{baseline - size * 0.9:.1f}" x2="0" y2="{baseline + 4}" gradientUnits="userSpaceOnUse">
<stop offset="0" stop-color="{top}"/><stop offset="1" stop-color="{bottom}"/></linearGradient></defs>
{snowflake(30, 42, 20, top)}
{dash}
<path d="{d}" fill="{OUTLINE}" stroke="{OUTLINE}" stroke-width="10" stroke-linejoin="round" transform="translate(0 2.5)"/>
<path d="{d}" fill="{OUTLINE}" stroke="{OUTLINE}" stroke-width="10" stroke-linejoin="round"/>
<path d="{d}" fill="#ffffff" stroke="#ffffff" stroke-width="6" stroke-linejoin="round"/>
<path d="{d}" fill="url(#g)"/>
</svg>
'''
    open(out, "w", encoding="utf-8").write(svg)

def title(text, out, size=58):
    """文書の題名: 大きな文字 + 左右の雪の結晶 + 下に破線。"""
    top, bottom = "#3aaee8", "#ff8ab4"
    baseline = 78
    x0 = 92
    d, x_end = text_path(text, size, x0, baseline)
    width = int(x_end) + 92
    height = 120
    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-label="{text}">
<title>{text}</title>
<defs><linearGradient id="g" x1="{x0}" y1="0" x2="{x_end:.0f}" y2="0" gradientUnits="userSpaceOnUse">
<stop offset="0" stop-color="{top}"/><stop offset="1" stop-color="{bottom}"/></linearGradient></defs>
{snowflake(42, 58, 26, "#3aaee8")}
{snowflake(width - 40, 58, 20, "#ff6f9f")}
<path d="M{x0} {height - 12} H{x_end:.0f}" stroke="{OUTLINE}" stroke-width="9" stroke-linecap="round"/>
<path d="M{x0} {height - 12} H{x_end:.0f}" stroke="#ffffff" stroke-width="6" stroke-linecap="round"/>
<path d="M{x0 + 4} {height - 12} H{x_end - 4:.0f}" stroke="{DASH}" stroke-width="3" stroke-linecap="round" stroke-dasharray="12 10"/>
<path d="{d}" fill="{OUTLINE}" stroke="{OUTLINE}" stroke-width="12" stroke-linejoin="round" transform="translate(0 3)"/>
<path d="{d}" fill="{OUTLINE}" stroke="{OUTLINE}" stroke-width="12" stroke-linejoin="round"/>
<path d="{d}" fill="#ffffff" stroke="#ffffff" stroke-width="7" stroke-linejoin="round"/>
<path d="{d}" fill="url(#g)"/>
</svg>
'''
    open(out, "w", encoding="utf-8").write(svg)


def sub(text, theme, out, size=30):
    """小見出し: 小さめの文字 + 小さな雪の結晶 (破線は付けない)。"""
    top, bottom = THEMES[theme]
    baseline = 42
    x0 = 44
    d, x_end = text_path(text, size, x0, baseline)
    width = int(x_end) + 16
    height = 56
    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-label="{text}">
<title>{text}</title>
<defs><linearGradient id="g" x1="0" y1="{baseline - size * 0.9:.1f}" x2="0" y2="{baseline + 4}" gradientUnits="userSpaceOnUse">
<stop offset="0" stop-color="{top}"/><stop offset="1" stop-color="{bottom}"/></linearGradient></defs>
{snowflake(20, 31, 12, top)}
<path d="{d}" fill="{OUTLINE}" stroke="{OUTLINE}" stroke-width="8" stroke-linejoin="round" transform="translate(0 2)"/>
<path d="{d}" fill="{OUTLINE}" stroke="{OUTLINE}" stroke-width="8" stroke-linejoin="round"/>
<path d="{d}" fill="#ffffff" stroke="#ffffff" stroke-width="5" stroke-linejoin="round"/>
<path d="{d}" fill="url(#g)"/>
</svg>
'''
    open(out, "w", encoding="utf-8").write(svg)


IMAGE = re.compile(r'<img src="([^"]*images/headings/[^"]+\.svg)" alt="([^"]+)"')

if __name__ == "__main__":
    for md in sys.argv[1:]:
        base = os.path.dirname(md)
        index = 0
        for path, text in IMAGE.findall(open(md, encoding="utf-8").read()):
            out = os.path.normpath(os.path.join(base, path))
            os.makedirs(os.path.dirname(out), exist_ok=True)
            name = os.path.basename(out)
            if name.startswith("title"):
                title(text, out)
            elif re.match(r"s\d", name):
                # 小見出し (### ・ ####) は、すぐ上の見出しと同じ色にしない
                sub(text, "pink" if index % 2 == 1 else "blue", out)
            else:
                heading(text, "blue" if index % 2 == 0 else "pink", out)
                index += 1
            print(out, text)
