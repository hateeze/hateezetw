# Электропитание, CPU, системный таймер, планировщик — исследование для hateeze

> Ключ темы: `power-cpu-timer-scheduler`. Дата исследования: 2026-10-26.
> Машиночитаемый каталог твиков: `power-cpu-timer-scheduler.tweaks.json` (в этой же папке).
> Все замеры и значения «по умолчанию» в этом документе получены **только командами чтения** на машине разработчика
> (Ryzen 7 5700X, ASRock A520M-HVS, Windows 11 Pro for Workstations 23H2 22631.7652, ru-RU). Ничего в системе не менялось.
> Исключение, о котором нужно знать: для замеров точности `Sleep(1)` тестовый процесс PowerShell временно запрашивал
> разрешение таймера (`timeBeginPeriod`/`NtSetTimerResolution`) и вызывал `SetProcessInformation(ProcessPowerThrottling)`
> **для самого себя** — это действует только на этот процесс и снимается при его завершении (так же делает любая игра/браузер).

---

## 0. Коротко (что реально важно)

1. **Схемы питания.** На Win10/11 «Максимальная производительность» (Ultimate, `e9a42b02-d5df-448d-aa00-03f14749eb61`)
   на питании от сети (AC) **совпадает с «Высокой производительностью»** во всём, кроме «Отключать жёсткий диск через» (0 вместо 1200 с).
   Это проверено на этой машине (Win11 23H2) и по выгрузке djdallmann (Win10 LTSC 2019). Причина: у Ultimate `PERSONALITY` AC = High performance,
   DC = Balanced, а собственных переопределений у шаблона всего одно (`DISKIDLE` = 0/0 в `User\Default\PowerSchemes\e9a42b02…`).
   Значит «магии» в Ultimate нет: минимальное состояние CPU 100 %, парковка ядер выключена, ASPM выключен, AHCI/NVMe без энергосбережения.
2. **В «Сбалансированной» Windows сама поднимает CPU до 100 %, когда нужно.** У Balanced есть PPM-профили `GameMode` и `LowLatency`
   (запуск приложений/загрузка), в которых на этой машине `PROCTHROTTLEMIN` = 100 % (AC и DC). Профили и оверлеи работают **только в схемах,
   производных от Balanced**. Поэтому на AMD Ryzen с двумя CCD и 3D V-Cache (7900X3D/7950X3D/9900X3D/9950X3D) и на Intel hybrid
   (12-е поколение и новее) переход на High/Ultimate/«hateeze Low Latency» может **ухудшить** игры (ломается парковка «лишнего» CCD / подсказки планировщику).
   Для таких CPU схему hateeze не предлагать (или строить её от Balanced) — см. §2.7.
3. **Схема «hateeze Low Latency»** честно = копия Ultimate + выключенные USB selective suspend и USB 3 LPM (+ явная фиксация остальных
   «производительных» значений, чтобы схема была детерминированной на любой машине). Оценка — `situational` (настольный ПК, нет X3D с двумя CCD, нет Intel hybrid).
4. **IDLEDISABLE (отключение C-states)** — Microsoft рекомендует это для soft real-time IoT, но для игр это `experimental` с высоким риском:
   100 % «занятости» в диспетчере задач, постоянный нагрев/потребление, на Ryzen с PBO/Precision Boost — потеря буста на 1–2 ядрах. В «Эксперимент».
5. **Таймер.** С Windows 10 2004 запрос разрешения таймера действует **на процесс**, а не глобально; с Windows 11 — ещё и игнорируется для
   свёрнутых/невидимых окон. На этой машине: глобальное текущее разрешение 1,0 мс (кто-то держит), но процесс без запроса получает `Sleep(1)` ≈ **15,4 мс**.
   `GlobalTimerResolutionRequests=1` (только Win11 и Server 2022+, на Win10 ядро этот ключ не знает) возвращает старое глобальное поведение. Это `experimental`.
   Точные 0,5000 мс дают `Sleep(1)` ≈ 1,50 мс (p50), а 0,507–0,52 мс — ≈ 1,02–1,04 мс (p50) — воспроизведено на этой машине (подтверждает valleyofdoom).
6. **bcdedit.** `useplatformclock`, `useplatformtick`, `disabledynamictick`, `tscsyncpolicy` — по документации Microsoft «только для отладки».
   Форсирование HPET (`useplatformclock yes`) — `avoid`: вызов QPC стоит ~0,8–1,0 мкс вместо ~20–30 нс (на этой машине замерено 23 нс/вызов, т.е. TSC).
   Полезное действие ровно одно: **удалить** `useplatformclock`, если его кто-то включил (`proven`, восстановление дефолта).
7. **Планировщик.** `Win32PrioritySeparation` = 2 по умолчанию; на клиентских Windows 2 ≡ 0x26 (короткий, переменный квант, 3:1).
   Замеров, доказывающих пользу 0x28/0x2A/0x18 для игр, нет — `experimental`. MMCSS влияет **только** на потоки, вызвавшие
   `AvSetMmThreadCharacteristics`; по документации Microsoft `GPU Priority` и `SFIO Priority` **не используются**, `SystemResponsiveness`
   ниже 10 (в т.ч. популярный 0) **зажимается до 20** (= дефолту), а для `Scheduling Category = High` поле `Priority` всегда считается равным 2.
   То есть популярный «твик Games» частично не делает ничего.
8. **PowerThrottlingOff** (политика «Выключить регулирование мощности») — `situational`: помогает фоновым процессам (OBS/запись/стрим/Discord),
   которые Windows переводит в EcoQoS; на саму игру (передний план) не влияет.
9. **Spectre/Meltdown** (`FeatureSettingsOverride=3`) — `avoid`: риск безопасности, на AMD Meltdown неприменим, часть античитов требует митигаций.

---

## 1. Состояние машины разработчика (прочитано командами чтения)

| Что | Значение | Команда |
|---|---|---|
| ОС | Windows 11 Pro for Workstations 23H2, build 22631.7652, OSLanguage 1049 (ru-RU) | `Get-CimInstance Win32_OperatingSystem`, `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion` |
| CPU | AMD Ryzen 7 5700X, 8C/16T, MaxClockSpeed 3401 | `Get-CimInstance Win32_Processor` |
| Драйвер процессора | `cpu.inf` «AMD Processor» 10.0.22621.5624 (inbox), AMD PPM Provisioning Driver **не установлен** | `Get-CimInstance Win32_PnPSignedDriver`, `pnputil /enum-drivers` |
| Возможности PPM (событие Kernel-Processor-Power ID 55) | Idle: **ACPI C-states, 2 состояния**; Performance: **ACPI CPPC** (`PerformanceImplementation=3`); Nominal 3401 МГц; MaxPerf 157 %; MinPerf 51 %; MinThrottle 16 % | `Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='Microsoft-Windows-Kernel-Processor-Power';Id=55}` |
| Схемы питания | Balanced `381b4222…`, **«Ultimate Performance (ExitLag)» `75237865-332a-4023-b48b-ddb3328f7b88` — активна**, High `8c5e7fda…`, Power saver `a1841308…`, Ultimate `e9a42b02…` | `powercfg /l` |
| Пользовательские переопределения | High performance изменена пользователем: `PROCTHROTTLEMIN` AC=10 (сток 100), `DISKIDLE` AC=0 (сток 1200), `VIDEOIDLE` AC=0 (сток 900). ExitLag-схема = копия Ultimate + `RTCWAKE` 0/0 | `HKLM\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\<scheme>\<sub>\<setting>` |
| Скрытые параметры | 124 из 150 параметров (в подгруппах) имеют `Attributes & 1` (скрыты); 5 параметров без подгруппы тоже скрыты. Ничего не раскрыто | `PowerReadSettingAttributes` (см. §2.3) |
| Сон | Доступен только S3; гибернация выключена (`HibernateEnabled=0`, сток 1); Fast Startup выключен (`HiberbootEnabled=0`, сток 1); Modern Standby (S0ix) не поддерживается | `powercfg /a`, `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power` |
| Game Mode | `HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled = 0` (пользователь выключил; сток — включён) | `reg query` |
| PowerThrottling | ключа `HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling` **нет** (сток) | `reg query` |
| Win32PrioritySeparation | `0x2` (сток) | `reg query HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl` |
| Session Manager\kernel | `GlobalTimerResolutionRequests` **отсутствует** (сток Win11 client) | `reg query` |
| MMCSS | драйвер `mmcss.sys`, Start=2 (Auto), RUNNING; значения SystemProfile — §4.2 | `sc qc MMCSS`, `reg query … /s` |
| BCD `{current}` | Нет ни одного из `useplatformclock`, `useplatformtick`, `disabledynamictick`, `tscsyncpolicy` (сток). `hypervisorlaunchtype Auto`, `nx OptIn` | `bcdedit /enum {current}` |
| VBS / гипервизор | `VirtualizationBasedSecurityStatus=2` (VBS работает), `SecurityServicesRunning={0}` (HVCI не работает); корневой раздел Hyper-V активен | `Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard Win32_DeviceGuard` |
| HPET | устройство «Высокоточный таймер событий» `ACPI\PNP0103\2&DABA3FF&0` — OK (в BIOS включён) | `Get-PnpDevice` |
| QPC | `QueryPerformanceFrequency` = 10 000 000 Гц; стоимость вызова `QueryPerformanceCounter` ≈ **23 нс** (P/Invoke из PowerShell, 5·10⁶ вызовов) ⇒ источник TSC, без системного вызова | замер, §3.5 |
| Разрешение таймера | `NtQueryTimerResolution`: Minimum(грубейшее)=156250, Maximum(точнейшее)=5000, Current=10000 (×100 нс) ⇒ 15,625 / 0,5 / 1,0 мс | замер, §3.2 |
| Spectre/Meltdown | `FeatureSettingsOverride`/`Mask` отсутствуют (сток). Есть `FeatureSettings=0x20000` — назначение **НЕ ПРОВЕРЕНО** (не документировано в KB, не трогать) | `reg query "HKLM\…\Memory Management"` |
| Диски / USB / сеть | SATA SSD «P4-240», NVMe «Ardor gaming m.2 1TB», USB-флешка; 2× AMD xHCI USB 3.10; Realtek PCIe GbE | `Get-PhysicalDisk`, `Win32_USBController`, `Get-NetAdapter` |

**Вывод о «чистоте» машины:** электропитание и Game Mode уже трогали (ExitLag, ручные правки High performance, выключенный Game Mode,
выключенные гибернация/Fast Startup). Поэтому дефолты для `revert` в каталоге брались не из «текущих значений активной схемы», а из
**хранилища шаблонов** (`PowerSettings\…\DefaultPowerSchemeValues\<scheme>` + `ProvAcSettingIndex/ProvDcSettingIndex`) и из стоковых схем
(Balanced/Ultimate/Power saver на этой машине не имеют пользовательских переопределений по процессорным параметрам), сверены с выгрузкой Win10 LTSC 2019 (djdallmann).

---
## 2. Электропитание

### 2.1. Как Windows хранит схемы (нужно понимать, чтобы правильно читать «дефолты»)

Все пути — `HKLM\SYSTEM\CurrentControlSet\Control\Power\…`. **Писать сюда напрямую не нужно** — только через API (§2.2) или `powercfg.exe`;
реестр описан, чтобы понимать, откуда берутся значения и что такое «дефолт».

| Узел | Что хранит |
|---|---|
| `PowerSettings\<SubGUID>` | Определение подгруппы: `FriendlyName`, `Description` (MUI-ссылки вида `@%SystemRoot%\system32\powrprof.dll,-NNN,Текст`), `Attributes` |
| `PowerSettings\<SubGUID>\<SettingGUID>` | Определение параметра: `FriendlyName`, `Description`, `Attributes` (DWORD, см. §2.3), для диапазона — `ValueMin`, `ValueMax`, `ValueIncrement`, `ValueUnits`; для перечисления — подключ `PossibleSettingIndex\NNN` с `FriendlyName`/`SettingValue` |
| `PowerSettings\<Sub>\<Setting>\DefaultPowerSchemeValues\<SchemeGUID>` | Дефолт для «личности» схемы: `ACSettingIndex`, `DCSettingIndex`; **`ProvAcSettingIndex`/`ProvDcSettingIndex`** — значение, подставленное провижинингом (ОС/OEM/драйвер CPU) **поверх** дефолта. Пример с этой машины: `PROCTHROTTLEMIN` Balanced: `ACSettingIndex=5`, но `ProvAcSettingIndex=0` ⇒ эффективно AC = 0 % |
| `PowerSettings\<Sub>\<Setting>\<ProfileGUID>\<SchemeGUID>` | Значения PPM-профиля (`ProvAcSettingIndex`/`ProvDcSettingIndex`). На этой машине: профили `GameMode` (`d4140c81-ebba-4e60-8561-6918290359cd`), `LowLatency` (`0da965dc-8fcf-4c0b-8efe-8dd5e7bc959a`), `SustainedPerf` (`0aabb002-a307-447e-9b81-1d819df6c6d0`) для Balanced задают `PROCTHROTTLEMIN` = 100 |
| `User\Default\PowerSchemes\<SchemeGUID>` | Шаблоны «скрытых» схем (Ultimate `e9a42b02…`, оверлеи `3af9b8d9…` High Performance Overlay, `961cc777…` Better Battery-life Overlay, `ded574b5…` Max Performance Overlay) и их переопределения. У Ultimate здесь единственное переопределение: `DISKIDLE` 0/0 |
| `User\PowerSchemes` | `ActivePowerScheme` (REG_SZ GUID) + по подключу на схему: `FriendlyName`, `Description`, и **пользовательские переопределения** `<Sub>\<Setting>\ACSettingIndex/DCSettingIndex` |
| `Profile\Events\{54533251…}\{ProfileGUID}` | Описание PPM-профилей (Name, Pri, условия срабатывания) |
| `HKLM\SOFTWARE\Policies\Microsoft\Power\PowerSettings\<SettingGUID>` (+ HKCU) | Групповая политика: `ACSettingIndex`/`DCSettingIndex` перекрывают любые значения схемы. На этой машине ключа нет |

Порядок вычисления эффективного значения для схемы S: политика → пользовательское переопределение `User\PowerSchemes\S` →
`ProvAc/ProvDc` → `DefaultPowerSchemeValues` для «личности» S (`PERSONALITY`: 0 = Power saver, 1 = High performance, 2 = Balanced;
у Ultimate AC=1, DC=2 — поэтому её AC-значения равны High, а DC — Balanced). Поверх схемы в рантайме действуют PPM-профили и
оверлеи слайдера «Режим питания» — **только для схем, производных от Balanced** (djdallmann, WINPOWER; совпадает с наблюдением:
у Ultimate для `PROFILE_GAMEMODE` AC = «НЕИЗВЕСТНО», у Balanced — 100).

Команды чтения для диагностики (все безопасны): `powercfg /l`, `powercfg /qh [SCHEME]` (h = включая скрытые), `powercfg /aliasesh`,
`powercfg /listprofiles`, `powercfg /queryprofile SCHEME_BALANCED PROFILE_GAMEMODE`, `powercfg /a`.

> **Вывод для приложения.** «Дефолт Windows» для параметра зависит от схемы, от CPU (провижининг) и от версии ОС.
> Поэтому для `revert` любого power-действия приложение **обязано** сначала сделать снапшот текущих AC/DC (через `PowerReadACValueIndex`/
> `PowerReadDCValueIndex`) и при откате восстанавливать снапшот. Значения `revert` в JSON — запасной вариант («Сбалансированная» на этой машине/Win11 23H2).
> **Не использовать** `powercfg /restoredefaultschemes` для отката: он удаляет все пользовательские схемы.

### 2.2. API (powrprof.dll) — сигнатуры и порядок работы

Все функции — `powrprof.dll` (заголовки `powrprof.h` и `powersetting.h`), доступны с Windows Vista. `RootPowerKey`/`UserRootPowerKey`
**всегда NULL** (зарезервировано). Буферы строк — UTF-16 с завершающим нулём, размер в **байтах**. Все функции возвращают Win32-код (0 = успех).
Изменения активной схемы вступают в силу только после `PowerSetActiveScheme` (документировано в Remarks к Power*Write*).

| Задача | Функция | Заметки |
|---|---|---|
| Перечислить схемы | `PowerEnumerate(NULL, NULL, NULL, ACCESS_SCHEME=16, i, buf, &size)` | `buf` = 16 байт (GUID); цикл до `ERROR_NO_MORE_ITEMS` (259) |
| Подгруппы схемы | `PowerEnumerate(NULL, &scheme, NULL, ACCESS_SUBGROUP=17, i, …)` | **Не возвращает** `NO_SUBGROUP_GUID` (`fea3413e-7e05-4911-9a71-700331f1c294`) — проверено: 16 подгрупп, 150 параметров, а `powercfg /qh` показывает 155 |
| Параметры подгруппы | `PowerEnumerate(NULL, &scheme, &sub, ACCESS_INDIVIDUAL_SETTING=18, i, …)` | Для 5 параметров без подгруппы вызывать с `&NO_SUBGROUP_GUID` (CONSOLELOCK, PERSONALITY, DEVICEIDLE, DISCONNECTEDSTANDBYMODE, CONNECTIVITYINSTANDBY) |
| Имя/описание | `PowerReadFriendlyName` / `PowerReadDescription(NULL, scheme, sub, setting, buf, &size)` | Схема: `scheme`≠NULL, `sub`=`setting`=NULL. Подгруппа: `scheme`=NULL, `sub`≠NULL. Параметр: `sub`+`setting`. Возвращают **локализованный** текст (ru-RU). Подгруппа `9596fb26-9850-41fd-ac3e-f7c3c00afd4b` (мультимедиа) возвращает **пустое** имя — нужен фолбэк |
| Диапазон | `PowerIsSettingRangeDefined(&sub,&set)` → `PowerReadValueMin/Max/Increment(NULL,&sub,&set,&v)` + `PowerReadValueUnitsSpecifier(NULL,&sub,&set,buf,&size)` | Единицы локализованы непоследовательно («Сек.», «milliseconds», «%», «Интервалы проверки») — показывать как есть или маппить по своему словарю |
| Варианты | `PowerReadPossibleFriendlyName(NULL,&sub,&set,idx,buf,&size)`, `PowerReadPossibleDescription(…)`, `PowerReadPossibleValue(NULL,&sub,&set,&type,idx,buf,&size)` | Цикл idx=0.. пока не ошибка. На этой машине для всех вариантов `type=4` (REG_DWORD) и value==idx, но **сопоставлять по value**, а не по индексу |
| Ни диапазона, ни вариантов | — | Бывает (например, `HETEROINCREASETHRESHOLD` `b000397d…` — упакованные байтовые пороги). Показывать сырое число/hex, редактирование — только «эксперт» |
| Текущее значение | `PowerReadACValueIndex` / `PowerReadDCValueIndex(NULL,&scheme,&sub,&set,&v)` | Возвращает эффективное значение схемы (с учётом Prov/дефолтов), без учёта PPM-профилей/оверлеев/политики |
| Дефолт «личности» | `PowerReadACDefaultIndex`/`PowerReadDCDefaultIndex(NULL,&personalityScheme,&sub,&set,&v)` | Читает `DefaultPowerSchemeValues`; учитывает ли `ProvAc*` — **НЕ ПРОВЕРЕНО** |
| Атрибуты | `PowerReadSettingAttributes(&sub,&set)` → DWORD; `PowerWriteSettingAttributes(&sub,&set,attr)` | `set`=NULL → атрибуты подгруппы |
| Записать | `PowerWriteACValueIndex`/`PowerWriteDCValueIndex(NULL,&scheme,&sub,&set,value)` | Требует админа. Затем, если `scheme` активна, — `PowerSetActiveScheme(NULL,&scheme)` |
| Активная схема | `PowerGetActiveScheme(NULL, &ptr)` → `LocalFree(ptr)`; `PowerSetActiveScheme(NULL,&scheme)` | |
| Дублировать | `PowerDuplicateScheme(NULL,&src,&pDst)` | Если `*pDst == NULL`, функция выделяет GUID (освободить `LocalFree`). `ERROR_ALREADY_EXISTS` (183) — схема с таким GUID уже есть. Передача заранее заданного GUID через непустой `*pDst` — по смыслу документации так и работает, но **НЕ ПРОВЕРЕНО** (требует записи); надёжный фолбэк — `powercfg /duplicatescheme <SRC> <DST>` (DST поддерживается, см. `powercfg /? DUPLICATESCHEME`) |
| Имя/описание схемы | `PowerWriteFriendlyName`/`PowerWriteDescription(NULL,&scheme,NULL,NULL,buf,bytesInclNull)` | или `powercfg /changename <GUID> "Имя" "Описание"` |
| Удалить | `PowerDeleteScheme(NULL,&scheme)` | Активную схему удалить нельзя — сначала активировать другую |
| Импорт | `PowerImportPowerScheme(NULL, L"C:\\path\\file.pow", &pGuid)` | `powercfg /import file.pow [GUID]` |
| Экспорт | API нет | `powercfg /export file.pow <GUID>` |
| Политика | `HKLM/HKCU\SOFTWARE\Policies\Microsoft\Power\PowerSettings\<SettingGUID>` | При наличии — показывать замок и не давать менять |

#### C# (.NET 10, `AllowUnsafeBlocks=true`, x64) — P/Invoke

