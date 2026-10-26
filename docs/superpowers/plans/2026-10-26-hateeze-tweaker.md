# hateeze Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Собрать hateeze — твикер Windows для игр (C# .NET 10 + WebView2, интерфейс liquid glass в серебре), с мониторингом задержек, каталогом проверенных твиков с откатом, установщиками драйверов и приложений, пресетами, бенчмарком и автообновлением.

**Architecture:** WinForms-окно без рамки хостит WebView2. Интерфейс (React + TypeScript + Vite) встроен в exe и общается с ядром через JSON-RPC поверх `postMessage`. Ядро `Hateeze.Core` — DI-контейнер, в котором функции разделов (`IFeatureModule`) и исполнители действий (`IActionExecutor`) находятся сканированием сборки. Поэтому каждый раздел живёт в своей папке, и общие файлы при добавлении раздела не трогаются.

**Tech Stack:** .NET 10 (net10.0-windows, x64), WinForms, Microsoft.Web.WebView2, Microsoft.Extensions.DependencyInjection, Microsoft.Diagnostics.Tracing.TraceEvent, xUnit; Node 24, Vite, React 19, TypeScript, lucide-react, @fontsource/jetbrains-mono.

> **Поправки от 2026-10-26.** `docs/DECISIONS.md` и `docs/catalog-format-v1.md` важнее этого плана там, где они расходятся. Главное: интерфейс на Svelte 5, а не React (Task 5 переписать перед выполнением); формат каталога и типы `TweakDef`/`TweakAction` (Task 3) — по `catalog-format-v1.md` (`kind: info`, `options` вместо `Range`, `{{nvidiaGpuIndex}}`, `nic` с `op: reset`); горячая клавиша — двойное нажатие; адрес интерфейса — `https://app.hateeze.example/`.

**Spec:** `docs/superpowers/specs/2026-10-26-hateeze-tweaker-design.md` — читать вместе с этим планом. Исследования с точными значениями: `docs/research/*.md` и `docs/research/*.tweaks.json`. Утверждённый дизайн: `design/mockup-overview.html`.

## Global Constraints

- Платформа: Windows 10 22H2+ и Windows 11 23H2/24H2/25H2, только x64. `TargetFramework` = `net10.0-windows`.
- Приложение всегда работает с правами администратора (`requireAdministrator` в манифесте). В интерфейсе это никак не обозначается.
- Язык интерфейса — только русский. Технические названия (DPC, ISR, MSI, MMCSS, HAGS) не переводятся.
- **Минимум текста.** Подписи в одно-два слова. Описания твиков — только в раскрывающейся строке. Никаких «Администратор», подсказок горячих клавиш, номеров версий в шапке.
- Палитра — серебро на тёмном, токены точно как в спецификации §11 и `design/mockup-overview.html`. Цветные только статусы: ok `#6ee7a8`, warn `#f5c451`, bad `#f47a8c`.
- Всё офлайн: никаких CDN. Шрифты и иконки в бандле.
- **Данные приложения:** `%ProgramData%\hateeze\` (`AppPaths.Root`, в тестах подменяется). Профиль WebView2: `%LOCALAPPDATA%\hateeze\WebView2`.
- Ничего не менять в системе машины разработчика. Тесты работают только с поддельными платформенными сервисами. Ручной запуск exe — только для чтения.
- Запрещённые цели (`SafetyGuard`): службы и драйверы vgc, vgk, FACEIT, FACEITService, EasyAntiCheat, EasyAntiCheat_EOS, BEService, BEDaisy, а также список критичных служб из `docs/research/services-tasks-privacy-cleanup.md`.
- Текст ошибок для пользователя — по-русски. Технические детали — в `%ProgramData%\hateeze\logs\`.
- Коммиты — маленькие, сообщение на английском в стиле `feat(scope): …`, с трейлером `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Откат после частичного сбоя.** Если твик из трёх действий падает на втором, первое должно быть восстановлено, а пользователь видит, что именно не применилось. Тест: `TweakEngineTests.Apply_FailsMidway_RollsBackEarlierActions` (Task 3).
2. **Значение отсутствовало до твика.** Если ключа или значения реестра раньше не было, откат должен его удалить, а не записать 0. Тест: `TweakEngineTests.Revert_ValueAbsentBefore_DeletesValue` (Task 3).
3. **DWORD больше `int.MaxValue`** (например, NetworkThrottlingIndex = 4294967295) записывается и читается без переполнения. Тест: `RegistryValueTests.Dword_Max_RoundTrips` (Task 2).
4. **Задача или служба отсутствует в этой сборке Windows.** Твик показывается как «Не найдено» и не ломает пакетное применение остальных. Тест: `TweakEngineTests.Status_MissingTask_IsUnsupported` (Task 3).
5. **Импорт чужого пресета с запрещёнными или неизвестными целями** не выполняет их и показывает, что отклонено. Тест: `PresetImportTests.Import_RejectsForbiddenAndUnknown` (Task 15).

---

## Часть A. Фундамент (последовательно)

### Task 1: Репозиторий, решение и сборка

**Files:**
- Create: `.gitignore`, `.gitattributes`, `Directory.Build.props`, `hateeze.sln`
- Create: `src/Hateeze.Core/Hateeze.Core.csproj`, `src/Hateeze.App/Hateeze.App.csproj`, `tests/Hateeze.Core.Tests/Hateeze.Core.Tests.csproj`
- Create: `build.ps1`

**Interfaces:**
- Produces: проекты `Hateeze.Core` (library, `AllowUnsafeBlocks`), `Hateeze.App` (WinExe, `UseWindowsForms`, ссылается на Core), `Hateeze.Core.Tests` (xUnit, ссылается на Core). `Directory.Build.props`: `net10.0-windows`, `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`, `PlatformTarget=x64`, `RuntimeIdentifier=win-x64` для App.
- Produces: `build.ps1 [-SkipTests] [-Version x.y.z]` → `web` npm ci/build → копия `web/dist` в `src/Hateeze.App/wwwroot` (EmbeddedResource) → `dotnet test` → `dotnet publish` single-file self-contained → `dist/hateeze.exe` + `dist/hateeze.exe.sha256`.

