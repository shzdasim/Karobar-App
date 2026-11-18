; ==============================================================================
; Karobar-App Native (.NET 9 + WebView2) — Inno Setup 6 Windows (.exe) Installer
; & Zero-Data-Loss OTA Updater
; ==============================================================================
; Features:
;   1. Pure Native Single-Binary C#/.NET 9 + Kestrel + WebView2 Architecture (Zero PHP!)
;   2. Terms & Conditions (EULA) acceptance gate
;   3. Privacy Policy & Data Ownership acceptance gate
;   4. Smart Database Mode:
;      - Auto-detects existing installation for 100% Zero-Data-Loss In-Place Update
;      - Fresh Database for new store installations
;      - Herd / Abuzar Waseela SQLite Database Import (preserves 100% of store data)
;      - Multi-PC POS Terminal Mode connecting over LAN
;   5. Pre-install Hardware Machine ID display + Ed25519 License Key binding gate
;      using native `Karobar-App.exe --cli license:machine-id` & `--cli license:verify-installer`
;   6. Initial Administrator Account creation wizard (Name, Email, Password)
;   7. Custom installation directory picker ("Install anywhere on C:\, D:\, etc.")
;   8. Supports silent OTA updates (/VERYSILENT /UPGRADE=1) triggered from inside Karobar-App
; ==============================================================================

#define MyAppName "Karobar-App Native"
#ifndef MyAppVersion
  #define MyAppVersion "2.0.1"
#endif
#define MyAppPublisher "Malik Asim Shahzad (shzdasim)"
#define MyAppURL "https://github.com/shzdasim/Karobar-App"
#define MyAppSupportURL "https://github.com/shzdasim"
#define MyAppExeName "Karobar-Native.exe"
#define BuildStageDir "..\..\dist\native-stage"

