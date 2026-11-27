// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

using System.Text;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Dictionary;

namespace Meltype.Ime;

/// <summary><see cref="ImeSession"/> の処理が使う、入力欄の状態でないもの。<see cref="ImeHost"/> が出来事ごとに渡す。</summary>
/// <param name="Settings">設定 (config.json)。出来事の始めに 1 回読んだもの。アプリ別設定は入力欄のアプリで反映する。</param>
internal sealed record ImeEnv(IImeEnvironment Environment, SessionParts Parts, Settings Settings)
{
    /// <summary>入力欄のアプリの設定 (アプリ別設定を反映したもの)。ImeSession の入口 (Handle・Close) で、入力欄が決まったら 1 回作る。</summary>
    public Settings App { get; init; } = Settings;

    public UserDictionary? UserDictionary => Parts.Services.UserDictionary;

    public Func<long> TimeMs => Parts.TimeMs;
}

/// <summary>入力欄 1 つ分の状態 (データだけ)。処理は <see cref="ImeSession"/> の関数が受け取って書き換える。</summary>
internal sealed class ImeSessionState
{
    public ImeField? Field;

    // 直前に入れた定型文 (すぐに Esc を押したら、打った ;名前 に戻す)
    public (string Text, string Typed)? LastSnippet;

    // 前の出来事の入力欄がゲームだった
    public bool Game;
    // 長押しを待っている最初のキーの応答の世代。0 なら待っていない
    public long GameKeyHoldRevision;

    // ---- 左右の Alt の単独押し (設定「左 Alt で英数 / 右 Alt で日本語」) ----
    // 押している左右の Alt と、単独押しにしない理由 (null なら、まだ単独押しの途中)
    public int AltDown;

    public string? AltNotAlone;

    // 前の応答の一覧が予測変換の候補だった (select の番号を読むため)
    public bool ListedPredictions;

    public bool UnreadableCode;

    // 入力欄に入ってから、まだ何も打っていない (この間に切り替えたモードを、入力欄のモードとして覚える)
    // セッションはキーで作るので、作ったときはまだ何も打っていない
    public bool FreshFocus = true;

    // ワークアラウンド: この入力欄の今の行のうち、本体が知っている文字 (通したキーの文字と確定した文字)。
    // キャレットが打つ以外で動いたら、空 (作ったときと同じ状態) に戻す
    public readonly StringBuilder Line = new();

    // ---- 変換ボックスへの入力 (コードの行・/command・英数状態) ----
    public readonly CompositionState Composition = new();

    public long UserDictionaryCheckedMs;

    /// <summary>
    /// コードの入力欄ではコメント・文字列以外を直接入力する。コメント・文字列の中かは、フロントが渡すキャレットの前の文字 (before) だけで決め、
    /// 打ったキーから行を追いかけない。
    /// </summary>
    public bool CodeInput;

    public bool CodeTerminal;

    // 半角/全角 で、コードの行でも日本語にしている (改行しても続ける。Meltype キーボードと同じ)
    public bool CodeJapanese;
}