- [ ] Step 1: `git init -b main`. `.gitignore` для bin/obj/node_modules/dist/wwwroot/.vs/*.user.
- [ ] Step 2: создать три проекта и решение. `dotnet build hateeze.sln`. Ожидается: Build succeeded.
- [ ] Step 3: commit `chore: solution scaffold`.

### Task 2: Платформенные абстракции и реальные реализации

**Files:**
- Create: `src/Hateeze.Core/Platform/IRegistry.cs`, `WindowsRegistry.cs`, `RegValue.cs`
- Create: `src/Hateeze.Core/Platform/IServiceManager.cs`, `WindowsServiceManager.cs`
- Create: `src/Hateeze.Core/Platform/IScheduledTasks.cs`, `WindowsScheduledTasks.cs`
- Create: `src/Hateeze.Core/Platform/IBcdStore.cs`, `BcdEditStore.cs`
- Create: `src/Hateeze.Core/Platform/IPowerValues.cs`, `PowrProfValues.cs`, `src/Hateeze.Core/Interop/PowrProf.cs`
- Create: `src/Hateeze.Core/Platform/IProcessRunner.cs`, `ProcessRunner.cs`
- Create: `src/Hateeze.Core/Platform/IRestorePoints.cs`, `RestorePoints.cs`
- Create: `src/Hateeze.Core/Platform/ISystemInfo.cs`, `SystemInfo.cs`
- Create: `src/Hateeze.Core/Platform/Http/Downloader.cs`
- Create: `src/Hateeze.Core/AppPaths.cs`, `src/Hateeze.Core/SingletonAttribute.cs`
- Test: `tests/Hateeze.Core.Tests/Fakes/*.cs`, `tests/Hateeze.Core.Tests/RegistryValueTests.cs`, `DownloaderTests.cs`

**Interfaces (Produces — точные сигнатуры):**
```csharp
namespace Hateeze.Core.Platform;
public enum RegHive { HKLM, HKCU }
public enum RegKind { DWORD, QWORD, SZ, EXPAND_SZ, BINARY, MULTI_SZ }
public sealed record RegValue(RegKind Kind, object Data); // Data: uint | ulong | string | byte[] | string[]
public interface IRegistry {
  RegValue? Get(RegHive hive, string path, string name);           // null если ключа/значения нет
  void Set(RegHive hive, string path, string name, RegValue value); // создаёт ключ при необходимости
  void Delete(RegHive hive, string path, string name);              // нет значения → no-op
  bool KeyExists(RegHive hive, string path);
  IReadOnlyList<string> SubKeys(RegHive hive, string path);
  IReadOnlyList<string> ValueNames(RegHive hive, string path);
}
public enum StartType { Boot, System, Automatic, AutomaticDelayed, Manual, Disabled }
public sealed record ServiceInfo(string Name, string DisplayName, StartType StartType, string Status);
public interface IServiceManager {
  ServiceInfo? Get(string name);                 // null — службы нет
  void SetStartType(string name, StartType type); // AutomaticDelayed = Automatic + DelayedAutostart=1
  void Stop(string name, TimeSpan timeout);
  void Start(string name, TimeSpan timeout);
  IReadOnlyList<ServiceInfo> List();
}
public interface IScheduledTasks {
  bool? IsEnabled(string taskPath);           // null — задачи нет
  void SetEnabled(string taskPath, bool enabled);
}
public interface IBcdStore {
  string? Get(string name);                   // из `bcdedit /enum {current}`; null — не задано
  void Set(string name, string value);
  void DeleteValue(string name);
}
public interface IPowerValues {
  Guid ActiveScheme();
  (uint ac, uint dc)? Read(Guid scheme, Guid subgroup, Guid setting);
  void Write(Guid scheme, Guid subgroup, Guid setting, uint ac, uint dc); // + PowerSetActiveScheme для применения
}
public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
public interface IProcessRunner { Task<ProcessResult> RunAsync(string file, string args, TimeSpan timeout, CancellationToken ct = default); }
public sealed record RestorePointResult(bool Ok, string Message, DateTime? CreatedAt);
public interface IRestorePoints { bool IsProtectionEnabled(); Task EnableProtectionAsync(); Task<RestorePointResult> CreateAsync(string description); }
public sealed record GpuInfo(string Name, string Vendor, string DriverVersion, string PnpId); // Vendor: nvidia|amd|intel|other
public sealed record SystemSnapshot(string OsCaption, int Build, string DisplayVersion, bool IsWin11, string CpuName, string CpuVendor, int Cores, int Threads, IReadOnlyList<GpuInfo> Gpus);
public interface ISystemInfo { SystemSnapshot Get(); }   // кэш на время жизни процесса
public sealed record DownloadRequest(Uri Url, string TargetPath, IReadOnlyDictionary<string,string>? Headers = null, string? Sha256 = null);
public sealed class Downloader { // [Singleton]; HttpClient с User-Agent "hateeze/<version>"
  public Task<string> DownloadAsync(DownloadRequest req, IProgress<(long done, long? total)>? progress, CancellationToken ct); // докачка через Range, проверка SHA256, возвращает путь
}
namespace Hateeze.Core;
public static class AppPaths { public static string Root {get; set;} /* %ProgramData%\hateeze */ public static string Logs, Downloads, Presets, Benchmarks; }
[AttributeUsage(AttributeTargets.Class)] public sealed class SingletonAttribute : Attribute {} // авто-регистрация в DI как сам класс и все его интерфейсы
```

- [ ] Step 1: тест `RegistryValueTests.Dword_Max_RoundTrips` на `FakeRegistry`: `Set(HKLM,"A","B",new(RegKind.DWORD, 4294967295u))`, затем `Get` возвращает `uint 4294967295`. Тест `WindowsRegistry_Dword_ConvertsUncheckedInt` проверяет чистую функцию конвертации `RegConvert.ToUInt(int)`/`FromUInt(uint)` (без доступа к реестру).
- [ ] Step 2: реализации. `WindowsRegistry` — `Microsoft.Win32.RegistryKey` с `RegistryView.Registry64`. `WindowsServiceManager` — `ChangeServiceConfig`/`QueryServiceConfig` (advapi32) + `DelayedAutostart` через `ChangeServiceConfig2`. `WindowsScheduledTasks` — COM `Schedule.Service` (late-bound `dynamic`). `BcdEditStore` — разбор вывода `bcdedit /enum {current}` (локализованный вывод: ключи-имена параметров bcdedit на английском и в русской Windows). `PowrProfValues` — `PowerReadACValueIndex`/`PowerReadDCValueIndex`/`PowerWriteACValueIndex`/`PowerWriteDCValueIndex`/`PowerGetActiveScheme`/`PowerSetActiveScheme`. `RestorePoints` — PowerShell `Checkpoint-Computer` через `IProcessRunner`. `SystemInfo` — WMI `Win32_OperatingSystem`/`Win32_Processor`/`Win32_VideoController` + `DisplayVersion` из реестра.
- [ ] Step 3: `FakeRegistry`, `FakeServiceManager`, `FakeScheduledTasks`, `FakeBcdStore`, `FakePowerValues`, `FakeProcessRunner`, `FakeRestorePoints`, `FakeSystemInfo` в тестовом проекте — словари в памяти.
- [ ] Step 4: `DownloaderTests.Resume_UsesRangeHeader` и `Sha256Mismatch_Throws` на подменённом `HttpMessageHandler`.
- [ ] Step 5: `dotnet test` → PASS. Commit `feat(core): platform abstractions`.

### Task 3: Движок твиков, журнал, базовый снимок, защита

**Files:**
- Create: `src/Hateeze.Core/Engine/TweakDef.cs`, `TweakAction.cs`, `Catalog.cs`, `CatalogLoader.cs`
- Create: `src/Hateeze.Core/Engine/IActionExecutor.cs`, `Executors/RegExecutor.cs`, `ServiceExecutor.cs`, `TaskExecutor.cs`, `BcdExecutor.cs`, `PowerExecutor.cs`, `PowerShellExecutor.cs`
- Create: `src/Hateeze.Core/Engine/Journal.cs`, `Baseline.cs`, `SafetyGuard.cs`, `TweakEngine.cs`
- Create: `src/Hateeze.Core/Catalog/` (EmbeddedResource `*.tweaks.json`)
- Test: `tests/Hateeze.Core.Tests/TweakEngineTests.cs`, `CatalogTests.cs`, `JournalTests.cs`, `SafetyGuardTests.cs`

**Interfaces (Produces):**
```csharp
namespace Hateeze.Core.Engine;
public enum Evidence { Proven, Situational, Experimental, Avoid }
public enum Risk { Low, Medium, High }
public enum RebootKind { None, Explorer, Logoff, Reboot }
public enum TweakKind { Toggle, Value, Select }
public sealed record TweakOption(string Label, JsonElement Value);
public sealed record TweakDef(string Id, string Category, string Title, string Description, string? Recommendation,
  Evidence Evidence, string? EvidenceNote, Risk Risk, string? RiskNote, RebootKind Reboot, string[] Os, string? OsNote,
  string Vendor, TweakKind Kind, TweakOption[]? Options, JsonElement? Range, TweakAction[] Apply, TweakAction[] Revert,
  TweakAction? Detect, string[] Sources);
