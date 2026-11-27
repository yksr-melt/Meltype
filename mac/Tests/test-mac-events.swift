// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 0924haruto12

import Cocoa
import InputMethodKit
import Carbon.HIToolbox
final class EventClient: NSObject, IMKTextInput {
 var document=""; var marked=""
 var appID = "local.meltype.event-client"
 var canReadSubstring = true
 var contextReads = 0
 var lineRect = NSRect(x:0,y:0,width:1,height:20)
 var fontSize: CGFloat = 13
 func text(_ value:Any?)->String { (value as? NSAttributedString)?.string ?? (value as? String ?? "") }
 func insertText(_ string: Any!, replacementRange: NSRange) { let value=text(string); if replacementRange.location != NSNotFound { document=(document as NSString).replacingCharacters(in:replacementRange,with:value) } else {document+=value}; marked="" }
 func setMarkedText(_ string: Any!, selectionRange: NSRange, replacementRange: NSRange) {marked=text(string)}
 func selectedRange()->NSRange {
  contextReads += 1
  return NSRange(location: (document as NSString).length, length: 0)
 }
 func markedRange()->NSRange {
  contextReads += 1
  if marked.isEmpty { return NSRange(location: NSNotFound, length: 0) }
  return NSRange(location: (document as NSString).length, length: (marked as NSString).length)
 }
 func attributedSubstring(from range:NSRange)->NSAttributedString! {
  contextReads += 1
  let text = document as NSString
  guard canReadSubstring, range.length > 0, range.location != NSNotFound, range.location <= text.length else { return nil }
  let length = min(range.length, text.length - range.location)
  return NSAttributedString(string: text.substring(with: NSRange(location: range.location, length: length)))
 }
 func length()->Int {(document as NSString).length}
 func characterIndex(for point:NSPoint, tracking mode:IMKLocationToOffsetMappingMode, inMarkedRange:UnsafeMutablePointer<ObjCBool>!)->Int {inMarkedRange?.pointee=false; return 0}
 func attributes(forCharacterIndex index:Int, lineHeightRectangle rectangle:UnsafeMutablePointer<NSRect>!)->[AnyHashable:Any]! {
  rectangle?.pointee = lineRect
  return [NSAttributedString.Key.font: NSFont.systemFont(ofSize: fontSize)]
 }
 func validAttributesForMarkedText()->[Any]! {[]}
 func overrideKeyboard(withKeyboardNamed name:String!) {}
 func selectMode(_ identifier:String!) {}
 func supportsUnicode()->Bool {true}
 func bundleIdentifier()->String! {appID}
 func windowLevel()->Int32 {0}
 func supportsProperty(_ property:TSMDocumentPropertyTag)->Bool {true}
 func uniqueClientIdentifierString()->String! {"synthetic-event-client"}
 func string(from range:NSRange, actualRange:UnsafeMutablePointer<NSRange>!)->String! {actualRange?.pointee=range;return attributedSubstring(from:range)?.string}
 func firstRect(forCharacterRange range:NSRange, actualRange:UnsafeMutablePointer<NSRange>!)->NSRect {actualRange?.pointee=range;return .zero}
}
@main
struct EventTests {
 static func main() {
  NativeCore.shared.initialize()
  let app=NSApplication.shared
  app.setActivationPolicy(.prohibited)
  guard let server=IMKServer(name:"LocalMeltypeBoundaryTests",bundleIdentifier:Bundle.main.bundleIdentifier) else {fatalError("IMKServer creation failed")}
  var passed=0
  func check(_ name:String,_ body:(MeltypeInputController,EventClient)->Void) {
   let client=EventClient()
   // IMKInputController の初期化は OS のクライアントプロキシ専用なので nil で初期化する。
   // handle / commitComposition には合成 IMKTextInput クライアントを明示して渡す。
   guard let controller=MeltypeInputController(server:server,delegate:nil,client:nil) else {fatalError("Controller creation failed")}
   body(controller,client); passed+=1; print("PASS: \(name)")
  }
  func key(_ controller:MeltypeInputController,_ client:EventClient,_ chars:String,code:UInt16=0,flags:NSEvent.ModifierFlags=[])->Bool {
   let event=NSEvent.keyEvent(with:.keyDown,location:.zero,modifierFlags:flags,timestamp:0,windowNumber:0,context:nil,characters:chars,charactersIgnoringModifiers:chars,isARepeat:false,keyCode:code)!
   let consumed=controller.handle(event,client:client)
   if !consumed && !flags.contains(.command) && !flags.contains(.control) && chars.unicodeScalars.allSatisfy({$0.value>=0x20}) {
    // アプリが元イベントを受ける部分を 1 回だけ模擬する。
    client.insertText(chars,replacementRange:NSRange(location:NSNotFound,length:0))
   }
   return consumed
  }
  func type(_ controller:MeltypeInputController,_ client:EventClient,_ raw:String) {
   for scalar in raw.unicodeScalars { _=key(controller,client,String(scalar),code:UInt16(kVK_ANSI_A)) }
  }
  func equal(_ actual:String,_ expected:String) {
   precondition(Array(actual.unicodeScalars)==Array(expected.unicodeScalars),"expected \(expected.debugDescription), got \(actual.debugDescription)")
  }
  check("secure event input and focused field hints") { _,_ in
   let safari = "com.apple.Safari"
   let appKit = "local.meltype.event-client"
   let protects = MeltypeInputController.protectsInput
   precondition(protects(safari, nil, true, false), "Safari's secure input works without AX")
   precondition(!protects(safari, false, true, false), "AX regular field overrides the fallback")
   precondition(!protects(safari, nil, false, false), "another process's secure input does not bypass Safari")
   precondition(!protects(appKit, nil, true, false), "secure input alone does not identify an AppKit field")
   precondition(protects(appKit, true, false, false), "AX identifies a secure AppKit field")
   precondition(protects(appKit, nil, false, true), "ABC secure input bypasses a stale controller")
   precondition(!protects(appKit, false, false, false), "regular input resumes after focus changes")
  }
  check("bracket commit consumes Return with caret movement enabled") { controller,client in
   precondition(key(controller,client,"[",code:UInt16(kVK_ANSI_LeftBracket)))
   precondition(key(controller,client,"]",code:UInt16(kVK_ANSI_RightBracket)))
   precondition(key(controller,client,"\r",code:UInt16(kVK_Return)))
   equal(client.document,"「」")
   equal(client.marked,"")
  }
  for prefix in ["", "前の行\n"] {
   check("snippet after \(prefix.debugDescription)") { controller,client in
    client.document = prefix
    type(controller,client,";tes")
    equal(client.document,prefix + ";tes")
    equal(client.marked,"")
    precondition(key(controller,client," ",code:UInt16(kVK_Space)))
    equal(client.document,prefix + "テスト")
   }
  }
  check("unreadable snippet after newline") { controller,client in
   client.document = "前の行\n"
   client.canReadSubstring = false
   precondition(key(controller,client,";",code:UInt16(kVK_ANSI_Semicolon)))
   equal(client.document,"前の行\n")
   precondition(!client.marked.isEmpty)
   controller.commitComposition(client)
  }
  for (authApp, normalApp, firstKey, first, rest) in [
   ("com.apple.loginwindow", "local.meltype.event-client", kVK_ANSI_S, "s", "a12"),
   ("com.apple.SecurityAgent", "com.apple.finder", kVK_ANSI_A, "a", "iueo"),
  ] {
   check("\(authApp) passes the first key and Return") { controller,client in
    client.appID = authApp
    precondition(!key(controller,client,first,code:UInt16(firstKey)), "auth client must receive the first key")
    type(controller,client,rest)
    precondition(!key(controller,client,"\r",code:UInt16(kVK_Return)))
    equal(client.document,first + rest)
    equal(client.marked,"")
    precondition(client.contextReads == 0, "auth context must not be read")
   }
   for commitFirst in [false, true] {
    check("\(authApp) cancels pending input, commit first \(commitFirst)") { controller,client in
     client.appID = normalApp
     type(controller,client,"ka")
     equal(client.marked,"か")
     client.appID = authApp
     client.contextReads = 0
     if commitFirst { controller.commitComposition(client) }
     precondition(!key(controller,client,"a",code:UInt16(kVK_ANSI_A)))
     controller.commitComposition(client)
     equal(client.document,"a")
     equal(client.marked,"")
     precondition(client.contextReads == 0, "auth context must not be read")
     client.appID = normalApp
     type(controller,client,"ka")
     equal(client.marked,"か")
     controller.commitComposition(client)
     // このテストでは SpaceAroundEnglish が有効。
     equal(client.document,"a か")
     equal(client.marked,"")
    }
   }
  }
  for (raw,event,expected) in [("e","\u{0301}\u{0300}","e\u{0301}\u{0300}"),("ka","\u{3099}\u{FE0F}","か\u{3099}\u{FE0F}"),("ka","\u{E0100}\u{E0101}","か\u{E0100}\u{E0101}"),("ka","😀","か😀"),("ka","👩‍👩‍👧‍👦","か👩‍👩‍👧‍👦"),("ka","e\u{0301}","かe\u{0301}")] {
   check("event \(event.debugDescription)") { controller,client in
    type(controller,client,raw)
    precondition(!key(controller,client,event),"external event must pass through")
    equal(client.document,expected); equal(client.marked,"")
   }
  }
  for action in ["Enter","Space","Tab","focus"] {
   check("protected \(action)") { controller,client in
    type(controller,client,"@kuraido")
    switch action {
    case "Enter": _=key(controller,client,"\r",code:UInt16(kVK_Return))
    case "Space": _=key(controller,client," ",code:UInt16(kVK_Space))
    case "Tab": _=key(controller,client,"\t",code:UInt16(kVK_Tab))
    default: controller.commitComposition(client)
    }
    equal(client.document,"@kuraido"+(action=="Space" ? " " : ""));equal(client.marked,"")
   }
  }
  for action in ["Enter","Space","Tab","focus"] {
   check("mixed protected \(action)") { controller,client in
    type(controller,client,"kyouha「@kuraido」ashita")
    switch action {
    case "Enter": _=key(controller,client,"\r",code:UInt16(kVK_Return))
    case "Space": _=key(controller,client," ",code:UInt16(kVK_Space));_=key(controller,client,"\r",code:UInt16(kVK_Return))
    case "Tab": _=key(controller,client,"\t",code:UInt16(kVK_Tab))
    default: controller.commitComposition(client)
    }
    equal(client.document,"きょうは「@kuraido」あした")
    equal(client.marked,"")
   }
  }
  let operationKeys: [(Int, Int)] = [
   (kVK_ForwardDelete, NSDeleteFunctionKey), (kVK_ANSI_KeypadClear, NSClearLineFunctionKey),
   (kVK_F1, NSF1FunctionKey), (kVK_F2, NSF2FunctionKey), (kVK_F3, NSF3FunctionKey),
   (kVK_F4, NSF4FunctionKey), (kVK_F5, NSF5FunctionKey), (kVK_F11, NSF11FunctionKey),
   (kVK_F12, NSF12FunctionKey), (kVK_F13, NSF13FunctionKey), (kVK_F14, NSF14FunctionKey),
   (kVK_F15, NSF15FunctionKey), (kVK_F16, NSF16FunctionKey), (kVK_F17, NSF17FunctionKey),
   (kVK_F18, NSF18FunctionKey), (kVK_F19, NSF19FunctionKey), (kVK_F20, NSF20FunctionKey),
  ]
  for (code, scalar) in operationKeys {
   for converting in [false, true] {
    check("operation key \(code), converting \(converting)") { controller,client in
     type(controller,client,"a")
     if converting { _=key(controller,client," ",code:UInt16(kVK_Space)) }
     let text=String(UnicodeScalar(scalar)!)
     let event=NSEvent.keyEvent(with:.keyDown,location:.zero,modifierFlags:[],timestamp:0,windowNumber:0,context:nil,characters:text,charactersIgnoringModifiers:text,isARepeat:false,keyCode:UInt16(code))!
     precondition(!controller.handle(event,client:client),"operation key must pass through")
     equal(client.document,"あ");equal(client.marked,"")
     precondition(!controller.handle(event,client:client),"empty composition must pass through")
     equal(client.document,"あ")
    }
   }
  }
  check("Backspace at protected boundary") { controller,client in
   type(controller,client,"./z]");_=key(controller,client,"",code:UInt16(kVK_Delete));equal(client.marked,"./z")
   _=key(controller,client,"\r",code:UInt16(kVK_Return));equal(client.document,"./z")
  }
  check("F6 override before marks") { controller,client in
   type(controller,client,"e");_=key(controller,client,"",code:UInt16(kVK_F6));_=key(controller,client,"\u{0301}\u{0300}");equal(client.document,"え\u{0301}\u{0300}")
  }
  check("F9 override") { controller,client in
   type(controller,client,"@kuraido");_=key(controller,client,"",code:UInt16(kVK_F9));_=key(controller,client,"\r",code:UInt16(kVK_Return));equal(client.document,"＠ｋｕｒａｉｄｏ")
  }
  check("JIS direct and kana modes") { controller,client in
   _=key(controller,client,"",code:UInt16(kVK_JIS_Eisu));type(controller,client,"ka");equal(client.document,"ka")
   _=key(controller,client,"",code:UInt16(kVK_JIS_Kana));type(controller,client,"ka");_=key(controller,client,"",code:UInt16(kVK_F6));_=key(controller,client,"\r",code:UInt16(kVK_Return));equal(client.document,"ka か")
  }
  check("Command shortcut commits once") { controller,client in
   type(controller,client,"@kuraido");precondition(!key(controller,client,"a",flags:.command));equal(client.document,"@kuraido");equal(client.marked,"")
  }
  func compositionPanel(showing guide: String) -> (NSPanel, NSTextField, NSScrollView) {
   for case let panel as NSPanel in app.windows where panel.isVisible {
    let views = panel.contentView?.subviews ?? []
    if let label = views.compactMap({ $0 as? NSTextField }).first(where: { !$0.isHidden && $0.stringValue == guide }),
       let scroll = views.compactMap({ $0 as? NSScrollView }).first {
     return (panel, label, scroll)
    }
   }
   fatalError("Missing composition panel with \(guide)")
  }
  func candidateTable() -> NSTableView {
   for window in app.windows where window.isVisible {
    for case let scroll as NSScrollView in window.contentView?.subviews ?? [] where !scroll.isHidden {
     if let table = scroll.documentView as? NSTableView { return table }
    }
   }
   fatalError("Missing candidate table")
  }
  func candidateCell(_ table: NSTableView, row: Int) -> NSTableCellView {
   guard let cell = table.view(atColumn: 0, row: row, makeIfNecessary: true) as? NSTableCellView else {
    fatalError("Missing candidate cell \(row)")
   }
   return cell
  }
  check("Spelling suggestion returns to the same panel after Escape") { controller, client in
   let visible = NSScreen.main!.visibleFrame
   client.lineRect = NSRect(x: visible.midX, y: visible.minY + 9, width: 1, height: 20)
   let guide = "buresureddo　もしかして: ブレスレット　<Tab>で修正"
   type(controller, client, "buresureddo")
   let (panel, _, scroll) = compositionPanel(showing: guide)
   precondition(scroll.isHidden && panel.frame.minY > client.lineRect.maxY)
   precondition(key(controller, client, " ", code: UInt16(kVK_Space)))
   precondition(!scroll.isHidden, "conversion should show candidates in the same panel")
   precondition(key(controller, client, "\u{1b}", code: UInt16(kVK_Escape)))
   equal(client.marked, "ぶれすれっど")
   precondition(compositionPanel(showing: guide).0 === panel && scroll.isHidden)
   controller.commitComposition(client)
   precondition(!panel.isVisible)
  }
  check("Candidates stay visible when the client has no caret position") { controller, client in
   client.lineRect = .zero
   type(controller, client, "peejitesuto")
   precondition(key(controller, client, " ", code: UInt16(kVK_Space)))
   let table = candidateTable()
   let window = table.window!
   let screen = NSScreen.screens.first { NSMouseInRect(NSEvent.mouseLocation, $0.frame, false) }!
   precondition(table.numberOfRows == 9 && screen.visibleFrame.contains(window.frame))
   // ウィンドウの位置は整数に丸められるので、1 ポイントまでのずれは許す。
   precondition(abs(window.frame.midX - screen.visibleFrame.midX) <= 1, "\(window.frame) \(screen.visibleFrame)")
   controller.commitComposition(client)
   precondition(!window.isVisible)
  }
  check("Predictive candidates leave the guide next to the same input line") { controller, client in
   let visible = NSScreen.main!.visibleFrame
   client.lineRect = NSRect(x: visible.midX, y: visible.minY + 9, width: 1, height: 20)
   client.fontSize = 9
   type(controller, client, "ko")
   let (panel, guide, scroll) = compositionPanel(showing: "ko")
   let guideY = panel.frame.minY + guide.frame.minY
   let guideHeight = guide.frame.height
   precondition((guide.attributedStringValue.attribute(.font, at: 0, effectiveRange: nil) as? NSFont)?.pointSize == 12)
   precondition(scroll.isHidden)
   client.fontSize = 36
   type(controller, client, "u")
   precondition(!scroll.isHidden, "the user dictionary should supply a prediction")
   let prediction = candidateCell(candidateTable(), row: 0)
   precondition(prediction.subviews == [prediction.textField!], "predictions do not have number labels")
   precondition(panel.frame.minY + guide.frame.minY == guideY)
   precondition(guide.frame.height == guideHeight && !scroll.frame.intersects(guide.frame))
   type(controller, client, "ho")
   precondition(compositionPanel(showing: "kouho").0 === panel && scroll.isHidden)
   precondition(panel.frame.minY + guide.frame.minY == guideY)
   controller.commitComposition(client)
  }
  check("The guide expands with typed text and stays inside the right edge") { _, _ in
   let visible = NSScreen.main!.visibleFrame
   let panel = CompositionPanel(choose: { _ in }, highlight: { _ in })
   for x in [visible.midX, visible.maxX - 30] {
    let line = NSRect(x: x, y: visible.midY, width: 1, height: 20)
    let placement = CompositionPanelPlacement(start: line, end: line, visible: visible)!
    var previous: NSRect?
    for text in ["abcdef", "abcdefg", "abcdefgh", "abcdefghijklmnop"] {
     panel.show(guide: text, list: nil, converting: false, clause: 0, placement: placement, aboveWindowLevel: 0)
     let (window, guide, _) = compositionPanel(showing: text)
     let frame = window.frame
     precondition(guide.frame.width >= guide.cell!.cellSize.width, "the guide must display the complete line")
     precondition(guide.frame.height == guide.cell!.cellSize.height)
     precondition(visible.contains(frame))
     precondition(x == visible.midX ? frame.minX == x : frame.maxX == visible.maxX)
     if let previous {
      precondition(frame.width > previous.width)
      if x != visible.midX { precondition(frame.minX < previous.minX) }
     }
     previous = frame
    }
   }
   panel.hide()
  }
  check("A long guide stays on one line and keeps the candidate rows") { _, _ in
   let visible = NSScreen.main!.visibleFrame
   let line = NSRect(x: visible.midX, y: visible.minY + 9, width: 1, height: 20)
   let placement = CompositionPanelPlacement(start: line, end: line, visible: visible)!
   let items = (1...15).map { "候補\($0)" }
   let list = CandidateList(items: items, notes: [], numbered: true, selected: 0, selectedMeaning: nil, meaningDelayMs: 0)
   let panel = CompositionPanel(choose: { _ in }, highlight: { _ in })
   panel.show(guide: "短い案内", list: list, converting: true, clause: 0, placement: placement, aboveWindowLevel: 0)
   let lineHeight = compositionPanel(showing: "短い案内").1.frame.height
   let long = String(repeating: "とても長い打ったキーと操作案内", count: 20)
   panel.show(guide: long, list: list, converting: true, clause: 0, placement: placement, aboveWindowLevel: 0)
   let (window, guide, scroll) = compositionPanel(showing: long)
   precondition(window.frame.width <= 520, "\(window.frame)")
   precondition(guide.frame.height == lineHeight, "the guide must not wrap")
   precondition(!scroll.isHidden && candidateTable().numberOfRows == 9)
   precondition(scroll.frame.height == 9 * 32)
   panel.hide()
  }
  check("The conversion panel fits nine rows and aligns notes on the right") { _, _ in
   let visible = NSScreen.main!.visibleFrame
   let line = NSRect(x: visible.midX, y: visible.minY + 9, width: 1, height: 20)
   let placement = CompositionPanelPlacement(start: line, end: line, visible: visible)!
   let items = (1...15).map { "候補\($0)" }
   let notes: [String?] = items.indices.map { $0 == 0 ? "注記" : "長めの注記" }
   let list = CandidateList(items: items, notes: notes, numbered: true, selected: 0,
                            selectedMeaning: nil, meaningDelayMs: 0)
   var highlighted: Int?
   let panel = CompositionPanel(choose: { _ in }, highlight: { highlighted = $0 })
   panel.show(guide: "操作案内", list: list, converting: true, clause: 0, placement: placement, aboveWindowLevel: 0)
   let (window, _, scroll) = compositionPanel(showing: "操作案内")
   let table = candidateTable()
   precondition(table.numberOfRows == 9)
   precondition(table.visibleRect.maxY == table.rect(ofRow: 8).maxY)
   let firstCell = candidateCell(table, row: 0)
   let title = firstCell.textField!.attributedStringValue
   precondition((title.attribute(.font, at: 0, effectiveRange: nil) as? NSFont) == NSFont.systemFont(ofSize: 15))
   var rightEdges: [CGFloat] = []
   for (index, note) in [(0, "注記"), (1, "長めの注記")] {
    let cell = candidateCell(table, row: index)
    cell.layoutSubtreeIfNeeded()
    let labels = cell.subviews.compactMap { $0 as? NSTextField }
    let detail = labels.first { $0.stringValue == note }!
    precondition(detail.alignment == .right)
    precondition(cell.textField!.frame.maxX < detail.frame.minX)
    rightEdges.append(detail.frame.maxX)
   }
   precondition(rightEdges[0] == rightEdges[1])
   let wheel = CGEvent(scrollWheelEvent2Source: nil, units: .line, wheelCount: 1, wheel1: -1, wheel2: 0, wheel3: 0)!
   scroll.scrollWheel(with: NSEvent(cgEvent: wheel)!)
   precondition(highlighted == 9, "scrolling past the page end should highlight the next page")
   let unnumbered = CandidateList(items: items, notes: [], numbered: false, selected: 9, selectedMeaning: nil, meaningDelayMs: 0)
   let firstHeight = window.frame.height
   panel.show(guide: "操作案内", list: unnumbered, converting: true, clause: 0, placement: placement, aboveWindowLevel: 0)
   precondition(table.numberOfRows == 6 && window.frame.height == firstHeight)
   let unnumberedCell = candidateCell(table, row: 0)
   precondition(unnumberedCell.subviews == [unnumberedCell.textField!])
   equal(unnumberedCell.textField!.stringValue, "候補10")
   panel.hide()
  }
  check("Candidate selection stays reachable in a short panel") { _, _ in
   let screen = NSScreen.main!.visibleFrame
   let visible = NSRect(x: screen.minX, y: screen.minY, width: 380, height: 200)
   let line = NSRect(x: visible.midX, y: visible.midY - 12, width: 1, height: 24)
   let placement = CompositionPanelPlacement(start: line, end: line, visible: visible)!
   var items = (0..<14).map { "候補\($0)" }
   items[9] = String(repeating: "長い候補", count: 30)
   let notes: [String?] = items.map { _ in "注記" }
   var clicked: Int?
   let panel = CompositionPanel(choose: { clicked = $0 }, highlight: { _ in })
   let list = CandidateList(items: items, notes: notes, numbered: true, selected: 11, selectedMeaning: "意味", meaningDelayMs: 20)
   panel.show(guide: "操作案内", list: list, converting: true, clause: 0, placement: placement, aboveWindowLevel: 0)
   let (window, guide, scroll) = compositionPanel(showing: "操作案内")
   let table = scroll.documentView as! NSTableView
   precondition(!window.canBecomeKey && visible.contains(window.frame))
   precondition(!guide.frame.intersects(scroll.frame))
   precondition(table.numberOfRows == 5 && table.selectedRow == 2)
   precondition(table.visibleRect.intersects(table.rect(ofRow: 2)))
   precondition(table.rect(ofRow: 0).height > table.rect(ofRow: 1).height)
   let row = table.rowView(atRow: 2, makeIfNecessary: true)
   precondition(row?.accessibilityPerformPress() == true && clicked == list.selected)
   let cell = candidateCell(table, row: 2)
   precondition(cell.textField?.stringValue == "候補11" && cell.textField?.alignment == .left)
   precondition(cell.subviews.compactMap { ($0 as? NSTextField)?.stringValue } == ["3", "候補11", "注記"])
   let detail = window.contentView?.subviews.last as! NSTextField
   precondition(detail.isHidden, "the meaning should wait for the delay")
   // 高さが足りないときは、案内と候補一覧を優先する。
   precondition(!scroll.isHidden && detail.isHidden)
   let roomy = CompositionPanelPlacement(start: line, end: line, visible: screen)!
   panel.show(guide: "操作案内", list: list, converting: true, clause: 0, placement: roomy, aboveWindowLevel: 0)
   let deadline = Date().addingTimeInterval(2)
   while detail.isHidden, Date() < deadline {
    RunLoop.main.run(until: min(deadline, Date().addingTimeInterval(0.05)))
   }
   precondition(!detail.isHidden && detail.stringValue == "意味",
                "meaning is hidden=\(detail.isHidden), value=\(detail.stringValue), available=\(roomy.availableHeight)")
   precondition(!detail.frame.intersects(guide.frame) && !detail.frame.intersects(scroll.frame))
   panel.show(guide: "操作案内", list: list, converting: true, clause: 0, placement: placement, aboveWindowLevel: 0)
   precondition(!scroll.isHidden && detail.isHidden)
   panel.hide()
  }
  for digit in [kVK_ANSI_2, kVK_ANSI_Keypad2] {
   check("Page keys and number key \(digit) follow the same Core page") { controller, client in
    let visible = NSScreen.main!.visibleFrame
    client.lineRect = NSRect(x: visible.midX, y: visible.minY + 9, width: 1, height: 20)
    type(controller, client, "peejitesuto")
    precondition(key(controller, client, " ", code: UInt16(kVK_Space)))
    let table = candidateTable()
    @MainActor func pageKey(_ code: Int, _ scalar: Int) {
     precondition(key(controller, client, String(UnicodeScalar(scalar)!), code: UInt16(code)))
     precondition(client.document.isEmpty)
     precondition(table.selectedRow == 0)
     let first = candidateCell(table, row: 0)
     equal(client.marked, first.textField!.stringValue)
     precondition((first.subviews.first as? NSTextField)?.stringValue == "1")
    }
    let first = client.marked
    precondition(table.numberOfRows == 9)
    pageKey(kVK_PageUp, NSPageUpFunctionKey)
    precondition(client.marked != first, "Page Up from the first page should wrap")
    pageKey(kVK_PageDown, NSPageDownFunctionKey)
    equal(client.marked, first)
    pageKey(kVK_PageDown, NSPageDownFunctionKey)
    precondition(client.marked != first, "Page Down should open the second page")
    let secondPage = client.marked
    let up = String(UnicodeScalar(NSUpArrowFunctionKey)!)
    let down = String(UnicodeScalar(NSDownArrowFunctionKey)!)
    precondition(key(controller, client, up, code: UInt16(kVK_UpArrow)))
    precondition(table.selectedRow == 8)
    let previousPageLast = candidateCell(table, row: 8)
    equal(client.marked, previousPageLast.textField!.stringValue)
    precondition((previousPageLast.subviews.first as? NSTextField)?.stringValue == "9")
    precondition(key(controller, client, down, code: UInt16(kVK_DownArrow)))
    precondition(table.selectedRow == 0)
    equal(client.marked, secondPage)
    let secondRow = candidateCell(table, row: 1)
    precondition((secondRow.subviews.first as? NSTextField)?.stringValue == "2")
    let expected = secondRow.textField!.stringValue
    precondition(key(controller, client, "2", code: UInt16(digit)))
    equal(client.document, expected)
    equal(client.marked, "")
   }
  }
  print("\(passed) Mac event/integration cases passed (synthetic client; OS IME registration and app GUI NOT_RUN)")
 }
}
