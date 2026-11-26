// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

import Cocoa

struct ColorSwatchRgba: Equatable {
    let r: UInt8
    let g: UInt8
    let b: UInt8
    let a: UInt8
}

enum ColorSwatchParser {
    static func parseRgba(_ text: String) -> ColorSwatchRgba? {
        guard !text.isEmpty else { return nil }
        let scalars = Array(text.unicodeScalars)
        if scalars[0] == "#" {
            return parseHex(scalars)
        }
        if let rgb = parseRgb(scalars) {
            return rgb
        }
        return parseHsl(scalars)
    }

    private static func parseHex(_ scalars: [UnicodeScalar]) -> ColorSwatchRgba? {
        let len = scalars.count - 1
        guard len == 3 || len == 4 || len == 6 || len == 8 else { return nil }
        var digits = [UInt8]()
        digits.reserveCapacity(len)
        for i in 1..<scalars.count {
            guard let d = hexDigit(scalars[i]) else { return nil }
            digits.append(d)
        }
        if len == 3 || len == 4 {
            return ColorSwatchRgba(
                r: (digits[0] << 4) | digits[0],
                g: (digits[1] << 4) | digits[1],
                b: (digits[2] << 4) | digits[2],
                a: len == 4 ? (digits[3] << 4) | digits[3] : 255
            )
        }
        return ColorSwatchRgba(
            r: (digits[0] << 4) | digits[1],
            g: (digits[2] << 4) | digits[3],
            b: (digits[4] << 4) | digits[5],
            a: len == 8 ? (digits[6] << 4) | digits[7] : 255
        )
    }

    private static func parseRgb(_ scalars: [UnicodeScalar]) -> ColorSwatchRgba? {
        var i = 0
        guard matchPrefix(scalars, &i, "rgba(") || matchPrefix(scalars, &i, "rgb(") else { return nil }
        skipSpaces(scalars, &i)
        guard let r = parseByteComponent(scalars, &i),
              parseSeparator(scalars, &i),
              let g = parseByteComponent(scalars, &i),
              parseSeparator(scalars, &i),
              let b = parseByteComponent(scalars, &i),
              let a = parseAlphaSuffix(scalars, &i) else { return nil }
        return ColorSwatchRgba(r: r, g: g, b: b, a: a)
    }

    private static func parseHsl(_ scalars: [UnicodeScalar]) -> ColorSwatchRgba? {
        var i = 0
        guard matchPrefix(scalars, &i, "hsla(") || matchPrefix(scalars, &i, "hsl(") else { return nil }
        skipSpaces(scalars, &i)
        guard let hDeg = parseDouble(scalars, &i, allowNegative: true),
              parseSeparator(scalars, &i),
              let sPct = parseDouble(scalars, &i, allowNegative: false),
              i < scalars.count, scalars[i] == "%", sPct >= 0, sPct <= 100 else { return nil }
        i += 1
        guard parseSeparator(scalars, &i),
              let lPct = parseDouble(scalars, &i, allowNegative: false),
              i < scalars.count, scalars[i] == "%", lPct >= 0, lPct <= 100 else { return nil }
        i += 1
        guard let a = parseAlphaSuffix(scalars, &i) else { return nil }

        let h = ((hDeg.truncatingRemainder(dividingBy: 360.0)) + 360.0).truncatingRemainder(dividingBy: 360.0) / 360.0
        let s = sPct / 100.0
        let l = lPct / 100.0
        if s == 0 {
            let gray = UInt8(clamping: Int(round(l * 255.0)))
            return ColorSwatchRgba(r: gray, g: gray, b: gray, a: a)
        }
        let q = l < 0.5 ? l * (1.0 + s) : l + s - l * s
        let p = 2.0 * l - q
        let r = UInt8(clamping: Int(round(hueToRgb(p, q, h + 1.0 / 3.0) * 255.0)))
        let g = UInt8(clamping: Int(round(hueToRgb(p, q, h) * 255.0)))
        let b = UInt8(clamping: Int(round(hueToRgb(p, q, h - 1.0 / 3.0) * 255.0)))
        return ColorSwatchRgba(r: r, g: g, b: b, a: a)
    }

