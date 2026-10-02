@echo off
rem Buduje EwidencjaRCP.exe (jeden plik, nie wymaga instalacji .NET na komputerze docelowym).
rem Wymaga .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
cd /d "%~dp0"
dotnet test tests\Rcp.Tests -c Release || goto :error
dotnet publish src\Rcp.App -c Release -r win-x64 -p:PublishSingleFile=true -o dist || goto :error
del /q dist\*.pdb 2>nul
echo.
echo Gotowe: %~dp0dist\EwidencjaRCP.exe
goto :eof
:error
echo Blad budowania.
exit /b 1
