@echo off
rem Builds QualControl.exe (and the offline qualifying sim) with the C# compiler that ships with Windows (.NET Framework 4).
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set SRC=src\GameMemory.cs src\Addr.cs src\Hub.cs src\Qualifying.cs src\QualOverlay.cs src\PlayerBanner.cs src\Overlay.cs src\ConeArt.cs src\Telemetry.cs src\Theme.cs src\Merge.cs src\MergeForm.cs
if not exist bin mkdir bin
"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ /out:bin\QualControl.exe /win32manifest:src\app.manifest /win32icon:src\QualControl.ico /resource:src\logo.png,QualControl.logo.png ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll %SRC% src\MainForm.cs || exit /b 1
echo built bin\QualControl.exe
"%CSC%" /nologo /target:exe /platform:x86 /out:bin\QualSim.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll %SRC% test\QualSim.cs || exit /b 1
