; Compile with the pinned NSIS 3.13 compiler through scripts/build-installer.ps1.
; This file authors an installer. Compiling it must never execute the installer.
Unicode true
RequestExecutionLevel admin
ManifestDPIAware true
ManifestSupportedOS Win10
SetCompressor /SOLID lzma
CRCCheck force
AllowSkipFiles off

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "nsDialogs.nsh"
!include "WinVer.nsh"
!include "x64.nsh"
!if ${NSIS_PTR_SIZE} != 4
  !error "This script uses the standard x86 Unicode NSIS stub."
!endif

!ifndef PAYLOAD_DIR
  !error "PAYLOAD_DIR is required."
!endif
!ifndef BACKEND_SCRIPT
  !error "BACKEND_SCRIPT is required."
!endif
!ifndef BACKEND_SHA256
  !error "BACKEND_SHA256 is required."
!endif
!ifndef PAYLOAD_MANIFEST
  !error "PAYLOAD_MANIFEST is required."
!endif
!ifndef PAYLOAD_MANIFEST_SHA256
  !error "PAYLOAD_MANIFEST_SHA256 is required."
!endif
!ifndef OUTPUT_FILE
  !error "OUTPUT_FILE is required."
!endif
!ifndef PRODUCT_VERSION
  !error "PRODUCT_VERSION is required."
!endif
!ifndef DISPLAY_VERSION
  !error "DISPLAY_VERSION is required."
!endif
!ifndef REPO_ROOT
  !error "REPO_ROOT is required."
!endif

!define ARP_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Tunnela"
!define PS_BASE `"$PowerShell" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass`

Name "Tunnela"
OutFile "${OUTPUT_FILE}"
InstallDir "$PROGRAMFILES64\Tunnela"
Icon "${REPO_ROOT}\src\Tunnela.Desktop\Assets\Tunnela.ico"
UninstallIcon "${REPO_ROOT}\src\Tunnela.Desktop\Assets\Tunnela.ico"
BrandingText "Tunnela ${DISPLAY_VERSION}"
VIProductVersion "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=1033 "ProductName" "Tunnela"
VIAddVersionKey /LANG=1033 "FileDescription" "Tunnela Setup"
VIAddVersionKey /LANG=1033 "FileVersion" "${DISPLAY_VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Tunnela contributors"
ShowInstDetails hide
ShowUninstDetails hide

Var PowerShell
Var StageDir
Var StageToken
Var StageOwned
Var StageEnvironmentReady
Var PreserveStage
Var ExistingInstallation
Var ControllerAccount
Var AccountDialog
Var AccountEdit
Var AccountConfirmation
Var BackendExit
Var BackendMessage
Var SetupMutex

!define MUI_ABORTWARNING
!define MUI_UNABORTWARNING
!define MUI_LANGDLL_ALLLANGUAGES
!define MUI_WELCOMEPAGE_TITLE "Tunnela ${DISPLAY_VERSION}"
!define MUI_WELCOMEPAGE_TEXT "$(WelcomeText)"
!define MUI_FINISHPAGE_TITLE "$(InstalledTitle)"
!define MUI_FINISHPAGE_TEXT "$(InstalledText)"
; No elevated post-install desktop launch: the user opens the Start menu shortcut.
!insertmacro MUI_PAGE_WELCOME
Page custom AccountPageCreate AccountPageLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!define MUI_UNFINISHPAGE_TEXT "$(UninstalledText)"
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Russian"

