// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai
// Copyright (C) 2026 aster-void
//
// Meltype IME のテキストサービス本体。TSF から読んだ事実 (キー・キャレットの前後の文字・入力欄の種類・IME の開閉) を
// Meltype.exe (TipServer の ImeHost) に渡し、返ってきた指示 (確定する文字・未確定の文字・候補・IME の開閉) を入力欄に反映する。
// 入力モードとモードを切り替えるキーの意味は Meltype.exe が決めるので、ここでは持たない。

#pragma once

#include <wrl/client.h>

#include <future>
#include <memory>
#include <vector>

#include "CandidateWindow.h"
#include "Globals.h"
#include "Json.h"
#include "Pipe.h"
#include "Shortcut.h"

namespace meltype {

class LangBarButton;

// Meltype.exe が最後に返した状態 (ImeState)。毎回まるごと置き換える。
// known が false の間 (まだ応答が無い・つながらない) は、日本語入力として文字と入力モードのキーを受け取る
struct ServerState {
    bool known = false;
    bool active = false;
    bool open = true;
    bool japanese = true;
    // 変換していないときに受け取るキー (ImeCapture)。text は ! から ~ までの文字 (except を除く)、keys はそれ以外に受け取るキー
    bool text = true;
    std::wstring except;
    std::vector<ToggleShortcut> keys = {{0x19, 0}, {0xC0, 4}, {0xF3, 0}, {0xF4, 0}, {0xF0, 0}, {0x15, 0}, {0x20, 1}};
    // 変換中に Ctrl と一緒に押しても Meltype.exe に送るキー (ほかの Ctrl・Alt・Windows キーとの組み合わせは、確定して通す)
    std::vector<UINT> composing;
    double rev = 0;
};

class TextService final : public ITfTextInputProcessorEx,
                          public ITfThreadMgrEventSink,
                          public ITfKeyEventSink,
                          public ITfCompositionSink,
                          public ITfDisplayAttributeProvider,
                          public ITfCompartmentEventSink,
                          public ITfTextEditSink {
public:
    TextService();

    // IUnknown
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    STDMETHODIMP_(ULONG) AddRef() override;
    STDMETHODIMP_(ULONG) Release() override;

    // ITfTextInputProcessor(Ex)
    STDMETHODIMP Activate(ITfThreadMgr* threadMgr, TfClientId clientId) override;
    STDMETHODIMP ActivateEx(ITfThreadMgr* threadMgr, TfClientId clientId, DWORD flags) override;
    STDMETHODIMP Deactivate() override;

    // ITfThreadMgrEventSink
    STDMETHODIMP OnInitDocumentMgr(ITfDocumentMgr*) override { return S_OK; }
    STDMETHODIMP OnUninitDocumentMgr(ITfDocumentMgr*) override { return S_OK; }
    STDMETHODIMP OnSetFocus(ITfDocumentMgr* focus, ITfDocumentMgr* previous) override;
    STDMETHODIMP OnPushContext(ITfContext*) override { return S_OK; }
    STDMETHODIMP OnPopContext(ITfContext*) override { return S_OK; }

    // ITfKeyEventSink
    STDMETHODIMP OnSetFocus(BOOL foreground) override;
    STDMETHODIMP OnTestKeyDown(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) override;
    STDMETHODIMP OnKeyDown(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) override;
    STDMETHODIMP OnTestKeyUp(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) override;
    STDMETHODIMP OnKeyUp(ITfContext* context, WPARAM wParam, LPARAM lParam, BOOL* eaten) override;
    STDMETHODIMP OnPreservedKey(ITfContext* context, REFGUID guid, BOOL* eaten) override;

    // ITfCompositionSink
    STDMETHODIMP OnCompositionTerminated(TfEditCookie ec, ITfComposition* composition) override;

    // ITfDisplayAttributeProvider
    STDMETHODIMP EnumDisplayAttributeInfo(IEnumTfDisplayAttributeInfo** ppEnum) override;
    STDMETHODIMP GetDisplayAttributeInfo(REFGUID guid, ITfDisplayAttributeInfo** ppInfo) override;

    // ITfCompartmentEventSink
    STDMETHODIMP OnChange(REFGUID guid) override;

    // ITfTextEditSink
    STDMETHODIMP OnEndEdit(ITfContext* context, TfEditCookie ec, ITfEditRecord* record) override;

    // タスクバーに出す入力モード (「あ」なら true)。コードの行では、IME は開いたまま「A」になる
    bool IsOpen() const { return state_.known && state_.active ? state_.japanese : open_; }
    // タスクバーのボタンから日本語 ⇔ 英数を切り替える。context が null なら、フォーカスのある入力欄を使う
    void ToggleInputMode(ITfContext* context);

private:
    ~TextService();

    // ---- キー ----
    // test: OnTestKeyDown から (入力欄の種類を読んだ結果を、続く OnKeyDown で使うために覚える)
    bool WouldEat(ITfContext* context, UINT vk, LPARAM lParam, wchar_t& ch, bool test);
    // Meltype.exe が、文字に関係なく受け取るように言ったキー (capture.keys) か
    bool CapturesKey(UINT vk, LPARAM lParam) const;
    // listed: capture.keys のキー (入力モードを表示する位置も送る)。repeat: 押したままの自動の繰り返し
    void HandleKey(ITfContext* context, UINT vk, wchar_t ch, bool repeat, bool listed);
    // 左右の Alt (VK_LMENU / VK_RMENU) の押下・解放を本体に送る (ImeCapture.AltKeys)。キーのシンクには Alt (WM_SYSKEYDOWN) が届かないので、
    // Meltype.exe の低レベルフックが見て kAltKey で知らせる (TipServer.OnKey)。単独押しかは本体が決め、解放は Meltype.exe が送り直す
    void HandleAltKey(UINT vk, UINT flags);
    bool IsReinjected(UINT vk);
    bool Reinject(UINT vk);
    void RememberPassed(UINT vk);
    void ForgetSecretField();
    // Meltype.exe から受け取った切り替えキーのうち、修飾キー付きのものを TSF (PreserveKey) に登録し直す
    void RefreshPreservedKeys();
    void UnpreserveKeys();

    // ---- Meltype.exe とのやり取り ----
    // 出来事を送って応答を読む。field は ImeField の JSON (読まないなら空)、anchor は入力モードを表示する位置
    bool Send(const char* type, const std::string& field, const std::string& fields, JsonValue& reply, DWORD timeoutMs = 800,
              bool optional = false, const RECT* anchor = nullptr);
    // 入力欄の事実 (ImeField) を読む (編集セッションの中で)。パスワード欄なら false (Meltype.exe に文字を送らない)
    bool ReadField(TfEditCookie ec, ITfContext* context, std::string& field);
    // 応答の状態を覚える。古い状態 (rev が小さい) では上書きしない
    void ApplyState(const JsonValue& state);
    // 応答の開閉を TSF のコンパートメントに入れる (編集セッションの外で)
    void FlushOpen();
    // 確定 (commit) の出来事の追加の項目
    static std::string CaretMoved(bool left);
    // 前の入力欄の、日本語にしていたコードの行などを忘れさせる (キャレットが動いた)
    void SendCaret(TfEditCookie ec, ITfContext* context, bool selectionChanged);
    // 状態を受け取る。type は sync か focus (入力欄にフォーカスが入った)
    // entered: 入力欄にフォーカスが入った (入力欄の事実も送る)
    void Sync(bool entered = false);
    // 変換中だけ、背景の変換の結果が届くのを専用の接続で待つ (Meltype.exe は応答でしか知らせられない)
    void StartConversionWait();
    void FinishConversionWait();
    static constexpr UINT kConversionWaitDone = WM_APP + 1;
    // Meltype.exe (TipServer.AltKeyMessage) が送る、左右の Alt の押下・解放。wParam は仮想キーコード、lParam は次の印 (TipServer.AltFlags と同じ)
    static constexpr UINT kAltKey = WM_APP + 2;
    static constexpr UINT kAltShift = 1, kAltCtrl = 2, kAltCommand = 8, kAltReleased = 0x100, kAltInterrupted = 0x200;
    void CreateMessageWindow();
    void DestroyMessageWindow();
    static LRESULT CALLBACK MessageWindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam);

