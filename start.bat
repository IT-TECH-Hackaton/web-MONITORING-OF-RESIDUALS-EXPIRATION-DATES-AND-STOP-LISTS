@echo off
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"
set "ROOT=%CD%"

echo.
echo  Dev: локальный API + Vite, hot reload.
echo  Docker web/api глушим — они держат 5173 и 5080.
echo.

where docker >nul 2>&1
if not errorlevel 1 (
  docker compose stop api web >nul 2>&1
)

call :port_open 5432
if errorlevel 1 (
  echo  Postgres на 5432 нет. Поднимаю только контейнер базы.
  where docker >nul 2>&1
  if errorlevel 1 (
    echo  Нет Docker и нет Postgres на localhost:5432.
    echo  Поставь PostgreSQL 16 или Docker Desktop и запусти ещё раз.
    goto :fail
  )
  docker compose up -d postgres
  if errorlevel 1 (
    echo  Не поднялся postgres.
    goto :fail
  )
  echo  Жду готовности базы...
  set "TRIES=0"
  :wait_pg
  call :port_open 5432
  if not errorlevel 1 goto :pg_ok
  set /a TRIES+=1
  if %TRIES% GEQ 40 (
    echo  Postgres не ответил на 5432.
    goto :fail
  )
  timeout /t 1 /nobreak >nul
  goto :wait_pg
)
:pg_ok
echo  База: localhost:5432

where node >nul 2>&1
if errorlevel 1 (
  echo  Нет Node.js. Поставь LTS: https://nodejs.org/
  goto :fail
)

where dotnet >nul 2>&1
if errorlevel 1 (
  echo  Нет dotnet в PATH.
  echo  Поставь SDK 8: https://dotnet.microsoft.com/download/dotnet/8.0
  echo  winget install Microsoft.DotNet.SDK.8
  goto :fail
)
dotnet --list-sdks 2>nul | findstr /b "8." >nul
if errorlevel 1 (
  echo  Есть runtime, нет .NET 8 SDK — без него hot reload не запустится.
  echo  Поставь: https://dotnet.microsoft.com/download/dotnet/8.0
  echo  winget install Microsoft.DotNet.SDK.8
  goto :fail
)
echo  .NET 8 SDK ок

if not exist "%ROOT%\node_modules" (
  echo  npm install...
  call npm install
  if errorlevel 1 goto :fail
)

call :port_open 5080
if not errorlevel 1 (
  echo  Порт 5080 занят. Останови старый API или docker api и запусти снова.
  goto :fail
)
call :port_open 5173
if not errorlevel 1 (
  echo  Порт 5173 занят. Останови старый Vite или docker web и запусти снова.
  goto :fail
)

echo.
echo  Окна: API watch и Vite. Правки подхватываются сами.
echo  Веб     http://localhost:5173
echo  API     http://localhost:5080
echo  Swagger http://localhost:5080/swagger
echo  manager / manager123 · kitchen / kitchen123 · waiter / waiter123
echo.

start "monitoring-api" /D "%ROOT%\backend\src\Api" cmd /k "chcp 65001 >nul && set ASPNETCORE_ENVIRONMENT=Development&& echo API watch  http://localhost:5080&& dotnet watch run --urls http://localhost:5080"
start "monitoring-web" /D "%ROOT%" cmd /k "chcp 65001 >nul && echo Vite  http://localhost:5173&& npm run dev:web"

echo  Готово. Это окно можно закрыть.
echo.
endlocal
exit /b 0

:port_open
powershell -NoProfile -Command "try { $c = New-Object Net.Sockets.TcpClient; $c.Connect('127.0.0.1', %~1); $c.Close(); exit 0 } catch { exit 1 }"
exit /b %ERRORLEVEL%

:fail
echo.
echo  Не стартовал.
pause
endlocal
exit /b 1