```csharp
using System.Runtime.InteropServices;
using System.Text;

namespace Hateeze.Power;

internal static unsafe partial class PowrProf
{
    private const string Dll = "powrprof.dll";

    public const uint ACCESS_SCHEME = 16, ACCESS_SUBGROUP = 17, ACCESS_INDIVIDUAL_SETTING = 18;
    public const uint ERROR_SUCCESS = 0, ERROR_FILE_NOT_FOUND = 2, ERROR_INVALID_PARAMETER = 87,
                      ERROR_ALREADY_EXISTS = 183, ERROR_MORE_DATA = 234, ERROR_NO_MORE_ITEMS = 259;
    public const uint POWER_ATTRIBUTE_HIDE = 0x1, POWER_ATTRIBUTE_SHOW_AOAC = 0x2;

    public static readonly Guid NO_SUBGROUP_GUID = new("fea3413e-7e05-4911-9a71-700331f1c294");
    public static readonly Guid SCHEME_BALANCED  = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid SCHEME_HIGH      = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid SCHEME_SAVER     = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid SCHEME_ULTIMATE  = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    // DWORD PowerEnumerate(HKEY, const GUID*, const GUID*, POWER_DATA_ACCESSOR, ULONG, UCHAR*, DWORD*)
    [LibraryImport(Dll)] public static partial uint PowerEnumerate(nint root, Guid* scheme, Guid* subGroup, uint accessFlags, uint index, byte* buffer, uint* bufferSize);

    [LibraryImport(Dll)] public static partial uint PowerReadFriendlyName(nint root, Guid* scheme, Guid* subGroup, Guid* setting, byte* buffer, uint* bufferSize);
    [LibraryImport(Dll)] public static partial uint PowerReadDescription (nint root, Guid* scheme, Guid* subGroup, Guid* setting, byte* buffer, uint* bufferSize);
    [LibraryImport(Dll)] public static partial uint PowerReadValueMin      (nint root, Guid* subGroup, Guid* setting, uint* value);
    [LibraryImport(Dll)] public static partial uint PowerReadValueMax      (nint root, Guid* subGroup, Guid* setting, uint* value);
    [LibraryImport(Dll)] public static partial uint PowerReadValueIncrement(nint root, Guid* subGroup, Guid* setting, uint* value);
    [LibraryImport(Dll)] public static partial uint PowerReadValueUnitsSpecifier(nint root, Guid* subGroup, Guid* setting, byte* buffer, uint* bufferSize);
    [LibraryImport(Dll)] public static partial uint PowerReadPossibleFriendlyName(nint root, Guid* subGroup, Guid* setting, uint possibleIndex, byte* buffer, uint* bufferSize);
    [LibraryImport(Dll)] public static partial uint PowerReadPossibleDescription (nint root, Guid* subGroup, Guid* setting, uint possibleIndex, byte* buffer, uint* bufferSize);
    [LibraryImport(Dll)] public static partial uint PowerReadPossibleValue(nint root, Guid* subGroup, Guid* setting, uint* type, uint possibleIndex, byte* buffer, uint* bufferSize);
    [LibraryImport(Dll)] [return: MarshalAs(UnmanagedType.U1)] public static partial bool PowerIsSettingRangeDefined(Guid* subGroup, Guid* setting);
    [LibraryImport(Dll)] public static partial uint PowerReadSettingAttributes (Guid* subGroup, Guid* setting);
    [LibraryImport(Dll)] public static partial uint PowerWriteSettingAttributes(Guid* subGroup, Guid* setting, uint attributes);

    [LibraryImport(Dll)] public static partial uint PowerReadACValueIndex (nint root, Guid* scheme, Guid* subGroup, Guid* setting, uint* value);
    [LibraryImport(Dll)] public static partial uint PowerReadDCValueIndex (nint root, Guid* scheme, Guid* subGroup, Guid* setting, uint* value);
    [LibraryImport(Dll)] public static partial uint PowerReadACDefaultIndex(nint root, Guid* personality, Guid* subGroup, Guid* setting, uint* value);
    [LibraryImport(Dll)] public static partial uint PowerReadDCDefaultIndex(nint root, Guid* personality, Guid* subGroup, Guid* setting, uint* value);
    [LibraryImport(Dll)] public static partial uint PowerWriteACValueIndex(nint root, Guid* scheme, Guid* subGroup, Guid* setting, uint value);
    [LibraryImport(Dll)] public static partial uint PowerWriteDCValueIndex(nint root, Guid* scheme, Guid* subGroup, Guid* setting, uint value);

    [LibraryImport(Dll)] public static partial uint PowerGetActiveScheme(nint userRoot, Guid** activePolicyGuid);
    [LibraryImport(Dll)] public static partial uint PowerSetActiveScheme(nint userRoot, Guid* scheme);
    [LibraryImport(Dll)] public static partial uint PowerDuplicateScheme(nint root, Guid* source, Guid** destination);
    [LibraryImport(Dll)] public static partial uint PowerDeleteScheme(nint root, Guid* scheme);
    [LibraryImport(Dll)] public static partial uint PowerWriteFriendlyName(nint root, Guid* scheme, Guid* subGroup, Guid* setting, byte* buffer, uint bufferSize);
    [LibraryImport(Dll)] public static partial uint PowerWriteDescription (nint root, Guid* scheme, Guid* subGroup, Guid* setting, byte* buffer, uint bufferSize);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PowerImportPowerScheme(nint root, string importFilePath, Guid** destination);

    [LibraryImport("kernel32.dll")] public static partial nint LocalFree(nint mem);
}
```

> Сигнатуры чтения (`PowerEnumerate`, `PowerReadFriendlyName`, `PowerReadValueMin/Max/Increment/UnitsSpecifier`, `PowerReadPossibleFriendlyName/Value`,
> `PowerReadAC/DCValueIndex`, `PowerReadSettingAttributes`, `PowerIsSettingRangeDefined`, `PowerGetActiveScheme`) **проверены вызовами на этой машине**
> (эквивалентный `DllImport` через PowerShell `Add-Type`). Сигнатуры записи взяты из документации Microsoft и на машине не вызывались (запрет на изменения).

#### C# — модель и чтение «всех параметров» (как в LatencyLens)

Важно для C#: нельзя брать адрес локальной переменной, захваченной лямбдой (CS1686), и нельзя смешивать `yield` с указателями —
поэтому ниже обычные методы, которые копируют GUID в локальные переменные и берут их адрес внутри себя.

```csharp
public sealed record PowerOption(uint Value, string Name, string? Description);
public sealed record PowerSettingInfo(
    Guid SubGroup, string SubGroupName, Guid Setting, string Name, string? Description,
    uint Attributes, bool IsRange, uint Min, uint Max, uint Increment, string? Units,
    IReadOnlyList<PowerOption> Options, uint Ac, uint Dc, bool PolicyLocked)
{
    public bool Hidden => (Attributes & PowrProf.POWER_ATTRIBUTE_HIDE) != 0;
}

public static unsafe class PowerReader
{
    private enum Str { FriendlyName, Description, Units, PossibleName, PossibleDescription }

    // Универсальное чтение строки: 1-й вызов с buffer=null → размер, 2-й — данные (UTF-16, с \0).
    private static string? ReadStr(Str kind, Guid? scheme, Guid? sub, Guid? set, uint index = 0)
    {
        Guid s = scheme ?? default, g = sub ?? default, t = set ?? default;
        Guid* ps = scheme is null ? null : &s, pg = sub is null ? null : &g, pt = set is null ? null : &t;
        uint size = 0;
        uint r = Call(kind, ps, pg, pt, index, null, &size);
        if ((r != PowrProf.ERROR_SUCCESS && r != PowrProf.ERROR_MORE_DATA) || size == 0) return null;
        byte[] buf = new byte[size];
        fixed (byte* p = buf) r = Call(kind, ps, pg, pt, index, p, &size);
        return r == PowrProf.ERROR_SUCCESS ? Encoding.Unicode.GetString(buf, 0, (int)size).TrimEnd('\0') : null;
    }

    private static uint Call(Str k, Guid* s, Guid* g, Guid* t, uint i, byte* b, uint* n) => k switch
    {
        Str.FriendlyName        => PowrProf.PowerReadFriendlyName(0, s, g, t, b, n),
        Str.Description         => PowrProf.PowerReadDescription(0, s, g, t, b, n),
        Str.Units               => PowrProf.PowerReadValueUnitsSpecifier(0, g, t, b, n),
        Str.PossibleName        => PowrProf.PowerReadPossibleFriendlyName(0, g, t, i, b, n),
        Str.PossibleDescription => PowrProf.PowerReadPossibleDescription(0, g, t, i, b, n),
        _ => PowrProf.ERROR_INVALID_PARAMETER
    };

    public static Guid GetActive()
    {
        Guid* p = null;
        uint r = PowrProf.PowerGetActiveScheme(0, &p);
        if (r != 0) throw new System.ComponentModel.Win32Exception((int)r);
        try { return *p; } finally { PowrProf.LocalFree((nint)p); }
    }

    private static List<Guid> Enumerate(Guid? scheme, Guid? sub, uint access)
    {
        var list = new List<Guid>();
        Guid s = scheme ?? default, g = sub ?? default;
        for (uint i = 0; ; i++)
        {
            Guid item; uint size = (uint)sizeof(Guid);
            uint r = PowrProf.PowerEnumerate(0, scheme is null ? null : &s, sub is null ? null : &g, access, i, (byte*)&item, &size);
            if (r != PowrProf.ERROR_SUCCESS) break;          // ERROR_NO_MORE_ITEMS (259) — конец
            list.Add(item);
        }
        return list;
    }

    public static List<Guid> Schemes() => Enumerate(null, null, PowrProf.ACCESS_SCHEME);
    public static string? SchemeName(Guid scheme) => ReadStr(Str.FriendlyName, scheme, null, null);

    public static List<PowerSettingInfo> ReadAll(Guid scheme)
    {
        var result = new List<PowerSettingInfo>();
        var subs = Enumerate(scheme, null, PowrProf.ACCESS_SUBGROUP);
        subs.Insert(0, PowrProf.NO_SUBGROUP_GUID);           // ACCESS_SUBGROUP её не возвращает (проверено)
        foreach (Guid sub in subs)
        {
            string subName = sub == PowrProf.NO_SUBGROUP_GUID ? "Без подгруппы"
                : ReadStr(Str.FriendlyName, null, sub, null) is { Length: > 0 } n ? n
                : KnownNames.SubGroup(sub);                    // 9596fb26-… → «Параметры мультимедиа»
            foreach (Guid set in Enumerate(scheme, sub, PowrProf.ACCESS_INDIVIDUAL_SETTING))
                result.Add(ReadOne(scheme, sub, subName, set));
        }
        return result;
    }

    private static PowerSettingInfo ReadOne(Guid scheme, Guid sub, string subName, Guid set)
    {
        Guid sc = scheme, g = sub, t = set;
        string name = ReadStr(Str.FriendlyName, null, sub, set) ?? set.ToString();
        string? desc = ReadStr(Str.Description, null, sub, set);
        uint attr = PowrProf.PowerReadSettingAttributes(&g, &t);
        bool isRange = PowrProf.PowerIsSettingRangeDefined(&g, &t);
        uint min = 0, max = 0, inc = 0; string? units = null;
        var options = new List<PowerOption>();
        if (isRange)
        {
            PowrProf.PowerReadValueMin(0, &g, &t, &min);
            PowrProf.PowerReadValueMax(0, &g, &t, &max);
            PowrProf.PowerReadValueIncrement(0, &g, &t, &inc);
            units = ReadStr(Str.Units, null, sub, set);
        }
        else
        {
            for (uint i = 0; i < 64; i++)
            {
                string? optName = ReadStr(Str.PossibleName, null, sub, set, i);
                if (optName is null) break;
                uint type = 0, val = i, size = sizeof(uint);
                PowrProf.PowerReadPossibleValue(0, &g, &t, &type, i, (byte*)&val, &size); // type 4 = REG_DWORD
                options.Add(new PowerOption(val, optName, ReadStr(Str.PossibleDescription, null, sub, set, i)));
            }
        }
        uint ac = 0, dc = 0;
        PowrProf.PowerReadACValueIndex(0, &sc, &g, &t, &ac);   // ERROR_FILE_NOT_FOUND → параметра нет в этой схеме
        PowrProf.PowerReadDCValueIndex(0, &sc, &g, &t, &dc);
        return new PowerSettingInfo(sub, subName, set, name, desc, attr, isRange, min, max, inc, units,
                                    options, ac, dc, PolicyStore.IsLocked(set)); // HKLM/HKCU\SOFTWARE\Policies\Microsoft\Power\PowerSettings\<set>
    }

    /// Записать значение и применить, если схема активна. ПЕРЕД вызовом сохранить снапшот (Ac, Dc) для отката.
    public static void Write(Guid scheme, Guid sub, Guid set, uint? ac, uint? dc)
    {
        Guid sc = scheme, g = sub, t = set;
        if (ac is uint a) Check(PowrProf.PowerWriteACValueIndex(0, &sc, &g, &t, a));
        if (dc is uint d) Check(PowrProf.PowerWriteDCValueIndex(0, &sc, &g, &t, d));
        if (GetActive() == scheme) Check(PowrProf.PowerSetActiveScheme(0, &sc));
    }

    private static void Check(uint r) { if (r != 0) throw new System.ComponentModel.Win32Exception((int)r); }
}
```

Нюансы отображения (видно в выгрузке этой машины):
- Таймауты в секундах: 0 = «Никогда»; `EXECTIME` AC = 4294967295 = «Бесконечно».
- `PERFBOOSTMODE` на Win11 23H2 имеет 7 вариантов (0–6), а документация Microsoft описывает только 0–4 — читать варианты из ОС, а не хардкодить.
- `PowerReadACValueIndex` возвращает значение **схемы**; что реально действует прямо сейчас (с учётом PPM-профиля Game Mode, оверлея «Режим питания»,
  QoS потока) — API не показывает. Для UI это стоит подписать.
- Для `nint root`, `Guid*` и `byte*` используется source-generated `LibraryImport` (блиттабельные типы, совместимо с trimming/AOT self-contained).

### 2.3. Скрытые параметры (`Attributes`)

- `Attributes` (DWORD) в `PowerSettings\<sub>\<setting>`: бит `0x1` = `POWER_ATTRIBUTE_HIDE` (скрыть в «Дополнительных параметрах питания»),
  бит `0x2` = `POWER_ATTRIBUTE_SHOW_AOAC` (показывать на системах с Modern Standby). Отсутствие значения = видим.
  На этой машине: `PROCTHROTTLEMIN`, `PROCTHROTTLEMAX`, `DISKIDLE`, `STANDBYIDLE`, `ASPM`, `VIDEOIDLE`, батарея и др. имеют `2`; почти все остальные — `1`.
- **Для чтения/записи раскрывать не нужно**: `PowerEnumerate` и `PowerRead*/PowerWrite*` работают со скрытыми параметрами так же, как с видимыми
  (`powercfg /qh` показывает все 155). Раскрытие влияет **только** на видимость в `powercfg.cpl` и **безопасно** (значения не меняются).
- Если всё же делать кнопку «Показать в Панели управления»: менять **только бит 0x1** (`attr & ~1`), сохранить исходный DWORD и при откате вернуть его
  (`PowerWriteSettingAttributes(&sub,&set,original)`), либо `powercfg -attributes <SUB> <SETTING> -ATTRIB_HIDE` / `+ATTRIB_HIDE`.
  Не затирать бит `0x2`. Изменение глобальное (для всех схем), не требует перезагрузки (панель нужно переоткрыть).

### 2.4. Полный справочник параметров (выгрузка `powercfg /qh` этой машины, Win11 23H2, Ryzen 5700X)

Колонки «AC/DC» — эффективные значения стоковых схем на этой машине (для High performance показан сток из `DefaultPowerSchemeValues`, в скобках — изменённое пользователем).
Значения у разных CPU/ОС отличаются (провижининг) — это справочник, а не константы для кода. Схема «Ultimate Performance (ExitLag)» совпадает с Ultimate, кроме `RTCWAKE` = 0/0.

#### Параметры не принадлежат к подгруппе — `SUB_NONE` — `fea3413e-7e05-4911-9a71-700331f1c294`

| Alias | GUID параметра | Имя (из ОС, ru-RU) | Диапазон / варианты | Attributes | Balanced AC/DC | High AC/DC | Ultimate AC/DC | Power saver AC/DC |
|---|---|---|---|---|---|---|---|---|
| `CONSOLELOCK` | `0e796bdb-100d-47d6-a2d5-f7d2daa51f51` | Требовать введения пароля при пробуждении | 0=Нет; 1=Да | 1 (HIDE) | 1/1 | 1/1 | 1/1 | 1/1 |
| `PERSONALITY` | `245d8541-3943-4422-b025-13a784f679b7` | Тип схемы электропитания | 0=Экономия энергии; 1=Высокая производительность; 2=Сбалансированная | 1 (HIDE) | 2/2 | 1/1 | 1/2 | 0/0 |
| `DEVICEIDLE` | `4faab71a-92e5-4726-b531-224559672d19` | Политика простоя устройства | 0=Производительность; 1=Энергосбережение | 1 (HIDE) | 0/1 | 0/0 | 0/1 | 1/1 |
| `DISCONNECTEDSTANDBYMODE` | `68afb2d9-ee95-47a8-8f50-4115088073b1` | Режим ожидания с отключением | 0=Обычный; 1=Агрессивный | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `CONNECTIVITYINSTANDBY` | `f15576e8-98b7-4186-b944-eafa664402d9` | Сетевое подключение в ждущем режиме | 0=Отключить; 1=Включить; 2=Под управлением Windows | 1 (HIDE) | 1/2 | 1/1 | 1/2 | 1/1 |

#### Жесткий диск — `SUB_DISK` — `0012ee47-9041-4b5d-9b77-535fba8b1442`

| Alias | GUID параметра | Имя (из ОС, ru-RU) | Диапазон / варианты | Attributes | Balanced AC/DC | High AC/DC | Ultimate AC/DC | Power saver AC/DC |
|---|---|---|---|---|---|---|---|---|
| — | `0b2d69d7-a2a1-449c-9680-f91c70521c60` | AHCI Link Power Management - HIPM/DIPM | 0=Active; 1=HIPM; 2=HIPM+DIPM; 3=DIPM; 4=Lowest | 1 (HIDE) | 1/1 | 0/1 | 0/1 | 2/2 |
| `DISKMAXPOWER` | `51dea550-bb38-4bc4-991b-eacf37be5ec8` | Максимальный уровень потребления питания | 0…100 (шаг 1) % | 1 (HIDE) | 100/100 | 100/100 | 100/100 | 100/100 |
| `DISKIDLE` | `6738e2c4-e8a5-4a42-b16a-e040e769756e` | Отключать жесткий диск через | 0…4294967295 (шаг 1) Сек. | 2 (SHOW_AOAC) | 1200/600 | 1200/1200 (на машине изменено: 0/1200) | 0/0 | 1200/300 |
| `DISKBURSTIGNORE` | `80e3c60e-bb94-4ad8-bbe0-0d3195efc663` | Длительность неучитываемых всплесков активности жесткого диска | 0…4294967295 (шаг 1) Сек. | 1 (HIDE) | 0/30 | 0/0 | 0/30 | 30/30 |
| — | `d3d55efd-c1ff-424e-9dc3-441be7833010` | Secondary NVMe Idle Timeout | 0…60000 (шаг 1) milliseconds | 1 (HIDE) | 2000/1000 | 2000/2000 | 2000/1000 | 1000/1000 |
| `NVMEPRIMARYIDLETIMEOUT` | `d639518a-e56d-4345-8af2-b9f32fb26109` | Primary NVMe Idle Timeout | 0…60000 (шаг 1) milliseconds | 1 (HIDE) | 200/100 | 200/200 | 200/100 | 100/100 |
| — | `dab60367-53fe-4fbc-825e-521d069d2456` | AHCI Link Power Management - Adaptive | 0…300000 (шаг 1) millisecond | 1 (HIDE) | 100/100 | 0/0 | 0/100 | 100/100 |
| — | `dbc9e238-6de9-49e3-92cd-8c2b4946b472` | Secondary NVMe Power State Transition Latency Tolerance | 0…60000 (шаг 1) milliseconds | 1 (HIDE) | 100/100 | 0/0 | 0/100 | 200/200 |
| `DISKNVMENOPPME` | `fc7372b6-ab2d-43ee-8797-15e9841f2cca` | NVMe NOPPME | 0=Выкл; 1=Вкл | 1 (HIDE) | 1/0 | 1/0 | 1/0 | 1/0 |
| — | `fc95af4d-40e7-4b6d-835a-56d131dbc80e` | Primary NVMe Power State Transition Latency Tolerance | 0…60000 (шаг 1) milliseconds | 1 (HIDE) | 15/50 | 0/10 | 0/50 | 100/200 |

#### Параметры фона рабочего стола — `0d7dbae2-4294-402a-ba8e-26777e8488cd`

