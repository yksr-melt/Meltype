// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 0924haruto12

import Cocoa
import InputMethodKit
import Carbon.HIToolbox
final class EventClient: NSObject, IMKTextInput {
 var document=""; var marked=""
 func text(_ value:Any?)->String { (value as? NSAttributedString)?.string ?? (value as? String ?? "") }
 func insertText(_ string: Any!, replacementRange: NSRange) { let value=text(string); if replacementRange.location != NSNotFound { document=(document as NSString).replacingCharacters(in:replacementRange,with:value) } else {document+=value}; marked="" }
 func setMarkedText(_ string: Any!, selectionRange: NSRange, replacementRange: NSRange) {marked=text(string)}
 func selectedRange()->NSRange {NSRange(location:(document as NSString).length,length:0)}
 func markedRange()->NSRange {marked.isEmpty ? NSRange(location:NSNotFound,length:0):NSRange(location:(document as NSString).length,length:(marked as NSString).length)}
 func attributedSubstring(from range:NSRange)->NSAttributedString! { let text=document as NSString; guard range.location != NSNotFound && range.location<=text.length else{return nil}; return NSAttributedString(string:text.substring(with:NSRange(location:range.location,length:min(range.length,text.length-range.location)))) }
 func length()->Int {(document as NSString).length}
 func characterIndex(for point:NSPoint, tracking mode:IMKLocationToOffsetMappingMode, inMarkedRange:UnsafeMutablePointer<ObjCBool>!)->Int {inMarkedRange?.pointee=false; return 0}
 func attributes(forCharacterIndex index:Int, lineHeightRectangle rectangle:UnsafeMutablePointer<NSRect>!)->[AnyHashable:Any]! {rectangle?.pointee=NSRect(x:0,y:0,width:1,height:20); return [:]}
 func validAttributesForMarkedText()->[Any]! {[]}
 func overrideKeyboard(withKeyboardNamed name:String!) {}
 func selectMode(_ identifier:String!) {}
 func supportsUnicode()->Bool {true}
 func bundleIdentifier()->String! {"local.meltype.event-client"}
 func windowLevel()->Int32 {0}
 func supportsProperty(_ property:TSMDocumentPropertyTag)->Bool {true}
 func uniqueClientIdentifierString()->String! {"synthetic-event-client"}
 func string(from range:NSRange, actualRange:UnsafeMutablePointer<NSRange>!)->String! {actualRange?.pointee=range;return attributedSubstring(from:range)?.string}
 func firstRect(forCharacterRange range:NSRange, actualRange:UnsafeMutablePointer<NSRange>!)->NSRect {actualRange?.pointee=range;return .zero}
}
#if !REAL_CONVERTER
final class MeltypeConverter {
 static let shared=MeltypeConverter()
 func clauses(for text:String,context:String?)->[(reading:String,text:String)] {[]}
 func candidates(for text:String)->[String] {[]}
}
#endif
var candidatesWindow: IMKCandidates? = nil
@main
struct EventTests {
 static func main() {
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
    #if REAL_CONVERTER
    precondition(client.document.contains("「@kuraido」"),"protected token changed")
    precondition(!client.document.contains("kyouha") && !client.document.contains("ashita"),"Japanese gaps skipped")
    #else
    equal(client.document,"きょうは「@kuraido」あした")
    #endif
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
  #if REAL_CONVERTER
  let converter=MeltypeConverter.shared
  let candidates=converter.candidates(for:"きょう")
  precondition(candidates.contains("今日"),"real dictionary conversion missing: \(candidates)")
  let clauses=converter.clauses(for:"きょうはいいてんき",context:nil)
  precondition(clauses.map(\.reading).joined()=="きょうはいいてんき","real clause readings lost")
  precondition(clauses.map(\.text).joined().contains("今日"),"real clause conversion missing")
  guard let directory=NativeCore.shared.dataDirectory else {fatalError("missing data directory")}
  precondition(FileManager.default.fileExists(atPath:directory+"/azooKey"),"converter ignored isolated data directory")
  passed+=3
  print("PASS: actual azooKey candidates, clause reading coverage, isolated data directory")
  #endif
  print("\(passed) Mac event/integration cases passed (synthetic client; OS IME registration and app GUI NOT_RUN)")
 }
}
