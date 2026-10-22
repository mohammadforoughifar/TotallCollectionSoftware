@echo off
setlocal
REM Uses ConnectionStrings__Default / appsettings of THIS installation. Never embeds a server or password.
REM Back up the database and stop other instances before running this command.
pushd "%~dp0" || exit /b 1
if exist "Inventory.Api.exe" (
    Inventory.Api.exe --update-database %*
    goto done
)
if exist "Inventory.Api.dll" (
    dotnet Inventory.Api.dll --update-database %*
    goto done
)
if not exist "inventory\src\Inventory.Api\Inventory.Api.csproj" (
    echo [DB] Inventory.Api application/project not found beside this script.
    popd
    exit /b 1
)
cd /d "inventory\src\Inventory.Api"
dotnet run --project Inventory.Api.csproj --no-launch-profile -- --update-database %*
:done
set "RESULT=%ERRORLEVEL%"
popd
endlocal & exit /b %RESULT%
