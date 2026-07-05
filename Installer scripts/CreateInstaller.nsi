# define name of installer
	!system 'powershell -NoProfile -ExecutionPolicy Bypass -File "GenerateInstallerIncludes.ps1"'
	!include "App-Version.txt"
	!include "MUI.nsh"
	!include "FileFunc.nsh"
	Name "Deathcounter and Soundboard, Version ${Version}"
	OutFile "..\Installers\DCSB_${Version}.exe"
 
# define installation directory
	InstallDir "$PROGRAMFILES\Deathcounter and Soundboard"
	InstallDirRegKey HKLM Software\DCSB InstallLocation
 
# for removing Start Menu shortcut in Windows 7
	RequestExecutionLevel admin

# define icon
	!define MUI_ICON "..\DCSB\icon.ico"

# install pages
	!insertmacro MUI_PAGE_WELCOME
	!insertmacro MUI_PAGE_LICENSE "..\LICENSE"
	!insertmacro MUI_PAGE_COMPONENTS
	!insertmacro MUI_PAGE_DIRECTORY
	!insertmacro MUI_PAGE_INSTFILES
		# These indented statements modify settings for MUI_PAGE_FINISH
		!define MUI_FINISHPAGE_NOAUTOCLOSE
		# launch via explorer.exe so the app starts non-elevated; running it
		# directly would inherit the installer's admin token and UIPI would
		# block drag and drop from (non-elevated) Explorer into the app
		!define MUI_FINISHPAGE_RUN
		!define MUI_FINISHPAGE_RUN_FUNCTION LaunchApplication
		!define MUI_FINISHPAGE_RUN_CHECKED
		!define MUI_FINISHPAGE_RUN_TEXT "Start Deathcounter and Soundboard"
	!insertmacro MUI_PAGE_FINISH

# uninstall pages
	!insertmacro MUI_UNPAGE_WELCOME
	!insertmacro MUI_UNPAGE_CONFIRM
	!insertmacro MUI_UNPAGE_INSTFILES
	!insertmacro MUI_UNPAGE_FINISH
 
# language
	!insertmacro MUI_LANGUAGE "English"

# start the app de-elevated by asking the shell (explorer) to launch it
Function LaunchApplication
	Exec '"$WINDIR\explorer.exe" "$INSTDIR\DCSB.exe"'
FunctionEnd

# The app is framework-dependent: it needs the .NET 10 Desktop Runtime
# (Microsoft.WindowsDesktop.App) installed. Detect it by looking for a 10.x
# runtime folder under the shared framework directory; if it's missing, offer
# to open the download page. The user can still proceed (the .NET apphost shows
# its own prompt on first launch), but this catches it up front.
!define DOTNET_DOWNLOAD_URL "https://dotnet.microsoft.com/download/dotnet/10.0/runtime?cid=getdotnetcore&runtime=desktop"

Function CheckDotNetRuntime
	StrCpy $R1 "0"
	FindFirst $R0 $R2 "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App\10.*"
	loop:
		StrCmp $R2 "" done
		StrCpy $R1 "1"          # found at least one 10.x runtime folder
		FindNext $R0 $R2
		Goto loop
	done:
	FindClose $R0

	StrCmp $R1 "1" runtimeOk
		MessageBox MB_YESNO|MB_ICONEXCLAMATION \
			"Deathcounter and Soundboard needs the Microsoft .NET 10 Desktop Runtime (x64), which does not appear to be installed.$\n$\nOpen the download page now?$\n$\n(Choose No to continue installing anyway - the app will prompt you again on first launch.)" \
			IDNO runtimeOk
			ExecShell "open" "${DOTNET_DOWNLOAD_URL}"
	runtimeOk:
FunctionEnd

Function .onInit
	Call CheckDotNetRuntime
FunctionEnd

