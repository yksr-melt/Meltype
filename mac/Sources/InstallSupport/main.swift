// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

import Darwin
import Foundation

// ロック用のファイル記述子を exec 後も引き継ぎ、
// 交換・検証・失敗時の復旧が終わるまで、シェルと子プロセスでロックを保持する。
if CommandLine.arguments.count >= 4, CommandLine.arguments[1] == "--lock" {
    let descriptor = open(CommandLine.arguments[2], O_CREAT | O_RDWR | O_NOFOLLOW, S_IRUSR | S_IWUSR)
    guard descriptor >= 0 else { perror("Cannot open installation lock"); exit(1) }
    var entry = stat()
    guard fstat(descriptor, &entry) == 0, entry.st_mode & S_IFMT == S_IFREG,
          entry.st_uid == getuid() else {
        fputs("Installation lock must be a regular file owned by the current user.\n", stderr)
        exit(1)
    }
    guard flock(descriptor, LOCK_EX | LOCK_NB) == 0 else {
        perror("Another installation may be running; cannot lock the installation")
        exit(75)
    }
    let command = Array(CommandLine.arguments.dropFirst(3))
    var arguments = command.map { strdup($0) } + [nil]
    execv(command[0], &arguments)
    perror("Cannot start locked installation")
    exit(1)
}

do {
    let arguments = Array(CommandLine.arguments.dropFirst())
    // 復旧時の新版停止では「-」を渡し、更新前の入力状態を上書きせずに停止する。
    if (4...5).contains(arguments.count), arguments[0] == "--stop" {
        try stopInputMethod(at: URL(fileURLWithPath: arguments[1]),
                            reference: URL(fileURLWithPath: arguments[2]),
                            stateFile: arguments[3] == "-" ? nil : URL(fileURLWithPath: arguments[3]),
                            manualJob: arguments.count == 5 ? arguments[4] : nil)
        exit(0)
    }
    if arguments.count == 3, arguments[0] == "--restore" {
        try restoreInputMethod(at: URL(fileURLWithPath: arguments[1]),
                               stateFile: URL(fileURLWithPath: arguments[2]))
        exit(0)
    }
    if arguments.count == 2, arguments[0] == "--start" {
        try startInputMethod(at: URL(fileURLWithPath: arguments[1]))
        exit(0)
    }
    if arguments.count == 2, arguments[0] == "--validate-bundle" {
        try validateBundleLayout(at: URL(fileURLWithPath: arguments[1]))
        exit(0)
    }
} catch let error as InputSelectionFailure {
    // 登録・起動済みで選択だけ失敗した場合は、呼び出し元で新版を維持できるよう区別する。
    fputs("Input method started, but selection could not be restored: \(error.localizedDescription). Select Meltype from the input menu.\n", stderr)
    exit(2)
} catch {
    fputs("Installation operation failed: \(error.localizedDescription)\n", stderr)
    exit(1)
}

guard CommandLine.arguments.count == 3 else {
    fputs("Usage: MeltypeInstallSupport directory directory | --lock file executable [arguments...] | --stop app reference state [manual-job-label] | --restore app state | --start app | --validate-bundle app\n", stderr)
    exit(1)
}
let paths = Array(CommandLine.arguments.dropFirst())
var entries = [stat(), stat()]
for index in paths.indices {
    guard lstat(paths[index], &entries[index]) == 0 else {
        perror(paths[index])
        exit(1)
    }
    guard entries[index].st_mode & S_IFMT == S_IFDIR else {
        fputs("Not a directory (symbolic links are not accepted): \(paths[index])\n", stderr)
        exit(1)
    }
}
guard entries[0].st_dev == entries[1].st_dev,
      entries[0].st_ino != entries[1].st_ino else {
    fputs("Expected different directories on the same volume.\n", stderr)
    exit(1)
}
// Contents 同士を一度に交換し、更新・復旧のどちらでも外側のアプリを取り外さない。
guard renamex_np(paths[0], paths[1], UInt32(RENAME_SWAP)) == 0 else {
    perror("Cannot atomically exchange directories")
    exit(1)
}