    // ---- 入力欄への反映 ----
    // 応答 (ImeOutput) を入力欄に反映する (編集セッションの中で)。使ったキーなら true
    bool ApplyOutput(TfEditCookie ec, ITfContext* context, const JsonValue& reply);
    void Apply(TfEditCookie ec, ITfContext* context, const JsonValue& result);
    void MoveCaretAfterCommit(TfEditCookie ec, ITfContext* context, ITfRange* range, const std::wstring& text, int count);
    bool ExtendOverPrevious(TfEditCookie ec, ITfRange* range, LONG count, const JsonValue& expect);
    void RewriteBeforeCaret(ITfContext* context, ITfRange* end, LONG count, const std::wstring& before, const std::wstring& text);
    bool WriteCommit(TfEditCookie ec, ITfContext* context, ITfRange* range, const std::wstring& text, int caretBack = 0, ITfRange** movedCaret = nullptr);
    void ApplyComposition(TfEditCookie ec, ITfContext* context, const JsonValue& view);
    void ShowCandidates(TfEditCookie ec, ITfContext* context, const JsonValue& view);
    void EndComposition(TfEditCookie ec);
    void SetAttribute(TfEditCookie ec, ITfContext* context, ITfRange* range, TfGuidAtom atom);
    // 未確定の内容を確定する (フォーカスが移った・英数にしたなど)。非同期の編集セッションで行う
    void CommitAsync(bool keepAppCaret, bool left = false);
    // 同期で確定する。できなければ false。keepAppCaret: キャレットを動かさず、前の語も確定し直さない
    bool CommitNow(bool keepAppCaret = false, bool left = false);
    void CommitInSession(TfEditCookie ec, ITfContext* context, bool keepAppCaret, bool left = false);
    // アプリ (や、確定できない前の入力欄) が変換中の文字を表示のまま確定した: Meltype.exe も確定したことにする
    void CommittedOutside();
    void AbandonComposition();
    void ResumeOrCommit(ITfContext* context);
    void SelectCandidate(int index);
    // キャレットの前を読めたら true (読めなければ before は空のまま。読めない入力欄を調べられるように、変わったときだけ tip-log に書く)
    bool SurroundingText(TfEditCookie ec, ITfContext* context, std::wstring& before, std::wstring& after, std::wstring& selected);
    bool beforeUnreadable_ = false;
    // 選択範囲の画面上の位置 (入力モードの表示に使う)。分からなければ空
    RECT CaretRect(TfEditCookie ec, ITfContext* context);
    bool ContextDisabled(ITfContext* context);
    void AdviseTextEditSink(ITfDocumentMgr* documentMgr);
    void UnadviseTextEditSink();
    bool IsSecretField(TfEditCookie ec, ITfContext* context);
    // 入力欄の種類を読む読み取りの編集セッション。1 = パスワード欄、0 = 違う、-1 = 読めない (キーの編集セッションの中で読む)
    int ReadSecretField(ITfContext* context);
    void SetOpen(bool open);
    void UpdateLangBar();
    bool ForgetAbandoned(ITfComposition* composition);
    void EndAbandonedNow();