LangString WelcomeText ${LANG_RUSSIAN} "Мастер установит Tunnela для Windows.$\r$\n$\r$\nПеред установкой или обновлением отключите VPN в Tunnela и выберите «Закрыть интерфейс…» в меню значка в трее.$\r$\n$\r$\nКаталог установки: $PROGRAMFILES64\Tunnela.$\r$\nСохранённые профили останутся на месте."
LangString WelcomeText ${LANG_ENGLISH} "This wizard installs Tunnela for Windows.$\r$\n$\r$\nBefore installing or updating, disconnect the VPN in Tunnela and choose Close interface from its tray menu.$\r$\n$\r$\nInstallation folder: $PROGRAMFILES64\Tunnela.$\r$\nYour saved profiles will be preserved."
LangString InstalledTitle ${LANG_RUSSIAN} "Tunnela установлена"
LangString InstalledTitle ${LANG_ENGLISH} "Tunnela is installed"
LangString InstalledText ${LANG_RUSSIAN} "Откройте Tunnela через меню «Пуск».$\r$\n$\r$\nФоновая служба готова к работе и будет запускаться вместе с Windows. Подключение VPN выполняется кнопкой в приложении.$\r$\n$\r$\nДля обновления снова запустите установщик. Профили сохранятся."
LangString InstalledText ${LANG_ENGLISH} "Open Tunnela from the Start menu.$\r$\n$\r$\nThe background service is ready and will start with Windows. Connect to the VPN using the button in the app.$\r$\n$\r$\nTo update, run the installer again. Your profiles will be preserved."
LangString UninstalledText ${LANG_RUSSIAN} "Tunnela удалена.$\r$\n$\r$\nСохранённые профили оставлены в вашей учётной записи Windows. Они будут доступны после повторной установки."
LangString UninstalledText ${LANG_ENGLISH} "Tunnela has been removed.$\r$\n$\r$\nSaved profiles remain in your Windows account and will be available after reinstalling."
LangString AccountTitle ${LANG_RUSSIAN} "Доступ к Tunnela"
LangString AccountTitle ${LANG_ENGLISH} "Access to Tunnela"
LangString AccountSubtitle ${LANG_RUSSIAN} "Выберите учётную запись Windows для управления VPN."
LangString AccountSubtitle ${LANG_ENGLISH} "Choose the Windows account that will control the VPN."
LangString AccountNewText ${LANG_RUSSIAN} "Укажите учётную запись, под которой вы обычно работаете. Если для установки введён пароль другого администратора, здесь нужна ваша обычная учётная запись.$\r$\n$\r$\nФормат: КОМПЬЮТЕР\имя или ДОМЕН\имя."
LangString AccountNewText ${LANG_ENGLISH} "Choose the account you normally use. If you entered another administrator's password to install, select your everyday account here.$\r$\n$\r$\nFormat: COMPUTER\name or DOMAIN\name."
LangString AccountUpdateText ${LANG_RUSSIAN} "При обновлении сохраняется существующий доступ. Учётная запись администратора, запустившего установщик, его не заменяет."
LangString AccountUpdateText ${LANG_ENGLISH} "This update preserves the existing account access. The administrator running Setup does not replace that account."
LangString AccountConfirm ${LANG_RUSSIAN} "Разрешить указанной учётной записи управлять VPN"
LangString AccountConfirm ${LANG_ENGLISH} "Allow this account to control the VPN"
LangString AccountRequired ${LANG_RUSSIAN} "Укажите вашу учётную запись Windows и подтвердите доступ."
LangString AccountRequired ${LANG_ENGLISH} "Enter your Windows account and confirm access."
LangString UnsupportedPlatform ${LANG_RUSSIAN} "Нужна 64-разрядная Windows 10 или новее на процессоре x64."
LangString UnsupportedPlatform ${LANG_ENGLISH} "Windows 10 or later on an x64 processor is required."
LangString SilentUnsupported ${LANG_RUSSIAN} "Для первой версии требуется интерактивная установка. Запустите установщик без /S."
LangString SilentUnsupported ${LANG_ENGLISH} "This version requires interactive setup. Run Setup without /S."
LangString UnsafeProgramFiles ${LANG_RUSSIAN} "Не удалось подтвердить защиту каталога Program Files. Установка остановлена."
LangString UnsafeProgramFiles ${LANG_ENGLISH} "The protection of the Program Files folder could not be verified. Setup has stopped."
LangString StageFailure ${LANG_RUSSIAN} "Не удалось создать защищённый временный каталог установки."
LangString StageFailure ${LANG_ENGLISH} "The protected setup staging folder could not be created."
LangString BackendFailure ${LANG_RUSSIAN} "Установка остановлена.$\r$\n$\r$\n$BackendMessage"
LangString BackendFailure ${LANG_ENGLISH} "Setup stopped.$\r$\n$\r$\n$BackendMessage"
LangString BackendUnavailable ${LANG_RUSSIAN} "Не удалось выполнить установочный модуль. Перед повторной попыткой проверьте сообщения об ошибках ниже."
LangString BackendUnavailable ${LANG_ENGLISH} "The installation module could not run. See the details below before retrying."
LangString ExtractionFailure ${LANG_RUSSIAN} "Не удалось распаковать установочные файлы."
LangString ExtractionFailure ${LANG_ENGLISH} "The setup files could not be extracted."
LangString RetainedStage ${LANG_RUSSIAN} "Для восстановления сохранён защищённый каталог: $StageDir"
LangString RetainedStage ${LANG_ENGLISH} "The protected recovery folder was preserved: $StageDir"
LangString RegistrationFailure ${LANG_RUSSIAN} "Приложение установлено, но ярлык или запись удаления не созданы. Для исправления снова запустите установщик. Удаление также доступно через $INSTDIR\uninstall.exe."
LangString RegistrationFailure ${LANG_ENGLISH} "The app is installed, but its shortcut or uninstall entry could not be created. Run Setup again to repair this. You can also uninstall using $INSTDIR\uninstall.exe."
LangString UninstallFailure ${LANG_RUSSIAN} "Удаление остановлено.$\r$\n$\r$\n$BackendMessage"
LangString UninstallFailure ${LANG_ENGLISH} "Uninstall stopped.$\r$\n$\r$\n$BackendMessage"
LangString UninstallMissing ${LANG_RUSSIAN} "Установочный модуль отсутствует или каталог не защищён. Переустановите Tunnela, затем повторите удаление."
LangString UninstallMissing ${LANG_ENGLISH} "The installation module is missing or its folder is not protected. Reinstall Tunnela, then try uninstalling again."
LangString UninstallCleanupFailure ${LANG_RUSSIAN} "Основные компоненты удалены, но очистка ярлыка или записи удаления не завершена. Профили сохранены. Повторная установка восстановит возможность удаления."
LangString UninstallCleanupFailure ${LANG_ENGLISH} "The main components were removed, but shortcut or uninstall-entry cleanup did not finish. Profiles were preserved. Reinstalling will restore the uninstall option."
LangString InstallingText ${LANG_RUSSIAN} "Установка файлов и фоновой службы Tunnela…"
LangString InstallingText ${LANG_ENGLISH} "Installing Tunnela files and background service…"
LangString RemovingText ${LANG_RUSSIAN} "Удаление Tunnela с сохранением профилей…"
LangString RemovingText ${LANG_ENGLISH} "Removing Tunnela and preserving profiles…"
LangString SetupBusy ${LANG_RUSSIAN} "Другая установка или удаление Tunnela уже выполняется. Дождитесь завершения."
LangString SetupBusy ${LANG_ENGLISH} "Another Tunnela installation or uninstall is running. Wait for it to finish."