    private static func hueToRgb(_ p: Double, _ q: Double, _ tInput: Double) -> Double {
        var t = tInput
        if t < 0 { t += 1.0 }
        if t > 1 { t -= 1.0 }
        if t < 1.0 / 6.0 { return p + (q - p) * 6.0 * t }
        if t < 0.5 { return q }
        if t < 2.0 / 3.0 { return p + (q - p) * (2.0 / 3.0 - t) * 6.0 }
        return p
    }

    private static func hexDigit(_ scalar: UnicodeScalar) -> UInt8? {
        switch scalar.value {
        case 48...57: return UInt8(scalar.value - 48)
        case 65...70: return UInt8(scalar.value - 55)
        case 97...102: return UInt8(scalar.value - 87)
        default: return nil
        }
    }

    private static func skipSpaces(_ scalars: [UnicodeScalar], _ i: inout Int) {
        while i < scalars.count && (scalars[i] == " " || scalars[i] == "\t") { i += 1 }
    }

    private static func matchPrefix(_ scalars: [UnicodeScalar], _ i: inout Int, _ word: String) -> Bool {
        let target = Array(word.unicodeScalars)
        guard i + target.count <= scalars.count else { return false }
        for offset in 0..<target.count {
            let v = scalars[i + offset].value
            let lower = (v >= 65 && v <= 90) ? v + 32 : v
            if lower != target[offset].value { return false }
        }
        i += target.count
        return true
    }

    private static func parseByteComponent(_ scalars: [UnicodeScalar], _ i: inout Int) -> UInt8? {
        let start = i
        var value = 0
        while i < scalars.count && scalars[i].value >= 48 && scalars[i].value <= 57 {
            value = value * 10 + Int(scalars[i].value - 48)
            if value > 255 { return nil }
            i += 1
        }
        return i > start ? UInt8(value) : nil
    }

    private static func parseDouble(_ scalars: [UnicodeScalar], _ i: inout Int, allowNegative: Bool) -> Double? {
        let start = i
        if allowNegative && i < scalars.count && scalars[i] == "-" { i += 1 }
        var hasDigit = false
        while i < scalars.count && scalars[i].value >= 48 && scalars[i].value <= 57 {
            hasDigit = true
            i += 1
        }
        if i < scalars.count && scalars[i] == "." {
            i += 1
            while i < scalars.count && scalars[i].value >= 48 && scalars[i].value <= 57 {
                hasDigit = true
                i += 1
            }
        }
        guard hasDigit else {
            i = start
            return nil
        }
        let token = String(String.UnicodeScalarView(scalars[start..<i]))
        return Double(token)
    }

    private static func parseSeparator(_ scalars: [UnicodeScalar], _ i: inout Int) -> Bool {
        let before = i
        skipSpaces(scalars, &i)
        if i < scalars.count && scalars[i] == "," {
            i += 1
            skipSpaces(scalars, &i)
            return true
        }
        return i > before
    }

    private static func parseAlphaSuffix(_ scalars: [UnicodeScalar], _ i: inout Int) -> UInt8? {
        skipSpaces(scalars, &i)
        if i < scalars.count && scalars[i] == ")" {
            i += 1
            return i == scalars.count ? 255 : nil
        }
        guard i < scalars.count && (scalars[i] == "," || scalars[i] == "/") else { return nil }
        i += 1
        skipSpaces(scalars, &i)
        guard var val = parseDouble(scalars, &i, allowNegative: false) else { return nil }
        if i < scalars.count && scalars[i] == "%" {
            i += 1
            guard val >= 0 && val <= 100 else { return nil }
            val /= 100.0
        } else if val < 0 || val > 1 {
            return nil
        }
        skipSpaces(scalars, &i)
        guard i < scalars.count && scalars[i] == ")" else { return nil }
        i += 1
        return i == scalars.count ? UInt8(clamping: Int(round(val * 255.0))) : nil
    }
}

private final class ColorSwatchView: NSView {
    private let rgba: ColorSwatchRgba

    init(rgba: ColorSwatchRgba) {
        self.rgba = rgba
        super.init(frame: .zero)
        translatesAutoresizingMaskIntoConstraints = false
    }

    required init?(coder: NSCoder) { nil }

