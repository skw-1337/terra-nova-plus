@echo off
rem Builds TNPlus.exe with the C# compiler shipped with Windows (.NET Framework 4)
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%CSC%" /nologo /optimize /platform:anycpu /win32manifest:app.manifest /win32icon:icon.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:TNPlus.exe TNPlus.cs TNPlusGui.cs TNPlusKeys.cs HdPayload.cs HdPayloadFr.cs HdPayloadEn.cs HdPayloadFrWide.cs HdPayloadEnWide.cs AweBank.cs
if errorlevel 1 (echo BUILD FAILED) else (echo Built TNPlus.exe)
pause
