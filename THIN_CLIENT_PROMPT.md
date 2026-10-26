# PROMPT: Doopass thin-client — REST API на хосте + Android-клиент (MAUI Blazor Hybrid)

> Этот документ — самодостаточная инструкция для реализации в чистой сессии.
> Работай в репозитории doopass-next (решение `Doopass.slnx`). Прежде чем писать код,
> прочитай указанные в «Фаза 0» файлы: факты ниже описывают текущее состояние репозитория.

## 0. Контекст проекта (факты, проверь по коду)

**Doopass** — менеджер паролей на .NET 10. Зашифрованное хранилище `store.enc`
(Fernet + PBKDF2-HMAC-SHA256, формат менять запрещено). Существующие проекты:

| Проект | Назначение |
|---|---|
| `src/Doopass.Core` | Store, Fernet/PBKDF2, Settings, StoreBackup, PasswordValidator, AppLogger |
| `src/Doopass.Tui` | Terminal.Gui v2 TUI (не работает на Android — не подключать к мобильному клиенту) |
| `src/Doopass.Cli` | единая точка входа: без аргументов — TUI; `serve [--port N] [--no-browser] [--lan]` — Blazor Server (порт по умолчанию 7240) |
| `tests/Doopass.Core.Tests`, `tests/Doopass.Tui.Tests` | xUnit |

**Целевая идея проекта**: сервер запущен на домашнем ПК и хранит единственный vault;
телефон/планшет/другие ПК подключаются к нему по локальной сети как **тонкие клиенты**.
Веб-версия (Blazor Server) уже работает по LAN. Нужен Android-клиент, который:
подключается к API хоста, умеет находить хост в LAN и полностью повторяет
функциональность веб-версии. Локального хранилища на телефоне нет — телефон хранит
только адрес хоста, отпечаток его TLS-сертификата и токен сессии.

**Ключевой существующий фасад** `src/Doopass.Cli/Services/DoopassSession.cs` —
per-circuit сессия над хранилищем. Его публичная поверхность (это контракт будущего API):

- `Settings Settings` (`src/Doopass.Core/Configuration/Settings.cs`):
  `Sections`, `GetSection(section)`, `GetValue(section,key)`,
  `ModifyProperty(section,key,value)`, `RestoreDefaults()`;
  пути `StoreFile`, `LogFile`, `BackupsDir`, `BackupExtension`; плейсхолдер `APPDATA/`.
- `StoreBackup Backups` (`src/Doopass.Core/Store/StoreBackup.cs`):
  `GetBackupNames()` — имена **без расширения**, новейшие первыми;
  `MakeBackup()`; `SwapStoreWithBackup(backupName)` — имя с расширением, сперва
  создаёт safety-backup; `DeleteBackup(backupName)`; `Reload` сессии перечитывает store с диска.
- Сама сессия: `IsUnlocked`, `StoreExists`, `Login(password) → (bool, string)`,
  `Init(password) → (bool, string)` (внутри валидирует пароль `PasswordValidator`),
  `Logout()`, `Reload()`, `GetKeys()`, `GetValue(key)`, `Add(key,value)`,
  `Update(oldKey,newKey,newValue)` (реализовано как Remove+Add), `Remove(key)`.
- Исключения из `Doopass.Core/Exceptions.cs`: `IncorrectPasswordError`,
  `PropertyAlreadyExistError`, `PropertyDoesNotExistError`, `StoreIsNotInitializedError`.

**Компоненты Blazor** (`src/Doopass.Cli/Components/`): `Pages/` — `Login`, `InitStore`,
`Store`, `Backups`, `Settings`, `NotFound`, `Error`; `Layout/` — `MainLayout`, `AuthLayout`;
`Ui/` — `Icon`, `Modal`, `ToastHost`, `ThemeToggle`; плюс `App.razor`, `Routes.razor`,
`_Imports.razor`. Сервис `ToastService` (`Services/ToastService.cs`).
Все страницы инжектят `DoopassSession` **синхронно**: например `Store.razor` вызывает
`Session.GetKeys()`, `@Session.GetValue(key)` в разметке, `Session.Add/Update/Remove`;
`Backups.razor` — `Session.Backups.*` и `Session.Settings.BackupExtension`;
`Settings.razor` — `Session.Settings.Sections/GetSection/ModifyProperty/RestoreDefaults`.

