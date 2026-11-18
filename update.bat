@echo off
rem 1) Edit GODOT below to your Godot .NET *console* exe (file name contains _mono_ and _console)
set GODOT="C:\Godot\Godot_v4.7.2-stable_mono_win64_console.exe"
set PROJ=C:\TNBCY00
set ZIP=%USERPROFILE%\Downloads\TNBCY00.zip
if exist "%ZIP%" (
  tar -xf "%ZIP%" -C C:\
  del "%ZIP%"
)
%GODOT% --headless --path %PROJ% --import
%GODOT% --headless --path %PROJ% --build-solutions --quit
%GODOT% --headless --path %PROJ% --export-debug "Android" %PROJ%\Builds\TNBCY.apk
if errorlevel 1 (echo EXPORT FAILED & pause & exit /b 1)
set ADB=C:\Android\platform-tools\adb.exe
if exist %ADB% %ADB% install -r %PROJ%\Builds\TNBCY.apk
pause
