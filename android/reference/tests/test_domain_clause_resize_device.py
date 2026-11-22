# SPDX-License-Identifier: GPL-3.0-or-later
"""Shift+矢印で文節を伸ばし、分野辞書の候補が出るかを実機で確認する。

Run test_clause_edit.py first to build/install the conversion fixture.
"""
from pathlib import Path

helper = Path(__file__).with_name('test_prediction_shift_clipboard.py')
exec(compile(helper.read_text(encoding='utf-8').split('results = {}')[0], str(helper), 'exec'))


def candidates():
    return [n.get('text') for n in tree().iter('node') if n.get('content-desc', '').startswith('候補 ')]


def lock_shift():
    a, b, c, d = bounds(find('Shift、2回押すと大文字固定'))
    adb('shell', f'input tap {(a+c)//2} {(b+d)//2}; input tap {(a+c)//2} {(b+d)//2}')
    time.sleep(.2)
    assert find('大文字固定を解除') is not None


start()
type_word('sentankyodaishou')
print('before:', editor(), candidates(), flush=True)

# 先頭の文節まで戻り、Shift+右で文節を伸ばす
for _ in range(3):
    tap('左へ移動')
print('selected:', editor(), candidates(), flush=True)
lock_shift()
for i in range(8):
    tap('右へ移動')
    print(f'resized {i}:', editor(), candidates(), flush=True)
    if '先端巨大症' in candidates():
        break
print('done:', editor(), candidates(), flush=True)
tap('空白・変換')
print('after space:', editor(), candidates(), flush=True)
# 候補バーをスクロールして後ろの候補を確認
nodes = [n for n in tree().iter('node') if n.get('content-desc', '').startswith('候補 ')]
if len(nodes) >= 2:
    a, b, c, d = bounds(nodes[-1])
    adb('shell', 'input', 'swipe', c, (b + d) // 2, a, (b + d) // 2, 300)
    time.sleep(.3)
    print('after swipe:', editor(), candidates(), flush=True)
OUT.joinpath('clause-resize.png').write_bytes(adb('exec-out', 'screencap', '-p'))
print('RESULT:', json.dumps({'editor': editor(), 'candidates': candidates()}, ensure_ascii=False), flush=True)
