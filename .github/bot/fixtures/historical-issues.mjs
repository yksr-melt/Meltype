// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Akatuki1121
// Static snapshots of public Meltype Issues for deterministic offline regression checks.
// These examples are a small regression set, not a statistically representative precision/recall estimate.
export const historical = {
  6: {
    number: 6,
    title: '[不具合]',
    body: 'LINEのデスクトップアプリで文字を入力しようとしたが、Meltypeの変換バーが現れず、通常のIMEで入力された。',
    labels: [{ name: 'bug' }, { name: 'windows' }, { name: 'アプリ: チャット' }],
  },
  95: {
    number: 95,
    title: '[不具合] 一部アプリにて、最初に入力した文字が連続してしまう',
    body: '関連issue: #6 #44 #48 #89。複数アプリで最初の文字が繰り返される問題をまとめて管理する。LINEで再現を確認。OBS、Telegram Desktopは未確認。',
    labels: [{ name: 'bug' }, { name: 'windows' }],
  },
  111: {
    number: 111,
    title: '[不具合] unityで日本語が打てない',
    body: 'UnityのTextMeshProで日本語を入力しようとしても、あいうえおではなくaiueoと表示される。Windows 11、Meltype 0.2.1。',
    labels: [{ name: 'bug' }, { name: 'windows' }],
  },
  117: {
    number: 117,
    title: '[不具合]',
    body: 'CLIP STUDIO PAINTのテキストレイヤーやパネルに入力しようとしても、入力ボックスではないと判定されMeltypeが動作しない。Windows 11、Meltype 1.0.2。',
    labels: [{ name: 'bug' }, { name: 'windows' }],
  },
  878: {
    number: 878,
    title: '[調査] Brave で Meltype IME の日本語入力ができない (#873, #874)',
    body: '#873 と #874 は同じ問題とみられるので調査をまとめる。Braveの一部入力欄でMeltype IMEを使っても英字がそのまま入り、日本語に切り替わらない。Microsoft IMEなら同じ欄で日本語を打てる。',
    labels: [{ name: 'bug' }, { name: 'windows' }, { name: 'アプリ: ブラウザー' }],
  },

  261: {
    number: 261,
    title: '[不具合] 入力が早いと日本語にならない',
    body: 'Claude、ChatGPT、Comet などで入力が早いとローマ字が日本語にならない。入力が早ければほぼ毎回発生する。',
    labels: [{ name: 'bug' }, { name: 'windows' }],
  },
  316: {
    number: 316,
    title: '[誤判定] 英単語 Claude の直後に空白なしで日本語を続けると「Cぁうっえ」になる',
    body: 'Claude のチャット入力欄で英単語 Claude の後に空白を入れず日本語を続けると、残りがローマ字として読まれる。#261 と同じ環境で起きており、関連するかもしれない。\n\nRelates to #261',
    labels: [{ name: 'windows' }, { name: 'bug' }],
  },
  717: {
    number: 717,
    title: '[誤判定] URL入力時に日本語と判別されてしまう',
    body: '### 種類\n英語のつもりがかなになった (みいてぃんg)\n### 打ったもの (キーをそのまま)\nzenn.dev\n### 出たもの\nぜん。dev\n### 期待した結果\nzenn.dev\n### 前後の文 (分かれば)\n_No response_\n### どのアプリで\nZen Browser のURL入力欄 (control+T)\n### OS\nLinux (プレビュー版)\n### Meltype の版\n1.2.1',
    labels: [{ name: '誤判定' }, { name: 'linux' }],
  },
  725: {
    number: 725,
    title: '[誤判定] OpenStreetMap が おぺんstreetmap になる',
    body: '### 種類\n英語のつもりがかなになった (みいてぃんg)\n### 打ったもの (キーをそのまま)\nopenstreetmap\n### 出たもの\nおぺんstreetmap\n### 期待した結果\nopenstreetmap\n### 前後の文 (分かれば)\n_No response_\n### どのアプリで\nFloorp\n### OS\nLinux (プレビュー版)\n### Meltype の版\n1.2.1',
    labels: [{ name: '誤判定' }, { name: 'linux' }],
  },
  873: {
    number: 873,
    title: '[不具合] BraveのTwitchコメント欄・U-NEXT検索欄で日本語に切り替えられず半角しか打てない',
    body: 'Windows 11。Meltype キーボード。Brave ブラウザの Twitch コメント欄・U-NEXT 検索欄で、半角/全角を押しても「A」のままで半角英字しか入らず、日本語に戻らない。Microsoft IMEなら同じ欄で日本語を打てる。',
    labels: [{ name: 'bug' }, { name: 'windows' }, { name: 'アプリ: ブラウザー' }],
  },
  874: {
    number: 874,
    title: '[不具合] PWA内フォームでの変換が機能しない',
    body: 'Windows 11 26H2。IME 自動切替 (Microsoft IME を使う)。PWA(Brave) のフォームで、Meltype IMEによる日本語入力を期待しているが、日本語入力モードでもすべてローマ字として扱われる。ログでは入力欄と認識している。',
    labels: [{ name: 'bug' }, { name: 'windows' }, { name: 'アプリ: ブラウザー' }],
  },
};

export const cases = [
  { source: 873, expected: 874, related: true, note: 'Brave/PWA IME input failure' },
  { source: 874, expected: 873, related: true, note: 'reciprocal Brave/PWA IME input failure' },
  { source: 878, expected: 873, related: true, note: 'explicitly consolidated Brave issue' },
  { source: 878, expected: 874, related: true, note: 'explicitly consolidated PWA issue' },
  { source: 316, expected: 261, related: true, note: 'explicit reference; keyword search may miss' },
  { source: 95, expected: 6, related: true, note: 'explicitly consolidated duplicate/related app-input report' },
  { source: 717, expected: 725, related: false, note: 'same form/category but different defect' },
  { source: 725, expected: 717, related: false, note: 'reciprocal unrelated-defect control' },
  { source: 717, expected: 95, related: false, note: 'overlapping Japanese n-grams are not distinct evidence' },
  { source: 95, expected: 717, related: false, note: 'reciprocal control for overlapping Japanese n-grams' },
  { source: 111, expected: 117, related: false, note: 'same broad bug label, different app and failure mode' },
  { source: 117, expected: 111, related: false, note: 'reciprocal unrelated app/failure-mode control' },
];
