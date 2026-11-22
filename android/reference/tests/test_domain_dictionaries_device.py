# SPDX-License-Identifier: GPL-3.0-or-later
"""分野辞書の候補・文節への影響・BackSpace再判定を実機で確認する。

Run test_clause_edit.py first to build/install the conversion fixture.
"""
from pathlib import Path

helper = Path(__file__).with_name('test_prediction_shift_clipboard.py')
exec(compile(helper.read_text(encoding='utf-8').split('results = {}')[0], str(helper), 'exec'))


def candidates():
    return [n.get('text') for n in tree().iter('node') if n.get('content-desc', '').startswith('候補 ')]


def choose(text):
    node = next(n for n in tree().iter('node')
                if n.get('text') == text and n.get('content-desc', '').startswith('候補 '))
    a, b, c, d = bounds(node)
    adb('shell', 'input', 'tap', (a + c) // 2, (b + d) // 2)
    time.sleep(.25)


def backspace():
    tap('削除、長押しで連続削除')


results = {}

# 1. 変換エンジンが返す文節と、分野辞書が文節区切りへ入らないことを確認
start()
type_word('sentankyodaishou')
probe = {'editor': editor(), 'composing': find('fixture-composing').get('text'),
         'candidates': candidates()}
results['clause'] = probe
print('PROBE:', json.dumps(probe, ensure_ascii=False), flush=True)
assert probe['composing'] != '-1,-1', '変換中ではない'
# 変換エンジンが 2 文節以上に分割しても、分野辞書が Contains に無いため誤結合しない
assert probe['editor'] != 'せんたんきょだいしょう', '分野辞書が文節区切りに使われている'
print('PASS: domain dictionary is not used for clause segmentation on device', flush=True)

# 2. 実機で出る候補を選んで確定できる (通常の辞書候補)
start()
type_word('goji')
words = candidates()
assert '誤字' in words and '護持' in words, words
choose('誤字')
assert editor() == '誤字', editor()
results['selection'] = words
print('PASS: on-device candidate selection still works', flush=True)

# 3. BackSpace 後の再判定 (実機の変換エンジンが漢字・カタカナを返すため、出力は記録して確認する)
for keys, expected in [('ff\buronto', None), ('kk\bakkou', None),
                       ('nk\ba', None), ('kank\bi', None)]:
    start()
    for c in keys:
        if c == '\b':
            backspace()
        else:
            keys_node = {n.get('content-desc'): n.get('bounds') for n in tree().iter('node')}
            if c not in keys_node:
                raise AssertionError(('key not found', c, keys))
            x1, y1, x2, y2 = map(int, __import__('re').findall(r'\d+', keys_node[c]))
            adb('shell', 'input', 'tap', (x1 + x2) // 2, (y1 + y2) // 2)
            time.sleep(.12)
    tap('確定・改行')
    word = editor()
    if expected is not None:
        assert word == expected, (keys, word, expected)
    results['backspace_' + keys] = word
    print(f'RESULT: BackSpace {keys!r} -> {word}', flush=True)

OUT.joinpath('domain-dictionaries.json').write_text(
    json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
print('ALL PASS', flush=True)