# main section: the application itself (required, cannot be deselected)
Section "Deathcounter and Soundboard (required)" SecApp
    SectionIn RO

    # writing registry and shortcuts for all users
    SetShellVarContext all

    # set the installation directory as the destination for the following actions
    SetOutPath $INSTDIR
 
    # create the uninstaller
    WriteUninstaller "uninstall.exe"

    # install app files (list generated from DCSB\bin\Release by GenerateInstallerIncludes.ps1)
    !include "Files-Install.nsi"

    # add application to Add/Remove programs
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DCSB" \
                 "DisplayName" "Deathcounter and Soundboard"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DCSB" \
                 "UninstallString" "$\"$INSTDIR\uninstall.exe$\""
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DCSB" \
                 "QuietUninstallString" "$\"$INSTDIR\uninstall.exe$\" /S"
	WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DCSB" \
                 "Publisher" "Kalejin"
	WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DCSB" \
                 "DisplayVersion" "${Version}"
	WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DCSB" \
                 "DisplayIcon" "$INSTDIR\DCSB.exe"
    ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
    IntFmt $0 "0x%08X" $0
	WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DCSB" \
                 "EstimatedSize" "$0"
    WriteRegStr HKLM "Software\DCSB" \
                 "InstallLocation" "$\"$INSTDIR$\""
SectionEnd

# optional section: start menu shortcuts (selected by default)
Section "Start Menu shortcuts" SecStartMenu
    # creating start menu shortcuts for all users
    SetShellVarContext all
    CreateDirectory "$SMPROGRAMS\Deathcounter and Soundboard"
    CreateShortCut "$SMPROGRAMS\Deathcounter and Soundboard\DCSB.lnk" "$INSTDIR\DCSB.exe"
    CreateShortCut "$SMPROGRAMS\Deathcounter and Soundboard\uninstall.lnk" "$INSTDIR\uninstall.exe"
SectionEnd

# optional section: desktop shortcut (selected by default)
Section "Desktop shortcut" SecDesktop
    # creating the desktop shortcut for all users
    SetShellVarContext all
    CreateShortCut "$DESKTOP\Deathcounter and Soundboard.lnk" "$INSTDIR\DCSB.exe"
SectionEnd

# descriptions shown on the components page
!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
    !insertmacro MUI_DESCRIPTION_TEXT ${SecApp} "The application and all files required to run it."
    !insertmacro MUI_DESCRIPTION_TEXT ${SecStartMenu} "Add shortcuts to the Start Menu."
    !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "Add a shortcut to the desktop."
!insertmacro MUI_FUNCTION_DESCRIPTION_END
 
# uninstaller section start
Section "uninstall"
 
    # creating start menu shortcuts for all users
    SetShellVarContext all

    # delete the uninstaller and app files (list generated by GenerateInstallerIncludes.ps1)
    Delete "$INSTDIR\uninstall.exe"
    !include "Files-Uninstall.nsi"
    StrCpy $0 "$INSTDIR"
    Call un.DeleteDirIfEmpty
 
    # remove the desktop shortcut (if the user created one)
    Delete "$DESKTOP\Deathcounter and Soundboard.lnk"

    # remove the link from the start menu
    Delete "$SMPROGRAMS\Deathcounter and Soundboard\uninstall.lnk"
    Delete "$SMPROGRAMS\Deathcounter and Soundboard\DCSB.lnk"
    StrCpy $0 "$SMPROGRAMS\Deathcounter and Soundboard"
    Call un.DeleteDirIfEmpty

    # remove registry key
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DCSB"
    DeleteRegKey HKLM "Software\DCSB"
 
# uninstaller section end
SectionEnd

Function un.DeleteDirIfEmpty
  FindFirst $R0 $R1 "$0\*.*"
  strcmp $R1 "." 0 NoDelete
   FindNext $R0 $R1
   strcmp $R1 ".." 0 NoDelete
    ClearErrors
    FindNext $R0 $R1
    IfErrors 0 NoDelete
     FindClose $R0
     Sleep 1000
     RMDir "$0"
  NoDelete:
   FindClose $R0
FunctionEnd