    override func draw(_ dirtyRect: NSRect) {
        if rgba.a < 255 {
            let midX = bounds.midX
            let midY = bounds.midY
            NSColor.white.setFill()
            NSBezierPath.fill(NSRect(x: bounds.minX, y: midY, width: midX - bounds.minX, height: bounds.maxY - midY))
            NSBezierPath.fill(NSRect(x: midX, y: bounds.minY, width: bounds.maxX - midX, height: midY - bounds.minY))
            NSColor.lightGray.setFill()
            NSBezierPath.fill(NSRect(x: midX, y: midY, width: bounds.maxX - midX, height: bounds.maxY - midY))
            NSBezierPath.fill(NSRect(x: bounds.minX, y: bounds.minY, width: midX - bounds.minX, height: midY - bounds.minY))
        }
        NSColor(srgbRed: CGFloat(rgba.r) / 255.0,
                green: CGFloat(rgba.g) / 255.0,
                blue: CGFloat(rgba.b) / 255.0,
                alpha: CGFloat(rgba.a) / 255.0).setFill()
        NSBezierPath.fill(bounds)
        NSColor.secondaryLabelColor.withAlphaComponent(0.6).setStroke()
        let border = NSBezierPath(rect: bounds.insetBy(dx: 0.5, dy: 0.5))
        border.lineWidth = 1
        border.stroke()
    }
}

/// 未確定文字列の行全体を避け、案内を入力行に近い側へ置く。
struct CompositionPanelPlacement {
    private static let gap: CGFloat = 6
    let caret: NSRect
    let visible: NSRect
    let above: Bool
    let availableHeight: CGFloat
    /// 入力位置が分からないときだけ、パネルの中央を caret にそろえる。
    let centered: Bool

    init?(start: NSRect, end: NSRect, visible: NSRect) {
        guard ModeIndicator.validCaret(start), ModeIndicator.validCaret(end),
              visible.minY <= start.midY, start.midY <= visible.maxY,
              visible.minY <= end.midY, end.midY <= visible.maxY else { return nil }
        caret = NSRect(x: start.minX, y: min(start.minY, end.minY), width: start.width,
                       height: max(start.maxY, end.maxY) - min(start.minY, end.minY))
        self.visible = visible
        let below = caret.minY - visible.minY
        let higher = visible.maxY - caret.maxY
        // 表示する候補や案内に関係なく、広い側を使う。同じ行で候補が出入りしても側が変わらない。
        above = higher > below
        availableHeight = floor(max(below, higher) - Self.gap)
        centered = false
        guard availableHeight > 0 else { return nil }
    }

    /// 入力位置が分からないときは、画面の下端の中央を入力位置とみなし、その上に出す。
    init(bottomOf visible: NSRect) {
        caret = NSRect(x: visible.midX, y: visible.minY, width: 0, height: 0)
        self.visible = visible
        above = true
        availableHeight = floor(visible.height - Self.gap)
        centered = true
    }

    func frame(size: NSSize) -> NSRect {
        let x = centered ? caret.midX - size.width / 2 : caret.minX
        return NSRect(x: min(max(x, visible.minX), visible.maxX - size.width),
                      y: above ? caret.maxY + Self.gap : caret.minY - Self.gap - size.height,
                      width: size.width, height: size.height)
    }
}

private final class NonactivatingCompositionWindow: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

private final class CandidateTable: NSTableView {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

private final class CandidateScrollView: NSScrollView {
    var turnPage: ((Int) -> Bool)?
    private var turnedDuringGesture = false

    override func scrollWheel(with event: NSEvent) {
        if event.phase == .began { turnedDuringGesture = false }
        let delta = event.scrollingDeltaY
        if let documentView, delta != 0, event.momentumPhase.isEmpty,
           event.phase.isEmpty || !turnedDuringGesture {
            let viewport = documentVisibleRect
            let bounds = documentView.bounds
            let atEdge = delta > 0 ? viewport.minY <= bounds.minY : viewport.maxY >= bounds.maxY - 1
            // まずページ内をスクロールし、端に達した後の操作でページを送る。
            if atEdge, turnPage?(delta > 0 ? -1 : 1) == true {
                turnedDuringGesture = true
                return
            }
        }
        super.scrollWheel(with: event)
    }
}

private final class CandidateRowView: NSTableRowView {
    var choose: ((CandidateRowView) -> Bool)?

