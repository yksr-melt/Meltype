// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai
//
// Meltype IME のテキストサービス本体。キーを Meltype.exe に渡し、返ってきた結果
// (確定する文字・未確定の文字・候補) を入力欄に反映する。

#pragma once

#include <wrl/client.h>

#include <memory>
#include <future>
#include <vector>

#include "CandidateWindow.h"
#include "Globals.h"
#include "Json.h"
#include "Pipe.h"
#include "Shortcut.h"

namespace meltype {

class LangBarButton;

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

    // 入力モード (日本語 = 開いている / 英数 = 閉じている)。タスクバーのボタンから使う。
    // コードエディターのコードの行では、IME は開いたまま、次に打つキーが英数のままアプリに通るので「A」と出す
    bool IsOpen() const { return open_ && !codeEnglish_; }
    void SetOpen(bool open);
    // 登録された切り替えキーやタスクバーのボタンから日本語 ⇔ 英数を切り替える。
    // context が null の場合はフォーカス中の入力欄を使用する。
    void ToggleInputMode(ITfContext* context);

private:
    ~TextService();

    // test: OnTestKeyDown から (入力欄の種類を読んだ結果を、続く OnKeyDown で使うために覚える)
    bool WouldEat(ITfContext* context, UINT vk, LPARAM lParam, wchar_t& ch, bool test);
    void ForgetSecretField();
    bool IsReinjected(UINT vk);
    // 入力要求を試せるか。未確認なら要求を送り、サーバーの応答で判断する 
    bool ServerReady();
    // 設定の非同期取得を開始するか、完了済みの応答を反映する。通信完了は待たない。
    void Hello();
    // 取得した設定を反映する。currentState が false なら、入力応答で更新済みの状態は上書きしない。
    void ApplyHello(const JsonValue& reply, bool currentState);
    // 設定取得と、その結果を IME のスレッドで反映するためのタイマーを開始する。
    void StartHelloUpdates();
    // タイマーを止め、取得処理の終了を待ってから接続を解放する。
    void StopHelloUpdates();
    static LRESULT CALLBACK HelloWindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam);
    // Meltype.exe から受け取った入力モード切り替えショートカットを TSF (PreserveKey) に再登録する
    void RefreshPreservedShortcuts();
    // TSF に登録した入力モード切り替えショートカットを解除する
    void UnpreserveShortcuts();
    // 押されたキーと修飾キーの状態が、登録された切り替えショートカットのいずれかに一致するか判定する
    bool MatchesToggle(UINT vk, LPARAM lParam);
    void HandleKey(ITfContext* context, UINT vk, wchar_t ch, bool toggleText = false);
    // コードの行を日本語にする / 英数に戻すよう Meltype.exe に頼む。そうしたなら true
    bool SetCodeLine(ITfContext* context, bool japanese);
    // キャレットが動いたときに、次に打つキーが英数か Meltype.exe に聞く (コードエディターのとき。編集の通知の中で呼ぶ)
    void QueryMode(TfEditCookie ec, ITfContext* context, bool selectionChanged);
    // アプリに通したキーを覚える (QueryMode で伝える)
    void RememberPassed(UINT vk);
    // まだ伝えていない、アプリに通したキー (要求に足す JSON。無ければ空)
    std::string PendingPassed();
    // 応答の code / english を入力モードの表示に反映する
    void ApplyMode(const JsonValue& reply);
    // 要求を送って応答を読む。失敗したら false (optional は PipeClient::Transact と同じ)
    bool Request(const std::string& json, JsonValue& reply, DWORD timeoutMs = 800, bool optional = false);
    // 応答を入力欄に反映する (編集セッションの中で呼ぶ)
    void Apply(TfEditCookie ec, ITfContext* context, const JsonValue& reply);
    // range の前の count 文字が expect (前に確定した文字) なら、range をそこまで広げる。違えば false
    bool ExtendOverPrevious(TfEditCookie ec, ITfRange* range, LONG count, const JsonValue& expect);
    // 次の編集セッションで、キャレットの前の count 文字が before のままなら text に書き換えて確定する
    void RewriteBeforeCaret(ITfContext* context, LONG count, const std::wstring& before, const std::wstring& text);
    // range を text で置き換えて確定する (アプリによっては変換中の文字としてでないと入らないので、いったん変換中にする)
    bool WriteCommit(TfEditCookie ec, ITfContext* context, ITfRange* range, const std::wstring& text);
    void ApplyComposition(TfEditCookie ec, ITfContext* context, const JsonValue& view);
    void ShowCandidates(TfEditCookie ec, ITfContext* context, const JsonValue& view);
    void EndComposition(TfEditCookie ec);
    void SetAttribute(TfEditCookie ec, ITfContext* context, ITfRange* range, TfGuidAtom atom);
    // 未確定の内容を確定する (フォーカスが移った・英数にしたなど)。非同期の編集セッションで行う
    void CommitAsync(bool keepAppCaret);
    // 同期で確定する。できなければ false。keepAppCaret: キャレットを動かさず、前の語も確定し直さない
    bool CommitNow(bool keepAppCaret = false);
    // 確定できない前の入力欄の変換中の文字を手放す
    void AbandonComposition();
    // アプリが確定した打ちかけの文字を、変換中に戻す (戻せなければ確定したことにする)。context が null なら確定したことにする
    void ResumeOrCommit(ITfContext* context);
    void CommitInSession(TfEditCookie ec, ITfContext* context, bool keepAppCaret);
    void SelectCandidate(int index, bool prediction);
    void Reinject(UINT vk);
    void SurroundingText(TfEditCookie ec, ITfContext* context, std::wstring& before, std::wstring& after);
    bool ContextDisabled(ITfContext* context);
    void AdviseTextEditSink(ITfDocumentMgr* documentMgr);
    bool IsSecretField(TfEditCookie ec, ITfContext* context);
    // 入力欄の種類を読む読み取りの編集セッション。1 = パスワード欄、0 = 違う、-1 = 読めない (キーの編集セッションの中で読む)
    int ReadSecretField(ITfContext* context);
    std::string CommitRequest(bool moved) const;
    void UnadviseTextEditSink();
    void UpdateLangBar();
    // 明示的に入力モードを切り替えたとき、常駐側の「あ / A」を一瞬表示する。
    void FlashModeIndicator(ITfContext* context = nullptr);

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
    std::string sid_;
    bool serverActive_ = false;
    bool serverStateKnown_ = false; // 未取得・通信失敗は「無効」という応答と区別する
    unsigned long long serverStateRevision_ = 0; // 入力要求の完了ごとに進める状態の世代
    unsigned long long helloStateRevision_ = 0; // 設定取得を開始した時点の世代
    bool directDetection_ = false; // 英数状態でもローマ字検知が有効 (Meltype.exe の応答)
    bool directEnglishWord_ = false;
    bool modeIndicator_ = false;   // 設定「Meltype IME でも入力モードを表示」(応答の modeIndicator。ON のときだけ FlashModeIndicator が動く)
    bool fullWidthSpace_ = false;  // 設定「スペースを全角で入力」(Meltype.exe の応答の fullWidthSpace。日本語入力のときは Space も受け取る)
    ULONGLONG lastHello_ = 0;
    HWND helloWindow_ = nullptr; // IME のスレッドでタイマーを受ける、表示しないウィンドウ
    std::shared_ptr<PipeClient> helloPipe_; // 設定取得専用。入力用の pipe_ と同時に使える
    std::future<std::pair<bool, JsonValue>> helloResult_; // 取得中の要求は 1 つだけ。成功したかと応答を保持
    bool open_ = true;
    bool codeApp_ = false;         // コードエディター・ターミナル (Meltype.exe の応答の code)
    bool codeEnglish_ = false;     // 次に打つキーがコードの行なので英数のまま通る (Meltype.exe の応答の english)
    UINT passedVk_ = 0;            // 最後にアプリに通したキー (キャレットが動いたときに Meltype.exe に伝える)
    bool passedControl_ = false;   // そのとき Ctrl を押していたか
    ULONGLONG passedTime_ = 0;
    bool passedPending_ = false;   // そのキーをまだ Meltype.exe に伝えていない
    bool disabled_ = false;
    bool ownProcess_ = false;      // Meltype.exe 自身の中で動いている (要求にスレッドを付ける)  // Meltype.exe 自身の中では動かない (自分のサーバーを待って固まるため)
    bool eatenDown_[256] = {};
    bool toggleDown_[256] = {};
    bool shortcutsEnabled_ = true; // 初回接続前は従来の初期登録。接続後は最後に受け取った設定。
    std::vector<ToggleShortcut> toggleShortcuts_ = {{0x19, 0}, {0xC0, 4}};
    struct PreservedToggle { TF_PRESERVEDKEY key; GUID guid; };
    std::vector<PreservedToggle> preservedToggles_;
    struct Reinjected {
        UINT vk = 0;
        ULONGLONG time = 0;
    };
    Microsoft::WRL::ComPtr<ITfRange> resumeRange_;      // アプリが確定した打ちかけの文字 (次のキーで変換中に戻す)
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
    // 手放した変換中の文字の一覧から外す。一覧にあれば true
    bool ForgetAbandoned(ITfComposition* composition);
    // 終わるときに、手放した変換中の文字をその場で終える
    void EndAbandonedNow();
    UINT reinjectGuardVk_ = 0;     // 同じキーを続けて送り直した回数 (送り直しが止まらなくなるのを防ぐ)
    ULONGLONG reinjectGuardTime_ = 0;
    int reinjectGuardCount_ = 0;
    bool markerSeen_ = false;      // このアプリには送り直しの印が届く
    Reinjected reinjected_[4];     // 送り直して、まだ届いていないキー (それぞれ 1 回だけ素通しする)
    UINT passVk_ = 0;              // OnTestKeyDown で素通しにしたキー
    ULONGLONG passTime_ = 0;
    bool caretMoved_ = false;      // 前に確定してから、キャレットが動いたかもしれない (次のキーで Meltype.exe に伝える)
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
    bool deactivated_ = false;     // 終わった後は Meltype.exe とやり取りしない
    const wchar_t* passReason_ = nullptr;  // 最後にキーを通した理由 (ログに同じ理由を続けて書かない)
    bool keyFailed_ = false;
    bool consumed_ = true;

    std::unique_ptr<CandidateWindow> candidates_;
    LangBarButton* langBar_ = nullptr;
};

// ITfEditSession を関数で作る
HRESULT RunEditSession(ITfContext* context, TfClientId clientId, DWORD flags, std::function<HRESULT(TfEditCookie)> action);

}  // namespace meltype