| Alias | GUID параметра | Имя (из ОС, ru-RU) | Диапазон / варианты | Attributes | Balanced AC/DC | High AC/DC | Ultimate AC/DC | Power saver AC/DC |
|---|---|---|---|---|---|---|---|---|
| — | `309dce9b-bef4-4119-9921-a851fb12f0f4` | Слайд-шоу | 0=Доступно; 1=Приостановлено | 2 (SHOW_AOAC) | 0/1 | 0/0 | 0/1 | 0/1 |

#### Параметры адаптера беспроводной сети — `19cbb8fa-5279-450e-9fac-8a3d5fedd0c1`

| Alias | GUID параметра | Имя (из ОС, ru-RU) | Диапазон / варианты | Attributes | Balanced AC/DC | High AC/DC | Ultimate AC/DC | Power saver AC/DC |
|---|---|---|---|---|---|---|---|---|
| — | `12bbebe6-58d6-4636-95bb-3217ef867c1a` | Режим энергосбережения | 0=Максимальная производительность; 1=Минимальное энергосбережение; 2=Среднее энергосбережение; 3=Максимальное энергосбережение | нет (видим) | 0/2 | 0/0 | 0/2 | 0/3 |

#### Сон — `SUB_SLEEP` — `238c9fa8-0aad-41ed-83f4-97be242c8f20`

| Alias | GUID параметра | Имя (из ОС, ru-RU) | Диапазон / варианты | Attributes | Balanced AC/DC | High AC/DC | Ultimate AC/DC | Power saver AC/DC |
|---|---|---|---|---|---|---|---|---|
| `LEGACYRTCMITIGATION` | `1a34bdc3-7e6b-442e-a9d0-64b6ef378e84` | Снижение рисков, связанных с прежними версиями RTC | 0=Отключить; 1=Включить | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `AWAYMODE` | `25dfa149-5dd1-4736-b5ab-e8a37b5b8187` | Политика для режима отсутствия | 0=Нет; 1=Да | 1 (HIDE) | 1/0 | 1/0 | 1/0 | 1/0 |
| `STANDBYIDLE` | `29f6c1db-86da-48c5-9fdb-f2b67b1f44da` | Сон после | 0…4294967295 (шаг 1) Сек. | 2 (SHOW_AOAC) | 900/600 | 0/0 | 0/600 | 900/600 |
| `UNATTENDSLEEP` | `7bc4a2f9-d8fc-4469-b07b-33eb785aaca0` | Время ожидания автоматического перехода системы в режим сна | 0…4294967295 (шаг 1) Сек. | 1 (HIDE) | 120/120 | 120/120 | 120/120 | 120/120 |
| `HYBRIDSLEEP` | `94ac6d29-73ce-41a6-809f-6363ba21b47e` | Разрешить гибридный спящий режим | 0=Выкл; 1=Вкл | нет (видим) | 1/1 | 1/1 | 1/1 | 1/1 |
| `HIBERNATEIDLE` | `9d7815a6-7ee4-497e-8888-515a05f02364` | Гибернация после | 0…4294967295 (шаг 1) Сек. | 0 | 0/0 | 0/0 | 0/0 | 0/0 |
| `SYSTEMREQUIRED` | `a4b195f5-8225-47d8-8012-9d41369786e2` | Разрешить политику, требуемую системой | 0=Нет; 1=Да | 1 (HIDE) | 1/1 | 1/1 | 1/1 | 1/1 |
| `ALLOWSTANDBY` | `abfc2519-3608-4c2a-94ea-171b0ed546ab` | Разрешить ждущий режим | 0=Выкл; 1=Вкл | 1 (HIDE) | 1/1 | 1/1 | 1/1 | 1/1 |
| `RTCWAKE` | `bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d` | Разрешить таймеры пробуждения | 0=Отключить; 1=Включить; 2=Только важные таймеры пробуждения | 2 (SHOW_AOAC) | 1/1 | 1/1 | 1/1 | 1/1 |
| `REMOTEFILESLEEP` | `d4c1d4c8-d5cc-43d3-b83e-fc51215cb04d` | Разрешить переход в спящий режим при удаленном открытии | 0=Выкл; 1=Вкл | 1 (HIDE) | 1/1 | 0/0 | 0/1 | 1/1 |

#### Параметры USB — `2a737441-1930-4402-8d77-b2bebba308a3`

| Alias | GUID параметра | Имя (из ОС, ru-RU) | Диапазон / варианты | Attributes | Balanced AC/DC | High AC/DC | Ultimate AC/DC | Power saver AC/DC |
|---|---|---|---|---|---|---|---|---|
| — | `0853a681-27c8-4100-a2fd-82013e970683` | Hub Selective Suspend Timeout | 0…100000 (шаг 1) Millisecond | 1 (HIDE) | 50/50 | 50/50 | 50/50 | 50/50 |
| — | `48e6b7a6-50f5-4782-a5d4-53bb8f07e226` | Параметр временного отключения USB-порта | 0=Запрещено; 1=Разрешено | нет (видим) | 1/1 | 1/1 | 1/1 | 1/1 |
| — | `498c044a-201b-4631-a522-5c744ed4e678` | Setting IOC on all TDs | 0=Disabled; 1=Enabled | 1 (HIDE) | 1/0 | 1/1 | 1/0 | 0/0 |
| — | `d4e98f31-5ffe-4ce1-be31-1b38b384c009` | USB 3 Link Power Mangement | 0=Off; 1=Minimum power savings; 2=Moderate power savings; 3=Maximum power savings | 1 (HIDE) | 2/2 | 2/2 | 2/2 | 2/2 |

#### Устойчивость режима простоя — `SUB_IR` — `2e601130-5351-4d9d-8e04-252966bad054`

| Alias | GUID параметра | Имя (из ОС, ru-RU) | Диапазон / варианты | Attributes | Balanced AC/DC | High AC/DC | Ultimate AC/DC | Power saver AC/DC |
|---|---|---|---|---|---|---|---|---|
| `EXECTIME` | `3166bc41-7e98-4e03-b34e-ec0f5f2b218e` | Время ожидания запроса питания "Требуется выполнение" | 0…4294967295 (шаг 1) Сек. | 1 (HIDE) | 4294967295/300 | 4294967295/300 | 4294967295/300 | 4294967295/300 |
| `COALTIME` | `c36f0eb4-2988-4a70-8eee-0884fc2c2433` | Время ожидания объединения для подсистемы ввода-вывода | 0…4294967295 (шаг 1) миллисекунд | 1 (HIDE) | 0/60000 | 0/0 | 0/60000 | 0/60000 |
| `PROCIR` | `c42b79aa-aa3a-484b-a98f-2cf32aa90a28` | Разрешение таймера устойчивости в режиме простоя для процессора | 0…65000 (шаг 1) миллисекунд | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `DEEPSLEEP` | `d502f7ee-1dc7-4efd-a55d-f04b6f5c0545` | Режим глубокого сна включен или отключен | 0=Режим глубокого сна отключен; 1=Режим глубокого сна включен | 1 (HIDE) | 1/1 | 1/1 | 1/1 | 1/1 |

#### Параметры управления прерываниями — `SUB_INTSTEER` — `48672f38-7a9a-4bb2-8bf8-3d85be19de4e`

| Alias | GUID параметра | Имя (из ОС, ru-RU) | Диапазон / варианты | Attributes | Balanced AC/DC | High AC/DC | Ultimate AC/DC | Power saver AC/DC |
|---|---|---|---|---|---|---|---|---|
| `MODE` | `2bfc24f9-5ea2-4801-8213-3dbae01aa39d` | Режим управления прерываниями | 0=По умолчанию; 1=Любой процессор; 2=Любой неприпаркованный процессор с задержкой; 3=Любой неприпаркованный процессор; 4=Блокировать маршрутизацию прерываний; 5=Процессор 0; 6=Процессор 1 | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `PERPROCLOAD` | `73cde64d-d720-4bb2-a860-c755afe77ef2` | Целевая загрузка | 0…10000 (шаг 1) Десятые доли процента | 1 (HIDE) | 50/50 | 50/50 | 50/50 | 50/50 |
| `UNPARKTIME` | `d6ba4903-386f-4c2c-8adb-5c21b3328d25` | Триггер неприпаркованного времени | 0…100000 (шаг 1) мс | 1 (HIDE) | 100/100 | 100/100 | 100/100 | 100/100 |

#### Кнопки питания и крышка — `SUB_BUTTONS` — `4f971e89-eebd-4455-a8de-9e59040e7347`

| Alias | GUID параметра | Имя (из ОС, ru-RU) | Диапазон / варианты | Attributes | Balanced AC/DC | High AC/DC | Ultimate AC/DC | Power saver AC/DC |
|---|---|---|---|---|---|---|---|---|
| `LIDACTION` | `5ca83367-6e45-459f-a27b-476b1d01c936` | Действие закрытия крышки | 0=Действие не требуется; 1=Сон; 2=Гибернация; 3=Завершение работы | 1 (HIDE) | 1/1 | 1/1 | 1/1 | 1/1 |
| `PBUTTONACTION` | `7648efa3-dd9c-4e3e-b566-50f929386280` | Действие кнопки питания | 0=Действие не требуется; 1=Сон; 2=Гибернация; 3=Завершение работы; 4=Отключить дисплей | 1 (HIDE) | 3/3 | 3/3 | 3/3 | 3/3 |
| `SHUTDOWN` | `833a6b62-dfa4-46d1-82f8-e09e34d029d6` | Включить принудительное завершение работы при нажатии кнопок и закрытии крышки | 0=Выкл; 1=Вкл | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `SBUTTONACTION` | `96996bc0-ad50-47ec-923b-6f41874dd9eb` | Действие кнопки спящего режима | 0=Действие не требуется; 1=Сон; 2=Гибернация; 3=Завершение работы; 4=Отключить дисплей | 1 (HIDE) | 1/1 | 1/1 | 1/1 | 1/1 |
| `LIDOPENWAKE` | `99ff10e7-23b1-4c07-a9d1-5c3206d741b4` | Действие при открытии крышки | 0=Действие не требуется; 1=Включение экрана | 1 (HIDE) | 1/1 | 1/1 | 1/1 | 1/1 |
| `UIBUTTON_ACTION` | `a7066653-8d6c-40a8-910e-a1f54b84c7e5` | Кнопка питания меню "Пуск" | 0=Сон; 1=Гибернация; 2=Завершение работы | нет (видим) | 0/0 | 0/0 | 0/0 | 0/0 |

#### PCI Express — `SUB_PCIEXPRESS` — `501a4d13-42af-4429-9fd1-a8218c268e20`

| Alias | GUID параметра | Имя (из ОС, ru-RU) | Диапазон / варианты | Attributes | Balanced AC/DC | High AC/DC | Ultimate AC/DC | Power saver AC/DC |
|---|---|---|---|---|---|---|---|---|
| `ASPM` | `ee12f906-d277-404b-b6da-e5fa1a576df5` | Управление питанием состояния связи | 0=Откл.; 1=Умеренное энергосбережение; 2=Максимальное энергосбережение | 2 (SHOW_AOAC) | 1/2 | 0/0 | 0/2 | 2/2 |

#### Управление питанием процессора — `SUB_PROCESSOR` — `54533251-82be-4824-96c1-47b60b740d00`

