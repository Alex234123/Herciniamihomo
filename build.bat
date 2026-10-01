@echo off
setlocal
echo ========================================================
echo  Herciniamihomo Pro - 1000Hz Liquid Glass Build System
echo ========================================================
echo.

set NET_DIR=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set CSC=%NET_DIR%\csc.exe
set WPF_DIR=%NET_DIR%\WPF

if not exist "%CSC%" (
    echo Error: csc.exe not found at %CSC%
    pause
    exit /b 1
)

echo [1/2] Compiling src\Program.cs with Windows Native C# Compiler...
"%CSC%" /target:winexe /platform:anycpu /optimize+ /win32icon:app.ico /out:Herciniamihomo.exe ^
    /r:"%NET_DIR%\System.dll" ^
    /r:"%NET_DIR%\System.Core.dll" ^
    /r:"%NET_DIR%\System.Xaml.dll" ^
    /r:"%WPF_DIR%\WindowsBase.dll" ^
    /r:"%WPF_DIR%\PresentationCore.dll" ^
    /r:"%WPF_DIR%\PresentationFramework.dll" ^
    /r:"%NET_DIR%\System.Net.Http.dll" ^
    /r:"%NET_DIR%\System.Web.Extensions.dll" ^
    /r:"%NET_DIR%\System.Drawing.dll" ^
    /r:"%NET_DIR%\System.Windows.Forms.dll" ^
    src\Program.cs

if %ERRORLEVEL% neq 0 (
    echo [ERROR] Compilation failed!
    pause
    exit /b %ERRORLEVEL%
)

echo [2/2] Build Succeeded: Herciniamihomo.exe generated!
echo.
echo Launching Herciniamihomo.exe...
start "" "Herciniamihomo.exe"
