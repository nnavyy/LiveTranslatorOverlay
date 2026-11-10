Unicode true
RequestExecutionLevel user
!include "MUI2.nsh"
!include "x64.nsh"

Name "Live Translator Overlay"
OutFile "${SETUP_FILE}"
InstallDir "$LOCALAPPDATA\Programs\LiveTranslatorOverlay"
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey "ProductName" "Live Translator Overlay"
VIAddVersionKey "FileDescription" "Live Translator Overlay Setup"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
VIAddVersionKey "LegalCopyright" "Live Translator Overlay"
SetCompressor zlib

!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\LiveTranslatorOverlay.exe"
!insertmacro MUI_PAGE_WELCOME
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipAcceptedTerms
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE AcceptTerms
!define MUI_LICENSEPAGE_TEXT_TOP "Review the app license, terms of service and privacy policy before installing."
!insertmacro MUI_PAGE_LICENSE "${SETUP_TERMS}"
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Var TermsAccepted

Function SkipAcceptedTerms
  ClearErrors
  FileOpen $0 "$LOCALAPPDATA\LiveTranslatorOverlay\terms-accepted.txt" r
  IfErrors showTerms
  FileRead $0 $1
  FileClose $0
  ${If} $1 == "${TERMS_REVISION}$\r$\n"
    Abort
  ${EndIf}
  showTerms:
FunctionEnd

Function AcceptTerms
  StrCpy $TermsAccepted "1"
FunctionEnd

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "This setup requires 64-bit Windows." /SD IDOK
    Abort
  ${EndIf}
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\LiveTranslatorOverlay_SingleInstance_Mutex_v1") p.r0'
  ${If} $0 != 0
    System::Call 'kernel32::CloseHandle(p r0)'
    MessageBox MB_ICONSTOP "Close Live Translator Overlay before running setup." /SD IDOK
    Abort
  ${EndIf}
  StrCpy $INSTDIR "$LOCALAPPDATA\Programs\LiveTranslatorOverlay"
FunctionEnd

Section "Install"
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  File /r /x appsettings.json "${PUBLISH_DIR}\*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateShortcut "$SMPROGRAMS\Live Translator Overlay.lnk" "$INSTDIR\LiveTranslatorOverlay.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LiveTranslatorOverlay" "DisplayName" "Live Translator Overlay"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LiveTranslatorOverlay" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LiveTranslatorOverlay" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LiveTranslatorOverlay" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LiveTranslatorOverlay" "NoRepair" 1
  ${If} $TermsAccepted == "1"
    ClearErrors
    ExecWait '"$INSTDIR\LiveTranslatorOverlay.exe" --record-setup-terms ${TERMS_REVISION}' $0
    ${If} ${Errors}
    ${OrIf} $0 != 0
      DetailPrint "Review the terms again when you open Live Translator."
    ${EndIf}
  ${EndIf}

  IfSilent done
  DetailPrint "Checking local speech files..."
  ClearErrors
  ExecWait '"$INSTDIR\LiveTranslatorOverlay.exe" --prepare-local-speech' $0
  ${If} ${Errors}
    DetailPrint "Local speech files can be set up when you first start audio."
  ${ElseIf} $0 != 0
    DetailPrint "Local speech files can be set up when you first start audio."
  ${EndIf}
  done:
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  Delete "$SMPROGRAMS\Live Translator Overlay.lnk"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LiveTranslatorOverlay"
  RMDir /r "$LOCALAPPDATA\Programs\LiveTranslatorOverlay"
SectionEnd
