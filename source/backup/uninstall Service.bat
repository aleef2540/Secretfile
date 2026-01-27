@echo off
SET SERVICE_NAME=backend

sc stop %SERVICE_NAME%
sc delete %SERVICE_NAME%

echo ❌ Service %SERVICE_NAME% removed.
pause