| Alias | GUID параметра | Имя (из ОС, ru-RU) | Диапазон / варианты | Attributes | Balanced AC/DC | High AC/DC | Ultimate AC/DC | Power saver AC/DC |
|---|---|---|---|---|---|---|---|---|
| `PERFINCTHRESHOLD` | `06cadf0e-64ed-448a-8927-ce7bf90eb35d` | Порог увеличения производительности процессора | 0…100 (шаг 1) % | 1 (HIDE) | 30/90 | 30/30 | 30/90 | 90/90 |
| `PERFINCTHRESHOLD1` | `06cadf0e-64ed-448a-8927-ce7bf90eb35e` | Порог увеличения производительности процессора с классом энергоэффективности 1 | 0…100 (шаг 1) % | 1 (HIDE) | 60/90 | 60/90 | 60/90 | 60/90 |
| `CPMINCORES` | `0cc5b647-c1df-4637-891a-dec35c318583` | Минимальное число ядер в состоянии простоя | 0…100 (шаг 1) % | 1 (HIDE) | 100/10 | 100/100 | 100/10 | 10/10 |
| `CPMINCORES1` | `0cc5b647-c1df-4637-891a-dec35c318584` | Минимальное число ядер в состоянии простоя для процессора с классом энергоэффективности 1 | 0…100 (шаг 1) % | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `PERFDECTHRESHOLD` | `12a0ab44-fe28-4fa9-b3bd-4b64f44960a6` | Порог уменьшения производительности процессора | 0…100 (шаг 1) % | 1 (HIDE) | 10/30 | 10/10 | 10/30 | 60/60 |
| `PERFDECTHRESHOLD1` | `12a0ab44-fe28-4fa9-b3bd-4b64f44960a7` | Порог уменьшения производительности процессора с классом энергоэффективности 1 | 0…100 (шаг 1) % | 1 (HIDE) | 20/30 | 20/30 | 20/30 | 20/30 |
| `HETEROCLASS1INITIALPERF` | `1facfc65-a930-4bc5-9f38-504ec097bbc0` | Начальная производительность неприпаркованного процессора с классом энергоэффективности 1 | 0…100 (шаг 1) % | 1 (HIDE) | 50/50 | 50/50 | 50/50 | 50/50 |
| `CPCONCURRENCY` | `2430ab6f-a520-44a2-9601-f7f23b5134b1` | Порог параллелизма приостановки ядер для производительности процессора | 0…100 (шаг 1) % | 1 (HIDE) | 97/97 | 97/97 | 97/97 | 97/97 |
| `CPINCREASETIME` | `2ddd5a84-5a71-437e-912a-db0b8c788732` | Время увеличения занятости ядер производительности процессора | 1…100 (шаг 1) Интервалы проверки | 1 (HIDE) | 3/3 | 7/7 | 7/3 | 1/1 |
| `PERFEPP` | `36687f9e-e3a5-4dbf-b1dc-15eb381c6863` | Политика относительно предпочтительных вариантов энергопотребления процессоров | 0…100 (шаг 1) % | 1 (HIDE) | 0/50 | 0/0 | 0/50 | 60/60 |
| `PERFEPP1` | `36687f9e-e3a5-4dbf-b1dc-15eb381c6864` | Политика относительно предпочтительных вариантов энергопотребления процессоров с классом энергоэффективности 1 | 0…100 (шаг 1) % | 1 (HIDE) | 33/50 | 0/0 | 0/50 | 60/60 |
| `THROTTLING` | `3b04d4fd-1cc7-4f23-ab1c-d1337819c4bb` | Разрешить состояния снижения питания | 0=Выкл; 1=Вкл; 2=Автоматически | 1 (HIDE) | 2/2 | 2/2 | 2/2 | 2/2 |
| `HETEROINCREASETIME` | `4009efa7-e72d-4cba-9edf-91084ea8cbc3` | Время увеличения производительности процессора с классом энергоэффективности 1 | 1…100 (шаг 1) Интервалы проверки | 1 (HIDE) | 1/1 | 1/1 | 1/1 | 1/1 |
| `PERFDECPOL` | `40fbefc7-2e9d-4d25-a185-0cfd8574bac6` | Политика уменьшения производительности процессора | 0=Оптимальное; 1=Единый; 2=Реактивное | 1 (HIDE) | 1/0 | 1/1 | 1/0 | 2/2 |
| `PERFDECPOL1` | `40fbefc7-2e9d-4d25-a185-0cfd8574bac7` | Политика уменьшения производительности процессора с классом энергоэффективности 1 | 0=Оптимальное; 1=Единый; 2=Реактивное | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `LONGTHREADARCHCLASSLOWERTHRESHOLD` | `43f278bc-0f8a-46d0-8b31-9a23e615d713` | Нижний предел архитектуры процессора потоков большой продолжительности | 0…255 (шаг 1) Класс архитектуры процессора | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `CPPERF` | `447235c7-6a8d-4cc0-8e24-9eaf70b96e2b` | Приостановленное состояние производительности приостановки ядра производительности процессора | 0=Без предпочтений; 1=Состояние максимальной производительности; 2=Состояние минимальной производительности | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `CPPERF1` | `447235c7-6a8d-4cc0-8e24-9eaf70b96e2c` | Приостановленное состояние производительности приостановки ядра производительности процессора с классом энергоэффективности 1 | 0=Без предпочтений; 1=Состояние максимальной производительности; 2=Состояние минимальной производительности | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `PERFBOOSTPOL` | `45bcc044-d885-43e2-8605-ee0ec6e96b59` | Политика усиления производительности процессора | 0…100 (шаг 1) % | 1 (HIDE) | 60/40 | 100/100 | 100/40 | 0/0 |
| `PERFINCPOL` | `465e1f50-b610-473a-ab58-00d1077dc418` | Политика увеличения производительности процессора | 0=Оптимальное; 1=Единый; 2=Реактивное; 3=Идеально агрессивный | 1 (HIDE) | 2/0 | 2/2 | 2/0 | 1/1 |
| `PERFINCPOL1` | `465e1f50-b610-473a-ab58-00d1077dc419` | Политика увеличения производительности процессора с классом энергоэффективности 1 | 0=Оптимальное; 1=Единый; 2=Реактивное; 3=Идеально агрессивный | 1 (HIDE) | 3/3 | 3/3 | 3/3 | 3/3 |
| `LATENCYHINTEPP` | `4b70f900-cdd9-4e66-aa26-ae8417f98173` | Подсказка о чувствительности к задержке и установках производительности процессора | 0…100 (шаг 1) % | 1 (HIDE) | 100/100 | 100/100 | 100/100 | 100/100 |
| `LATENCYHINTEPP1` | `4b70f900-cdd9-4e66-aa26-ae8417f98174` | Подсказка о чувствительности к задержке, настройки производительности процессора для класса энергоэффективности процессора 1 | 0…100 (шаг 1) % | 1 (HIDE) | 100/100 | 100/100 | 100/100 | 100/100 |
| `IDLEDEMOTE` | `4b92d758-5a24-4851-a470-815d78aee119` | Пороговое значение понижения состояния простоя процессора | 0…100 (шаг 1) % | 1 (HIDE) | 40/20 | 40/40 | 40/20 | 20/20 |
| `CPDISTRIBUTION` | `4bdaf4e9-d103-46d7-a5f0-6280121616ef` | Пороговое значение распространения парковки ядра для производительности процессора | 0…100 (шаг 1) % | 1 (HIDE) | 90/90 | 90/90 | 90/90 | 90/90 |
| `PERFCHECK` | `4d2b0152-7d5c-498b-88e2-34345392a2c5` | Интервал проверки производительности процессора | 1…5000 (шаг 1) миллисекунд | 1 (HIDE) | 15/30 | 15/15 | 15/30 | 200/200 |
| `PERFDUTYCYCLING` | `4e4450b3-6179-4e91-b8f1-5bb9938f81a1` | Переключение рабочих циклов процессора | 0=Отключен; 1=Включен | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `SHORTTHREADARCHCLASSLOWERTHRESHOLD` | `53824d46-87bd-4739-aa1b-aa793fac36d6` | Нижний предел архитектуры процессора потоков небольшой продолжительности | 0…255 (шаг 1) Класс архитектуры процессора | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `IDLEDISABLE` | `5d76a2ca-e8c0-402f-a133-2158492d58ad` | Отключение простоя процессора | 0=Включить состояние простоя; 1=Отключить состояние простоя | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `LATENCYHINTUNPARK` | `616cdaa5-695e-4545-97ad-97dc2d1bdd88` | Минимальное число занятых ядер или пакетов с учетом указаний о чувствительности к задержкам | 0…100 (шаг 1) % | 1 (HIDE) | 50/50 | 50/50 | 50/50 | 50/50 |
| `LATENCYHINTUNPARK1` | `616cdaa5-695e-4545-97ad-97dc2d1bdd89` | Минимальное число занятых ядер или пакетов с учетом указаний о чувствительности к задержкам для процессора с классом энергоэффективности 1 | 0…100 (шаг 1) % | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `LATENCYHINTPERF` | `619b7505-003b-4e82-b7a6-4dd29c300971` | Производительность процессора с учетом указаний о чувствительности к задержкам | 0…100 (шаг 1) % | 1 (HIDE) | 99/99 | 99/99 | 99/99 | 0/0 |
| `LATENCYHINTPERF1` | `619b7505-003b-4e82-b7a6-4dd29c300972` | Производительность процессора с учетом указаний о чувствительности к задержкам для процессора с классом энергоэффективности 1 | 0…100 (шаг 1) % | 1 (HIDE) | 50/50 | 50/50 | 50/50 | 50/50 |
| `IDLESCALING` | `6c2993b0-8f48-481f-bcc6-00dd2742aa06` | Масштабирование порогового значения простоя процессора | 0=Отключить масштабирование; 1=Включить масштабирование | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 1/1 |
| `CPDECREASEPOL` | `71021b41-c749-4d21-be74-a00f335d582b` | Политика уменьшения занятости ядер производительности процессора | 0=Оптимальное число ядер; 1=Одно ядро; 2=Все доступные ядра; 3=Восьмая часть ядер | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `PROCFREQMAX` | `75b0ae3f-bce0-45a7-8c89-c9611c25e100` | Максимальная частота процессора | 0…4294967295 (шаг 1) МГц | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `PROCFREQMAX1` | `75b0ae3f-bce0-45a7-8c89-c9611c25e101` | Максимальная частота процессора с классом энергоэффективности 1 | 0…4294967295 (шаг 1) МГц | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `IDLEPROMOTE` | `7b224883-b3cc-4d79-819f-8374152cbe7c` | Пороговое значение повышения состояния простоя процессора | 0…100 (шаг 1) % | 1 (HIDE) | 60/40 | 60/60 | 60/40 | 40/40 |
| `PERFHISTORY` | `7d24baa7-0b84-480f-840c-1b0743c00f5f` | Журнал счетчика производительности процессора | 1…128 (шаг 1) Интервалы проверки | 1 (HIDE) | 1/1 | 1/1 | 1/1 | 1/1 |
| `PERFHISTORY1` | `7d24baa7-0b84-480f-840c-1b0743c00f60` | Журнал счетчика производительности процессора с классом энергоэффективности 1 | 1…128 (шаг 1) Интервалы проверки | 1 (HIDE) | 1/1 | 1/1 | 1/1 | 1/1 |
| `HETERODECREASETIME` | `7f2492b6-60b1-45e5-ae55-773f8cd5caec` | Время уменьшения производительности процессора с классом энергоэффективности 1 | 1…100 (шаг 1) Интервалы проверки | 1 (HIDE) | 3/3 | 3/3 | 3/3 | 3/3 |
| `HETEROPOLICY` | `7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5` | Применяется неоднородная политика | 0=Использовать неоднородную политику 0; 1=Использовать неоднородную политику 1; 2=Использовать неоднородную политику 2; 3=Использовать неоднородную политику 3; 4=Использовать неоднородную политику 4 | 1 (HIDE) | 4/4 | 4/4 | 4/4 | 4/4 |
| `SHORTTHREADARCHCLASSUPPERTHRESHOLD` | `828423eb-8662-4344-90f7-52bf15870f5a` | Верхний предел архитектуры процессора потоков небольшой продолжительности | 0…255 (шаг 1) Класс архитектуры процессора | 1 (HIDE) | 255/255 | 255/255 | 255/255 | 255/255 |
| `PROCTHROTTLEMIN` | `893dee8e-2bef-41e0-89c6-b55d0929964c` | Минимальное состояние процессора | 0…100 (шаг 1) % | 2 (SHOW_AOAC) | 0/5 | 100/5 (на машине изменено: 10/5) | 100/5 | 5/5 |
| `PROCTHROTTLEMIN1` | `893dee8e-2bef-41e0-89c6-b55d0929964d` | Минимальное состояние процессора с классом энергоэффективности 1 | 0…100 (шаг 1) % | 1 (HIDE) | 5/5 | 100/5 | 100/5 | 5/5 |
| `PERFAUTONOMOUS` | `8baa4a8a-14c6-4451-8e8b-14bdbd197537` | Автономный режим производительности процессоров | 0=Отключен; 1=Включен | 1 (HIDE) | 1/1 | 1/1 | 1/1 | 1/1 |
| `SCHEDPOLICY` | `93b8b6dc-0698-4d1c-9ee4-0644e900c85d` | Политика планирования неоднородной цепочки | 0=Все процессоры; 1=Производительные процессоры; 2=Предпочитать производительные процессоры; 3=Эффективные процессоры; 4=Предпочитать эффективные процессоры; 5=Автоматически | 1 (HIDE) | 5/5 | 5/5 | 5/5 | 5/5 |
| `CPOVERUTIL` | `943c8cb6-6f93-4227-ad87-e9a3feec08d1` | Порог использования функции парковки ядер процессора | 5…100 (шаг 1) % | 1 (HIDE) | 85/85 | 60/60 | 60/85 | 90/90 |
| `SYSCOOLPOL` | `94d3a615-a899-4ac5-ae2b-e4d8f634367f` | Политика охлаждения системы | 0=Пассивный; 1=Активный | 1 (HIDE) | 1/0 | 1/1 | 1/0 | 0/0 |
| `SOFTPARKLATENCY` | `97cfac41-2217-47eb-992d-618b1977c907` | Производительность процессора ядро режим ожидания программное ожидание задержка | 0…4294967295 (шаг 1) микросекунд | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `PERFINCTIME` | `984cf492-3bed-4488-a8f9-4286c97bf5aa` | Время увеличения производительности процессора | 1…100 (шаг 1) Интервалы проверки | 1 (HIDE) | 1/1 | 1/1 | 1/1 | 3/3 |
| `PERFINCTIME1` | `984cf492-3bed-4488-a8f9-4286c97bf5ab` | Время увеличения производительности процессора с классом энергоэффективности 1 | 1…100 (шаг 1) Интервалы проверки | 1 (HIDE) | 1/1 | 1/1 | 1/1 | 3/3 |
| `IDLESTATEMAX` | `9943e905-9a30-4ec1-9b99-44dd3b76f7a2` | Максимальное состояние простоя процессора | 0…20 (шаг 1) Тип состояния | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `HETEROINCREASETHRESHOLD` | `b000397d-9b0b-483d-98c9-692a6060cfbf` | Порог увеличения уровня производительности при увеличении количества процессоров с классом энергоэффективности 1 | нет диапазона/вариантов (сырой DWORD) | 1 (HIDE) | 255/255 | 255/255 | 255/255 | 255/255 |
| `HETEROINCREASETHRESHOLD1` | `b000397d-9b0b-483d-98c9-692a6060cfc0` | Порог увеличения уровня производительности при увеличении количества процессоров с классом энергоэффективности 2 | нет диапазона/вариантов (сырой DWORD) | 1 (HIDE) | 255/255 | 255/255 | 255/255 | 255/255 |
| `MODULEUNPARKPOLICY` | `b0deaf6b-59c0-4523-8a45-ca7f40244114` | Политика возобновления работы модулей | 0=Отключено; 1=Циклический перебор; 2=Последовательно | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `SMTUNPARKPOLICY` | `b28a6829-c5f7-444e-8f61-10e24e85c532` | Политика возобновления работы потоков SMT | 0=Ядро; 1=Ядро на поток; 2=Циклический перебор; 3=Последовательно | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `COMPLEXUNPARKPOLICY` | `b669a5e9-7b1d-4132-baaa-49190abcfeb6` | Политика возобновления работы комплексов | 0=Отключено; 1=Циклический перебор; 2=Последовательно | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `SHORTSCHEDPOLICY` | `bae08b81-2d5e-4688-ad6a-13243356654b` | Политика планирования неоднородной краткосрочной цепочки | 0=Все процессоры; 1=Производительные процессоры; 2=Предпочитать производительные процессоры; 3=Эффективные процессоры; 4=Предпочитать эффективные процессоры; 5=Автоматически | 1 (HIDE) | 2/5 | 2/5 | 2/5 | 2/5 |
| `PROCTHROTTLEMAX` | `bc5038f7-23e0-4960-96da-33abaf5935ec` | Максимальное состояние процессора | 0…100 (шаг 1) % | 2 (SHOW_AOAC) | 100/100 | 100/100 | 100/100 | 100/100 |
| `PROCTHROTTLEMAX1` | `bc5038f7-23e0-4960-96da-33abaf5935ed` | Максимальное состояние процессора с классом энергоэффективности 1 | 0…100 (шаг 1) % | 1 (HIDE) | 100/100 | 100/100 | 100/100 | 75/75 |
| `PERFBOOSTMODE` | `be337238-0d82-4146-a960-4f3749d470c7` | Режим усиления производительности процессора | 0=Отключен; 1=Включен; 2=Агрессивный; 3=Эффективно включенный; 4=Эффективно агрессивный; 5=Агрессивный при гарантированном; 6=Эффективно агрессивный при гарантированном | 1 (HIDE) | 2/2 | 2/2 | 2/2 | 2/2 |
| `LONGTHREADARCHCLASSUPPERTHRESHOLD` | `bf903d33-9d24-49d3-a468-e65e0325046a` | Верхний предел архитектуры процессора потоков большой продолжительности | 0…255 (шаг 1) Класс архитектуры процессора | 1 (HIDE) | 255/255 | 255/255 | 255/255 | 255/255 |
| `IDLECHECK` | `c4581c31-89ab-4597-8e2b-9c9cab440e6b` | Проверка времени простоя процессора | 1…200000 (шаг 1) микросекунд | 1 (HIDE) | 50000/50000 | 50000/50000 | 50000/50000 | 50000/50000 |
| `CPINCREASEPOL` | `c7be0679-2817-4d69-9d02-519a537ed0c6` | Политика увеличения занятости ядер производительности процессора | 0=Оптимальное число ядер; 1=Одно ядро; 2=Все доступные ядра; 3=Восьмая часть ядер | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `PERFAUTONOMOUSWINDOW` | `cfeda3d0-7697-4566-a922-a9086cd49dfa` | Окно действий процессоров в автономном режиме | 0…1270000000 (шаг 1) микросекунд | 1 (HIDE) | 30000/30000 | 30000/30000 | 30000/30000 | 30000/30000 |
| `PERFDECTIME` | `d8edeb9b-95cf-4f95-a73c-b061973693c8` | Время уменьшения производительности процессора | 1…100 (шаг 1) Интервалы проверки | 1 (HIDE) | 1/1 | 1/1 | 1/1 | 1/1 |
| `PERFDECTIME1` | `d8edeb9b-95cf-4f95-a73c-b061973693c9` | Время уменьшения производительности процессора с классом энергоэффективности 1 | 1…100 (шаг 1) Интервалы проверки | 1 (HIDE) | 2/2 | 2/2 | 2/2 | 2/2 |
| `SHORTTHREADRUNTIMETHRESHOLD` | `d92998c2-6a48-49ca-85d4-8cceec294570` | Порог коротких и долгих потоков | 0…100000 (шаг 1) микросекунд | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `CPDECREASETIME` | `dfd10d17-d5eb-45dd-877a-9a34ddd15c82` | Время уменьшения занятости ядер производительности процессора | 1…100 (шаг 1) Интервалы проверки | 1 (HIDE) | 10/10 | 20/20 | 20/10 | 2/2 |
| `DISTRIBUTEUTIL` | `e0007330-f589-42ed-a401-5ddb10e785d3` | Распределение степени загрузки процессоров, выполняемое служебной программой приостановки ядер | 0=Отключен; 1=Включен | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `CPMAXCORES` | `ea062031-0e34-4ff1-9b6d-eb1059334028` | Максимальное число ядер в состоянии простоя | 0…100 (шаг 1) % | 1 (HIDE) | 100/100 | 100/100 | 100/100 | 100/100 |
| `CPMAXCORES1` | `ea062031-0e34-4ff1-9b6d-eb1059334029` | Максимальное число ядер в состоянии простоя для процессора с классом энергоэффективности 1 | 0…100 (шаг 1) % | 1 (HIDE) | 100/100 | 100/100 | 100/100 | 100/100 |
| `CPHEADROOM` | `f735a673-2066-4f80-a0c5-ddee0cf1bf5d` | Порог операционного запаса параллелизма приостановки ядер для производительности процессора | 0…100 (шаг 1) % | 1 (HIDE) | 20/20 | 20/20 | 20/20 | 20/20 |
| `HETERODECREASETHRESHOLD` | `f8861c27-95e7-475c-865b-13c0cb3f9d6b` | Порог уменьшения уровня производительности при снижении количества процессоров с классом энергоэффективности 1 | нет диапазона/вариантов (сырой DWORD) | 1 (HIDE) | 255/255 | 255/255 | 255/255 | 255/255 |
| `HETERODECREASETHRESHOLD1` | `f8861c27-95e7-475c-865b-13c0cb3f9d6c` | Порог уменьшения уровня производительности процессора для уменьшения числа процессоров с классом энергоэффективности 2 | нет диапазона/вариантов (сырой DWORD) | 1 (HIDE) | 255/255 | 255/255 | 255/255 | 255/255 |
| `HETEROCLASS0FLOORPERF` | `fddc842b-8364-4edc-94cf-c17f60de1c80` | Минимальная производительность процессора с классом энергоэффективности 0 при наличии неприпаркованных процессоров с классом энергоэффективности 1 | 0…100 (шаг 1) % | 1 (HIDE) | 100/100 | 100/100 | 100/100 | 100/100 |

#### Параметры графики — `SUB_GRAPHICS` — `5fb4938d-1ee8-4b0f-9a3c-5036b0ab995c`

| Alias | GUID параметра | Имя (из ОС, ru-RU) | Диапазон / варианты | Attributes | Balanced AC/DC | High AC/DC | Ultimate AC/DC | Power saver AC/DC |
|---|---|---|---|---|---|---|---|---|
| `GPUPREFERENCEPOLICY` | `dd848b2a-8a5d-4451-9ae2-39cd41658f6c` | Политика предпочтений в отношении графического процессора | 0=Нет; 1=Низкое энергопотребление | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/1 |

#### Экран — `SUB_VIDEO` — `7516b95f-f776-4464-8c53-06167f40cc99`

| Alias | GUID параметра | Имя (из ОС, ru-RU) | Диапазон / варианты | Attributes | Balanced AC/DC | High AC/DC | Ultimate AC/DC | Power saver AC/DC |
|---|---|---|---|---|---|---|---|---|
| `VIDEODIM` | `17aaa29b-8b43-4b94-aafe-35f64daaf1ee` | Гасить экран после | 0…4294967295 (шаг 1) Сек. | 1 (HIDE) | 585/285 | 885/585 | 885/285 | 285/105 |
| `VIDEOIDLE` | `3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e` | Отключать экран через | 0…4294967295 (шаг 1) Сек. | 2 (SHOW_AOAC) | 300/180 | 900/600 (на машине изменено: 0/600) | 900/180 | 300/120 |
| `ADVANCEDCOLORQUALITYBIAS` | `684c3e69-a4f7-4014-8754-d45179a56167` | Сдвиг качество Дополнительные цвета | 0=Расширенный цвет экономии энергии смещения; 1=Расширенный цвет визуального смещения качества | 1 (HIDE) | 1/0 | 1/0 | 1/0 | 1/0 |
| `VIDEOCONLOCK` | `8ec4b3a5-6868-48c2-be75-4f3044be88a7` | Время ожидания до отключения экрана блокировки консоли | 0…4294967295 (шаг 1) Сек. | 2 (SHOW_AOAC) | 60/60 | 60/60 | 60/60 | 60/60 |
| `VIDEOADAPT` | `90959d22-d6a1-49b9-af93-bce885ad335b` | Адаптивное отключение экрана | 0=Выкл; 1=Вкл | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `ALLOWDISPLAY` | `a9ceb8da-cd46-44fb-a98b-02af69de4623` | Разрешить политику, требуемую для экрана | 0=Нет; 1=Да | 1 (HIDE) | 1/1 | 1/1 | 1/1 | 1/1 |
| `VIDEONORMALLEVEL` | `aded5e82-b909-4619-9949-f5d71dac0bcb` | Яркость экрана | 0…100 (шаг 1) % | 2 (SHOW_AOAC) | 75/75 | 100/100 | 100/75 | 75/75 |
| — | `f1fbfde2-a960-4165-9f88-50667911ce96` | Уровень яркости экрана в режиме уменьшенной яркости | 0…100 (шаг 1) % | 2 (SHOW_AOAC) | 50/50 | 50/50 | 50/50 | 50/50 |
| `ADAPTBRIGHT` | `fbd9aa66-9553-4097-ba44-ed6e9d65eab8` | Включить адаптивную регулировку яркости | 0=Выкл; 1=Вкл | 2 (SHOW_AOAC) | 0/0 | 0/0 | 0/0 | 0/0 |

#### Поведение питания с поддержкой присутствия — `SUB_PRESENCE` — `8619b916-e004-4dd8-9b66-dae86f806698`

| Alias | GUID параметра | Имя (из ОС, ru-RU) | Диапазон / варианты | Attributes | Balanced AC/DC | High AC/DC | Ultimate AC/DC | Power saver AC/DC |
|---|---|---|---|---|---|---|---|---|
| `HUPRVIDEOIDLE` | `0a7d6ab6-ac83-4ad1-8282-eca5b58308f3` | Время ожидания адаптивного экрана датчика присутствия человека при отсутствии человека | 0…4294967295 (шаг 1) Сек. | 1 (HIDE) | 30/30 | 30/30 | 30/30 | 30/30 |
| `STANDBYRESERVETIME` | `468fe7e5-1158-46ec-88bc-5b96c9e44fd0` | Время ожидания резерва | 0…4294967295 (шаг 1) Сек. | 1 (HIDE) | 0/1200 | 0/1200 | 0/1200 | 0/1200 |
| `STANDBYRESETPERCENT` | `49cb11a5-56e2-4afb-9d38-3df47872e21b` | Процентный уровень для сброса в режиме ожидания | 0…100 (шаг 1) % | 1 (HIDE) | 0/75 | 0/0 | 0/75 | 0/0 |
| `NSENINPUTPRETIME` | `5adbbfbc-074e-4da1-ba38-db8b36b2c8f3` | Время ожидания входного сигнала не с датчика присутствия | 0…4294967295 (шаг 1) Сек. | 1 (HIDE) | 240/240 | 240/240 | 240/240 | 240/240 |
| `STANDBYBUDGETGRACEPERIOD` | `60c07fe1-0556-45cf-9903-d56e32210242` | Льготный период ожидания бюджета | 0…4294967295 (шаг 1) Сек. | 1 (HIDE) | 0/900 | 0/0 | 0/900 | 0/0 |
| `USERPRESENCEPREDICTION` | `82011705-fb95-4d46-8d35-4042b1d20def` | Режим прогнозирования присутствия пользователя | 0=Отключено; 1=Включено | 1 (HIDE) | 0/1 | 0/0 | 0/1 | 0/0 |
| `STANDBYBUDGETPERCENT` | `9fe527be-1b70-48da-930d-7bcf17b44990` | Процент ожидания бюджета | 0…100 (шаг 1) % | 1 (HIDE) | 0/5 | 0/0 | 0/5 | 0/0 |
| `HUPRVIDEODIMAWAY` | `a79c8e0e-f271-482d-8f8a-5db9a18312de` | Время ожидания перед затемнением адаптивного экрана датчика присутствия человека при отсутствии человека | 0…4294967295 (шаг 1) Сек. | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |
| `STANDBYRESERVEGRACEPERIOD` | `c763ee92-71e8-4127-84eb-f6ed043a3e3d` | Льготный период ожидания резерва | 0…4294967295 (шаг 1) Сек. | 1 (HIDE) | 0/300 | 0/0 | 0/300 | 0/0 |
| `HUPRVIDEODIM` | `cf8c6097-12b8-4279-bbdd-44601ee5209d` | Время ожидания перед затемнением адаптивного экрана датчика присутствия человека при отсутствии внимания человека | 0…4294967295 (шаг 1) Сек. | 1 (HIDE) | 5/5 | 5/5 | 5/5 | 5/5 |
| `HUPRVIDEOIDLEINATTENTIVE` | `ee16691e-6ab3-4619-bb48-1c77c9357e5a` | Время ожидания адаптивного отключения экрана датчика присутствия человека при отсутствии внимания человека | 0…4294967295 (шаг 1) Сек. | 1 (HIDE) | 0/0 | 0/0 | 0/0 | 0/0 |

#### Параметры мультимедиа (имя ОС не вернула) — `9596fb26-9850-41fd-ac3e-f7c3c00afd4b`

| Alias | GUID параметра | Имя (из ОС, ru-RU) | Диапазон / варианты | Attributes | Balanced AC/DC | High AC/DC | Ultimate AC/DC | Power saver AC/DC |
|---|---|---|---|---|---|---|---|---|
| — | `10778347-1370-4ee0-8bbd-33bdacaade49` | Изменение качества воспроизведения видео | 0=Изменение качества воспроизведения видео для энергосбережения; 1=Изменение качества воспроизведения видео для повышения производительности | нет (видим) | 1/0 | 1/0 | 1/0 | 1/0 |
| — | `34c7b99f-9a6d-4b3c-8dc7-b6693b78cef4` | При воспроизведении видео | 0=Оптимизация качества видео; 1=Оптимизация; 2=Оптимизация энергосбережения | нет (видим) | 0/1 | 0/0 | 0/1 | 1/2 |

#### Параметры режима энергосбережения — `SUB_ENERGYSAVER` — `de830923-a562-41af-a086-e3a2c6bad2da`

| Alias | GUID параметра | Имя (из ОС, ru-RU) | Диапазон / варианты | Attributes | Balanced AC/DC | High AC/DC | Ultimate AC/DC | Power saver AC/DC |
|---|---|---|---|---|---|---|---|---|
| `ESBRIGHTNESS` | `13d09884-f74e-474a-a852-b6bde8ad03a8` | Весовой коэффициент яркости монитора | 0…100 (шаг 1) % | 1 (HIDE) | 100/70 | 100/70 | 100/70 | 100/70 |
| `ESPOLICY` | `5c5bb349-ad29-4ee2-9d0b-2b25270f7a81` | Политика режима энергосбережения | 0=Пользовательский; 1=Агрессивный | 1 (HIDE) | 0/1 | 0/0 | 0/1 | 0/0 |
| `ESBATTTHRESHOLD` | `e69653ca-cf7f-4f05-aa73-cb833fa90ad4` | Уровень заряда | 0…100 (шаг 1) Заряд батареи в процентах | 1 (HIDE) | 0/20 | 0/0 | 0/20 | 0/20 |

