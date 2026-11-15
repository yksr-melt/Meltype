// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
import UIKit

@main
final class AppDelegate: UIResponder, UIApplicationDelegate {
    var window: UIWindow?
    func application(_ application: UIApplication, didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?) -> Bool {
        // UI tests accept the consent screen without depending on its layout.
        if ProcessInfo.processInfo.arguments.contains("--uitest-skip-consent") {
            ConsentStore.accept()
        }
        let window = UIWindow(frame: UIScreen.main.bounds)
        self.window = window
        if ConsentStore.accepted {
            window.rootViewController = makeSetupNavigation()
            window.makeKeyAndVisible()
            startAnalytics()
        } else {
            let consent = ConsentViewController()
            consent.onAccept = { [weak self] in
                guard let self else { return }
                ConsentStore.accept()
                self.window?.rootViewController = self.makeSetupNavigation()
                self.startAnalytics()
            }
            window.rootViewController = consent
            window.makeKeyAndVisible()
        }
        return true
    }

    private func makeSetupNavigation() -> UINavigationController {
        let navigation = UINavigationController(rootViewController: SetupViewController())
        let appearance = UINavigationBarAppearance()
        appearance.configureWithOpaqueBackground(); appearance.backgroundColor = AppStyle.canvas
        appearance.shadowColor = .clear
        navigation.navigationBar.standardAppearance = appearance
        navigation.navigationBar.scrollEdgeAppearance = appearance
        navigation.navigationBar.tintColor = .label
        return navigation
    }

    /// 同意が済んだあとだけ送る。キーボード拡張では呼ばない。
    private func startAnalytics() {
        FlashAnalytics.start()
        FlashAnalytics.track("app_open")
        if SetupViewController.hasKeyboardExtension() { FlashAnalytics.track("keyboard_ready") }
    }
}

/// 初回起動の同意。同意するまで設定画面へ進めない。
enum ConsentStore {
    private static let key = "MeltypeConsentVersion"
    static let version = 1
    static var accepted: Bool { UserDefaults.standard.integer(forKey: key) >= version }
    static func accept() { UserDefaults.standard.set(version, forKey: key) }
    static let text = """
    Meltype-FLASH を使うには、次の内容に同意してください。

    ・本ソフトは GNU GPL v3 以降で提供され、無保証です。
    ・より良い入力のために、利用状況（アプリの版・OSの版・起動回数・画面を開いた回数）を匿名で送信します。この送信は設定で停止できません。
    ・入力内容・入力履歴・辞書・設定値・入力欄の文字列は送信しません。
    ・送信先は Aptabase です。
    """
}

final class ConsentViewController: UIViewController {
    var onAccept: (() -> Void)?
    override func viewDidLoad() {
        super.viewDidLoad()
        let stack = AppStyle.scrollColumn(in: self)
        let hero = AppStyle.column(spacing: 10)
        hero.addArrangedSubview(AppStyle.label("FLASH  /  FOR iPHONE", .caption1, color: .secondaryLabel))
        let headline = AppStyle.label("はじめる前に", .largeTitle)
        headline.font = UIFontMetrics(forTextStyle: .largeTitle).scaledFont(for: .systemFont(ofSize: 34, weight: .bold))
        hero.addArrangedSubview(headline)
        hero.addArrangedSubview(AppStyle.label("Meltype-FLASH を安心して使っていただくために、利用状況の扱いを確認してください。", .subheadline, color: .secondaryLabel))
        stack.addArrangedSubview(hero)
        stack.addArrangedSubview(AppStyle.card(AppStyle.label(ConsentStore.text, .footnote, color: .secondaryLabel)))
        let accept = AppStyle.button("同意してはじめる", symbol: "checkmark", primary: true) { [weak self] in self?.onAccept?() }
        accept.accessibilityIdentifier = "consent-accept"
        stack.addArrangedSubview(accept)
        stack.addArrangedSubview(AppStyle.label("同意しない場合は、アプリを終了してください。", .caption1, color: .secondaryLabel))
    }
}

final class SetupViewController: UIViewController, UITextViewDelegate {
    private let field = MeltypeTextView()
    private let placeholder = AppStyle.label("ここに、今日のひとこと。", .body, color: .tertiaryLabel)
    private let extensionStatus = AppStyle.label("", .footnote, color: .secondaryLabel)
    private let preview = KeyboardPreviewView()
    private let appearanceStore = KeyboardAppearanceStore()
    private var settings: UIButton!

    static func hasKeyboardExtension(in app: Bundle = .main) -> Bool {
        guard let plugins = app.builtInPlugInsURL,
              let urls = try? FileManager.default.contentsOfDirectory(at: plugins, includingPropertiesForKeys: nil) else { return false }
        return urls.filter { $0.pathExtension == "appex" }.contains { url in
            guard let bundle = Bundle(url: url),
                  let info = bundle.object(forInfoDictionaryKey: "NSExtension") as? [String: Any] else { return false }
            return info["NSExtensionPointIdentifier"] as? String == "com.apple.keyboard-service"
                && FileManager.default.isExecutableFile(atPath: bundle.executableURL?.path ?? "")
        }
    }

