# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
# README の飾りの画像を作り直す。
#   docs/images/demo.svg     … kyouhagoogledekensaku を打って「今日はgoogleで検索」になるまでの動き (ライブ変換。SMIL のアニメーション)
#   docs/images/features.svg … できること のカード 3 枚
#   docs/images/cards/*.svg  … プライバシー・開発に参加する のカード (1 枚ずつ。カードごとにリンクを付ける)
#   pip install fonttools
#   curl -L -o mplus.ttf https://cdn.jsdelivr.net/gh/google/fonts@main/ofl/mplusrounded1c/MPLUSRounded1c-ExtraBold.ttf
#   python3 tools/make-readme-art.py docs/images
# 文字は M PLUS Rounded 1c (SIL Open Font License 1.1) の字形を図形にして埋め込む。
# デモの途中の表示は、Meltype の本物の判定で打ったときの見え方 (src/Meltype.Core.Tests の --repro で確かめたもの)。
import os, sys
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen

font = TTFont(os.environ.get("MPLUS_FONT", "mplus.ttf"))
glyph_set = font.getGlyphSet()
cmap = font.getBestCmap()
upm = font["head"].unitsPerEm
hmtx = font["hmtx"]

BLUE, PINK, OUTLINE, INK, GRAY = "#3aaee8", "#ff6f9f", "#7391ee", "#33415c", "#8a94a8"


class Glyphs:
    """使った字形を 1 回だけ <defs> に入れ、<use> で並べる (同じ字を何度も描くアニメーションを小さくするため)。"""

    def __init__(self):
        self.defs = {}

    def glyph_id(self, ch):
        name = cmap.get(ord(ch))
        if name is None:
            return None, 0
        gid = "g%x" % ord(ch)
        if gid not in self.defs:
            pen = SVGPathPen(glyph_set)
            glyph_set[name].draw(TransformPen(pen, (1, 0, 0, -1, 0, 0)))
            self.defs[gid] = f'<path id="{gid}" d="{pen.getCommands()}"/>'
        return gid, hmtx[name][0]

    def width(self, text, size):
        return sum(hmtx[cmap[ord(c)]][0] for c in text if ord(c) in cmap) * size / upm

    def text(self, text, x, baseline, size, color_of):
        """文字列を <use> で並べる。color_of(文字) で 1 文字ずつ色を決める。終わりの x を返す。"""
        scale = size / upm
        parts = []
        for ch in text:
            gid, advance = self.glyph_id(ch)
            if gid:
                parts.append(f'<use href="#{gid}" transform="translate({x:.1f} {baseline}) scale({scale:.4f})" fill="{color_of(ch)}"/>')
            x += advance * scale
        return "".join(parts), x

    def defs_xml(self):
        return "<defs>" + "".join(self.defs.values()) + "</defs>"


def mixed_color(ch):
    # 英字は水色、かな・漢字は濃い紺 (日本語と英語が見分けられているのが分かるように)
    return BLUE if ch.isascii() and ch.isalpha() else INK


# 打ったキーと、そのときの変換ボックスの見え方。ライブ変換 (既定で ON) なので、4 文字以上の日本語は打つそばから漢字になる。
# かな・英字の見え方は --repro で確かめたもの。漢字は変換エンジンの結果 (きょうは → 今日は、でけんさく → で検索)。
# 変換エンジンの結果が読めない途中 (きょうはご・でけんさ など) のコマは入れない。
TYPING = [
    ("k", "k"), ("ky", "ky"), ("kyo", "きょ"), ("kyou", "きょう"), ("kyouh", "きょうh"), ("kyouha", "今日は"),
    ("kyouhag", "今日はg"), ("kyouhagoog", "今日はgoog"), ("kyouhagoogl", "今日はgoogl"), ("kyouhagoogle", "今日はgoogle"),
    ("kyouhagooglede", "今日はgoogleで"), ("kyouhagoogledek", "今日はgoogleでk"), ("kyouhagoogledeke", "今日はgoogleでけ"),
    ("kyouhagoogledeken", "今日はgoogleでけn"), ("kyouhagoogledekens", "今日はgoogleでけんs"),
    ("kyouhagoogledekensaku", "今日はgoogleで検索"),
]
CONVERTED = "今日はgoogleで検索"