#### Батарея — `SUB_BATTERY` — `e73a048d-bf27-4f12-9731-8b2076e8891f`

| Alias | GUID параметра | Имя (из ОС, ru-RU) | Диапазон / варианты | Attributes | Balanced AC/DC | High AC/DC | Ultimate AC/DC | Power saver AC/DC |
|---|---|---|---|---|---|---|---|---|
| `BATFLAGSCRIT` | `5dbb7c9f-38e9-40d2-9749-4f8a0e9f640f` | Уведомление о почти полной разрядке батареи | 0=Выкл; 1=Вкл | 2 (SHOW_AOAC) | 1/1 | 1/1 | 1/1 | 1/1 |
| `BATACTIONCRIT` | `637ea02f-bbcb-4015-8e2c-a1c7b9c0b546` | Действие почти полной разрядки батарей | 0=Действие не требуется; 1=Сон; 2=Гибернация; 3=Завершение работы | 2 (SHOW_AOAC) | 2/2 | 2/2 | 2/2 | 2/2 |
| `BATLEVELLOW` | `8183ba9a-e910-48da-8769-14ae6dc1170a` | Уровень низкого заряда батарей | 0…100 (шаг 1) % | 2 (SHOW_AOAC) | 10/10 | 10/10 | 10/10 | 10/10 |
| `BATLEVELCRIT` | `9a66d8d7-4ff7-4ef9-b5a2-5a326ca2a469` | Уровень почти полной разрядки батарей | 0…100 (шаг 1) % | 2 (SHOW_AOAC) | 5/5 | 5/5 | 5/5 | 5/5 |
| `BATFLAGSLOW` | `bcded951-187b-4d05-bccc-f7e51960c258` | Уведомление о низком заряде батарей | 0=Выкл; 1=Вкл | 2 (SHOW_AOAC) | 1/1 | 1/1 | 1/1 | 1/1 |
| `BATACTIONLOW` | `d8742dcb-3e6a-4b3c-b3fe-374623cdcf06` | Действие низкого заряда батарей | 0=Действие не требуется; 1=Сон; 2=Гибернация; 3=Завершение работы | 2 (SHOW_AOAC) | 0/0 | 0/0 | 0/0 | 0/0 |
| — | `f3c5027d-cd16-4930-aa6b-90db844a8f00` | Уровень резервной батареи | 0…100 (шаг 1) % | 2 (SHOW_AOAC) | 7/7 | 7/7 | 7/7 | 7/7 |


### 2.5. PPM-профили и оверлеи: почему «Сбалансированная» не так плоха

Microsoft (Processor power management options) описывает профили: `Default`, `LowLatency` (загрузка и запуск приложений), `LowPower`, `GameMode`
(включён Game Mode и запущена игра), `SustainedPerf` (Mixed Reality), `Constrained` (экономия батареи), `ScreenOff`, `Standby`, плюс уровни QoS
(`EntryLevelPerf`, `Background`, `UtilityQos`, `EcoQos`, `MultimediaQos`). На этой машине (`powercfg /listprofiles`, `/queryprofile`):

| Профиль | GUID | Что задаёт в Balanced на этой машине |
|---|---|---|
| GameMode | `d4140c81-ebba-4e60-8561-6918290359cd` | `PROCTHROTTLEMIN` AC = 100, DC = 100 |
| LowLatency | `0da965dc-8fcf-4c0b-8efe-8dd5e7bc959a` | `PROCTHROTTLEMIN` AC = 100, DC = 100 (условие «High Performance Burst», TimeLimit 6 с) |
| SustainedPerf | `0aabb002-a307-447e-9b81-1d819df6c6d0` | `PROCTHROTTLEMIN` AC = 100; `PERFEPP` AC = 0, DC = 33 |

Следствия для hateeze:
- На Balanced с включённым Game Mode Windows **сама** держит минимальное состояние CPU 100 % во время игры. Выигрыш High/Ultimate для самой игры
  поэтому меньше, чем принято думать; заметнее — в паузах (меню, загрузки) и для фоновых процессов.
- Профили/оверлеи действуют **только** в схемах, производных от Balanced (djdallmann; у Ultimate `PROFILE_GAMEMODE` AC = «НЕИЗВЕСТНО»).
  Драйвер AMD 3D V-Cache Performance Optimizer на 7900X3D/7950X3D/9900X3D/9950X3D паркует CCD без V-Cache именно через профиль Game Mode
  (HWBusters: требуется Game Mode = On, драйвер/служба оптимизатора, BIOS «CPPC Dynamic Preferred Cores» = Auto). Отсюда вывод
  (логический, **на X3D-железе НЕ ПРОВЕРЕНО**): на таких CPU не включать High/Ultimate/hateeze-схему и не выключать Game Mode.
- Game Mode на этой машине выключен пользователем (`AutoGameModeEnabled=0`) — профиль `GameMode` сейчас не срабатывает.

### 2.6. Ultimate Performance (`e9a42b02-d5df-448d-aa00-03f14749eb61`)

- Появилась в Windows 10 1803 (Insider build 17101): «расширяет High performance и убирает микрозадержки тонкого управления питанием»
  (Windows Insider Blog, 14.02.2018). Видима из коробки только в Pro for Workstations (как на этой машине); в остальных редакциях шаблон есть,
  но скрыт — добавляется `powercfg /duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 [<НОВЫЙ_GUID>]` (так делает и ChrisTitusTech/winutil:
  `Invoke-WPFUltimatePerformance.ps1` → `/duplicatescheme`, затем `/setactive`; откат у winutil — `powercfg /restoredefaultschemes`, т.е. сброс **всех** схем — так делать не надо).
- **Фактическое отличие от High performance** (AC): только `DISKIDLE` = 0 вместо 1200 с. Проверено на этой машине (diff стоковых значений) и
  по выгрузке Win10 LTSC 2019 (djdallmann `PowerPlanSettingsCompare.csv`: единственное различие — «Turn off hard disk after»).
  DC-значения Ultimate совпадают с Balanced (`PERSONALITY` DC = 2).
- Отличия Ultimate от Balanced на этой машине (AC): `PROCTHROTTLEMIN` 100 vs 0 (Prov), `PROCTHROTTLEMIN1` 100 vs 5, `ASPM` 0 vs 1,
  AHCI HIPM/DIPM 0 vs 1, AHCI Adaptive 0 vs 100 мс, NVMe Primary tolerance 0 vs 15 мс, NVMe Secondary tolerance 0 vs 100 мс,
  `DISKIDLE` 0 vs 1200 с, `STANDBYIDLE` 0 vs 900 с, `PERFBOOSTPOL` 100 vs 60, `CPINCREASETIME` 7 vs 3, `CPOVERUTIL` 60 vs 85, `CPDECREASETIME` 20 vs 10,
  `REMOTEFILESLEEP` 0 vs 1, экран/яркость. Всё остальное — **одинаково** (в том числе `CPMINCORES` 100, `PERFEPP` 0, `PERFBOOSTMODE` 2, `IDLEDISABLE` 0,
  `PERFINCPOL` 2, `PERFINCTHRESHOLD` 30, `PERFDECTHRESHOLD` 10, `PERFCHECK` 15 мс — провижининг Win11 для этого CPU уже сделал Balanced «агрессивной» на AC).
- На системах с Modern Standby (S0 Low Power Idle) High performance/Ultimate обычно недоступны или игнорируются — **НЕ ПРОВЕРЕНО** (на этой машине S0ix нет).
  В коде: если `PowerDuplicateScheme(Ultimate)` вернул ошибку — фолбэк на дублирование Balanced с теми же значениями.

### 2.7. Схема «hateeze Low Latency»

**Назначение и честная оценка.** Детерминированная «производительная» схема: копия Ultimate + выключенные USB selective suspend и USB 3 LPM +
явная запись всех значимых AC-значений (на машинах, где провижининг/OEM поменял шаблоны, Ultimate может отличаться). Эффект ≈ эффекту Ultimate/High:
меньше задержка выхода из энергосберегающих состояний CPU/шины/накопителей, CPU не опускается ниже базовой частоты. Измеримого роста среднего FPS
ожидать **не нужно**; польза — в стабильности задержек при неполной загрузке (киберспорт, высокий FPS при низкой загрузке GPU/CPU) и в устранении
отвалов/«засыпаний» USB-периферии. Оценка: **situational**.

**Когда НЕ предлагать (гейтинг в коде):**
1. Ноутбук/работа от батареи (`GetSystemPowerStatus().BatteryFlag != 128` и есть батарея) — только по явному согласию, DC-значения не трогаем.
2. AMD Ryzen с двумя CCD и 3D V-Cache (асимметричный L3: два L3-кэша разного размера через `GetLogicalProcessorInformationEx(RelationCache)`,
   либо имя CPU `Ryzen 9 (7900X3D|7950X3D|9900X3D|9950X3D)`) — ломает парковку CCD через Game Mode-профиль Balanced.
3. Intel hybrid (`GetSystemCpuSetInformation`: есть `EfficiencyClass > 0`) — Thread Director/подсказки QoS работают в Balanced; High/Ultimate на hybrid
   повышает нагрев/потребление E-ядер, а польза для игр не доказана.
4. Modern Standby (S0ix): `powercfg /a` / `CallNtPowerInformation(SystemPowerCapabilities).AoAc == TRUE` — не предлагать.

**Идентичность схемы.** Фиксированный GUID: `3a66c45b-84b7-4327-bc45-26425f4d2a35` (сгенерирован для hateeze; позволяет идемпотентно находить/обновлять/удалять
схему). Имя: «hateeze Low Latency». Описание: «Схема hateeze: минимальные задержки. Основа — «Максимальная производительность». Не для ноутбуков».

