// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
// Copyright (C) 2026 aster-void

import Carbon.HIToolbox
import Cocoa
import InputMethodKit
import UniformTypeIdentifiers

/// 入力欄 (クライアント) ごとの IME。Input Method Kit から読んだ事実 (キー・キャレットの前後の文字) を本体 (libMeltypeNative.dylib の ImeHost) に渡し、
/// 返ってきた指示 (確定する文字・変換中の表示・候補・入力モードの表示) を入力欄に反映する。入力モードは本体が決めるので、ここでは持たない。
/// Info.plist の InputMethodServerControllerClass に書いた名前で、Input Method Kit が作る。
@objc(MeltypeInputController)
final class MeltypeInputController: IMKInputController, ModeServiceTarget {
    /// 本体が最後に返した表示のモード (入力メニューのチェックに使う)
    private var japanese = true
    private var hasMarkedText = false
    private var candidateList: [String] = []
    private let modeIndicator = ModeIndicator.shared
    private var auxPanel: NSPanel?
    private var displayedAux: String?

    /// 判定の文脈とコードの行の判定に使う、キャレットの前の文字数
    private static let beforeLength = 4000
    private static let afterLength = 20

    /// フォーカスのある入力欄。背景の変換の結果が届いたら、この入力欄の表示を作り直す。メインスレッドからだけ使う。
    nonisolated(unsafe) static weak var active: MeltypeInputController?

    /// 入力の本体 (ImeHost)。Input Method Kit は入力欄ごとにこのクラスを作るが、入力モードはすべての入力欄で同じにするので、プロセスで 1 つにする。
    /// メインスレッドからだけ使う。
    nonisolated(unsafe) private static var host = NativeCore.shared.createHost()

    /// 送るのは同じプロセスの importUserDictionary だけなので、ほかのプロセスから届く分散通知では受けない
    nonisolated(unsafe) private static let dictionaryObserver = NotificationCenter.default.addObserver(
        forName: Notification.Name("MeltypeUserDictionaryChanged"), object: nil, queue: .main
    ) { _ in
        // 取り込んだユーザー辞書は、入力の本体を作るときに読む
        if let active = MeltypeInputController.active { active.commitComposition(active.client()) }
        NativeCore.shared.destroyHost(MeltypeInputController.host)
        MeltypeInputController.host = NativeCore.shared.createHost()
    }

    override init!(server: IMKServer!, delegate: Any!, client inputClient: Any!) {
        super.init(server: server, delegate: delegate, client: inputClient)
        _ = Self.dictionaryObserver
    }

    // ---- 本体とのやり取り ----

    /// 出来事を本体に送り、返ってきた指示を入力欄に出す。本体が処理できなければ nil。
    @discardableResult
    private func send(_ type: String, client: IMKTextInput?, _ fields: [String: Any] = [:]) -> ImeOutput? {
        var event: [String: Any] = ["version": 1, "type": type, "instance": "mac"]
        if let client { event["field"] = field(of: client) }
        event.merge(fields) { _, new in new }
        guard let output = NativeCore.shared.send(Self.host, event) else { return nil }
        japanese = output.state.languageMode == .japanese
        if let client { apply(output.result, to: client) }
        if output.state.modeIndicatorMs > 0 {
            modeIndicator.show(japanese ? .japanese : .direct, caret: client.flatMap { modeIndicatorCaret($0) }, milliseconds: output.state.modeIndicatorMs)
        }
        return output
    }