; Native x86 NSIS explicitly invokes the 64-bit PowerShell host. User data is
; passed through child-process environment variables, never inserted in code.
!macro CommonFunctions PREFIX
Function ${PREFIX}AcquireSetupMutex
  System::Call 'kernel32::CreateMutexW(p 0,i 0,w "Global\Tunnela.Setup.v1")p.r0 ?e'
  Pop $1
  ${If} $0 == 0
  ${OrIf} $1 == 183
    ${If} $0 != 0
      System::Call 'kernel32::CloseHandle(p r0)'
    ${EndIf}
    MessageBox MB_ICONSTOP "$(SetupBusy)"
    Abort
  ${EndIf}
  StrCpy $SetupMutex $0
FunctionEnd

Function ${PREFIX}SetPowerShell
  StrCpy $PowerShell "$WINDIR\SysNative\WindowsPowerShell\v1.0\powershell.exe"
  IfFileExists "$PowerShell" +3 0
    MessageBox MB_ICONSTOP "$(UnsupportedPlatform)"
    Abort
  ; Do not let elevated PowerShell autoload modules from a user's module path.
  System::Call 'kernel32::SetEnvironmentVariableW(w "PSModulePath",w "$WINDIR\System32\WindowsPowerShell\v1.0\Modules")i.r0'
  ${If} $0 == 0
    MessageBox MB_ICONSTOP "$(StageFailure)"
    Abort
  ${EndIf}
