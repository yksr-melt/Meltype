# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# 分野ごとの辞書 dictionaries/domain-*.txt を、外部の辞書から作る (#1650)。
# 形式は「読み<Tab>表記<Tab>表記 …」(読みはひらがな、1 つの読みは 1 行)。出典とライセンスはファイルの先頭のコメントに書く。
#
#   MELTYPE_MOZC=… python3 tools/make-domain-dictionaries.py medical JMED_DICT_mini_v1.1.2/ > dictionaries/domain-medical.txt
#   MELTYPE_MOZC=… python3 tools/make-domain-dictionaries.py it IDiME_for_Meltype.tsv > dictionaries/domain-it.txt
#   MELTYPE_MOZC=… python3 tools/make-domain-dictionaries.py skk SKK-JISYO.station > dictionaries/domain-stations.txt
#   MELTYPE_MOZC=… python3 tools/make-domain-dictionaries.py skk SKK-JISYO.propernoun > dictionaries/domain-propernouns.txt
#   MELTYPE_MOZC=… python3 tools/make-domain-dictionaries.py skk SKK-JISYO.jinmei SKK-JISYO.fullname > dictionaries/domain-names.txt
#   MELTYPE_MOZC=… python3 tools/make-domain-dictionaries.py laws <e-Gov の laws-*.json のフォルダー> > dictionaries/domain-laws.txt
#   MELTYPE_MOZC=… python3 tools/make-domain-dictionaries.py sudachi propernouns core_lex.csv > dictionaries/domain-propernouns-sudachi.txt
#   MELTYPE_MOZC=… python3 tools/make-domain-dictionaries.py sudachi words core_lex.csv > dictionaries/domain-words-sudachi.txt
#
# どの辞書も、Mozc の変換ヘルパー (環境変数 MELTYPE_MOZC) に読みを変換させ、Mozc が出せない語だけを入れる。
#
# 入手先:
#   JMED-DICT-mini: https://sociocom.naist.jp/jmed-dict/ (zip を展開したフォルダーを渡す)
#   IDiME: https://github.com/ssvp003/IDiME の src/IDiME_for_Meltype.tsv
#   SKK 辞書: https://github.com/skk-dev/dict
#   e-Gov 法令: https://laws.e-gov.go.jp/api/2/laws?limit=1000&offset=0 (offset を 1000 ずつ増やして全部)
#   SudachiDict: http://sudachi.s3-website-ap-northeast-1.amazonaws.com/sudachidict-raw/ の core_lex.zip
# 標準ライブラリだけで動く (.xlsx は zip の中の XML を直接読む)。
import csv
import json
import os
import re
import subprocess
import sys
import threading
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

HIRAGANA = re.compile(r'^[ぁ-ゖー]+$')
# 漢字を含む表記。大きな辞書では、英字だけの書き方 (あいしゃ → AISHA) や数字で始まる語 (1000本。数の変換で出る) を入れない
KANJI = re.compile(r'[\u3400-\u9fff々〆ヶ]')

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


def known_to_mozc(entries):
    """Mozc の変換ヘルパーが読みから出せる語を除く (1 つの文節の候補か、文節をつないだ最初の変換結果)。"""
    readings = sorted(entries)
    helper = subprocess.Popen([os.environ['MELTYPE_MOZC'], ''], stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, encoding='utf-8')
    if helper.stdout.readline().strip() != 'READY':
        raise RuntimeError('Mozc の変換ヘルパーが起動しない')

    # 書き終わる前に読み始めないと、ヘルパーの出力のパイプが詰まって止まる
    def feed():
        for reading in readings:
            helper.stdin.write(f'C\t\t{reading}\n')
        helper.stdin.write('Q\n')
        helper.stdin.close()
    threading.Thread(target=feed, daemon=True).start()
    for reading, line in zip(readings, helper.stdout):
        clauses = [c.split('\x1f') for c in line.rstrip('\n').split('\x1e')]
        known = {''.join(c[1] for c in clauses if len(c) > 1)}
        if len(clauses) == 1:
            known.update(clauses[0][1:])
        words = [w for w in entries[reading] if w not in known]
        if words:
            entries[reading] = words
        else:
            del entries[reading]
    helper.wait()
    return entries


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


def kana_in_order(title, reading):
    """題名のかなの並び (における・かつ …) が、読みの中に順に現れるか。"""
    at = 0
    for run in re.findall('[ぁ-ゖァ-ヶー]+', title):
        at = reading.find(to_hiragana(run), at)
        if at < 0:
            return False
        at += len(run)
    return True