    /// 入力欄の事実 (ImeField)。キャレットの前後は、変換中なら変換中の文字の前後
    private func field(of client: IMKTextInput) -> [String: Any] {
        var field: [String: Any] = ["composing": hasMarkedText]
        if let app = client.bundleIdentifier() { field["app"] = app }
        let marked = client.markedRange()
        let anchor = marked.location != NSNotFound && marked.length > 0 ? marked : client.selectedRange()
        guard anchor.location != NSNotFound else {
            noteBefore(unreadable: true) { "selectedRange が NSNotFound" }
            return field
        }
        if anchor.length > 0, !hasMarkedText, let selection = client.attributedSubstring(from: anchor)?.string { field["selection"] = selection }
        let start = max(0, anchor.location - Self.beforeLength)
        let requested = NSRange(location: start, length: anchor.location - start)
        let before = client.attributedSubstring(from: requested)?.string
        if let before { field["before"] = before }
        noteBefore(unreadable: before == nil) {
            // 原因を調べるために、短い範囲なら読めるかも書く
            let shortStart = max(0, anchor.location - 20)
            let short = client.attributedSubstring(from: NSRange(location: shortStart, length: anchor.location - shortStart)) != nil
            return "頼んだ範囲 \(NSStringFromRange(requested))・selectedRange \(NSStringFromRange(client.selectedRange()))・" +
                "markedRange \(NSStringFromRange(marked))・20 文字前からなら\(short ? "読めた" : "読めない")"
        }
        if let after = client.attributedSubstring(from: NSRange(location: anchor.location + anchor.length, length: Self.afterLength))?.string { field["after"] = after }
        return field
    }

    /// キャレットの前の文字を読めないか (読めない入力欄を調べられるように、変わったときだけ Console に書く)
    private var beforeUnreadable = false

    private func noteBefore(unreadable: Bool, _ detail: () -> String) {
        guard unreadable != beforeUnreadable else { return }
        beforeUnreadable = unreadable
        NSLog("Meltype: %@", unreadable ? "キャレットの前の文字を読めません (\(detail()))" : "キャレットの前の文字を読めるようになりました")
    }

    func refreshConversion() {
        send("ConversionFinished", client: client())
    }

    // ---- Input Method Kit からの呼び出し ----

    override func activateServer(_ sender: Any!) {
        super.activateServer(sender)
        Self.active = self
        ModeServices.shared.activate(self)
        send("CaretMoved", client: client(), ["caret": ["moved": true, "passedKey": 0, "focusEntered": true]])
    }

    override func recognizedEvents(_ sender: Any!) -> Int {
        Int(NSEvent.EventTypeMask.keyDown.rawValue)
    }

    override func handle(_ event: NSEvent!, client sender: Any!) -> Bool {
        guard let event, event.type == .keyDown, let client = sender as? IMKTextInput else { return false }
        let flags = event.modifierFlags
        var key: [String: Any] = ["vk": KeyMapping.virtualKey(for: event),
                                  "shift": flags.contains(.shift), "ctrl": flags.contains(.control),
                                  "alt": flags.contains(.option), "command": flags.contains(.command), "repeat": event.isARepeat]
        // event.characters は「見た目の 1 文字」で、UTF-16 要素が複数のことがある
        // (補助面の文字・結合文字・ZWJ 絵文字・異体字セレクター)。1 スカラーならそのコードポイント、複数ならそのまま渡す
        let text = KeyMapping.text(for: event)
        let scalars = Array(text.unicodeScalars)
        if scalars.count > 1 {
            key["text"] = text
        } else if let scalar = scalars.first {
            // JIS キーボードの Option+Space などは全角・ノーブレークの空白を入れる。Space として渡す (本体が全角・半角を決める)
            let space = scalar.value == 0x20 || scalar.value == 0x3000 || scalar.value == 0xA0
            key["ch"] = space ? 0x20 : scalar.value
            if space { key["vk"] = 0x20 }
        }
        return send("KeyPressed", client: client, ["key": key])?.result.consumed ?? false
    }

    /// フォーカスが外れた・クリックで別の場所に移ったときなど。未確定の内容をそのまま確定する。
    override func commitComposition(_ sender: Any!) {
        guard let client = (sender as? IMKTextInput) ?? self.client() else { return }
        send("CommitRequested", client: client, ["caret": ["moved": true, "passedKey": 0]])
    }

    override func deactivateServer(_ sender: Any!) {
        if Self.active === self { Self.active = nil }
        ModeServices.shared.deactivate(self)
        if let client = (sender as? IMKTextInput) ?? self.client() {
            send("CommitRequested", client: client, ["caret": ["moved": true, "passedKey": 0, "focusLeft": true]])
        }
        candidatesWindow?.hide()
        auxPanel?.orderOut(nil)
        displayedAux = nil
        meaningKey = nil
        candidateList = []
        super.deactivateServer(sender)
    }