public sealed record TweakAction { // один тип со всеми полями; JSON-имена = camelCase из формата каталога
  public required string Type {get; init;} // reg|bcd|service|task|power|nic|nvdrs|powershell|msi|affinity
  public string? Op, Hive, Path, Name, ValueType, StartType, Subgroup, Setting, Alias, Keyword, Script, Target;
  public JsonElement? Value; [JsonPropertyName("equals")] public JsonElement? EqualsValue;
  public bool? Stop, Enabled; public uint? Ac, Dc, SettingId;
}
public sealed record ActionState(string Target, bool Exists, JsonNode? Data); // Target — ключ цели: "reg:HKLM\\path\\name", "svc:Name", "task:\\path", "bcd:name", "power:sub/setting", "nic:adapter/keyword", "nvdrs:id", "msi:instanceId", "affinity:instanceId"
public interface IActionExecutor {
  string Type {get;}
  string TargetKey(TweakAction a);
  ActionState Read(TweakAction a);                      // текущее состояние цели
  void Apply(TweakAction a);                            // a.Value уже подставлено
  void Restore(TweakAction a, ActionState state);       // вернуть ровно state (Exists=false → удалить/вернуть «нет»)
  bool Matches(TweakAction a, ActionState current);     // цель уже в желаемом состоянии
  bool IsSupported(TweakAction a, out string? reason);  // цель существует в этой системе
}
public enum TweakState { Applied, NotApplied, Partial, Unsupported }
public sealed record TweakStatus(string Id, TweakState State, JsonNode? Value, string? Reason);
public sealed record ItemResult(string Id, bool Ok, string? Error);
public sealed record BatchResult(IReadOnlyList<ItemResult> Results, bool RebootRequired);
public sealed class Catalog { IReadOnlyList<TweakDef> All; TweakDef? Find(string id); IEnumerable<TweakDef> Visible(ISystemInfo sys); } // Visible: без Avoid, подходит по os/vendor
public sealed class TweakEngine { // [Singleton]
  TweakStatus GetStatus(string id);
  BatchResult Apply(IReadOnlyList<(string id, JsonElement? value)> items);
  BatchResult ApplyActions(string sourceId, IReadOnlyList<TweakAction> actions); // для динамических разделов (MSI, NIC, MMCSS, питание)
  BatchResult Revert(IReadOnlyList<string> ids);
  BatchResult RevertTargets(string sourceId, IReadOnlyList<TweakAction> actions); // откат динамических целей к baseline
  BatchResult RevertAll();
  Task<RestorePointResult?> EnsureSnapshotAsync(); // точка восстановления + baseline (один раз)
  bool RebootRequired {get;}
}
public sealed class SafetyGuard { bool IsForbidden(TweakAction a, out string reason); } // [Singleton]
```
Правила движка (из спецификации §4):
- **Перед первым изменением** — `EnsureSnapshotAsync`.
- **Baseline.** Хранит первое увиденное состояние каждой цели, ключ — `TargetKey`. Цель, не попавшая в baseline при снимке, добавляется туда при первом касании. Файл `AppPaths.Root\baseline.json`.
- **Применение одного элемента:**
  - для каждого действия проверить `SafetyGuard` → `Read` (до) → `Apply` → `Read` (после) → `Matches`;
  - при ошибке или несовпадении — `Restore` уже выполненных действий этого элемента в обратном порядке, элемент `Ok=false`;
  - каждое действие пишется в `journal.jsonl` строкой `{ts, kind:"apply"|"revert", source, target, before, after, ok, error}`.
- **Revert твика.** Для каждой цели из `Apply`: если цель есть в baseline — `Restore(baseline)`, иначе выполнить `Revert`-действия каталога.
- **RevertAll.** Каждая цель, встречавшаяся в журнале, восстанавливается к baseline, в порядке, обратном первому касанию.
- **Value/Select.** Строка `"{{value}}"` в `Value` действий заменяется переданным значением. Текущее значение — `Read` первого такого действия.
- **Статусы.** `Unsupported` — если `IsSupported=false` хоть у одного действия (reason «Не найдено»). `Partial` — часть совпадает.

- [ ] Step 1: тесты (FakeRegistry и др.):
  - `Apply_Then_Revert_RestoresExactBytes` — исходное `DWORD 2` → apply 38 → revert → снова `DWORD 2`;
  - `Revert_ValueAbsentBefore_DeletesValue`;
  - `Apply_FailsMidway_RollsBackEarlierActions` — второй executor бросает исключение, первое значение восстановлено, `Ok=false`;
  - `Status_MissingTask_IsUnsupported`;
  - `ValueTweak_SubstitutesPlaceholder`;
  - `RevertAll_RestoresBaselineInReverseOrder`;
  - `ForbiddenService_IsRejected` — действие над `vgc` даёт `Ok=false`, служба не тронута.
- [ ] Step 2: `CatalogTests` загружает все встроенные `*.tweaks.json` и проверяет:
  - JSON валиден, id уникальны;
  - у каждого не-Avoid непустые `Apply` и `Revert`;
  - `{{value}}` встречается только при Kind=Value|Select;
  - для каждого `Type` есть зарегистрированный `IActionExecutor` (список типов, которые реализуют разделы, — `nic`, `nvdrs`, `msi`, `affinity` — разрешён в виде whitelist до слияния разделов);
  - ни одно действие не запрещено `SafetyGuard`.
- [ ] Step 3: реализация, `dotnet test` → PASS. Commit `feat(core): tweak engine with journal and baseline`.

### Task 4: RPC, операции, DI и хост WebView2

**Files:**
- Create: `src/Hateeze.Core/Rpc/RpcAttribute.cs`, `IFeatureModule.cs`, `RpcRouter.cs`, `RpcException.cs`, `IEventSink.cs`, `Operations.cs`, `IStartupTask.cs`, `IShutdownTask.cs`
- Create: `src/Hateeze.Core/Hosting/CoreServices.cs` (`AddHateezeCore(IServiceCollection)` — сканирование сборки: `[Singleton]`, `IFeatureModule`, `IActionExecutor`, `IStartupTask`, `IShutdownTask`)
- Create: `src/Hateeze.Core/Features/Core/CoreModule.cs` (RPC `app.*`, `tweaks.*`, `safety.*`, `journal.*`, `system.*`)
- Create: `src/Hateeze.Core/Contracts/Monitoring.cs` — только интерфейсы и records `ILatencyMonitor`, `LatencySummary`, `ModuleLatency`, `LatencySample`, `ISystemCounters`, `CounterSnapshot` (Task 7) и `ITimerService`, `TimerInfo(double CurrentMs, double MinMs, double MaxMs)`, `SleepStats(double AvgMs, double P99Ms, double MaxMs, int[] Histogram)` (Task 8). Реализаций нет — их делают Task 7 и 8.
- Create: `src/Hateeze.App/Program.cs`, `MainForm.cs`, `Bridge/WebBridge.cs`, `Bridge/EmbeddedAssets.cs`, `Native/Dwm.cs`, `Native/Hotkey.cs`, `app.manifest`, `hateeze.ico`
- Test: `tests/Hateeze.Core.Tests/RpcRouterTests.cs`, `OperationsTests.cs`

**Interfaces (Produces):**
```csharp
namespace Hateeze.Core.Rpc;
[AttributeUsage(AttributeTargets.Method)] public sealed class RpcAttribute(string method) : Attribute { public string Method => method; }
public interface IFeatureModule {}  // класс с методами [Rpc]; сигнатура метода: (TParams? p, CancellationToken ct) | (TParams p) | () → T | Task<T> | Task
public sealed class RpcException(string code, string message, string? details = null) : Exception(message) { public string Code => code; public string? Details => details; }
public interface IEventSink { void Emit(string evt, object? data); }
public sealed class OperationContext { public string Id {get;} public CancellationToken Token {get;} public void Report(string stage, double? percent = null, string? message = null); }
public sealed class Operations { // [Singleton]
  public string Start(string kind, Func<OperationContext, Task<object?>> work); // события op.progress {operationId, stage, percent, message}, op.done {operationId, ok, result, error}
  public bool Cancel(string id);
}
public sealed class RpcRouter { public Task<object?> InvokeAsync(string method, JsonElement? @params, CancellationToken ct); } // неизвестный метод → RpcException("E_METHOD", "Неизвестная команда")
public interface IStartupTask { Task RunAsync(CancellationToken ct); }  // после загрузки интерфейса
public interface IShutdownTask { void Shutdown(); }                      // при выходе и при падении
```
- JSON: `JsonSerializerOptions` с `JsonNamingPolicy.CamelCase`, `JsonStringEnumConverter(camelCase)`, reflection-based (trimming выключен).
- **Встроенные RPC (CoreModule):**

| Метод | Параметры | Результат |
|---|---|---|
| `app.info` | — | `{version, system: SystemSnapshot}` |
| `tweaks.list` | `{category?, evidence?: string[]}` | `TweakView[]` = поля TweakDef для UI (без apply/revert) + `status` |
| `tweaks.apply` | `{items:[{id, value?}]}` | `BatchResult` |
| `tweaks.revert` | `{ids}` | `BatchResult` |
| `tweaks.revertAll` | — | `BatchResult` |
| `tweaks.search` | `{query}` | `[{id, title, category}]` |
| `safety.status` | — | `{baselineAt?, restorePoint?, journalCount, rebootRequired, protectionEnabled}` |
| `safety.snapshot` | — | `RestorePointResult` |
| `safety.enableProtection` | — | — |
| `journal.list` | `{limit}` | записи журнала |
| `system.reboot` | `{delaySec}` | `shutdown /r /t N` |
| `system.openUrl` | `{url}` | — |

- **Методы окна** обрабатывает `WebBridge` сам, без Core: `window.minimize`, `window.toggleMaximize`, `window.close`, `window.startDrag` (ReleaseCapture + WM_NCLBUTTONDOWN/HTCAPTION).
- **Окно:**
  - WinForms без рамки, минимум 1100×760, скругления Win11 (`DWMWA_WINDOW_CORNER_PREFERENCE=2`), фон `#060708`, ресайз по краям через WM_NCHITTEST;
  - WebView2: UserDataFolder `%LOCALAPPDATA%\hateeze\WebView2`; ассеты из EmbeddedResource `wwwroot/**` через `WebResourceRequested` на `https://app.hateeze/*`;
  - в Release DevTools и контекстное меню выключены;
  - при отсутствии WebView2 Runtime — диалог со ссылкой на Evergreen Bootstrapper.