    override func accessibilityPerformPress() -> Bool {
        choose?(self) ?? false
    }
}

/// Core が返した一覧と案内を、入力先のフォーカスを保ったまま表示する。
final class CompositionPanel: NSObject, NSTableViewDataSource, NSTableViewDelegate {
    private static let padding: CGFloat = 8
    private static let spacing: CGFloat = 4
    private static let candidatePadding: CGFloat = 4
    private static let noteGap: CGFloat = 12
    private static let numberColumnWidth: CGFloat = 26
    // Windows IME の候補窓と同じ論理サイズにそろえ、入力欄の書式で一覧の高さが変わらないようにする。
    private static let candidateFont = NSFont.systemFont(ofSize: 15)
    private static let guideFont = NSFont.systemFont(ofSize: 12)
    private static let meaningFont = NSFont.systemFont(ofSize: 13)
    private static let candidateRowHeight: CGFloat = 32
    private static let swatchSize: CGFloat = 14
    private static let swatchGap: CGFloat = 8
    // Windows IME の候補窓 (kMaxWidth) と同じ上限。長い案内や候補でパネルが広がりすぎないようにする。
    private static let maxWidth: CGFloat = 520
    // Core の数字キー選択は、選択中の候補が属する 9 件のページ内で行う。
    private static let candidatesPerPage = 9

    private let window = NonactivatingCompositionWindow(contentRect: .zero,
        styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
    private let scroll = CandidateScrollView()
    private let table = CandidateTable()
    private let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("candidate"))
    private var rows: [Row] = []
    // 案内は Windows IME と同じく 1 行に収め、はみ出す分は打ち切る。折り返すと候補一覧の高さを奪う。
    private let guideLabel: NSTextField = {
        let label = NSTextField(labelWithString: "")
        label.cell?.lineBreakMode = .byTruncatingTail
        return label
    }()
    private let meaningLabel = CompositionPanel.makeLabel()
    private let positionLabel = CompositionPanel.makeLabel()
    private let measuringLabel = CompositionPanel.makeLabel()
    private let choose: (Int) -> Void
    private let highlight: (Int) -> Void

    private var content: Content?
    private var rowsKey: RowsKey?
    private var meaningRequest: MeaningRequest?
    private var meaningTimer: Timer?
    private var meaning: String?

    private struct Row {
        let index: Int
        let number: Int?
        let title: NSAttributedString
        let note: NSAttributedString?
        let noteWidth: CGFloat
        let swatch: ColorSwatchRgba?
        let height: CGFloat
    }

    private struct Content {
        let guide: String
        let list: CandidateList?
        let converting: Bool
        let placement: CompositionPanelPlacement

        var pageStart: Int {
            guard converting, let list, list.items.indices.contains(list.selected) else { return 0 }
            return list.selected / CompositionPanel.candidatesPerPage * CompositionPanel.candidatesPerPage
        }
    }

    private struct CandidateText {
        let title: NSAttributedString
        let titleWidth: CGFloat
        let note: NSAttributedString?
        let noteWidth: CGFloat
        let swatch: ColorSwatchRgba?
    }

    private struct RowsKey: Equatable {
        let items: [String]
        let notes: [String?]
        let numbered: Bool
        let range: Range<Int>
        let width: CGFloat
    }

    private struct MeaningRequest: Equatable {
        let clause: Int
        let index: Int
        let candidate: String
        let text: String
        let milliseconds: Int
    }

