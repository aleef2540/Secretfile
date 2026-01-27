@echo off
setlocal enabledelayedexpansion

cd source\app

for /f "delims=" %%i in ('dir /s /b "run.vbs"') do (
    set "filepath=%%i"
)

if not defined filepath (
    echo ไม่พบ run.vbs
    pause
    exit /b
)

for /f "delims=" %%i in ('dir /s /b "icon.ico"') do (
    set "iconPath=%%i"
)

if not defined iconPath (
    echo ไม่พบ icon.ico
    pause
    exit /b
)

echo Found file path: !filepath!

:: ดึง path Desktop จริงของ user ด้วย PowerShell
for /f "delims=" %%d in ('powershell -NoProfile -Command "[Environment]::GetFolderPath('Desktop')"') do set "desktopPath=%%d"

echo Desktop path detected as: !desktopPath!

set "targetPath=!filepath!"
set "shortcutPath=!desktopPath!\SecretFile.lnk"

:: หาโฟลเดอร์ StartIn จาก targetPath
for %%F in ("!targetPath!") do set "startIn=%%~dpF"

echo Creating shortcut at: !shortcutPath! with icon !iconPath! and StartIn !startIn!

powershell -Command " $WshShell = New-Object -ComObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('!shortcutPath!'); $Shortcut.TargetPath = '!targetPath!'; $Shortcut.IconLocation = '!iconPath!'; $Shortcut.WorkingDirectory = '!startIn!'; $Shortcut.Save() "

if exist "!shortcutPath!" (
    echo Shortcut was created successfully.
) else (
    echo Shortcut creation failed!
)