def demo(out):
    g = Glyphs()
    width, height = 760, 210
    frames = []  # (秒, 中身)
    card_x, card_y, card_w, card_h = 20, 18, width - 40, height - 40
    text_x, text_base, size = 56, 128, 40
    keys_base, keys_size = 72, 20

    def keys_line(keys, note=""):
        label, x = g.text("打ったキー", card_x + 28, keys_base, 16, lambda c: GRAY)
        typed, x = g.text(keys, x + 14, keys_base, keys_size, lambda c: PINK)
        badge = ""
        if note:
            nw = g.width(note, 18) + 28
            bx = width - card_x - 28 - nw
            badge_text, _ = g.text(note, bx + 14, keys_base, 18, lambda c: "#ffffff")
            badge = f'<rect x="{bx:.0f}" y="{keys_base - 21}" width="{nw:.0f}" height="28" rx="14" fill="{PINK}"/>{badge_text}'
        return label + typed + badge

    def composition(display, style):
        body, x_end = g.text(display, text_x, text_base, size, mixed_color if style != "converted" else lambda c: BLUE if c.isascii() else INK)
        line = ""
        if style == "typing":
            line = f'<path d="M{text_x} {text_base + 12} H{x_end:.0f}" stroke="{INK}" stroke-width="2" stroke-dasharray="3 5" stroke-linecap="round"/>'
            line += f'<rect x="{x_end + 4:.0f}" y="{text_base - 36}" width="3" height="44" fill="{PINK}"><animate attributeName="opacity" values="1;0;1" dur="0.8s" repeatCount="indefinite"/></rect>'
        elif style == "converted":
            line = f'<rect x="{text_x - 6}" y="{text_base - 40}" width="{x_end - text_x + 12:.0f}" height="54" rx="10" fill="{BLUE}" opacity="0.14"/>'
            line += f'<path d="M{text_x} {text_base + 12} H{x_end:.0f}" stroke="{BLUE}" stroke-width="4" stroke-linecap="round"/>'
        return line + body

    frames.append((0.7, keys_line("") + f'<rect x="{text_x}" y="{text_base - 36}" width="3" height="44" fill="{PINK}"><animate attributeName="opacity" values="1;0;1" dur="0.8s" repeatCount="indefinite"/></rect>'))
    typed = 0
    for keys, display in TYPING:
        # 次のコマまでに打つ文字数に合わせて見せる (コマを省いたところも、同じ速さで打っているように)
        frames[-1] = (frames[-1][0] + 0.15 * max(0, len(keys) - typed - 1), frames[-1][1]) if typed else frames[-1]
        frames.append((0.15, keys_line(keys) + composition(display, "typing")))
        typed = len(keys)
    frames.append((1.6, keys_line("kyouhagoogledekensaku", "打つそばから漢字に") + composition(TYPING[-1][1], "typing")))
    frames.append((2.2, keys_line("", "Enter で確定") + composition(CONVERTED, "done")))

    # 確かめる用: 環境変数 DEMO_FRAME=番号 なら、そのコマだけを止めて描く
    if os.environ.get("DEMO_FRAME"):
        frames = [(1.0, frames[int(os.environ["DEMO_FRAME"])][1])]
    total = sum(t for t, _ in frames)
    layers = []
    start = 0.0
    for duration, body in frames:
        a, b = start / total, (start + duration) / total
        if a == 0:
            values, times = "1;0;0", f"0;{b:.4f};1"
        elif b >= 0.9999:
            values, times = "0;1;1", f"0;{a:.4f};1"
        else:
            values, times = "0;1;0;0", f"0;{a:.4f};{b:.4f};1"
        layers.append(f'<g opacity="{1 if a == 0 else 0}"><animate attributeName="opacity" calcMode="discrete" values="{values}" keyTimes="{times}" dur="{total:.2f}s" repeatCount="indefinite"/>{body}</g>')
        start += duration

    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-label="kyouhagoogledekensaku と打つと、今日はgoogleで検索 になる">