- **Горячая клавиша:** `RegisterHotKey(MOD_CONTROL|MOD_ALT|MOD_SHIFT, 'R')` → событие `hotkey.reset`.
- **Падение:** `AppDomain.UnhandledException` → лог и все `IShutdownTask`.

- [ ] Step 1: тесты `RpcRouterTests.Routes_ParamsAndResult`, `Unknown_Throws_E_METHOD`, `RpcException_PropagatesCode`; `OperationsTests.Cancel_StopsWork_EmitsDoneWithCancelled`.
- [ ] Step 2: реализация Core-части. `dotnet test` → PASS.
- [ ] Step 3: App-часть. `dotnet build` → succeeded. Commit `feat(app): webview2 host and rpc bridge`.

### Task 5: Веб-оболочка и дизайн-система

**Files:**
- Create: `web/package.json`, `vite.config.ts`, `tsconfig.json`, `index.html`, `eslint.config.js`
- Create: `web/src/main.tsx`, `src/app/App.tsx`, `src/app/Titlebar.tsx`, `src/app/Sidebar.tsx`, `src/app/router.ts`, `src/app/pages.ts`, `src/app/CommandPalette.tsx`, `src/app/RebootBanner.tsx`, `src/app/ResetCountdown.tsx`
- Create: `web/src/api/bridge.ts`, `src/api/types.ts`, `src/api/mock/index.ts`, `src/api/mock/core.ts`
- Create: `web/src/ui/tokens.css`, `glass.css`, `Backdrop.tsx`, `GlassPanel.tsx`, `Button.tsx`, `Toggle.tsx`, `Slider.tsx`, `NumberField.tsx`, `Select.tsx`, `Segmented.tsx`, `Chip.tsx` (EvidenceChip, RiskBadge), `Table.tsx`, `Modal.tsx` (+ `confirm()`), `Toast.tsx` (+ `toast()`), `Progress.tsx`, `EmptyState.tsx`, `PageHeader.tsx`, `LineChart.tsx`, `Sparkline.tsx`, `Ring.tsx`, `TweakRow.tsx`, `TweakList.tsx`, `hooks.ts` (`useRpc`, `useEvent`, `useOperation`), `index.ts`
- Create: `web/src/pages/_template/` (пример страницы — не попадает в меню)

