# План: WebUI → Thin Client (через API)

## Цель
Заменить прямые вызовы Core методов в локальном Blazor Server web-интерфейсе на HTTP-вызовы к REST API того же процесса, по аналогии с тем как работает мобильное приложение (`RemoteVaultSession`).

## Файлы к изменению (5 файлов + 1 удалить)

| # | Файл | Действие | Суть |
|---|------|----------|------|
| 1 | `WebUi/Abstractions/CoreExceptions.cs` | **Создать** | Вынести `PropertyAlreadyExistError` / `PropertyDoesNotExistError` из Core. Страницы сейчас ловят их через `Doopass.Core.` fully-qualified name — это прямая зависимость WebUI от Core. Новый файл в пространстве имён `Doopass.WebUi.Abstractions`. |
| 2 | `WebUi/Services/LocalApiService.cs` | **Создать** | `IVaultSession` реализация: HTTP calls к `/api/vault/*`, `/api/auth/*`, `/api/backups/*`, `/api/settings/*` на localhost:{port}. JSON serialization/deserialization, error mapping (409→PE, 404→PNE, 401→UnauthorizedAccessException, 429→InvalidOperationException). Почти копия `RemoteVaultSession` без certificate pinning, ConnectionStore и timeout/Offline логики. |
| 3 | `Cli/Api/WebHostFactory.cs` | **Изменить** | DI: заменить регистрацию `DoopassSession` + `IVaultSession → DoopassSession` на `LocalApiService` + `IVaultSession → LocalApiService`. Удалить `using Doopass.Cli.Services;` и `typeof(DoopassSession).Assembly` в ServeAsset. |
| 4 | ~~`Cli/Services/DoopassSession.cs`~~ | **Удалить** | Больше не нужен — вся логика через API (327 lines). |
| 5 | `WebUi/Pages/Store.razor` | **Изменить** | Убрать `Doopass.Core.` prefix из catch pattern (line 339), так как исключения теперь в `Doopass.WebUi.Abstractions`. |

## Страницы НЕ меняются
Login, InitStore, Backups, Settings — они уже используют абстракцию `IVaultSession` целиком. После подмены реализации все страницы продолжат работать без изменений.

## API endpoint'ы
Никаких новых эндпоинтов не надо — `VaultEndpoints.cs` уже покрывает все операции:
- `/api/auth/login`, `/api/auth/init`, `/api/auth/logout`
- `/api/vault/state`, `/api/vault/keys`, `/api/vault/value`, `/api/vault/pairs`, `/api/vault/reload`
- `/api/backups/*`, `/api/settings/*`

## Коммиты (4 штуки)

```
commit 1: WebUi: extract CoreExceptions to Abstractions layer
  - New file: CoreExceptions.cs with PropertyAlreadyExistError + PropertyDoesNotExistError
  - Removes Doopass.Core dependency from Store.razor catch blocks

commit 2: WebUi: add LocalApiService as thin client IVaultSession implementation
  - New file: Services/LocalApiService.cs
  - HTTP calls to /api/* endpoints, JSON mapping, error status→exception
  - Bearer token from authentication cookie (Blazor Server auth middleware)

commit 3: Cli: swap DoopassSession for LocalApiService in DI
  - WebHostFactory: register LocalApiService instead of DoopassSession
  - Remove unused using + assembly reference

commit 4: remove obsolete DoopassSession class
  - Deleted Cli/Services/DoopassSession.cs (327 lines)
  - All web operations now route through /api/* endpoints like mobile does
```

## Что не трогать
- `VaultEndpoints.cs` — уже покрывает все операции
- `ApiVaultCoordinator` — ядро API, не меняется
- `RemoteVaultSession.cs` — мобильная версия, untouched
- SignalR/VaultHubRelay/IVaultChangeNotifier — события остаются для real-time sync между открытыми вкладками браузера

## Зависимости / риски
- Blazor Server уже аутентифицирован через cookie. LocalApiService будет делать HTTP к localhost:{port} — нужно передавать auth cookie в этих запросах, иначе API отклонит с 401. Это решается `HttpClient` с `HttpMessageHandler`, который проксирует cookie из текущего Blazor Server circuit.
- Port: если порт через `--port N`, LocalApiService должен знать этот порт. Можно получить из DI (Settings.ApiPort) или встроить логику определения порта из options.
