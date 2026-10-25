@echo off
rem Publishes the Windows head as Native AOT on this machine.
rem
rem Two things this VS 2026 layout needs that the ILCompiler cannot
rem find on its own: its vswhere probe misses this install (so the
rem environmental tools are used instead), and VsDevCmd does not put
rem the MSVC toolset on PATH (so the linker and libraries are pinned).
call "D:\vs2026\Common7\Tools\VsDevCmd.bat" -arch=x64 -no_logo
set PATH=D:\vs2026\VC\Tools\MSVC\14.51.36231\bin\Hostx64\x64;%PATH%
set LIB=D:\vs2026\VC\Tools\MSVC\14.51.36231\lib\x64;C:\Program Files (x86)\Windows Kits\10\Lib\10.0.26100.0\um\x64;C:\Program Files (x86)\Windows Kits\10\Lib\10.0.26100.0\ucrt\x64
cd /d E:\DevNull.Client
dotnet publish src\DevNull.Client.Windows -c Release -r win-x64 -p:PublishAot=true -p:TreatWarningsAsErrors=true -p:IlcUseEnvironmentalTools=true -p:DebugType=none -p:DebugSymbols=false -o publish\win-x64
rem The WPF native support files ride the System.Reactive framework
rem reference and nothing in this Avalonia app reaches them.
del publish\win-x64\wpfgfx_cor3.dll publish\win-x64\PresentationNative_cor3.dll publish\win-x64\PenImc_cor3.dll publish\win-x64\vcruntime140_cor3.dll publish\win-x64\D3DCompiler_47_cor3.dll 2>nul