**Алгоритм создания (C#):**
1. Снапшот: GUID текущей активной схемы → сохранить в настройках hateeze (нужно для отката).
2. Если схема `3a66c45b…` уже есть (`PowerEnumerate`) — перейти к шагу 4 (обновить значения).
3. `PowerDuplicateScheme(Ultimate → 3a66c45b…)`; при ошибке — `powercfg /duplicatescheme e9a42b02-… 3a66c45b-…`; при ошибке (нет шаблона/Modern Standby) —
   дублировать Balanced `381b4222-…` (тогда в UI пометить «основа: Сбалансированная»).
4. `PowerWriteFriendlyName`/`PowerWriteDescription` (или `powercfg /changename`).
5. Записать AC-значения из таблицы ниже (`PowerWriteACValueIndex`), DC оставить как в источнике.
6. `PowerSetActiveScheme(3a66c45b…)`. Перечитать значения и показать дифф пользователю.
7. Откат: `PowerSetActiveScheme(<сохранённая схема или Balanced>)`, затем (по желанию пользователя) `PowerDeleteScheme(3a66c45b…)`.

**Значения схемы (AC; DC не меняем):**

| Подгруппа | Параметр (alias) | GUID параметра | AC | Ultimate AC (сток) | Оценка | Комментарий |
|---|---|---|---|---|---|---|
| Процессор `54533251-82be-4824-96c1-47b60b740d00` | PROCTHROTTLEMIN | `893dee8e-2bef-41e0-89c6-b55d0929964c` | 100 | 100 | situational | Не опускаться ниже базовой частоты; на CPPC это минимум 100 % от nominal |
| ″ | PROCTHROTTLEMIN1 | `893dee8e-2bef-41e0-89c6-b55d0929964d` | 100 | 100 | situational | Только для hybrid (класс 1); на прочих CPU не используется |
| ″ | PROCTHROTTLEMAX | `bc5038f7-23e0-4960-96da-33abaf5935ec` | 100 | 100 | proven | 100 % = буст разрешён. <100 % отключает буст на многих CPU — частый «твик для температуры», для игр вреден |
| ″ | CPMINCORES | `0cc5b647-c1df-4637-891a-dec35c318583` | 100 | 100 | situational | Парковка ядер выключена (документировано: 100 % отключает алгоритм) |
| ″ | CPMAXCORES | `ea062031-0e34-4ff1-9b6d-eb1059334028` | 100 | 100 | proven (дефолт) | Не уменьшать |
| ″ | PERFBOOSTMODE | `be337238-0d82-4146-a960-4f3749d470c7` | 2 (Aggressive) | 2 | proven (дефолт) | 0 = буст выключен — грубая ошибка многих «энергосберегающих» твиков |
| ″ | PERFBOOSTPOL | `45bcc044-d885-43e2-8605-ee0ec6e96b59` | 100 | 100 | situational | Только для legacy P-states (не CPPC); безвредно |
| ″ | PERFEPP | `36687f9e-e3a5-4dbf-b1dc-15eb381c6863` | 0 | 0 | situational | Только CPPC v2 в autonomous mode (Intel HWP / AMD CPPC2) |
| ″ | PERFEPP1 | `36687f9e-e3a5-4dbf-b1dc-15eb381c6864` | 0 | 0 | situational | То же для класса 1 (hybrid) |
| ″ | IDLEDISABLE | `5d76a2ca-e8c0-402f-a133-2158492d58ad` | 0 | 0 | — | **Явно 0**: отключение idle — отдельный экспериментальный твик, в схему не входит |
| ″ | SYSCOOLPOL | `94d3a615-a899-4ac5-ae2b-e4d8f634367f` | 1 (Active) | 1 | proven (дефолт) | Активное охлаждение (вентиляторы) прежде троттлинга |
| PCI Express `501a4d13-42af-4429-9fd1-a8218c268e20` | ASPM | `ee12f906-d277-404b-b6da-e5fa1a576df5` | 0 (Откл.) | 0 | situational | Работает, только если BIOS отдал ASPM ОС (_OSC) |
| USB `2a737441-1930-4402-8d77-b2bebba308a3` | USB selective suspend | `48e6b7a6-50f5-4782-a5d4-53bb8f07e226` | **0** | 1 | situational | Главное отличие от Ultimate |
| ″ | USB 3 Link Power Management | `d4e98f31-5ffe-4ce1-be31-1b38b384c009` | **0** (Off) | 2 | situational | Убирает U1/U2 на USB 3.x |
| Жёсткий диск `0012ee47-9041-4b5d-9b77-535fba8b1442` | DISKIDLE | `6738e2c4-e8a5-4a42-b16a-e040e769756e` | 0 (никогда) | 0 | situational | Важно только для HDD |
| ″ | AHCI LPM HIPM/DIPM | `0b2d69d7-a2a1-449c-9680-f91c70521c60` | 0 (Active) | 0 | situational | Только SATA |
| ″ | AHCI LPM Adaptive | `dab60367-53fe-4fbc-825e-521d069d2456` | 0 | 0 | situational | Только SATA |
| ″ | Primary NVMe Power State Transition Latency Tolerance | `fc95af4d-40e7-4b6d-835a-56d131dbc80e` | 0 | 0 | situational | 0 мс ⇒ StorNVMe не выберет неоперационное PS (кроме PS с ENLAT+EXLAT = 0) |
| ″ | Secondary NVMe Power State Transition Latency Tolerance | `dbc9e238-6de9-49e3-92cd-8c2b4946b472` | 0 | 0 | situational | — |
| Сон `238c9fa8-0aad-41ed-83f4-97be242c8f20` | STANDBYIDLE | `29f6c1db-86da-48c5-9fdb-f2b67b1f44da` | 0 (никогда) | 0 | situational | Комфорт (долгие загрузки), не производительность |
| ″ | HIBERNATEIDLE | `9d7815a6-7ee4-497e-8888-515a05f02364` | 0 | 0 | situational | — |
| Беспроводной адаптер `19cbb8fa-5279-450e-9fac-8a3d5fedd0c1` | Режим энергосбережения | `12bbebe6-58d6-4636-95bb-3217ef867c1a` | 0 (Макс. производительность) | 0 | situational | Wi-Fi |
| Без подгруппы `fea3413e-7e05-4911-9a71-700331f1c294` | DEVICEIDLE | `4faab71a-92e5-4726-b531-224559672d19` | 0 (Производительность) | 0 | situational | Политика простоя устройств |

Не трогаем (оставляем как в Ultimate, с обоснованием): `Hub Selective Suspend Timeout` (не действует при выключенном selective suspend), NVMe idle timeouts
(при tolerance = 0 не важны), `NVMe NOPPME` (разрешение превышать мощность неоперационного состояния для фоновых операций контроллера — к задержкам не относится),
`Setting IOC on all TDs` (недокументирован), `LATENCYHINTPERF` 99 %, параметры гетерогенного планирования (Automatic), `THROTTLING` (Auto), interrupt steering,
экран/яркость (личное предпочтение), кнопки/крышка, батарея.

**C# — создание схемы:**

```csharp
public static unsafe class HateezeScheme
{
    public static readonly Guid Id = new("3a66c45b-84b7-4327-bc45-26425f4d2a35");
    const string Name = "hateeze Low Latency";
    const string Description = "Схема hateeze: минимальные задержки. Основа — «Максимальная производительность». Не для ноутбуков.";

    static readonly Guid SUB_NONE = new("fea3413e-7e05-4911-9a71-700331f1c294"), SUB_PROC = new("54533251-82be-4824-96c1-47b60b740d00"),
        SUB_PCIE = new("501a4d13-42af-4429-9fd1-a8218c268e20"), SUB_USB = new("2a737441-1930-4402-8d77-b2bebba308a3"),
        SUB_DISK = new("0012ee47-9041-4b5d-9b77-535fba8b1442"), SUB_SLEEP = new("238c9fa8-0aad-41ed-83f4-97be242c8f20"),
        SUB_WIFI = new("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1");

    static readonly (Guid Sub, Guid Setting, uint Ac)[] Values =
    {
        (SUB_PROC, new("893dee8e-2bef-41e0-89c6-b55d0929964c"), 100), // PROCTHROTTLEMIN
        (SUB_PROC, new("893dee8e-2bef-41e0-89c6-b55d0929964d"), 100), // PROCTHROTTLEMIN1
        (SUB_PROC, new("bc5038f7-23e0-4960-96da-33abaf5935ec"), 100), // PROCTHROTTLEMAX
        (SUB_PROC, new("0cc5b647-c1df-4637-891a-dec35c318583"), 100), // CPMINCORES
        (SUB_PROC, new("ea062031-0e34-4ff1-9b6d-eb1059334028"), 100), // CPMAXCORES
        (SUB_PROC, new("be337238-0d82-4146-a960-4f3749d470c7"), 2),   // PERFBOOSTMODE = Aggressive
        (SUB_PROC, new("45bcc044-d885-43e2-8605-ee0ec6e96b59"), 100), // PERFBOOSTPOL
        (SUB_PROC, new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863"), 0),   // PERFEPP
        (SUB_PROC, new("36687f9e-e3a5-4dbf-b1dc-15eb381c6864"), 0),   // PERFEPP1
        (SUB_PROC, new("5d76a2ca-e8c0-402f-a133-2158492d58ad"), 0),   // IDLEDISABLE = 0 (idle разрешён)
        (SUB_PROC, new("94d3a615-a899-4ac5-ae2b-e4d8f634367f"), 1),   // SYSCOOLPOL = Active
        (SUB_PCIE, new("ee12f906-d277-404b-b6da-e5fa1a576df5"), 0),   // ASPM Off
        (SUB_USB,  new("48e6b7a6-50f5-4782-a5d4-53bb8f07e226"), 0),   // USB selective suspend: запрещено
        (SUB_USB,  new("d4e98f31-5ffe-4ce1-be31-1b38b384c009"), 0),   // USB 3 LPM: Off
        (SUB_DISK, new("6738e2c4-e8a5-4a42-b16a-e040e769756e"), 0),   // DISKIDLE: никогда
        (SUB_DISK, new("0b2d69d7-a2a1-449c-9680-f91c70521c60"), 0),   // AHCI HIPM/DIPM: Active
        (SUB_DISK, new("dab60367-53fe-4fbc-825e-521d069d2456"), 0),   // AHCI Adaptive: 0 мс
        (SUB_DISK, new("fc95af4d-40e7-4b6d-835a-56d131dbc80e"), 0),   // NVMe Primary latency tolerance
        (SUB_DISK, new("dbc9e238-6de9-49e3-92cd-8c2b4946b472"), 0),   // NVMe Secondary latency tolerance
        (SUB_SLEEP, new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da"), 0),  // STANDBYIDLE: никогда
        (SUB_SLEEP, new("9d7815a6-7ee4-497e-8888-515a05f02364"), 0),  // HIBERNATEIDLE: никогда
        (SUB_WIFI, new("12bbebe6-58d6-4636-95bb-3217ef867c1a"), 0),   // Wi-Fi: максимальная производительность
        (SUB_NONE, new("4faab71a-92e5-4726-b531-224559672d19"), 0),   // DEVICEIDLE: производительность
    };

    public static Guid CreateOrUpdate(out Guid previousActive)
    {
        previousActive = PowerReader.GetActive();                   // сохранить в настройки hateeze для отката
        Guid id = Id;
        if (!PowerReader.Schemes().Contains(id))
        {
            if (!TryDuplicate(PowrProf.SCHEME_ULTIMATE, id) && !RunPowercfg($"/duplicatescheme {PowrProf.SCHEME_ULTIMATE} {id}"))
                if (!TryDuplicate(PowrProf.SCHEME_BALANCED, id)) throw new InvalidOperationException("Не удалось создать схему");
        }
        WriteString(id, Name, isDescription: false);
        WriteString(id, Description, isDescription: true);
        foreach (var (sub, setting, ac) in Values)
        {
            Guid s = sub, t = setting;
            uint r = PowrProf.PowerWriteACValueIndex(0, &id, &s, &t, ac);
            if (r != 0 && r != PowrProf.ERROR_FILE_NOT_FOUND)        // параметра может не быть на этой платформе — пропускаем
                throw new System.ComponentModel.Win32Exception((int)r);
        }
        uint ra = PowrProf.PowerSetActiveScheme(0, &id);
        if (ra != 0) throw new System.ComponentModel.Win32Exception((int)ra);
        return id;
    }

    static bool TryDuplicate(Guid source, Guid dest)
    {
        Guid src = source, dst = dest; Guid* pDst = &dst;            // непустой *pDst → «использовать этот GUID» (НЕ ПРОВЕРЕНО, см. §2.2)
        uint r = PowrProf.PowerDuplicateScheme(0, &src, &pDst);
        if (r == 0 && pDst != &dst) PowrProf.LocalFree((nint)pDst); // если API всё-таки выделил новый GUID — освободить
        return r == 0 && PowerReader.Schemes().Contains(dest);
    }

    static void WriteString(Guid scheme, string text, bool isDescription)
    {
        byte[] bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
        fixed (byte* p = bytes)
        {
            uint r = isDescription
                ? PowrProf.PowerWriteDescription(0, &scheme, null, null, p, (uint)bytes.Length)
                : PowrProf.PowerWriteFriendlyName(0, &scheme, null, null, p, (uint)bytes.Length);
            if (r != 0) throw new System.ComponentModel.Win32Exception((int)r);
        }
    }

    public static void Revert(Guid previousActive, bool deleteScheme)
    {
        Guid target = PowerReader.Schemes().Contains(previousActive) && previousActive != Id ? previousActive : PowrProf.SCHEME_BALANCED;
        PowrProf.PowerSetActiveScheme(0, &target);
        if (deleteScheme) { Guid id = Id; PowrProf.PowerDeleteScheme(0, &id); }
    }

    static bool RunPowercfg(string args) { /* Process.Start("powercfg.exe", args), WaitForExit, ExitCode == 0 */ return false; }
}
```

### 2.8. Отдельные параметры — оценка

| Параметр | Что делает | Оценка | Детали |
|---|---|---|---|
| Минимальное состояние процессора (`PROCTHROTTLEMIN`) | Нижняя граница производительности, % от nominal (CPPC) / max P-state | situational | 100 % на AC — ускоряет реакцию при неполной загрузке. На Balanced Windows сама ставит 100 % в профилях GameMode/LowLatency. Для Ryzen с CPPC (на этой машине CPPC: MinPerf 51 %, Max 157 % от 3401 МГц) — частота не опускается ниже базовой, ядра в простое всё равно уходят в C-state. На ноутбуках — заметный нагрев/шум. |
| Максимальное состояние (`PROCTHROTTLEMAX`) | Верхняя граница | proven (оставить 100) | Значение 99 % — известный «твик», отключающий буст (на Intel/AMD с legacy P-states) → минус 10–30 % однопоточной частоты. В каталоге не делаем. |
| Парковка ядер (`CPMINCORES`) | Мин. % незапаркованных логических CPU на NUMA-узел; 100 % = алгоритм парковки выключен (MS) | situational | На этой машине (Win11 23H2, Zen 3) уже 100 % на AC в Balanced/High/Ultimate (DC Balanced = 10 %). Win10 LTSC 2019 (Intel, djdallmann): 100/100. Т.е. на настольном ПК чаще всего ничего не меняет. **Вредно** на dual-CCD X3D и, вероятно, на Intel hybrid (там парковка — часть стратегии). Проверка эффекта: счётчик `\Processor Information(*)\Parking Status` (на этой машине все 0 = не запаркованы). |
| Режим усиления (`PERFBOOSTMODE`) | Как выбирать буст выше nominal | proven (дефолт 2) | MS: 0 Disabled, 1 Enabled, 2 Aggressive, 3 Efficient Enabled, 4 Efficient Aggressive; Win11 добавил 5/6 («…при гарантированном»). Для CPPC non-autonomous: Enabled = целевой буст, Aggressive = максимальный. Не ставить 0. |
| EPP (`PERFEPP`) | Значение в регистр Energy Performance Preference: 0 → 0 (производительность), 100 → 255 (экономия), промежуточные = v·255/100 (MS) | situational | Действует только на CPPC v2 с autonomous mode (Intel HWP; AMD CPPC2 — на Zen 3 **НЕ ПРОВЕРЕНО**, на Linux amd-pstate EPP работает с Zen 2+). На этой машине AC уже 0 во всех схемах (Balanced — через Prov). Дефолт Balanced без провижининга: 33/50 (Win10 LTSC) |
| Отключение простоя (`IDLEDISABLE`) | 1 = процессор не входит в C-states (активное ожидание в C0) | experimental, риск high | MS рекомендует для soft real-time IoT. Минусы: диспетчер задач показывает 100 % CPU; постоянный нагрев и потребление; на Ryzen/Intel с турбобустом теряется запас мощности для одно-/малопоточного буста (valleyofdoom: «не отключайте, если полагаетесь на PBO/Turbo; избегайте при включённом SMT»). Замеров пользы для игр на Zen 3 нет. Только раздел «Эксперимент», с предупреждением о температуре, только AC, обязательно с откатом. |
| Idle demote/promote (`IDLEDEMOTE` `4b92d758-5a24-4851-a470-815d78aee119`, `IDLEPROMOTE` `7b224883-b3cc-4d79-819f-8374152cbe7c`) | Пороги «простоя» для перехода между C-state | experimental | Документированы MS (Win11), но эффект не измерен; на Zen 3 Windows видит лишь 2 ACPI C-state, `IDLESCALING` = 0. Популярное «demote=promote=100» удерживает мелкие C-state — частичный аналог IDLEDISABLE без доказательств пользы. |
| Idle threshold scaling (`IDLESCALING` `6c2993b0-8f48-481f-bcc6-00dd2742aa06`), `IDLECHECK`, `IDLESTATEMAX` `9943e905-9a30-4ec1-9b99-44dd3b76f7a2` | — | experimental (не включаем в каталог) | Не документированы на Learn / нет замеров. `IDLESTATEMAX` («Максимальное состояние простоя», 0…20) — **НЕ ПРОВЕРЕНО**, учитывается ли. |
| PCIe Link State PM (`ASPM`) | L0s/L1 на PCIe-линках | situational | Работает, только если firmware через ACPI `_OSC` передал управление ASPM ОС; иначе настройка ничего не меняет. Возможная польза — меньше задержка выхода из L1 для GPU/NVMe/NIC; известны проблемы ASPM у некоторых сетевых контроллеров (отвалы). MS: для NVMe L1.x нужен «Maximum power savings» — т.е. Off = NVMe не уходит в L1.x. В High/Ultimate уже 0. |
| USB selective suspend (`48e6b7a6…`) | Разрешение драйверам USB-хабов приостанавливать простаивающие порты | situational | Выключение лечит «засыпание»/переподключение мыши, гарнитуры, ЦАП, USB-аудиоинтерфейсов; активные HID с постоянным опросом и так не засыпают. На десктопе риск минимален; на ноутбуке — расход батареи (DC не трогаем). Дефолт 1/1 во всех схемах, вкл. Ultimate. |
| USB 3 Link Power Management (`d4e98f31…`) | U1/U2 состояния линков USB 3.x | situational | 0 = Off убирает задержки выхода из U1/U2 и типичные отвалы USB 3-устройств (Framework KB). Дефолт 2/2 (Moderate) во всех схемах. |
| Hub Selective Suspend Timeout (`0853a681-27c8-4100-a2fd-82013e970683`) | Таймаут простоя хаба до приостановки, мс (дефолт 50) | avoid | При выключенном selective suspend не действует; само по себе на задержки ввода не влияет. |
| Setting IOC on all TDs (`498c044a-201b-4631-a522-5c744ed4e678`) | Прерывание на каждый Transfer Descriptor xHCI | avoid (не трогать) | Не документирован; AC = 1 (Enabled) в Balanced/High/Ultimate. |
| Отключать диск через (`DISKIDLE`) | Остановка шпинделя HDD | situational | Для SSD/NVMe не влияет; для HDD с играми — убирает «подвисания» при раскрутке. |
| NVMe idle timeouts (`d639518a-e56d-4345-8af2-b9f32fb26109` Primary, `d3d55efd-c1ff-424e-9dc3-441be7833010` Secondary) | Время простоя до перехода в F-state | situational (вместе с tolerance) | MS: StorNVMe после таймаута выбирает самое глубокое PS с ENLAT+EXLAT ≤ tolerance. Дефолты: Performance 200 мс / 0 мс(AC) / 2000 мс / 0 мс; Balanced 200/15/2000/100 (AC). Т.е. на High/Ultimate неоперационные PS на AC фактически не используются. |
| NVMe latency tolerances (`fc95af4d…`, `dbc9e238…`) | Допустимая задержка перехода | situational | 0 на AC = минимальная задержка первого I/O после простоя. В High/Ultimate уже 0. |
| NVMe NOPPME (`fc7372b6-ab2d-43ee-8797-15e9841f2cca`) | Non-Operational Power State Permissive Mode | avoid | Не связан с задержками; дефолт 1/0. |
| AHCI LPM (`0b2d69d7…`, `dab60367…`) | HIPM/DIPM (Partial/Slumber), адаптивный таймаут | situational | MS: по умолчанию HIPM-only; через 100 мс в Partial StorAHCI переводит линк в Slumber; DEVSLP через 6 с. 0 = Active — без переходов. Только SATA. |
| Latency sensitivity hint (`LATENCYHINTPERF` `619b7505-003b-4e82-b7a6-4dd29c300971`) | Подъём производительности на события ввода (кнопки мыши, тач, Enter) | experimental | MS: значение = до какого уровня поднять производительность; дефолт 99 % в Balanced/High/Ultimate (Power saver — 0). 100 вместо 99 — эффект не измерен. |
| Гетерогенное планирование (`SCHEDPOLICY` `93b8b6dc-0698-4d1c-9ee4-0644e900c85d`, `SHORTSCHEDPOLICY` `bae08b81-2d5e-4688-ad6a-13243356654b`, `HETEROPOLICY` `7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5`) | Предпочтение P/E-ядер для длинных/коротких потоков | experimental, только Intel hybrid | MS: 0 All, 1 Performant, 2 Prefer performant, 3 Efficient, 4 Prefer efficient, 5 Automatic. Дефолт Win11 23H2: SCHED 5/5, SHORT 2/5; Win10 LTSC: 5/5, 5/5. «Prefer performant» может помочь играм без поддержки Thread Director, но ломает энергосбережение фона — замеров нет. На AMD (кроме гибридных Zen 5/5c мобильных — **НЕ ПРОВЕРЕНО**) не применимо. |
| Сон/гибернация (`STANDBYIDLE`, `HIBERNATEIDLE`, `HYBRIDSLEEP` `94ac6d29-73ce-41a6-809f-6363ba21b47e`) | Таймауты | situational | На производительность в игре не влияют. Дефолт сна AC: Win11 22H2+ = 900 с (эта машина), Win10 = 1800 с; DC 600 / 900. |
| Гибернация целиком (`powercfg /hibernate off`) | Удаляет hiberfil.sys, отключает и Fast Startup | situational | Экономит место на диске (размер ≈ доля RAM). На FPS не влияет. На этой машине уже выключено. |
| Fast Startup (`HiberbootEnabled`) | «Гибридное» выключение (сессия ядра в hiberfil) | situational | Выключение гарантирует полную переинициализацию драйверов при «Завершении работы» (полезно после обновления драйверов/BIOS, лечит часть проблем с uptime/таймерами). Цена — медленнее холодный старт. Дефолт 1 (`HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power\HiberbootEnabled`). В JSON — запись `power.fast-startup-off` из `gaming-graphics-amdgpu.tweaks.json` (не дублируется здесь). |

### 2.9. PowerThrottlingOff

- Ключ: `HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling`, `PowerThrottlingOff` (DWORD) = 1 — выключить «регулирование мощности»
  (EcoQoS/LowQoS для фоновых процессов). Это **официальная политика**: `Power.admx`, политика `PowerThrottlingTurnOff` («Выключить регулирование мощности»,
  Конфигурация компьютера → Административные шаблоны → Система → Управление электропитанием → Параметры регулирования мощности),
  `key="System\CurrentControlSet\Control\Power\PowerThrottling" valueName="PowerThrottlingOff"`, enabled=1, disabled=0,
  supportedOn `SUPPORTED_Windows_10_0_NOSERVER` (проверено в `C:\Windows\PolicyDefinitions\Power.admx` этой машины). Ключ хранится не в `Policies` ⇒
  «preference»: остаётся после снятия политики.
- Дефолт: ключа нет (на этой машине нет) ⇒ revert = `delete`.
- Что даёт: Windows не будет понижать частоту/переносить на E-ядра процессы, которые считает фоновыми (свёрнутый OBS/рекордер, стрим-кодировщик,
  голосовой чат). На саму игру в фокусе не влияет (передний план — High QoS). На десктопе без hybrid эффект минимален.
  Точечная альтернатива без глобального ключа: процесс сам вызывает `SetProcessInformation(ProcessPowerThrottling, ControlMask=EXECUTION_SPEED, StateMask=0)`
  (HighQoS) — hateeze может делать это для выбранных пользователем процессов (как «Режим эффективности: выкл.» в диспетчере задач).
- Оценка: **situational**, риск low (энергопотребление ноутбука). Нужна ли перезагрузка — **НЕ ПРОВЕРЕНО** (в каталоге указано `reboot`, безопасный вариант).
  Microsoft Q&A отмечает, что глобального «официального» выключателя Efficiency Mode нет — `PowerThrottlingOff` его не гарантирует на всех сборках.

### 2.10. Особенности AMD Ryzen

- **AMD Ryzen Balanced plan** — обходной путь 2017 г. для Zen 1 (отключал парковку, снижал пороги/таймеры P-state). Устарел: на этой машине с актуальным
  пакетом чипсета AMD (GPIO 2.2.0.137 от 03/2026, PCI 1.0.0.90, SMBus 5.12.0.44) схемы «AMD Ryzen Balanced» нет; Windows 10 1903+ получила
  CCX-aware планировщик, а провижининг Win11 для Zen 3 уже делает Balanced «быстрой» на AC (`PERFINCPOL`=2, `PERFINCTHRESHOLD`=30, `CPMINCORES`=100,
  `PERFEPP`=0, `PERFCHECK`=15). Ставить/импортировать Ryzen Balanced — **avoid**.
- **CPPC.** Событие Kernel-Processor-Power 55 подтверждает ACPI CPPC (`PerformanceImplementation=3`) и 2 ACPI C-state. В BIOS «CPPC» и
  «CPPC Preferred Cores» должны быть Enabled/Auto: так Windows знает лучшие ядра (preferred cores) и сажает на них «тяжёлые» потоки. Это рекомендация
  (BIOS), приложение может только проверить событие 55 и показать подсказку.
- **Core parking на Zen 3 / Win11:** по умолчанию выключена на AC (см. выше); твик «отключить парковку» на этой машине ничего не меняет.
  Для X3D с двумя CCD парковка — механизм оптимизации, отключать нельзя.
- **AMD PPM Provisioning File Driver** (ставится на Zen 4/Zen 5 X3D, меняет Balanced) на этой машине не установлен (Zen 3, один CCD).

---
## 3. Системный таймер

### 3.1. Основы

- Частота прерываний часов по умолчанию 64 Гц = 15,625 мс; максимум (на x64-ПК) 2 кГц = 0,5 мс. Приложения повышают её через
  `timeBeginPeriod(ms)` (winmm, шаг 1 мс) или недокументированный `NtSetTimerResolution` (ntdll, шаг 100 нс).
- `NtQueryTimerResolution(out Minimum, out Maximum, out Current)` — **внимание к именам**: `Minimum` = самое грубое (156250 = 15,625 мс),
  `Maximum` = самое точное (5000 = 0,5 мс), единицы — 100 нс. Сигнатуры:
  `NTSTATUS NtQueryTimerResolution(PULONG MinimumResolution, PULONG MaximumResolution, PULONG CurrentResolution);`
  `NTSTATUS NtSetTimerResolution(ULONG DesiredResolution, BOOLEAN SetResolution, PULONG CurrentResolution);`
- Повышенная частота повышает точность `Sleep`/ожиданий с таймаутом/waitable timers, но увеличивает число прерываний и мешает глубоким C-state
  (MS, Remarks к `timeBeginPeriod`). На точность `QueryPerformanceCounter` не влияет.

### 3.2. Поведение в Windows 10 2004+ и Windows 11 (документация + замеры)

Microsoft (Remarks к `timeBeginPeriod`):
- До Windows 10 2004 запрос действовал глобально (берётся минимальный запрошенный период среди всех процессов).
- **С Windows 10 2004** функция «больше не влияет на глобальное разрешение»: для вызвавших процессов используется самое точное из запрошенного,
  для остальных Windows не гарантирует разрешение выше системного по умолчанию.
- **С Windows 11**, если процесс-владелец окна полностью перекрыт, свёрнут или иначе невидим/неслышим, Windows не гарантирует ему повышенное разрешение
  (обход — `SetProcessInformation(ProcessPowerThrottling)` с `ControlMask = PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION (0x4)`, `StateMask = 0`).

Замеры на этой машине (`QueryPerformanceCounter` вокруг `Sleep(1)`, 100–400 итераций, система в простое; процесс PowerShell, запрос только для себя):

| Запрос в тестовом процессе | Текущее глобальное (`NtQueryTimerResolution.Current`) | `Sleep(1)` avg | p50 | p99 | max |
|---|---|---|---|---|---|
| нет запроса | 1,0000 мс (кто-то в системе держит 1 мс) | **15,38 мс** | 15,30 | 16,37 | 16,49 |
| `timeBeginPeriod(1)` | 1,0000 мс | 1,278 мс | 1,053 | 2,014 | 2,252 |
| `NtSetTimerResolution(10000)` = 1,0 мс | 1,0000 мс | 1,30–1,54 мс | 1,06–1,53 | 2,51 | 2,65 |
| `NtSetTimerResolution(5000)` = 0,5000 мс | 0,5000 мс | 1,44–1,45 мс | **1,4996–1,4997** | 1,68–2,00 | 2,02 |
| 5010 (0,501 мс) | — | 1,269 | 1,188 | 1,649 | 2,051 |
| 5025–5050 (0,5025–0,505 мс) | — | 1,18–1,21 | 1,04–1,05 | 1,55–1,69 | 2,02 |
| 5070 (0,507 мс) | — | 1,093 | **1,032** | 1,537 | 2,021 |
| 5100–5200 (0,51–0,52 мс) | — | 1,077–1,082 | 1,025–1,042 | 1,52–1,53 | 1,87 |
| 5500 / 6000 | — | 1,114 / 1,144 | 1,098 / 1,114 | 1,56 / 1,61 | 2,03 / 2,05 |

Выводы:
1. Подтверждено «per-process» поведение: при глобальных 1 мс процесс без собственного запроса спит ≈15,4 мс.
2. Ровно 0,5000 мс даёт `Sleep(1)` ≈ 1,5 мс (таймер срабатывает на тиках 0,5 мс, а ожидание округляется вверх «за» 1 мс), тогда как
   0,507–0,52 мс — ≈ 1,03 мс. Это воспроизводит наблюдение valleyofdoom («micro-adjusting», 0,507 мс точнее 0,5 мс у большинства из 30+ участников).
3. Цифры шумные (простой, Hyper-V корневой раздел, разовые прогоны). Для калибровки на машине пользователя — встроенный бенчмарк (§3.4), под нагрузкой.

### 3.3. `GlobalTimerResolutionRequests`

- `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel`, `GlobalTimerResolutionRequests` (DWORD) = 1 — возвращает **глобальное** поведение
  (запрос одного процесса действует на все). valleyofdoom: переменная ядра `KiGlobalTimerResolutionRequests` есть **только** в Windows 11+ и
  Windows Server 2022+ (во всех ядрах Win10 2004–22H2, включая LTSC/Server < 2022, строки нет — ключ игнорируется). На Server 2022 значение = 1 по умолчанию.
  На этой машине (Win11 23H2) значения нет ⇒ дефолт 0/отсутствует ⇒ revert = `delete`. Требует перезагрузки (читается ядром при старте; **НЕ ПРОВЕРЕНО**
  применяется ли «на лету» — ставим `reboot`).
- Даже с ключом на Win11 действует правило «невидимого окна» для процесса, **делающего запрос**, — поэтому процесс-держатель должен вызвать
  `SetProcessInformation(... IGNORE_TIMER_RESOLUTION ..., StateMask = 0)` (valleyofdoom проверил: со свёрнутым окном без этого вызова Sleep(1) снова 15,6 мс).
- Что даёт: игра без собственного запроса (или с запросом 1 мс) получает точность 0,5 мс от внешнего процесса (hateeze). Польза только для
  игр/лимитеров, которые пейсят кадры через `Sleep`/таймеры. Цена: все фоновые процессы снова обслуживаются часто (ради чего MS и сделала изменение),
  выше потребление/прерывания. valleyofdoom в README TimerResolution: «следует использовать только для отладки»; в PC-Tuning: предпочитать per-process
  поведение и RTSS (hybrid-wait), сравнивать варианты замерами. Оценка: **experimental**.

### 3.4. Удержание разрешения таймера приложением hateeze + встроенный MeasureSleep

Что реально даёт процесс hateeze, удерживающий 0,5 мс через `NtSetTimerResolution`:
- **Win10 2004+ / Win11 без ключа** — только самому hateeze. Для игр — **ничего**.
- **Win11 + `GlobalTimerResolutionRequests=1`** — всем процессам; при этом держатель должен отключить игнорирование запроса для невидимого окна.
- **Win10 ≤ 1909** — глобально (как раньше).

Рекомендуемая реализация (экспериментальная функция «Разрешение таймера»):
1. Показывать только на Win11 (build ≥ 22000) и только вместе с ключом `GlobalTimerResolutionRequests` (с перезагрузкой) или на Win10 ≤ 1909.
2. Значение по умолчанию 5070 (0,507 мс) + кнопка «Подобрать автоматически»: прогнать 5000…5200 с шагом 10–25 и выбрать минимальный p50/stdev
   `Sleep(1)` **под нагрузкой** (например, при запущенной игре).
3. Держать запрос в отдельном фоновом потоке/службе, снимать при выходе (`NtSetTimerResolution(x, FALSE, …)`), показывать текущее разрешение.

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static partial class TimerNative
{
    [LibraryImport("ntdll.dll")] public static partial int NtQueryTimerResolution(out uint minimum, out uint maximum, out uint current);
    [LibraryImport("ntdll.dll")] public static partial int NtSetTimerResolution(uint desired, [MarshalAs(UnmanagedType.U1)] bool set, out uint current);

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }
    public const int ProcessPowerThrottling = 4;                         // PROCESS_INFORMATION_CLASS
    public const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    public const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
    public const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x4;

    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessInformation(nint hProcess, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, uint size);
    [LibraryImport("kernel32.dll")] public static partial nint GetCurrentProcess();
}