**Interfaces (Produces):**
```ts
// api/bridge.ts
export function call<T = unknown>(method: string, params?: unknown): Promise<T>   // ошибка → throw RpcError {code, message, details}
export function on(event: string, handler: (data: any) => void): () => void
export const isHost: boolean                                                       // !!window.chrome?.webview; иначе работают моки
// api/mock — каждая страница кладёт src/pages/<id>/mock.ts: export const mocks: Record<string, (params: any) => any>
//            и при необходимости export const mockEvents: (emit: (evt, data) => void) => () => void
// app/pages.ts — import.meta.glob('../pages/*/index.tsx', {eager:true}); каждая страница:
export interface PageMeta { id: string; title: string; icon: LucideIcon; group: 'monitor'|'tweaks'|'install'|'system'; order: number }
export const meta: PageMeta;   export default function Page(): JSX.Element;
// ui/TweakList.tsx — общий список твиков категории:
export function TweakList(props: { category?: string; evidence?: Evidence[]; ids?: string[]; title?: string }): JSX.Element
//   загружает tweaks.list, строка = TweakRow (иконка, название, EvidenceChip, значок перезагрузки, Toggle|NumberField|Select), клик → раскрытие (description, recommendation, riskNote, evidenceNote, sources);
//   «отложенные» изменения копятся локально; кнопки PageHeader: «Рекомендуемое», «Отменить», «Применить» → tweaks.apply / tweaks.revert; тост с итогом.
// hooks.ts
export function useRpc<T>(method: string, params?: unknown, deps?: unknown[]): { data?: T; error?: RpcError; loading: boolean; reload(): void }
export function useEvent(event: string, handler: (d: any) => void): void
export function useOperation(): { start(method: string, params?: unknown): Promise<string>; progress?: OpProgress; done?: OpDone; cancel(): void }
```
- **Меню:** порядок и иконки из спецификации и макета. Группы разделены тонкими линиями без подписей; раздел «Видеокарта» единый.
- **Роутинг:** `#/<pageId>`, по умолчанию `overview`.
- **Ctrl+K:** палитра ищет по названиям страниц и `tweaks.search`. Карта категория→страница: `power→power, cpu→cores, timer→timer, scheduler→scheduler, input→input, network→network, graphics|gaming→gaming, nvidia|amd→gpu, services→services, tasks→tasks, privacy→privacy, cleanup→cleanup`. Твики с `experimental` ведут на `experimental`.
- **`hotkey.reset`:** модалка с отсчётом 10 с и кнопкой «Отмена». По окончании — `tweaks.revertAll`, затем `system.reboot {delaySec:0}`.
- **Шапка окна:** `mousedown` по пустому месту → `window.startDrag`; двойной клик → `window.toggleMaximize`.
- **Стили:** перенести из макета токены, `.glass`, `.liquid` (SVG-фильтр `#lg-refract` в `index.html`), фон с бликами и зерном, кнопки, переключатель, чипы. Ничего не придумывать сверх макета.