[Setup]
AppId={{D4E91A72-8C3B-4F60-B52E-9A18C47E6309}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppSupportURL}
AppUpdatesURL={#MyAppURL}/releases
AppCopyright=Copyright (C) 2026 Malik Asim Shahzad (https://github.com/shzdasim)
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Windows ERP — Author & Developer: Malik Asim Shahzad (https://github.com/shzdasim)
VersionInfoTextVersion={#MyAppVersion}
VersionInfoCopyright=Copyright (C) 2026 Malik Asim Shahzad (https://github.com/shzdasim)
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}.0
DefaultDirName={autopf}\Karobar-App-Native
DefaultGroupName={#MyAppName}
DisableDirPage=no
AllowNoIcons=yes
CloseApplications=force
AppMutex=Global\KarobarNativeErpV2Mutex
RestartApplications=no
LicenseFile=..\..\installer\legal\TermsAndConditions.txt
OutputDir=..\..\dist\installer
OutputBaseFilename=Karobar-Native-Setup
SetupIconFile=..\..\desktop\icon.ico
Compression=lzma2/fast
SolidCompression=no
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\icon.ico
#ifdef SignToolEnabled
SignTool=karobarsign
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Dirs]
Name: "{app}"; Permissions: users-modify
Name: "{app}\database"; Permissions: users-modify
Name: "{app}\public"; Permissions: users-modify
Name: "{app}\resources"; Permissions: users-modify
Name: "{app}\storage"; Permissions: users-modify
Name: "{app}\storage\app"; Permissions: users-modify
Name: "{app}\storage\app\public"; Permissions: users-modify
Name: "{app}\storage\app\backups"; Permissions: users-modify
Name: "{app}\storage\app\whatsapp-agent"; Permissions: users-modify
Name: "{app}\storage\backups"; Permissions: users-modify
Name: "{app}\storage\logs"; Permissions: users-modify

[Files]
; Native single-file executable extracted on wizard boot to compute Machine ID & verify Ed25519 signature BEFORE installation
Source: "{#BuildStageDir}\{#MyAppExeName}"; Flags: dontcopy
Source: "..\..\desktop\icon.ico"; DestDir: "{app}"; Flags: ignoreversion
; Full staged Native C#/.NET 9 + WebView2 + React 19 SPA bundle (+ portable Node.js for optional WhatsApp sidecar)
; CRITICAL ZERO-DATA-LOSS EXCLUSIONS: Never overwrite existing SQLite database, .env, machine-id, license-clock, WhatsApp session, uploaded logos/files, or backups!
Source: "{#BuildStageDir}\*"; DestDir: "{app}"; \
  Excludes: "database\database.sqlite*,storage\app\.machine-id,storage\app\.license-clock,storage\app\whatsapp-auth\*,storage\app\public\*,storage\app\backups\*,storage\backups\*,.env"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"; IconFilename: "{app}\icon.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"; Tasks: desktopicon

[Run]
; Install Developer Authenticode Certificate into local Trusted Root & Trusted Publishers store so Karobar-App.exe is trusted on this PC
Filename: "{sys}\certutil.exe"; \
  Parameters: "-addstore -f ""Root"" ""{app}\desktop\MalikAsimShahzad-KarobarERP.cer"""; \
  Flags: runhidden waituntilterminated; \
  Check: FileExists(ExpandConstant('{app}\desktop\MalikAsimShahzad-KarobarERP.cer'))

Filename: "{sys}\certutil.exe"; \
  Parameters: "-addstore -f ""TrustedPublisher"" ""{app}\desktop\MalikAsimShahzad-KarobarERP.cer"""; \
  Flags: runhidden waituntilterminated; \
  Check: FileExists(ExpandConstant('{app}\desktop\MalikAsimShahzad-KarobarERP.cer'))

; Post-Install / Upgrade Native CLI Bootstrapper:
; - In upgrade mode (--upgrade-only via admin-init.json): backs up SQLite DB, runs schema updates & permissions, preserves 100% of users, license, settings & store data.
; - In fresh/import mode: initializes SQLite DB, creates Admin account, seals hardware-bound license.
; - Skipped in Multi-PC POS Terminal Mode (which connects directly to the Store Server over LAN).
Filename: "{app}\{#MyAppExeName}"; \
  Parameters: "--cli install --seal-file=""{tmp}\install-license.seal"" --admin-config=""{tmp}\admin-init.json"" --accept-legal --cleanup-temp"; \
  WorkingDir: "{app}"; \
  StatusMsg: "Updating Karobar-App native database schema and runtime configuration..."; \
  Flags: runhidden waituntilterminated; \
  Check: not IsTerminalModeSelected

; Launch the Native Desktop Application after interactive installation completes
Filename: "{app}\{#MyAppExeName}"; \
  Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent; \
  Check: not IsCliUpgradeMode

; Automatically relaunch Karobar-App.exe after a silent 1-click OTA update (/UPGRADE=1)
Filename: "{app}\{#MyAppExeName}"; \
  Flags: nowait; \
  Check: IsCliUpgradeMode

[Code]
var
  PrivacyPage: TWizardPage;
  PrivacyMemo: TNewMemo;
  PrivacyAcceptCheck: TNewCheckBox;

  DatabaseImportPage: TWizardPage;
  KeepExistingDbRadio: TNewRadioButton;
  FreshDbRadio: TNewRadioButton;
  ImportDbRadio: TNewRadioButton;
  TerminalModeRadio: TNewRadioButton;
  SqliteFileEdit: TNewEdit;
  BrowseSqliteBtn: TNewButton;
  ServerUrlEdit: TNewEdit;
  TerminalNameEdit: TNewEdit;

  LicenseKeyPage: TWizardPage;
  MachineIdEdit: TNewEdit;
  CopyMachineIdBtn: TNewButton;
  LicenseKeyMemo: TNewMemo;

  AdminAccountPage: TWizardPage;
  AdminNameEdit: TNewEdit;
  AdminEmailEdit: TNewEdit;
  AdminPasswordEdit: TPasswordEdit;
  AdminConfirmPasswordEdit: TPasswordEdit;

  DetectedMachineId: String;

function OtaUpgradeFlagPath(): String;
begin
  Result := GetEnv('TEMP') + '\karobar-native-ota-upgrade.flag';
end;

function IsCliUpgradeMode(): Boolean;
var
  SrcExeUpper: String;
begin
  SrcExeUpper := UpperCase(ExtractFileName(ExpandConstant('{srcexe}')));
  Result :=
    WizardSilent() or
    (ExpandConstant('{param:UPGRADE|0}') = '1') or
    (Pos('KAROBAR-NATIVE-UPDATE', SrcExeUpper) > 0) or
    FileExists(OtaUpgradeFlagPath());
end;

function ExistingInstallDbPath(): String;
begin
  if FileExists(ExpandConstant('{app}\database\database.sqlite')) then
    Result := ExpandConstant('{app}\database\database.sqlite')
  else
    Result := ExpandConstant('{app}\app\database\database.sqlite');
end;

function HasExistingInstalledDatabase(): Boolean;
begin
  Result := FileExists(ExistingInstallDbPath());
end;

function IsUpgradeDatabaseModeSelected(): Boolean;
begin
  Result := IsCliUpgradeMode() or ( Assigned(KeepExistingDbRadio) and KeepExistingDbRadio.Checked );
end;

function IsTerminalModeSelected(): Boolean;
begin
  Result := Assigned(TerminalModeRadio) and TerminalModeRadio.Checked;
end;

{ Compute deterministic hardware Machine ID using native Karobar-App.exe --cli license:machine-id }
function ComputeInstallerMachineId(): String;
var
  ResultCode: Integer;
  OutId: AnsiString;
begin
  Result := 'KRB-DETECTING';
  if IsCliUpgradeMode() then
  begin
    Result := 'KRB-UPGRADE-MODE';
    Exit;
  end;
  try
    ExtractTemporaryFile('{#MyAppExeName}');
    if Exec(
      ExpandConstant('{tmp}\{#MyAppExeName}'),
      '--cli license:machine-id --out="' + ExpandConstant('{tmp}\machine-id.txt') + '"',
      ExpandConstant('{tmp}'),
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode
    ) then
    begin
      if LoadStringFromFile(ExpandConstant('{tmp}\machine-id.txt'), OutId) then
        Result := Trim(String(OutId));
    end;
  except
    Result := 'KRB-UNKNOWN-HOST';
  end;
end;

procedure OnPrivacyCheckClick(Sender: TObject);
begin
  WizardForm.NextButton.Enabled := PrivacyAcceptCheck.Checked;
end;

procedure OnCopyMachineIdClick(Sender: TObject);
begin
  WizardForm.ActiveControl := MachineIdEdit;
  MachineIdEdit.SelectAll();
  SendMessage(MachineIdEdit.Handle, $0301, 0, 0);
  MsgBox('Machine ID copied to clipboard! Send this Machine ID to Karobar Support to receive your single-machine license key.', mbInformation, MB_OK);
end;

procedure CreatePrivacyPolicyPage();
begin
  PrivacyPage := CreateCustomPage(
    wpLicense,
    'Privacy Policy & Data Ownership Agreement',
    'Please review how Karobar-App stores and protects your store data before continuing.'
  );

  PrivacyMemo := TNewMemo.Create(PrivacyPage);
  PrivacyMemo.Parent := PrivacyPage.Surface;
  PrivacyMemo.Left := 0;
  PrivacyMemo.Top := 0;
  PrivacyMemo.Width := PrivacyPage.SurfaceWidth;
  PrivacyMemo.Height := PrivacyPage.SurfaceHeight - ScaleY(38);
  PrivacyMemo.ScrollBars := ssVertical;
  PrivacyMemo.ReadOnly := True;
  PrivacyMemo.Text :=
    'KAROBAR-APP — PRIVACY POLICY & DATA OWNERSHIP' + #13#10 + #13#10 +
    '1. 100% LOCAL-FIRST DATA OWNERSHIP' + #13#10 +
    'All business data (products, batches, invoices, ledgers, accounts, and HR records) is stored locally on your computer inside your chosen installation directory. We do not collect, upload, or sell your store records to any external server.' + #13#10 + #13#10 +
    '2. HARDWARE IDENTIFICATION FOR SINGLE-MACHINE LICENSING' + #13#10 +
    'To permanently bind your software license to this machine, the Software computes a one-way cryptographic hash (KRB-XXXX-XXXX-XXXX-XXXX) from your OS and hardware identifiers.' + #13#10 + #13#10 +
    '3. ZERO-DATA-LOSS SOFTWARE UPDATES' + #13#10 +
    'When connected to the internet, Karobar-App checks GitHub Releases for new features and bug fixes. Applying an update preserves 100% of your local database, users, license, and settings.' + #13#10 + #13#10 +
    '4. LOCAL SECURITY & ACCESS CONTROL' + #13#10 +
    'Administrator credentials created in the next step are hashed locally using bcrypt.';

  PrivacyAcceptCheck := TNewCheckBox.Create(PrivacyPage);
  PrivacyAcceptCheck.Parent := PrivacyPage.Surface;
  PrivacyAcceptCheck.Left := 0;
  PrivacyAcceptCheck.Top := PrivacyMemo.Top + PrivacyMemo.Height + ScaleY(10);
  PrivacyAcceptCheck.Width := PrivacyPage.SurfaceWidth;
  PrivacyAcceptCheck.Caption := 'I have read and agree to the Karobar-App Privacy Policy';
  PrivacyAcceptCheck.Checked := False;
  PrivacyAcceptCheck.OnClick := @OnPrivacyCheckClick;
end;

procedure OnDbModeChange(Sender: TObject);
begin
  SqliteFileEdit.Enabled := ImportDbRadio.Checked;
  BrowseSqliteBtn.Enabled := ImportDbRadio.Checked;
  if Assigned(ServerUrlEdit) then
    ServerUrlEdit.Enabled := TerminalModeRadio.Checked;
  if Assigned(TerminalNameEdit) then
    TerminalNameEdit.Enabled := TerminalModeRadio.Checked;
end;

procedure OnBrowseSqliteClick(Sender: TObject);
var
  SelectedFile: String;
begin
  SelectedFile := SqliteFileEdit.Text;
  if GetOpenFileName(
    'Select Existing Herd / Abuzar Waseela / Karobar SQLite Database File',
    SelectedFile,
    '',
    'SQLite Database Files (*.sqlite;*.db;*.sqlite3)|*.sqlite;*.db;*.sqlite3|All Files (*.*)|*.*',
    'sqlite'
  ) then
  begin
    SqliteFileEdit.Text := SelectedFile;
    ImportDbRadio.Checked := True;
    OnDbModeChange(Sender);
  end;
end;

procedure CreateDatabaseImportPage();
var
  FileLabel, ServerUrlLabel, TerminalNameLabel: TNewStaticText;
begin
  DatabaseImportPage := CreateCustomPage(
    wpSelectDir,
    'Deployment Mode, Database Setup & Multi-PC Terminal',
    'Choose Standalone 1-PC setup (Fresh, Update, or SQLite/Waseela Import) or connect this PC as a Multi-PC POS Terminal.'
  );

  KeepExistingDbRadio := TNewRadioButton.Create(DatabaseImportPage);
  KeepExistingDbRadio.Parent := DatabaseImportPage.Surface;
  KeepExistingDbRadio.Left := 0;
  KeepExistingDbRadio.Top := ScaleY(2);
  KeepExistingDbRadio.Width := DatabaseImportPage.SurfaceWidth;
  KeepExistingDbRadio.Caption := 'Keep Existing Database & License (In-Place Update — 100% Zero Data Loss)';
  KeepExistingDbRadio.Checked := False;
  KeepExistingDbRadio.OnClick := @OnDbModeChange;

  FreshDbRadio := TNewRadioButton.Create(DatabaseImportPage);
  FreshDbRadio.Parent := DatabaseImportPage.Surface;
  FreshDbRadio.Left := 0;
  FreshDbRadio.Top := KeepExistingDbRadio.Top + ScaleY(22);
  FreshDbRadio.Width := DatabaseImportPage.SurfaceWidth;
  FreshDbRadio.Caption := 'Standalone 1-PC: Start with a Fresh Database (New Store Installation)';
  FreshDbRadio.Checked := True;
  FreshDbRadio.OnClick := @OnDbModeChange;

  ImportDbRadio := TNewRadioButton.Create(DatabaseImportPage);
  ImportDbRadio.Parent := DatabaseImportPage.Surface;
  ImportDbRadio.Left := 0;
  ImportDbRadio.Top := FreshDbRadio.Top + ScaleY(22);
  ImportDbRadio.Width := DatabaseImportPage.SurfaceWidth;
  ImportDbRadio.Caption := 'Standalone 1-PC: Import Existing SQLite / Abuzar Waseela Database';
  ImportDbRadio.Checked := False;
  ImportDbRadio.OnClick := @OnDbModeChange;

  FileLabel := TNewStaticText.Create(DatabaseImportPage);
  FileLabel.Parent := DatabaseImportPage.Surface;
  FileLabel.Left := ScaleX(18);
  FileLabel.Top := ImportDbRadio.Top + ScaleY(20);
  FileLabel.Caption := 'Existing SQLite Database File (.sqlite / .db):';

  SqliteFileEdit := TNewEdit.Create(DatabaseImportPage);
  SqliteFileEdit.Parent := DatabaseImportPage.Surface;
  SqliteFileEdit.Left := ScaleX(18);
  SqliteFileEdit.Top := FileLabel.Top + ScaleY(16);
  SqliteFileEdit.Width := DatabaseImportPage.SurfaceWidth - ScaleX(118);
  SqliteFileEdit.Enabled := False;

  BrowseSqliteBtn := TNewButton.Create(DatabaseImportPage);
  BrowseSqliteBtn.Parent := DatabaseImportPage.Surface;
  BrowseSqliteBtn.Left := SqliteFileEdit.Left + SqliteFileEdit.Width + ScaleX(8);
  BrowseSqliteBtn.Top := SqliteFileEdit.Top - ScaleY(1);
  BrowseSqliteBtn.Width := ScaleX(92);
  BrowseSqliteBtn.Height := SqliteFileEdit.Height + ScaleY(2);
  BrowseSqliteBtn.Caption := 'Browse...';
  BrowseSqliteBtn.Enabled := False;
  BrowseSqliteBtn.OnClick := @OnBrowseSqliteClick;

  TerminalModeRadio := TNewRadioButton.Create(DatabaseImportPage);
  TerminalModeRadio.Parent := DatabaseImportPage.Surface;
  TerminalModeRadio.Left := 0;
  TerminalModeRadio.Top := SqliteFileEdit.Top + ScaleY(30);
  TerminalModeRadio.Width := DatabaseImportPage.SurfaceWidth;
  TerminalModeRadio.Caption := 'Multi-PC POS Terminal: Connect to Central Store Server over LAN';
  TerminalModeRadio.Checked := False;
  TerminalModeRadio.OnClick := @OnDbModeChange;

  ServerUrlLabel := TNewStaticText.Create(DatabaseImportPage);
  ServerUrlLabel.Parent := DatabaseImportPage.Surface;
  ServerUrlLabel.Left := ScaleX(18);
  ServerUrlLabel.Top := TerminalModeRadio.Top + ScaleY(20);
  ServerUrlLabel.Caption := 'Store Server LAN URL (e.g. http://192.168.1.10:8000):';

  ServerUrlEdit := TNewEdit.Create(DatabaseImportPage);
  ServerUrlEdit.Parent := DatabaseImportPage.Surface;
  ServerUrlEdit.Left := ScaleX(18);
  ServerUrlEdit.Top := ServerUrlLabel.Top + ScaleY(16);
  ServerUrlEdit.Width := (DatabaseImportPage.SurfaceWidth - ScaleX(30)) div 2;
  ServerUrlEdit.Text := 'http://192.168.1.10:8000';
  ServerUrlEdit.Enabled := False;

  TerminalNameLabel := TNewStaticText.Create(DatabaseImportPage);
  TerminalNameLabel.Parent := DatabaseImportPage.Surface;
  TerminalNameLabel.Left := ServerUrlEdit.Left + ServerUrlEdit.Width + ScaleX(12);
  TerminalNameLabel.Top := ServerUrlLabel.Top;
  TerminalNameLabel.Caption := 'Terminal / Counter Name:';

  TerminalNameEdit := TNewEdit.Create(DatabaseImportPage);
  TerminalNameEdit.Parent := DatabaseImportPage.Surface;
  TerminalNameEdit.Left := TerminalNameLabel.Left;
  TerminalNameEdit.Top := ServerUrlEdit.Top;
  TerminalNameEdit.Width := DatabaseImportPage.SurfaceWidth - TerminalNameEdit.Left;
  TerminalNameEdit.Text := 'Counter 1';
  TerminalNameEdit.Enabled := False;
end;

procedure CreateLicenseBindingPage();
var
  MachineIdLabel, KeyLabel: TNewStaticText;
begin
  LicenseKeyPage := CreateCustomPage(
    DatabaseImportPage.ID,
    'Software License Key & Machine Binding',
    'Enter your Karobar-App license key. This key will permanently bind to this computer and can never be used on another machine.'
  );

  MachineIdLabel := TNewStaticText.Create(LicenseKeyPage);
  MachineIdLabel.Parent := LicenseKeyPage.Surface;
  MachineIdLabel.Left := 0;
  MachineIdLabel.Top := 0;
  MachineIdLabel.Caption := 'Your Computer''s Hardware Machine ID (Send this to receive your bound key):';

  MachineIdEdit := TNewEdit.Create(LicenseKeyPage);
  MachineIdEdit.Parent := LicenseKeyPage.Surface;
  MachineIdEdit.Left := 0;
  MachineIdEdit.Top := MachineIdLabel.Top + ScaleY(20);
  MachineIdEdit.Width := LicenseKeyPage.SurfaceWidth - ScaleX(130);
  MachineIdEdit.ReadOnly := True;
  MachineIdEdit.Text := DetectedMachineId;

  CopyMachineIdBtn := TNewButton.Create(LicenseKeyPage);
  CopyMachineIdBtn.Parent := LicenseKeyPage.Surface;
  CopyMachineIdBtn.Left := MachineIdEdit.Width + ScaleX(8);
  CopyMachineIdBtn.Top := MachineIdEdit.Top - ScaleY(1);
  CopyMachineIdBtn.Width := ScaleX(122);
  CopyMachineIdBtn.Height := MachineIdEdit.Height + ScaleY(2);
  CopyMachineIdBtn.Caption := 'Copy Machine ID';
  CopyMachineIdBtn.OnClick := @OnCopyMachineIdClick;

  KeyLabel := TNewStaticText.Create(LicenseKeyPage);
  KeyLabel.Parent := LicenseKeyPage.Surface;
  KeyLabel.Left := 0;
  KeyLabel.Top := MachineIdEdit.Top + ScaleY(36);
  KeyLabel.Caption := 'Paste your Machine-Bound License Key (KRBL....):';

  LicenseKeyMemo := TNewMemo.Create(LicenseKeyPage);
  LicenseKeyMemo.Parent := LicenseKeyPage.Surface;
  LicenseKeyMemo.Left := 0;
  LicenseKeyMemo.Top := KeyLabel.Top + ScaleY(20);
  LicenseKeyMemo.Width := LicenseKeyPage.SurfaceWidth;
  LicenseKeyMemo.Height := LicenseKeyPage.SurfaceHeight - LicenseKeyMemo.Top - ScaleY(4);
  LicenseKeyMemo.ScrollBars := ssVertical;
end;

procedure CreateAdminAccountPage();
var
  NameLbl, EmailLbl, PassLbl, ConfirmLbl: TNewStaticText;
begin
  AdminAccountPage := CreateCustomPage(
    LicenseKeyPage.ID,
    'Create Store Administrator Account',
    'Set up your primary Administrator login credentials. You will use these to sign in on first launch and open the Setup Genie.'
  );

  NameLbl := TNewStaticText.Create(AdminAccountPage);
  NameLbl.Parent := AdminAccountPage.Surface;
  NameLbl.Left := 0;
  NameLbl.Top := 0;
  NameLbl.Caption := 'Administrator Full Name:';

  AdminNameEdit := TNewEdit.Create(AdminAccountPage);
  AdminNameEdit.Parent := AdminAccountPage.Surface;
  AdminNameEdit.Left := 0;
  AdminNameEdit.Top := NameLbl.Top + ScaleY(18);
  AdminNameEdit.Width := AdminAccountPage.SurfaceWidth;
  AdminNameEdit.Text := 'Store Administrator';

  EmailLbl := TNewStaticText.Create(AdminAccountPage);
  EmailLbl.Parent := AdminAccountPage.Surface;
  EmailLbl.Left := 0;
  EmailLbl.Top := AdminNameEdit.Top + ScaleY(34);
  EmailLbl.Caption := 'Administrator Email Address (Used to Sign In):';

  AdminEmailEdit := TNewEdit.Create(AdminAccountPage);
  AdminEmailEdit.Parent := AdminAccountPage.Surface;
  AdminEmailEdit.Left := 0;
  AdminEmailEdit.Top := EmailLbl.Top + ScaleY(18);
  AdminEmailEdit.Width := AdminAccountPage.SurfaceWidth;
  AdminEmailEdit.Text := 'admin@karobar.local';

  PassLbl := TNewStaticText.Create(AdminAccountPage);
  PassLbl.Parent := AdminAccountPage.Surface;
  PassLbl.Left := 0;
  PassLbl.Top := AdminEmailEdit.Top + ScaleY(34);
  PassLbl.Caption := 'Administrator Password (Minimum 6 characters):';

  AdminPasswordEdit := TPasswordEdit.Create(AdminAccountPage);
  AdminPasswordEdit.Parent := AdminAccountPage.Surface;
  AdminPasswordEdit.Left := 0;
  AdminPasswordEdit.Top := PassLbl.Top + ScaleY(18);
  AdminPasswordEdit.Width := AdminAccountPage.SurfaceWidth;

  ConfirmLbl := TNewStaticText.Create(AdminAccountPage);
  ConfirmLbl.Parent := AdminAccountPage.Surface;
  ConfirmLbl.Left := 0;
  ConfirmLbl.Top := AdminPasswordEdit.Top + ScaleY(34);
  ConfirmLbl.Caption := 'Confirm Administrator Password:';

  AdminConfirmPasswordEdit := TPasswordEdit.Create(AdminAccountPage);
  AdminConfirmPasswordEdit.Parent := AdminAccountPage.Surface;
  AdminConfirmPasswordEdit.Left := 0;
  AdminConfirmPasswordEdit.Top := ConfirmLbl.Top + ScaleY(18);
  AdminConfirmPasswordEdit.Width := AdminAccountPage.SurfaceWidth;
end;

function EscapeJsonString(const S: String): String;
var
  R: String;
begin
  R := S;
  StringChangeEx(R, '\', '\\', True);
  StringChangeEx(R, '"', '\"', True);
  Result := R;
end;

procedure WriteAdminInitJson();
var
  AdminJson, ImportPath: String;
begin
  if IsUpgradeDatabaseModeSelected() then
  begin
    AdminJson := '{"upgrade_only":true}';
  end
  else
  begin
    ImportPath := '';
    if Assigned(ImportDbRadio) and ImportDbRadio.Checked then
      ImportPath := Trim(SqliteFileEdit.Text);

    AdminJson :=
      '{"name":"' + EscapeJsonString(Trim(AdminNameEdit.Text)) +
      '","email":"' + EscapeJsonString(Trim(AdminEmailEdit.Text)) +
      '","password":"' + EscapeJsonString(AdminPasswordEdit.Text) +
      '","import_sqlite":"' + EscapeJsonString(ImportPath) +
      '","upgrade_only":false}';
  end;

  SaveStringToFile(ExpandConstant('{tmp}\admin-init.json'), AdminJson, False);
end;

procedure InitializeWizard();
var
  SavedDirAnsi: AnsiString;
  SavedDir: String;
begin
  DetectedMachineId := ComputeInstallerMachineId();
  CreatePrivacyPolicyPage();
  CreateDatabaseImportPage();
  CreateLicenseBindingPage();
  CreateAdminAccountPage();

  if IsCliUpgradeMode() then
  begin
    if FileExists(OtaUpgradeFlagPath()) then
    begin
      if LoadStringFromFile(OtaUpgradeFlagPath(), SavedDirAnsi) then
      begin
        SavedDir := Trim(String(SavedDirAnsi));
        if SavedDir <> '' then
          WizardForm.DirEdit.Text := SavedDir;
      end;
    end;
    WizardForm.LicenseAcceptedRadio.Checked := True;
    PrivacyAcceptCheck.Checked := True;
    KeepExistingDbRadio.Checked := True;
    FreshDbRadio.Checked := False;
    WriteAdminInitJson();
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if IsCliUpgradeMode() then
  begin
    if (PageID = wpLicense) or
       (PageID = PrivacyPage.ID) or
       (PageID = wpSelectDir) or
       (PageID = DatabaseImportPage.ID) or
       (PageID = LicenseKeyPage.ID) or
       (PageID = AdminAccountPage.ID) or
       (PageID = wpSelectProgramGroup) or
       (PageID = wpSelectTasks) or
       (PageID = wpReady) then
    begin
      Result := True;
      Exit;
    end;
  end;

  { When updating an existing installation or installing in Multi-PC Terminal Mode, skip License & Admin creation pages }
  if (Assigned(KeepExistingDbRadio) and KeepExistingDbRadio.Checked) or
     (Assigned(TerminalModeRadio) and TerminalModeRadio.Checked) then
  begin
    if (PageID = LicenseKeyPage.ID) or (PageID = AdminAccountPage.ID) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if IsCliUpgradeMode() and (not WizardSilent()) then
  begin
    if (CurPageID = wpWelcome) or (CurPageID = wpLicense) or (CurPageID = wpReady) then
    begin
      WizardForm.LicenseAcceptedRadio.Checked := True;
      PrivacyAcceptCheck.Checked := True;
      KeepExistingDbRadio.Checked := True;
      FreshDbRadio.Checked := False;
      WriteAdminInitJson();
      PostMessage(WizardForm.NextButton.Handle, $00F5, 0, 0);
      Exit;
    end;
    if CurPageID = wpFinished then
    begin
      PostMessage(WizardForm.NextButton.Handle, $00F5, 0, 0);
      Exit;
    end;
  end;

  if CurPageID = PrivacyPage.ID then
  begin
    WizardForm.NextButton.Enabled := PrivacyAcceptCheck.Checked;
  end;

  if CurPageID = DatabaseImportPage.ID then
  begin
    if HasExistingInstalledDatabase() then
    begin
      KeepExistingDbRadio.Enabled := True;
      if (not ImportDbRadio.Checked) and (not TerminalModeRadio.Checked) then
      begin
        KeepExistingDbRadio.Checked := True;
        FreshDbRadio.Checked := False;
      end;
    end
    else
    begin
      KeepExistingDbRadio.Enabled := False;
      if KeepExistingDbRadio.Checked then
      begin
        KeepExistingDbRadio.Checked := False;
        FreshDbRadio.Checked := True;
      end;
    end;
    OnDbModeChange(nil);
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  KeyText, ImportPath: String;
  VerifyResultCode: Integer;
  ErrorMsgAnsi: AnsiString;
begin
  Result := True;

  if IsCliUpgradeMode() then
  begin
    WriteAdminInitJson();
    Exit;
  end;

  { 1. Enforce Privacy Policy Acceptance }
  if CurPageID = PrivacyPage.ID then
  begin
    if not PrivacyAcceptCheck.Checked then
    begin
      MsgBox('You must read and accept the Privacy Policy before continuing.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  { 2. Enforce Database Setup / Upgrade / Herd Import Selection }
  if CurPageID = DatabaseImportPage.ID then
  begin
    if ImportDbRadio.Checked then
    begin
      ImportPath := Trim(SqliteFileEdit.Text);
      if (ImportPath = '') or (not FileExists(ImportPath)) then
      begin
        MsgBox('Please select a valid existing SQLite database file (.sqlite / .db) to import, or choose another option.', mbError, MB_OK);
        Result := False;
        Exit;
      end;
    end;

    if KeepExistingDbRadio.Checked or (Assigned(TerminalModeRadio) and TerminalModeRadio.Checked) then
    begin
      WriteAdminInitJson();
    end;
  end;

  { 3. Enforce Cryptographic Ed25519 License Key & Single-Machine Binding Gate }
  if CurPageID = LicenseKeyPage.ID then
  begin
    KeyText := Trim(LicenseKeyMemo.Text);
    if (Length(KeyText) < 24) or (Pos('.', KeyText) = 0) then
    begin
      MsgBox('Please paste a valid Karobar-App machine-bound license key (KRBL....) to bind this installation to your computer.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    SaveStringToFile(ExpandConstant('{tmp}\input-key.txt'), KeyText, False);

    if not Exec(
      ExpandConstant('{tmp}\{#MyAppExeName}'),
      '--cli license:verify-installer --key-file="' + ExpandConstant('{tmp}\input-key.txt') + '" --seal-out="' + ExpandConstant('{tmp}\install-license.seal') + '" --error-out="' + ExpandConstant('{tmp}\license-error.txt') + '"',
      ExpandConstant('{tmp}'),
      SW_HIDE,
      ewWaitUntilTerminated,
      VerifyResultCode
    ) or (VerifyResultCode <> 0) then
    begin
      if LoadStringFromFile(ExpandConstant('{tmp}\license-error.txt'), ErrorMsgAnsi) then
        MsgBox(Trim(String(ErrorMsgAnsi)), mbError, MB_OK)
      else
        MsgBox('License key verification failed. Ensure this key is valid and bound to Machine ID: ' + DetectedMachineId, mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  { 4. Enforce Initial Administrator Credentials Gate }
  if CurPageID = AdminAccountPage.ID then
  begin
    if Trim(AdminNameEdit.Text) = '' then
    begin
      MsgBox('Please enter the Administrator Full Name.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    if (Pos('@', AdminEmailEdit.Text) = 0) or (Pos('.', AdminEmailEdit.Text) = 0) then
    begin
      MsgBox('Please enter a valid Administrator Email Address (e.g. admin@mystore.com).', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    if Length(AdminPasswordEdit.Text) < 6 then
    begin
      MsgBox('Administrator password must be at least 6 characters long.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    if AdminPasswordEdit.Text <> AdminConfirmPasswordEdit.Text then
    begin
      MsgBox('The password and confirmation password do not match.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    WriteAdminInitJson();
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  NetCfgJson: String;
begin
  if CurStep = ssInstall then
  begin
    WriteAdminInitJson();
  end;
  if CurStep = ssPostInstall then
  begin
    if Assigned(TerminalModeRadio) and TerminalModeRadio.Checked then
    begin
      NetCfgJson :=
        '{"mode":"terminal","serverUrl":"' + EscapeJsonString(Trim(ServerUrlEdit.Text)) +
        '","terminalName":"' + EscapeJsonString(Trim(TerminalNameEdit.Text)) + '"}';
      SaveStringToFile(ExpandConstant('{app}\storage\app\network-mode.json'), NetCfgJson, False);
      ForceDirectories(ExpandConstant('{userappdata}\Karobar-App-Native'));
      SaveStringToFile(ExpandConstant('{userappdata}\Karobar-App-Native\network-mode.json'), NetCfgJson, False);
      SaveStringToFile(ExpandConstant('{userappdata}\Karobar-App-Native\network-config.json'), NetCfgJson, False);
    end;
    if FileExists(OtaUpgradeFlagPath()) then
      DeleteFile(OtaUpgradeFlagPath());
  end;
end;