public sealed class TimerResolutionService : IDisposable
{
    private uint _held;
    public static (double coarseMs, double finestMs, double currentMs) Query()
    {
        TimerNative.NtQueryTimerResolution(out var min, out var max, out var cur);
        return (min / 10000.0, max / 10000.0, cur / 10000.0);
    }

    public uint Hold(uint desired100ns)                 // напр. 5070 = 0,507 мс
    {
        // Win11: не давать системе игнорировать наш запрос, когда окно hateeze свёрнуто/скрыто.
        var st = new TimerNative.PROCESS_POWER_THROTTLING_STATE {
            Version = TimerNative.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
            ControlMask = TimerNative.PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
            StateMask = 0 };
        TimerNative.SetProcessInformation(TimerNative.GetCurrentProcess(), TimerNative.ProcessPowerThrottling, ref st,
                                          (uint)Marshal.SizeOf<TimerNative.PROCESS_POWER_THROTTLING_STATE>());
        Release();
        int status = TimerNative.NtSetTimerResolution(desired100ns, true, out var current);
        if (status != 0) throw new InvalidOperationException($"NtSetTimerResolution: 0x{status:X8}");
        _held = desired100ns;
        return current;
    }

    public void Release() { if (_held != 0) { TimerNative.NtSetTimerResolution(_held, false, out _); _held = 0; } }
    public void Dispose() => Release();

    /// Порт MeasureSleep (valleyofdoom): точность Sleep(1). Запускать в отдельном потоке, лучше под нагрузкой.
    public static (double avg, double p50, double p99, double stdev) MeasureSleep(int samples = 500)
    {
        var d = new double[samples];
        for (int i = 0; i < samples; i++)
        {
            long a = Stopwatch.GetTimestamp();
            Thread.Sleep(1);                                  // = kernel32!Sleep(1) (SleepEx)
            d[i] = (Stopwatch.GetTimestamp() - a) * 1000.0 / Stopwatch.Frequency;
        }
        double avg = d.Average(), sd = Math.Sqrt(d.Sum(x => (x - avg) * (x - avg)) / (samples - 1));
        Array.Sort(d);
        return (avg, d[samples / 2], d[(int)(samples * 0.99) - 1], sd);
    }
}
```

Проверено на этой машине: `SetProcessInformation(GetCurrentProcess(), 4, {Version=1, ControlMask=0x4, StateMask=0}, 12)` → TRUE;
недопустимая маска 0x80 → FALSE, `ERROR_INVALID_PARAMETER` (87) — константы верны. `NtSetTimerResolution(5000/5050/…)` возвращает STATUS_SUCCESS.

Как измерять «реальную точность» для пользователя: (1) MeasureSleep выше (`Sleep(1)` дельта к 1 мс; чем ближе p50 к 1,0 и меньше stdev — тем лучше);
(2) кто держит разрешение — `powercfg /energy` (раздел «Platform Timer Resolution: Outstanding Timer Request», пишет HTML-отчёт — это запись файла, делать
только по кнопке) или WPR/WPA «Power Usage» → Timer Resolution Requests (djdallmann); (3) Sysinternals ClockRes.

### 3.5. bcdedit: что помогает, что вредит

Документация Microsoft (BCDEdit /set): `useplatformclock` — «принудительно использовать платформенные часы как счётчик производительности», **only for debugging**;
`disabledynamictick` — «включает/выключает dynamic tick», **only for debugging**; `useplatformtick` — «тик часов только от платформенного источника, синтетические
таймеры запрещены» (Win8+), **only for debugging**; `tscsyncpolicy` Default | Legacy | Enhanced — **only for debugging**.

| Опция | Что делает | Оценка | Обоснование / замеры |
|---|---|---|---|
| `useplatformclock yes` | QPC от HPET/ACPI PM timer вместо TSC | **avoid** | MS: чтение платформенного таймера ≈ **0,8–1,0 мкс** на вызов и требует системного вызова; TSC — десятки–сотни тактов без перехода в ядро. На этой машине QPC (TSC) ≈ 23 нс/вызов. Игры вызывают QPC тысячи раз за кадр → прямые потери FPS и рост задержек (сообщения о падении загрузки GPU 96 % → 44 % на «HPET bug» системах, overclockers.at). |
| `bcdedit /deletevalue useplatformclock` | Вернуть дефолт (TSC) | **proven** | Восстановление штатного поведения, если твикер включил HPET. Детект: строка `useplatformclock        Yes` в `bcdedit /enum {current}` |
| `disabledynamictick yes` | Отключает «бестиковый» режим простоя (периодические прерывания часов всегда) | experimental | Документированного/измеренного выигрыша для игр нет; известен как средство от редких проблем таймера на отдельных платформах. Минус — больше прерываний и потребление (ноутбуки). Требует перезагрузки. |
| `useplatformtick yes` | Тик только от платформенного таймера | avoid | Нет доказательств пользы; есть сообщения о росте DPC-задержек/джиттера (Blur Busters), MS — только отладка. В актуальной редакции PC-Tuning valleyofdoom не рекомендуется (раздел удалён). |
| `tscsyncpolicy Enhanced/Legacy` | Политика синхронизации TSC | avoid | Windows по умолчанию использует `Default` (не Enhanced — проверено valleyofdoom через `HalpTscSyncPolicy` в WinDbg); djdallmann не нашёл влияния в событиях HAL. |

Поведение при наличии гипервизора (на этой машине `hypervisorlaunchtype Auto`, VBS работает): корневой раздел получает время через enlightenments
гипервизора (reference TSC), QPC = 10 МГц. Влияние bcdedit-опций таймера в этом режиме **НЕ ПРОВЕРЕНО** (и не должно проверяться на этой машине).

Чтение BCD в коде: `bcdedit /enum {current}` (имена элементов выводятся по-английски даже в ru-RU: `identifier`, `useplatformclock`, значения `Yes/No`) —
парсить построчно `^(\w+)\s+(.+)$`. Альтернатива — WMI `root\wmi` `BcdStore`/`BcdObject` (сложнее). Запись — `bcdedit /set {current} <opt> <value>` /
`bcdedit /deletevalue {current} <opt>`; при включённом BitLocker изменения BCD могут вызвать запрос ключа восстановления — предупреждать пользователя.

### 3.6. HPET в BIOS

Рекомендация, а не твик: **оставить как есть** (обычно Enabled). Windows не использует HPET для QPC, если TSC пригоден, а `useplatformclock` не задан;
отключение HPET в BIOS не даёт доказанного выигрыша и на многих AM4-платах отсутствует как опция. Если пользователь когда-то форсировал HPET через bcdedit —
лечится `deletevalue useplatformclock` (см. выше). Оценка «выключить HPET в BIOS» — **avoid** (бесполезно).

---
## 4. Планировщик

### 4.1. `Win32PrioritySeparation`

`HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl\Win32PrioritySeparation` (DWORD). Дефолт **2** (значение на этой машине), допустимо 0x0–0x3F;
ядро читает только младшие 6 бит (valleyofdoom: значения > 0x3F повторяют младшие). Формат `XXYYZZ` (TechNet cc976120):

| Биты | Поле | 00 | 01 | 10 | 11 |
|---|---|---|---|---|---|
| 5–4 `XX` | Длина интервала (квант) | по умолчанию (клиент: короткий; сервер: длинный) | длинный | короткий | по умолчанию |
| 3–2 `YY` | Переменный/фиксированный | по умолчанию (клиент: переменный; сервер: фиксированный) | переменный | фиксированный | по умолчанию |
| 1–0 `ZZ` | Буст переднего плана (`PsPrioritySeparation`) | 1:1 (0) | 2:1 (1) | 3:1 (2) | 3:1 (2) |

Кванты (Windows Internals, в «единицах кванта» = 1/3 тика часов; индекс = `PsPrioritySeparation`): Short Variable 6/12/18; Short Fixed 18/18/18;
Long Variable 12/24/36; Long Fixed 36/36/36. При «фиксированном» кванте передний план не получает длиннее квант, но `PsPrioritySeparation` всё равно
используется как буст приоритета после ожидания у потоков процесса переднего плана (Windows Internals, цитата у valleyofdoom).

| Значение | Биты | Смысл (клиентская Windows) | Комментарий |
|---|---|---|---|
| **2** (дефолт) | 00 00 10 | короткий, переменный, 3:1 | Тождественно 0x26 на клиенте |
| 0x26 = 38 | 10 01 10 | короткий, переменный, 3:1 | То, что пишет UI «Оптимизировать работу: программ» (по данным сообществ; при первом переключении в UI) |
| 0x18 = 24 | 01 10 00 | длинный, фиксированный, 1:1 | UI «служб, работающих в фоновом режиме» (серверный профиль) |
| 0x28 = 40 | 10 10 00 | короткий, фиксированный, 1:1 (PsPrioSep 0) | Популярный «киберспортивный» вариант |
| 0x2A = 42 | 10 10 10 | короткий, фиксированный, 3:1 (PsPrioSep 2) | Популярный вариант |
| 0x14/0x16, 0x24/0x25 и т.д. | — | см. полную таблицу valleyofdoom (research.md §5) | — |

- Применяется **сразу**, без перезагрузки: djdallmann проверил в WinDbg (`PsPrioritySeparation`, `PspForegroundQuantum`, `QuantumReset` у процессов меняются на лету).
- Доказательств (замеров FPS/латентности), что 0x28/0x2A/0x18 лучше дефолта для игр, **нет** ни у valleyofdoom, ни у djdallmann (они разбирали только семантику).
  Оценка: **experimental**, откат — 2. Риск low (влияет на отзывчивость фоновых задач/записи).

### 4.2. MMCSS (Multimedia Class Scheduler Service)

**Как работает.** Драйвер `mmcss.sys` (служба `MMCSS`, KERNEL_DRIVER, Start = 2 Auto, на этой машине RUNNING). Поток **сам** регистрируется в задаче вызовом
`AvSetMmThreadCharacteristicsW(L"Games", &taskIndex)` / `AvSetMmMaxThreadCharacteristics` (avrt.dll), после чего MMCSS поднимает его приоритет по категории:
High → 23–26, Medium → 16–22, Low → 8–15, исчерпавшие квоту → 1–7 (MS Learn). **Потоки, которые не вызывали `AvSetMmThreadCharacteristics`, MMCSS не трогает** —
поэтому правки `Tasks\Games` влияют только на игры/движки, которые явно регистрируют потоки как «Games» (таких меньшинство; аудиодвижок Windows `audiodg`/WASAPI
использует «Audio»/«Pro Audio»).

**Документированная семантика (MS Learn, редакция 2025-07-14):**
- `SystemResponsiveness` — % CPU, гарантированный низкоприоритетным задачам. Значения, не кратные 10, округляются **вниз** до кратного 10.
  **Значения ниже 10 и выше 100 зажимаются до 20**; 100 отключает MMCSS (драйвер возвращает `STATUS_SERVER_DISABLED`). ⇒ популярное `SystemResponsiveness=0`
  **равно дефолту 20**; минимально эффективное — 10.
- `Priority` 1–8; **для `Scheduling Category = High` всегда трактуется как 2**.
- `GPU Priority` (0–31) — «This priority is not yet used»; `SFIO Priority` — «This value is not used».
- `Clock Rate` — с Windows 7 больше не гарантирует частоту таймера (только подсказка).
- `Affinity` 0x00 и 0xFFFFFFFF — «аффинитет не используется».
- `NetworkThrottlingIndex` — в текущей статье не описан. По Марку Руссиновичу (2007): пока воспроизводится мультимедиа под MMCSS, NDIS «индицирует» не более
  10 пакетов/мс (10 000 pps ≈ 15 МБ/с при 1500 байт); ошибка с несколькими NIC усиливала ограничение. `0xFFFFFFFF` отключает. Для игрового трафика
  (сотни–тысячи pps) лимит недостижим ⇒ на пинг/потери не влияет; эффект есть только при гигабитной загрузке во время воспроизведения медиа.

**Что читает `mmcss.sys` при загрузке** (djdallmann, трасса WPR): в `SystemProfile` — `SystemResponsiveness`, `SchedulerTimerResolution`, `SchedulerPeriod`,
`NoLazyMode`, `NetworkThrottlingIndex`, `MaxThreadsTotal`, `MaxThreadsPerProcess`, `LazyModeTimeout`, `IdleDetectionCycles`; в каждой задаче — `Scheduling Category`,
`Priority When Yielded`, `Priority`, `Latency Sensitive`, `Clock Rate`, `BackgroundPriority`, `Background Only`, `Affinity`. **`GPU Priority` и `SFIO Priority` в трассе
не читались** — согласуется с «not used» в документации. Значения `NoLazyMode`, `LazyModeTimeout`, `SchedulerPeriod`, `SchedulerTimerResolution`, `IdleDetectionCycles`,
`MaxThreads*`, `Latency Sensitive`, `Priority When Yielded` **не документированы**; их встроенные дефолты в `mmcss.sys` **НЕ ПРОВЕРЕНЫ** (на этой машине в реестре отсутствуют).

**Все значения на этой машине** (`reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" /s`, Win11 23H2 — сток):

`SystemProfile`: `NetworkThrottlingIndex` = 0xA (10), `SystemResponsiveness` = 0x14 (20). Отсутствуют: `LazyModeTimeout`, `NoLazyMode`, `IdleDetectionCycles`,
`SchedulerPeriod`, `SchedulerTimerResolution`, `MaxThreadsTotal`, `MaxThreadsPerProcess`.

| Задача (`Tasks\…`) | Affinity | Background Only | BackgroundPriority | Clock Rate | GPU Priority | Priority | Scheduling Category | SFIO Priority | Latency Sensitive |
|---|---|---|---|---|---|---|---|---|---|
| Audio | 0 | True | — (нет) | 10000 (0x2710) | 8 | 6 | Medium | Normal | — (нет) |
| Capture | 0 | True | — | 10000 | 8 | 5 | Medium | Normal | — |
| DisplayPostProcessing | 0 | True | 8 | 10000 | 8 | 8 | High | Normal | — |
| Distribution | 0 | True | — | 10000 | 8 | 4 | Medium | Normal | — |
| Games | 0 | **False** | — | 10000 | 8 | **2** | **Medium** | **Normal** | — |
| Playback | 0 | False | 4 | 10000 | 8 | 3 | Medium | Normal | — |
| Pro Audio | 0 | False | — | 10000 | 8 | 1 | High | Normal | — |
| Window Manager | 0 | True | — | 10000 | 8 | 5 | Medium | Normal | — |

Типы: `Affinity`, `BackgroundPriority`, `Clock Rate`, `GPU Priority`, `Priority` — REG_DWORD; `Background Only`, `Scheduling Category`, `SFIO Priority` — REG_SZ.
Дефолты на Win10 22H2 — по всем известным выгрузкам такие же; **на Win10 этой командой НЕ ПРОВЕРЕНО** (машины нет).

**Оценки:**
- `SystemResponsiveness` 10 — experimental (только для MMCSS-потоков; 0 бессмыслен — зажимается до 20).
- `NetworkThrottlingIndex` 0xFFFFFFFF — experimental (на игры не влияет; помогает только при гигабитных загрузках во время медиа).
- `Tasks\Games` «Priority 6 / Scheduling Category High / SFIO High / GPU Priority 8» — experimental: SFIO/GPU не используются, Priority при High трактуется как 2,
  а работает всё это только для потоков, зарегистрированных как «Games».
- `NoLazyMode`/`LazyModeTimeout` — experimental, не документированы.
- Отключение службы MMCSS — **avoid** (аудиодвижок теряет защиту от глитчей; DWM/медиа-потоки теряют приоритет).

### 4.3. IFEO PerfOptions (`CpuPriorityClass`, `IoPriority`, `PagePriority`)

`HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\<game>.exe\PerfOptions`:

| Значение | Тип | Значения | Комментарий |
|---|---|---|---|
| `CpuPriorityClass` | DWORD | 1 Idle, 2 Normal, 3 High, 5 BelowNormal, 6 AboveNormal (Microsoft Q&A; 4 = Realtime **не применяется**/не используется — **НЕ ПРОВЕРЕНО**) | Класс приоритета задаётся при создании процесса — работает и для процессов, защищённых античитом от `SetPriorityClass` (Process Lasso описывает это как свою функцию «permanent priority via IFEO»). |
| `IoPriority` | DWORD | 0 VeryLow, 1 Low, 2 Normal (дефолт), 3 High (по `IO_PRIORITY_HINT`) | Повышение до 3 через IFEO — **НЕ ПРОВЕРЕНО** (High обычно зарезервирован). Понижение (0–1) работает (dumpanalysis.org: notepad с 1/1). |
| `PagePriority` | DWORD | 1–5 (5 = MEMORY_PRIORITY_NORMAL, дефолт) | Выше 5 для пользовательских процессов не бывает ⇒ «повышать» нечем; только понижение для фоновых программ. |

- Работает ли: да, ядро применяет PerfOptions при создании процесса (подтверждения сообществ, Process Lasso; на этой машине PerfOptions нет ни для одного exe — **на
  этой машине не проверялось**, т.к. требует записи).
- Эффект для игр: High-класс поднимает базовый приоритет потоков игры до 13 → выигрывает у фоновых Normal-процессов при 100 % загрузке CPU; при свободных ядрах
  эффекта нет. Риски: голодание собственных вспомогательных процессов (лаунчер, оверлеи, голосовой чат на Normal), рост задержки ввода в системе при «зависании» игры.
- Античиты: IFEO — классический вектор persistence (ключ `Debugger`), его мониторят EDR (Elastic rule «IFEO injection»). Подтверждённых банов за `PerfOptions`
  нет, но гарантий никто не даёт; Vanguard/FACEIT политики по этому ключу публично не описывают — **НЕ ПРОВЕРЕНО**. Никогда не трогать `Debugger`/`GlobalFlag`/`MitigationOptions`.
- Оценка: `CpuPriorityClass=3` — **experimental**, риск medium; `IoPriority`/`PagePriority` для игр — **avoid** (повышать нечем/не проверено).
- Откат: удалить значение (и ключ `PerfOptions`, если пуст; ключ `<exe>` не удалять, если в нём есть другие значения — их мог создать не hateeze).

---

## 5. CPU: прочее

| Тема | Оценка | Детали |
|---|---|---|
| SMT / Hyper-Threading | experimental (только BIOS) | valleyofdoom: отключение уменьшает конкуренцию за ресурсы ядра и джиттер, но может как помочь, так и навредить — **только бенчмарком**. 5700X 8C/16T: многие современные игры используют > 8 потоков. Приложение — только рекомендация. |
| CPPC / CPPC Preferred Cores | proven (оставить включёнными, BIOS) | Без CPPC Windows не знает «лучшие» ядра; на этой машине CPPC активен (событие 55). Проверка в приложении — читать событие 55 (`PerformanceImplementation`). |
| Spectre/Meltdown (`FeatureSettingsOverride`/`FeatureSettingsOverrideMask` в `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management`) | **avoid** | MS (Windows client guidance): «выключить CVE-2017-5715 + CVE-2017-5754» = Override 3, Mask 3; «дефолт» = Override 0, Mask 3 (или удалить оба). На AMD Meltdown неприменим ⇒ выигрыша от его «выключения» нет; на Zen 4 отключение Spectre v2 может даже снизить производительность (Phoronix у valleyofdoom); часть античитов (FACEIT) требует митигаций. Риск безопасности высокий. Дефолт: значений нет (эта машина). |
| Удаление/переименование `mcupdate_AuthenticAMD.dll` / `mcupdate_GenuineIntel.dll` | **avoid** | Отключает загрузку микрокода ОС (исправления безопасности и стабильности), ломается при обновлениях Windows. |
| «AMD Ryzen Balanced» | **avoid** | Устарел (см. §2.10). |
| Core parking на Ryzen | situational | См. §2.8; на dual-CCD X3D — не трогать. |
| Interrupt steering (`SUB_INTSTEER` `48672f38-7a9a-4bb2-8bf8-3d85be19de4e`, MODE `2bfc24f9-5ea2-4801-8213-3dbae01aa39d`) | avoid (не включаем) | Режимы 0–6 (по умолчанию/любой/…/Процессор 0/1). Не документировано для клиентских ОС; управление прерываниями — тема `input`/`network` (MSI, affinity). |

