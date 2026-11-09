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
!insertmacro MUI_PAGE_LICENSE "${APP_LICENSE}"
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

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
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  Delete "$SMPROGRAMS\Live Translator Overlay.lnk"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\LiveTranslatorOverlay"
  RMDir /r "$LOCALAPPDATA\Programs\LiveTranslatorOverlay"
SectionEnd