<title>kyouhagoogledekensaku → 今日はgoogleで検索</title>
{g.defs_xml()}
<rect x="{card_x + 4}" y="{card_y + 6}" width="{card_w}" height="{card_h}" rx="22" fill="{OUTLINE}"/>
<rect x="{card_x}" y="{card_y}" width="{card_w}" height="{card_h}" rx="22" fill="#ffffff" stroke="{OUTLINE}" stroke-width="3"/>
<path d="M{card_x + 24} {keys_base + 18} H{card_x + card_w - 24}" stroke="#e3e8f8" stroke-width="2"/>
{"".join(layers)}
</svg>
'''
    open(out, "w", encoding="utf-8").write(svg)


CARDS = [
    ("あ A", "混ぜたまま打てる", ["英単語は英字のまま、", "日本語はかな・漢字に。"], BLUE),
    ("</>", "コードの手も止めない", ["コメントと文字列だけ日本語に。", "/command もそのまま入る"], PINK),
    ("pc", "ぜんぶ PC の中で", ["判定も変換もローカルで完結。", "打った文字を外に送りません"], OUTLINE),
]


def features(out):
    g = Glyphs()
    card_w, card_h, gap = 280, 190, 20
    width = card_w * 3 + gap * 2 + 16
    height = card_h + 20
    cards = []
    for i, (icon, title, lines, color) in enumerate(CARDS):
        x = 8 + i * (card_w + gap)
        y = 6
        picture = drawn_icon(icon, x + 52, y + 50)
        if picture is None:
            icon_text, _ = g.text(icon, 0, 0, 26, lambda c: "#ffffff")
            picture = f'<g transform="translate({x + 52 - g.width(icon, 26) / 2:.1f} {y + 59})">{icon_text}</g>'
        title_text, _ = g.text(title, x + 24, y + 112, 24, lambda c: INK)
        body = "".join(g.text(line, x + 24, y + 144 + k * 26, 16, lambda c: GRAY)[0] for k, line in enumerate(lines))
        cards.append(f'''<rect x="{x + 4}" y="{y + 5}" width="{card_w}" height="{card_h}" rx="20" fill="{color}" opacity="0.55"/>
<rect x="{x}" y="{y}" width="{card_w}" height="{card_h}" rx="20" fill="#ffffff" stroke="{color}" stroke-width="3"/>
<circle cx="{x + 52}" cy="{y + 50}" r="30" fill="{color}"/>
{picture}
{title_text}{body}''')
    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-label="混ぜたまま打てる / コードの手も止めない / ぜんぶ PC の中で">
<title>できること</title>
{g.defs_xml()}
{"".join(cards)}
</svg>
'''
    open(out, "w", encoding="utf-8").write(svg)