FunctionEnd

Function ${PREFIX}VerifyProgramFiles
  ; Same effective-write mask as ProtectedFiles.WriteRights in the service.
  ; InheritOnly ACEs do not grant rights to this folder. Reject redirected paths.
  nsExec::ExecToStack `${PS_BASE} -Command "try{$$p=[Environment]::GetFolderPath('ProgramFiles');$$t='S-1-5-18','S-1-5-32-544','S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464';$$a=Get-Acl -LiteralPath $$p;if($$t -notcontains $$a.GetOwner([Security.Principal.SecurityIdentifier]).Value){exit 1};foreach($$r in $$a.GetAccessRules(1,1,[Security.Principal.SecurityIdentifier])){if(($$r.AccessControlType -eq 0)-and!( $$r.PropagationFlags -band 2)-and($$r.FileSystemRights -band 852310)-and($$t -notcontains $$r.IdentityReference.Value)){exit 1}};for($$d=[IO.DirectoryInfo]$$p;$$d;$$d=$$d.Parent){if($$d.Attributes -band 1024){exit 1}};exit 0}catch{exit 1}"`
  Pop $BackendExit
  Pop $BackendMessage
  ${If} $BackendExit != 0
    MessageBox MB_ICONSTOP "$(UnsafeProgramFiles)"
    Abort
  ${EndIf}
FunctionEnd
!macroend
!insertmacro CommonFunctions ""
!insertmacro CommonFunctions "un."

Function .onInit
  StrCpy $StageOwned 0
  StrCpy $StageEnvironmentReady 0
  StrCpy $PreserveStage 0
  ; Choose English explicitly before LangDLL, independent of the Windows locale.
  ; The selection dialog still offers Russian for this installer invocation.
  StrCpy $LANGUAGE ${LANG_ENGLISH}
  !insertmacro MUI_LANGDLL_DISPLAY
  ${IfNot} ${AtLeastWin10}
  ${OrIfNot} ${IsNativeAMD64}
    MessageBox MB_ICONSTOP "$(UnsupportedPlatform)"
    Abort
  ${EndIf}
  IfSilent 0 +4
    MessageBox MB_ICONSTOP "$(SilentUnsupported)"
    SetErrorLevel 2
    Abort
  SetRegView 64
  SetShellVarContext all
  ; Ignore /D: the service deliberately accepts only this protected location.
  StrCpy $INSTDIR "$PROGRAMFILES64\Tunnela"
  Call AcquireSetupMutex
  Call SetPowerShell
  Call VerifyProgramFiles
  Call CreateProtectedStage
  SetOutPath "$StageDir"
  ClearErrors
  File /oname=installer-actions.ps1 "${BACKEND_SCRIPT}"
  File /oname=payload-manifest.json "${PAYLOAD_MANIFEST}"
  ${If} ${Errors}
    MessageBox MB_ICONSTOP "$(ExtractionFailure)"
    Call CleanupStage
    Abort
  ${EndIf}
  Call SecureStage
  ; The hash is compiled into Setup. Securing an extracted file is followed by
  ; verification, so an earlier owner cannot replace the elevated script.
  nsExec::ExecToStack `${PS_BASE} -Command "try{$$p=Join-Path $$env:TUNNELA_SETUP_STAGE 'installer-actions.ps1';if((Get-FileHash -LiteralPath $$p -Algorithm SHA256 -ErrorAction Stop).Hash -ne '${BACKEND_SHA256}'){exit 1};exit 0}catch{exit 1}"`
  Pop $BackendExit
  Pop $BackendMessage
  ${If} $BackendExit != 0
    MessageBox MB_ICONSTOP "$(ExtractionFailure)"
    Call CleanupStage
    Abort
  ${EndIf}
  nsExec::ExecToStack `${PS_BASE} -Command "[Console]::OutputEncoding=[Text.Encoding]::Unicode;& (Join-Path $$env:TUNNELA_SETUP_STAGE 'installer-actions.ps1') -Action Inspect -OutputFormat Nsis;exit $$LASTEXITCODE"`
  Pop $BackendExit
  Pop $BackendMessage
  ${If} $BackendExit != 0
    Call ShowBackendFailure
    Call CleanupStage
    Abort
  ${EndIf}
  ; The backend emits one bounded line: NEW|account or UPDATE|account.
  StrCpy $0 $BackendMessage 4
  ${If} $0 == "NEW|"
    StrCpy $ExistingInstallation 0
    StrCpy $ControllerAccount $BackendMessage "" 4
  ${Else}
    StrCpy $0 $BackendMessage 7
    ${If} $0 != "UPDATE|"
      StrCpy $BackendMessage "$(BackendUnavailable)"
      Call ShowBackendFailure
      Call CleanupStage
      Abort
    ${EndIf}
    StrCpy $ExistingInstallation 1
    StrCpy $ControllerAccount $BackendMessage "" 7
  ${EndIf}
  ; Console WriteLine may leave CR/LF; strip only trailing line separators.
  trim_account:
    StrCpy $0 $ControllerAccount 1 -1
    ${If} $0 == "$\r"
    ${OrIf} $0 == "$\n"
      StrCpy $ControllerAccount $ControllerAccount -1
      Goto trim_account
    ${EndIf}
