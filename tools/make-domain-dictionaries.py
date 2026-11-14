# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# 分野ごとの辞書 dictionaries/domain-*.txt を、外部の辞書から作る (#1650)。
# 形式は「読み<Tab>表記<Tab>表記 …」(読みはひらがな、1 つの読みは 1 行)。出典とライセンスはファイルの先頭のコメントに書く。
#
#   python3 tools/make-domain-dictionaries.py medical JMED_DICT_mini_v1.1.2/ > dictionaries/domain-medical.txt
#   python3 tools/make-domain-dictionaries.py it IDiME_for_Meltype.tsv > dictionaries/domain-it.txt
#   python3 tools/make-domain-dictionaries.py skk SKK-JISYO.station > dictionaries/domain-stations.txt
#   python3 tools/make-domain-dictionaries.py skk SKK-JISYO.propernoun > dictionaries/domain-propernouns.txt
#   python3 tools/make-domain-dictionaries.py skk SKK-JISYO.jinmei SKK-JISYO.fullname > dictionaries/domain-names.txt
#
# 入手先:
#   JMED-DICT-mini: https://sociocom.naist.jp/jmed-dict/ (zip を展開したフォルダーを渡す)
#   IDiME: https://github.com/ssvp003/IDiME の src/IDiME_for_Meltype.tsv
#   SKK 辞書: https://github.com/skk-dev/dict
# 標準ライブラリだけで動く (.xlsx は zip の中の XML を直接読む)。
import re
import sys
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

HIRAGANA = re.compile(r'^[ぁ-ゖー]+$')

# よく使う語の読み (dictionaries/readings.txt・readings-extra.txt)。この読みは変換エンジンの候補で足りていて、
# 分野の語 (きょう → 京・恭) を足すと、ふだんの語の候補 (ひらがな) が後ろに押される。分野の語は、よく使う語に無い読みだけにする
ROOT = Path(__file__).resolve().parent.parent
COMMON = {line.split('\t')[0] for name in ('readings.txt', 'readings-extra.txt')
          for line in (ROOT / 'dictionaries' / name).read_text(encoding='utf-8').splitlines() if line and not line.startswith('#')}


def to_hiragana(text):
    return ''.join(chr(ord(c) - 0x60) if 'ァ' <= c <= 'ヶ' else c for c in text)


def add(entries, reading, word):
    """読みがひらがなだけの語を足す。かなで書く語 (読みと同じ・カタカナにしただけ) は、変換の候補に既にあるので入れない。"""
    reading = to_hiragana(reading.strip())
    # 全角の英数字・記号 (ＩＩＰ、Ｉ‐１３１) は、変換エンジンの候補や打った英字と同じ半角にする
    word = ''.join(chr(ord(c) - 0xFEE0) if '！' <= c <= '～' else c for c in word.strip()).replace('‐', '-')
    if not HIRAGANA.match(reading) or reading in COMMON or not word or to_hiragana(word) == reading:
        return
    # タブ・空白は欄の区切りと紛らわしいので入れない。括弧の付いた形 (あいおい損害保険(株)) は注釈を書いたもので、打ちたい語ではない
    if any(c in word for c in '\t 　#()（）'):
        return
    words = entries.setdefault(reading, [])
    if word not in words:
        words.append(word)