    init(choose: @escaping (Int) -> Void, highlight: @escaping (Int) -> Void) {
        self.choose = choose
        self.highlight = highlight
        super.init()
        window.title = "Meltype"
        window.setAccessibilityLabel("Meltype の変換候補と入力案内")
        window.hidesOnDeactivate = false
        window.hasShadow = true
        window.backgroundColor = .windowBackgroundColor
        window.collectionBehavior = [.moveToActiveSpace, .fullScreenAuxiliary, .ignoresCycle]

        scroll.drawsBackground = false
        scroll.hasVerticalScroller = true
        // スクロールバーに候補の文字が重ならないよう、一覧の右に幅を確保する。
        scroll.scrollerStyle = .legacy
        scroll.autohidesScrollers = true
        scroll.documentView = table
        scroll.turnPage = { [weak self] step in self?.turnPage(step) ?? false }

        table.style = .plain
        table.rowSizeStyle = .custom
        table.headerView = nil
        table.intercellSpacing = .zero
        table.backgroundColor = .clear
        table.setAccessibilityLabel("変換候補")
        table.allowsEmptySelection = true
        table.addTableColumn(column)
        table.dataSource = self
        table.delegate = self
        table.target = self
        table.action = #selector(candidateClicked(_:))

        window.contentView?.subviews = [guideLabel, scroll, positionLabel, meaningLabel]
    }

    func show(guide: String, list: CandidateList?, converting: Bool, clause: Int, placement: CompositionPanelPlacement,
              aboveWindowLevel: Int32) {
        content = Content(guide: guide, list: list, converting: converting, placement: placement)
        window.level = NSWindow.Level(rawValue: max(NSWindow.Level.floating.rawValue, Int(aboveWindowLevel) + 1))

        var request: MeaningRequest?
        if let list, list.items.indices.contains(list.selected), let text = list.selectedMeaning, !text.isEmpty {
            request = MeaningRequest(clause: clause, index: list.selected, candidate: list.items[list.selected],
                                     text: text, milliseconds: list.meaningDelayMs)
        }
        if request != meaningRequest {
            meaningTimer?.invalidate()
            meaningTimer = nil
            meaning = nil
            meaningRequest = request
            if let request {
                if request.milliseconds <= 0 {
                    meaning = request.text
                } else {
                    let timer = Timer(timeInterval: Double(request.milliseconds) / 1000, repeats: false) { [weak self] _ in
                        guard let self else { return }
                        self.meaningTimer = nil
                        self.meaning = request.text
                        self.layout()
                    }
                    // 候補をスクロールしている間 (eventTracking) も意味を出せるよう、common に登録する。
                    RunLoop.main.add(timer, forMode: .common)
                    meaningTimer = timer
                }
            }
        }
        layout()
    }

    func hide() {
        meaningTimer?.invalidate()
        meaningTimer = nil
        meaningRequest = nil
        meaning = nil
        content = nil
        rowsKey = nil
        rows = []
        table.reloadData()
        window.orderOut(nil)
    }

