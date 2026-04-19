# GoodTrip

Единый backend теперь работает на ASP.NET Core (`DataBase`), без обязательного запуска Node.js части.

## Что изменено

- Перенесен API `GET /api/sights/search` из `backend` в `DataBase/Program.cs`.
- `GET /api/sights/search` теперь читает достопримечательности из PostgreSQL.
- При старте API автоматически создает нужные таблицы (`attraction_categories`, `attractions`, `reviews`) и добавляет тестовые данные, если их еще нет.
- Добавлен health-check `GET /api/health`.
- Исправлен проект `DataBase/App.csproj` (полноценный web-проект .NET 8).
- Добавлен `Dockerfile` в корень для удобного деплоя одного сервиса.

## Локальный запуск

```bash
dotnet restore DataBase/App.csproj
dotnet run --project DataBase/App.csproj
```

По умолчанию Swagger доступен по `/swagger`.

## Запуск в Docker

```bash
docker build -t goodtrip-backend .
docker run -p 8080:8080 goodtrip-backend
```

API будет доступен на `http://localhost:8080`.

## Запуск API + PostgreSQL через Docker Compose

```bash
docker compose up --build
```

После запуска:

- API: `http://localhost:8080`
- Swagger: `http://localhost:8080/swagger`
- Health: `http://localhost:8080/api/health`

Остановка:

```bash
docker compose down
```