FunctionEnd

Function CreateProtectedStage
  System::Call 'ole32::CoCreateGuid(g .r0)i.r1'
  ${If} $1 != 0
    MessageBox MB_ICONSTOP "$(StageFailure)"
    Abort
  ${EndIf}
  StrLen $1 $0
  ${If} $1 != 38
    MessageBox MB_ICONSTOP "$(StageFailure)"
    Abort
  ${EndIf}
  StrCpy $StageToken $0 36 1
  StrCpy $StageDir "$PROGRAMFILES64\Tunnela.Setup.$StageToken"
  ; CreateDirectoryW with a SECURITY_ATTRIBUTES object applies the restrictive
  ; DACL atomically. ERROR_ALREADY_EXISTS is failure, never an adoption.
  System::Call 'advapi32::ConvertStringSecurityDescriptorToSecurityDescriptorW(w "O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)",i 1,*p.r1,p 0)i.r0'
  ${If} $0 == 0
    MessageBox MB_ICONSTOP "$(StageFailure)"
    Abort
  ${EndIf}
  System::Call '*(i 12,p r1,i 0)p.r2'
  System::Call 'kernel32::CreateDirectoryW(w "$StageDir",p r2)i.r0'
  System::Free $2
  System::Call 'kernel32::LocalFree(p r1)'
  ${If} $0 == 0
    MessageBox MB_ICONSTOP "$(StageFailure)"
    Abort
  ${EndIf}
  StrCpy $StageOwned 1
  System::Call 'kernel32::SetEnvironmentVariableW(w "TUNNELA_SETUP_STAGE",w "$StageDir")i.r0'
  System::Call 'kernel32::SetEnvironmentVariableW(w "TUNNELA_SETUP_TOKEN",w "$StageToken")i.r1'
  ${If} $0 == 0
  ${OrIf} $1 == 0
    MessageBox MB_ICONSTOP "$(StageFailure)"
    Abort
  ${EndIf}
  StrCpy $StageEnvironmentReady 1
  System::Call 'kernel32::SetEnvironmentVariableW(w "TUNNELA_PAYLOAD_MANIFEST_SHA256",w "${PAYLOAD_MANIFEST_SHA256}")i.r0'
  ${If} $0 == 0
    MessageBox MB_ICONSTOP "$(StageFailure)"
    Call CleanupStage
    Abort
  ${EndIf}
FunctionEnd