**wwwroot** (`src/Doopass.Cli/wwwroot/`): `app.css`, `theme.js`, `app.js` (+ `pwa/` — манифест,
service worker, иконки; PWA-ассеты остаются серверными и в клиент не переезжают).
Все текстовые ассеты встроены как EmbeddedResource с LogicalName `Doopass.Cli.wwwroot.*`
и раздаются вручную через `ServeAsset(...)` в `Program.cs` (ради single-file publish).

## 1. Правила работы

1. **Не менять** формат `store.enc`, `Doopass.Tui`, поведение веб-версии и TUI для
   конечного пользователя. Весь существующий код и тесты остаются зелёными:
   `dotnet build Doopass.slnx` и `dotnet test` после каждой фазы.
2. Стиль кода как в репозитории: file-scoped namespaces, `sealed` классы, English
   comments и UI-строки, без лишних NuGet-зависимостей в Core.
3. Новый код разрешено добавлять только в: новый `Doopass.WebUi` (RCL),
   новый `Doopass.Mobile` (Android), новый `Doopass.Api.Tests`, `src/Doopass.Cli/Api/*`
   и минимальные правки `Program.cs`, `DoopassSession`, компонентов (переезд в RCL + async).
4. Работай фазами из раздела 2. Каждая фаза имеет критерии приёмки — не переходи
   к следующей, пока не выполнены, и фиксируй результат (что сделано, что проверено).
5. Обработка ошибок API: HTTP-статусы `400` (валидация), `401` (неверный пароль /
   просроченный токен), `404` (ключ/бэкап не найден), `409` (конфликт, ключ уже есть),
   `429` (rate limit). Тела ошибок: `{ "error": "<message>" }`.
6. **Коммиты — твоя ответственность: коммить по ходу работы, не копи всё в один коммит**:
   - стиль — Conventional Commits на английском (как в истории репозитория):
     `refactor`, `feat`, `test`, `docs`, `build` со scope по фазе:
     `refactor(ui): extract Doopass.WebUi RCL`, `feat(api): add token auth endpoints`,
     `feat(mobile): add RemoteVaultSession`, `test(api): cover backup restore flow`,
     `docs: document Android thin client`;
   - гранулярность: один логически завершённый шаг — один коммит; внутри фазы
     нормально несколько коммитов (например, в Фазе 1: перенос компонентов в RCL →
     введение `IVaultSession` → async-рефакторинг страниц); после выполнения
     критериев приёмки фазы — коммит фазы обязателен;
   - коммитить только при зелёных `dotnet build Doopass.slnx` и `dotnet test`;
   - в коммиты не должны попадать артефакты сборки (`bin/`, `obj/`, `dist/`,
     `.pwa-assets/`) — проверь/дополни `.gitignore` перед первым коммитом;
   - **никогда не коммитить секреты**: TLS `.pfx` и пароли к нему, сгенерированные
     токены, `settings.json` с реальными путями/адресами хоста.

## 2. Целевая архитектура

```
Хост (ПК, Windows/Linux)                      Телефон (Android)
┌────────────────────────────────┐            ┌─────────────────────────────┐
│ doopass serve --lan [--tls]    │            │ Doopass.Mobile (MAUI)       │
│  ├─ Blazor Server UI (браузер) │            │  ├─ Doopass.WebUi (RCL)     │
│  ├─ REST /api/* ←── HTTP/JSON ──────────────┼──┤  RemoteVaultSession      │
│  ├─ SignalR /hubs/vault ←─ WS ──────────────┼──┤  VaultPushClient         │
│  ├─ UDP :7241 discovery ←─ UDP ─────────────┼──┤  HostDiscovery           │
│  └─ ApiVaultCoordinator        │            │  └─ Connect.razor (+уникальные
│     (единственный Store, токены)│            │      страницы клиента)      │
└────────────────────────────────┘            └─────────────────────────────┘
```

