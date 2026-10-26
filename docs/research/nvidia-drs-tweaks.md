# NVIDIA: настройки драйвера через NVAPI DRS из C# и реестровые твики

**Ключ темы:** `nvidia-drs-tweaks` · **Дата исследования:** 2026-10-26 · **Проект:** hateeze (C# .NET 10, net10.0-windows, x64, WebView2, всегда от администратора).
**Машинно-читаемый каталог:** `docs/research/nvidia-drs-tweaks.tweaks.json` (30 записей: 13 situational, 8 experimental, 9 avoid; `proven` — 0, см. §0).

> Всё, что помечено **«проверено на машине»**, получено **только чтением** на ПК разработчика: RTX 4060 (PCI\VEN_10DE&DEV_2882), драйвер **617.14** (`32.0.16.1714`, ветка `r617_07`, дата 17.09.2026), Windows 11 Pro for Workstations 23H2 (22631). Для DRS написан и запущен read-only зонд на .NET 10: он вызывал только `Initialize / GetDriverAndBranchVersion / EnumPhysicalGPUs / GPU_GetFullName / DRS_CreateSession / DRS_LoadSettings / Get* / Enum* / Find* / DestroySession / Unload`. **Ни одного `SetSetting / SaveSettings / Restore* / Delete*` не вызывалось**, реестр и службы не менялись.

---

## 0. Краткие выводы (TL;DR)

1. **ID функций NVAPI** взяты из официального `nvapi_interface.h` (NVIDIA/nvapi, R615), сверены с NVIDIA Profile Inspector (NPI) и NvAPIWrapper. **Все разрешаются через `nvapi_QueryInterface` на 617.14.** Размер и версия структур проверены `sizeof` в .NET 10: `NVDRS_SETTING_V1` = **12320** байт, версия **0x00013020**.
2. **Публичный NVAPI скрывает часть настроек.** Для `0x10835000` (Ultra Low Latency), `0x0005F543` (ULL CPL State) и `0x10835005` (лимит FPS в фоне) публичный `NvAPI_DRS_GetSetting` (0x73BF8338) возвращает **NVAPI_SETTING_NOT_FOUND (-160)**. Недокументированный `GetSetting` «Ex» (**0xEA99498D**) читает их нормально. Проверено на 617.14. Именно поэтому с 2022 года NPI использует внутренние ID `0x8A2CF5F5` (SetSetting) / `0xEA99498D` (GetSetting) / `0x7DD5B261` (RestoreProfileDefaultSetting) / `0xD20D29DF` (DeleteProfileSetting) с фолбэком на публичные. **Без этих функций режим «Ультра» и фоновый лимит FPS не реализовать.**
3. Публичный `EnumAvailableSettingIds` возвращает 130 ID, внутренний — 220. Base Profile на машине содержит 92 настройки по внутреннему `EnumSettings` и 67 по публичному.
4. **Глобальный профиль не перекрывает предопределённые профили игр.** Пример (проверено): профиль *Apex Legends* сам задаёт `PRERENDERLIMIT = 1` (predefined), поэтому глобальный «Режим низкой задержки» на него не влияет. UI должен об этом предупреждать.
5. **Строго `proven` среди NVIDIA-твиков нет.** У всех полезных DRS-настроек эффект зависит от условий: GPU-/CPU-bound, DX11 или DX12, наличие VRR, Reflex. Поэтому они `situational`, с явными условиями.
6. **Телеметрия в 2025–2026 устроена иначе, чем пишут сборники твиков.** Службы `NvTelemetryContainer` и задач `NvTm*` на драйвере без GFE/NVIDIA App **нет** (проверено). Телеметрию шлют **плагины** `NVDisplay.ContainerLocalSystem`: `GamesessionTelemetry_Plugin` (`_NvGSTPlugin.dll`) и `NvTelemetryEventAnalyzer_DeviceDriver_Plugin` (`_DisplayDriverRAS.dll`). В журнале оба помечены как *optional*. Обновление игровых профилей — тоже плагин (`NvProfileUpdaterPlugin`), он ходит на `services.gfe.nvidia.com`.
7. Ключи `RMHdcpKeyglobZero`, `DisableDynamicPstate`, `PerfLevelSrc`, `EnableTiledDisplay`, `DisablePreemption`, `DisableCudaContextPreemption`, `RmProfilingAdminOnly` **по-прежнему присутствуют строками в `nvlddmkm.sys` 617.14**, то есть драйвер их читает. Это не делает их полезными (см. §4).

---

## 1. Что прочитано на машине разработчика

### 1.1 Службы и задачи

| Объект | Состояние |
|---|---|
| `NVDisplay.ContainerLocalSystem` («NVIDIA Display Container LS») | **Automatic, Running**, LocalSystem, группа Video; `ImagePath = %SystemRoot%\System32\DriverStore\FileRepository\nv_dispi.inf_amd64_8203e83b25a5bd10\Display.NvContainer\NVDisplay.Container.exe -arg <ExeDir>\arguments.txt`; восстановление: RESTART 6 с, RESTART 8 с, RUN `NvContainerRecovery.bat` 10 с; `FailureActionsOnNonCrashFailures=1`; триггеров нет |
| `nvlddmkm` (ядро) | Start=3 (DEMAND), Tag 7, Group Video |
| `NvTelemetryContainer`, `NvContainerLocalSystem` (NVIDIA App/GFE) | **отсутствуют** |
| Задачи `NvTm*`, `NvProfileUpdater*`, `NvDriverUpdateCheckDaily*`, `NvNodeLauncher*` | **отсутствуют** (`Get-ScheduledTask`, `schtasks /query`) |
| `nvagent` | это «Служба виртуализации сети» Windows, к NVIDIA не относится — фильтр `*nv*` её ловит, **не трогать** |

Установлено (Uninstall): «NVIDIA Графический драйвер 617.14», NVIDIA Install Application 2.1002.468.0, NVIDIA Broadcast 2.3.0 и его компоненты. GUID установщика — `{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}`, тот же, что в именах старых задач `NvTm*_{...}`. Appx: `NVIDIACorp.NVIDIAControlPanel 8.1.969.0`. NVIDIA App и GFE **не установлены**: журнал профиль-апдейтера пишет `IsNvAppInstalled ... result 2`, `GFE is installed: 0`.

### 1.2 Плагины контейнера (из журналов `C:\ProgramData\NVIDIA\*.log_backup1`)

- **LocalSystem** (`...\plugins\LocalSystem`): `NvcDispCorePlugin` (mandatory), `NvPluginWatchdog` (mandatory, `NvcDispWatchdog.dll`), `NvXDCore` (mandatory), `NvWksServicePlugin` (mandatory), **`NvTelemetryEventAnalyzer_DeviceDriver_Plugin` (optional, `_DisplayDriverRAS.dll`)**, `NvMessageBusBroadcast` (optional, `_NvMsgBusBroadcast.dll`), `NvTopps_Plugin` (optional, `_nvtopps.dll`). Соответствие «плагин ↔ DLL» **подтверждено журналом** (строки `Loaded …\plugins\LocalSystem/_DisplayDriverRAS.dll` → `Loaded optional plugin NvTelemetryEventAnalyzer_DeviceDriver_Plugin` и т. д.; `NvWksServicePlugin` = `wksServicePluginZ.dll`).
- **Session** (`...\plugins\Session`, запускается сторожем от имени пользователя): `NvProfileUpdaterPlugin` (mandatory), `NvXDSyncPlugin` (mandatory), `NvWksServicePlugin` (mandatory), **`GamesessionTelemetry_Plugin` (optional, `_NvGSTPlugin.dll`)**.
- Контейнер пишет в журнал «Telemetry API initialized».
- `NvProfileUpdaterPlugin` ходит на `https://services.gfe.nvidia.com/GFE/v1.0/profiles/Optimus?{"branch":"r617_07"}`. После неудачи повторяет попытку через минуту, потом по расписанию. Данные лежат в `C:\ProgramData\NVIDIA Corporation\NvProfileUpdaterPlugin\{drs,optimus}\update.dat`.
- `NVDisplay.Container.exe` содержит строки `ModuleMap\%ls`, `%ls\DisableLoad`, `%ls\DisableLoadForUsers`, `%ls\RestrictLoadForUsers`. В `HKLM\SYSTEM\CurrentControlSet\Services\NVDisplay.ContainerLocalSystem\Parameters\LocalSystem\NvcDispCorePlugin` уже есть `DisableLoad = 0`, а также пустой подключ `_DisplayDriverRAS`. Конфиг сессионного контейнера: `-cfg NVDisplay.ContainerLocalSystem\Parameters\Session`; этот ключ **не существует**.

### 1.3 Реестр NVIDIA

- Ключ класса GPU: `HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000`. В нём `DriverDesc = "NVIDIA GeForce RTX 4060"`, `ProviderName = "NVIDIA"`, `MatchingDeviceId = "pci\ven_10de&dev_2882"`, `DriverVersion = 32.0.16.1714`. Подтверждено через PnP: `DEVPKEY_Device_Driver` = `{4d36e968-e325-11ce-bfc1-08002be10318}\0000` и `HKLM\SYSTEM\CurrentControlSet\Enum\PCI\VEN_10DE&DEV_2882&SUBSYS_180110DE&REV_A1\4&1fc990d7&0&0019\Driver`. Соседний подключ `Configuration` без значений — **не NVIDIA**, его нужно пропускать.
- В ключе класса **нет** `RMHdcpKeyglobZero`, `DisableDynamicPstate`, `EnableTiledDisplay`, `PerfLevelSrc`, `PowerMizer*`. **Есть** значения от INF: `AllowTiledDisplayOverride=1`, `RmDisableInst2Sys=1`, `RMCtxswLog=3`, `RmFbsrPagedDMA=1`, `RMIntrDisableMsixOnNextReload=0`, `FeatureControl=4`, `Acceleration.Level=0` и другие.
- `HKLM\SYSTEM\CurrentControlSet\Services\nvlddmkm\Parameters`: `DCHUVen=0x10de`, `DmaRemappingCompatible=3`, `NvProfileUpdateFirstRun=0`, `WDDMv22EnableSmoothTransition=0`. Подключ `FTS` содержит ~100 флагов `EnableGR*`/`EnableRID*`/`EnableDDC*`/`EnableFSN*` = 1 — это feature toggles драйвера, **не трогать**. `nvlddmkm\Global\NVTweak\Features\EnableFeature1=1`. Ключей `nvlddmkm\Global\Startup` и `...\NVTweak\RmProfilingAdminOnly` **нет**.
- `HKLM\SOFTWARE\NVIDIA Corporation`: `Global\{CoProcManager, NGXCore (ShowDlssIndicator=0), NvApp (пусто), NvBroadcast, NVTweak}`, `Installer2\...`, `NVDisplay.Container\GameSessionTelemetry\ConfPath`, `Parameters\Global\DrsPath` (`NVDRS_GOLD_PATH=C:\Windows\system32\drivers`, `NVDRS_PATH=C:\ProgramData`). Ключей **`NvControlPanel2`**, **`Global\FTS`**, **`Global\NvCplApi`** **нет**. Ключа `HKLM\SOFTWARE\WOW6432Node\NVIDIA Corporation` нет.
- `HKCU\Software\NVIDIA Corporation`: `Global\CoProcManager\ShowContextMenu=1` — это пункт «Запустить с графическим процессором», а **не** «Панель управления NVIDIA». Также `NVControlPanel2\Client\launchsource=5`. Ключа `Global\NvCplApi\Policies` **нет**, то есть пункт меню рабочего стола показывается по умолчанию.
- Контекстное меню рабочего стола: `HKCR\Directory\Background\shellex\ContextMenuHandlers\NvCplDesktopContext` → CLSID `{3D1975AF-48C6-4f8e-A182-BE0E08FA86A9}` → `nvshext.dll` (DriverStore). `nvshext.dll` читает `SOFTWARE\NVIDIA Corporation\Global\NvCplApi\Policies`, `SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvCplApi\Policies` и `...\Parameters\Global\NvCplApi\Policies`. В `nvcpl.dll` рядом лежат строки `SOFTWARE\NVIDIA Corporation\Global\NvCplApi\Policies` + `ContextUIPolicy`, а также `SOFTWARE\NVIDIA Corporation\NVControlPanel2\Client` + `OptInOrOutPreference`.

### 1.4 Файлы DRS

- Пользовательская БД: `C:\ProgramData\NVIDIA Corporation\Drs\nvdrsdb0.bin` и `nvdrsdb1.bin` (по 1 932 016 байт, двойная буферизация), `nvdrssel.bin` (1 байт — какой буфер активен), `nvdrswr.lk` (lock), `update.bin` (OTA-профили), `nvAppTimestamps`.
- «Золотая» БД драйвера: `C:\Windows\System32\drivers\NVIDIA Corporation\Drs\nvdrsdb.bin` (1 497 628 байт) + `dbInstaller.exe`.
- `C:\Windows\System32\nvapi64.dll` 32.0.16.1714 (574 040 байт), `SysWOW64\nvapi.dll`. Приложение x64 загружает **`nvapi64.dll`**.

### 1.5 Текущие значения DRS (Base Profile = текущий глобальный, проверено)

`GetBaseProfile` и `GetCurrentGlobalProfile` вернули **один и тот же handle**. Имя профиля — `Base Profile`, `isPredefined=1`, `gpuSupport=0x3`, 92 настройки. У всех 92 `isCurrentPredefined=0`, `isPredefinedValid=0`: панель NVIDIA записывает в Base Profile весь набор «глобальных параметров» как пользовательские значения.

| Настройка | ID | Значение на машине | Дефолт драйвера |
|---|---|---|---|
| Power management mode | 0x1057EB71 | 5 | 5 |
| Maximum pre-rendered frames | 0x007BA09E | 0 | 0 |
| ULL CPL State | 0x0005F543 | 0 (только внутренний API) | 0 |
| FRL Low Latency (ULL Enabled) | 0x10835000 | 0 (только внутренний API) | 0 |
| Vertical Sync | 0x00A879CF | **0x08416747 (Force off)** — выставлено пользователем | 0x60925292 |
| Vertical Sync Tear Control | 0x005A375C | 0x96861077 | 0x96861077 |
| Frame Rate Limiter | 0x10835002 | 0 | 0 |
| Background Application Max Frame Rate | 0x10835005 | 0 (только внутренний API) | 0 |
| Texture filtering - Quality | 0x00CE2691 | 0 | 0 |
| Anisotropic sample optimization | 0x00E73211 | **1** | 0 |
| Anisotropic filter optimization | 0x0084CD70 | **1** | 0 |
| Negative LOD bias | 0x0019BB68 | 0 | 0 |
| Trilinear optimization | 0x002ECAF2 | 0 (= оптимизация ВКЛ) | 0 |
| Threaded optimization | 0x20C1221E | 0 | 0 |
| Shader Cache / max size | 0x00198FFF / 0x00AC8497 | 1 / 0x4000 | 1 / 0x4000 |
| Triple buffering | 0x20FDD1F9 | 0 | 0 |
| G-SYNC (VRR_APP_OVERRIDE) | 0x10A879CF | 0 | 0 |
| VRR requested state / VRR global feature / Variable refresh Rate | 0x1094F1F7 / 0x1094F157 / 0x10A879CE | 1 / 1 / 1 | 1 / — / 1 |
| Preferred refresh rate | 0x0064B541 | 0 | 0 |
| Vulkan/OpenGL present method | 0x20D690F8 | 2 (Auto) | 2 |
| Platform Boost | 0x10834FFE | 1 | — |
| **Не заданы** (NOT_FOUND в обоих API) | 0x50166C5E CUDA P2, 0x1075D972 Ansel, 0x000F00BA/BB/FF и 0x000BFA21 rBAR, 0x10E41E01/0x10E41DF3/0x10E41E06 DLSS overrides, 0x1194F158 VRR_MODE, 0x108F0841 Export perf counters, 0x00EAD189 Precompile | — | см. §3 |

---

## 2. NVAPI из C# без SDK

### 2.1 Механизм

У `nvapi64.dll` единственная нужная экспортируемая функция — **`nvapi_QueryInterface(uint id) → void*`**. Все остальные функции получаются по 32-битному ID. На x64 соглашение о вызовах одно (Microsoft x64); `Cdecl` указывается для читаемости. Порядок работы:

```
NvAPI_Initialize → (DRS_CreateSession → DRS_LoadSettings → GetCurrentGlobalProfile → Get/SetSetting… → DRS_SaveSettings → DRS_DestroySession) → NvAPI_Unload
```

- `NvAPI_Initialize` возвращает ошибку, если нет драйвера или GPU NVIDIA: `NVAPI_NVIDIA_DEVICE_NOT_FOUND (-6)`, `NVAPI_LIBRARY_NOT_FOUND (-2)`. Если `nvapi64.dll` отсутствует, `DllImport` бросает `DllNotFoundException` — в этом случае **скрыть категорию nvidia**.
- `NvAPI_DRS_SaveSettings` требует прав администратора, иначе `NVAPI_INVALID_USER_PRIVILEGE (-137)`. Приложение и так работает от админа.
- Сессия **не потокобезопасна**: все DRS-вызовы делайте из одного потока, одна сессия на одну пачку действий.

### 2.2 Таблица ID функций

Источник — `nvapi_interface.h` (NVIDIA/nvapi, ветка main, R615). Колонка «617.14» — результат `QueryInterface ≠ NULL` на машине разработчика.

| Функция | ID | 617.14 | Сигнатура (C) |
|---|---|---|---|
| NvAPI_Initialize | `0x0150E828` | OK | `NvAPI_Status ()` |
| NvAPI_Unload | `0xD22BDD7E` | OK | `()` |
| NvAPI_GetErrorMessage | `0x6C2D048C` | OK | `(NvAPI_Status nr, NvAPI_ShortString szDesc /*char[64]*/)` |
| NvAPI_GetInterfaceVersionString | `0x01053FA5` | — | `(NvAPI_ShortString)` |
| NvAPI_EnumPhysicalGPUs | `0xE5AC921F` | OK | `(NvPhysicalGpuHandle h[64], NvU32* pCount)` |
| NvAPI_GPU_GetFullName | `0xCEEE8E9F` | OK | `(NvPhysicalGpuHandle, NvAPI_ShortString)` |
| NvAPI_SYS_GetDriverAndBranchVersion | `0x2926AAAD` | OK | `(NvU32* ver /*61714*/, NvAPI_ShortString branch /*"r617_07"*/)` |
| NvAPI_DRS_CreateSession | `0x0694D52E` | OK | `(NvDRSSessionHandle* ph)` |
| NvAPI_DRS_DestroySession | `0xDAD9CFF8` | OK | `(NvDRSSessionHandle)` |
| NvAPI_DRS_LoadSettings | `0x375DBD6B` | OK | `(hSession)` |
| NvAPI_DRS_SaveSettings | `0xFCBC7E14` | OK | `(hSession)` |
| NvAPI_DRS_LoadSettingsFromFile | `0xD3EDE889` | — | `(hSession, NvAPI_UnicodeString fileName)` |
| NvAPI_DRS_SaveSettingsToFile | `0x2BE25DF8` | — | `(hSession, NvAPI_UnicodeString fileName)` |
| NvAPI_DRS_CreateProfile | `0xCC176068` | OK | `(hSession, NVDRS_PROFILE* info, NvDRSProfileHandle* ph)` |
| NvAPI_DRS_DeleteProfile | `0x17093206` | — | `(hSession, hProfile)` |
| NvAPI_DRS_SetCurrentGlobalProfile | `0x1C89C5DF` | — | `(hSession, NvAPI_UnicodeString name)` |
| NvAPI_DRS_GetCurrentGlobalProfile | `0x617BFF9F` | OK | `(hSession, NvDRSProfileHandle* ph)` |
| NvAPI_DRS_GetProfileInfo | `0x61CD6FD6` | OK | `(hSession, hProfile, NVDRS_PROFILE*)` |
| NvAPI_DRS_SetProfileInfo | `0x16ABD3A9` | — | `(hSession, hProfile, NVDRS_PROFILE*)` |
| NvAPI_DRS_FindProfileByName | `0x7E4A9A0B` | OK | `(hSession, NvAPI_UnicodeString name, NvDRSProfileHandle* ph)` |
| NvAPI_DRS_EnumProfiles | `0xBC371EE0` | — | `(hSession, NvU32 index, NvDRSProfileHandle* ph)` |
| NvAPI_DRS_GetNumProfiles | `0x1DAE4FBC` | — | `(hSession, NvU32* n)` |
| NvAPI_DRS_CreateApplication | `0x4347A9DE` | OK | `(hSession, hProfile, NVDRS_APPLICATION*)` |
| NvAPI_DRS_DeleteApplicationEx | `0xC5EA85A1` | — | `(hSession, hProfile, NVDRS_APPLICATION*)` |
| NvAPI_DRS_DeleteApplication | `0x2C694BC6` | — | `(hSession, hProfile, NvAPI_UnicodeString appName)` |
| NvAPI_DRS_GetApplicationInfo | `0xED1F8C69` | — | `(hSession, hProfile, NvAPI_UnicodeString appName, NVDRS_APPLICATION*)` |
| NvAPI_DRS_EnumApplications | `0x7FA2173A` | — | `(hSession, hProfile, NvU32 start, NvU32* count, NVDRS_APPLICATION*)` |
| NvAPI_DRS_FindApplicationByName | `0xEEE566B2` | OK | `(hSession, NvAPI_UnicodeString appName, NvDRSProfileHandle* ph, NVDRS_APPLICATION* /*in: version*/)` |
| NvAPI_DRS_SetSetting | `0x577DD202` | OK | `(hSession, hProfile, NVDRS_SETTING*)` |
| NvAPI_DRS_GetSetting | `0x73BF8338` | OK | `(hSession, hProfile, NvU32 settingId, NVDRS_SETTING*)` |
| NvAPI_DRS_EnumSettings | `0xAE3039DA` | OK | `(hSession, hProfile, NvU32 start, NvU32* count, NVDRS_SETTING*)` |
| NvAPI_DRS_EnumAvailableSettingIds | `0xF020614A` | OK (130 ID) | `(NvU32* ids, NvU32* maxCount)` |
| NvAPI_DRS_EnumAvailableSettingValues | `0x2EC39F90` | — | `(NvU32 id, NvU32* maxNum, NVDRS_SETTING_VALUES*)` |
| NvAPI_DRS_GetSettingIdFromName | `0xCB7309CD` | — | `(NvAPI_UnicodeString name, NvU32* id)` |
| NvAPI_DRS_GetSettingNameFromId | `0xD61CBE6E` | OK | `(NvU32 id, NvAPI_UnicodeString* name)` |
| NvAPI_DRS_DeleteProfileSetting | `0xE4A26362` | OK | `(hSession, hProfile, NvU32 id)` |
| NvAPI_DRS_RestoreAllDefaults | `0x5927B094` | — | `(hSession)` — **не использовать** (сбрасывает всё) |
| NvAPI_DRS_RestoreProfileDefault | `0xFA5F6134` | — | `(hSession, hProfile)` |
| NvAPI_DRS_RestoreProfileDefaultSetting | `0x53F0381E` | OK | `(hSession, hProfile, NvU32 id)` |
| NvAPI_DRS_GetBaseProfile | `0xDA8466A0` | OK | `(hSession, NvDRSProfileHandle* ph)` |

**Недокументированные варианты.** Используются NPI: появились в коммите `75c99ea` от 2022-11-13 («Ultra Low Latency setting is back #49 … workaround for some settings throw errors when changed #46 #96 #94»), фолбэки на публичные ID добавлены в коммите `f003324` от 2023-03-26. На 617.14 все разрешаются через QueryInterface.

| Назначение | Внутр. ID | Фолбэк (публичный) | Сигнатура (по NPI) |
|---|---|---|---|
| SetSetting | `0x8A2CF5F5` | `0x577DD202` | `(hSession, hProfile, NVDRS_SETTING*, NvU32 x = 0, NvU32 y = 0)` |
| GetSetting | `0xEA99498D` | `0x73BF8338` | `(hSession, hProfile, NvU32 id, NVDRS_SETTING*, NvU32* x /*out, на 617.14 всегда 0*/)` |
| EnumSettings | `0xCFD6983E` | `0xAE3039DA` | как публичная |
| EnumAvailableSettingIds | `0xE5DE48E5` | `0xF020614A` | как публичная (220 ID на 617.14) |
| GetSettingNameFromId | `0x1EB13791` | `0xD61CBE6E` | как публичная (знает имена скрытых: 0x10835000 → «FRL Low Latency», 0x10835005 → «Background Application Max Frame Rate», 0x000BFA21 → «Resizable Bar») |
| DeleteProfileSetting | `0xD20D29DF` | `0xE4A26362` | как публичная |
| RestoreProfileDefaultSetting | `0x7DD5B261` | `0x53F0381E` | как публичная |

> Почему это обязательно: issue #46 в NPI (2020) — «Since R445.75 the NVAPI does not allow either to read or write the following settings anymore: GSYNC - Indicator Overlay 0x10029538, Ultra Low Latency 0x10835000, … Ultra Low Latency CPL State 0x0005F543». Issue #94 (2022) — «DRS_SETSETTING FAILED: NVAPI_SETTING_NOT_FOUND» при записи лимитера. На 617.14 чтение через публичный API этих ID **по-прежнему** даёт -160, а через `0xEA99498D` — OK (§1.5). **Запись** через `0x8A2CF5F5` на этой машине **НЕ ПРОВЕРЕНА**, потому что изменять систему запрещено. Её ~4 года массово используют NPI 2.4.x–3.0.2.3 (последний релиз — 2026-10-13).
>
> Лишние аргументы при вызове cdecl-функции на x64 безвредны: аргументы в регистрах, стек чистит вызывающий. Поэтому вызов `0x8A2CF5F5` с `(…, 0, 0)` корректен, а фолбэк на публичную 3-аргументную функцию тоже работает.

### 2.3 Константы и структуры (nvapi.h / nvapi_lite_common.h, R615)

```
#define NVAPI_SHORT_STRING_MAX     64      typedef char  NvAPI_ShortString[64];
#define NVAPI_UNICODE_STRING_MAX   2048    typedef NvU16 NvAPI_UnicodeString[2048];   // 4096 байт
#define NVAPI_BINARY_DATA_MAX      4096
#define NVAPI_MAX_PHYSICAL_GPUS    64
#define NVAPI_SETTING_MAX_VALUES   100
#define MAKE_NVAPI_VERSION(typeName,ver) (NvU32)(sizeof(typeName) | ((ver)<<16))
#define NVAPI_DRS_GLOBAL_PROFILE   ((NvDRSProfileHandle) -1)
typedef enum { NVDRS_DWORD_TYPE, NVDRS_BINARY_TYPE, NVDRS_STRING_TYPE, NVDRS_WSTRING_TYPE, NVDRS_QWORD_TYPE } NVDRS_SETTING_TYPE;  // QWORD — с R610
typedef enum { NVDRS_CURRENT_PROFILE_LOCATION, NVDRS_GLOBAL_PROFILE_LOCATION, NVDRS_BASE_PROFILE_LOCATION, NVDRS_DEFAULT_PROFILE_LOCATION } NVDRS_SETTING_LOCATION;
typedef struct { NvU32 valueLength; NvU8 valueData[4096]; } NVDRS_BINARY_SETTING;          // 4100 байт
#pragma pack(push,4)
typedef struct _NVDRS_SETTING_V1 {
  NvU32 version; NvAPI_UnicodeString settingName; NvU32 settingId;
  NVDRS_SETTING_TYPE settingType; NVDRS_SETTING_LOCATION settingLocation;
  NvU32 isCurrentPredefined; NvU32 isPredefinedValid;
  union { NvU32 u32PredefinedValue; NVDRS_BINARY_SETTING binaryPredefinedValue; NvAPI_UnicodeString wszPredefinedValue; NvU64 u64PredefinedValue; };
  union { NvU32 u32CurrentValue;    NVDRS_BINARY_SETTING binaryCurrentValue;    NvAPI_UnicodeString wszCurrentValue;    NvU64 u64CurrentValue; };
} NVDRS_SETTING_V1;
#pragma pack(pop)
```

**Расчёт размеров и версий.** Все значения проверены `sizeof` в .NET 10 на машине.

| Структура | Расчёт | sizeof | Версия (`MAKE_NVAPI_VERSION`) |
|---|---|---|---|
| `NVDRS_SETTING_V1` (pack 4) | 4 + 4096 + 4·5 + 4100 (union, выравнивание 4) · 2 | **12320 (0x3020)** | ver 1 → **0x00013020** (77856) |
| `NVDRS_PROFILE_V1` | 4 + 4096 + 4·4 | **4116 (0x1014)** | ver 1 → **0x00011014** |
| `NVDRS_APPLICATION_V3` | 4 + 4 + 4096·4 + 4 (bitfield) | 16396 (0x400C) | ver 3 → 0x0003400C |
| `NVDRS_APPLICATION_V4` | V3 + 4096 (commandLine) | **20492 (0x500C)** | ver 4 → **0x0004500C** |
| `NVDRS_SETTING_VALUES` (pack 4) | 12 + 4100 + 100·4100 | 414112 (0x651A0) | ver 1 → 0x000751A0 — размер не влезает в 16 бит, бит 16 сливается со старшими. Не используем; **НЕ ПРОВЕРЕНО** вызовом |

Ловушки:
- **Union — 4100 байт с выравниванием 4.** Если в C# объявить union через `[FieldOffset]` с `ulong`, при `Pack = 8` получится 12328 байт и `NVAPI_INCOMPATIBLE_STRUCT_VERSION (-9)`. Используйте `fixed byte[4100]` и `Pack = 4`.
- `NVDRS_APPLICATION_V4.isMetro:1 / isCommandLine:1 / reserved:30` — это одно поле `uint flags` после `fileInFolder`.
- `FindApplicationByName` требует заполнить `version` во входной структуре приложения.

### 2.4 Модель профилей DRS (важно для UX)

- **Base Profile** — то, что NVCP показывает на вкладке «Глобальные параметры». На GeForce `GetCurrentGlobalProfile` = `GetBaseProfile` (проверено). На Quadro/RTX Pro текущим глобальным может быть пресет («3D App - …») — **пишите в `GetCurrentGlobalProfile`**, как NVCP.
- **Профили игр** (их ~тысячи, `isPredefined=1`) содержат *predefined*-значения. При чтении из профиля игры `settingLocation = GLOBAL_PROFILE_LOCATION (1)` означает «значение унаследовано из глобального», `CURRENT_PROFILE_LOCATION (0)` с `isCurrentPredefined=1` — «задано NVIDIA для этой игры». **Глобальное значение не перекрывает predefined-значение игры.** Проверено: `r5apex.exe` → профиль «Apex Legends», `PRERENDERLIMIT` = 1 (predefined); у CS2, Valorant, Fortnite, Dota 2 и Overwatch ключевые настройки наследуются из глобального.
- Пользовательские значения Base Profile: `isCurrentPredefined=0`, `isPredefinedValid=0`. `RestoreProfileDefaultSetting` удаляет пользовательское значение, после чего действует дефолт драйвера. Его надо брать из таблицы §3 (`NvApiDriverSettings.h`): через API получить его нельзя, `isPredefinedValid=0`.
- `GetSetting` на Base Profile для незаданной настройки возвращает `NVAPI_SETTING_NOT_FOUND`. Это **норма**: значит «действует дефолт».
- Изменения действуют на **процессы, запущенные после `SaveSettings`** (драйвер читает профиль при инициализации UMD). UI: «перезапустите игру». Точный момент применения для каждого ID — НЕ ПРОВЕРЕНО.
- **Сосуществование с NVCP / NVIDIA App.** Они держат свою DRS-сессию. Если NVCP открыт со старым состоянием и пользователь нажмёт «Применить», наши значения будут перезаписаны. Рекомендация: перед применением проверить процесс `nvcplui.exe` и попросить закрыть панель (логический вывод, экспериментально не проверялось).

### 2.5 Эталонная обёртка C# (.NET 10, function pointers)

Код **скомпилирован** и read-only-часть **запущена** на машине (`dotnet run --file`, `AllowUnsafeBlocks=true`, x64). Результат: `Driver 61714 branch r617_07; GPUs: NVIDIA GeForce RTX 4060; internalApi=True`, значения совпали с §1.5. Методы записи скомпилированы, но **не вызывались**.

```csharp
// <AllowUnsafeBlocks>true</AllowUnsafeBlocks>, PlatformTarget x64
using System.Runtime.InteropServices;

namespace Hateeze.Nvidia;

public enum NvStatus : int
{
    OK = 0, ERROR = -1, LIBRARY_NOT_FOUND = -2, NO_IMPLEMENTATION = -3, API_NOT_INITIALIZED = -4, INVALID_ARGUMENT = -5,
    NVIDIA_DEVICE_NOT_FOUND = -6, END_ENUMERATION = -7, INVALID_HANDLE = -8, INCOMPATIBLE_STRUCT_VERSION = -9,
    INVALID_POINTER = -14, FUNCTION_NOT_FOUND = -136, INVALID_USER_PRIVILEGE = -137, SETTING_NOT_FOUND = -160,
    SETTING_SIZE_TOO_LARGE = -161, TOO_MANY_SETTINGS_IN_PROFILE = -162, PROFILE_NOT_FOUND = -163, PROFILE_NAME_IN_USE = -164,
    EXECUTABLE_NOT_FOUND = -166, EXECUTABLE_ALREADY_IN_USE = -167, DATATYPE_MISMATCH = -168, PROFILE_REMOVED = -169,
    ACCESS_DENIED = -175, INVALID_FUNCTION_POINTER = -9999 // собственный маркер «функции нет»
}
public enum NvDrsSettingType : int { Dword = 0, Binary = 1, String = 2, WString = 3, Qword = 4 }
public enum NvDrsSettingLocation : int { CurrentProfile = 0, GlobalProfile = 1, BaseProfile = 2, DefaultProfile = 3 }

[StructLayout(LayoutKind.Sequential, Pack = 4)]   // sizeof = 12320, Version = 0x00013020
public unsafe struct NVDRS_SETTING_V1
{
    public uint version;
    public fixed char settingName[2048];
    public uint settingId;
    public NvDrsSettingType settingType;
    public NvDrsSettingLocation settingLocation;
    public uint isCurrentPredefined;
    public uint isPredefinedValid;
    public fixed byte predefinedValue[4100];
    public fixed byte currentValue[4100];
    public static uint Version => (uint)sizeof(NVDRS_SETTING_V1) | (1u << 16);
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]   // sizeof = 4116, Version = 0x00011014
public unsafe struct NVDRS_PROFILE_V1
{
    public uint version; public fixed char profileName[2048];
    public uint gpuSupport; public uint isPredefined; public uint numOfApps; public uint numOfSettings;
    public static uint Version => (uint)sizeof(NVDRS_PROFILE_V1) | (1u << 16);
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]   // sizeof = 20492, Version = 0x0004500C
public unsafe struct NVDRS_APPLICATION_V4
{
    public uint version; public uint isPredefined;
    public fixed char appName[2048]; public fixed char userFriendlyName[2048];
    public fixed char launcher[2048]; public fixed char fileInFolder[2048];
    public uint flags;                       // bit0 isMetro, bit1 isCommandLine
    public fixed char commandLine[2048];
    public static uint Version => (uint)sizeof(NVDRS_APPLICATION_V4) | (4u << 16);
}

public readonly record struct DrsDwordRead(NvStatus Status, bool Found, uint? Value, NvDrsSettingLocation? Location, bool IsUserValue);

/// Один экземпляр = одна DRS-сессия. Не потокобезопасен.
public sealed unsafe class NvDrs : IDisposable
{
    const uint ID_Initialize = 0x0150E828, ID_Unload = 0xD22BDD7E, ID_GetErrorMessage = 0x6C2D048C,
               ID_EnumPhysicalGPUs = 0xE5AC921F, ID_GPU_GetFullName = 0xCEEE8E9F, ID_SYS_GetDriverAndBranchVersion = 0x2926AAAD,
               ID_DRS_CreateSession = 0x0694D52E, ID_DRS_DestroySession = 0xDAD9CFF8, ID_DRS_LoadSettings = 0x375DBD6B,
               ID_DRS_SaveSettings = 0xFCBC7E14, ID_DRS_SaveSettingsToFile = 0x2BE25DF8, ID_DRS_LoadSettingsFromFile = 0xD3EDE889,
               ID_DRS_GetCurrentGlobalProfile = 0x617BFF9F, ID_DRS_FindApplicationByName = 0xEEE566B2,
               ID_DRS_GetSetting = 0x73BF8338, ID_DRS_SetSetting = 0x577DD202,
               ID_DRS_RestoreProfileDefaultSetting = 0x53F0381E, ID_DRS_DeleteProfileSetting = 0xE4A26362;
    const uint IDX_DRS_SetSetting = 0x8A2CF5F5, IDX_DRS_GetSetting = 0xEA99498D,
               IDX_DRS_RestoreProfileDefaultSetting = 0x7DD5B261, IDX_DRS_DeleteProfileSetting = 0xD20D29DF;

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    static extern IntPtr nvapi_QueryInterface(uint id);
    static IntPtr Q(uint id) => nvapi_QueryInterface(id);

    readonly delegate* unmanaged[Cdecl]<int> _unload;
    readonly delegate* unmanaged[Cdecl]<int, byte*, int> _getErrorMessage;
    readonly delegate* unmanaged[Cdecl]<IntPtr, int> _destroySession, _loadSettings, _saveSettings;
    readonly delegate* unmanaged[Cdecl]<IntPtr, char*, int> _saveToFile, _loadFromFile;
    readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, int> _getCurrentGlobalProfile;
    readonly delegate* unmanaged[Cdecl]<IntPtr, char*, IntPtr*, NVDRS_APPLICATION_V4*, int> _findApplicationByName;
    readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, NVDRS_SETTING_V1*, int> _getSetting;
    readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, NVDRS_SETTING_V1*, int> _setSetting;
    readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, int> _restoreDefaultSetting, _deleteProfileSetting;
    readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, NVDRS_SETTING_V1*, uint*, int> _getSettingX;
    readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, NVDRS_SETTING_V1*, uint, uint, int> _setSettingX;
    readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, int> _restoreDefaultSettingX, _deleteProfileSettingX;

    IntPtr _session;
    public uint DriverVersion { get; }              // 61714 -> "617.14"
    public string DriverBranch { get; } = "";       // "r617_07"
    public IReadOnlyList<string> GpuNames { get; }
    public bool HasInternalApi => _getSettingX != null && _setSettingX != null;

    NvDrs(out bool ok)
    {
        ok = false;
        var init = (delegate* unmanaged[Cdecl]<int>)Q(ID_Initialize);
        if (init == null || init() != 0) { GpuNames = Array.Empty<string>(); return; }
        _unload = (delegate* unmanaged[Cdecl]<int>)Q(ID_Unload);
        _getErrorMessage = (delegate* unmanaged[Cdecl]<int, byte*, int>)Q(ID_GetErrorMessage);

        var drv = (delegate* unmanaged[Cdecl]<uint*, byte*, int>)Q(ID_SYS_GetDriverAndBranchVersion);
        uint ver = 0; byte* branch = stackalloc byte[64];
        if (drv != null && drv(&ver, branch) == 0) { DriverVersion = ver; DriverBranch = Marshal.PtrToStringAnsi((IntPtr)branch) ?? ""; }

        var names = new List<string>();
        var enumGpus = (delegate* unmanaged[Cdecl]<IntPtr*, uint*, int>)Q(ID_EnumPhysicalGPUs);
        var fullName = (delegate* unmanaged[Cdecl]<IntPtr, byte*, int>)Q(ID_GPU_GetFullName);
        IntPtr* gpus = stackalloc IntPtr[64]; uint count = 0; byte* name = stackalloc byte[64];
        if (enumGpus != null && fullName != null && enumGpus(gpus, &count) == 0)
            for (int i = 0; i < count; i++) if (fullName(gpus[i], name) == 0) names.Add(Marshal.PtrToStringAnsi((IntPtr)name) ?? "");
        GpuNames = names;

        _destroySession = (delegate* unmanaged[Cdecl]<IntPtr, int>)Q(ID_DRS_DestroySession);
        _loadSettings = (delegate* unmanaged[Cdecl]<IntPtr, int>)Q(ID_DRS_LoadSettings);
        _saveSettings = (delegate* unmanaged[Cdecl]<IntPtr, int>)Q(ID_DRS_SaveSettings);
        _saveToFile = (delegate* unmanaged[Cdecl]<IntPtr, char*, int>)Q(ID_DRS_SaveSettingsToFile);
        _loadFromFile = (delegate* unmanaged[Cdecl]<IntPtr, char*, int>)Q(ID_DRS_LoadSettingsFromFile);
        _getCurrentGlobalProfile = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, int>)Q(ID_DRS_GetCurrentGlobalProfile);
        _findApplicationByName = (delegate* unmanaged[Cdecl]<IntPtr, char*, IntPtr*, NVDRS_APPLICATION_V4*, int>)Q(ID_DRS_FindApplicationByName);
        _getSetting = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, NVDRS_SETTING_V1*, int>)Q(ID_DRS_GetSetting);
        _setSetting = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, NVDRS_SETTING_V1*, int>)Q(ID_DRS_SetSetting);
        _restoreDefaultSetting = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, int>)Q(ID_DRS_RestoreProfileDefaultSetting);
        _deleteProfileSetting = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, int>)Q(ID_DRS_DeleteProfileSetting);
        _getSettingX = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, NVDRS_SETTING_V1*, uint*, int>)Q(IDX_DRS_GetSetting);
        _setSettingX = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, NVDRS_SETTING_V1*, uint, uint, int>)Q(IDX_DRS_SetSetting);
        _restoreDefaultSettingX = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, int>)Q(IDX_DRS_RestoreProfileDefaultSetting);
        _deleteProfileSettingX = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, int>)Q(IDX_DRS_DeleteProfileSetting);

        var create = (delegate* unmanaged[Cdecl]<IntPtr*, int>)Q(ID_DRS_CreateSession);
        if (create == null || _loadSettings == null || _getSetting == null) return;
        IntPtr hs;
        if (create(&hs) != 0) return;
        _session = hs;
        if (_loadSettings(_session) != 0) return;
        ok = true;
    }

    /// null — нет nvapi64.dll / драйвера / GPU NVIDIA (категорию «nvidia» скрыть).
    public static NvDrs? TryOpen(out string? error)
    {
        error = null;
        try
        {
            var d = new NvDrs(out bool ok);
            if (ok) return d;
            d.Dispose(); error = "NvAPI_Initialize/DRS_CreateSession/LoadSettings failed"; return null;
        }
        catch (DllNotFoundException) { error = "nvapi64.dll not found"; return null; }
        catch (EntryPointNotFoundException) { error = "nvapi_QueryInterface not exported"; return null; }
    }

    public string ErrorText(int status)
    {
        if (_getErrorMessage == null) return ((NvStatus)status).ToString();
        byte* b = stackalloc byte[64];
        return _getErrorMessage(status, b) == 0 ? Marshal.PtrToStringAnsi((IntPtr)b) ?? status.ToString() : status.ToString();
    }

    public IntPtr GlobalProfile
    {
        get { IntPtr h; int s = _getCurrentGlobalProfile(_session, &h); if (s != 0) throw new NvApiException("DRS_GetCurrentGlobalProfile", s, this); return h; }
    }

    int GetSettingRaw(IntPtr hProfile, uint id, NVDRS_SETTING_V1* s)
    {
        s->version = NVDRS_SETTING_V1.Version;
        if (_getSettingX != null) { uint x = 0; return _getSettingX(_session, hProfile, id, s, &x); } // видит скрытые ID
        return _getSetting(_session, hProfile, id, s);
    }

    public DrsDwordRead ReadGlobalDword(uint settingId)
    {
        var s = new NVDRS_SETTING_V1();
        int st = GetSettingRaw(GlobalProfile, settingId, &s);
        if (st == (int)NvStatus.SETTING_NOT_FOUND) return new(NvStatus.SETTING_NOT_FOUND, false, null, null, false);
        if (st != 0) return new((NvStatus)st, false, null, null, false);
        if (s.settingType != NvDrsSettingType.Dword) return new(NvStatus.DATATYPE_MISMATCH, true, null, s.settingLocation, false);
        return new(NvStatus.OK, true, *(uint*)s.currentValue, s.settingLocation, s.isCurrentPredefined == 0);
    }

    /// Эффективное значение = сохранённое, иначе дефолт драйвера из NvDrsDefaults.
    public uint? GetEffectiveGlobalDword(uint settingId)
    {
        var r = ReadGlobalDword(settingId);
        if (r.Found && r.Value is uint v) return v;
        return NvDrsDefaults.TryGet(settingId, out var d) ? d : null;
    }

    public void SetGlobalDword(uint settingId, uint value)   // без Save()
    {
        var s = new NVDRS_SETTING_V1
        {
            version = NVDRS_SETTING_V1.Version, settingId = settingId,
            settingType = NvDrsSettingType.Dword, settingLocation = NvDrsSettingLocation.CurrentProfile,
        };
        *(uint*)s.currentValue = value;
        IntPtr hp = GlobalProfile;
        int st = _setSettingX != null ? _setSettingX(_session, hp, &s, 0, 0) : (int)NvStatus.INVALID_FUNCTION_POINTER;
        if (st != 0) st = _setSetting(_session, hp, &s);
        if (st != 0) throw new NvApiException("DRS_SetSetting", st, this);
    }

    /// Вернуть дефолт драйвера; SETTING_NOT_FOUND = «и так дефолт» = успех.
    public void RestoreGlobalDefault(uint settingId)
    {
        IntPtr hp = GlobalProfile;
        int st = _restoreDefaultSettingX != null ? _restoreDefaultSettingX(_session, hp, settingId) : (int)NvStatus.INVALID_FUNCTION_POINTER;
        if (st != 0 && st != (int)NvStatus.SETTING_NOT_FOUND) st = _restoreDefaultSetting(_session, hp, settingId);
        if (st != 0 && st != (int)NvStatus.SETTING_NOT_FOUND)
        {
            st = _deleteProfileSettingX != null ? _deleteProfileSettingX(_session, hp, settingId) : _deleteProfileSetting(_session, hp, settingId);
            if (st != 0 && st != (int)NvStatus.SETTING_NOT_FOUND) throw new NvApiException("DRS_RestoreProfileDefaultSetting", st, this);
        }
    }

    public void Save() { int st = _saveSettings(_session); if (st != 0) throw new NvApiException("DRS_SaveSettings", st, this); }
    public void ExportToFile(string path) { fixed (char* p = path) { int st = _saveToFile(_session, p); if (st != 0) throw new NvApiException("DRS_SaveSettingsToFile", st, this); } }
    public void ImportFromFile(string path) { fixed (char* p = path) { int st = _loadFromFile(_session, p); if (st != 0) throw new NvApiException("DRS_LoadSettingsFromFile", st, this); } }

    public IntPtr? FindProfileByExe(string exeName)   // "cs2.exe" -> профиль "Counter-strike 2"
    {
        var app = new NVDRS_APPLICATION_V4 { version = NVDRS_APPLICATION_V4.Version };
        IntPtr hp; int st;
        fixed (char* pe = exeName) st = _findApplicationByName(_session, pe, &hp, &app);
        if (st == (int)NvStatus.EXECUTABLE_NOT_FOUND) return null;   // -166
        if (st != 0) throw new NvApiException("DRS_FindApplicationByName", st, this);
        return hp;
    }

    public void Dispose()
    {
        if (_session != IntPtr.Zero && _destroySession != null) { _destroySession(_session); _session = IntPtr.Zero; }
        if (_unload != null) _unload();
    }
}

public sealed class NvApiException : Exception
{
    public int Status { get; }
    internal NvApiException(string fn, int status, NvDrs d) : base($"{fn} failed: {status} ({d.ErrorText(status)})") => Status = status;
}

/// Дефолты драйвера для ID каталога (NvApiDriverSettings.h R615; CUDA P2 — по NPI OverrideDefault).
public static class NvDrsDefaults
{
    static readonly Dictionary<uint, uint> Map = new()
    {
        [0x1057EB71] = 5, [0x007BA09E] = 0, [0x0005F543] = 0, [0x10835000] = 0,
        [0x00A879CF] = 0x60925292, [0x005A375C] = 0x96861077, [0x10835002] = 0, [0x10835005] = 0,
        [0x00CE2691] = 0, [0x00E73211] = 0, [0x0019BB68] = 0, [0x002ECAF2] = 0, [0x20C1221E] = 0,
        [0x00198FFF] = 1, [0x00AC8497] = 0x4000, [0x20FDD1F9] = 0, [0x0064B541] = 0,
        [0x50166C5E] = 1, [0x1075D972] = 1, [0x10E41E01] = 0, [0x10E41DF3] = 0,
    };
    public static bool TryGet(uint id, out uint value) => Map.TryGetValue(id, out value);
}
```

### 2.6 Спецификация действия `{"type":"nvdrs"}` для движка твиков

```jsonc
{"type":"nvdrs","settingId":274197361,"value":1}            // apply: DWORD в текущий глобальный профиль
{"type":"nvdrs","settingId":11041231,"value":"{{value}}"}  // select/value: подставить выбранное uint
{"type":"nvdrs","settingId":274197361,"op":"restoreDefault"} // revert
```

1. **Пакетность.** Все `nvdrs`-действия одного твика — и лучше всей пачки твиков — выполняются в **одной** сессии: `TryOpen` → `SetGlobalDword`/`RestoreGlobalDefault` по списку → **один `Save()`** → `Dispose()`. Если хоть одна запись не удалась, `Save()` не вызывайте: изменения сессии отбрасываются при `DestroySession`. Так получается атомарность.
2. **Запись.** Сначала внутренний `0x8A2CF5F5(h, p, &s, 0, 0)`; если ошибка или функции нет — публичный `0x577DD202`. **После `Save()` перечитайте значение** через `ReadGlobalDword`. Если оно не совпало, твик считается «не применён», и это пишется в лог. Это нужно для скрытых ID без гарантий: `0x50166C5E`, `0x10835000`, `0x0005F543`, `0x10835005`.
3. **restoreDefault.** `0x7DD5B261` → `0x53F0381E` → `DeleteProfileSetting`. `NVAPI_SETTING_NOT_FOUND (-160)` считается успехом.
4. **detect** (поле `detect` в JSON = null). Твик «применён», если для **каждого** apply-действия `GetEffectiveGlobalDword(id) == value`. `NOT_FOUND` трактуется как значение из `NvDrsDefaults`. Для `select`/`value` UI показывает текущее эффективное значение: ищет его среди `options` или выводит числом.
5. **Взаимоисключение без доп. полей.** `nvidia.low-latency-on` и `nvidia.low-latency-ultra` пишут одни и те же 3 ID. Применение одного автоматически «снимает» другой (detect другого станет false). В UI их лучше показывать как один переключатель с тремя положениями (Выкл/Вкл/Ультра), сгруппировав по префиксу `nvidia.low-latency-`.
6. **Журнал для «вернуть как было».** Revert в каталоге возвращает **дефолт драйвера**, а не прежнее пользовательское значение. Пример: на машине разработчика пользователь сам выставил V-Sync = Off. Рекомендуется перед первой записью сохранить `(settingId, Found, Value)` в журнал приложения и дать отдельную кнопку «вернуть как было».
7. **Бэкап.** Перед первым изменением DRS вызвать `ExportToFile(%ProgramData%\hateeze\backup\nvdrs-<timestamp>.txt)`. Это `NvAPI_DRS_SaveSettingsToFile`; формат файла NVIDIA — **НЕ ПРОВЕРЕНО** (запись файла не делалась), восстановление — `ImportFromFile` + `Save()`. Файлы `C:\ProgramData\NVIDIA Corporation\Drs\nvdrsdb*.bin` вручную не копировать и не подменять.
8. **Права и ошибки.** `-137` → нет админа; `-160` при Set → нет внутреннего API или ID не поддерживается этим драйвером; `-9` → неправильный размер структуры (проверить `sizeof`). Любую ошибку показывать текстом из `NvAPI_GetErrorMessage`.
9. **Видимость твиков.** Категория `nvidia` скрывается, если `TryOpen` вернул null. Твики со скрытыми ID дополнительно скрываются, если `HasInternalApi == false`.

---

## 3. Настройки DRS: ID, значения, дефолты, рекомендации, оценка

Источники ID и значений — `NvApiDriverSettings.h` (NVIDIA/nvapi R615, далее «H») и `CustomSettingNames.xml` NPI 3.0.2.3 (далее «NPI»). Имена во 2-й колонке — то, что возвращает `GetSettingNameFromId` на 617.14.

| Параметр NVCP | Имя в драйвере / ID (hex · dec) | Значения | Дефолт | Для соревновательных игр | Оценка |
|---|---|---|---|---|---|
| Режим управления электропитанием | «Power management mode» `PREFERRED_PSTATE_ID` 0x1057EB71 · 274197361 | 0 Adaptive, **1 Prefer maximum performance**, 2 Driver controlled, 3 Prefer consistent performance, 4 Prefer min, 5 Optimal power («Normal») | 5 | 1 | situational |
| Режим низкой задержки | `PRERENDERLIMIT_ID` 0x007BA09E · 8102046 + «FRL Low Latency» 0x10835000 · 277041152 + ULL CPL State 0x0005F543 · 390467 | Выкл: 0/0/0; **Вкл: 1/0/1**; **Ультра: 1/1/2** | 0/0/0 | Вкл (DX11 без Reflex); Ультра — по замеру | situational |
| Макс. кол-во заранее подготовленных кадров (старый NVCP) | `PRERENDERLIMIT_ID` | 0 = app, 1…8 (MAX 0xFF) | 0 | 1 = то же, что «Вкл» | situational |
| Вертикальная синхронизация | «Vertical Sync» `VSYNCMODE_ID` 0x00A879CF · 11041231 | 0x60925292 (1620202130) по приложению; 0x08416747 (138504007) Force off; 0x47814940 (1199655232) Force on; 0x18888888 (411601032) Fast; 0x32610244 / 0x71271021 / 0x13245256 — 1/2, 1/3, 1/4 частоты | 0x60925292 | G-SYNC: On; без VRR: Off | situational |
| Адаптивный V-Sync | «Vertical Sync Tear Control» `VSYNCTEARCONTROL_ID` 0x005A375C · 5912412 | 0x96861077 (2525368439) Standard, 0x99941284 (2576618116) Adaptive | 0x96861077 | не нужен | — (не в каталоге) |
| Макс. частота кадров | «Frame Rate Limiter» `FRL_FPS_ID` 0x10835002 · 277041154 | 0 = off, 20–1000 (`FRL_FPS_MAX` 0x3FF) | 0 | частота − 3…4 при G-SYNC без Reflex | situational |
| (служебное NVCP) | «Frame Rate Limiter for NVCPL» 0x1083500A · 277041162 | запоминает положение слайдера | 0 | не трогать (см. §7) | — |
| Макс. частота кадров фонового приложения | «Background Application Max Frame Rate» 0x10835005 · 277041157 (**скрытый**) | 0 = off, NPI: 5–60 | 0 | 20–30 по желанию | situational |
| (служебное NVCP) | 0x10835006 · 277041158 | слайдер фонового лимита | 0 | не трогать | — |
| Фильтрация текстур — качество | `QUALITY_ENHANCEMENTS_ID` 0x00CE2691 · 13510289 | 0xFFFFFFF6 (4294967286) High quality, **0 Quality**, 10 (0x0A) Performance, 20 (0x14) High performance | 0 | High performance — по замеру | experimental |
| … подстановка качества | `QUALITY_ENHANCEMENT_SUBSTITUTION_ID` 0x00CE2692 | 0 нет, 1 HQ → Quality | 0 | не трогать | — |
| Оптимизация анизотропной выборки | `PS_TEXFILTER_ANISO_OPTS2_ID` 0x00E73211 · 15151633 | 0/1 | 0 (H) | можно 1 | experimental |
| (оптимизация аниз. фильтра, без отдельного пункта NVCP) | `PS_TEXFILTER_BILINEAR_IN_ANISO_ID` 0x0084CD70 | 0/1 | 0 | не трогать | — |
| Отрицательное отклонение УД | `PS_TEXFILTER_NO_NEG_LODBIAS_ID` 0x0019BB68 · 1686376 | 0 Allow, 1 Clamp | 0 | Allow | **avoid** (Clamp) |
| Трилинейная оптимизация | `PS_TEXFILTER_DISABLE_TRILIN_SLOPE_ID` 0x002ECAF2 · 3066610 | **0 = оптимизация ВКЛ**, 1 = выкл | 0 | оставить 0 (уже «быстрый» вариант) | — (менять бессмысленно; в каталог не включено) |
| Потоковая оптимизация | `OGL_THREAD_CONTROL_ID` 0x20C1221E · 549528094 | 0 Auto, 1 On, 2 Off | 0 | Auto | situational (только OpenGL) |
| Кэш шейдеров | «Shader Cache» `PS_SHADERDISKCACHE_ID` 0x00198FFF · 1675263 | 0/1 | 1 | 1 | avoid (Off) |
| Размер кэша шейдеров | «Shader disk cache maximum size» `PS_SHADERDISKCACHE_MAX_SIZE_ID` 0x00AC8497 · 11306135 | МБ: 0 off, 128, 256, 512, 1024, 4096, 5120, 8192, 10240, **16384**, 102400, 0xFFFFFFFF = безлимит | **0x4000 = 16 ГБ** | 100 ГБ / безлимит при нехватке | situational |
| Кэш шейдеров (OpenGL) | NPI 0x208F3F2E | 0/1 | 1 (?) | не трогать | — |
| Предкомпиляция шейдеров в простое | NPI 0x00EAD189 («Shader Cache - Pre-compile Shader Options», 595.97+) | 0 off, 1 low, 2 med, 3 high | ? (на машине не задано) | НЕ ПРОВЕРЕНО | — |
| Тройная буферизация | «Triple buffering» `OGL_TRIPLE_BUFFER_ID` 0x20FDD1F9 · 553505273 | 0/1 (только OpenGL + V-Sync) | 0 | 0 | avoid (On) |
| Технология монитора / G-SYNC (на приложение) | «G-SYNC» `VRR_APP_OVERRIDE_ID` 0x10A879CF · 279476687 | 0 Allow (G-SYNC), 1 Force off, 2 Disallow, 3 ULMB, 4 Fixed refresh | 0 | 0 | — (док) |
| Глобальный режим G-SYNC | «Enable G-SYNC globally» `VRR_MODE_ID` 0x1194F158 · 294973784 | 0 off, 1 fullscreen, 2 fullscreen+windowed | 1 | 2 (если G-SYNC включён в NVCP) | — (док, см. ниже) |
| | «VRR requested state» `VRRREQUESTSTATE_ID` 0x1094F1F7 · 278196727 | 0/1/2 как выше | 1 | — | — |
| | «Toggle the VRR global feature» `VRRFEATUREINDICATOR_ID` 0x1094F157 | 0/1 | (1 на машине) | — | — |
| Переменная частота обновления (VRR для Windows) | «Variable refresh Rate» `VSYNCVRRCONTROL_ID` 0x10A879CE | 0 off, 1 on, 0x9F95128E not supported | 1 | 1 | — |
| Предпочтительная частота обновления | «Preferred refresh rate» `REFRESH_RATE_OVERRIDE_ID` 0x0064B541 · 6600001 | 0 Application-controlled, 1 Highest available (маска 0xFF0 — low-latency RR, не документирована) | 0 | 1 для старых fullscreen-игр | situational |
| CUDA — принудительное P2 | NPI «CUDA - Force P2 State» 0x50166C5E · 1343646814 (**скрытый, вне списков**) | 0 Off, 1 On | 1 (NPI OverrideDefault) | для игр не нужно | situational |
| CUDA — Sysmem fallback policy | 0x10ECECC9 (на машине 0) | 0 driver default, 1 prefer no fallback, 2 prefer fallback | 0 | не трогать | — |
| rBAR — Enable / Enable v2 / Options / Size limit | 0x000F00BA · 983226 / 0x000BFA21 · 784929 (616.56+, 0 off, 1 auto, 2 on) / 0x000F00BB / 0x000F00FF (**QWORD**, 200 MB…32 GB) | — | не задано | не форсировать глобально | **avoid** |
| DLSS-SR: подмена DLL | «Enable DLSS-SR override» `NGX_DLSS_SR_OVERRIDE_ID` 0x10E41E01 · 283385345 | 0/1 | 0 | опционально | experimental |
| DLSS-SR: пресет | «Override DLSS-SR presets» `NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID` 0x10E41DF3 · 283385331 | 0 off, 1…15 пресеты A…O (J/K — Transformer gen1, L/M — gen2 по NPI), 0x00FFFFFF Latest | 0 | Latest (вместе с override) | experimental |
| DLSS: Streamline override | «Enable Streamline override» `SL_DLSS_OVERRIDE_ID` 0x10E41E06 (616.56+) | 0/1 | 0 | — | — |
| DLSS: профиль пресета | NPI 0x00634291 «Forced Model Preset Profile» | 0 N/A, 1 Recommended, 2 Custom | — | НЕ ПРОВЕРЕНО | — |
| Ansel | «Enable Ansel» `ANSEL_ENABLE_ID` 0x1075D972 · 276158834 | 0/1 | 1 | 0 только при конфликтах | situational |
| | «NVIDIA Predefined Ansel Usage» `ANSEL_ALLOW_ID` 0x1035DB89 | 0/1 | 1 (predefined в профилях игр) | не трогать | — |
| Метод презентации Vulkan/OpenGL | «Vulkan/OpenGL present method» `OGL_CPL_PREFER_DXPRESENT_ID` 0x20D690F8 | 0 native, 1 layered on DXGI, 2 Auto | 2 | Auto | — |
| Экспорт счётчиков производительности | «Export Performance Counters» `EXPORT_PERF_COUNTERS_ID` 0x108F0841 | 0/1 | 0 | 0 | — (не путать с RmProfilingAdminOnly, §4.10) |

### 3.1 Пояснения по ключевым настройкам

**Power management mode (`PREFERRED_PSTATE`).** NVIDIA в статье о Reflex пишет, что «Prefer maximum performance» держит GPU на высоких частотах и снижает задержку, когда игра упирается в CPU, ценой энергопотребления. В GPU-bound сцене эффекта нет. Важно: ставить в **глобальный** профиль. Предопределённые профили отдельных приложений (DWM, Explorer и т. п.) у NVIDIA часто Adaptive (по djdallmann), поэтому рабочий стол не будет держать P0. Оценка: **situational** (CPU-bound, высокий FPS при неполной загрузке GPU; ноутбуки — только от сети).

**Low Latency Mode.** Это не одна настройка, а **три**:
- Выкл = `PRERENDERLIMIT 0` (app) + `ULL Enabled 0` + `CPL State 0` — проверено чтением на машине: NVCP в состоянии «Выкл».
- Вкл = `PRERENDERLIMIT 1` + `ULL Enabled 0` + `CPL State 1`.
- Ультра = `PRERENDERLIMIT 1` + `ULL Enabled 1` + `CPL State 2`.

Соответствие для «Вкл»/«Ультра» взято из NPI (описание `0x10835000`: «recommended to set up Maximum Pre-rendered Frames flag to 1») и из сообщества (Guru3D/NPI). Чтением после переключения в NVCP на этой машине — **НЕ ПРОВЕРЕНО**, так как это изменение системы. `CPL State` нужен только для отображения в NVCP, но записывать его надо, иначе панель покажет «Выкл».

NVIDIA (Gamescom 2019): «Вкл.» = `Max_Prerendered_Frames = 1`; «Ультра» — just-in-time scheduling, «до 33%» ниже задержка. Работает в DX9/DX11; в DX12/Vulkan «the game decides when to queue the frame». Больше всего пользы при GPU-bound и ~60–100 FPS. Reflex имеет приоритет над NULL, и NVIDIA рекомендует Reflex. С G-SYNC + V-Sync «Ультра» по наблюдениям Blur Busters автоматически ставит лимит (~138 FPS на 144 Гц, драйвер 441.08+). Если у пользователя свой лимитер — лучше «Вкл.». Оценка: **situational**.

**Vertical sync.** Для G-SYNC: «G-SYNC + V-SYNC (NVCP) + лимит FPS ниже частоты» (Blur Busters G-SYNC 101, замеры). Без VRR V-Sync добавляет задержку, а «Fast Sync» полезен только при FPS ≫ Гц. На машине уже Force off (пользователь). Оценка: **situational**.

**Max Frame Rate (v3).** Появился в 441.87 (тогда 40–240 FPS), диапазон 20–1000 — с 442.19. Внешние ограничители в замерах Blur Busters дают большую задержку, чем внутриигровые, но для G-SYNC главное — держать FPS ниже частоты. NVCP для отображения хранит положение слайдера в `0x1083500A`. Каталог пишет только `0x10835002`. Покажет ли NVCP включённый лимит корректно — **НЕ ПРОВЕРЕНО** (см. §7).

**Texture filtering.** На Turing+ эффект в пределах погрешности; PC-Tuning рекомендует «High performance» только с собственным бенчмарком. «Trilinear optimization»: в H значение **0 = оптимизация включена** (NPI: «0 = On (will be ignored if using high quality)»), дефолт и так «быстрый». **Negative LOD bias = Clamp** не ускоряет и может «замылить» текстуры в играх с DLSS/DLAA/TAA, которые сознательно ставят отрицательный bias → avoid.

**Threaded optimization (`OGL_THREAD_CONTROL`).** Только OpenGL (префикс `OGL_` в H). Действие зависит от игры → оставить Auto, менять только для конкретной игры.

**Shader cache size.** H (R615): `PS_SHADERDISKCACHE_MAX_SIZE_DEFAULT = 0x4000` (МБ) = 16 ГБ; прочитано на машине 0x4000. Сторонние источники называют 1/4/10 ГБ — это **старые** дефолты, они менялись с версиями. Увеличение помогает только при реальном переполнении кэша (`%LOCALAPPDATA%\NVIDIA\DXCache`, `GLCache`). Выключение кэша — avoid.

**Triple buffering.** Только OpenGL и только вместе с V-Sync. Добавляет кадр в очередь — для соревновательных игр avoid.

**G-SYNC (в каталог не включено).** Главный переключатель «Включить G-SYNC/G-SYNC Compatible» в NVCP привязан к **дисплею** и хранится не в DRS. Где именно и через какой NVAPI он задаётся — НЕ ПРОВЕРЕНО. В DRS живут производные настройки: `VRR_MODE` (глобальный режим fullscreen / +windowed; на машине **не задан** ни в одном API → действует дефолт 1), `VRRREQUESTSTATE`, `VRR_APP_OVERRIDE`. Какие из них пишет NVCP при выборе «для оконного и полноэкранного режимов» — **НЕ ПРОВЕРЕНО**. Рекомендация для UI: дать ссылку «Настройте G-SYNC в Панели управления NVIDIA» и показывать подсказку про связку V-Sync + лимит.

**Preferred refresh rate.** Работает только для эксклюзивного полноэкранного режима, когда игра выбирает частоту сама. Маска `REFRESH_RATE_OVERRIDE_LOW_LATENCY_RR_MASK = 0xFF0` в H не документирована — не использовать.

**CUDA – Force P2 State.** ID `0x50166C5E` отсутствует и в публичном (130), и во внутреннем (220) списке ID драйвера 617.14, в Base Profile не задан. Пишется ли он вообще через SetSetting — **НЕ ПРОВЕРЕНО** (NPI его пишет). Движок обязан перечитать значение после Save. Для игр не нужен (PC-Tuning: только CUDA-нагрузка).

**rBAR.** Для QWORD «Size Limit» нужна поддержка `NVDRS_QWORD_TYPE` (NPI добавил её в мае 2026: «Final Fix for R610.xx - added QWORD support»). Формат битовой маски «Options» не документирован. NVIDIA включает rBAR только для игр из белого списка, а TechSpot показал регрессии → **avoid** для глобального форсирования.

**DLSS overrides.** ID есть в публичном списке 617.14: имена «Enable DLSS-SR override» и «Override DLSS-SR presets» читаются публичным `GetSettingNameFromId`. NVIDIA App ставит их **на профиль игры**. Глобальное значение (Base Profile) действует на все DLSS-игры, если игра не запрещает подмену. По NPI: «Only DLSS2+ games support the override, certain games may also disallow using it» — то есть эффект глобального значения **НЕ ПРОВЕРЕН**. Это качество картинки, не задержка → experimental, опционально.

**Ansel.** `ANSEL_ENABLE` в Base Profile не задан (= 1). PC-Tuning советует выключать из-за инъекции во все игры и возможных конфликтов. Влияния на FPS нет → situational. Устаревший способ — `NvCameraEnable.exe off` из папки драйвера; в 617.14 не проверялся.

---

## 4. Реестр, службы и задачи NVIDIA

### 4.1 Как найти подключ класса GPU (для `{{nvidiaGpuIndex}}`)

В каталоге пути вида `SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\{{nvidiaGpuIndex}}` содержат **плейсхолдер**. Это **требование к движку**: развернуть его в каждый подходящий 4-значный подключ и применить действие к каждому. Если подходящих нет — твик недоступен.

```csharp
using Microsoft.Win32;

static IReadOnlyList<string> FindNvidiaGpuClassIndexes()
{
    const string ClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    var list = new List<string>();
    using var root = Registry.LocalMachine.OpenSubKey(ClassKey);
    if (root is null) return list;
    foreach (var name in root.GetSubKeyNames())
    {
        // пропускаем "Configuration", "Properties" (последний может давать отказ в доступе)
        if (name.Length != 4 || !name.All(char.IsAsciiDigit)) continue;
        try
        {
            using var k = root.OpenSubKey(name);
            if (k is null) continue;
            var provider = k.GetValue("ProviderName") as string;               // "NVIDIA"
            var match    = k.GetValue("MatchingDeviceId") as string;           // "pci\ven_10de&dev_2882"
            var desc     = k.GetValue("DriverDesc") as string;                 // "NVIDIA GeForce RTX 4060"
            bool isNv = string.Equals(provider, "NVIDIA", StringComparison.OrdinalIgnoreCase)
                     || (match?.StartsWith(@"pci\ven_10de", StringComparison.OrdinalIgnoreCase) ?? false)
                     || (desc?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ?? false);
            if (isNv) list.Add(name);   // "0000"
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
    }
    return list;
}
```

Более точный вариант — только **присутствующие** устройства. Взять `PNPDeviceID` из `Win32_VideoController` или `Get-PnpDevice -Class Display`, оставить `PCI\VEN_10DE*` и прочитать `HKLM\SYSTEM\CurrentControlSet\Enum\<PNPDeviceID>\Driver` → `{4d36e968-...}\0000`. В подключах класса могут оставаться «призраки» старых видеокарт; запись в них безвредна, но для detect лучше учитывать только присутствующие. Значения в ключе класса **сбрасываются при переустановке или чистой установке драйвера**. Движок должен показывать твик как «не применён», если значения пропали.

### 4.2 Телеметрия NVIDIA: что реально есть в 2025–2026

| Механизм | Где | Актуальность | Что в каталоге |
|---|---|---|---|
| Служба `NvTelemetryContainer` («NVIDIA Telemetry Container») | GFE-эпоха, драйверы до ~2019–2021 | **Отсутствует** на 617.14 (проверено); privacy.sexy отмечает отсутствие на Win11 уже с 497.09 | `nvidia.telemetry-legacy-tasks` (service, движок пропускает отсутствующую) |
| Задачи `NvTmMon_`, `NvTmRep_`, `NvTmRepOnLogon_`, `NvTmRep_CrashReport1..4_{B2FE1952-…}` | GFE / старые пакеты, `C:\Program Files\NVIDIA Corporation\...\NvTmRep.exe` | **Отсутствуют** на 617.14. Есть ли они с NVIDIA App 2025–2026 — НЕ ПРОВЕРЕНО | там же (task) |
| `NvProfileUpdaterDaily_`/`OnLogon_`, `NvDriverUpdateCheckDaily_`, `NVIDIA GeForce Experience SelfUpdate_`, `NvNodeLauncher_` | GFE | Отсутствуют. Профиль-апдейтер теперь **плагин контейнера** | `nvidia.avoid-profile-updater-off` (не телеметрия) |
| `HKLM\SOFTWARE\NVIDIA Corporation\NvControlPanel2\Client\OptInOrOutPreference` = 0 | согласие на сбор данных NVCP | Строка есть в `nvcpl.dll` 617.14; значения на машине нет | `nvidia.telemetry-optout-registry` |
| `HKLM\SOFTWARE\NVIDIA Corporation\Global\FTS\EnableRID44231/64640/66610` = 0 | старые feature toggles телеметрии | Ключа `Global\FTS` на 617.14 нет. Флаги FTS переехали в `nvlddmkm\Parameters\FTS` с другими RID; трогать их нельзя | там же (experimental) |
| `HKLM\SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\Startup\SendTelemetryData` = 0 | старый флаг | Ключа нет; действует ли — НЕ ПРОВЕРЕНО | там же |
| Плагины `GamesessionTelemetry_Plugin` (`_NvGSTPlugin.dll`, Session) и `NvTelemetryEventAnalyzer_DeviceDriver_Plugin` (`_DisplayDriverRAS.dll`, LocalSystem) | NVDisplay.Container | **Актуально** (617.14, optional-плагины; журнал `C:\ProgramData\NVIDIA Corporation\GameSessionTelemetry\telemetryPlugin.log`) | `nvidia.container-telemetry-plugins` — `DisableLoad=1` в `...\Parameters\LocalSystem\_DisplayDriverRAS` и `...\Parameters\Session\_NvGSTPlugin` (experimental, **НЕ ПРОВЕРЕНО** записью) |
| Отключение всей службы `NVDisplay.ContainerLocalSystem` | | Ломает NVCP | `nvidia.display-container-disable` (experimental, risk medium) |

Как проверить твик плагинов (для QA): после перезагрузки в `C:\ProgramData\NVIDIA\NVDisplay.ContainerLocalSystem.log` (и `.log_backup1`) не должно быть `Loaded optional plugin NvTelemetryEventAnalyzer_DeviceDriver_Plugin`, а в `DisplaySessionContainer1.log` — `Loaded optional plugin GamesessionTelemetry_Plugin`. Если строки остались, имя ключа неверное (возможно, нужно имя плагина, а не DLL, или запись через `ModuleMap\<name>`), и твик надо пометить как неработающий.

### 4.3 HDCP — `RMHdcpKeyglobZero` (DWORD 1, ключ класса)

djdallmann: значение создаёт «неподдерживаемую» конфигурацию HDCP. NVCP → «Просмотр состояния HDCP» показывает «This display does not support HDCP as configured». Требуется перезагрузка; переустановка драйвера сбрасывает значение. Строка `RMHdcpKeyglobZero` есть в `nvlddmkm.sys` 617.14. **Пользы для FPS и задержки не измерено.** Цена — не воспроизводится DRM-контент (стриминг в HD/4K, Blu-ray). Оценка: **experimental**, risk medium. Откат: `delete` (по умолчанию значения нет).

### 4.4 `DisableDynamicPstate` (DWORD 1, ключ класса)

djdallmann проверил через `nvidia-smi`, что GPU держит P0 даже при профилях Adaptive/Optimal и на рабочем столе. PC-Tuning приводит этот ключ с обязательным собственным бенчмарком и предупреждением, что частота может превысить заданную при разгоне. Измеренного выигрыша в задержке нет. Цена — высокая мощность и температура в простое, на ноутбуках разряд батареи. Оценка: **experimental**, risk medium; `reboot`. Чем отличается от «Prefer maximum performance»: тот режим действует **только пока работает 3D-приложение**, а этот — всегда.

### 4.5 PowerMizer / `PerfLevelSrc` — устарело → avoid

`PerfLevelSrc = 0x2222`, `PowerMizerEnable = 0`, `PowerMizerLevel(AC) = 1` — механизм старых GPU (до Kepler). В `nvlddmkm.sys` 617.14 осталась строка `PerfLevelSrc`, а `PowerMizerEnable` уже **нет**. Документации и замеров для современных GPU нет; нужное поведение дают `PREFERRED_PSTATE` / `DisableDynamicPstate`.

### 4.6 «Disable Ansel»

Способ через DRS — `ANSEL_ENABLE_ID = 0` (§3). Старый способ `NvCameraEnable.exe off` (в папке драйвера/NvCamera) на 617.14 не проверялся.

### 4.7 NVIDIA Display Container LS

Служба `NVDisplay.ContainerLocalSystem` (Automatic, LocalSystem) поднимает LocalSystem-контейнер и через сторожа — Session-контейнер (§1.2). Без неё в Win11 не открывается Appx-панель NVIDIA, пропадает пункт меню рабочего стола, перестают приходить OTA-обновления профилей игр, не работает NvTopps (политики питания и производительности, важно для ноутбуков). DRS-настройки через NVAPI работают и без контейнера: NVAPI обращается к БД в процессе вызывающего. Это логический вывод, **НЕ ПРОВЕРЕНО** с остановленной службой. Обновление драйвера вернёт `Automatic`. После отката нужно запустить службу (`sc start NVDisplay.ContainerLocalSystem`) или перезагрузиться: в схеме `service` нет поля «start» — см. §6.

### 4.8 Preemption — `DisablePreemption`, `DisableCudaContextPreemption` → avoid

Строки есть в `nvlddmkm.sys` 617.14 рядом с `EnableMidBufferPreemption`, `EnableCEPreemption`, `EnableMidGfxPreemption`, `DisablePreemptionOnS3S4`, то есть драйвер их **читает**. Microsoft (GPU Preemption, WDDM 1.2+): если ОС не может вытеснить долгие пакеты, задерживается работа DWM («glitches during window transitions and animations»), а TDR «might repeatedly reset the GPU, and eventually cause a system bug check». Сюда же относится Windows-ключ `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Scheduler\EnablePreemption = 0`. Замеров выгоды нет. **avoid**, risk high.

### 4.9 `EnableTiledDisplay` → avoid

Касается только «плиточных» мониторов (5K/8K MST). В ключе класса INF уже ставит `AllowTiledDisplayOverride = 1`. Данных о влиянии на обычные конфигурации нет.

### 4.10 Контекстное меню и счётчики производительности

- **Меню рабочего стола «Панель управления NVIDIA».** `HKCU\Software\NVIDIA Corporation\Global\NvCplApi\Policies\ContextUIPolicy` (DWORD): **0 = скрыть**, **2 = показать**. Значения взяты из сторонних инструкций (Eleven Forum, winhelponline). По умолчанию значения нет. `nvcpl.dll`/`nvshext.dll` 617.14 читают этот путь (§1.3); `nvshext.dll` также смотрит `SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvCplApi\Policies`. Есть отзывы, что на отдельных версиях пункт возвращался. Оценка: **situational** (косметика), `reboot = explorer`. Альтернатива — удалить `HKCR\Directory\Background\shellex\ContextMenuHandlers\NvCplDesktopContext`. **Не использовать**: драйвер восстанавливает ключ при обновлении, а это вмешательство в регистрацию COM.
- Не путать с `HKCU\Software\NVIDIA Corporation\Global\CoProcManager\ShowContextMenu` (на машине = 1) — это пункт «Запустить с графическим процессором» для Optimus.
- **«Разработчик → Управление счётчиками производительности GPU».** `RmProfilingAdminOnly`: 1 — только админы (дефолт с 419.17, бюллетень NVIDIA по side-channel, ноябрь 2018), 0 — все. По строкам `nvlddmkm.sys` значение читается из ветки `Global\NVTweak`; вероятный путь `HKLM\SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NVTweak` — **НЕ ПРОВЕРЕНО**. NVIDIA (ERR_NVGPUCTRPERM): с R610 пункт есть в NVIDIA App (System > Advanced > Developer); «Starting with driver R610, Windows Control Panel support is deprecated and will be dropped in a future release». Для игр бесполезно и ослабляет защиту → **avoid**.

---

## 5. Сводка каталога (`nvidia-drs-tweaks.tweaks.json`)

| id | Тип действий | evidence | risk | reboot |
|---|---|---|---|---|
| nvidia.power-max-performance | nvdrs | situational | low | none |
| nvidia.low-latency-on | nvdrs ×3 | situational | low | none |
| nvidia.low-latency-ultra | nvdrs ×3 (скрытые ID) | situational | low | none |
| nvidia.vsync-mode | nvdrs, select | situational | low | none |
| nvidia.max-frame-rate | nvdrs, value 20–1000 | situational | low | none |
| nvidia.background-max-frame-rate | nvdrs (скрытый), value 5–60 | situational | low | none |
| nvidia.texture-filtering-quality | nvdrs, select | experimental | low | none |
| nvidia.aniso-sample-optimization | nvdrs | experimental | low | none |
| nvidia.threaded-optimization | nvdrs, select | situational | low | none |
| nvidia.shader-cache-size | nvdrs, select | situational | low | none |
| nvidia.preferred-refresh-highest | nvdrs | situational | low | none |
| nvidia.cuda-force-p2-off | nvdrs (скрытый) | situational | medium | none |
| nvidia.ansel-disable | nvdrs | situational | low | none |
| nvidia.dlss-sr-override-latest | nvdrs ×2 | experimental | low | none |
| nvidia.hdcp-disable | reg (класс GPU) | experimental | medium | reboot |
| nvidia.dynamic-pstate-disable | reg (класс GPU) | experimental | medium | reboot |
| nvidia.telemetry-optout-registry | reg ×5 | experimental | low | reboot |
| nvidia.telemetry-legacy-tasks | service + task ×7 | situational | low | none |
| nvidia.container-telemetry-plugins | reg ×2 | experimental | low | reboot |
| nvidia.display-container-disable | service | experimental | medium | none |
| nvidia.desktop-context-menu-hide | reg HKCU | situational | low | explorer |
| nvidia.avoid-* (9 шт.) | — | avoid | — | — |

`avoid`: negative-lod-clamp, shader-cache-off, triple-buffering-on, rebar-force-global, perf-counters-all-users, disable-preemption, tiled-display-off, powermizer-legacy, profile-updater-off.

## 6. Требования к движку, которые выходят за базовую схему

1. **Новый тип** `nvdrs` — по §2.6.
2. **Плейсхолдер** `{{nvidiaGpuIndex}}` в `path` reg-действий — по §4.1. Применять ко всем найденным подключам; detect = все совпали.
3. **Отсутствующие службы и задачи** (`NvTelemetryContainer`, `NvTm*`) — пропускать без ошибки. Если ни один объект твика не найден, скрывать твик как «неприменимо».
4. **Создание отсутствующих ключей** при `op:set` (например `...\Parameters\Session\_NvGSTPlugin`, `HKCU\...\NvCplApi\Policies`).
5. **Revert службы в Automatic** — после смены типа запуска желательно стартовать службу. В схеме нет поля `start`: либо добавить его, либо держать в движке правило «revert → Automatic ⇒ sc start».
6. **Группы взаимоисключения** — `nvidia.low-latency-on` / `nvidia.low-latency-ultra` (одни и те же ID, см. §2.6 п.5).
7. **Предупреждение о профилях игр** — для DRS-твиков показывать: «Если у игры есть собственный профиль NVIDIA с этой настройкой (например, Apex Legends — макс. кадров 1), глобальное значение на неё не действует. Перезапустите игру после применения».

## 7. Не проверено / открытые вопросы

1. **Запись** через внутренний `NvAPI_DRS_SetSetting` 0x8A2CF5F5 для `0x10835000`, `0x0005F543`, `0x10835005`, `0x50166C5E` на 617.14 — НЕ ПРОВЕРЕНО (запрещено менять систему; чтение работает). Проверить на тестовой машине: Set → Save → перечитать → открыть NVCP и убедиться, что «Режим низкой задержки» показывает «Ультра».
2. Точное соответствие «Вкл/Ультра» NVCP ↔ 3 DRS-ID — по данным NPI и сообщества, не перечитано после переключения в NVCP.
3. Нужно ли дублировать `0x1083500A` («Frame Rate Limiter for NVCPL») и `0x10835006`, чтобы NVCP корректно отображал включённый лимит.
4. Какие DRS-ID пишет NVCP для G-SYNC «оконный и полноэкранный» и где хранится главный переключатель G-SYNC дисплея.
5. Твик `DisableLoad` для плагинов телеметрии: имя подключа (DLL или плагин) — не проверено записью; критерий проверки см. §4.2.
6. Путь `RmProfilingAdminOnly` в Windows (вероятно `nvlddmkm\Global\NVTweak`).
7. Значения `ContextUIPolicy` (0/2) — из сторонних источников; не проверено на Appx-NVCP 8.1.969.
8. Работа глобального DLSS-override на играх вне белого списка NVIDIA App.
9. Задачи и службы, которые ставит NVIDIA App 2025–2026 (на машине App нет).
10. Формат файла `NvAPI_DRS_SaveSettingsToFile` (бэкап не создавался — это запись файла от имени драйвера; на тестовой машине проверить, что `ImportFromFile` его читает).
11. Диапазон слайдера «Фоновый лимит FPS» в NVCP (в каталоге 5–60 по NPI).
12. Работают ли DRS-изменения при остановленной службе Display Container.

## 8. Источники

**Первоисточники NVIDIA**
- NVIDIA/nvapi — `nvapi_interface.h` (ID функций): https://github.com/NVIDIA/nvapi/blob/main/nvapi_interface.h
- NVIDIA/nvapi — `nvapi.h` (DRS-структуры, `#pragma pack(4)`, сигнатуры): https://github.com/NVIDIA/nvapi/blob/main/nvapi.h
- NVIDIA/nvapi — `nvapi_lite_common.h` (`MAKE_NVAPI_VERSION`, `NvAPI_ShortString`, `EnumPhysicalGPUs`, `SYS_GetDriverAndBranchVersion`): https://github.com/NVIDIA/nvapi/blob/main/nvapi_lite_common.h
- NVIDIA/nvapi — `NvApiDriverSettings.h` (ID и значения настроек, дефолты): https://github.com/NVIDIA/nvapi/blob/main/NvApiDriverSettings.h
- NVIDIA, Gamescom 2019 Game Ready Driver (Low Latency Mode): https://www.nvidia.com/en-us/geforce/news/gamescom-2019-game-ready-driver/
- NVIDIA, Reflex Low Latency Platform (Reflex vs NULL, Power management mode): https://www.nvidia.com/en-us/geforce/news/reflex-low-latency-platform/
- NVIDIA Developer, ERR_NVGPUCTRPERM (счётчики производительности, R610): https://developer.nvidia.com/nvidia-development-tools-solutions-err_nvgpuctrperm-permission-issue-performance-counters

**Microsoft**
- GPU Preemption (WDDM 1.2+): https://learn.microsoft.com/en-us/windows-hardware/drivers/display/gpu-preemption

**Исходники и исследования**
- NVIDIA Profile Inspector — `NvapiDrsWrapper.cs`: https://github.com/Orbmu2k/nvidiaProfileInspector/blob/master/nvidiaProfileInspector/Native/NVAPI/NvapiDrsWrapper.cs
- NPI — `CustomSettingNames.xml`: https://github.com/Orbmu2k/nvidiaProfileInspector/blob/master/nvidiaProfileInspector/CustomSettingNames.xml
- NPI — `DrsSettingsServiceBase.cs` / `DrsSettingsService.cs` (StoreDwordValue, ResetValue): https://github.com/Orbmu2k/nvidiaProfileInspector/tree/master/nvidiaProfileInspector/Common
- NPI коммит 2022-11-13 (переход на внутренние ID): https://github.com/Orbmu2k/nvidiaProfileInspector/commit/75c99ea6c71a522835aac785fcb1af7480c2664b
- NPI коммит 2023-03-26 (fallback function IDs): https://github.com/Orbmu2k/nvidiaProfileInspector/commit/f003324d5514a48d695af47995d0802da4eb6202
- NPI коммит 2026-05-27 (QWORD для R610): https://github.com/Orbmu2k/nvidiaProfileInspector/commit/dde3b68f12178d9ddcf3a9ca41e1c6f32d9a1789
- NPI issue #46 (NVAPI не даёт читать и писать ULL с R445.75): https://github.com/Orbmu2k/nvidiaProfileInspector/issues/46
- NPI issue #94 (NVAPI_SETTING_NOT_FOUND при записи FRL): https://github.com/Orbmu2k/nvidiaProfileInspector/issues/94
- NvAPIWrapper — FunctionId.cs (сверка ID): https://github.com/falahati/NvAPIWrapper/blob/master/NvAPIWrapper/Native/Helpers/FunctionId.cs
- valleyofdoom/PC-Tuning — configure-nvidia.md: https://github.com/valleyofdoom/PC-Tuning/blob/main/docs/configure-nvidia.md
- djdallmann/GamingPCSetup — WINDRIVERS (HDCP, DisableDynamicPstate): https://github.com/djdallmann/GamingPCSetup/blob/master/CONTENT/RESEARCH/WINDRIVERS/README.md
- ChrisTitusTech/winutil — `config/tweaks.json` (NVIDIA-твиков нет, проверено grep): https://github.com/ChrisTitusTech/winutil/blob/main/config/tweaks.json
- privacy.sexy — windows.yaml (телеметрия NVIDIA: OptInOrOutPreference, FTS RID, SendTelemetryData, NvTm*): https://github.com/undergroundwires/privacy.sexy/blob/master/src/application/collections/windows.yaml
- beatcracker/toptout issue #3 (NvTelemetryContainer, NvTm*): https://github.com/beatcracker/toptout/issues/3

**Замеры и сообщество** (вторичные источники, учтены с оговорками)
- Blur Busters G-SYNC 101 (V-Sync, лимиты FPS, задержка): https://blurbusters.com/gsync/gsync101-input-lag-tests-and-settings/
- Blur Busters forum, «Driver 441.08: Ultra-Low Latency Now with G-SYNC Support»: https://forums.blurbusters.com/viewtopic.php?p=44687
- Geeks3D, драйвер 441.87 (Max Frame Rate 40–240): https://geeks3d.com/?p=9974
- TechSpot, Resizable BAR (регрессии в части игр): https://www.techspot.com/review/2234-nvidia-resizable-bar/
- gHacks, NVIDIA telemetry (2016, исторический контекст): https://www.ghacks.net/2016/11/07/nvidia-telemetry-tracking/
- Eleven Forum, NVIDIA Control Panel context menu (ContextUIPolicy): https://www.elevenforum.com/t/add-or-remove-nvidia-control-panel-context-menu-in-windows-11.7984/
- winhelponline, удаление пункта NVIDIA из контекстного меню: https://www.winhelponline.com/blog/how-to-remove-nvidia-control-panel-from-the-context-menu/