Function SecureStage
  ; Files extracted by the elevated token may still have an individual owner.
  ; Normalize descendants before executing or trusting any extracted content.
  nsExec::ExecToStack `${PS_BASE} -Command "$$ErrorActionPreference='Stop';try{$$s=[Security.Principal.SecurityIdentifier]'S-1-5-32-544';$$items=@(Get-ChildItem -LiteralPath $$env:TUNNELA_SETUP_STAGE -Force -Recurse);if($$items|Where-Object {$$_.Attributes -band 1024}){exit 1};foreach($$f in ($$items|Sort-Object {$$_.FullName.Length})){$$a=Get-Acl -LiteralPath $$f.FullName;$$a.SetOwner($$s);$$a.SetAccessRuleProtection($$false,$$false);foreach($$r in @($$a.GetAccessRules($$true,$$false,[Security.Principal.SecurityIdentifier]))){$$a.RemoveAccessRuleSpecific($$r)};Set-Acl -LiteralPath $$f.FullName -AclObject $$a};exit 0}catch{exit 1}"`
  Pop $BackendExit
  Pop $BackendMessage
  ${If} $BackendExit != 0
    MessageBox MB_ICONSTOP "$(StageFailure)"
    Call CleanupStage
    SetErrorLevel 1
    Abort
  ${EndIf}
FunctionEnd

Function AccountPageCreate
  !insertmacro MUI_HEADER_TEXT "$(AccountTitle)" "$(AccountSubtitle)"
  nsDialogs::Create 1018
  Pop $AccountDialog
  ${If} $AccountDialog == error
    Abort
  ${EndIf}
  ${If} $ExistingInstallation == 1
    ${NSD_CreateLabel} 0 0 100% 55u "$(AccountUpdateText)"
    Pop $0
  ${Else}
    ${NSD_CreateLabel} 0 0 100% 55u "$(AccountNewText)"
    Pop $0
  ${EndIf}
  ${NSD_CreateText} 0 62u 100% 14u "$ControllerAccount"
  Pop $AccountEdit
  SendMessage $AccountEdit ${EM_SETLIMITTEXT} 256 0
  ${If} $ExistingInstallation == 1
    EnableWindow $AccountEdit 0
  ${Else}
    ${NSD_CreateCheckbox} 0 88u 100% 28u "$(AccountConfirm)"
    Pop $AccountConfirmation
  ${EndIf}
  nsDialogs::Show
FunctionEnd

Function AccountPageLeave
  ${If} $ExistingInstallation != 1
    ${NSD_GetText} $AccountEdit $ControllerAccount
    ${NSD_GetState} $AccountConfirmation $0
    ${If} $ControllerAccount == ""
    ${OrIf} $0 != ${BST_CHECKED}
      MessageBox MB_ICONEXCLAMATION "$(AccountRequired)"
      Abort
    ${EndIf}
  ${EndIf}
  ; Pass through a System plug-in register as well: account text must not enter
  ; that plug-in's own call-expression parser before backend validation.
  StrCpy $0 $ControllerAccount
  System::Call 'kernel32::SetEnvironmentVariableW(w "TUNNELA_SETUP_ACCOUNT",w r0)i.r1'
  ${If} $1 == 0
    MessageBox MB_ICONSTOP "$(AccountRequired)"
    Abort
  ${EndIf}
FunctionEnd

Function ShowBackendFailure
  ${If} $BackendMessage == ""
    StrCpy $BackendMessage "$(BackendUnavailable)"
  ${EndIf}
  DetailPrint "$BackendMessage"
  MessageBox MB_ICONSTOP "$(BackendFailure)"
FunctionEnd

Function CleanupStage
  ${If} $StageOwned != 1
    Return
  ${EndIf}
  ${If} $StageEnvironmentReady != 1
    Return
  ${EndIf}
  ${If} $PreserveStage == 1
    Return
  ${EndIf}
  SetOutPath "$WINDIR"
  ; Reconstruct the exact owned target before recursive removal. Never follow a
  ; reparse point in this tree. Failed-install recovery material is retained.
  nsExec::ExecToStack `${PS_BASE} -Command "$$ErrorActionPreference='Stop';try{$$g=[Guid]::ParseExact($$env:TUNNELA_SETUP_TOKEN,'D');$$p=Join-Path ([Environment]::GetFolderPath('ProgramFiles')) ('Tunnela.Setup.'+$$g.ToString('D'));if($$p -ne $$env:TUNNELA_SETUP_STAGE){exit 1};if(Test-Path -LiteralPath $$p){if((Get-Item -LiteralPath $$p -Force).Attributes -band 1024){exit 1};if(Get-ChildItem -LiteralPath $$p -Force -Recurse|Where-Object {$$_.Attributes -band 1024}){exit 1};Remove-Item -LiteralPath $$p -Recurse -Force};exit 0}catch{exit 1}"`
  Pop $BackendExit
  Pop $BackendMessage
  ${If} $BackendExit == 0
    StrCpy $StageOwned 0
  ${Else}
    DetailPrint "$(RetainedStage)"
  ${EndIf}
