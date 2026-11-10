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
  ReadRegStr $0 HKCU "Software\LiveTranslatorOverlay" "SetupTermsVersion"
  ${If} $0 == "1"
    Abort
  ${EndIf}
FunctionEnd

Function AcceptTerms
  StrCpy $TermsAccepted "1"
FunctionEnd

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "This setup requires 64-bit Windows."
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
    WriteRegStr HKCU "Software\LiveTranslatorOverlay" "SetupTermsVersion" "1"
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