    // ---- 変換の候補の一覧 ----

    override func candidates(_ sender: Any!) -> [Any]! {
        candidateList
    }

    override func candidateSelected(_ candidateString: NSAttributedString!) {
        guard let client = self.client(),
              let string = candidateString?.string,
              let index = candidateList.firstIndex(of: string) else { return }
        // クリックした候補が候補ウィンドウで選ばれているので、覚えている位置も合わせる。
        windowIndex = index
        send("CandidateClicked", client: client, ["index": index])
    }

    /// 候補ウィンドウの中で選択が動いた (マウスで選んだときなど)。本体の選択も合わせる (確定はしない)。
    /// 本体の選択を候補ウィンドウに反映しているとき (selectingFromCore) にも呼ばれるので、そのときは何もしない。
    /// moveDown / moveUp の通知が遅れて届くと古い番号に戻してしまうので、マウスの操作のときだけ受け付ける。
    private var selectingFromCore = false

    override func candidateSelectionChanged(_ candidateString: NSAttributedString!) {
        let mouseTypes: [NSEvent.EventType] = [.leftMouseDown, .leftMouseUp, .leftMouseDragged]
        guard !selectingFromCore,
              let type = NSApp.currentEvent?.type, mouseTypes.contains(type),
              let client = self.client(),
              let string = candidateString?.string,
              let index = candidateList.firstIndex(of: string) else { return }
        windowIndex = index
        send("CandidateHighlighted", client: client, ["index": index])
    }

    // ---- メニュー (メニューバーの入力メニュー) ----

