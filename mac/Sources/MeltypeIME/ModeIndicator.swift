// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

import Cocoa

/// 選択中の入力モードを一覧の中で強調する。入力先のフォーカスは奪わない。
final class ModeIndicator {
    static let shared = ModeIndicator()
    private var panel: NSPanel?
    private var timer: Timer?
    private var generation = 0

    static func validCaret(_ rect: NSRect) -> Bool {
        rect.minX.isFinite && rect.minY.isFinite && rect.width.isFinite && rect.height.isFinite &&
        rect.width >= 0 && rect.height > 0
    }

    private static let fontSize: CGFloat = 12
    private static let padding: CGFloat = 2
    private static let spacing: CGFloat = fontSize / 2

    private static func labelSize(_ text: String) -> NSSize {
        let label = NSTextField(labelWithString: text)
        // 選択が変わってもウィンドウの大きさを変えない。
        label.font = .systemFont(ofSize: fontSize, weight: .semibold)
        label.sizeToFit()
        return NSSize(width: ceil(label.frame.width), height: ceil(label.frame.height))
    }

    private struct Layout {
        let size: NSSize
        let rows: [NSRect]
        let caption: NSRect?

        init(compact: Bool) {
            let labels = MacInputMode.allCases.map { labelSize($0.shortTitle) }
            let rowHeight = labels.map(\.height).max()! + padding * 2
            if compact {
                var x = padding
                rows = labels.map { label in
                    let row = NSRect(x: x, y: padding, width: label.width + padding * 2, height: rowHeight)
                    x = row.maxX + spacing
                    return row
                }
                size = NSSize(width: x - spacing + padding, height: rowHeight + padding * 2)
                caption = nil
            } else {
                size = NSSize(width: 250, height: 180)
                rows = MacInputMode.allCases.indices.map { index in
                    NSRect(x: 10, y: 118 - CGFloat(index) * 50, width: 230, height: 44)
                }
                caption = NSRect(x: 10, y: 160, width: 230, height: 16)
            }
        }
    }

    static func frame(caret: NSRect?, visible: NSRect) -> NSRect {
        let size = Layout(compact: caret != nil).size
        let origin: NSPoint
        if let caret {
            origin = NSPoint(x: max(visible.minX, min(caret.minX - size.width / 2, visible.maxX - size.width)),
                             y: max(visible.minY, min(caret.minY - size.height - 6, visible.maxY - size.height)))
        } else {
            origin = NSPoint(x: visible.midX - size.width / 2, y: visible.midY - size.height / 2)
        }
        return NSRect(origin: origin, size: size)
    }

    static func content(mode: MacInputMode, compact: Bool) -> NSView {
        let layout = Layout(compact: compact)
        let background = NSVisualEffectView(frame: NSRect(origin: .zero, size: layout.size))
        background.material = .hudWindow
        background.blendingMode = .behindWindow
        background.state = .active
        background.wantsLayer = true
        background.layer?.cornerRadius = compact ? 5 : 16
        background.layer?.masksToBounds = true
        for (index, item) in MacInputMode.allCases.enumerated() {
            let row = NSView(frame: layout.rows[index])
            row.setAccessibilityElement(true)
            row.setAccessibilityRole(.radioButton)
            row.setAccessibilityLabel(item.title)
            row.setAccessibilityValue(item == mode ? 1 : 0)
            row.wantsLayer = true
            row.layer?.cornerRadius = compact ? 3 : 10
            if item == mode { row.layer?.backgroundColor = NSColor.controlAccentColor.cgColor }
            let label = NSTextField(labelWithString: item.shortTitle)
            label.font = .systemFont(ofSize: compact ? fontSize : 20, weight: item == mode ? .semibold : .regular)
            label.textColor = item == mode ? .white : .labelColor
            label.alignment = .center
            label.frame = compact ? row.bounds.insetBy(dx: padding, dy: padding)
                : NSRect(x: 0, y: 10, width: row.frame.width, height: 28)
            label.setAccessibilityElement(false)
            row.addSubview(label)
            background.addSubview(row)
        }
        if let captionFrame = layout.caption {
            let caption = NSTextField(labelWithString: "Meltype")
            caption.font = .systemFont(ofSize: 11)
            caption.textColor = .secondaryLabelColor
            caption.alignment = .center
            caption.frame = captionFrame
            background.addSubview(caption)
        }
        return background
    }

    func show(_ mode: MacInputMode, caret: NSRect? = nil, milliseconds: Int) {
        timer?.invalidate()
        generation += 1
        let currentGeneration = generation
        // サービスと IMK の確定イベントが完了してから出す。確定に伴う UI の整理で隠されないようにする。
        DispatchQueue.main.async { [weak self] in
            guard let self, self.generation == currentGeneration else { return }
            self.present(mode, caret: caret, milliseconds: milliseconds)
        }
    }

    private func present(_ mode: MacInputMode, caret: NSRect?, milliseconds: Int) {
        let screen: NSScreen?
        if let caret {
            screen = NSScreen.screens.first(where: { $0.frame.contains(caret.origin) || $0.frame.intersects(caret) })
        } else {
            screen = NSScreen.screens.first(where: { NSMouseInRect(NSEvent.mouseLocation, $0.frame, false) })
        }
        guard let screen else { hide(); return }
        let frame = Self.frame(caret: caret, visible: screen.visibleFrame)
        let panel = self.panel ?? NSPanel(contentRect: frame, styleMask: [.borderless, .nonactivatingPanel],
                                        backing: .buffered, defer: false)
        panel.level = .popUpMenu
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.ignoresMouseEvents = true
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.contentView = Self.content(mode: mode, compact: caret != nil)
        panel.setFrame(frame, display: true)
        self.panel = panel
        panel.orderFrontRegardless()
        timer = Timer.scheduledTimer(withTimeInterval: Double(milliseconds) / 1000, repeats: false) { [weak self] _ in self?.hide() }
    }

    func hide() {
        generation += 1
        timer?.invalidate()
        timer = nil
        panel?.orderOut(nil)
    }

    deinit { timer?.invalidate() }
}