Структура после рефакторинга:

```
src/
├── Doopass.Core/           # без изменений
├── Doopass.Tui/            # без изменений
├── Doopass.WebUi/          # НОВОЕ: Razor Class Library (Components + wwwroot + IVaultSession)
├── Doopass.Cli/            # TUI + Blazor Server + /api/* (ссылки: Core, Tui, WebUi)
└── Doopass.Mobile/         # НОВОЕ: net10.0-android MAUI (ссылки: Core, WebUi)
tests/
├── Doopass.Core.Tests/     # без изменений
├── Doopass.Tui.Tests/      # без изменений
└── Doopass.Api.Tests/      # НОВОЕ: интеграционные тесты REST API (xUnit)
```

Ссылки: `Doopass.Mobile` **не ссылается** на `Doopass.Cli` (иначе утянет TUI).

---

## Фаза 1. RCL `Doopass.WebUi` + `IVaultSession` + async-рефакторинг страниц

### 1.1 Создать RCL

- `src/Doopass.WebUi/Doopass.WebUi.csproj` — SDK `Microsoft.NET.Sdk.Razor`,
  TFM `net10.0`, ссылка на `Doopass.Core`, AssemblyName `Doopass.WebUi`.
- Перенести туда `Components/**` и `ToastService.cs`. Пространства имён заменить
  `Doopass.Cli.Components...` → `Doopass.WebUi.Components...`, `Doopass.Cli.Services`
  → `Doopass.WebUi.Services`; обновить `_Imports.razor`.
- **Ассеты**: перенести в csproj RCL те же EmbeddedResource-декларации с новыми
  LogicalName: `app.css`, `theme.js`, `app.js` → `Doopass.WebUi.wwwroot.app.css` и т.д.
  PWA-ассеты (манифест, sw.js, иконки, apple-touch-icon) и `blazor.web.js`
  (из NuGet) **остаются в Cli** — это серверные вещи.
- В `Cli.Program.cs` метод `ServeAsset` начинает читать ресурсы из сборки RCL
  (`typeof(Doopass.WebUi.Components.App).Assembly`), URL-роуты и LogicalName для
  `blazor.web.js` не меняются. Поведение single-file publish сохраняется.
- `Doopass.Cli` добавляет ProjectReference на `Doopass.WebUi` и регистрирует
  компоненты из неё (`MapRazorComponents<Doopass.WebUi.Components.App>()`).

### 1.2 Ввести контракт `IVaultSession`

Файл `src/Doopass.WebUi/Abstractions/IVaultSession.cs` (весь асинхронный, так как
мобильная реализация ходит по HTTP; локальная реализация оборачивает синхронные
вызовы в `Task.FromResult`):

```csharp
namespace Doopass.WebUi.Abstractions;

public sealed record VaultState(bool StoreExists, bool IsUnlocked, string ServerVersion);

public interface IVaultSession
{
    ValueTask<VaultState> GetStateAsync(CancellationToken ct = default);
    ValueTask<(bool Success, string Message)> LoginAsync(string password, CancellationToken ct = default);
    ValueTask<(bool Success, string Message)> InitAsync(string password, CancellationToken ct = default);
    ValueTask LogoutAsync(CancellationToken ct = default);
    ValueTask ReloadAsync(CancellationToken ct = default);

    ValueTask<IReadOnlyList<string>> GetKeysAsync(CancellationToken ct = default);
    ValueTask<string> GetValueAsync(string key, CancellationToken ct = default);
    ValueTask AddAsync(string key, string value, CancellationToken ct = default);
    ValueTask UpdateAsync(string oldKey, string newKey, string newValue, CancellationToken ct = default);
    ValueTask RemoveAsync(string key, CancellationToken ct = default);

    // имена бэкапов БЕЗ расширения; расширение сервер знает сам
    ValueTask<IReadOnlyList<string>> GetBackupNamesAsync(CancellationToken ct = default);
    ValueTask CreateBackupAsync(CancellationToken ct = default);
    ValueTask RestoreBackupAsync(string backupName, CancellationToken ct = default);
    ValueTask DeleteBackupAsync(string backupName, CancellationToken ct = default);

    ValueTask<IReadOnlyList<(string Section, IReadOnlyDictionary<string, string> Pairs)>> GetSettingsAsync(CancellationToken ct = default);
    ValueTask SetSettingAsync(string section, string key, string value, CancellationToken ct = default);
    ValueTask RestoreDefaultSettingsAsync(CancellationToken ct = default);
}
```