    LONG refs_ = 1;
    ITfThreadMgr* threadMgr_ = nullptr;
    TfClientId clientId_ = TF_CLIENTID_NULL;
    DWORD threadMgrCookie_ = TF_INVALID_COOKIE;
    DWORD compartmentCookie_ = TF_INVALID_COOKIE;
    ITfContext* editSinkContext_ = nullptr;
    DWORD editSinkCookie_ = TF_INVALID_COOKIE;

    ITfComposition* composition_ = nullptr;
    ITfContext* compositionContext_ = nullptr;

    TfGuidAtom atomInput_ = TF_INVALID_GUIDATOM;
    TfGuidAtom atomConverted_ = TF_INVALID_GUIDATOM;
    TfGuidAtom atomTarget_ = TF_INVALID_GUIDATOM;

    PipeClient pipe_;
    std::string instance_;
    ServerState state_;
    bool open_ = true;             // TSF の IME の開閉 (OS の事実)
    int pendingOpen_ = -1;         // 応答の開閉を、編集セッションを終えてから入れる (-1 = 入れない)
    bool settingOpen_ = false;     // 自分で開閉を変えている (OnChange で Meltype.exe に伝え直さない)
    bool disabled_ = false;        // サインインや UAC の画面では何もしない
    bool deactivated_ = false;     // 終わった後は Meltype.exe とやり取りしない
    HWND window_ = nullptr;        // 変換の結果を待ち終わったことを IME のスレッドで受ける、表示しないウィンドウ
    std::shared_ptr<PipeClient> conversionPipe_;  // wait 専用。入力用の pipe_ を塞がない
    std::future<int> conversionWait_;  // 待っている wait は 1 つだけ。世代を返す (失敗は -1)
    int conversionGeneration_ = -1;    // 知っている世代 (Meltype.exe の ConversionSignal)。-1 は未取得

