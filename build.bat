@echo off
rem Builds StickBuddy.exe with the C# compiler that comes with Windows. No installs needed.
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /nowarn:4014 /out:StickBuddy.exe /lib:"%FW%\WPF" ^
  /r:System.Speech.dll /r:System.Web.Extensions.dll /r:System.Security.dll ^
  /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo Build FAILED.
  exit /b 1
)
echo.
echo Built StickBuddy.exe - double-click it to start your stick buddy!
