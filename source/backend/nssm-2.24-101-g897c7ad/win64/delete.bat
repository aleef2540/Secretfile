@echo off
SET SERVICE_NAME=MyNodeService

sc stop %SERVICE_NAME%
sc delete %SERVICE_NAME%

echo ❌ Service %SERVICE_NAME% removed.
exit