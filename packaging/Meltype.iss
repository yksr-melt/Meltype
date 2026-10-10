; SPDX-License-Identifier: GPL-3.0-or-later
; Copyright (C) 2026 Yukishiro
;
; Meltype の Windows インストーラー (Inno Setup 6)。Build-Package.ps1 で作った dist\Meltype\app を、
; zip の Install.cmd (install.ps1) と同じ場所 (%LOCALAPPDATA%\Programs\Meltype) に入れる。管理者権限は不要。
;   ISCC.exe /DAppVersion=1.1.1 /DSourceDir=..\dist\Meltype /DOutputDir=..\dist packaging\Meltype.iss
; → dist\Meltype-1.1.1-setup.exe

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
; install.ps1 と同じ場所。管理者権限は使わない (現在のユーザーだけに入れる)
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
Name: "meltypeime"; Description: "Meltype IME を登録する (管理者権限が必要)"; GroupDescription: "入力方式:"
#endif
Name: "startup"; Description: "Windows の起動時に Meltype を起動する"; GroupDescription: "その他:"

[Files]
Source: "{#SourceDir}\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
#if HasMeltypeIme
Source: "setup-ime.ps1"; DestDir: "{app}"; Flags: ignoreversion
#endif

[Icons]
Name: "{userprograms}\Meltype"; Filename: "{app}\Meltype.exe"; WorkingDir: "{app}"; Comment: "Meltype: 日本語と英語を自動で打ち分ける"
Name: "{userstartup}\Meltype"; Filename: "{app}\Meltype.exe"; WorkingDir: "{app}"; Comment: "Meltype: 日本語と英語を自動で打ち分ける"; Tasks: startup

[Run]
#if HasMeltypeIme
; IME は本体を起動する前に登録する。ユーザーの言語一覧は元のユーザーの権限で更新し、
; PC 全体への登録だけ既存のスクリプトで昇格する。サイレント時には UAC を出さない。
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\setup-ime.ps1"" {code:ImeAskParameter}"; StatusMsg: "Meltype IME を登録しています..."; Flags: runhidden waituntilterminated runasoriginaluser skipifsilent
#endif
; 管理者として実行していても、ふつうの権限で起動する (install.ps1 と同じ)
Filename: "{app}\Meltype.exe"; Description: "Meltype を起動する"; Flags: nowait postinstall runasoriginaluser

[UninstallRun]
Filename: "{app}\Meltype.exe"; Parameters: "--exit"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "ExitMeltype"
; サインインしたらすぐ起動するタスク (Meltype が作る: #283)
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN Meltype /F"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteStartupTask"

[UninstallDelete]
; 設定と学習データ ({localappdata}\Meltype) は、アンインストールの最後に聞いてから消す (CurUninstallStepChanged)
Type: filesandordirs; Name: "{app}"

[Code]
#if HasMeltypeIme
function ImeAskParameter(Param: String): String;
begin
  Result := '';
  if WizardIsTaskSelected('meltypeime') then Result := '-Ask';
end;
#endif
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

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopMeltype();
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // zip の Install.cmd で入れていたときの「設定 → アプリ」の項目は消す (この インストーラーの項目と二重にならないように)
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Meltype');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
begin
  if CurUninstallStep = usUninstall then StopMeltype();
  // 設定と学習データは入れ直したときにも使えるよう、聞いてから消す (既定は残す)。画面の無いアンインストールでは残す。
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent() then
  begin
    Data := ExpandConstant('{localappdata}\Meltype');
    if DirExists(Data) and (MsgBox('設定と学習データ (' + Data + ') も削除しますか?' + #13#10 + #13#10 + '「いいえ」なら残します。もう一度インストールしたときに、そのまま使えます。', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(Data, True, True, True);
  end;
end;
