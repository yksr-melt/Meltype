// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
import UIKit

private final class PreeditLabel: UILabel {
    override func drawText(in rect: CGRect) {
        super.drawText(in: rect.insetBy(dx: 10, dy: 0))
    }
}

struct EngineReply: Decodable {
    struct Edit: Decodable { let text: String; let deleteBefore: Int }
    struct View: Decodable { let text: String; let candidates: [String] }
    let consumed: Bool
    let commits: [Edit]
    let view: View?
    let selectionExpect: String?
}

class KeyboardViewController: UIInputViewController, UIContextMenuInteractionDelegate {
    var clipboardAccessAllowed: Bool { hasFullAccess }
    var clipboardText: String? {
        get { UIPasteboard.general.string }
        set {
            if let text = newValue { UIPasteboard.general.setItems([["public.utf8-plain-text": text]], options: [.localOnly: true]) }
        }
    }
    private let commandStatus = UILabel()
    private var selectionConversion: EditingSelection?
    var initialFlickLayout: Bool? { nil }
    var allowsSystemKeyboardSwitch: Bool { needsInputModeSwitchKey }
    private var session: UInt64 = 0
    private let preedit = PreeditLabel()
    private let candidates = UIStackView()
    private var composing = false
    private var applying = false
    private var upper = false
    private var letters = [UIButton]()
    private var globe: UIButton?
    private let keyArea = UIStackView()
    private var flick = false
    private var modeKey: UIButton?
    private var lastFlick: String?
    private let appearanceStore = KeyboardAppearanceStore()
    private let backdrop = KeyboardBackdrop()
    private var appearance = KeyboardAppearance()
    private var appearanceImage: UIImage?
    private var loadedImageName: String?