FunctionEnd

Function .onGUIEnd
  Call CleanupStage
  ${If} $SetupMutex != ""
    System::Call 'kernel32::CloseHandle(p $SetupMutex)'
  ${EndIf}
FunctionEnd

Section "Tunnela" MainSection
  SetOutPath "$StageDir\payload"
  ClearErrors
  File /r "${PAYLOAD_DIR}\*"
  ${If} ${Errors}
    MessageBox MB_ICONSTOP "$(ExtractionFailure)"
    SetErrorLevel 1
    Abort
  ${EndIf}
  ClearErrors
  WriteUninstaller "$StageDir\uninstall.exe"
  ${If} ${Errors}
    MessageBox MB_ICONSTOP "$(ExtractionFailure)"
    SetErrorLevel 1
    Abort
  ${EndIf}
  Call SecureStage
  DetailPrint "$(InstallingText)"
  ; Once mutation starts, do not discard backups on an unknown/failed result.
  StrCpy $PreserveStage 1
  nsExec::ExecToStack `${PS_BASE} -Command "[Console]::OutputEncoding=[Text.Encoding]::Unicode;& (Join-Path $$env:TUNNELA_SETUP_STAGE 'installer-actions.ps1') -Action Install -OutputFormat Nsis -PayloadDirectory (Join-Path $$env:TUNNELA_SETUP_STAGE 'payload') -ControllerAccount $$env:TUNNELA_SETUP_ACCOUNT -PayloadManifestHash $$env:TUNNELA_PAYLOAD_MANIFEST_SHA256;exit $$LASTEXITCODE"`
  Pop $BackendExit
  Pop $BackendMessage
  ${If} $BackendExit != 0
    ; Exit 1 guarantees no mutation or completed rollback. Exit 2 and transport
    ; failures retain staging because the final installation state is unknown.
    ${If} $BackendExit == 1
      StrCpy $PreserveStage 0
    ${EndIf}
    Call ShowBackendFailure
    ${If} $PreserveStage == 1
      DetailPrint "$(RetainedStage)"
      MessageBox MB_ICONINFORMATION "$(RetainedStage)"
    ${Else}
      Call CleanupStage
    ${EndIf}
    SetErrorLevel 1
    Abort
  ${EndIf}
  StrCpy $PreserveStage 0
  DetailPrint "$BackendMessage"
  ; The backend has committed payload, manifest, service, and both uninstall
  ; files. Only now publish the Start menu shortcut and the ARP registration.
  ClearErrors
  CreateShortcut "$SMPROGRAMS\Tunnela.lnk" "$INSTDIR\desktop\Tunnela.Desktop.exe" "" "$INSTDIR\desktop\Tunnela.Desktop.exe" 0
  WriteRegStr HKLM "${ARP_KEY}" "DisplayName" "Tunnela"
  WriteRegStr HKLM "${ARP_KEY}" "DisplayVersion" "${DISPLAY_VERSION}"
  WriteRegStr HKLM "${ARP_KEY}" "Publisher" "Tunnela contributors"
  WriteRegStr HKLM "${ARP_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${ARP_KEY}" "DisplayIcon" "$INSTDIR\desktop\Tunnela.Desktop.exe,0"
  WriteRegStr HKLM "${ARP_KEY}" "UninstallString" '$\"$INSTDIR\uninstall.exe$\"'
  WriteRegDWORD HKLM "${ARP_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${ARP_KEY}" "NoRepair" 1
  WriteRegDWORD HKLM "${ARP_KEY}" "InstallerLanguage" $LANGUAGE
  ${If} ${Errors}
    MessageBox MB_ICONSTOP "$(RegistrationFailure)"
    Call CleanupStage
    SetErrorLevel 1
    Abort
  ${EndIf}
  Call CleanupStage
  SetErrorLevel 0