Замечания:
- `Login`/`Init` возвращают тот же кортеж `(bool, string)` — компоненты уже так их используют.
- Бэкапы: контракт в терминах имён **без расширения**; `Backups.razor` перестаёт
  использовать `Session.Settings.BackupExtension` (сервер сам строит имена файлов).
- `StoreExists`/`IsUnlocked` заменяются одним `GetStateAsync()`.

### 1.3 Реализовать интерфейс локально

`DoopassSession` реализует `IVaultSession` (существующая синхронная логика без
изменений, методы-обёртки). Инъекции в компонентах меняются с
`@inject DoopassSession Session` на `@inject IVaultSession Session`
(в `MainLayout`, `Store`, `Login`, `InitStore`, `Backups`, `Settings`).

### 1.4 Async-рефакторинг страниц

Синхронные вызовы становятся async (паттерн для каждой страницы):

- `Store.razor`: ключи загружаются в `OnInitializedAsync` в поле `_keys`;
  значения получают **лениво и кэшируют в компоненте**: при `ToggleReveal(key)` —
  `await Session.GetValueAsync(key)` и положить в `_revealedValues[key]`; при
  скрытии — удалить из кэша (секрет не держится в DOM лишний раз). В разметке
  `@Session.GetValue(key)` → `@_revealedValues[key]`. Так же в `BeginEdit`
  (`_formValue = await Session.GetValueAsync(key)`), Copy, Count.
- `Login.razor`, `InitStore.razor`: хендлеры становятся `async Task`,
  `await Session.LoginAsync(...)`, состояние «store exists» — через `GetStateAsync()`.
- `Backups.razor`: `Refresh()` → async, `await Session.CreateBackupAsync()` и т.д.;
  убрать `Session.Settings.BackupExtension`.
- `Settings.razor`: один `await Session.GetSettingsAsync()` в `OnInitializedAsync`
  вместо цикла по `Settings.Sections`; изменение и restore-defaults — async.
- `MainLayout.razor`: logout — async; если в layout была индикация unlocked — через `GetStateAsync()`.

### 1.5 Критерии приёмки Фазы 1

- `dotnet build Doopass.slnx` и `dotnet test` — зелёные.
- TUI работает как раньше; `doopass serve` открывает веб-версию с идентичным
  поведением (ручная проверка: логин, CRUD пар, бэкапы, настройки, темы).
- Веб-версия по-прежнему публикуется single-file (`scripts/publish-portable`).

---

## Фаза 2. REST API + discovery + SignalR hub на хосте

Весь новый код — `src/Doopass.Cli/Api/`. Регистрация в `Program.cs` (serve-режим):
`app.MapDoopassApi()`, `app.MapHub<VaultHub>("/hubs/vault")`, discovery как
`IHostedService`. JSON: camelCase, дефолтные опции ASP.NET Core.

### 2.1 Сессии API: `ApiVaultCoordinator`

Ключевое решение — **координатор держит единственный разблокированный `Store`
на всех токенов** (устраняет гонки двух «параллельных» хранилищ над одним файлом):

```csharp
public sealed class ApiVaultCoordinator
{
    // единственный Store, пока жива хотя бы одна сессия; под lock
    private Store? _store; private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, ApiSession> _sessions = new(); // token -> session
}
public sealed class ApiSession(string token, DateTimeOffset lastSeenUtc); // sliding TTL
```

- `LoginAsync(password)`: `Store.TryInitializeStore(password, settings.StoreFile)`;
  успех → положить/обновить Store в координаторе, выдать токен
  (32 байта `RandomNumberGenerator` → base64url), зарегистрировать сессию.