def drawn_icon(kind, cx, cy):
    """カードの丸の中の絵 (白い線)。kind が絵の名前でなければ、文字のアイコンとして扱う (None を返す)。"""
    st = 'fill="none" stroke="#ffffff" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round"'
    shapes = {
        # 鍵: 体と、上のつる
        "lock": f'<rect x="{cx - 11}" y="{cy - 3}" width="22" height="17" rx="4" {st}/><path d="M{cx - 6} {cy - 3} v-5 a6 6 0 0 1 12 0 v5" {st}/><circle cx="{cx}" cy="{cy + 5}" r="1.8" fill="#ffffff"/>',
        # 雲
        "cloud": f'<path d="M{cx - 11} {cy + 9} h22 a7 7 0 0 0 0 -14 a9 9 0 0 0 -17 -2 a6 6 0 0 0 -5 16 z" {st}/>',
        # パソコン: 画面と台
        "pc": f'<rect x="{cx - 14}" y="{cy - 12}" width="28" height="19" rx="3" {st}/><path d="M{cx - 6} {cy + 13} h12 M{cx} {cy + 7} v6" {st}/>',
        # 虫: 体・頭・脚・触角
        "bug": f'<ellipse cx="{cx}" cy="{cy + 3}" rx="7" ry="10" {st}/><path d="M{cx} {cy - 7} v20 M{cx - 7} {cy} h-6 M{cx + 7} {cy} h6 M{cx - 7} {cy + 7} l-5 4 M{cx + 7} {cy + 7} l5 4 M{cx - 6} {cy - 4} l-5 -4 M{cx + 6} {cy - 4} l5 -4 M{cx - 3} {cy - 9} l-3 -5 M{cx + 3} {cy - 9} l3 -5" {st}/>',
        # 開いた本
        "book": f'<path d="M{cx} {cy - 7} c-4 -4 -10 -4 -15 -3 v19 c5 -1 11 -1 15 3 c4 -4 10 -4 15 -3 v-19 c-5 -1 -11 -1 -15 3 z M{cx} {cy - 7} v19" {st}/>',
        # ！ (不具合の報告)
        "exclaim": f'<path d="M{cx} {cy - 13} v16" fill="none" stroke="#ffffff" stroke-width="5" stroke-linecap="round"/><circle cx="{cx}" cy="{cy + 12}" r="3" fill="#ffffff"/>',
        # 4 つに分かれた窓 (Windows)
        "panes": f'<rect x="{cx - 13}" y="{cy - 13}" width="11" height="11" rx="1.5" {st}/><rect x="{cx + 2}" y="{cy - 13}" width="11" height="11" rx="1.5" {st}/><rect x="{cx - 13}" y="{cy + 2}" width="11" height="11" rx="1.5" {st}/><rect x="{cx + 2}" y="{cy + 2}" width="11" height="11" rx="1.5" {st}/>',
        # ノートパソコン (Mac)
        "laptop": f'<rect x="{cx - 11}" y="{cy - 11}" width="22" height="15" rx="2.5" {st}/><path d="M{cx - 16} {cy + 9} h32 l-2 4 h-28 z" {st}/>',
        # 端末 (Linux): 枠と >_
        "terminal": f'<rect x="{cx - 15}" y="{cy - 12}" width="30" height="24" rx="3.5" {st}/><path d="M{cx - 8} {cy - 4} l5 4 l-5 4 M{cx + 1} {cy + 5} h7" {st}/>',
        # 吹き出し (Discord で話す)
        "chat": f'<path d="M{cx - 14} {cy - 11} h28 a3 3 0 0 1 3 3 v14 a3 3 0 0 1 -3 3 h-16 l-7 6 v-6 h-5 a3 3 0 0 1 -3 -3 v-14 a3 3 0 0 1 3 -3 z" {st}/><circle cx="{cx - 7}" cy="{cy - 1}" r="1.8" fill="#ffffff"/><circle cx="{cx}" cy="{cy - 1}" r="1.8" fill="#ffffff"/><circle cx="{cx + 7}" cy="{cy - 1}" r="1.8" fill="#ffffff"/>',
        # ハート (応援する)
        "heart": f'<path d="M{cx} {cy + 13} c-9 -7 -16 -12 -16 -19 a8 8 0 0 1 16 -3 a8 8 0 0 1 16 3 c0 7 -7 12 -16 19 z" {st}/>',
        # Pull Request の枝分かれ
        "pr": f'<circle cx="{cx - 8}" cy="{cy - 10}" r="3.5" {st}/><circle cx="{cx - 8}" cy="{cy + 11}" r="3.5" {st}/><circle cx="{cx + 9}" cy="{cy + 11}" r="3.5" {st}/><path d="M{cx - 8} {cy - 6} v13 M{cx + 9} {cy + 7} v-9 a5 5 0 0 0 -5 -5 h-5 M{cx + 2} {cy - 10} l-3 3 l3 3" {st}/>',
    }
    return shapes.get(kind)