---

## 6. Как проверять эффект (для встроенной диагностики hateeze)

- **Парковка:** `\Processor Information(*)\Parking Status` (0 = не запаркован) — `PerformanceCounter`/PDH.
- **Частота/буст:** `\Processor Information(_Total)\% Processor Performance` (на этой машине 127 % при активной Ultimate-схеме), `Processor Frequency`, `% of Maximum Frequency`.
- **Возможности PPM:** событие 55 `Microsoft-Windows-Kernel-Processor-Power` (тип idle, CPPC, Nominal/Max/Min %).
- **Точность таймера:** MeasureSleep (§3.4); текущее разрешение — `NtQueryTimerResolution`.
- **QPC-источник:** замер стоимости `Stopwatch.GetTimestamp()` (≈ 20–40 нс ⇒ TSC; ≈ 800+ нс ⇒ HPET/PM timer, значит кто-то включил `useplatformclock`).
- **DPC/ISR:** LatencyMon / `xperf` (вне приложения); **кадры:** PresentMon/FrameView (вне приложения).
- **Активная схема и значения:** `PowerGetActiveScheme`, `PowerReadAC/DCValueIndex`; PPM-профили — `powercfg /queryprofile`.

---

## 7. Риски, «НЕ ПРОВЕРЕНО» и открытые вопросы

**НЕ ПРОВЕРЕНО (и почему):**
1. Любые операции записи (PowerWrite*, PowerDuplicateScheme с заданным GUID, PowerSetActiveScheme, bcdedit /set, reg add) — запрещены правилом «только чтение».
   Сигнатуры записи — по документации Microsoft. Перед релизом проверить на тестовой ВМ/машине: создание/удаление схемы hateeze, откат.
2. Windows 10 22H2, Windows 11 24H2/25H2 — машин нет; дефолты Win10 сверены только с выгрузкой djdallmann (Win10 LTSC 2019, Intel i7-6700K). Отличия, которые известны:
   сон AC 1800 с (Win10) vs 900 с (Win11 22H2+), экран AC 600 vs 300, `SHORTSCHEDPOLICY` 5/5 vs 2/5, Balanced AC агрессивность зависит от провижининга CPU.
3. Поведение на AMD dual-CCD X3D и Intel hybrid при High/Ultimate — вывод логический (профили работают только в Balanced + описание AMD-оптимизатора), без замеров.
4. Поддержка EPP (autonomous CPPC) на Zen 3 под Windows.
5. Нужна ли перезагрузка для `PowerThrottlingOff` и `GlobalTimerResolutionRequests` (указано `reboot` как безопасное).
6. Встроенные дефолты `mmcss.sys` для недокументированных значений (`LazyModeTimeout`, `NoLazyMode`, `SchedulerPeriod` и др.).
7. IFEO `IoPriority=3` и `CpuPriorityClass=4`; позиция Vanguard/FACEIT/EAC по `PerfOptions`.
8. `FeatureSettings=0x20000` в Memory Management на этой машине — происхождение/назначение не документировано.
9. Учитывает ли `PowerReadACDefaultIndex` значения `ProvAcSettingIndex`.
10. Modern Standby (S0ix): доступность High/Ultimate и поведение `PowerDuplicateScheme`.

**Риски для пользователя (показывать в UI):** нагрев/потребление и шум при IDLEDISABLE и min state 100 % (особенно ноутбуки); отвалы части NIC при ASPM-манипуляциях — наоборот,
выключение ASPM обычно лечит; HPET/useplatformclock — падение FPS; Spectre — безопасность; IFEO — потенциальные вопросы античитов; изменения BCD при BitLocker.

**Открытые вопросы для команды:**
- Предлагать ли hateeze-схему на Balanced-основе для X3D/hybrid (только USB/AHCI/NVMe/ASPM-часть), или не предлагать вообще?
- Нужна ли функция «Разрешение таймера» в v1, учитывая, что без `GlobalTimerResolutionRequests` она бесполезна для игр, а с ним — `experimental`?
- Где хранить снапшоты для отката (рекомендация: `%ProgramData%\hateeze\backup\*.json` с версией ОС и GUID схемы).

---

## 8. Каталог `power-cpu-timer-scheduler.tweaks.json` — соглашения

- Формат строго по ТЗ (массив объектов; Action-типы `reg`, `bcd`, `service`, `power`, `powershell`). DWORD — десятичными числами
  (`NetworkThrottlingIndex` = 4294967295 = 0xFFFFFFFF).
- `power`-действия применяются к **активной** схеме (через `PowerWriteACValueIndex`/`PowerWriteDCValueIndex` + `PowerSetActiveScheme`). Поле `alias` — справочное
  (у USB/NVMe/AHCI алиасов нет — там указано описательное имя). Во всех power-твиках меняется **только AC**; `dc` в `apply` равен дефолту Balanced
  (чтобы не «ломать» ноутбуки на батарее).
- `revert` для `power` — значения стоковой «Сбалансированной» на этой машине (Win11 23H2, Zen 3, с учётом провижининга) либо, где явно сказано в `osNote`, —
  непровижиненный дефолт Windows. **Приоритет у снапшота**, сделанного приложением перед применением (см. §2.1).
- `"{{value}}"` — подстановка выбранного значения для `kind: value|select`. В `scheduler.ifeo-cpu-priority-high` подстановка — **строка** (имя exe) и стоит в `path`.
- Записи с `evidence: "avoid"` — защита от повторного добавления вредных твиков; у части из них `apply` заполнен реальными значениями **только для документации**
  (чтобы никто не «изобрёл» неверные), в UI их не показывать.
- Записи-«рекомендации» (BIOS: CPPC, SMT, HPET; X3D-гейтинг; служба удержания таймера) имеют пустые `apply`/`revert` — это информационные карточки или фичи,
  реализуемые кодом приложения (описано в `description`/`evidenceNote`).
- `detect` заполнен только там, где «применено» нельзя вычислить сравнением `apply` с текущим состоянием: схема hateeze (по активному GUID),
  раскрытие скрытых параметров (по `Attributes`), гибернация (`HibernateEnabled` = 0). Для `powershell`-detect сравнивать stdout скрипта (`True`/`False`) с `equals`.
  Detect-скрипты только читают (проверено запуском на этой машине: оба вернули `False`).
- `powershell`-скрипты — фолбэк; предпочтительная реализация схемы hateeze — нативный C# из §2.7. Скрипты рассчитаны на Windows PowerShell 5.1, проверены
  парсером (`System.Management.Automation.Language.Parser`, 0 ошибок), **не исполнялись**. Содержат кириллицу: передавать через `-EncodedCommand`
  (UTF-16LE base64) или сохранять во временный `.ps1` в **UTF-8 с BOM**. Не добавлять `$ErrorActionPreference='Stop'` вместе с `2>$null` у нативных
  команд (в 5.1 это превращает stderr в terminating error).
- Фиксированный GUID схемы hateeze: `3a66c45b-84b7-4327-bc45-26425f4d2a35`.
- `power.fast-startup-off` (HiberbootEnabled 0/1) в этот каталог **не включён**, потому что уже есть в `gaming-graphics-amdgpu.tweaks.json`
  с теми же действиями (иначе дублируется id). Оценка там та же — situational.

### 8.1. Сводка каталога (45 записей)

| id | Название | Оценка | Риск | Перезагрузка | ОС | Вендор |
|---|---|---|---|---|---|---|
| `cpu.cppc-preferred-cores-bios` | CPPC и Preferred Cores в BIOS (рекомендация) | proven | low | reboot | win10, win11 | amd |
| `power.boost-mode` | Режим усиления (буста) процессора | proven | low | none | win10, win11 | any |
| `power.show-hidden-settings` | Показать скрытые параметры в «Электропитании» | proven | low | none | win10, win11 | any |
| `timer.remove-forced-hpet` | Убрать принудительный HPET (useplatformclock) | proven | low | reboot | win10, win11 | any |
| `cpu.x3d-dual-ccd-keep-balanced` | Ryzen X3D с двумя CCD: оставить «Сбалансированную» и Game Mode | situational | low | none | win10, win11 | amd |
| `power.ahci-link-pm-off` | SATA: выключить энергосбережение линка (AHCI LPM) | situational | low | none | win10, win11 | any |
| `power.core-parking-off` | Отключить парковку ядер (от сети) | situational | medium | none | win10, win11 | any |
| `power.disk-idle-never` | Не отключать жёсткий диск (от сети) | situational | low | none | win10, win11 | any |
| `power.epp-performance` | EPP: приоритет производительности (от сети) | situational | low | none | win10, win11 | any |
| `power.hateeze-low-latency-scheme` | Схема питания «hateeze Low Latency» | situational | low | none | win10, win11 | any |
| `power.hibernate-off` | Отключить гибернацию | situational | low | none | win10, win11 | any |
| `power.min-processor-state` | Минимальное состояние процессора (от сети) | situational | low | none | win10, win11 | any |
| `power.nvme-latency-tolerance-zero` | NVMe: без глубоких состояний простоя (от сети) | situational | low | none | win10, win11 | any |
| `power.pcie-aspm-off` | Выключить энергосбережение PCI Express (ASPM) | situational | low | none | win10, win11 | any |
| `power.power-throttling-off` | Выключить регулирование мощности (Power Throttling) | situational | low | reboot | win10, win11 | any |
| `power.sleep-never-ac` | Не переходить в сон от сети | situational | low | none | win10, win11 | any |
| `power.usb-selective-suspend-off` | Запретить временное отключение USB-портов | situational | low | none | win10, win11 | any |
| `power.usb3-lpm-off` | Выключить USB 3 Link Power Management | situational | low | none | win10, win11 | any |
| `cpu.smt-disable-bios` | Отключить SMT/Hyper-Threading (BIOS) | experimental | medium | reboot | win10, win11 | any |
| `power.hetero-prefer-performant` | Intel hybrid: предпочитать производительные ядра | experimental | medium | none | win10, win11 | intel |
| `power.idle-demote-promote` | Пороги понижения/повышения состояния простоя | experimental | medium | none | win10, win11 | any |
| `power.idle-disable` | Отключить состояния простоя CPU (C-states) | experimental | high | none | win10, win11 | any |
| `power.latency-hint-perf-100` | Подсказка о задержке: 100 % производительности | experimental | low | none | win10, win11 | any |
| `scheduler.ifeo-cpu-priority-high` | Высокий приоритет CPU для игры (IFEO) | experimental | medium | none | win10, win11 | any |
| `scheduler.mmcss-games-task` | MMCSS: приоритет задачи «Games» | experimental | medium | reboot | win10, win11 | any |
| `scheduler.mmcss-network-throttling-off` | MMCSS: отключить ограничение сети при воспроизведении медиа | experimental | low | reboot | win10, win11 | any |
| `scheduler.mmcss-no-lazy-mode` | MMCSS: отключить «ленивый» режим (NoLazyMode) | experimental | medium | reboot | win10, win11 | any |
| `scheduler.mmcss-system-responsiveness` | MMCSS: резерв CPU для фоновых задач | experimental | low | reboot | win10, win11 | any |
| `scheduler.win32-priority-separation` | Разделение приоритетов (Win32PrioritySeparation) | experimental | low | none | win10, win11 | any |
| `timer.disable-dynamic-tick` | Отключить dynamic tick | experimental | medium | reboot | win10, win11 | any |
| `timer.global-timer-resolution-requests` | Глобальное разрешение таймера (Windows 11) | experimental | medium | reboot | win11 | any |
| `timer.resolution-service` | Удерживать разрешение таймера (служба hateeze) | experimental | low | none | win10, win11 | any |
| `cpu.amd-ryzen-balanced-plan` | Схема «AMD Ryzen Balanced» | avoid | low | none | win10, win11 | amd |
| `cpu.microcode-dll-rename` | Отключить загрузку микрокода ОС (переименование mcupdate_*.dll) | avoid | high | reboot | win10, win11 | any |
| `cpu.spectre-meltdown-mitigations-off` | Отключить защиту от Spectre v2/Meltdown | avoid | high | reboot | win10, win11 | any |
| `power.max-processor-state-99` | Максимальное состояние процессора 99 % (антитвик) | avoid | high | none | win10, win11 | any |
| `power.nvme-noppme` | NVMe: режим NOPPME | avoid | low | none | win10, win11 | any |
| `power.usb-hub-suspend-timeout` | Таймаут приостановки USB-хаба | avoid | low | none | win10, win11 | any |
| `power.usb-ioc-all-tds` | USB: прерывание на каждый TD («Setting IOC on all TDs») | avoid | medium | none | win10, win11 | any |
| `scheduler.ifeo-io-page-priority` | IFEO: IoPriority / PagePriority для игры | avoid | medium | none | win10, win11 | any |
| `scheduler.mmcss-disable` | Отключить службу MMCSS | avoid | high | reboot | win10, win11 | any |
| `timer.hpet-bios-disable` | Выключить HPET в BIOS | avoid | low | reboot | win10, win11 | any |
| `timer.tscsyncpolicy` | Политика синхронизации TSC (tscsyncpolicy) | avoid | medium | reboot | win10, win11 | any |
| `timer.useplatformclock-force-hpet` | Принудительный HPET (useplatformclock yes) | avoid | high | reboot | win10, win11 | any |
| `timer.useplatformtick` | Тик от платформенного таймера (useplatformtick yes) | avoid | medium | reboot | win10, win11 | any |

---

## 9. Источники

Первоисточники Microsoft:
- PowerEnumerate — https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerenumerate
- powrprof.h (полный список функций) — https://learn.microsoft.com/en-us/windows/win32/api/powrprof/
- powersetting.h (PowerGetActiveScheme, PowerSetActiveScheme, PowerWriteAC/DCValueIndex, PowerReadAC/DCValue) — https://learn.microsoft.com/en-us/windows/win32/api/powersetting/
- PowerDuplicateScheme — https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerduplicatescheme
- PowerReadPossibleValue — https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerreadpossiblevalue
- PowerWriteSettingAttributes (POWER_ATTRIBUTE_HIDE) — https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerwritesettingattributes
- PowerWriteFriendlyName — https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerwritefriendlyname
- Processor power management options (профили, QoS) — https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/configure-processor-power-management-options
- PERFBOOSTMODE — https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-perfboostmode
- CPMinCores — https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-core-parking-cpmincores
- PerfEnergyPreference — https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-perfenergypreference
- PerfLatencyHint — https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-perflatencyhint
- IdleDemoteThreshold — https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-idledemotethreshold
- SchedulingPolicy (hetero) — https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/configuration-for-hetero-power-scheduling-schedulingpolicy
- NVMe power management (StorNVMe) — https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/power-management-for-storage-hardware-devices-nvme
- SATA/AHCI power management (StorAHCI) — https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/power-management-for-storage-hardware-devices-sata
- Soft real-time device (IdleDisable) — https://learn.microsoft.com/en-us/windows/iot/iot-enterprise/soft-real-time/soft-real-time-device
- timeBeginPeriod (изменения 2004 / Win11) — https://learn.microsoft.com/en-us/windows/win32/api/timeapi/nf-timeapi-timebeginperiod
- SetProcessInformation (ProcessPowerThrottling, IGNORE_TIMER_RESOLUTION) — https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setprocessinformation
- Quality of Service — https://learn.microsoft.com/en-us/windows/win32/procthread/quality-of-service
- BCDEdit /set — https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/bcdedit--set
- Acquiring high-resolution time stamps (стоимость QPC на TSC vs HPET) — https://learn.microsoft.com/en-us/windows/win32/sysinfo/acquiring-high-resolution-time-stamps
- Multimedia Class Scheduler Service — https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service
- M. Russinovich, Vista Multimedia Playback and Network Throughput — https://learn.microsoft.com/en-us/archive/blogs/markrussinovich/vista-multimedia-playback-and-network-throughput
- Windows client guidance (Spectre/Meltdown, FeatureSettingsOverride) — https://support.microsoft.com/en-us/topic/windows-client-guidance-for-it-pros-to-protect-against-silicon-based-microarchitectural-and-speculative-execution-side-channel-vulnerabilities-35820a8a-ae13-1299-88cc-357f104f5b11
- Win32PrioritySeparation (TechNet) — https://technet.microsoft.com/library/Cc976120
- Windows Insider build 17101 (Ultimate Performance) — https://blogs.windows.com/windows-insider/2018/02/14/announcing-windows-10-insider-preview-build-17101-fast-build-17604-skip-ahead/ (curl получает HTTP 403 — защита от ботов; найдено поиском, в браузере открывается)
- Политика «Выключить регулирование мощности» — локальный файл `C:\Windows\PolicyDefinitions\Power.admx` (policy `PowerThrottlingTurnOff`) и `ru-RU\Power.adml`

Исследования с замерами / исходники:
- valleyofdoom/PC-Tuning (CPU idle states, timer resolution, Spectre, SMT, Game Mode) — https://github.com/valleyofdoom/PC-Tuning
- valleyofdoom/PC-Tuning research.md (TscSyncPolicy, Win32PrioritySeparation, GlobalTimerResolutionRequests, micro-adjusting) — https://github.com/valleyofdoom/PC-Tuning/blob/main/docs/research.md
- valleyofdoom/TimerResolution (MeasureSleep, SetTimerResolution) — https://github.com/valleyofdoom/TimerResolution
- B. Dawson, Windows Timer Resolution: The Great Rule Change — https://randomascii.wordpress.com/2020/10/04/windows-timer-resolution-the-great-rule-change/
- djdallmann/GamingPCSetup — WINPOWER (схемы, оверлеи, профили) — https://github.com/djdallmann/GamingPCSetup/blob/master/CONTENT/RESEARCH/WINPOWER/README.md
- djdallmann — WINKERNEL (HAL timers, Win32PrioritySeparation realtime, timer resolution) — https://github.com/djdallmann/GamingPCSetup/blob/master/CONTENT/RESEARCH/WINKERNEL/README.md
- djdallmann — значения, читаемые mmcss.sys — https://github.com/djdallmann/GamingPCSetup/blob/master/CONTENT/RESEARCH/FINDINGS/registrykeys_mmcss.txt
- djdallmann — дефолты схем Win10 LTSC 2019 — https://github.com/djdallmann/GamingPCSetup/blob/master/CONTENT/RESEARCH/FINDINGS/PowerPlanSettingsCompare.csv
- ChrisTitusTech/winutil tweaks.json — https://github.com/ChrisTitusTech/winutil/blob/main/config/tweaks.json
- ChrisTitusTech/winutil Invoke-WPFUltimatePerformance.ps1 — https://github.com/ChrisTitusTech/winutil/blob/main/functions/public/Invoke-WPFUltimatePerformance.ps1

Вторичные (использованы с оговорками):
- Process Lasso — IFEO priorities — https://bitsum.com/apps/process-lasso/docs/rules/priorities/
- Microsoft Q&A — CpuPriorityClass values — https://learn.microsoft.com/en-us/answers/questions/4362251/how-to-permanently-set-priority-processes-using-re
- dumpanalysis.org — I/O and Memory Priority in Vista (IFEO IoPriority/PagePriority) — https://www.dumpanalysis.org/blog/index.php/2008/01/27/io-and-memory-priority-in-vista/
- HWBusters — 7950X3D core parking — https://hwbusters.com/cpu/amd-ryzen-9-7950x3d-core-parking-problem-solution/
- Framework KB — USB 3 LPM и отвалы — https://knowledgebase.frame.work/en_us/my-usb-3-0-device-is-disconnecting-and-reconnecting-ryS3VSvYF
- overclockers.at — The HPET bug — https://overclockers.at/articles/the-hpet-bug-what-it-is-and-what-it-isnt
- Blur Busters forum — useplatformtick/disabledynamictick — https://forums.blurbusters.com/viewtopic.php?p=67575 (curl получает HTTP 403 — защита от ботов)
- Elastic — IFEO injection detection rule — https://www.elastic.co/guide/en/security/master/prebuilt-rule-8-3-1-image-file-execution-options-injection.html
- PCWorld (2017) — AMD Ryzen Balanced power plan — https://www.pcworld.com/article/406352/amds-ryzen-balanced-power-plan-for-windows-promises-to-boost-cpu-performance.html
- Microsoft Q&A — Efficiency mode / PowerThrottlingOff — https://learn.microsoft.com/en-us/answers/questions/2224687/how-do-i-permanently-disable-efficiency-mode-that