    override func viewDidLoad() {
        super.viewDidLoad()
        let brand = AppStyle.label("Meltype-FLASH", .headline)
        navigationItem.leftBarButtonItem = UIBarButtonItem(customView: brand)
        navigationItem.backButtonDisplayMode = .minimal
        navigationItem.rightBarButtonItem = UIBarButtonItem(image: UIImage(systemName: "slider.horizontal.3"), style: .plain, target: self, action: #selector(openAppearance))
        navigationItem.rightBarButtonItem?.accessibilityLabel = "背景・テーマ"
        let stack = AppStyle.scrollColumn(in: self)
        let ai = UIButton(type: .system); ai.setTitle("AIモデル", for: .normal)
        ai.addTarget(self, action: #selector(openAISettings), for: .touchUpInside)
        // 画面の高さを変えないよう、AIモデルと同じ行に並べる
        let currency = UIButton(type: .system); currency.setTitle("通貨の換算", for: .normal)
        currency.accessibilityIdentifier = "open-currency-settings"
        currency.addTarget(self, action: #selector(openCurrencySettings), for: .touchUpInside)
        stack.addArrangedSubview(AppStyle.row([ai, currency]))
        let hero = AppStyle.column(spacing: 10)
        let kicker = AppStyle.label("FLASH  /  FOR iPHONE", .caption1, color: .secondaryLabel)
        let mark = UIImageView(image: UIImage(named: "BrandMark")); mark.contentMode = .scaleAspectFit
        mark.backgroundColor = .white; mark.layer.cornerRadius = 14; mark.clipsToBounds = true
        mark.widthAnchor.constraint(equalToConstant: 48).isActive = true
        mark.heightAnchor.constraint(equalToConstant: 48).isActive = true
        hero.addArrangedSubview(AppStyle.row([kicker, UIView(), mark]))
        let headline = AppStyle.label("ひらめきを、\nそのまま。", .largeTitle)
        headline.font = UIFontMetrics(forTextStyle: .largeTitle).scaledFont(for: .systemFont(ofSize: 38, weight: .bold))
        hero.addArrangedSubview(headline)
        hero.addArrangedSubview(AppStyle.label("日本語と英語を、ひとつのキーボードで。", .subheadline, color: .secondaryLabel))
        stack.addArrangedSubview(hero)
        stack.addArrangedSubview(preview)
        let setup = AppStyle.column(spacing: 14)
        let heading = AppStyle.column(spacing: 4)
        heading.addArrangedSubview(AppStyle.label("キーボードを追加", .headline))
        heading.addArrangedSubview(AppStyle.label("設定から、かんたん2ステップ。", .subheadline, color: .secondaryLabel))
        setup.addArrangedSubview(AppStyle.row([AppStyle.symbol("keyboard"), heading]))
        settings = AppStyle.button("iPhoneの設定を開く", symbol: "arrow.up.right", primary: true) { [weak self] in self?.openSettings() }
        settings.accessibilityIdentifier = "open-settings"; setup.addArrangedSubview(settings)
        let help = UIButton(type: .system)
        help.setTitle("追加の手順を見る", for: .normal); help.tintColor = .secondaryLabel
        help.titleLabel?.font = .preferredFont(forTextStyle: .footnote)
        help.heightAnchor.constraint(greaterThanOrEqualToConstant: 44).isActive = true
        help.addAction(UIAction { [weak self] _ in self?.showSetupHelp() }, for: .touchUpInside)
        setup.addArrangedSubview(help)
        extensionStatus.accessibilityIdentifier = "keyboard-extension-status"
        setup.addArrangedSubview(extensionStatus)
        stack.addArrangedSubview(AppStyle.card(setup))
        let customize = AppStyle.button("自分らしいキーボードに", symbol: "paintpalette") { [weak self] in self?.openAppearance() }
        customize.accessibilityIdentifier = "open-appearance"
        stack.addArrangedSubview(customize)
        let flickDemo = AppStyle.button("フリック入力を試す", symbol: "hand.draw") { [weak self] in
            self?.navigationController?.pushViewController(KeyboardDemoViewController(), animated: true)
        }
        flickDemo.accessibilityIdentifier = "open-flick-demo"
        stack.addArrangedSubview(flickDemo)
        let test = AppStyle.column(spacing: 12)
        test.addArrangedSubview(AppStyle.row([AppStyle.symbol("square.and.pencil"), AppStyle.label("さっそく、書いてみよう", .headline)]))
        test.addArrangedSubview(AppStyle.label("入力欄で🌐を長押しして、Meltype-FLASHへ。", .footnote, color: .secondaryLabel))
        // 入力テストはOSのスペル修正候補で選択メニューを置き換えない。
        field.autocorrectionType = .no; field.spellCheckingType = .no
        field.font = .preferredFont(forTextStyle: .body); field.adjustsFontForContentSizeCategory = true
        field.backgroundColor = AppStyle.canvas; field.layer.cornerRadius = 14
        field.textContainerInset = UIEdgeInsets(top: 14, left: 10, bottom: 14, right: 10)
        field.heightAnchor.constraint(equalToConstant: 126).isActive = true
        field.accessibilityIdentifier = "input-test"; field.accessibilityLabel = "キーボードの入力テスト"
        field.delegate = self
        let toolbar = UIToolbar(); toolbar.sizeToFit()
        toolbar.items = [UIBarButtonItem(barButtonSystemItem: .flexibleSpace, target: nil, action: nil), UIBarButtonItem(title: "完了", style: .done, target: self, action: #selector(endEditing))]
        field.inputAccessoryView = toolbar
        placeholder.translatesAutoresizingMaskIntoConstraints = false; placeholder.isUserInteractionEnabled = false
        field.addSubview(placeholder)
        NSLayoutConstraint.activate([
            placeholder.leadingAnchor.constraint(equalTo: field.frameLayoutGuide.leadingAnchor, constant: 15),
            placeholder.trailingAnchor.constraint(lessThanOrEqualTo: field.frameLayoutGuide.trailingAnchor, constant: -15),
            placeholder.topAnchor.constraint(equalTo: field.frameLayoutGuide.topAnchor, constant: 14)
        ])
        test.addArrangedSubview(field); stack.addArrangedSubview(AppStyle.card(test))
        let privacy = AppStyle.label("通常入力はフルアクセス不要\nshuntiは共有モデルへのアクセスを許可すると使えます。入力内容は送信しません。\n利用状況の送信は、同意後は停止できません。", .caption1, color: .secondaryLabel)
        let privacyRow = AppStyle.row([AppStyle.symbol("lock.shield", color: .secondaryLabel), privacy])
        stack.addArrangedSubview(privacyRow)
        refresh()
    }
    override func viewWillAppear(_ animated: Bool) {
        super.viewWillAppear(animated); refresh()
    }
    private func refresh() {
        guard isViewLoaded else { return }
        let available = Self.hasKeyboardExtension()
        extensionStatus.text = available ? "キーボードの準備ができています。" : "キーボード拡張が見つかりません。導入ツールで拡張が削除された可能性があります。手順を確認して再導入してください。"
        extensionStatus.textColor = available ? .secondaryLabel : .systemRed
        settings?.isEnabled = available
        let appearance = appearanceStore.load()
        preview.apply(appearance, image: appearanceStore.image(for: appearance))
    }
    private func openSettings() {
        guard let url = URL(string: UIApplication.openSettingsURLString) else { return }
        UIApplication.shared.open(url, options: [:]) { [weak self] opened in
            if !opened { DispatchQueue.main.async { self?.showSetupHelp() } }
        }
    }
    private func showSetupHelp() {
        let message = Self.hasKeyboardExtension()
            ? "1. 設定 → 一般 → キーボード → キーボード → 新しいキーボードを追加 → Meltype-FLASH\n\n2. 入力欄で🌐を長押ししてMeltype-FLASHを選択。\n\n「設定を開く」はアプリの設定を開きます。「キーボード」があれば、そこからも追加できます。フルアクセスは不要です。"
            : "キーボード拡張が削除されているため、設定から追加できません。iPASideなどの導入ツールで拡張を削除せず、本体と拡張（.appex）の両方を署名できる方法で再導入してください。"
        let alert = UIAlertController(title: "キーボードの追加方法", message: message, preferredStyle: .alert)
        alert.addAction(UIAlertAction(title: "閉じる", style: .default)); present(alert, animated: true)
    }
    @available(iOS 16.0, *)
    func textView(_ textView: UITextView, editMenuForTextIn range: NSRange, suggestedActions: [UIMenuElement]) -> UIMenu? {
        (textView as? MeltypeTextView)?.selectionMenu(for: range, suggestedActions: suggestedActions)
    }
    func textViewDidChange(_ textView: UITextView) { placeholder.isHidden = !textView.text.isEmpty }
    @objc private func openAISettings() { navigationController?.pushViewController(AISettingsViewController(), animated: true) }
    @objc private func openCurrencySettings() { navigationController?.pushViewController(CurrencySettingsViewController(), animated: true) }
    @objc private func openAppearance() { navigationController?.pushViewController(AppearanceViewController(), animated: true) }
    @objc private func endEditing() { view.endEditing(true) }
}
