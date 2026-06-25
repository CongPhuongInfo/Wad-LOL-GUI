@echo off
setlocal
echo +------------------------------------------+
echo ^|  RiotWadGui - Build Script (.NET 4.5)    ^|
echo +------------------------------------------+

:: Tim csc.exe
set CSC=
for %%v in (4.0.30319) do (
    if exist "%WINDIR%\Microsoft.NET\Framework64\v%%v\csc.exe" (
        set CSC=%WINDIR%\Microsoft.NET\Framework64\v%%v\csc.exe
    ) else if exist "%WINDIR%\Microsoft.NET\Framework\v%%v\csc.exe" (
        set CSC=%WINDIR%\Microsoft.NET\Framework\v%%v\csc.exe
    )
)

if "%CSC%"=="" (
    echo [!] Khong tim thay csc.exe
    echo     Dam bao may da cai .NET Framework 4.x
    pause & exit /b 1
)

echo [*] Compiler: %CSC%

:: References
set REFS=-r:System.dll -r:System.Core.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll

:: ZstdNet optional
if exist "ZstdNet.dll" (
    echo [*] Tim thay ZstdNet.dll - bat ho tro Zstd
    set REFS=%REFS% -r:ZstdNet.dll
) else (
    echo [~] Khong co ZstdNet.dll - chi ho tro Raw va GZip
)

:: Build
if not exist "bin" mkdir bin

"%CSC%" ^
    -out:bin\RiotWadGui.exe ^
    -target:winexe ^
    -optimize+ ^
    -platform:anycpu ^
    %REFS% ^
    MainForm.cs

if errorlevel 1 (
    echo.
    echo [!] Build THAT BAI
    pause & exit /b 1
)

:: Copy ZstdNet neu co
if exist "ZstdNet.dll"     copy /Y "ZstdNet.dll"     "bin\" >nul
if exist "x64\libzstd.dll" (
    if not exist "bin\x64" mkdir "bin\x64"
    copy /Y "x64\libzstd.dll" "bin\x64\" >nul
)

echo.
echo [OK] Build thanh cong: bin\RiotWadGui.exe
echo.
pause