- [ ] Step 1: `npm create vite` (react-ts), зависимости (`lucide-react`, `@fontsource/jetbrains-mono`), `npm run build` → OK.
- [ ] Step 2: дизайн-система и оболочка. `npx tsc --noEmit` и `npx eslint .` → 0 ошибок.
- [ ] Step 3: проверка в браузере на моках (`npm run dev`): оболочка совпадает с макетом. Commit `feat(web): shell and design system`.

### Task 6: Импорт каталога из исследования

**Files:**
- Create: `src/Hateeze.Core/Catalog/<category>.tweaks.json` — по одному на категорию: power, cpu, timer, scheduler, input, network, gaming, graphics, nvidia, amd, services, tasks, privacy
- Create: `tools/split-catalog.mjs`

- [ ] Step 1: скрипт берёт `docs/research/*.tweaks.json` (после проверки скептиками) и раскладывает записи по категориям. `experimental` остаётся в своей категории (страница «Эксперимент» фильтрует по evidence).
- [ ] Step 2: `dotnet test --filter CatalogTests` → PASS. Расхождения форматов исправить в JSON, не в тесте. Commit `feat(catalog): import verified tweak catalog`.

---

## Часть B. Разделы (параллельно, каждый в своём git worktree)

Общие правила для каждой задачи части B:
- Агент владеет только своими папками: `src/Hateeze.Core/Features/<Feature>/`, своими `Catalog/<category>.tweaks.json`, `web/src/pages/<id>/`, `tests/Hateeze.Core.Tests/<Feature>/`. Общие файлы фундамента не правит. Если нужно расширение фундамента — описать в отчёте.
- Каждая страница: `index.tsx` (+ `meta`), `mock.ts` с правдоподобными данными. Страница должна выглядеть как макет: стекло, серебро, минимум текста.
- Каждый модуль C#: `[Singleton]`-сервисы, `IFeatureModule` с `[Rpc]`. Платформенный доступ — через интерфейсы, которые тестируются фейками.
- Готово = `dotnet build`, `dotnet test`, `npx tsc --noEmit`, `npx eslint .`, `npm run build` проходят. Плюс скриншот страницы на моках. Плюс коммит в ветку worktree.
- Источники значений — только `docs/research/*.md`. Если там «НЕ ПРОВЕРЕНО», в UI это помечается как experimental или не включается.

### Task 7: Мониторинг задержек, «Обзор», «Задержки»
- **Владеет:** `Features/Monitoring/`, `pages/overview/`, `pages/latency/`.
- **Реализует (интерфейсы — в `src/Hateeze.Core/Contracts/Monitoring.cs`, создаёт фундамент в Task 4):**
```csharp
public interface ILatencyMonitor { bool IsRunning {get;} string? LastError {get;} void Start(); void Stop(); void Reset();
  LatencySummary GetSummary(); IReadOnlyList<ModuleLatency> GetModules(); IReadOnlyList<LatencySample> GetSeries(TimeSpan window); }
public sealed record LatencySummary(bool Running, string? Error, double MaxDpcUs, double AvgDpcUs, double MaxIsrUs, double AvgIsrUs, long DpcCount, long IsrCount,
  string? WorstModule, double WorstUs, int Modules, int ProblemModules, double ElapsedSec, string Rating, int Score);
public sealed record ModuleLatency(string Module, double CurrentUs, double MaxUs, double AvgUs, long DpcCount, long IsrCount, string Level); // Level: ok|warn|bad
public sealed record LatencySample(double T, double MaxDpcUs, double MaxIsrUs);
public interface ISystemCounters { CounterSnapshot Get(); }
public sealed record CounterSnapshot(double InterruptPct, double DpcPct, double PageFaultsPerSec, double HardFaultsPerSec, double DiskLatencyMs, double[] CoreUsage, double[] CoreMhz, bool[] CoreParked);
```
- **ETW:** TraceEvent kernel session `hateeze-kernel` (или NT Kernel Logger, если своё имя недоступно — по исследованию). Рейтинг и балл — по шкале спецификации §8.
- **RPC:** `monitor.start|stop|reset|summary|modules|series`, `monitor.subscribe|unsubscribe`, событие `monitor.tick` (2 Гц: summary + последний sample + counters). `overview.recommendations` — до 4 не применённых `proven`+`low` твиков, подходящих системе.
- **Тесты:**
  - `LatencyAggregatorTests.MaxAndAvg_PerModule`;
  - `Rating_Thresholds` (99→Отлично, 100→Хорошо, 499→Норма, 999→Плохо, 1000→Критично);
  - `ModuleMapping_ResolvesAddressToDriver`.

### Task 8: Ядра CPU, таймер, бенчмарк
- **Владеет:** `Features/Cpu/`, `Features/Timer/`, `Features/Benchmark/`, `pages/cores/`, `pages/timer/`, `pages/benchmark/`, `Catalog/timer.tweaks.json`, `Catalog/cpu.tweaks.json`.
- **Реализует `ITimerService`** (интерфейс в Contracts):
  - `TimerInfo Query()` → (CurrentMs, MinMs, MaxMs);
  - `void Hold(bool on, double ms = 0.5)`;
  - `SleepStats MeasureSleep(int samples)` → (AvgMs, P99Ms, MaxMs, Histogram).
  - Потребляет `ILatencyMonitor` и `ISystemCounters`.
