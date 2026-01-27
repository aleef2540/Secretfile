@echo off
SET SERVICE_NAME=backend
SET EXE_PATH=C:\Users\aleef\OneDrive\Desktop\secret\backend\bin\Release\net9.0\win-x64\publish\backend.exe

echo ================================
echo 🔧 Installing %SERVICE_NAME%...
echo ================================

REM Create the service
sc stop %SERVICE_NAME%
sc delete %SERVICE_NAME%

sc create %SERVICE_NAME% binPath= "%EXE_PATH%" start= auto
sc config %SERVICE_NAME% obj= "LocalSystem"

echo ✅ Service created: %SERVICE_NAME%

REM Start the service
echo 🟢 Starting service...
sc start %SERVICE_NAME%

echo ✅ Service %SERVICE_NAME% started successfully!
pause