def laws(folder):
    entries = {}
    for path in sorted(Path(folder).glob('laws-*.json')):
        for law in json.loads(path.read_text(encoding='utf-8'))['laws']:
            info = law['revision_info']
            title = info['law_title']
            # 打つのは今ある法律の名前。改正・施行・経過措置の法令の名前は長い文で、続けて打つことはない
            if info['law_type'] != 'Act' or info['repeal_status'] != 'None' or re.search('改正|施行|経過措置|廃止', title):
                continue
            reading = info['law_title_kana']
            # 読みが改題前の題名のままの法律がある (…駐留軍用地跡地の有効かつ適切な… の読みが、旧題 …の返還に伴う… のもの)。
            # 題名のかなが読みに順に現れない法律は、読みが今の題名と食い違うので入れない
            if not kana_in_order(title, reading):
                continue
            add(entries, reading, title)
            # 題名のカタカナの大きい ツ (あん摩マツサージ) は促音。ふだん打つ読み (まっさーじ) でも引けるようにする
            for run in re.findall('[ァ-ヶー]*ツ[ァ-ヶー]*', title):
                add(entries, reading.replace(to_hiragana(run), to_hiragana(run).replace('つ', 'っ'), 1), title)
    header = [
        '# Meltype 分野の辞書: 法令名 (今ある法律の名前)',
        '# 元データ: e-Gov 法令検索 法令 API (https://laws.e-gov.go.jp/)。デジタル庁',
        '# ライセンス: 公共データ利用規約 (第1.0版) (https://www.digital.go.jp/resources/open_data/public_data_license_v1.0)。CC BY 4.0 と互換。',
        '# 加工: 廃止されていない法律の題名と読み (law_title_kana) を取り出した。改正・施行・経過措置・廃止の法律と、読みが今の題名と食い違う法律 (改題前の読み) は除いた。題名の大きい ツ (マツサージ) は っ にした読みも足した。',
    ]
    return header, entries


# SudachiDict の分類 (品詞の 2〜4 番目) → 分野
SUDACHI = {
    # 人名 (固有名詞,人名) は入れない。まれな字の組み合わせ (粥多朗) が多く、名前として確かめられない
    'propernouns': lambda pos: pos[0] == '固有名詞' and pos[1] == '一般',
    'words': lambda pos: pos[0] == '普通名詞',
}


def sudachi(kind, path):
    entries = {}
    with open(path, encoding='utf-8', newline='') as f:
        for row in csv.reader(f):
            # 見出し, 左id, 右id, コスト, 表記, 品詞 1〜6, 読み, 正規化表記, …。地名 (固有名詞,地名) は町名・字名がほとんどで、郵便番号の変換 (#343) で扱う
            # 正規化表記 (13 列目) と違う表記は異表記 (洗ひざらし・いとま乞ひ)。Mozc で絞ると異表記ばかり残るので、正規化表記と同じ表記だけ
            if len(row) < 13 or row[5] != '名詞' or not SUDACHI[kind](row[6:9]) or not KANJI.search(row[4]) or row[4][0].isdigit() or row[4] != row[12]:
                continue
            add(entries, row[11], row[4])
    header = [
        f'# Meltype 分野の辞書: {dict(propernouns="固有名詞", words="一般の語")[kind]} (SudachiDict core から作成)',
        '# 元データ: SudachiDict (株式会社ワークスアプリケーションズ) https://github.com/WorksApplications/SudachiDict',
        '# ライセンス: Apache License 2.0。SudachiDict は UniDic の一部 (BSD-3-Clause、Copyright (c) 2011-2013, The UniDic Consortium) を含む。全文は THIRD-PARTY-NOTICES.md。',
        '# 加工: 漢字を含む名詞のうち正規化表記と同じ表記と、読みを取り出し、かなだけで書く語・よく使う語の読み (readings.txt) を除いた。',
    ]
    return header, entries


def main():
    kind, *args = sys.argv[1:]
    header, entries = {'medical': medical, 'it': it, 'skk': skk, 'laws': laws, 'sudachi': sudachi}[kind](*args)
    out = sys.stdout
    out.reconfigure(encoding='utf-8', newline='\n')
    # どの辞書も、Mozc が読みから出せる語は入れない。変換エンジンの候補と同じ語を足しても、読み込みの時間とメモリが増えるだけ
    entries = known_to_mozc(entries)
    for line in header:
        print(line)
    print('# Mozc の変換ヘルパー (MELTYPE_MOZC) に読みを変換させ、Mozc が出せる語は除いた。')
    print(f'# 作り直し: MELTYPE_MOZC=… python3 tools/make-domain-dictionaries.py {" ".join([kind, *args[:1]]) if kind == "sudachi" else kind} …')
    print('# 形式: 読み<Tab>表記<Tab>表記 …  (変換の候補では、ひらがな・カタカナの後ろに並ぶ)')
    for reading in sorted(entries):
        print(reading + '\t' + '\t'.join(entries[reading]))


main()