SectionEnd

Function un.onInit
  SetRegView 64
  SetShellVarContext all
  StrCpy $INSTDIR "$PROGRAMFILES64\Tunnela"
  ReadRegDWORD $LANGUAGE HKLM "${ARP_KEY}" "InstallerLanguage"
  ; Preserve an explicit Russian selection; use English for missing/invalid data.
  ${If} $LANGUAGE != ${LANG_RUSSIAN}
    StrCpy $LANGUAGE ${LANG_ENGLISH}
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
  ${OrIfNot} ${IsNativeAMD64}
    MessageBox MB_ICONSTOP "$(UnsupportedPlatform)"
    Abort
  ${EndIf}
  Call un.SetPowerShell
  Call un.AcquireSetupMutex
  Call un.VerifyProgramFiles
FunctionEnd

Function un.onGUIEnd
  ${If} $SetupMutex != ""
    System::Call 'kernel32::CloseHandle(p $SetupMutex)'
  ${EndIf}
FunctionEnd

Section "Uninstall"
  DetailPrint "$(RemovingText)"
  ; Validate the backend file BEFORE executing it. The file and ancestors must
  ; not be redirected or writable/owned by a non-administrator. No untrusted
  ; path from ARP, /D, _?=, a profile, or a service message is used.
  nsExec::ExecToStack `${PS_BASE} -Command "try{$$p=Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Tunnela\installer-actions.ps1';foreach($$q in @($$p,(Split-Path $$p))){$$f=Get-Item -LiteralPath $$q -Force -ErrorAction Stop;if($$f.Attributes -band 1024){exit 1};$$a=Get-Acl -LiteralPath $$q;$$t='S-1-5-18','S-1-5-32-544';if($$t -notcontains $$a.GetOwner([Security.Principal.SecurityIdentifier]).Value){exit 1};foreach($$r in $$a.GetAccessRules(1,1,[Security.Principal.SecurityIdentifier])){if(($$r.AccessControlType -eq 0)-and!($$r.PropagationFlags -band 2)-and($$r.FileSystemRights -band 852310)-and($$t -notcontains $$r.IdentityReference.Value)){exit 1}}};exit 0}catch{exit 1}"`
  Pop $BackendExit
  Pop $BackendMessage
  ${If} $BackendExit != 0
    MessageBox MB_ICONSTOP "$(UninstallMissing)"
    SetErrorLevel 1
    Abort
  ${EndIf}
  nsExec::ExecToStack `${PS_BASE} -Command "[Console]::OutputEncoding=[Text.Encoding]::Unicode;& (Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Tunnela\installer-actions.ps1') -Action Uninstall -OutputFormat Nsis;exit $$LASTEXITCODE"`
  Pop $BackendExit
  Pop $BackendMessage
  ${If} $BackendExit != 0
    ${If} $BackendMessage == ""
      StrCpy $BackendMessage "$(UninstallMissing)"
    ${EndIf}
    DetailPrint "$BackendMessage"
    MessageBox MB_ICONSTOP "$(UninstallFailure)"
    SetErrorLevel 1
    Abort
  ${EndIf}
  DetailPrint "$BackendMessage"
  ; The backend removed only this product's service/desktop. User profiles and
  ; ProgramData recovery records are never recursively removed by this script.
  ClearErrors
  Delete "$SMPROGRAMS\Tunnela.lnk"
  DeleteRegKey HKLM "${ARP_KEY}"
  Delete "$INSTDIR\installer-actions.ps1"
  Delete "$INSTDIR\uninstall.exe"
  ${If} ${Errors}
    MessageBox MB_ICONSTOP "$(UninstallCleanupFailure)"
    SetErrorLevel 1
    Abort
  ${EndIf}
  ; Empty-directory removal is deliberately non-recursive: any retained
  ; transaction backup belongs to recovery, not automatic uninstall cleanup.
  RMDir "$INSTDIR"
  SetErrorLevel 0
SectionEnd
