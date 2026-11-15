// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
import UIKit

final class CurrencySettingsViewController: UIViewController, UITextFieldDelegate {
    private let store = CurrencySettingsStore()
    private let mode = UIButton(type: .system)
    private let digits = UIButton(type: .system)
    private let manual = UITextField()
    private let save = UIButton(type: .system)
    private let status = UILabel()
    private var selection = CurrencySelection()

    override func viewDidLoad() {
        super.viewDidLoad()
        title = "通貨の換算"
        view.backgroundColor = AppStyle.canvas
        let column = AppStyle.scrollColumn(in: self)
        column.addArrangedSubview(AppStyle.label("金額の読みから、別の通貨に換算した候補を出します。例: 100どる → ≒1.6万円。", .footnote, color: .secondaryLabel))

        mode.showsMenuAsPrimaryAction = true
        mode.accessibilityIdentifier = "currency-mode"
        column.addArrangedSubview(AppStyle.row([AppStyle.label("通貨のレート", .headline), mode]))

        digits.showsMenuAsPrimaryAction = true
        digits.accessibilityIdentifier = "currency-decimals"
        column.addArrangedSubview(AppStyle.row([AppStyle.label("換算した額の小数の桁", .headline), digits]))

        manual.borderStyle = .roundedRect
        manual.autocorrectionType = .no
        manual.autocapitalizationType = .none
        manual.spellCheckingType = .no
        manual.returnKeyType = .done
        manual.placeholder = "例: 150（1 ドル = 150 円）"
        manual.accessibilityIdentifier = "currency-manual"
        manual.accessibilityLabel = "手入力のレート（1 単位あたりの円）"
        manual.delegate = self
        column.addArrangedSubview(AppStyle.label("手入力のレート（1 単位あたりの円）", .headline))
        column.addArrangedSubview(manual)
        column.addArrangedSubview(AppStyle.label("数値だけなら米ドル。通貨コードを書くとその通貨です（USD 150, EUR=170, GBP:200）。カンマ・セミコロン・改行で区切って複数書けます。", .footnote, color: .secondaryLabel))

        save.setTitle("レートを保存", for: .normal)
        save.accessibilityIdentifier = "currency-save"
        save.addTarget(self, action: #selector(saveSelection), for: .touchUpInside)
        column.addArrangedSubview(save)

        status.numberOfLines = 0
        status.font = .preferredFont(forTextStyle: .footnote)
        column.addArrangedSubview(status)

        column.addArrangedSubview(AppStyle.label("iOS はネットからレートを取りません。固定はこの版に同梱した配布時のレート、手入力は自分で入れた数です。自動は Windows・macOS・Linux で使えます。", .caption1, color: .secondaryLabel))

        selection = store.load()
        manual.text = selection.manual
        refresh()
    }

    private func refresh() {
        mode.setTitle(selection.modeLabel, for: .normal)
        mode.menu = UIMenu(children: CurrencySelection.modes.map { value in
            UIAction(title: value == "Manual" ? "手入力" : "固定（同梱のレート）",
                     state: selection.mode == value ? .on : .off) { [weak self] _ in
                guard let self = self else { return }
                self.selection.mode = value
                self.refresh()
                self.status.text = "「レートを保存」で反映します。"
            }
        })
        digits.setTitle("\(selection.decimals) 桁", for: .normal)
        digits.menu = UIMenu(children: (0...4).map { value in
            UIAction(title: value == 0 ? "0（1 の位まで）" : "\(value) 桁",
                     state: selection.decimals == value ? .on : .off) { [weak self] _ in
                guard let self = self else { return }
                self.selection.decimals = value
                self.refresh()
                self.status.text = "「レートを保存」で反映します。"
            }
        })
        manual.isHidden = selection.mode != "Manual"
    }

    @objc private func saveSelection() {
        view.endEditing(true)
        selection.manual = manual.text ?? ""
        do {
            try store.save(selection)
            status.text = "保存しました。キーボードを開き直すと反映します。"
        } catch {
            status.text = "保存できませんでした。共有フォルダーを利用できません。App Group を含む署名で導入してください。"
        }
        refresh()
    }

    func textFieldShouldReturn(_ textField: UITextField) -> Bool {
        textField.resignFirstResponder()
        return true
    }
}
