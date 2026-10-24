@echo off
rem Builds and runs the tests of Terra Nova Plus with the C# compiler shipped with Windows (.NET Framework 4), nothing
rem to install and no game needed: tests\TNPlusTests.exe, from the sources of TNPlus.exe (the .cs files and references
rem of build.bat's compiler line, the one the launchers build with) plus tests\*.cs. Exit code 1 when the build or a
rem test fails. "run_tests.bat Keys" runs the tests whose name contains Keys.
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
set "SRC=%~dp0..\src"
set "SRCS="
set "REFS="
rem build.bat's compiler line is the one whose first word is "%CSC%"
for /f "usebackq delims=" %%L in ("..\src\build.bat") do (
    set "FIRST="
    set "LS="
    set "LR="
    for %%T in (%%L) do (
        if not defined FIRST set "FIRST=%%~T"
        set "TOK=%%~T"
        if /i "%%~xT"==".cs" set LS=!LS! "!SRC!\%%~T"
        if /i "!TOK:~0,3!"=="/r:" set "LR=!LR! %%~T"
    )
    if "!FIRST!"=="%%CSC%%" (
        set "SRCS=!LS!"
        set "REFS=!LR!"
    )
)
set "RC=1"
if not defined SRCS (
    echo build.bat's compiler line not found
) else (
    "%CSC%" /nologo /debug:full /platform:anycpu !REFS! /main:TNPlusTestMain /out:TNPlusTests.exe !SRCS! *.cs
    if errorlevel 1 (
        echo TESTS BUILD FAILED
    ) else (
        TNPlusTests.exe %*
        set "RC=!ERRORLEVEL!"
    )
)
if not defined CI pause
exit /b %RC%
