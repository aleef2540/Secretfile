@echo off
setlocal enabledelayedexpansion

cd..
cd..

for /f "delims=" %%i in ('dir /s /b "rundev.bat"') do (
    set "filepath=%%i"
)

:: แปลง filepath ให้ได้แค่ path โฟลเดอร์ (ตัดชื่อไฟล์ออก)
for %%j in ("!filepath!") do set "folderpath=%%~dpj"

:: ตัด \ ท้ายสุดออก (ถ้ามี)
if "!folderpath:~-1!"=="\" set "folderpath=!folderpath:~0,-1!"

cd nssm-2.24-101-g897c7ad
cd win64

nssm install MyNodeService "!filepath!" run dev
nssm set MyNodeService AppDirectory "!folderpath!"
nssm start MyNodeService

exit