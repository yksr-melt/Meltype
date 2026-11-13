; SPDX-License-Identifier: GPL-3.0-or-later
; Copyright (C) 2026 Yukishiro
;
; Meltype の Windows インストーラー (Inno Setup 6)。Build-Package.ps1 が dist\Meltype\app から作る。管理者権限は不要。
; 新規インストール・アプリ内の自動更新 (/VERYSILENT)・winget・Install-Meltype.ps1 のどれもこのインストーラーで入れる。
;   ISCC.exe /DAppVersion=1.2.2-pre.2 /DSourceDir=..\dist\Meltype /DOutputDir=..\dist packaging\Meltype.iss
; → dist\Meltype-1.2.2-pre.2-setup.exe

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\Meltype"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif
#define HasMeltypeIme FileExists(SourceDir + "\app\meltype-ime.ps1") && DirExists(SourceDir + "\app\tip")

[Setup]
; 「設定 → アプリ」の項目 (HKCU\...\Uninstall\Meltype_is1)
AppId=Meltype
AppName=Meltype
AppVersion={#AppVersion}
AppVerName=Meltype {#AppVersion}
AppPublisher=Yukishiro
AppPublisherURL=https://github.com/yksr-melt/Meltype
AppSupportURL=https://github.com/yksr-melt/Meltype/issues
; 管理者権限は使わない (現在のユーザーだけに入れる)
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Meltype
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
UninstallDisplayIcon={app}\Meltype.exe
UninstallDisplayName=Meltype
OutputDir={#OutputDir}
OutputBaseFilename=Meltype-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 同梱の .NET ランタイムは x64 (ARM64 の Windows 11 でも x64 のエミュレーションで動く)
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile={#SourceDir}\LICENSE.txt
SetupLogging=yes
; 動いている Meltype は [Code] で止める (再起動の確認を出さない)
CloseApplications=no
RestartApplications=no

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
#if HasMeltypeIme
; 登録済みなら聞かずに更新する (PrepareToInstall)
Name: "meltypeime"; Description: "Meltype IME を登録する (管理者権限が必要)"; GroupDescription: "入力方式:"; Check: not IsMeltypeImeRegistered
#endif
Name: "startup"; Description: "Windows の起動時に Meltype を起動する"; GroupDescription: "その他:"

[Files]
#if HasMeltypeIme
; PrepareToInstall で ExtractTemporaryFiles する。SolidCompression なので、前にあるファイルを全部展開しなくて済むよう先頭に置く
Source: "{#SourceDir}\app\tip\*"; DestDir: "{app}\tip"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\app\meltype-ime.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "setup-ime.ps1"; DestDir: "{app}"; Flags: ignoreversion
#endif
Source: "{#SourceDir}\app\*"; Excludes: "\tip,\meltype-ime.ps1"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; 1.5.1 以前の zip 版 (Install.cmd) が置いたアンインストーラー
Type: files; Name: "{app}\uninstall.ps1"

[Icons]
Name: "{userprograms}\Meltype"; Filename: "{app}\Meltype.exe"; WorkingDir: "{app}"; Comment: "Meltype: 日本語と英語を自動で打ち分ける"
Name: "{userstartup}\Meltype"; Filename: "{app}\Meltype.exe"; WorkingDir: "{app}"; Comment: "Meltype: 日本語と英語を自動で打ち分ける"; Tasks: startup

[Run]
; 管理者として実行していても、ふつうの権限で起動する。サイレント実行 (自動更新・winget) でも起動する
Filename: "{app}\Meltype.exe"; Description: "Meltype を起動する"; Flags: nowait postinstall runasoriginaluser

[UninstallRun]
Filename: "{app}\Meltype.exe"; Parameters: "--exit"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "ExitMeltype"
; サインインしたらすぐ起動するタスク (Meltype が作る: #283)
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN Meltype /F"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteStartupTask"

[UninstallDelete]
; 設定と学習データ ({localappdata}\Meltype) は、アンインストールの最後に聞いてから消す (CurUninstallStepChanged)
Type: filesandordirs; Name: "{app}"

[Code]
function IsMeltypeImeRegistered(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\Microsoft\CTF\TIP\{417D801B-A9BD-4C26-BD16-356A825A6998}');
end;

function PowerShell(): String;
begin
  Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

// 動いている Meltype (と Mozc のヘルパー) を止める。管理者として動いている Meltype も止まるよう、まず --exit で終了の合図を送る。
procedure StopMeltype();
var
  ResultCode: Integer;
  Installed: String;
begin
  Installed := ExpandConstant('{localappdata}\Programs\Meltype\Meltype.exe');
  if FileExists(Installed) then
    Exec(Installed, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM Meltype.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM meltype_mozc_helper.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(300);
end;

#if HasMeltypeIme
// これから入れる Meltype IME を登録する (登録済みで同じ DLL なら何もしない)。
// IME の DLL は PC 全体の Program Files にあり、本体はユーザーの LOCALAPPDATA にある。本体だけ新しくすると、Meltype.exe が
// 別ビルドの impl と通信できず Meltype キーボードで動く (TipBuild) ので、IME を先に登録し、登録できなければ本体を入れ替えない。
function RegisterMeltypeIme(): Boolean;
var
  ResultCode: Integer;
  Parameters: String;
begin
  ExtractTemporaryFiles('{app}\tip\Register-Tip.ps1');
  ExtractTemporaryFiles('{app}\tip\x64\*');
  ExtractTemporaryFiles('{app}\tip\x86\*');
  ExtractTemporaryFiles('{app}\meltype-ime.ps1');
  ExtractTemporaryFiles('{app}\setup-ime.ps1');
  // ExtractTemporaryFiles は {tmp} の下の「{app}」という名前のフォルダーに展開するので、{app} は展開しない
  Parameters := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\') + '{app}\setup-ime.ps1" -Source "' + ExpandConstant('{tmp}\') + '{app}\tip"';
  // ウィザードを出さないとき (自動更新・winget) は、UAC の前に Meltype からの確認だと分かる説明を出す (#970)
  if WizardSilent() then Parameters := Parameters + ' -Explain';
  // 言語の一覧は元のユーザーの設定なので、元のユーザーの権限で動かす (PC 全体への登録だけ setup-ime.ps1 が昇格する)
  Result := ExecAsOriginalUser(PowerShell(), Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;
#endif

// Meltype IME の登録を断られた・失敗したときに本体を入れるかは、実行する前から登録されていたかで決める。
//   登録済み: 本体も入れない (本体だけ入れると、Meltype.exe が別ビルドの impl と通信できず Meltype キーボードで動く)
//   未登録: 本体は入れる (Meltype IME が無いので版はずれず、Meltype キーボードで動く)
function PrepareToInstall(var NeedsRestart: Boolean): String;
#if HasMeltypeIme
var
  ResultCode: Integer;
  Installed: String;
#endif
begin
  Result := '';
#if HasMeltypeIme
  if IsMeltypeImeRegistered() then
  begin
    if not RegisterMeltypeIme() then
    begin
      // 自動更新では、前の版の Meltype はもう終了している。前の版のまま使えるように起動し直す
      Installed := ExpandConstant('{app}\Meltype.exe');
      if FileExists(Installed) then
        ExecAsOriginalUser(Installed, '', ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ResultCode);
      Result := 'Meltype IME を更新できなかったので、Meltype を更新しませんでした。前の版のまま使えます。' + #13#10 +
        '記録: ' + ExpandConstant('{%TEMP}\Meltype-setup-ime.log');
      exit;
    end;
  end
  // 登録を断っても、Meltype キーボードで使える。あとからトレイの「Meltype IME」で登録できる
  else if WizardIsTaskSelected('meltypeime') then
    RegisterMeltypeIme();
#endif
  StopMeltype();
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // 1.5.1 以前の zip 版 (Install.cmd) の「設定 → アプリ」の項目は消す (このインストーラーの項目と二重にならないように)
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Meltype');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
#if HasMeltypeIme
  ResultCode: Integer;
#endif
begin
  if CurUninstallStep = usUninstall then
  begin
    StopMeltype();
#if HasMeltypeIme
    // IME の登録を外す ({app} を消す前に)。PC 全体の登録を外すのは setup-ime.ps1 が昇格する。
    // サイレント時には UAC を出さないので外さない。
    if not UninstallSilent() then
      Exec(PowerShell(), '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\setup-ime.ps1') + '" -Uninstall', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
#endif
  end;
  // 設定と学習データは入れ直したときにも使えるよう、聞いてから消す (既定は残す)。画面の無いアンインストールでは残す。
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent() then
  begin
    Data := ExpandConstant('{localappdata}\Meltype');
    if DirExists(Data) and (MsgBox('設定と学習データ (' + Data + ') も削除しますか?' + #13#10 + #13#10 + '「いいえ」なら残します。もう一度インストールしたときに、そのまま使えます。', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(Data, True, True, True);
  end;
end;