    override func menu() -> NSMenu! {
        let menu = NSMenu()
        for (mode, action) in [(MacInputMode.japanese, #selector(selectJapanese(_:))), (MacInputMode.direct, #selector(selectDirect(_:)))] {
            let item = menu.addItem(withTitle: mode.title, action: action, keyEquivalent: "")
            item.target = self
            item.state = (mode == .japanese) == japanese ? .on : .off
        }
        menu.addItem(.separator())
        let importItem = menu.addItem(withTitle: "macOSのユーザー辞書を取り込む…", action: #selector(importUserDictionary(_:)), keyEquivalent: "")
        importItem.target = self
        let configItem = menu.addItem(withTitle: "Meltype の設定ファイル (config.json) を開く…", action: #selector(openConfigFile(_:)), keyEquivalent: "")
        configItem.target = self
        menu.addItem(withTitle: "Meltype のデータフォルダを開く (設定・ユーザー辞書)", action: #selector(openDataFolder(_:)), keyEquivalent: "")
        menu.addItem(withTitle: "不具合の報告・提案… (Mac 版はプレビュー版です)", action: #selector(openReport(_:)), keyEquivalent: "")
        return menu
    }

    @objc private func selectJapanese(_ sender: Any?) { send("JapaneseModeSelected", client: client()) }
    @objc private func selectDirect(_ sender: Any?) { send("AlphanumericModeSelected", client: client()) }

    func selectModeFromService(_ action: ModeShortcutAction) -> Bool {
        guard let client = client() else { return false }
        switch action {
        case .japanese: send("JapaneseModeSelected", client: client)
        case .direct: send("AlphanumericModeSelected", client: client)
        case .toggle: send("ModeToggleRequested", client: client)
        }
        return true
    }

    private func modeIndicatorCaret(_ client: IMKTextInput) -> NSRect? {
        // IMK のインライン位置。0 はインラインセッションがない場合の現在の選択を表す。
        let marked = client.markedRange()
        let index = marked.location == NSNotFound ? 0 : marked.length
        var rectangle = NSRect.zero
        _ = client.attributes(forCharacterIndex: index, lineHeightRectangle: &rectangle)
        return ModeIndicator.validCaret(rectangle) ? rectangle : nil
    }

    @objc private func importUserDictionary(_ sender: Any?) {
        commitComposition(client())
        // 入力メニューの処理を抜けてから、操作できる画面として表示する。
        DispatchQueue.main.async {
            do {
                try ImportDialogPresentation.run {
                    Self.importUserDictionaryFile()
                }
            } catch {
                NSLog("Meltype: %@", error.localizedDescription)
            }
        }
    }

    private static func importUserDictionaryFile() {
        let panel = NSOpenPanel()
        // 取り込めるのは、macOS の「ユーザ辞書」から書き出した plist だけ。
        // ~/Library/KeyboardServices の TextReplacements.db は Apple 内部の形式なので読まない。
        panel.message = MacUserDictionary.exportGuide
        panel.directoryURL = FileManager.default.urls(for: .desktopDirectory, in: .userDomainMask).first
        panel.allowedContentTypes = [.propertyList]
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false
        guard panel.runModal() == .OK, let source = panel.url,
              let directory = NativeCore.shared.dataDirectory else { return }
        let alert = NSAlert()
        do {
            let result = try MacUserDictionary.importFile(source, directory: directory)
            alert.messageText = "ユーザー辞書に\(result.added)語を追加しました"
            alert.informativeText = "無効な項目・登録済みの項目を\(result.skipped)語飛ばしました。"
            NotificationCenter.default.post(name: Notification.Name("MeltypeUserDictionaryChanged"), object: nil)
        } catch {
            alert.messageText = "ユーザー辞書を取り込めませんでした"
            alert.informativeText = error.localizedDescription + "\n\n" + MacUserDictionary.exportGuide
        }
        alert.runModal()
    }

    @objc private func openReport(_ sender: Any?) {
        guard let url = NativeCore.shared.reportUrl else { return }
        NSWorkspace.shared.open(url)
    }

    /// 設定ファイル (Windows 版と共通の config.json) を、無ければ空の設定で作ってから、既定のエディターで開く (issue #508)。
    /// 空の設定にも、本体が隣に書く config.schema.json を指定しておく (エディターで項目の補完・説明が出る)。
    @objc private func openConfigFile(_ sender: Any?) {
        guard let directory = NativeCore.shared.dataDirectory else { return }
        let url = URL(fileURLWithPath: directory, isDirectory: true).appendingPathComponent("config.json")
        if !FileManager.default.fileExists(atPath: url.path) {
            try? "{\n  \"$schema\": \"./config.schema.json\"\n}\n".write(to: url, atomically: true, encoding: .utf8)
        }
        NSWorkspace.shared.open(url)
    }

    @objc private func openDataFolder(_ sender: Any?) {
        guard let directory = NativeCore.shared.dataDirectory else { return }
        NSWorkspace.shared.open(URL(fileURLWithPath: directory, isDirectory: true))
    }

    // ---- 指示を入力欄に反映する ----

    private func apply(_ result: SessionResult, to client: IMKTextInput) {
        for edit in result.commits {
            // 確定し直し: キャレット (変換中の文字があればその先頭) の前の remove を置き換える (本体が照合済み)
            var range = NSRange(location: NSNotFound, length: 0)
            if let remove = edit.removeBefore {
                let length = (remove as NSString).length
                let marked = client.markedRange()
                let caret = marked.location != NSNotFound && marked.length > 0 ? marked.location : client.selectedRange().location
                if caret != NSNotFound && caret >= length { range = NSRange(location: caret - length, length: length) }
            }
            client.insertText(NSAttributedString(string: edit.text), replacementRange: range)
            hasMarkedText = false
        }
        if let view = result.view {
            showComposition(view, client: client)
        } else {
            hideComposition(client: client)
        }
    }

    /// 変換中の文字を入力欄に下線付きで出す (変換中は文節ごと、選んでいる文節は太い下線)。
    private func showComposition(_ view: CompositionView, client: IMKTextInput) {
        let text = NSMutableAttributedString(string: view.text)
        let length = (view.text as NSString).length
        if view.converting && !view.clauses.isEmpty {
            var location = 0
            for (index, clause) in view.clauses.enumerated() {
                let clauseLength = (clause as NSString).length
                let style = index == view.selectedClause ? kTSMHiliteSelectedConvertedText : kTSMHiliteConvertedText
                addMark(style, to: text, range: NSRange(location: location, length: clauseLength))
                location += clauseLength
            }
        } else {
            addMark(kTSMHiliteRawText, to: text, range: NSRange(location: 0, length: length))
        }
        client.setMarkedText(NSAttributedString(attributedString: text), selectionRange: NSRange(location: length, length: 0), replacementRange: NSRange(location: NSNotFound, length: 0))
        hasMarkedText = length > 0
        updateCandidates(view.candidates, client: client)
        // 打ったキー・もしかして・案内を、入力位置の下のパネルに 1 行で出す
        let aux = [view.typedKeys, view.spellingSuggestion, view.hint].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: "　")
        showAux(aux.isEmpty ? nil : aux, client: client)
    }

    private func hideComposition(client: IMKTextInput) {
        if hasMarkedText {
            // Keep the payload type consistent when clearing marked text.
            client.setMarkedText(NSAttributedString(string: ""), selectionRange: NSRange(location: 0, length: 0), replacementRange: NSRange(location: NSNotFound, length: 0))
            hasMarkedText = false
        }
        candidateList = []
        candidatesWindow?.hide()
        auxPanel?.orderOut(nil)
        displayedAux = nil
        meaningKey = nil
    }

    private func showAux(_ text: String?, client: IMKTextInput) {
        let selection = client.selectedRange()
        guard let text, selection.location != NSNotFound else {
            auxPanel?.orderOut(nil)
            displayedAux = nil
            return
        }
        var caret = NSRect.zero
        _ = client.attributes(forCharacterIndex: selection.location, lineHeightRectangle: &caret)
        guard caret.minX.isFinite, caret.minY.isFinite,
              caret.width.isFinite, caret.height.isFinite, caret.height > 0 else {
            auxPanel?.orderOut(nil)
            displayedAux = nil
            return
        }
        displayedAux = text
        let label = NSTextField(labelWithString: text)
        label.font = .systemFont(ofSize: 13)
        label.sizeToFit()
        let size = NSSize(width: label.frame.width + 24, height: label.frame.height + 16)
        let panel = auxPanel ?? NSPanel(contentRect: NSRect(origin: .zero, size: size),
            styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.level = panelLevel(.floating, above: client)
        panel.hasShadow = true
        panel.backgroundColor = .windowBackgroundColor
        panel.ignoresMouseEvents = true
        panel.setContentSize(size)
        panel.contentView?.subviews.forEach { $0.removeFromSuperview() }
        label.frame.origin = NSPoint(x: 12, y: 8)
        panel.contentView?.addSubview(label)
        if let screen = NSScreen.screens.first(where: { $0.frame.intersects(caret) }) ?? NSScreen.main {
            let visible = screen.visibleFrame
            panel.setFrameOrigin(NSPoint(x: max(visible.minX, min(caret.minX, visible.maxX - size.width)),
                y: max(visible.minY, min(caret.minY - size.height - 6, visible.maxY - size.height))))
        }
        auxPanel = panel
        panel.orderFrontRegardless()
    }

    private func addMark(_ style: Int, to text: NSMutableAttributedString, range: NSRange) {
        guard range.length > 0, range.location + range.length <= text.length,
              let marks = mark(forStyle: style, at: range) else { return }
        var attributes: [NSAttributedString.Key: Any] = [:]
        for (key, value) in marks {
            if let name = key as? String {
                attributes[NSAttributedString.Key(name)] = value
            } else if let name = key as? NSAttributedString.Key {
                attributes[name] = value
            }
        }
        text.addAttributes(attributes, range: range)
    }

    /// 候補で本体が決めた時間 (meaningDelayMs) 止まったら、その候補の意味を候補ウィンドウの注釈に出す。
    private var meaningKey: String?

    private func scheduleMeaning(_ list: CandidateList) {
        let key = list.selectedMeaning.map { "\(list.selected):\($0)" }
        guard key != meaningKey else { return }
        meaningKey = key
        guard let key, let meaning = list.selectedMeaning else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + .milliseconds(list.meaningDelayMs)) { [weak self] in
            guard let self, self.meaningKey == key, let window = candidatesWindow, window.isVisible() else { return }
            window.showAnnotation(NSAttributedString(string: meaning))
        }
    }

    private func updateCandidates(_ list: CandidateList?, client: IMKTextInput) {
        guard let window = candidatesWindow else { return }
        guard let list else {
            meaningKey = nil
            candidateList = []
            window.hide()
            return
        }
        // 作り直し・選択で候補ウィンドウから candidateSelectionChanged が来ても、本体に返さない。
        selectingFromCore = true
        defer { selectingFromCore = false }
        // 注記 (候補の種類など) は候補の後ろに付ける
        let items = zip(list.items, list.notes).map { item, note in note.map { item + "　" + $0 } ?? item }
        // 一覧を作り直すと選択が先頭に戻るので、中身が変わったとき (別の文節に移ったときなど) だけ作り直す。
        if candidateList != items || !window.isVisible() {
            candidateList = items
            // IMK が入力位置を取り直すよう、更新前にいったん非表示にする。
            window.hide()
            window.update()
            window.show(kIMKLocateCandidatesBelowHint)
            // macOS 26 の IMK は入力位置の更新前に画面を選ぶため、画面移動直後は以前の画面に収まる。
            // 更新された入力位置で画面を選び直し、その後で選択位置を本体とそろえる。
            window.show(kIMKLocateCandidatesBelowHint)
            windowIndex = 0
        }
        setCandidatesWindowLevel(above: client)
        if list.selected >= 0 && list.selected < list.items.count {
            selectInWindow(window, index: list.selected)
        }
        scheduleMeaning(list)
    }

    /// 候補ウィンドウで今選ばれている候補の番号 (一覧を作り直すと先頭に戻る)。
    private var windowIndex = 0

    /// 候補ウィンドウ (IMKUIPanel) のもともとの表示レベル (macOS 15 で確かめた値。IMK のウィンドウはこの 1 つだけだった)。
    private static let defaultCandidatesLevel = NSWindow.Level(rawValue: 20)

    /// Spotlight のように、ふつうのウィンドウより高い表示レベルの入力欄では、候補ウィンドウや予測候補のパネルが入力欄の裏に隠れる。
    /// なので、もともとの表示レベルと「入力欄より 1 つ上」の高い方にする。
    /// 候補ウィンドウはすべての入力欄で共有するので、前の入力欄で上げたままにしないよう、毎回入れ直す。
    private func panelLevel(_ base: NSWindow.Level, above client: IMKTextInput) -> NSWindow.Level {
        NSWindow.Level(rawValue: max(base.rawValue, Int(client.windowLevel()) + 1))
    }

    /// IMKCandidates には表示レベルを変える口が無いので、このプロセスにある IMK のウィンドウ (候補ウィンドウは IMKUIPanel) を探して変える。
    private func setCandidatesWindowLevel(above client: IMKTextInput) {
        for window in NSApp.windows where String(describing: type(of: window)).hasPrefix("IMK") {
            window.level = panelLevel(Self.defaultCandidatesLevel, above: client)
        }
    }

    /// 候補ウィンドウの選択を index 番目の候補に合わせる。
    /// selectCandidate(withIdentifier:) では選択が動かず、selectedCandidate() も今の選択とずれることがある (macOS 26)。
    /// なので、選択の位置はこちらで覚えておき、矢印キーと同じ moveDown / moveUp でその差だけ動かす。
    private func selectInWindow(_ window: IMKCandidates, index: Int) {
        let step = index > windowIndex ? #selector(NSResponder.moveDown(_:)) : #selector(NSResponder.moveUp(_:))
        guard window.responds(to: step) else { return }
        for _ in 0..<abs(index - windowIndex) {
            window.perform(step, with: nil)
        }
        windowIndex = index
    }
}
