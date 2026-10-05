#define AppName "KariyerTakip"
#define AppVersion "1.1.0"
#define Root ".."

[Setup]
#ifdef TestBuild
AppId=KariyerTakip-Installer-SmokeTest
#else
AppId={{F2C77385-94DF-4E09-B61D-AAB3E9ED0C94}
#endif
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=KariyerTakip
AppPublisherURL=https://github.com/cumakaya0000/kariyerTakip
AppSupportURL=https://github.com/cumakaya0000/kariyerTakip/issues
DefaultDirName={autopf}\KariyerTakip
DefaultGroupName=KariyerTakip
DisableProgramGroupPage=yes
DisableDirPage=no
DisableWelcomePage=no
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
SetupArchitecture=x86
ArchitecturesInstallIn64BitMode=x64os arm64
#ifdef X64Only
ArchitecturesAllowed=x64os
#else
ArchitecturesAllowed=x86compatible
#endif
MinVersion=10.0.17763
#ifdef TestBuild
OutputDir={#Root}\artifacts\test-installer
OutputBaseFilename=KariyerTakip-Kurulum-Test
#else
#ifdef NoTempSetup
OutputDir={#Root}\artifacts\KariyerTakip-x64-TempYok
UseSetupLdr=no
#else
OutputDir={#Root}\artifacts
#endif
#ifdef X64Only
OutputBaseFilename=KariyerTakip-Kurulum-x64
#else
OutputBaseFilename=KariyerTakip-Kurulum
#endif
#endif
SetupIconFile={#Root}\Assets\kt.ico
UninstallDisplayIcon={app}\KariyerTakip.exe
UninstallDisplayName=KariyerTakip
Uninstallable=yes
#ifdef TestBuild
AppMutex=Global\KariyerTakip-Installer-SmokeTest
#else
AppMutex=Global\KariyerTakip
#endif
SetupMutex=KariyerTakipSetup
WizardStyle=modern dynamic windows11
WizardSizePercent=120
WizardImageFile={#Root}\Assets\wizard.bmp
WizardSmallImageFile={#Root}\Assets\wizard-small.bmp
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
UsePreviousTasks=yes
ChangesAssociations=no
VersionInfoVersion={#AppVersion}
VersionInfoDescription=KariyerTakip Kurulum Sihirbazı
#ifdef SignedBuild
SignTool=kt-sign
SignedUninstaller=yes
#endif

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstünde KT simgeli kısayol oluştur"; GroupDescription: "Kısayollar:"; Flags: unchecked
Name: "startmenuicon"; Description: "Başlat menüsüne program ve kaldırma kısayolları ekle"; GroupDescription: "Kısayollar:"

[Files]
#ifndef X64Only
Source: "{#Root}\artifacts\publish\win-x86\KariyerTakip.exe"; DestDir: "{app}"; Flags: ignoreversion; Check: UseX86
#endif
Source: "{#Root}\artifacts\publish\win-x64\KariyerTakip.exe"; DestDir: "{app}"; Flags: ignoreversion; Check: UseX64
#ifndef X64Only
Source: "{#Root}\artifacts\publish\win-arm64\KariyerTakip.exe"; DestDir: "{app}"; Flags: ignoreversion; Check: UseArm64
#endif
Source: "{#Root}\appsettings.example.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\profile.example.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\Assets\kt.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\SECURITY.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\scripts\ZamanlanmisTarama.bat"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\KariyerTakip"; Filename: "{app}\KariyerTakip.exe"; WorkingDir: "{app}"; IconFilename: "{app}\kt.ico"; Tasks: startmenuicon
Name: "{group}\KariyerTakip'i Kaldır"; Filename: "{uninstallexe}"; Tasks: startmenuicon
Name: "{autodesktop}\KariyerTakip"; Filename: "{app}\KariyerTakip.exe"; WorkingDir: "{app}"; IconFilename: "{app}\kt.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\KariyerTakip.exe"; WorkingDir: "{app}"; Description: "KariyerTakip'i şimdi aç"; Flags: nowait postinstall skipifsilent unchecked runasoriginaluser
Filename: "{app}"; Description: "Kurulum klasörünü aç"; Flags: shellexec postinstall skipifsilent unchecked runasoriginaluser

[Messages]
WelcomeLabel1=KT ile kamu ilanlarını daha kolay takip edin
WelcomeLabel2=KariyerTakip; ilanları öğrenim, KPSS, şehir ve tecrübe bilgilerinizle karşılaştırır. Başvurularınızı ve son tarihleri tek ekranda takip etmenizi sağlar.%n%nBu sihirbaz programı tanıtır, kurulum klasörünü seçtirir ve başlangıç tercihlerinizi sorar.%n%nDevam etmek için İleri düğmesine basın.
SelectDirDesc=Programın kurulacağı klasörü seçin.
SelectDirLabel3=Program dosyaları bu klasöre kurulacak. Profil, ilan ve Telegram ayarlarınız kullanıcı hesabınızın ayrı veri klasöründe saklanır. Farklı bir konum seçmek için Gözat düğmesini kullanın.
SelectTasksDesc=Kısayol tercihlerinizi belirleyin.
SelectTasksLabel2=İstediğiniz kısayolları işaretleyin. Son ekranda programı hemen açmayı ve kurulum klasörünü göstermeyi ayrıca seçebilirsiniz. Windows ile başlatmayı daha sonra programın Telegram ve Sistem sekmesinden etkinleştirebilirsiniz.
ReadyLabel1=Tercihlerinizi kontrol edin. Yükle düğmesi programı seçtiğiniz klasöre kurar.
InstallingLabel=KT program dosyaları ve seçtiğiniz kısayollar hazırlanıyor. Kurulum bittikten sonra Profilim ekranında bölümünüzü, KPSS puanınızı ve şehirlerinizi seçebilirsiniz.
FinishedHeadingLabel=KariyerTakip kullanıma hazır
FinishedLabel=Önce Profilim sekmesinde bilgilerinizi kaydedin, ardından Şimdi Tara düğmesine basın. Telegram bildirimleri için Telegram ve Sistem sekmesini kullanın.%n%nSonuçlar otomatik değerlendirmedir; başvurudan önce resmî ilan metnini kontrol edin.%n%nProgramı Denetim Masası veya Windows Ayarları > Uygulamalar üzerinden kaldırabilirsiniz.

[Code]
var
  FeaturesPage: TWizardPage;
  FeaturesText: TNewMemo;

function GetFileAttributesW(FileName: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function DataDirectoryIsSafe(Path: String): Boolean;
var
  FindRec: TFindRec;
  Child: String;
begin
  Result := True;
  if not DirExists(Path) then Exit;
  if (GetFileAttributesW(Path) and $400) <> 0 then
  begin
    Result := False;
    Exit;
  end;
  if FindFirst(Path + '\*', FindRec) then
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        if (FindRec.Attributes and $400) <> 0 then
        begin
          Result := False;
          Exit;
        end;
        Child := Path + '\' + FindRec.Name;
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          if not DataDirectoryIsSafe(Child) then
          begin
            Result := False;
            Exit;
          end;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

function UseArm64: Boolean;
begin
  Result := ProcessorArchitecture = paArm64;
end;

function UseX64: Boolean;
begin
  Result := IsWin64 and not UseArm64;
end;

function UseX86: Boolean;
begin
  Result := not IsWin64;
end;

procedure InitializeWizard;
begin
  FeaturesPage := CreateCustomPage(wpWelcome, 'KariyerTakip neler yapar?', 'İlan keşfinden başvuru takibine kadar tek bir çalışma alanı.');
  FeaturesText := TNewMemo.Create(FeaturesPage);
  FeaturesText.Parent := FeaturesPage.Surface;
  FeaturesText.SetBounds(0, 0, FeaturesPage.SurfaceWidth, FeaturesPage.SurfaceHeight);
  FeaturesText.ReadOnly := True;
  FeaturesText.ScrollBars := ssVertical;
  FeaturesText.BorderStyle := bsNone;
  FeaturesText.WordWrap := True;
  FeaturesText.Text :=
    '1. Profilinizi tanımlayın' + #13#10 +
    'Bölüm, öğrenim, KPSS, ehliyet ve şehirleri seçim kutularından belirtin. Birden fazla profil oluşturabilirsiniz.' + #13#10#13#10 +
    '2. İlanları değerlendirin' + #13#10 +
    'Kamu ilanlarını tarayın; uygun, kontrol gerekli ve uygun olmayan kadroların nedenlerini inceleyin. Seçili ilanları birlikte açın.' + #13#10#13#10 +
    '3. Başvurularınızı takip edin' + #13#10 +
    'Başvuracağım, başvurdum veya geçtim durumlarını ve kişisel notlarınızı kaydedin. Telegram bildirimlerini isteğe bağlı etkinleştirin.' + #13#10#13#10 +
    'Gizlilik ve kullanım' + #13#10 +
    'Profiliniz bu Windows hesabında saklanır. Uygulama e-Devlet şifresi istemez. Başvuru resmî sitede yapılır; sonuçlar bir başvuru garantisi değildir.';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpInstalling then
  begin
    WizardForm.StatusLabel.Caption := 'Kurulum konumu: ' + ExpandConstant('{app}');
  end;
  if CurPageID = wpFinished then
  begin
    WizardForm.FinishedLabel.Caption :=
      'KariyerTakip başarıyla kuruldu.' + #13#10#13#10 +
      'Kurulum konumu: ' + ExpandConstant('{app}') + #13#10#13#10 +
      'Aşağıdan programı açmayı veya kurulum klasörünü göstermeyi seçebilirsiniz.' + #13#10 +
      'Denetim Masası > Programlar ve Özellikler üzerinden kaldırabilirsiniz.';
  end;
end;

function InitializeUninstall: Boolean;
begin
#ifdef TestBuild
  Result := True;
#else
  Result := not CheckForMutexes('Global\KariyerTakip');
  if not Result then
    MsgBox('KariyerTakip açık. Pencereyi ve sistem tepsisindeki programı kapatıp kaldırmayı yeniden başlatın.', mbInformation, MB_OK);
#endif
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
#ifndef TestBuild
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'KariyerTakip');
    { Personal data is deliberately retained unless the user explicitly chooses deletion. }
    if not UninstallSilent then
      if MsgBox('Program kaldırıldı. Kayıtlı profilleri, Telegram tokenını, ilan veritabanını ve günlükleri de silmek istiyor musunuz?' + #13#10#13#10 + 'Hayır seçerseniz yeniden kurduğunuzda bu veriler kullanılabilir.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        if not DataDirectoryIsSafe(ExpandConstant('{localappdata}\KariyerTakip')) then
        begin
          MsgBox('Veri klasörü dosya bağlantıları içeriyor; güvenlik için otomatik veri temizliği yapılmadı.', mbInformation, MB_OK);
          Exit;
        end;
        DeleteFile(ExpandConstant('{localappdata}\KariyerTakip\telegram.secret'));
        DeleteFile(ExpandConstant('{localappdata}\KariyerTakip\profile.json'));
        DeleteFile(ExpandConstant('{localappdata}\KariyerTakip\profiles.json'));
        DeleteFile(ExpandConstant('{localappdata}\KariyerTakip\appsettings.json'));
        DeleteFile(ExpandConstant('{localappdata}\KariyerTakip\api-health.json'));
        DeleteFile(ExpandConstant('{localappdata}\KariyerTakip\kariyertakip.db'));
        DeleteFile(ExpandConstant('{localappdata}\KariyerTakip\kariyertakip.db-wal'));
        DeleteFile(ExpandConstant('{localappdata}\KariyerTakip\kariyertakip.db-shm'));
        { Only fixed application-owned directories are removed; custom paths are not followed. }
        DelTree(ExpandConstant('{localappdata}\KariyerTakip\logs'), True, True, True);
        DelTree(ExpandConstant('{localappdata}\KariyerTakip\feedback-fixtures'), True, True, True);
        DelTree(ExpandConstant('{localappdata}\KariyerTakip\documents'), True, True, True);
      end;
#endif
  end;
end;
