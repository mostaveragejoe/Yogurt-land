@echo off
rem One-time installer for Lead Engine v2.
rem Double-click this file one time. It installs the free Python
rem packages that the app needs.

setlocal
set "PY=C:\Users\joe\AppData\Local\Programs\Python\Python313\python.exe"
if not exist "%PY%" set "PY=py"
where /q "%PY%" >nul 2>&1
if errorlevel 1 if not exist "%PY%" set "PY=python"

echo.
echo Lead Engine v2 - package installation. This can take some minutes.
echo.
"%PY%" -m pip install --upgrade pip
"%PY%" -m pip install flask requests beautifulsoup4 openpyxl apscheduler rapidfuzz dnspython gspread google-auth
if errorlevel 1 (
  echo.
  echo The installation failed. Make sure that Python 3 is installed
  echo and that the internet connection is up. Then run this file again.
) else (
  echo.
  echo Done. Now double-click launcher.pyw to start the app.
)
echo.
pause