    private func layout() {
        guard let content else { return }
        let padding = Self.padding
        let spacing = Self.spacing
        let list = content.list
        let items = list?.items ?? []
        let notes = list?.notes ?? []
        let numbered = list?.numbered == true
        let numberWidth = numbered ? Self.numberColumnWidth : 0
        let candidates = items.enumerated().map { index, item in
            candidateText(item, note: notes.indices.contains(index) ? notes[index] : nil)
        }
        // 表示中のパネルの大きさと中身を、途中の状態を見せずに一度に入れ替える。
        if window.isVisible { window.disableScreenUpdatesUntilFlush() }
        let pageStart = content.pageStart
        let end = content.converting ? min(items.count, pageStart + Self.candidatesPerPage) : items.count
        let range = pageStart..<end
        guideLabel.attributedStringValue = styled(content.guide, font: Self.guideFont, color: .labelColor, lineBreakMode: .byTruncatingTail)
        meaningLabel.attributedStringValue = styled(meaning ?? "", font: Self.meaningFont, color: .secondaryLabelColor)
        let position = content.converting && items.count > Self.candidatesPerPage
            ? "\(max(0, list?.selected ?? 0) + 1) / \(items.count)" : ""
        positionLabel.attributedStringValue = styled(position, font: Self.guideFont, color: .secondaryLabelColor, alignment: .right)
        positionLabel.alignment = .right
        let rowInset = 2 * Self.candidatePadding
        let scrollerWidth = NSScroller.scrollerWidth(for: .regular, scrollerStyle: .legacy)
        // セル自身を測り、描画時に引かれる文字欄の左右の余白も幅に含める。
        let longestRow = candidates.map {
            $0.titleWidth
                + ($0.swatch == nil ? 0 : Self.swatchGap + Self.swatchSize)
                + ($0.note == nil ? 0 : Self.noteGap + $0.noteWidth)
        }.max() ?? 0
        let listWidth = items.isEmpty ? 0 : longestRow + numberWidth + rowInset + scrollerWidth
        let width = min(max(listWidth, naturalWidth(of: guideLabel), naturalWidth(of: meaningLabel), naturalWidth(of: positionLabel), 44),
                        floor(min(content.placement.visible.width, Self.maxWidth)) - 2 * padding)
        var remaining = content.placement.availableHeight - 2 * padding
        let layoutManager = NSLayoutManager()
        let guideLineHeight = ceil(layoutManager.defaultLineHeight(for: Self.guideFont))
        guard width > rowInset + scrollerWidth + numberWidth, remaining >= guideLineHeight else {
            window.orderOut(nil)
            return
        }

        let guideHeight = min(height(of: guideLabel, width: width), remaining)
        remaining -= guideHeight
        if guideHeight > 0 { remaining -= spacing }

        let rowWidth = floor(width - scrollerWidth)
        let newRows = range.map { index in
            makeRow(candidates[index], index: index, number: numbered ? index - pageStart + 1 : nil,
                    availableWidth: rowWidth - rowInset - numberWidth)
        }
        let positionHeight = height(of: positionLabel, width: width)
        let showPosition = positionHeight > 0 && remaining >= Self.candidateRowHeight + positionHeight + spacing
        if showPosition { remaining -= positionHeight + spacing }
        // 意味は案内と候補一行が収まった後に出し、余った高さを一覧に使う。
        let meaningLineHeight = ceil(layoutManager.defaultLineHeight(for: Self.meaningFont))
        let detailBudget = remaining - Self.candidateRowHeight - spacing
        let detailHeight = detailBudget >= meaningLineHeight
            ? min(height(of: meaningLabel, width: width), 3 * meaningLineHeight, detailBudget) : 0
        if detailHeight > 0 { remaining -= detailHeight + spacing }
        // 最後のページの候補が少なくても、ページ送りでパネルの高さを変えない。
        let regularPageHeight = CGFloat(min(items.count, Self.candidatesPerPage)) * Self.candidateRowHeight
        let documentHeight = newRows.reduce(CGFloat.zero) { $0 + $1.height }
        let listHeight = content.converting && items.count > Self.candidatesPerPage ? regularPageHeight : documentHeight
        let scrollHeight = remaining >= Self.candidateRowHeight ? min(listHeight, regularPageHeight, remaining) : 0

        let sections: [(NSView, CGFloat)] = [
            (guideLabel, guideHeight), (scroll, scrollHeight),
            (positionLabel, showPosition ? positionHeight : 0), (meaningLabel, detailHeight)
        ]
        let heights = sections.map(\.1).filter { $0 > 0 }
        guard !heights.isEmpty else {
            window.orderOut(nil)
            return
        }
        let selectionWasVisible = table.selectedRow >= 0 && !scroll.isHidden
            && table.visibleRect.intersects(table.rect(ofRow: table.selectedRow))
        let viewportShrank = scrollHeight < scroll.frame.height
        let height = heights.reduce(0, +) + CGFloat(heights.count - 1) * spacing + 2 * padding
        let frame = content.placement.frame(size: NSSize(width: width + 2 * padding, height: height))
        var edge = content.placement.above ? padding : height - padding
        for (view, viewHeight) in sections {
            view.isHidden = viewHeight == 0
            guard viewHeight > 0 else { continue }
            if !content.placement.above { edge -= viewHeight }
            view.frame = NSRect(x: padding, y: edge, width: width, height: viewHeight)
            edge += content.placement.above ? viewHeight + spacing : -spacing
        }

        guideLabel.toolTip = content.guide
        meaningLabel.toolTip = meaning
        let key = RowsKey(items: items, notes: notes, numbered: numbered, range: range, width: rowWidth)
        updateRows(newRows, key: key, selected: list?.selected ?? -1, viewportHeight: scrollHeight,
                   documentHeight: documentHeight, revealSelection: selectionWasVisible && viewportShrank)
        // 中身を並べ終えてから大きさを変えて描く。先に大きさを変えると、古い中身のまま広がった枠が一瞬見える。
        window.setFrame(frame, display: true)
        window.ignoresMouseEvents = scrollHeight == 0
        if !window.isVisible { window.orderFrontRegardless() }
    }

