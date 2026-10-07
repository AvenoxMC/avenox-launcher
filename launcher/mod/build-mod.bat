@echo off
rem Builds eagler-relay.jar (needs a JDK 21+ and the launcher's downloaded libraries).
cd /d "%~dp0"
set MIXIN=
for /r "%LOCALAPPDATA%\EaglerJavaLauncher\libraries\net\fabricmc\sponge-mixin" %%f in (*.jar) do set MIXIN=%%f
if "%MIXIN%"=="" (
	echo Lance d'abord le jeu une fois depuis le launcher pour telecharger les bibliotheques.
	exit /b 1
)
if exist build rmdir /s /q build
mkdir build
javac --release 21 -proc:none -nowarn -cp "%MIXIN%" -d build src\eaglerrelay\mixin\ConnectionMixin.java || exit /b 1
copy /y resources\*.json build >nul
cd build
jar --create --file ..\eagler-relay.jar . || exit /b 1
cd ..
rmdir /s /q build
echo eagler-relay.jar construit.