- **RPC:**
  - `cpu.topology`;
  - `cpu.subscribe` (событие `cpu.tick` с CoreUsage/CoreMhz/CoreParked);
  - `timer.query|hold|measure`;
  - `bench.start {seconds, label}` (операция), `bench.list`, `bench.compare {a, b}`, `bench.delete`. Результат хранится в `AppPaths.Benchmarks`.
- **Тесты:**
  - `BenchCompare_PercentDelta` (лучше — отрицательная задержка → зелёный);
  - `SleepStats_P99`.

### Task 9: Прерывания (MSI и привязка)
- **Владеет:** `Features/Interrupts/`, `pages/interrupts/`.
- **Executors:** `msi` (Target=InstanceId; Value `{enabled, limit?}`), `affinity` (Target=InstanceId; Value `{policy, mask?, priority?}`) — запись в `Enum\<InstanceId>\Device Parameters\Interrupt Management\...` точно по исследованию.
- **RPC:**
  - `irq.devices` → `[{instanceId, name, class, mode: msi|line, msiSupported, msiLimit, priority, policy, mask, danger?: string}]`;
  - `irq.input` → USB-контроллеры мыши и клавиатуры;
  - `irq.apply {items}` → через `TweakEngine.ApplyActions`.
- Опасные устройства (по исследованию) — `danger` и двойное подтверждение.
- **Тесты:**
  - `AffinityMask_Encoding` (ядро 0 → `01 00 00 00 00 00 00 00`, ядра 2,3 → `0C …`);
  - `MsiExecutor_RoundTrip`.

### Task 10: Электропитание
- **Владеет:** `Features/Power/`, `pages/power/`, `Catalog/power.tweaks.json`.
- **Функции:** схемы (список, активировать, дублировать, импорт .pow, экспорт, удалить не встроенную), полное описание параметров активной схемы (включая скрытые) через PowerEnumerate/PowerRead*, запись AC/DC, создание «hateeze Low Latency» по исследованию.
- **RPC:** `power.schemes|activate|duplicate|import|export|delete|describe {scheme}|write {items}|createHateeze`.
- Запись параметров идёт через `TweakEngine.ApplyActions` действиями `power`, чтобы попасть в журнал. Действие `power` получает необязательный `scheme`; без него — активная схема. Расширение `PowerExecutor` под `scheme` описать в отчёте, если в фундаменте его нет.
- **Тест:** `HateezeScheme_Values_MatchResearch` — набор GUID/значений из исследования.

### Task 11: Сеть
- **Владеет:** `Features/Network/`, `pages/network/`, `Catalog/network.tweaks.json`.
- **Executor `nic`:** Target = GUID адаптера + RegistryKeyword. Запись через ключ класса `{4d36e972-…}\000X` и перезапуск адаптера (или `Set-NetAdapterAdvancedProperty`).
- **RPC:**
  - `net.adapters` — физические адаптеры, их расширенные свойства с вариантами (`DisplayValue`/`RegistryValue`) и рекомендацией;
  - `net.setAdvanced {items}`;
  - `net.dns.get|set {preset: cloudflare|google|quad9|auto}`;
  - `net.flushDns`, `net.resetWinsock`.
- **Тест:** `NicRecommendation_ForRealtekEee_IsOff`.

### Task 12: Планировщик, ввод, игровой режим
- **Владеет:** `Features/Scheduler/`, `pages/scheduler/`, `pages/input/`, `pages/gaming/`, `Catalog/scheduler.tweaks.json`, `input.tweaks.json`, `gaming.tweaks.json`, `graphics.tweaks.json`.
- **Страницы:**
  - `input` и `gaming` — `TweakList` по категориям;
  - `scheduler` — глобальные параметры MMCSS, таблица задач MMCSS (редактор значений с дефолтами из исследования, запись через `ApplyActions` reg-действиями) и «Приоритет игр» (IFEO PerfOptions: добавить exe, CPU/IO приоритет).
- **Мышь:** после изменения параметров — `SystemParametersInfo` (SPI_SETMOUSE/SPI_SETMOUSESPEED), чтобы применилось без перелогина. Это отдельный `IActionExecutor` типа `spi` или пост-хук — по исследованию; описать в отчёте.
- **RPC:** `mmcss.get|set`, `ifeo.list|set|remove`.
- **Тест:** `Mmcss_Defaults_MatchResearch`.

### Task 13: Процессы
- **Владеет:** `Features/Processes/`, `pages/processes/`.
- **RPC:**
  - `proc.list` → `[{pid, name, path, cpu, memMb, priority, affinityMask, isGameGuess}]`, CPU% считается по разнице `TotalProcessorTime`;
  - `proc.setPriority`, `proc.setAffinity`.
- Для «Постоянного приоритета» используется `ifeo.set` из Task 12. Кнопка ведёт на страницу «Планировщик».
- **Тест:** `CpuPercent_FromTwoSamples`.

### Task 14: Видеокарта (NVIDIA / AMD)
- **Владеет:** `Features/Gpu/`, `pages/gpu/`, `Catalog/nvidia.tweaks.json`, `amd.tweaks.json`.
- **NVAPI DRS:** загрузка `nvapi64.dll`, `nvapi_QueryInterface`, структуры и ID — строго из `docs/research/nvidia-drs-tweaks.md` (проверено скептиком).
- **Executor `nvdrs`:** глобальный профиль. Read → значение или «по умолчанию»; Restore → `RestoreProfileDefaultSetting`, если раньше значение было дефолтным.
- **RPC:** `gpu.info`, `gpu.nv.settings` (список настроек с текущими значениями и вариантами), `gpu.nv.set {items}`, `gpu.nv.resetAll`, `gpu.nv.profiles` (поиск профиля игры по exe), `gpu.nv.setForApp {exe, items}`.
- Без NVIDIA — только AMD-твики. Без обоих — EmptyState «Видеокарта не поддерживается».
- **Тест:** `NvDrsSetting_StructSize_MatchesVersion` (размер структуры = значение из исследования).