    UINT passedVk_ = 0;            // 最後にアプリに通したキー (キャレットが動いたときに Meltype.exe に伝える)
    bool passedControl_ = false;   // そのとき Ctrl を押していたか
    ULONGLONG passedTime_ = 0;
    bool eatenDown_[256] = {};     // 受け取って、まだ離していないキー (離したことも受け取る。押したままなら自動の繰り返し)
    struct PreservedKey { TF_PRESERVEDKEY key; GUID guid; };
    std::vector<PreservedKey> preservedKeys_;
    std::vector<ToggleShortcut> preservedFrom_;  // 登録した切り替えキー (変わったときだけ登録し直す)
    struct Reinjected {
        UINT vk = 0;
        ULONGLONG time = 0;
    };
    Microsoft::WRL::ComPtr<ITfRange> resumeRange_;  // アプリが確定した打ちかけの文字 (次のキーで変換中に戻す)
    Microsoft::WRL::ComPtr<ITfContext> resumeContext_;
    std::wstring resumeText_;
    bool lastConverting_ = false;  // 最後に置いた変換中の文字が、変換した状態か (打っている途中なら false)
    int staleCommitTries_ = 0;     // 前の入力欄に残った変換中の文字を確定しようとした回数 (打鍵ごとに 1 回)
    UINT staleVk_ = 0;             // OnTestKeyDown で数えたキーと時刻 (続く OnKeyDown で数え直さない)
    ULONGLONG staleTime_ = 0;
    struct Abandoned {
        Microsoft::WRL::ComPtr<ITfComposition> composition;
        Microsoft::WRL::ComPtr<ITfContext> context;
    };
    std::vector<Abandoned> abandoned_;  // 手放した前の入力欄の変換中の文字 (あとで下線を消して終える)
    UINT reinjectGuardVk_ = 0;     // 同じキーを続けて送り直した回数 (送り直しが止まらなくなるのを防ぐ)
    ULONGLONG reinjectGuardTime_ = 0;
    int reinjectGuardCount_ = 0;
    bool markerSeen_ = false;      // このアプリには送り直しの印が届く
    Reinjected reinjected_[4];     // 送り直して、まだ届いていないキー (それぞれ 1 回だけ素通しする)
    UINT passVk_ = 0;              // OnTestKeyDown で素通しにしたキー
    ULONGLONG passTime_ = 0;
    bool ownEdit_ = false;         // 自分の編集セッションが終わった通知を、キャレットが動いたことにしない
    bool secretChecked_ = false;   // このキーで、入力欄の種類 (パスワード欄でないこと) を確かめた
    bool secretWhileComposing_ = false;  // 変換中にパスワード欄になったが、受け取る前に確定できなかった (キーの処理の中で確定して送り直す)
    UINT secretVk_ = 0;            // OnTestKeyDown で入力欄の種類を読んだキー・入力欄・時刻・結果 (続く OnKeyDown で 1 回だけ使う)
    Microsoft::WRL::ComPtr<ITfContext> secretContext_;
    ULONGLONG secretTime_ = 0;
    int secretResult_ = -1;
    bool secretPrivate_ = false;
    std::wstring lastSecretScopes_;  // 調べるとき用: 前にパスワードとみなした入力欄の種類 (同じならログに書かない)
    bool privateField_ = false;    // 前に入力欄の種類を読んだとき、入力を覚えないように求める入力欄 (IS_PRIVATE) だった
    const wchar_t* scopeSource_ = nullptr;  // 入力欄の種類を前に読んだところ (ログ用)
    bool keepAppCaret_ = false;    // 確定したあと、キャレットを動かさない (クリック・別の入力欄で確定したとき)
    const wchar_t* passReason_ = nullptr;  // 最後にキーを通した理由 (ログに同じ理由を続けて書かない)

    std::unique_ptr<CandidateWindow> candidates_;
    LangBarButton* langBar_ = nullptr;
};

// ITfEditSession を関数で作る
HRESULT RunEditSession(ITfContext* context, TfClientId clientId, DWORD flags, std::function<HRESULT(TfEditCookie)> action);

}  // namespace meltype