    private func styled(_ text: String, font: NSFont, color: NSColor,
                        alignment: NSTextAlignment = .left,
                        lineBreakMode: NSLineBreakMode = .byCharWrapping) -> NSAttributedString {
        let paragraph = NSMutableParagraphStyle()
        paragraph.alignment = alignment
        paragraph.lineBreakMode = lineBreakMode
        return NSAttributedString(string: text, attributes: [.font: font, .foregroundColor: color, .paragraphStyle: paragraph])
    }

    private static func makeLabel() -> NSTextField {
        let label = NSTextField(wrappingLabelWithString: "")
        label.alignment = .left
        label.cell?.lineBreakMode = .byCharWrapping
        label.cell?.truncatesLastVisibleLine = true
        return label
    }

    private func naturalWidth(of label: NSTextField) -> CGFloat {
        label.stringValue.isEmpty ? 0 : ceil(label.cell?.cellSize.width ?? 0)
    }

    private func height(of label: NSTextField, width: CGFloat) -> CGFloat {
        guard !label.stringValue.isEmpty else { return 0 }
        let bounds = NSRect(x: 0, y: 0, width: width, height: .greatestFiniteMagnitude)
        return ceil(label.cell?.cellSize(forBounds: bounds).height ?? 0)
    }

    private func candidateText(_ item: String, note: String?) -> CandidateText {
        let title = styled(item, font: Self.candidateFont, color: .labelColor)
        measuringLabel.attributedStringValue = title
        let titleWidth = naturalWidth(of: measuringLabel)
        let swatch = ColorSwatchParser.parseRgba(item)
        guard let note, !note.isEmpty else {
            return CandidateText(title: title, titleWidth: titleWidth, note: nil, noteWidth: 0, swatch: swatch)
        }
        let detail = styled(note, font: Self.guideFont, color: .secondaryLabelColor, alignment: .right)
        measuringLabel.attributedStringValue = detail
        return CandidateText(title: title, titleWidth: titleWidth, note: detail, noteWidth: naturalWidth(of: measuringLabel), swatch: swatch)
    }

    private func makeRow(_ text: CandidateText, index: Int, number: Int?, availableWidth: CGFloat) -> Row {
        let spacing = min(Self.noteGap, floor(availableWidth / 4))
        let swatchSpace = text.swatch == nil ? 0 : Self.swatchSize + Self.swatchGap
        let noteWidth = min(text.noteWidth, max(floor(availableWidth / 2) - spacing, availableWidth - spacing - text.titleWidth - swatchSpace))
        let reserved = (noteWidth > 0 ? noteWidth + spacing : 0) + swatchSpace
        measuringLabel.attributedStringValue = text.title
        let titleHeight = height(of: measuringLabel, width: availableWidth - reserved)
        var noteHeight: CGFloat = 0
        if let note = text.note, noteWidth > 0 {
            measuringLabel.attributedStringValue = note
            noteHeight = height(of: measuringLabel, width: noteWidth)
        }
        let rowHeight = max(Self.candidateRowHeight, max(titleHeight, noteHeight) + 2 * Self.candidatePadding)
        return Row(index: index, number: number, title: text.title, note: text.note, noteWidth: noteWidth, swatch: text.swatch, height: rowHeight)
    }

    private func updateRows(_ newRows: [Row], key: RowsKey, selected: Int, viewportHeight: CGFloat,
                            documentHeight: CGFloat, revealSelection: Bool) {
        let samePage = rowsKey?.items == key.items && rowsKey?.range == key.range
        let previousSelection = table.selectedRow
        let previousOffset = samePage ? scroll.contentView.bounds.origin.y : 0
        if key != rowsKey {
            rowsKey = key
            rows = newRows
            column.width = key.width
            table.reloadData()
        }
        scroll.contentView.scroll(to: NSPoint(x: 0, y: min(previousOffset, max(0, documentHeight - viewportHeight))))
        scroll.reflectScrolledClipView(scroll.contentView)

        let selectedRow = selected - key.range.lowerBound
        let selection = rows.indices.contains(selectedRow) ? IndexSet(integer: selectedRow) : IndexSet()
        if table.selectedRowIndexes != selection {
            table.selectRowIndexes(selection, byExtendingSelection: false)
        }
        if !selection.isEmpty, viewportHeight > 0, revealSelection || !samePage || previousSelection != selectedRow {
            table.scrollRowToVisible(selectedRow)
        }
    }