- Токен никто, кроме владельца, не должен знать: ответ только по HTTPS/локальной сети.
- TTL (sliding, по умолчанию 30 мин, переопределяется секцией `api` в settings.json:
  `session_ttl_minutes`; при чтении использовать дефолт, если секции нет —
  **не менять** embedded DefaultConfig.json). Истечение/Logout → удалить токен;
  когда сессий не осталось → отбросить Store (= relock) и broadcast `vaultLocked`.
- `GET /api/vault/state` анонимен: `{storeExists, unlocked, serverVersion}` —
  нужен странице логина/инициализации (не раскрывает секретов).

### 2.2 Эндпоинты (auth = `Authorization: Bearer <token>`, кроме `/api/vault/state`)

| Метод и путь | Тело | Ответ / ошибки |
|---|---|---|
| `POST /api/auth/login` | `{password}` | `200 {token, serverVersion}`; `401` неверный пароль |
| `POST /api/auth/init` | `{password}` | `200 {token, serverVersion}`; `400` сообщение валидатора; `409` уже существует |
| `POST /api/auth/logout` | — | `204`; убивает только этот токен |
| `GET /api/vault/state` | — | `200 {storeExists, unlocked, serverVersion}` |
| `GET /api/vault/keys` | — | `200 {keys: [...]}` |
| `GET /api/vault/value?key=X` | — | `200 {value}`; `404` |
| `POST /api/vault/pairs` | `{key, value}` | `204`; `409` ключ уже есть |
| `PUT /api/vault/pairs` | `{oldKey, newKey, newValue}` | `204`; `404`/`409` (newKey занят) |
| `DELETE /api/vault/pairs?key=X` | — | `204`; `404` |
| `POST /api/vault/reload` | — | `204` — перечитать store с диска |
| `GET /api/backups` | — | `200 {names: [...]}` (без расширения) |
| `POST /api/backups` | — | `204` — создать |
| `POST /api/backups/restore?name=X` | — | `204` (сервер сам делает safety-backup и reload) |
| `DELETE /api/backups?name=X` | — | `204`; `404` |
| `GET /api/settings` | — | `200 {sections: [{name, pairs}]}` |
| `PUT /api/settings/{section}/{key}` | `{value}` | `204` |
| `POST /api/settings/restore-defaults` | — | `204` |

- Ключи и значения могут содержать `/`, `=`, unicode — поэтому везде **query-параметры
  или тело JSON, никогда не path-сегменты** для ключей/имён бэкапов.
- На эндпоинты секретов добавить `Cache-Control: no-store` (middleware/фильтр на группу).
- Аутентификация — свой минимальный endpoint-filter/политика `Bearer` из
  `ApiVaultCoordinator` (не cookie — CSRF не применим к bearer-токенам; antiforgery
  на API не распространять).
- Маппинг ошибок: `PropertyAlreadyExistError` → 409, `PropertyDoesNotExistError` → 404,
  `IncorrectPasswordError` → 401, `StoreIsNotInitializedError` → 409/400, валидатор → 400.
- **Rate limit** на `login`/`init`: `AddRateLimiter`, fixed window ~5 запросов/мин на IP → `429`.
- Настройки `PUT /api/settings/*` и `POST restore-defaults` — только при
  unlocked-сессии (как сейчас в `Settings.razor`).
- После успешных мутаций: пары → событие `vaultChanged`; бэкапы → `backupsChanged`;
  настройки → `settingsChanged` (см. 2.3).

### 2.3 SignalR hub `/hubs/vault` (`Api/VaultHub.cs`)

- Клиент передаёт токен через `access_token` (стандартный `WithAccessTokenProvider`);
  в `OnConnectedAsync` валидировать, при отказе — disconnect. Группа = токен.
- События (camelCase): `vaultChanged` (broadcast всем, источник не важен — клиент
  перечитывает ключи), `backupsChanged` (всем), `settingsChanged` (всем),
  `vaultLocked` (только группе затронутого токена).
