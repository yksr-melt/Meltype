// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>
/// AI エージェントの入力欄などで打つ /command・$skill・@ファイル名 を見分ける (#193)。
/// 入力欄・行の先頭か空白の直後に打った / $ @ と、続く名前 (空白まで) は変換せず、打つたびにそのままアプリへ渡す
/// (変換ボックスに溜めると、アプリ側の補完の候補が出ない・選べない)。名前の後に空白を打てば普通の入力に戻る。
///
/// アプリに届いた文字 (素通しした打鍵・確定した文字列) を追いかけて、直前の文字が先頭・空白かを覚えておく。
/// taro@example.com の @ は前が英字なので対象にしない。フックのスレッドと UI スレッドの両方から呼ばれる。
/// </summary>
public sealed class SigilWord
{
    private readonly object _gate = new();
    // 直前が入力欄・行の先頭か空白か (null: 分からない。キャレットが動いた後など)。打ち始め (フォーカスが入った直後) は先頭とみなす。
    private bool? _boundary = true;
    // 記号から打っている名前の文字数 (記号を含む)。0 なら名前の途中ではない。
    private int _length;

    /// <summary>名前の前に付ける記号。</summary>
    public static bool IsSigil(char c) => c is '/' or '$' or '@';

    /// <summary>名前に使う文字: 空白以外の半角の英数字・記号 (review、skill-name、src/app.ts、plugin:cmd)。</summary>
    public static bool IsNameChar(char c) => c is > ' ' and <= '~';

    /// <summary>記号から始めた名前を打っている途中か。</summary>
    public bool IsActive { get { lock (_gate) return _length > 0; } }

    /// <summary>
    /// 変換ボックスが空のときに打った文字を、変換せずにそのままアプリへ渡すか。
    /// before はホストが教えてくれたキャレットの前の文字列 (分からなければ null)。渡した文字も <see cref="Append"/> で伝える。
    /// </summary>
    public bool PassesThrough(char c, string? before = null)
    {
        lock (_gate)
        {
            // 名前の途中のはずなのに、キャレットの前が空白・名前に使わない文字: 名前の後の空白・Enter が
            // こちらを通らずにアプリへ届いた (Meltype IME は変換中でないときの Space・Enter を送ってこない: #242)。普通の入力に戻る
            if (_length > 0 && !string.IsNullOrEmpty(before) && !IsNameChar(before[^1])) _length = 0;
            if (_length > 0) return IsNameChar(c);
            if (!IsSigil(c)) return false;
            // ホストが前の文字を教えてくれれば、そちらを使う。空 (入力欄の先頭) は、前の文字を打ったのを見ていないときだけ信じる
            // (前の文字を読めないアプリは空を返すことがある)。
            if (!string.IsNullOrEmpty(before)) _boundary = char.IsWhiteSpace(before[^1]);
            else if (before is not null && _boundary is null) _boundary = true;
            return _boundary == true;
        }
    }

    /// <summary>アプリに届いた文字列 (素通しした打鍵の文字・変換ボックスで確定した文字列)。</summary>
    public void Append(string text)
    {
        lock (_gate)
        {
            foreach (var c in text) AppendChar(c);
        }
    }

    public void Append(char c)
    {
        lock (_gate) AppendChar(c);
    }

    private void AppendChar(char c)
    {
        if (_length > 0)
        {
            if (IsNameChar(c))
            {
                _length++;
                return;
            }
            // 名前の後の空白 (など) で普通の入力に戻る。
            _length = 0;
        }
        else if (IsSigil(c) && _boundary == true)
        {
            _length = 1;
            _boundary = false;
            return;
        }
        _boundary = c == '\n' || char.IsWhiteSpace(c);
    }

    /// <summary>アプリに届いたキー (ch はその文字。無ければ null)。修飾キーと一緒に押したキーは <see cref="Lose"/> で伝える。</summary>
    public void OnKey(int vk, char? ch)
    {
        switch (vk)
        {
            case Input.VirtualKeys.Return: Start(); return;
            case Input.VirtualKeys.Back: Backspace(); return;
            case Input.VirtualKeys.Space: Append(' '); return;
            // Esc は補完の候補を閉じるだけのことが多いので、そのまま。
            case Input.VirtualKeys.Escape: return;
            // Tab (補完)・キャレットを動かすキー・Delete の後は、前の文字が分からない。
            case Input.VirtualKeys.Tab or (>= 0x21 and <= 0x28) or 0x2E: Lose(); return;
        }
        if (Input.VirtualKeys.IsModifier(vk)) return;
        if (ch is { } c && !char.IsControl(c)) Append(c);
    }

    /// <summary>BackSpace がアプリに届いた。名前の途中なら 1 文字戻し、記号まで消したら記号の前 (先頭・空白) に戻る。</summary>
    public void Backspace()
    {
        lock (_gate)
        {
            if (_length > 0)
            {
                if (--_length == 0) _boundary = true;
                return;
            }
            _boundary = null;
        }
    }

    /// <summary>Enter (新しい行・送信した後) や、別の入力欄に移ったとき。次の文字は先頭とみなす。</summary>
    public void Start()
    {
        lock (_gate)
        {
            _boundary = true;
            _length = 0;
        }
    }

    /// <summary>
    /// 入力欄のフォーカスが移った (Windows の UI Automation の通知)。次に打つ文字は先頭とみなす。
    /// ただし名前の途中なら続ける (補完の候補の一覧にフォーカスが移ったように通知するアプリがある)。
    /// </summary>
    public void FocusMoved()
    {
        lock (_gate)
        {
            if (_length == 0) _boundary = true;
        }
    }

    /// <summary>キャレットが動いた (矢印・クリック・Tab の補完・ショートカット) ので、前の文字が分からない。</summary>
    public void Lose()
    {
        lock (_gate)
        {
            _boundary = null;
            _length = 0;
        }
    }
}