    func numberOfRows(in tableView: NSTableView) -> Int {
        rows.count
    }

    func tableView(_ tableView: NSTableView, heightOfRow row: Int) -> CGFloat {
        rows[row].height
    }

    private func addLabel(_ text: NSAttributedString, to cell: NSTableCellView) -> NSTextField {
        let label = Self.makeLabel()
        label.attributedStringValue = text
        label.toolTip = text.string
        label.translatesAutoresizingMaskIntoConstraints = false
        cell.addSubview(label)
        NSLayoutConstraint.activate([
            label.topAnchor.constraint(equalTo: cell.topAnchor, constant: Self.candidatePadding),
            label.bottomAnchor.constraint(equalTo: cell.bottomAnchor, constant: -Self.candidatePadding)
        ])
        return label
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let item = rows[row]
        let cell = NSTableCellView()
        let padding = Self.candidatePadding
        let numberWidth = item.number == nil ? 0 : Self.numberColumnWidth
        if let number = item.number {
            let label = addLabel(styled(String(number), font: Self.guideFont, color: .secondaryLabelColor), to: cell)
            NSLayoutConstraint.activate([
                label.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: padding),
                label.widthAnchor.constraint(equalToConstant: numberWidth)
            ])
        }
        let title = addLabel(item.title, to: cell)
        cell.textField = title
        title.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: padding + numberWidth).isActive = true

        var trailingTarget: NSLayoutXAxisAnchor = cell.trailingAnchor
        var trailingConstant: CGFloat = -padding
        if let note = item.note, item.noteWidth > 0 {
            let detail = addLabel(note, to: cell)
            detail.alignment = .right
            let availableWidth = column.width - 2 * padding - numberWidth
            let spacing = min(Self.noteGap, floor(availableWidth / 4))
            NSLayoutConstraint.activate([
                detail.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -padding),
                detail.widthAnchor.constraint(equalToConstant: item.noteWidth)
            ])
            trailingTarget = detail.leadingAnchor
            trailingConstant = -spacing
        }
        if let swatch = item.swatch {
            let swatchView = ColorSwatchView(rgba: swatch)
            cell.addSubview(swatchView)
            title.setContentHuggingPriority(.defaultHigh, for: .horizontal)
            title.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
            NSLayoutConstraint.activate([
                swatchView.leadingAnchor.constraint(equalTo: title.trailingAnchor, constant: Self.swatchGap),
                swatchView.trailingAnchor.constraint(lessThanOrEqualTo: trailingTarget, constant: trailingConstant),
                swatchView.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
                swatchView.widthAnchor.constraint(equalToConstant: Self.swatchSize),
                swatchView.heightAnchor.constraint(equalToConstant: Self.swatchSize)
            ])
        } else {
            title.trailingAnchor.constraint(equalTo: trailingTarget, constant: trailingConstant).isActive = true
        }
        return cell
    }

    func tableView(_ tableView: NSTableView, rowViewForRow row: Int) -> NSTableRowView? {
        let view = CandidateRowView()
        view.choose = { [weak self] row in
            guard let self else { return false }
            return self.chooseVisibleRow(self.table.row(for: row))
        }
        return view
    }

    @objc private func candidateClicked(_ sender: NSTableView) {
        _ = chooseVisibleRow(sender.clickedRow)
    }

    private func chooseVisibleRow(_ index: Int) -> Bool {
        guard window.isVisible, !scroll.isHidden, rows.indices.contains(index) else { return false }
        choose(rows[index].index)
        return true
    }

    private func turnPage(_ step: Int) -> Bool {
        guard window.isVisible, !scroll.isHidden, let content, content.converting, let list = content.list,
              let first = rows.first else { return false }
        let start = first.index + step * Self.candidatesPerPage
        guard list.items.indices.contains(start) else { return false }
        highlight(start)
        return true
    }
}
