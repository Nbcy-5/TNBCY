@echo off
rem Builds the content patch (routes and trains) for the in-game updater
set GODOT="C:\Godot\Godot_v4.7.2-stable_mono_win64_console.exe"
set PROJ=C:\TNBCY02
%GODOT% --headless --path %PROJ% --export-pack "Content" %PROJ%\Builds\content.pck
echo Done: %PROJ%\Builds\content.pck
pause
