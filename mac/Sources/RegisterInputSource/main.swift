// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// Meltype.app を macOS に登録し直し、入力メニューの再起動を要求する小さな道具 (#134)。
// install-app.sh が Meltype.app を入れ替えたあとに呼ぶ。
// 終了コード: 0 = 登録済み、1 = 登録失敗、2 = 登録済みだが有効化は保留。
//   RegisterInputSource [Meltype.app のパス] [--keep-input-menu]   (省略したときは、この道具が入っている Meltype.app)
//
// 更新のたびに入力ソースから消える問題の対策。~/Library/Input Methods/Meltype.app を入れ替えるとき、
// バンドルが無い一瞬に入力ソースの走査が走ると、macOS は登録を消す。消えたあとは、バンドルを戻して
// 走査し直しても戻らないことがあり、これまではログアウトするしかなかった。
// TISRegisterInputSource で登録は戻せるが、入力メニューに表示されるとは限らない (#1433)。
//
// 登録後に TextInputMenuAgent の再起動を要求する (#134)。#1433 では登録・有効化・選択が
// 成功し、再起動を要求してもメニューに表示されなかった。実行した操作だけを報告し、
// 表示されたかは利用者が確認する。必要ならシステム設定から Meltype を削除・再追加する。
//
// IME の本体とは別の実行ファイルにしてある。本体を引数で呼び分ける形にすると、引数を知らない古い
// 本体に当てたときに IME として起動したままになり、install.sh がそこで止まってしまう。
import Carbon
import Foundation

let appPath = CommandLine.arguments.count >= 2 ? CommandLine.arguments[1] : Bundle.main.bundlePath

guard FileManager.default.fileExists(atPath: appPath) else {
    FileHandle.standardError.write(Data("Meltype.app が見つかりません: \(appPath)\n".utf8))
    exit(1)
}
guard let bundleID = Bundle(path: appPath)?.bundleIdentifier, !bundleID.isEmpty else {
    FileHandle.standardError.write(Data("Bundle ID が読めませんでした: \(appPath)\n".utf8))
    exit(1)
}

// 何度呼んでも増えない (すでに入っていれば、そのまま)。
let status = TISRegisterInputSource(URL(fileURLWithPath: appPath) as CFURL)
guard status == noErr else {
    FileHandle.standardError.write(Data("入力ソースに登録できませんでした (OSStatus \(status)): \(appPath)\n".utf8))
    exit(1)
}
print("入力ソースに Meltype を登録しました: \(appPath)")

// このアプリが有効な入力ソースとして見えているか。
// TISCreateInputSourceList(nil, false) は、有効な入力ソースだけを返す。
func isEnabledInputSource(bundleID: String) -> Bool {
    let list = TISCreateInputSourceList(nil, false)?.takeRetainedValue() as? [TISInputSource] ?? []
    return list.contains { source in
        guard let property = TISGetInputSourceProperty(source, kTISPropertyBundleID) else { return false }
        let id = Unmanaged<AnyObject>.fromOpaque(property).takeUnretainedValue() as? String
        return id == bundleID
    }
}

// 入力メニューの再起動を要求する。対象が動いていないときの終了コード 1 も正常に扱う。
func restartTextInputMenuAgent() throws -> Int32 {
    let killall = Process()
    killall.executableURL = URL(fileURLWithPath: "/usr/bin/killall")
    killall.arguments = ["TextInputMenuAgent"]
    killall.standardOutput = FileHandle.nullDevice
    killall.standardError = FileHandle.nullDevice
    try killall.run()
    killall.waitUntilExit()
    return killall.terminationStatus
}

// 有効な入力ソースの一覧に出てくるまで少し待つ (登録はすぐ効くが、反映までに間があることがある)。
// 見えないままメニューを作り直すと、かえって 3 項目だけの表示になってしまうので、確かめてからにする。
var available = false
for _ in 0..<30 {
    if isEnabledInputSource(bundleID: bundleID) {
        available = true
        break
    }
    usleep(100_000)
}
guard available else {
    FileHandle.standardError.write(Data("登録処理は実行しましたが、有効な入力ソースとして確認できませんでした。システム設定 → キーボード →（テキスト入力の）入力ソース →「編集…」→「+」→ 日本語 → Meltype から追加してください。\n".utf8))
    exit(2)
}
// 更新時は登録・有効化確認だけを行い、入力メニューを再起動しない。
if CommandLine.arguments.contains("--keep-input-menu") { exit(0) }
do {
    let status = try restartTextInputMenuAgent()
    guard status == 0 || status == 1 else {
        FileHandle.standardError.write(Data("入力ソースは登録しましたが、入力メニューの再起動要求は完了しませんでした (終了コード \(status))。\n".utf8))
        // 登録済みなので、表示更新の失敗だけではアプリの配置を巻き戻さない。
        exit(0)
    }
    if status == 0 {
        print("入力メニューの再起動を要求しました。表示されたかは入力メニューで確認してください。")
    } else {
        print("再起動する入力メニューのプロセスはありません。表示されたかは入力メニューで確認してください。")
    }
} catch {
    FileHandle.standardError.write(Data("入力メニューの再起動を要求できませんでした: \(error)\n".utf8))
    exit(0)
}
