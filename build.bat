@echo off
echo Building Mini Calendar Application...
echo.

REM 检查 .NET SDK 是否安装
dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: .NET SDK not found. Please install .NET 10 SDK.
    exit /b 1
)

echo Restoring NuGet packages...
dotnet restore
if %errorlevel% neq 0 (
    echo ERROR: Failed to restore NuGet packages.
    exit /b 1
)

echo Building project...
dotnet build -c Release
if %errorlevel% neq 0 (
    echo ERROR: Build failed.
    exit /b 1
)

echo Publishing application...
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --self-contained -o publish
if %errorlevel% neq 0 (
    echo ERROR: Publish failed.
    exit /b 1
)

echo.
echo Build completed successfully!
echo Output directory: publish\
echo.
echo To run the application:
echo   cd publish
echo   MiniCalendar.exe
echo.
pause