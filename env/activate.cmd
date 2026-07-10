@echo off
set "ROOT=%~dp0.."
set "PATH=%ROOT%\tools\dotnet;%ROOT%\tools\python;%ROOT%\runtime\ffmpeg;%ROOT%\tools\inno;%PATH%"
set "DOTNET_ROOT=%ROOT%\tools\dotnet"
set "DOTNET_MULTILEVEL_LOOKUP=0"
set "NUGET_PACKAGES=%ROOT%\tools\nuget-packages"
set "MULTICAMAPP_ROOT=%ROOT%"
set "PYTHONIOENCODING=utf-8"
echo MultiCamApp env active