### Task 15: Службы, задачи, приватность, очистка, эксперимент, пресеты
- **Владеет:** `Features/Cleanup/`, `Features/Presets/`, `pages/services/`, `pages/tasks/`, `pages/privacy/`, `pages/cleanup/`, `pages/experimental/`, `pages/presets/`, `Catalog/services.tweaks.json`, `tasks.tweaks.json`, `privacy.tweaks.json`.
- **Страницы:**
  - `services`, `tasks`, `privacy` — `TweakList`. Для служб дополнительно показывать текущий и дефолтный тип запуска;
  - `experimental` — `TweakList evidence=['experimental']` со всеми категориями.
- **Очистка:** категории из исследования.
  - RPC `clean.scan` → `[{id, title, bytes, files, needsClose?: string}]`, `clean.run {ids}` (операция) → `{freedBytes, errors}`;
  - Prefetch не трогать;
  - занятые файлы пропускать.
- **Пресеты:**
  - встроенные `gaming`, `gaming-plus`, `privacy` (§9);
  - файл `.hateeze.json` = `{format:"hateeze-preset", version:1, name, items:[{id, value?}], dynamic?:{msi:[{hardwareId, enabled, limit?}], affinity:[...], ifeo:[...], power?:{...}}}`;
  - RPC `presets.list|apply {id}|export {name, includeCurrent}|import {json}` → `{preview:{apply:[], skipped:[{id, reason}]}}`, `presets.applyImported {json}`;
  - импорт отклоняет неизвестные id, запрещённые цели и любые скрипты.
- **Тесты:** `PresetImportTests.Import_RejectsForbiddenAndUnknown`, `Cleanup_SkipsPrefetch`.

### Task 16: Установщик драйверов
- **Владеет:** `Features/Drivers/`, `pages/drivers/`.
- **NVIDIA** (по `docs/research/nvidia-driver-installer.md`):
  - определение GPU, список версий (Game Ready / Studio), выбор версии;
  - загрузка через `Downloader`, распаковка 7-Zip (установленный, иначе загрузка 7zr/7za по исследованию);
  - выбор компонентов (галочки), патч `setup.cfg`/`*.nvi`, тихая установка (чистая — опция);
  - пост-опции: телеметрия, MSI, HDCP, запрет драйверов через Windows Update.
- **AMD Radeon:** полный пакет / только драйвер. **AMD Chipset.**
- **DDU:** загрузка, запуск в безопасном режиме, гарантированный возврат обычной загрузки.
- **RPC:** `drivers.detect`, `drivers.nv.versions`, `drivers.nv.components {version}`, `drivers.nv.install {version, components, clean, post}` (операция), `drivers.amd.latest`, `drivers.amd.install {mode}` (операция), `drivers.chipset.latest|install`, `drivers.ddu.run {vendor}` (операция с подтверждением).
- **Тесты:** на фикстурах (сохранённые ответы API, фрагменты setup.cfg) — `NvLookup_ParsesVersions`, `SetupCfg_RemovesExcludedComponents`, `AmdUrl_RequiresReferer`, `Ddu_ArgsForNvidia`.

### Task 17: Установщик приложений
- **Владеет:** `Features/Apps/`, `pages/apps/`.
- **Каталог приложений:** `Features/Apps/apps.json` — id, название, категория (игры / браузеры / утилиты), иконка, URL, ключи тихой установки, winget id, детект установленного — строго из проверенного исследования.
- **RPC:** `apps.list` (со статусом «установлено / версия»), `apps.install {ids}` (операция по очереди; при ошибке загрузки — winget как запасной путь), `apps.valorant.region {region}`.
- **Тесты:** `AppDetect_FromUninstallKey`, `Install_FallsBackToWinget_OnDownloadError`.

### Task 18: Настройки и автообновление
- **Владеет:** `Features/Update/`, `Features/Settings/`, `pages/settings/`.
- **Обновление:** GitHub Releases (`UpdateOptions.Repo` = `"<owner>/hateeze"`, задаётся константой), проверка раз в сутки при старте (`IStartupTask`), SHA256, замена через переименование и перезапуск.
- **Настройки:** папка данных, журнал (просмотр и экспорт), точка восстановления, «о программе».
- **RPC:** `update.check`, `update.install` (операция), `settings.get|set`, `journal.export`.
- **Тесты:** `SemVer_Compare`, `ReleaseAsset_Selection`.

---

## Часть C. Сборка, ревью, выпуск

### Task 19: Слияние и интеграция
- [ ] Слить ветки Task 7–18 в `main` по одной. После каждой: `dotnet build`, `dotnet test`, `npm run build`.
- [ ] Удалить whitelist из `CatalogTests` (Task 3): к этому моменту все executors зарегистрированы.
- [ ] Проверить, что сканирование DI находит все модули: тест `CoreServicesTests.AllRpcMethodsUnique`.

### Task 20: Ревью и исправления
- [ ] Многоаспектное ревью: корректность, безопасность системы (откат, запрещённые цели), соответствие исследованию, UI против макета, ресурсы (утечки ETW-сессий, хэндлов).
- [ ] Каждую находку проверить отдельным скептиком и только подтверждённые исправить. Повторять, пока проход не будет пустым.

### Task 21: Проверка на машине (только чтение) и сборка
- [ ] `build.ps1` → `dist/hateeze.exe`. Запустить и пройти все разделы: данные читаются, ничего не применяется. Сделать скриншоты.
- [ ] Записать, что проверено, а что требует ручной проверки пользователем (применение твиков, установка драйверов).

### Task 22: GitHub, установщик, описание
- **Установщик:** `installer/hateeze.iss` (Inno Setup). Ставит в `%ProgramFiles%\hateeze`, создаёт ярлык в меню «Пуск» и на рабочем столе (опционально) и деинсталлятор. Рядом — portable exe.
- **CI:** `.github/workflows/release.yml` — на тег `v*`: сборка web, тесты, publish, Inno Setup. В релиз уходят `hateeze-setup-<ver>.exe`, `hateeze-<ver>-portable.exe` и `.sha256`.
- **`README.md`** (русский): что умеет, скриншоты, установка, безопасность и откат, сборка из исходников, лицензия.
- **Репозиторий:** создать через `gh repo create` (имя и видимость — согласовать с пользователем), push, первый релиз `v1.0.0`.