- Чтобы браузерная Blazor Server-версия тоже генерировала события: добавить
  singleton `IVaultChangeNotifier` (`Api/VaultChangeNotifier.cs`), инъекцировать его
  в `DoopassSession` и вызывать после `Add/Update/Remove/MakeBackup/SwapStore/
  DeleteBackup/ModifyProperty/RestoreDefaults`; hub подписывается на тот же сервис.
  Так изменения из веба/ТUI-потока API видны на телефоне и наоборот.
- Взаимная согласованность: после restore/удаления бэкапа **координатор сам делает
  `Reload`** своего Store; Blazor-сессии на других клиентах обновятся по событию
  (перечитывают `GetKeys`).

### 2.4 Discovery (UDP broadcast)

`Api/DiscoveryService.cs` — `IHostedService`:
- UDP-сокет на порту `7241` (`IPAddress.Any`), включается в `--lan`-режиме
  (или настройкой `api.announce`).
- На ASCII-датаграмму `DOOPASS_DISCOVER` отвечает JSON:
  `{"service":"doopass","name":"<hostname>","port":7240,"tls":<bool>,
    "fingerprint":"<sha256-hex|null>","version":"5.0.0"}`.
- Не отвечать, если порт web-server'а ещё не поднят/иная настройка —
  т.е. слушать announce-порт только при живом сервере.
- Задокументировать в README: правило Windows Firewall для входящего UDP 7241
  (добавить в `scripts/install-*.ps1|.sh`, где это уместно).

### 2.5 TLS (опция, рекомендуется)

- `serve --tls <путь-к-pfx> [--tls-pass <пароль>]` (или `api.tls_pfx` в settings.json):
  Kestrel слушает `https://0.0.0.0:7240`; при старте в лог печатается
  SHA-256-отпечаток сертификата (hex) — им клиент будет pin-нить.
- Discovery при этом отвечает `tls:true, fingerprint:<hex>`.
- Без TLS — работает как сейчас (plain HTTP), клиент будет предупреждать.

### 2.6 Критерии приёмки Фазы 2

- Новый проект `tests/Doopass.Api.Tests` (xUnit): вынести сборку web-host из
  `Program.cs` в тестируемую фабрику (например `internal static WebApplication
  BuildWebHost(int port, bool lan, ...)`) и тестировать на `127.0.0.1:0`.
  Обязательные кейсы: init→login→401 при неверном пароле; CRUD пар (включая 409/404);
  restore бэкапа меняет ключи и пишет safety-backup; settings round-trip;
  logout → 401 на всех защищённых эндпоинтах; TTL-истечение (подкрутить часы/таймаут);
  rate limit 429; формат discovery-ответа.
- Ручная проверка: `curl`-сценарий полного цикла; веб-версия не изменилась.

---

## Фаза 3. Android-клиент `Doopass.Mobile`

### 3.1 Проект

- `src/Doopass.Mobile/Doopass.Mobile.csproj`: TFM `net10.0-android`,
  `UseMaui=true`, `SingleProject=true` (только Android; iOS/десктоп — не в этой итерации).
  Пакет: `Microsoft.AspNetCore.SignalR.Client` (версия под .NET 10).
- `Platforms/Android/AndroidManifest.xml`: `INTERNET`,
  `android:usesCleartextTraffic="true"` (для LAN-HTTP; HTTPS-режим рекомендуется —
  оставить разрешение, но в UI предупреждать при http).
- Иконка приложения: из `.pwa-assets/icon-512.png` (+ maskable) в
  `Platforms/Android/Resources` — так же, как PWA-иконки генерит `scripts/generate-icons.py`.

### 3.2 Хостинг Blazor

- `MauiProgram.cs`:

```csharp
builder.Services.AddSingleton<ConnectionStore>();   // адрес хоста, pin, токен (SecureStorage)
builder.Services.AddSingleton<HostDiscovery>();     // UDP broadcast-клиент
builder.Services.AddSingleton<VaultPushClient>();   // SignalR
builder.Services.AddScoped<IVaultSession, RemoteVaultSession>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddMaui(...);
builder.Services.AddBlazorWebView();
```

  В Hybrid scoped-сервисы живут на всё приложение (нет circuit'ов) — для телефона
  это корректно: одна сессия на приложение. В логах не забыть `AppLogger`
  инициализировать по пути песочницы (`FileSystem.AppDataDirectory`).
- `wwwroot/index.html` (в Mobile-проекте): `<div id="app">`,
  `<link href="_content/Doopass.WebUi/app.css">`,
  `<script src="_content/Doopass.WebUi/theme.js"></script>` (ранний выбор темы),
  обязательный `<script src="_framework/blazor.webview.js" autostart="false"></script>`.
- Корневой роутер: `Routes.razor` из RCL должен получить параметр
  `AdditionalAssemblies` (Router это умеет) со сборкой Mobile, чтобы
  находились страницы обеих сборок. Клиентская страница `Connect.razor`
  (`@page "/connect"`, layout `ConnectLayout`) лежит **в Mobile-проекте**.
- Стартовый маршрут: если подключение не сконфигурировано/недоступно — редирект на `/connect`.

### 3.3 `RemoteVaultSession : IVaultSession`

- `HttpClient` c базовым адресом из `ConnectionStore` и с `ServerCertificateCustomValidationCallback`,
  который при HTTPS сверяет SPKI/сертификат с запиненным отпечатком (см. 3.4);
  при HTTP — обычный обработчик (и отсутствие pin не ошибка).
- Каждый метод интерфейса → соответствующий вызов API (таблица 2.2);
  маппинг статусов в исключения Core: 409 → `PropertyAlreadyExistError`,
  404 → `PropertyDoesNotExistError`, 401 → разлогинить сессию (сбросить токен)
  и вернуть `(false, message)` для login, для остальных — кинуть
  `UnauthorizedAccessException`-подобное поведение, согласованное с компонентами
  (проверь, как страницы обрабатывают ошибки, и сохрани их UX).
- Кортежи `(Success, Message)` для Login/Init: `401` → `(false, "Incorrect password!")`,
  `400` → `(false, <текст валидатора>)` — как в локальной версии.
- Секретные ответы не логировать и не кэшировать.

### 3.4 Безопасность клиента

- `ConnectionStore`: host URL, схема, pinned fingerprint, токен — в
  `Microsoft.Maui.Essentials.SecureStorage` (Android Keystore).
- Первый запуск: `/connect` → discovery (см. 3.5) или ручной ввод → probe
  `GET /api/vault/state` → показать fingerprint (для TLS) → «Доверять и подключиться»
  → сохранить pin → дальше `/login` / `/init` / `/store` по состоянию.
- Повторный запуск: есть токен → проверить его `GET /api/vault/state` с auth
  (или лёгкий пинг) → живой токен → сразу `/store`; `401` → стереть токен, `/connect`→`/login`.
- При plain HTTP показывать явное предупреждение «подключение без шифрования (LAN)».

### 3.5 Discovery на телефоне

`HostDiscovery`:
- UDP broadcast `DOOPASS_DISCOVER` на `255.255.255.255:7241`; собирать ответы ~1.5 c,
  дедуплицировать по host:port; перед `ReceiveAsync` брать
  `WifiManager.MulticastLock` (разрешение `CHANGE_WIFI_MULTICAST_STATE`).
- Результат — список `{name, url, tls, fingerprint, version}` для `/connect`.
- В эмуляторе Android UDP broadcast через NAT не проходит — там работает ручной ввод,
  адрес хоста из эмулятора: `10.0.2.2:7240` (упомянуть в README).

### 3.6 Push-обновления `VaultPushClient`

- `HubConnection` с `WithUrl("<host>/hubs/vault")` и `WithAccessTokenProvider`
  (токен из `ConnectionStore`); авто-reconnect c backoff; события:
  `vaultChanged` → обновить открытый `Store`/`Backups`/`Settings` экран
  (см. 1.4 кэши — подписка страниц через scoped-сервис-релей `IVaultEvents`:
  страница подписывается в `OnInitialized`, отписывается в `Dispose`);
  `vaultLocked` → сброс токена и навигация на `/login`;
  `backupsChanged`/`settingsChanged` → обновить соответствующие экраны.

### 3.7 Навигация и UX

- Android back: оставить дефолт BlazorWebView (back в истории WebView), убедиться,
  что с `/connect`→back приложение не закрывается неожиданно; при необходимости
  переопределить `OnBackPressed` в MainActivity.
- Строки UI — English (как в веб-версии); типографика/темы наследуются от RCL CSS.
- Copy-to-clipboard: `navigator.clipboard` в Hybrid WebView ненадёжен (после `await`
  за чтением секрета user activation уже потерян, а `app.js` в mobile-хостинг не
  подключён), поэтому используется `ISecretClipboard` → нативный MAUI `Clipboard`;
  веб-хост копирует через тот же интерфейс, но реализацией `JsClipboard`.

### 3.8 Критерии приёмки Фазы 3

- Сборка: `dotnet build src/Doopass.Mobile/Doopass.Mobile.csproj` (debug) — без ошибок.
- На устройстве/эмуляторе: discovery находит хост; ручной ввод работает; вход по
  мастер-паролю; полный CRUD пар; copy; бэкапы; настройки; relock по TTL и по
  событию `vaultLocked`; темы; поворот экрана не теряет состояние раскрытых значений
  неожидаемым образом.

---

## Фаза 4. Хардening, упаковка, документация

1. **Release-публикация Android**: `dotnet publish src/Doopass.Mobile -c Release
   -f net10.0-android -p:AndroidPackageFormat=aab` (для ручной установки на тест —
   `-p:AndroidPackageFormat=apk`). Проверить trimming: если роняет reflection
   (System.Text.Json/SignalR) — добавить `TrimmerRootDescriptor.xml`. Тестировать
   именно Release-сборку на реальном устройстве.
2. **Хост**: добавить в `scripts/package-release` и README раздел «API и клиенты»:
   эндпоинты кратко, флаги `--tls`, `--announce` (если вынесены), UDP 7241,
   fingerprint-подтверждение на клиенте, путь для AAB/APK.
3. **README**: новый раздел «Android thin client» (установка, подключение, discovery,
   предупреждение про HTTP/TLS) и обновлённый раздел PWA (не конфликтует: PWA — для
   браузера, native-клиент — полноценное приложение).
4. Финальный ручной E2E-чеклист (зафиксировать результаты):
   - ПК `doopass serve --lan`; телефон: discovery → подключение → логин → CRUD;
   - изменение пары на телефоне появляется в браузере без перезагрузки (SignalR), и наоборот;
   - restore бэкапа на телефоне: браузер видит изменения; safety-backup создан;
   - `G`-lock в TUI (если TUI пишет через DoopassSession с нотификатором — иначе
     TTL) или TTL-истечение → телефон на `/login`;
   - logout на телефоне → 401 на все запросы; попытки логина >5/мин → 429;
   - TLS-режим: fingerprint совпадает с тем, что печатает хост; подмена сертификата
     (другой self-signed) → клиент отвергает подключение.
5. Все тесты зелёные: `dotnet test`.

## 5. Не-цели (не делать в этой итерации)

- Локальный vault на телефоне, офлайн-кэш секретов, облачная синхронизация.
- iOS/macOS/Windows-цели MAUI (архитектура их допускает, но не реализуем).
- Изменение формата хранилища или обратная несовместимость `settings.json`
  (новую секцию `api` читать опционально, дефолты — в коде).
- Веб-авторизация через API для браузерной версии (Blazor Server остаётся как есть).
- gRPC/OTel/прочая инфраструктура — сознательно не вводить.

## 6. Порядок старта (для исполнителя)

1. Прочитай: `README.md`, `src/Doopass.Cli/Program.cs`,
   `src/Doopass.Cli/Services/DoopassSession.cs`, `src/Doopass.Cli/Components/Pages/*.razor`,
   `src/Doopass.Core/Store/Store.cs`, `StoreBackup.cs`, `Configuration/Settings.cs`,
   `Exceptions.cs`, csproj-файлы.
2. Выполняй фазы 1 → 2 → 3 → 4 строго по порядку, с критериями приёмки каждой.
3. После каждой фазы: `dotnet build Doopass.slnx && dotnet test`, коммит фазы
   (см. правило 6 в разделе 1) и краткий отчёт.