def card(icon, title, lines, color, out):
    """1 枚だけのカード (README の下の方で、カードごとにリンクを付けるため)。"""
    g = Glyphs()
    card_w, card_h = 280, 190
    x, y = 8, 6
    picture = drawn_icon(icon, x + 52, y + 50)
    if picture is None:
        icon_text, _ = g.text(icon, 0, 0, 26, lambda c: "#ffffff")
        picture = f'<g transform="translate({x + 52 - g.width(icon, 26) / 2:.1f} {y + 59})">{icon_text}</g>'
    title_text, _ = g.text(title, x + 24, y + 112, 24, lambda c: INK)
    body = "".join(g.text(line, x + 24, y + 144 + k * 26, 16, lambda c: GRAY)[0] for k, line in enumerate(lines))
    width, height = card_w + 16, card_h + 20
    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-label="{title}">
<title>{title}</title>
{g.defs_xml()}
<rect x="{x + 4}" y="{y + 5}" width="{card_w}" height="{card_h}" rx="20" fill="{color}" opacity="0.55"/>
<rect x="{x}" y="{y}" width="{card_w}" height="{card_h}" rx="20" fill="#ffffff" stroke="{color}" stroke-width="3"/>
<circle cx="{x + 52}" cy="{y + 50}" r="30" fill="{color}"/>
{picture}
{title_text}{body}
</svg>
'''
    open(out, "w", encoding="utf-8").write(svg)


# README の下の方のカード (ファイル名, アイコン (drawn_icon の絵の名前), 題, 説明 2 行, 色)
MORE_CARDS = [
    ("privacy-send", "lock", "打った文字は送らない", ["判定も変換も PC の中で完結。", "ネットには出しません"], BLUE),
    ("privacy-network", "cloud", "既定の通信は 2 つだけ", ["自動更新の確認と、", "自分で開いた報告のフォーム"], PINK),
    ("privacy-storage", "pc", "保存も PC の中", ["設定・学習データ・辞書は", "%LOCALAPPDATA% の中に"], OUTLINE),
    ("contribute-issue", "exclaim", "不具合の報告", ["どのアプリで・何と打って・", "どうなったかを教えてください"], PINK),
    ("contribute-dictionary", "book", "辞書の追加", ["足りない語・社名は", "Pull Request か Issue で"], BLUE),
    ("contribute-code", "pr", "コードで協力", ["バグの修正も新しい機能も", "Pull Request で大歓迎！"], OUTLINE),
    ("os-windows", "panes", "Windows", ["Windows 10 / 11", "インストーラーで入れるだけ！"], BLUE),
    ("os-mac", "laptop", "Mac", ["macOS 13 以降の", "入力ソース (プレビュー版)"], PINK),
    ("os-linux", "terminal", "Linux", ["IBus / fcitx5 の", "入力ソース (プレビュー版)"], OUTLINE),
    ("community-discord", "chat", "Discord", ["使い方の相談・雑談・", "開発の話はこちらで！"], BLUE),
    ("support-sponsors", "heart", "GitHub Sponsors", ["開発を続けるための", "応援をお待ちしています！"], PINK),
]


if __name__ == "__main__":
    folder = sys.argv[1] if len(sys.argv) > 1 else "docs/images"
    demo(os.path.join(folder, "demo.svg"))
    features(os.path.join(folder, "features.svg"))
    os.makedirs(os.path.join(folder, "cards"), exist_ok=True)
    for name, icon, title, lines, color in MORE_CARDS:
        card(icon, title, lines, color, os.path.join(folder, "cards", f"{name}.svg"))
    print("ok")