def xlsx_rows(path):
    """.xlsx の最初のシートの行 (列の名前 → 値)。"""
    ns = {'m': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
    with zipfile.ZipFile(path) as z:
        # 文字列を共有の表に置かず、セルに直接書いた .xlsx もある (JMED-DICT-mini)
        names = z.namelist()
        shared = [''.join(t.text or '' for t in si.iter(f'{{{ns["m"]}}}t'))
                  for si in ET.fromstring(z.read('xl/sharedStrings.xml')).findall('m:si', ns)] if 'xl/sharedStrings.xml' in names else []
        sheet = ET.fromstring(z.read('xl/worksheets/sheet1.xml'))
    header = None
    for row in sheet.iter(f'{{{ns["m"]}}}row'):
        values = {}
        for c in row.findall('m:c', ns):
            column = re.match(r'[A-Z]+', c.get('r')).group()
            v = c.find('m:v', ns)
            if v is None:
                inline = c.find('m:is', ns)
                text = ''.join(t.text or '' for t in inline.iter(f'{{{ns["m"]}}}t')) if inline is not None else ''
            else:
                text = shared[int(v.text)] if c.get('t') == 's' else v.text
            values[column] = text
        if header is None:
            header = values
            continue
        yield {header[k]: v for k, v in values.items() if k in header}


def medical(folder):
    entries = {}
    # JMED-DICT-mini は、頻度の上位 10,000 行までが人手で確かめられている (README)。それより下は入れない
    for name in ('DISEASE_mini.xlsx', 'MEDICATION_mini.xlsx', 'BODY_mini.xlsx'):
        for i, row in enumerate(xlsx_rows(Path(folder) / name)):
            if i >= 10000:
                break
            add(entries, row.get('出現形よみ', ''), row.get('出現形', ''))
    header = [
        '# Meltype 分野の辞書: 医療 (病名・症状・医薬品・体の部位)',
        '# 元データ: JMED-DICT-mini ver 1.1.2 (奈良先端科学技術大学院大学 ソーシャル・コンピューティング研究室) https://sociocom.naist.jp/jmed-dict/',
        '# ライセンス: CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/)。',
        '# 加工: 病名・医薬品・部位の各データの上位 10,000 行から、出現形と出現形よみを取り出し、読みで 1 行にまとめた。かなだけで書く語と、よく使う語の読み (readings.txt) は除いた。',
    ]
    return header, entries


def it(path):
    entries = {}
    for line in Path(path).read_text(encoding='utf-8-sig').splitlines():
        fields = line.split('\t')
        if len(fields) >= 2:
            add(entries, fields[0], fields[1])
    header = [
        '# Meltype 分野の辞書: 情報 (高等学校の情報科の用語)',
        '# 元データ: IDiME (https://github.com/ssvp003/IDiME)。情報処理学会 情報入試委員会「情報科全教科書用語リスト 第2版」をもとにしたもの',
        '# ライセンス: CC BY-SA 4.0 (https://creativecommons.org/licenses/by-sa/4.0/)。この派生データも CC BY-SA 4.0。',
        '# 加工: 読みがひらがなだけの行を、読みで 1 行にまとめた。かなだけで書く語と、よく使う語の読み (readings.txt) は除いた。',
    ]
    return header, entries


def skk(*paths):
    entries = {}
    notices = []
    for path in paths:
        text = Path(path).read_bytes().decode('euc_jis_2004', errors='replace')
        # 先頭の著作権表示を写す (Copyright の行と、それに続く「;;    名前 <メール>」の行)
        notices += [line[2:].strip() for line in text.splitlines()[:20]
                    if line.startswith(';;') and ('Copyright' in line or (line.startswith(';;   ') and '<' in line))]
        for line in text.splitlines():
            if line.startswith(';') or ' /' not in line:
                continue
            reading, candidates = line.split(' /', 1)
            # 送り仮名のある語 (かk) は活用するので入れない
            for candidate in candidates.strip('/').split('/'):
                word = candidate.split(';')[0]  # ; の後ろは注釈
                if word.startswith('('):
                    continue  # (concat …) などの式
                add(entries, reading, word)
    names = ' '.join(Path(p).name for p in paths)
    header = [
        f'# Meltype 分野の辞書 (SKK 辞書 {names} から作成)',
        '# 元データ: SKK 辞書 https://github.com/skk-dev/dict',
        *[f'# {n}' for n in dict.fromkeys(notices)],
        '# ライセンス: GPL-2.0-or-later (各ファイルの先頭の「version 2, or (at your option) any later version」)。Meltype では GPL-3.0-or-later で使う。',
        '# 加工: 送り仮名の無い語の候補から注釈と式を除き、読みがひらがなだけの語を取り出した。かなだけで書く語と、よく使う語の読み (readings.txt) は除いた。',
    ]
    return header, entries


def main():
    kind, *args = sys.argv[1:]
    header, entries = {'medical': medical, 'it': it, 'skk': skk}[kind](*args)
    out = sys.stdout
    out.reconfigure(encoding='utf-8', newline='\n')
    for line in header:
        print(line)
    print(f'# 作り直し: python3 tools/make-domain-dictionaries.py {kind} …')
    print('# 形式: 読み<Tab>表記<Tab>表記 …  (変換の候補では、変換エンジンの候補の後、ひらがなの前に並ぶ)')
    for reading in sorted(entries):
        print(reading + '\t' + '\t'.join(entries[reading]))


main()