    override func viewDidLoad() {
        super.viewDidLoad()
        session = meltype_ios_create()
        backdrop.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(backdrop)
        NSLayoutConstraint.activate([
            backdrop.leadingAnchor.constraint(equalTo: view.leadingAnchor), backdrop.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            backdrop.topAnchor.constraint(equalTo: view.topAnchor), backdrop.bottomAnchor.constraint(equalTo: view.bottomAnchor)
        ])
        let column = UIStackView()
        column.axis = .vertical
        column.spacing = 7
        column.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(column)
        NSLayoutConstraint.activate([
            column.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 5),
            column.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -5),
            column.topAnchor.constraint(equalTo: view.topAnchor, constant: 7),
            column.bottomAnchor.constraint(equalTo: view.bottomAnchor, constant: -7)
        ])
        preedit.text = "Meltype-FLASH"
        preedit.font = .systemFont(ofSize: 16, weight: .medium)
        preedit.layer.cornerRadius = 10
        preedit.clipsToBounds = true
        preedit.accessibilityIdentifier = "preedit"
        preedit.isUserInteractionEnabled = true
        preedit.addInteraction(UIContextMenuInteraction(delegate: self))
        preedit.heightAnchor.constraint(equalToConstant: 34).isActive = true
        column.addArrangedSubview(preedit)
        let scroll = UIScrollView()
        scroll.heightAnchor.constraint(equalToConstant: 36).isActive = true
        candidates.axis = .horizontal
        candidates.spacing = 6
        candidates.translatesAutoresizingMaskIntoConstraints = false
        scroll.addSubview(candidates)
        NSLayoutConstraint.activate([
            candidates.leadingAnchor.constraint(equalTo: scroll.contentLayoutGuide.leadingAnchor),
            candidates.trailingAnchor.constraint(equalTo: scroll.contentLayoutGuide.trailingAnchor),
            candidates.topAnchor.constraint(equalTo: scroll.contentLayoutGuide.topAnchor),
            candidates.bottomAnchor.constraint(equalTo: scroll.contentLayoutGuide.bottomAnchor),
            candidates.heightAnchor.constraint(equalTo: scroll.frameLayoutGuide.heightAnchor)
        ])
        column.addArrangedSubview(scroll)
        let mode = button("かなフリック") { [weak self] in self?.toggleLayout() }
        mode.accessibilityIdentifier = "keyboard-layout-toggle"
        mode.heightAnchor.constraint(equalToConstant: 34).isActive = true
        modeKey = mode
        column.addArrangedSubview(mode)
        commandStatus.font = .preferredFont(forTextStyle: .caption1)
        commandStatus.textColor = .secondaryLabel; commandStatus.numberOfLines = 0
        commandStatus.accessibilityIdentifier = "editing-status"
        commandStatus.isHidden = true
        column.addArrangedSubview(commandStatus)
        keyArea.axis = .vertical; keyArea.spacing = 7
        column.addArrangedSubview(keyArea)
        flick = initialFlickLayout ?? (UserDefaults.standard.string(forKey: "keyboard-layout") == "flick")
        rebuildKeys()
        reloadAppearance()
    }
    private func rebuildKeys() {
        for child in keyArea.arrangedSubviews { keyArea.removeArrangedSubview(child); child.removeFromSuperview() }
        letters.removeAll(); globe = nil
        modeKey?.setTitle(flick ? "ABC・QWERTYに切替" : "かなフリックに切替", for: .normal)
        modeKey?.accessibilityValue = flick ? "かなフリック" : "QWERTY"
        if flick { buildFlickKeys(); applyAppearance(); return }
        for row in ["qwertyuiop", "asdfghjkl", "zxcvbnm", "1234567890", "-@/.,?!"] {
            let line = makeRow()
            for character in row {
                let text = String(character)
                let key = button(text) { [weak self] in
                    guard let self = self else { return }
                    self.type(self.upper ? text.uppercased() : text)
                }
                if character.isLetter { letters.append(key) }
                line.addArrangedSubview(key)
            }
            keyArea.addArrangedSubview(line)
        }
        let actions = makeRow()
        let switchKey = button("🌐") { }
        switchKey.addTarget(self, action: #selector(handleInputModeList(from:with:)), for: .allTouchEvents)
        switchKey.accessibilityLabel = "次のキーボード"
        globe = switchKey
        actions.addArrangedSubview(switchKey)
        actions.addArrangedSubview(button("⇧") { [weak self] in
            guard let self = self else { return }
            self.upper.toggle()
            for key in self.letters {
                let title = key.title(for: .normal) ?? ""
                key.setTitle(self.upper ? title.uppercased() : title.lowercased(), for: .normal)
            }
            self.applyAppearance()
        })
        actions.addArrangedSubview(button("空白・変換") { [weak self] in self?.convertOrSpace() })
        actions.addArrangedSubview(button("⌫") { [weak self] in self?.deleteCharacter() })
        actions.addArrangedSubview(button("確定 ↵") { [weak self] in self?.confirm() })
        keyArea.addArrangedSubview(actions)
        applyAppearance()
    }
    var hasPendingComposition: Bool { composing }
    private func refreshCommands() {
        commandStatus.isHidden = commandStatus.text?.isEmpty ?? true
    }
    func contextMenuInteraction(_ interaction: UIContextMenuInteraction, configurationForMenuAtLocation location: CGPoint) -> UIContextMenuConfiguration? {
        guard composing else { return nil }
        return UIContextMenuConfiguration(identifier: nil, previewProvider: nil) { [weak self] _ in
            UIMenu(children: [UIAction(title: "変換を取り消す", image: UIImage(systemName: "xmark")) { _ in
                self?.performEditingCommand("変換取消")
            }])
        }
    }
    private func resetComposition() {
        selectionConversion = nil
        apply(meltype_ios_reset(session))
    }
    func performEditingCommand(_ command: String) {
        if command == "変換取消" {
            resetComposition(); commandStatus.text = "変換を取り消しました。選択した文字は変更していません。"; refreshCommands(); return
        }
        guard !composing else {
            commandStatus.text = "入力中の文字を確定するか、変換を取り消してから操作してください。"; return
        }
        if ["コピー", "切り取り", "貼り付け"].contains(command), !clipboardAccessAllowed {
            commandStatus.text = "コピー・貼り付けには設定で任意の「フルアクセス」を許可してください。入力・変換・選択削除は許可なしで使えます。"; return
        }
        if command == "貼り付け" {
            let document = textDocumentProxy.documentIdentifier
            let before = textDocumentProxy.documentContextBeforeInput
            let after = textDocumentProxy.documentContextAfterInput
            let selection = textDocumentProxy.selectedText
            guard let text = clipboardText, !text.isEmpty else { commandStatus.text = "貼り付ける文字がありません。"; return }
            guard document == textDocumentProxy.documentIdentifier, before == textDocumentProxy.documentContextBeforeInput,
                  after == textDocumentProxy.documentContextAfterInput, selection == textDocumentProxy.selectedText else {
                commandStatus.text = "入力欄や選択範囲が変わったため、貼り付けを中止しました。"; return
            }
            applying = true; textDocumentProxy.insertText(text); applying = false
            commandStatus.text = "貼り付けました。"; refreshCommands(); return
        }
        guard let selected = EditingSelection(textDocumentProxy) else { commandStatus.text = "先に入力欄の文字を選択してください。"; return }
        switch command {
        case "コピー", "切り取り":
            clipboardText = selected.text
            if command == "切り取り" {
                guard selected.matches(textDocumentProxy) else { commandStatus.text = "コピーしましたが、選択範囲が変わったため削除していません。"; return }
                applying = true; textDocumentProxy.deleteBackward(); applying = false
            }
            commandStatus.text = command == "コピー" ? "選択範囲をコピーしました。" : "選択範囲を切り取りました。"
        case "選択を削除":
            guard selected.matches(textDocumentProxy) else { commandStatus.text = "選択範囲が変わったため、削除を中止しました。"; return }
            applying = true; textDocumentProxy.deleteBackward(); applying = false
            commandStatus.text = "選択範囲を削除しました。"
        case "選択を変換":
            guard selected.text.utf8.count <= 4096 else { commandStatus.text = "選択範囲が長すぎます。短く選択し直してください。"; return }
            selectionConversion = selected
            let consumed = selected.text.withCString { apply(meltype_ios_reconvert(session, $0, UInt32(selected.text.utf8.count))) }
            if !consumed { resetComposition(); commandStatus.text = "この選択範囲の読みを取得できませんでした。元の文字は変更していません。" }
            else { commandStatus.text = "候補か確定で置き換えます。未確定欄を長押しすると取り消せます。" }
        default: break
        }
        refreshCommands()
    }
    override func selectionWillChange(_ textInput: UITextInput?) {
        super.selectionWillChange(textInput)
        if !applying, selectionConversion != nil {
            resetComposition(); commandStatus.text = "選択範囲が変わったため、変換を取り消しました。"
        }
    }
    override func selectionDidChange(_ textInput: UITextInput?) {
        super.selectionDidChange(textInput); refreshCommands()
    }
    override func textDidChange(_ textInput: UITextInput?) {
        super.textDidChange(textInput); refreshCommands()
    }
    private func toggleLayout() {
        if composing { apply(meltype_ios_commit(session)) }
        lastFlick = nil; upper = false; flick.toggle()
        UserDefaults.standard.set(flick ? "flick" : "qwerty", forKey: "keyboard-layout")
        rebuildKeys()
    }
    private func buildFlickKeys() {
        for rowIndex in 0..<4 {
            let row = makeRow(height: 58)
            for column in 0..<3 {
                if rowIndex == 3 && column == 0 {
                    let variant = button("゛小゜") { [weak self] in self?.changeVariant() }
                    variant.accessibilityLabel = "濁点・半濁点・小文字を切替"
                    row.addArrangedSubview(variant)
                } else {
                    let index = rowIndex == 3 ? 8 + column : rowIndex * 3 + column
                    let key = FlickKey(kana: FlickKana.groups[index]) { [weak self] in self?.inputKana($0) }
                    decorate(key); row.addArrangedSubview(key)
                }
            }
            switch rowIndex {
            case 0: row.addArrangedSubview(button("⌫") { [weak self] in self?.deleteCharacter() })
            case 1: row.addArrangedSubview(button("空白・変換") { [weak self] in self?.convertOrSpace() })
            case 2: row.addArrangedSubview(button("確定 ↵") { [weak self] in self?.confirm() })
            default:
                let switchKey = button("🌐") { }
                switchKey.addTarget(self, action: #selector(handleInputModeList(from:with:)), for: .allTouchEvents)
                switchKey.accessibilityLabel = "次のキーボード"; globe = switchKey
                row.addArrangedSubview(switchKey)
            }
            keyArea.addArrangedSubview(row)
        }
    }
    private func convertOrSpace() {
        if !composing, !(textDocumentProxy.selectedText?.isEmpty ?? true) { performEditingCommand("選択を変換") }
        else { type(" ") }
    }
    private func inputKana(_ text: String) {
        type(text)
        lastFlick = composing ? text : nil
    }
    private func changeVariant() {
        guard composing, let last = lastFlick, let changed = FlickKana.variant(last) else { return }
        apply(meltype_ios_key(session, 8, 0, 0))
        inputKana(changed)
    }
    private func deleteCharacter() {
        lastFlick = nil
        if composing { apply(meltype_ios_key(session, 8, 0, 0)) }
        else { textDocumentProxy.deleteBackward() }
    }
    private func confirm() {
        lastFlick = nil
        if composing { apply(meltype_ios_commit(session)) }
        else { textDocumentProxy.insertText("\n") }
    }
    override func viewWillAppear(_ animated: Bool) {
        super.viewWillAppear(animated)
        reloadAppearance()
        let ai = AISettingsStore()
        if hasFullAccess, ai.selection() == "Shunti", ai.installed, let path = ai.dataDirectory?.path {
            _ = path.withCString { meltype_ios_set_ai(session, $0, UInt32(path.utf8.count), 1) }
        } else { _ = meltype_ios_set_ai(session, nil, 0, 0) }
        applyCurrencySettings()
    }
    // 通貨のレートは App Group の設定をそのまま渡す (固定・手入力と小数の桁)。
    private func applyCurrencySettings() {
        let selection = CurrencySettingsStore().load()
        _ = selection.mode.withCString { mode in
            selection.manual.withCString { manual in
                meltype_ios_set_currency(session, mode, UInt32(selection.mode.utf8.count), manual, UInt32(selection.manual.utf8.count), Int32(selection.decimals))
            }
        }
    }
    private func reloadAppearance() {
        appearance = appearanceStore.load()
        if loadedImageName != appearance.imageName {
            appearanceImage = appearanceStore.image(for: appearance)
            loadedImageName = appearance.imageName
        }
        applyAppearance()
    }
    private func applyAppearance() {
        backdrop.apply(appearance.theme, image: appearanceImage)
        view.backgroundColor = appearance.theme.background
        preedit.textColor = appearance.theme.text
        preedit.backgroundColor = appearance.theme.key
        func style(_ parent: UIView) {
            if let key = parent as? UIButton {
                let title = key.title(for: .normal) ?? ""
                let accent = title == "確定 ↵" || (title == "⇧" && upper)
                key.backgroundColor = accent ? UIColor(red: 1, green: 0.86, blue: 0.16, alpha: 1) : appearance.theme.key
                key.setTitleColor(accent ? UIColor(white: 0.1, alpha: 1) : appearance.theme.text, for: .normal)
                (key as? FlickKey)?.setHintColor(appearance.theme.text)
            }
            parent.subviews.forEach(style)
        }
        style(view)
    }
    override func traitCollectionDidChange(_ previousTraitCollection: UITraitCollection?) {
        super.traitCollectionDidChange(previousTraitCollection)
        if isViewLoaded { applyAppearance() }
    }
    override func viewWillLayoutSubviews() {
        super.viewWillLayoutSubviews()
        globe?.isHidden = !flick && !allowsSystemKeyboardSwitch
        globe?.isEnabled = allowsSystemKeyboardSwitch
        globe?.alpha = allowsSystemKeyboardSwitch ? 1 : 0
    }
    override func textWillChange(_ textInput: UITextInput?) {
        super.textWillChange(textInput)
        if !applying && session != 0 { resetComposition() }
    }
    override func viewWillDisappear(_ animated: Bool) {
        super.viewWillDisappear(animated)
        if session != 0 { resetComposition() }
    }
    deinit { if session != 0 { meltype_ios_destroy(session) } }

    private func makeRow(height: CGFloat = 44) -> UIStackView {
        let row = UIStackView()
        row.distribution = .fillEqually
        row.spacing = 5
        row.heightAnchor.constraint(equalToConstant: height).isActive = true
        return row
    }
    private func button(_ title: String, action: @escaping () -> Void) -> UIButton {
        let key = UIButton(type: .system)
        key.setTitle(title, for: .normal)
        key.titleLabel?.font = .systemFont(ofSize: title.count > 2 ? 14 : 19, weight: title.count > 2 ? .medium : .regular)
        key.titleLabel?.numberOfLines = title.count > 2 ? 2 : 1
        key.titleLabel?.textAlignment = .center
        key.titleLabel?.adjustsFontSizeToFitWidth = true
        key.titleLabel?.minimumScaleFactor = 0.8
        decorate(key)
        key.addAction(UIAction { _ in action() }, for: .touchUpInside)
        return key
    }
    private func decorate(_ key: UIButton) {
        key.backgroundColor = appearance.theme.key
        key.setTitleColor(appearance.theme.text, for: .normal)
        key.layer.cornerRadius = 8
        key.layer.cornerCurve = .continuous
        key.layer.shadowColor = UIColor.black.cgColor
        key.layer.shadowOpacity = 0.12
        key.layer.shadowOffset = CGSize(width: 0, height: 1)
        key.layer.shadowRadius = 0
    }
    private func type(_ text: String) {
        lastFlick = nil
        guard let scalar = text.unicodeScalars.first else { return }
        let vk = text == " " ? 32 : Int32(text.uppercased().unicodeScalars.first!.value)
        if !apply(meltype_ios_key(session, vk, Int32(scalar.value), upper ? 1 : 0)) {
            textDocumentProxy.insertText(text)
        }
    }
    @discardableResult
    private func apply(_ pointer: UnsafeMutablePointer<CChar>?) -> Bool {
        lastFlick = nil
        guard let pointer = pointer else { return false }
        defer { meltype_ios_free(pointer) }
        guard let reply = try? JSONDecoder().decode(EngineReply.self, from: Data(String(cString: pointer).utf8)) else { return false }
        if let selection = selectionConversion {
            guard selection.matches(textDocumentProxy), reply.selectionExpect == nil || reply.selectionExpect == selection.text,
                  reply.commits.count <= 1 else {
                resetComposition(); commandStatus.text = "選択範囲または入力欄が変わったため、変換を取り消しました。"; return true
            }
        }
        applying = true
        defer { applying = false; refreshCommands() }
        // The Rust host disables backward corrections. Never delete host text here.
        guard reply.commits.allSatisfy({ $0.deleteBefore == 0 }) else {
            if let reset = meltype_ios_reset(session) { meltype_ios_free(reset) }
            composing = false
            preedit.text = ""
            return true
        }
        let replacesSelection = selectionConversion != nil
        for edit in reply.commits { textDocumentProxy.insertText(edit.text) }
        if !reply.commits.isEmpty {
            selectionConversion = nil
            if replacesSelection { commandStatus.text = "選択範囲を置き換えました。" }
        }
        composing = !(reply.view?.text.isEmpty ?? true)
        preedit.text = reply.view?.text ?? ""
        if !composing { selectionConversion = nil }
        for child in candidates.arrangedSubviews { candidates.removeArrangedSubview(child); child.removeFromSuperview() }
        for (index, text) in (reply.view?.candidates ?? []).prefix(20).enumerated() {
            candidates.addArrangedSubview(button("  \(text)  ") { [weak self] in
                guard let self = self else { return }
                self.apply(meltype_ios_select(self.session, UInt32(index)))
                self.apply(meltype_ios_commit(self.session))
            })
        }
        return reply.consumed
    }
}
