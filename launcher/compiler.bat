@echo off
rem Compile EaglerJavaLauncher.exe with the C# compiler shipped with Windows (.NET Framework 4.8).
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /out:EaglerJavaLauncher.exe ^
 /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
 /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll ^
 /r:System.IO.Compression.FileSystem.dll /r:Microsoft.VisualBasic.dll /r:System.Security.dll ^
 /resource:mod\eagler-relay.jar,eagler-relay.jar ^
 src\*.cs
if errorlevel 1 (
	echo Echec de la compilation.
	pause
	exit /b 1
)
echo EaglerJavaLauncher.exe compile.
