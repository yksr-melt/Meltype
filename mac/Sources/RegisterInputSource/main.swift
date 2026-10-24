// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// Meltype.app を「有効な入力ソース」として macOS に登録し直し、入力メニューの表示も作り直す、小さな道具 (#134)。
// install.sh / build.sh が Meltype.app を入れ替えたあとに呼ぶ。
//   RegisterInputSource [Meltype.app のパス]   (省略したときは、この道具が入っている Meltype.app)
//
// 更新のたびに入力ソースから消える問題の対策。~/Library/Input Methods/Meltype.app を入れ替えるとき、
// バンドルが無い一瞬に入力ソースの走査が走ると、macOS は登録を消す。消えたあとは、バンドルを戻して
// 走査し直しても戻らないことがあり、これまではログアウトするしかなかった。
// TISRegisterInputSource なら、実行中でもその場で登録を戻せる (ログアウトは要らない)。
//
// 登録し直しただけでは、入力メニューの表示が古いまま残ることがある。入力メニューの一覧は
// TextInputMenuAgent が持っていて、あとから入力ソースが増えても自分では読み直さないためで、
// この状態では「絵文字と記号を表示」「キーボードビューアを表示」「キーボード設定を開く…」の
// 3 項目しか出ない (一覧は、選択できるキーボード系の入力ソースが 2 つ以上あるときだけ出る)。
// そこで、登録し直したあとに TextInputMenuAgent を再起動して、一覧を作り直させる。
// macOS がすぐ起動し直すので、こちらもログアウトは要らない。
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

// 入力メニューの表示を作り直させる。動いていなければ、何も起きない (killall が失敗するだけ)。
func restartTextInputMenuAgent() {
    let killall = Process()
    killall.executableURL = URL(fileURLWithPath: "/usr/bin/killall")
    killall.arguments = ["TextInputMenuAgent"]
    killall.standardOutput = FileHandle.nullDevice
    killall.standardError = FileHandle.nullDevice
    do {
        try killall.run()
        killall.waitUntilExit()
    } catch {
        // ここで失敗しても、入力ソースの登録は済んでいる。ログアウトすれば表示も直る。
        FileHandle.standardError.write(Data("入力メニューの表示を更新できませんでした: \(error)\n".utf8))
    }
}

// 有効な入力ソースの一覧に出てくるまで少し待つ (登録はすぐ効くが、反映までに間があることがある)。
// 見えないままメニューを作り直すと、かえって 3 項目だけの表示になってしまうので、確かめてからにする。
if let bundleID = Bundle(path: appPath)?.bundleIdentifier {
    var available = false
    for _ in 0..<30 {
        if isEnabledInputSource(bundleID: bundleID) {
            available = true
            break
        }
        usleep(100_000)
    }
    if available {
        restartTextInputMenuAgent()
        print("入力メニューの表示を更新しました")
    } else {
        print("まだ入力ソースとして有効になっていません。システム設定 → キーボード →（テキスト入力の）入力ソース →「編集…」→「+」→ 日本語 → Meltype から追加してください。")
        print("それでも出てこなければ、ログアウトしてログインし直してください。")
    }
} else {
    print("Meltype.app の Bundle ID が読めませんでした。ログアウトしてログインし直すと直ります。")
}
