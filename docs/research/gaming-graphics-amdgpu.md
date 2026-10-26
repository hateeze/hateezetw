# Игровые функции Windows, графика, VBS и реестровые твики AMD GPU

**Ключ темы:** `gaming-graphics-amdgpu` · **Дата исследования:** 2026-10-26 · **Проект:** hateeze (C# .NET 10, WebView2, всегда от администратора)
**Машиночитаемый каталог:** `gaming-graphics-amdgpu.tweaks.json` (32 записи: 3 proven, 12 situational, 3 experimental, 14 avoid). JSON сгенерирован скриптом и проверен парсером; все PowerShell-скрипты внутри разобраны `System.Management.Automation.Language.Parser` без ошибок, detect-скрипты прогнаны на машине разработчика (только чтение).

> Вне темы (исследуются отдельно): NVIDIA, Intel GPU/CPU, планировщик/MMCSS (`SystemProfile\Tasks\Games`), таймеры, сеть, службы и задачи (кроме служб AMD), электропитание (кроме Fast Startup).

---

## 0. Кратко (TL;DR)

| ID в каталоге | Что | Оценка | Риск | Рекомендация для игр |
|---|---|---|---|---|
| `gaming.game-mode` | Game Mode (AutoGameModeEnabled) | **proven** (поведение документировано) | low | Оставить ВКЛ (дефолт) |
| `gaming.background-recording-off` | Фоновая запись «Записать то, что произошло» | **proven** | low | Выключить |
| `gaming.game-dvr-off` | Game DVR / захват Game Bar | situational | low | Выключить, если пишете через OBS/ShadowPlay/ReLive. **Не трогать на 7950X3D/9950X3D** |
| `gaming.game-dvr-policy-off` | Политика AllowGameDVR=0 | situational | low | Только если запись включается сама или пользователей несколько |
| `gaming.fso-global-off` | Глобальное отключение FSO через GameConfigStore | experimental | low | Не по умолчанию; точечно на exe + PresentMon |
| `gaming.background-apps-off` | Политика LetAppsRunInBackground=2 | situational | medium | На слабых ПК / без Store-приложений |
| `graphics.hags` | HAGS (HwSchMode) | situational | medium | ВКЛ при DLSS FG / FSR 3 FG; иначе оставить дефолт |
| `graphics.windowed-optimizations` | Оптимизации для игр в оконном режиме (Win11) | **proven** | low | ВКЛ (DX10/11 в окне/borderless) |
| `graphics.vrr-optimize` | Переключатель VRR для DX11 | situational | low | ВКЛ только с VRR-монитором и старыми DX11-играми |
| `graphics.mpo-disable` | MPO off (OverlayTestMode=5) | situational | medium | Только при мерцании/вспышках/статтерах окон |
| `graphics.mpo-disable-overlays` | DisableOverlays=1 (25H2+) | experimental | medium | Только если OverlayTestMode не помог |
| `graphics.hvci-off` | Memory Integrity (HVCI) off | situational | **high** | **Не отключать при Valorant/LoL/FACEIT** |
| `graphics.vbs-off` | VBS off полностью | situational | **high** | То же; бессильно при Hyper-V/VMP/WSL2 |
| `graphics.vmp-off` | Платформа виртуальной машины off | situational | medium | Если не нужен WSL2/Android/Docker |
| `graphics.transparency-off` | Прозрачность off | experimental | low | По вкусу |
| `graphics.visual-effects-performance` | Анимации/тени off через SPI | situational | low | По вкусу (отзывчивость UI, не FPS) |
| `cleanup.startup-delay-off` | Задержка автозагрузки off | situational | low | По вкусу |
| `power.fast-startup-off` | Быстрый запуск off | situational | low | Выключить (чистая загрузка драйверов) |
| `graphics.tdr-off`, `graphics.hags-blocklist-remove`, `graphics.userpreferencesmask-raw`, `graphics.dwm-disable` | — | **avoid** | — | Не включать |
| `amd.*` (9 записей) | ULPS, термотроттлинг, preemption, DMACopy, StutterMode, UMD-ключи, FRTC, служба External Events, Anti-Lag+ | **avoid** | — | Не включать; всё — через AMD Software: Adrenalin |

**Главные выводы**

1. **Почти все «реестровые твики AMD» — avoid.** Значения не документированы AMD, известны по дампам 2016–2019 годов (эпоха Polaris/Vega/CrossFire), противоречат друг другу и перезаписываются Adrenalin. Часть из них опасна: отключение термотроттлинга, отключение compute preemption. `StutterMode` вопреки названию — энергосберегающий режим памяти, а не «режим статтеров» (подтверждено исходниками amdgpu в Linux). Всё полезное (кэш шейдеров, тесселяция, Anti-Lag, Chill, Enhanced Sync) делается в интерфейсе Adrenalin.
2. **VBS/HVCI в 2025–2026 стали частью требований античитов.** FACEIT: TPM 2.0 и Secure Boot обязательны для всех с 25.11.2025; IOMMU и VBS обязательны для части игроков (около 60%) с апреля 2025; некоторым игрокам может потребоваться Memory Integrity. Vanguard (Riot): на Win11 TPM 2.0 и Secure Boot обязательны; HVCI/IOMMU «могут потребоваться в зависимости от конфигурации» (ошибка `VAN: RESTRICTION 5`). Поэтому отключать HVCI/VBS без предупреждения нельзя.
3. **HAGS на Win11 обычно уже включён по умолчанию.** На машине разработчика `HwSchMode` отсутствует, а `D3DKMTQueryAdapterInfo(KMTQAITYPE_WDDM_2_7_CAPS)` возвращает `0xB` (Supported + Enabled). Состояние надо читать через D3DKMT, а не через реестр. В `GraphicsDrivers\BlockList\Kernel` этой Win11 23H2 стоит `DISABLE_HWSCH` для **всех AMD (1002) и Intel (8086)** GPU.
4. **MPO (`OverlayTestMode=5`) — лечение, а не ускорение.** Помогает от мерцания и вспышек окон (статья NVIDIA 5157). На 24H2/25H2 эффективность спорна. Проверять только PresentMon: dxdiag показывает возможности драйвера, а не фактическое состояние.
5. **Фоновая запись Game Bar реально стоит FPS** (Microsoft/Xbox это признаёт). На машине разработчика она включена (`HistoricalCaptureEnabled=1`), хотя само приложение Xbox Game Bar удалено.
6. **«Оптимизации для игр в оконном режиме»** — единственный «графический» твик Windows 11 с документированным снижением задержки (DX10/11 в окне и borderless). Хранится внутри одной REG_SZ-строки — писать только слиянием пар `ключ=значение;`.

---

## 1. Методика

- **Шкала доказательности** — как в задании (proven / situational / experimental / avoid). «Proven» ставится только при документированном Microsoft/вендором поведении **и** безопасности. Например, Game Mode — proven по документированному поведению (блок WU-драйверов и уведомлений), а не по росту FPS.
- **Первоисточники:** Microsoft Learn и DirectX Dev Blog, поддержка Microsoft/Xbox, NVIDIA Streamline FAQ, AMD/GPUOpen, Riot/FACEIT support, исходники Linux amdgpu. Дополнительно: valleyofdoom/PC-Tuning (README, registry-opts.md, configure-amd.md), ChrisTitusTech/winutil `config/tweaks.json`, ElevenForum (туториалы Shawn Brink с .reg-файлами и дефолтами), Tom's Hardware (замеры VBS/HVCI).
- **Машина разработчика:** только чтение (`reg query`, `Get-ItemProperty`, `Get-CimInstance`, `Get-WindowsOptionalFeature`, `dxdiag /t` в %TEMP%, `D3DKMTQueryAdapterInfo` через `Add-Type`, `powercfg /a`, `bcdedit /enum`). Ничего не изменялось.
- **Не удалось открыть напрямую** (403/429 и антибот): nvidia.custhelp.com 5157, forums.guru3d.com, страницы FACEIT (получены через curl с UA), deepwiki. Страница Microsoft «Options to optimize gaming performance in Windows 11» **удалена** (на 2026-10-26 перенаправляет на главную support.microsoft.com); используется архивная ссылка из PC-Tuning.

---

## 2. Снимок машины разработчика (2026-10-26)

Windows 11 Pro for Workstations **23H2, сборка 22631.7652** (UBR 0x1DE4), ru-RU. Ryzen 7 5700X, RTX 4060 (`PCI\VEN_10DE&DEV_2882&SUBSYS_180110DE`), драйвер 32.0.16.1714, монитор 1920×1080 @ 240 Гц (DisplayPort), WDDM 3.1.

**Важно: машина НЕ «чистая».** Ряд значений уже изменён пользователем или твикером, поэтому дефолты для revert брались из первоисточников и из `HKU\.DEFAULT` / `HKLM\...\VisualEffects\*\DefaultValue`, а не из HKCU этой машины.

| Ключ / значение | На машине | Дефолт Windows | Комментарий |
|---|---|---|---|
| `HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled` | `0` | нет значения (= ВКЛ) | Game Mode выключен пользователем |
| `HKCU\Software\Microsoft\GameBar\UseNexusForGameBarEnabled` | `0` | нет (= ВКЛ) | Кнопка Xbox на геймпаде не открывает Game Bar |
| `HKCU\System\GameConfigStore\GameDVR_Enabled` | `1` | `1` | |
| `...\GameDVR_FSEBehaviorMode` | `2` | `2` | |
| `...\GameDVR_HonorUserFSEBehaviorMode` | `0` | `0` | |
| `...\GameDVR_DXGIHonorFSEWindowsCompatible` | `0` | `0` | |
| `...\GameDVR_EFSEFeatureFlags` | `0` | `0` | |
| `...\GameDVR_FSEBehavior` | нет | нет | |
| `HKCU\...\CurrentVersion\GameDVR\AppCaptureEnabled` | `1` | `1` | |
| `HKCU\...\CurrentVersion\GameDVR\HistoricalCaptureEnabled` | **`1`** | `0` / нет | Фоновая запись включена! |
| `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR` | нет ключа | нет | |
| `HKLM\SOFTWARE\Microsoft\PolicyManager\default\ApplicationManagement\AllowGameDVR` | `value=1`, `GPBlockingRegKeyPath=Software\Policies\Microsoft\Windows\GameDVR`, `GPBlockingRegValueName=AllowGameDVR` | — | Подтверждает имя политики |
| Пакет `Microsoft.XboxGamingOverlay` (Game Bar) | **не установлен** | установлен | Есть только `Microsoft.XboxGameCallableUI` |
| `HKLM\...\WindowsRuntime\ActivatableClassId\Windows.Gaming.GameBar.PresenceServer.Internal.PresenceWriter\ActivationType` | `1` | `1` | Владелец ключа: `NT SERVICE\TrustedInstaller` |
| `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode` | нет | нет | HAGS при этом **включён** (см. ниже) |
| `GraphicsDrivers\BlockList\Kernel` | `Vendor=1002#Device=*#Version=*#Model=*#` и `Vendor=8086#...` = `DISABLE_HWSCH` (MULTI_SZ) | — | Системный блок-лист HAGS для AMD и Intel |
| `GraphicsDrivers\FeatureSetUsage\AdapterMPO_3` | `1` | — | |
| `HKLM\SOFTWARE\Microsoft\Windows\Dwm\OverlayTestMode` | **`5`** | нет | MPO уже отключён |
| `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` | нет ключа | нет | OWG/VRR/Auto HDR — дефолт |
| `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\EnableVirtualizationBasedSecurity` | `0` | зависит от установки | Но VBS **работает** (см. ниже) |
| `...\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled` | `0` | Win11 чистая установка на совместимом железе: `1` | |
| `...\Scenarios\CredentialGuard\Enabled` | `0` | | |
| `Win32_DeviceGuard` | `VirtualizationBasedSecurityStatus=2` (работает), `SecurityServicesRunning={0}`, `CodeIntegrityPolicyEnforcementStatus=2`, `AvailableSecurityProperties={1,2,3,4,...}` | | VBS работает без HVCI: включены Hyper-V, VMP и WSL; `bcdedit hypervisorlaunchtype Auto` |
| Secure Boot | `True` | | |
| TPM | AMD fTPM 2.0, ManufacturerVersion 3.94.2.5, Ready | | FACEIT: проблемная прошивка только `3.*.0.*` |
| `HKCU\...\Themes\Personalize\EnableTransparency` | **`0`** | `1` | |
| `HKCU\Control Panel\Desktop\UserPreferencesMask` | `9E 1E 07 80 12 00 00 00` | (`HKU\.DEFAULT`: `9E 3E 03 80 12 00 00 00`) | Раскладка различается — не «откатывать» на константу |
| `HKCU\Control Panel\Desktop\MenuShowDelay` | **`100`** | `400` | Чужой твик |
| `HKCU\Control Panel\Desktop\AutoEndTasks / HungAppTimeout / WaitToKillAppTimeout / LowLevelHooksTimeout` | `1 / 3000 / 3000 / 3000` | нет / 5000 / — / — | Чужие твики (вне темы, но могут влиять на хуки ввода) |
| `HKCU\...\Explorer\VisualEffects\VisualFXSetting` | нет | нет (= «Windows выбирает») | winutil ошибочно считает дефолтом `1` |
| `HKCU\...\Explorer\Serialize\StartupDelayInMSec` | **`0`** | нет ключа | Уже твикнуто |
| `HKLM\...\Session Manager\Power\HiberbootEnabled` | **`0`** | `1` | И гибернация выключена (`powercfg /a`: «Режим гибернации не включен») |
| `HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers` | `...\cs2.exe` = `~ DISABLEDXMAXIMIZEDWINDOWEDMODE` | — | FSO отключены для CS2 галочкой |
| `dxdiag` | `Hardware Scheduling: DriverSupportState:Stable Enabled:True`; `MPO MaxPlanes: 4`, `MPO Caps: ... (MPO3)`; `Graphics Preemption: Pixel`, `Compute Preemption: Dispatch` | | MPO в dxdiag = возможности драйвера, хотя `OverlayTestMode=5` |
| `D3DKMT KMTQAITYPE_WDDM_2_7_CAPS` (тип 70) | адаптер RTX 4060: `0xB` (HwSchSupported=1, HwSchEnabled=1, HwSchEnabledByDefault=0, IndependentVidPnVSyncControl=1); Basic Render: `0x0` | | Проверено вызовом из PowerShell |

---

## 3. Общие требования к реализации (C#)

### 3.1. HKCU при запуске от администратора

Приложение всегда запущено с повышением. Если у пользователя стандартная учётная запись и UAC запрашивает пароль **другого** администратора («over-the-shoulder»), то `Registry.CurrentUser`, `HKCU:` в PowerShell и `SystemParametersInfo` относятся к администратору, а не к тому, кто играет. Все HKCU-твики этого документа (GameBar, GameConfigStore, GameDVR, DirectX\UserGpuPreferences, Personalize, Serialize, AppCompatFlags) должны писаться в `HKEY_USERS\<SID вошедшего пользователя>`.

Повышенный процесс остаётся в той же сессии, поэтому SID проще всего получить так:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

static class InteractiveUser
{
    enum WTS_INFO_CLASS { WTSUserName = 5, WTSDomainName = 7 }
    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool WTSQuerySessionInformationW(IntPtr hServer, int sessionId, WTS_INFO_CLASS c, out IntPtr buf, out int bytes);
    [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr p);

    static string Query(int session, WTS_INFO_CLASS c)
    {
        if (!WTSQuerySessionInformationW(IntPtr.Zero, session, c, out var p, out _)) return "";
        try { return Marshal.PtrToStringUni(p) ?? ""; } finally { WTSFreeMemory(p); }
    }

    /// SID пользователя, владеющего интерактивной сессией этого процесса.
    public static SecurityIdentifier GetSid()
    {
        int session = Process.GetCurrentProcess().SessionId;
        string user = Query(session, WTS_INFO_CLASS.WTSUserName);
        string domain = Query(session, WTS_INFO_CLASS.WTSDomainName);
        if (string.IsNullOrEmpty(user)) return WindowsIdentity.GetCurrent().User!;
        return (SecurityIdentifier)new NTAccount(domain, user).Translate(typeof(SecurityIdentifier));
    }

    /// Корень «HKCU» нужного пользователя (его hive уже загружен, т.к. он вошёл в систему).
    public static RegistryKey OpenUserRoot(bool writable = true)
    {
        var sid = GetSid();
        if (sid == WindowsIdentity.GetCurrent().User) return Registry.CurrentUser;
        return Registry.Users.OpenSubKey(sid.Value, writable)
               ?? throw new InvalidOperationException("Hive пользователя не загружен: " + sid.Value);
    }
}
```

Для PowerShell-действий каталога (`type: "powershell"`) движок должен либо запускать скрипт в токене пользователя (сложно), либо **заменять `HKCU:\` на `Registry::HKEY_USERS\<SID>\`** перед запуском. Скрипты каталога пишут HKCU только через `-LiteralPath 'HKCU:\...'`, поэтому замена по подстроке безопасна. Скрипт визуальных эффектов (SPI) так исправить нельзя: `SystemParametersInfo` действует на профиль вызывающего процесса. В over-the-shoulder-сценарии этот твик нужно выполнять в непривилегированном дочернем процессе пользователя (например, через `CreateProcessWithTokenW` с токеном explorer.exe) или просто показывать предупреждение.

### 3.2. Снимок перед применением и семантика revert

- `revert` в каталоге = **дефолт Windows** (по заданию). Приложение дополнительно должно хранить **снимок значений до apply** (тип + данные, или факт отсутствия) и предлагать «Вернуть как было» отдельно от «Сбросить к дефолту Windows». Это особенно важно для `graphics.hvci-off`/`graphics.vbs-off`: на Win10 и на апгрейженных Win11 дефолт HVCI = выкл, а revert каталога ставит `Enabled=1`. Это может дать BSOD при несовместимых драйверах (Windows Security перед включением проверяет драйверы, а запись в реестр — нет).
- `op: "delete"` у `reg` = удалить **значение** (не ключ). Отсутствие значения ≠ 0. Например, `AutoGameModeEnabled` отсутствует = Game Mode включён.
- `PowerShell New-Item -Path <regkey> -Force` на **существующем** ключе пересоздаёт его и **теряет все значения**. В скриптах каталога `New-Item` вызывается только после `Test-Path`, без `-Force`.

### 3.3. Строковые значения со слиянием

Два места, где нельзя делать простую запись `set`:

**а) `HKCU\Software\Microsoft\DirectX\UserGpuPreferences\DirectXUserGlobalSettings` (REG_SZ)** — одна строка с несколькими парами: `SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;AutoHDREnable=…;`. Её же пишет «Параметры → Дисплей → Графика». Перезапись целиком сбросит соседние настройки (Auto HDR, VRR и т. п.). В каталоге это сделано PowerShell-слиянием (проверено на тестовых строках: `VRROptimizeEnable=0;SwapEffectUpgradeEnable=0;AutoHDREnable=1;` → `VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;AutoHDREnable=1;`). Реализация на C#:

```csharp
static void MergeDxGlobal(RegistryKey userRoot, string key, string value)
{
    using var k = userRoot.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", writable: true);
    var cur = k.GetValue("DirectXUserGlobalSettings") as string ?? "";
    var pairs = new List<KeyValuePair<string, string>>();
    foreach (var part in cur.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        int eq = part.IndexOf('=');
        if (eq <= 0) continue;
        pairs.Add(new(part[..eq].Trim(), part[(eq + 1)..].Trim()));
    }
    int idx = pairs.FindIndex(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    if (idx >= 0) pairs[idx] = new(pairs[idx].Key, value); else pairs.Add(new(key, value));
    k.SetValue("DirectXUserGlobalSettings", string.Concat(pairs.Select(p => $"{p.Key}={p.Value};")), RegistryValueKind.String);
}
static bool DxGlobalIs(RegistryKey userRoot, string key, string value) =>
    (userRoot.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences")?.GetValue("DirectXUserGlobalSettings") as string ?? "")
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Any(p => p.Equals($"{key}={value}", StringComparison.OrdinalIgnoreCase));
```

Применяется при **следующем запуске игры** (DXGI читает значение при создании фабрики), перезагрузка не нужна.

**б) `HKCU|HKLM\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers\<полный путь к exe>` (REG_SZ)** — список флагов совместимости через пробел с префиксом `~` (например `~ RUNASADMIN DISABLEDXMAXIMIZEDWINDOWEDMODE`). На этой машине встречаются префиксы `~` и `$`. Флаг FSO нужно добавлять и удалять, сохраняя остальные (см. §6.4).

### 3.4. Применение без перезагрузки

- Значения `Control Panel\Desktop` / анимации — только через `SystemParametersInfo(..., SPIF_UPDATEINIFILE | SPIF_SENDCHANGE)` (= `3`). Прямая запись в реестр не применяется до выхода из системы (подробнее §11).
- Прозрачность (`EnableTransparency`): после записи разослать `SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "ImmersiveColorSet", SMTO_ABORTIFHUNG, 1000, out _)`. Если не сработало — перезапуск Explorer.
- `TaskbarAnimations`, `ListviewShadow`, `EnableAeroPeek` — перезапуск Explorer.
- Всё, что читается ядром или dxgkrnl при загрузке (`HwSchMode`, `DeviceGuard`, `DisableOverlays`), требует **перезагрузки**. `OverlayTestMode` читает DWM — достаточно перезапуска DWM, но в UI проще требовать перезагрузку.
- **Важно про «перезагрузку» при включённом быстром запуске:** «Завершение работы» + включение **не** перезагружает ядро и драйверы (они восстанавливаются из hiberfil.sys, §14). В UI всегда писать «Перезагрузите ПК (Пуск → Перезагрузка)», а не «выключите и включите».

### 3.5. Реальное состояние — через API, а не реестр

| Что | Как проверить | Почему не реестр |
|---|---|---|
| HAGS | `D3DKMTQueryAdapterInfo(KMTQAITYPE_WDDM_2_7_CAPS = 70)` → биты `HwSchSupported`, `HwSchEnabled`, `HwSchEnabledByDefault` | `HwSchMode` может отсутствовать при включённом HAGS (эта машина) |
| VBS / HVCI | WMI `root\Microsoft\Windows\DeviceGuard : Win32_DeviceGuard` → `VirtualizationBasedSecurityStatus` (0/1/2), `SecurityServicesRunning` (2 = HVCI) | VBS работает при `EnableVirtualizationBasedSecurity=0`, если включены Hyper-V/VMP (эта машина) |
| MPO | Только PresentMon (режим вывода игры), см. §9 | dxdiag показывает возможности драйвера |
| VMP | `Get-WindowsOptionalFeature -Online -FeatureName VirtualMachinePlatform` (или DISM API) | — |

Проверенный код HAGS (на этой машине вернул `0xB` для RTX 4060 и `0x0` для Microsoft Basic Render Driver; типы 68/69/71/72 → `STATUS_INVALID_PARAMETER`, т.е. номер 70 верен):

```csharp
using System.Runtime.InteropServices;

static class Hags
{
    [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] struct D3DKMT_ADAPTERINFO { public uint hAdapter; public LUID AdapterLuid; public uint NumOfSources; public int bPrecisePresentRegionsPreferred; } // 20 байт
    [StructLayout(LayoutKind.Sequential)] struct D3DKMT_ENUMADAPTERS2 { public uint NumAdapters; public IntPtr pAdapters; }
    [StructLayout(LayoutKind.Sequential)] struct D3DKMT_QUERYADAPTERINFO { public uint hAdapter; public int Type; public IntPtr pPrivateDriverData; public uint PrivateDriverDataSize; } // 24 байта на x64
    [StructLayout(LayoutKind.Sequential)] struct D3DKMT_CLOSEADAPTER { public uint hAdapter; }
    [DllImport("gdi32.dll")] static extern int D3DKMTEnumAdapters2(ref D3DKMT_ENUMADAPTERS2 p);
    [DllImport("gdi32.dll")] static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO p);
    [DllImport("gdi32.dll")] static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER p);
    const int KMTQAITYPE_WDDM_2_7_CAPS = 70;

    public record Caps(long Luid, bool Supported, bool Enabled, bool EnabledByDefault);

    public static List<Caps> Query()
    {
        var result = new List<Caps>();
        var e = new D3DKMT_ENUMADAPTERS2();
        if (D3DKMTEnumAdapters2(ref e) != 0 || e.NumAdapters == 0) return result; // 1-й вызов: число адаптеров
        int sz = Marshal.SizeOf<D3DKMT_ADAPTERINFO>();
        e.pAdapters = Marshal.AllocHGlobal(sz * (int)e.NumAdapters);
        try
        {
            if (D3DKMTEnumAdapters2(ref e) != 0) return result;
            for (int i = 0; i < e.NumAdapters; i++)
            {
                var a = Marshal.PtrToStructure<D3DKMT_ADAPTERINFO>(e.pAdapters + i * sz);
                IntPtr buf = Marshal.AllocHGlobal(4);
                try
                {
                    Marshal.WriteInt32(buf, 0);
                    var q = new D3DKMT_QUERYADAPTERINFO { hAdapter = a.hAdapter, Type = KMTQAITYPE_WDDM_2_7_CAPS, pPrivateDriverData = buf, PrivateDriverDataSize = 4 };
                    if (D3DKMTQueryAdapterInfo(ref q) == 0)
                    {
                        uint v = (uint)Marshal.ReadInt32(buf);
                        result.Add(new(((long)a.AdapterLuid.High << 32) | a.AdapterLuid.Low, (v & 1) != 0, (v & 2) != 0, (v & 4) != 0));
                    }
                }
                finally { Marshal.FreeHGlobal(buf); var c = new D3DKMT_CLOSEADAPTER { hAdapter = a.hAdapter }; D3DKMTCloseAdapter(ref c); }
            }
        }
        finally { Marshal.FreeHGlobal(e.pAdapters); }
        return result; // адаптеры с Supported=false (Basic Render, iGPU без поддержки) игнорировать
    }
}
```

Проверка VBS/HVCI (NuGet `System.Management`; при `PublishTrimmed` пометить сборку как не триммируемую — WMI использует COM-интероп):

```csharp
using System.Management;

public record DeviceGuardState(uint VbsStatus, uint[] Running, uint[] Configured, uint[] Available);

static DeviceGuardState? ReadDeviceGuard()
{
    using var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\DeviceGuard", "SELECT * FROM Win32_DeviceGuard");
    foreach (ManagementObject o in s.Get())
        return new((uint)o["VirtualizationBasedSecurityStatus"],
                   (uint[])(o["SecurityServicesRunning"] ?? Array.Empty<uint>()),
                   (uint[])(o["SecurityServicesConfigured"] ?? Array.Empty<uint>()),
                   (uint[])(o["AvailableSecurityProperties"] ?? Array.Empty<uint>()));
    return null;
}
// HVCI работает: Running.Contains(2u); VBS работает: VbsStatus == 2; MBEC/GMET есть: Available.Contains(7u)
```

### 3.6. PowerShell-действия каталога

- Синтаксис — Windows PowerShell 5.1 (без `?:`, `??`). Запуск: `powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand <base64(UTF-16LE)>`. `-EncodedCommand` избавляет от проблем с кавычками и кириллицей.
- `detect` у PowerShell-записей возвращает в stdout `True`/`False`; сравнение с `equals` — по строке без учёта регистра после `Trim()`.
- Скрипты идемпотентны (повторный запуск ничего не ломает).

---

## 4. Game Mode (Игровой режим)

**Ключ:** `HKCU\Software\Microsoft\GameBar`
- `AutoGameModeEnabled` (DWORD): `1` — вкл, `0` — выкл. **Значения нет = включено** (ElevenForum/Brink: «Turn On… This is the default setting»; Xbox Support: Game Mode on by default).
- `AllowAutoGameMode` (DWORD) — устаревший ключ Win10 1703/1709. winutil пишет оба и считает «исходным» `0`, что для современных сборок неверно. В каталоге его не пишем; для детекта на Win10 можно учитывать.

**Что делает (Microsoft/Xbox Support):** пока игра на переднем плане, Windows Update не ставит драйверы и не показывает уведомления о перезагрузке. Игра получает приоритет ресурсов, что «помогает получить более стабильную частоту кадров в зависимости от игры и системы». При активном Game Mode нельзя менять режим питания.

**Доказательность:** поведение документировано → `proven`. Влияние на FPS неоднозначно. PC-Tuning отмечает, что Game Mode может мешать динамическим повышениям приоритета потоков (видно в Performance Monitor), и советует сравнить вкл/выкл замерами. Небольшие тесты на одной системе (i7-13700H + RTX 4060; MakeUseOf/iGeeks, 2025–2026): средний FPS почти не меняется, 1% low — от +9% (CS2) и +48% (Forza Horizon 5 с открытыми Edge/Discord) до −12% (007 First Light, CPU-bound). Это не контролируемые исследования.

**Особый случай — двухчиплетные AMD X3D (7950X3D / 7900X3D / 9950X3D / 9900X3D):** парковка ядер CCD без V-Cache выполняется связкой драйвера чипсета (Provisioning Packages / 3D V-Cache Performance Optimizer) и **Game Mode**. В гайдах по устранению проблем AMD-сообщества требуется, чтобы Game Mode был включён, а Xbox Game Bar обновлён. Приложение должно определять такие CPU (Win32_Processor.Name содержит `X3D` и число ядер > 8) и не предлагать выключать Game Mode / Game Bar / Game DVR.

**Каталог:** `gaming.game-mode` (`select`: 1/0; revert — удалить значение). Применяется со следующего запуска игры.

---

## 5. Xbox Game Bar и Game DVR

| Значение | Тип | Вкл | Выкл | Дефолт | Что делает |
|---|---|---|---|---|---|
| `HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled` | DWORD | 1 | 0 | 1 | Захват (клипы/скриншоты) Game Bar |
| `HKCU\System\GameConfigStore\GameDVR_Enabled` | DWORD | 1 | 0 | 1 | То же, «старый» флаг; PC-Tuning переключает оба |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\HistoricalCaptureEnabled` | DWORD | 1 | 0 | **0** | «Записывать то, что произошло» (фоновая запись) |
| `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR\AllowGameDVR` | DWORD | — | 0 | нет | Политика «Запись и трансляция игр Windows» (для всех пользователей) |
| `HKCU\Software\Microsoft\GameBar\UseNexusForGameBarEnabled` | DWORD | 1 | 0 | нет/1 (НЕ ПРОВЕРЕНО) | Открывать Game Bar кнопкой Xbox на геймпаде |

**Фоновая запись — единственная часть Game DVR с признанным влиянием на FPS.** Команда Xbox (news.xbox.com, 2016) пишет, что фоновая запись «скорее всего, повлияет на производительность игры, кроме систем с самыми мощными GPU», поэтому она выключена по умолчанию. ElevenForum (Win11): «Background recording can affect your game's performance». → `gaming.background-recording-off` = **proven**.

**Game DVR целиком** (`AppCaptureEnabled`/`GameDVR_Enabled`) — `situational`. Без фоновой записи выигрыш не измерен. Побочные эффекты:
- без Game Bar нельзя поставить «Запомнить, что это игра» → Game Mode и правила Game Bar для новой игры могут не сработать (PC-Tuning советует временно включать Game Bar для регистрации игры в `GameConfigStore\Children`);
- обратная проблема: обычное приложение, помеченное как игра, работает хуже (Microsoft Learn: Teams);
- для двухчиплетных X3D не трогать (§4).

**Политика** `AllowGameDVR=0` — `situational`: тот же эффект, только принудительно. Подтверждена на машине через `PolicyManager\default\ApplicationManagement\AllowGameDVR` (`GPBlockingRegKeyPath`).

**GameBarPresenceWriter** (`ActivationType=0`) — **avoid**. Ключ принадлежит TrustedInstaller, для записи нужно менять владельца или ACL системного ключа. Замеров нет. Альтернатива — отключить Game DVR или удалить приложение Game Bar (`Get-AppxPackage Microsoft.XboxGamingOverlay | Remove-AppxPackage` — это удаление приложения; если такой твик понадобится, его стоит отнести к теме «приложения/очистка»).

На Win11 в Параметрах нет переключателя «Включить Xbox Game Bar»: только описанные значения или удаление приложения (PC-Tuning).

---

## 6. Полноэкранный режим: FSE, FSO, flip-модели

### 6.1. Термины (как их показывает PresentMon)

| PresentMode в PresentMon | Что это | Задержка |
|---|---|---|
| `Hardware: Legacy Flip` | Настоящий эксклюзивный полноэкранный (FSE), DWM не участвует | минимальная |
| `Hardware: Independent Flip` | Flip-модель, окно на весь экран, DWM «отдаёт» дисплей приложению | как FSE |
| `Hardware Composed: Independent Flip` | То же, но через аппаратную плоскость MPO (поверх может быть другое окно) | как FSE |
| `Composed: Flip` | Flip-модель через композицию DWM | +1 кадр (обычно) |
| `Composed: Copy with GPU GDI` / `Copy with CPU GDI` | Старая blt-модель в окне | наихудшая |

Microsoft (DirectX blog «For best performance, use DXGI flip model»): в режиме Independent Flip на современных Windows можно получить задержку около 1 кадра, и flip-модель в borderless может сравняться с FSE или превзойти его.

### 6.2. Fullscreen Optimizations (FSO)

Microsoft (DirectX blog, 17.12.2019): FSO запускает игры, запрашивающие FSE, «в высокооптимизированном borderless-окне», а игра продолжает думать, что она в FSE. Когда поверх ничего нет, DWM отдаёт управление дисплеем игре. Если показывается оверлей (Game Bar), DWM снова композитит, и появляются небольшие накладные расходы. По телеметрии Microsoft производительность FSO «в среднем не хуже FSE». Отключение — галочкой «Отключить оптимизацию во весь экран» на вкладке «Совместимость» exe.

### 6.3. GameConfigStore (глобально, недокументировано)

| Значение (`HKCU\System\GameConfigStore`, DWORD) | Дефолт | «FSO off»-твик | Что известно |
|---|---|---|---|
| `GameDVR_FSEBehaviorMode` | 2 | 2 | Поведение FSE (смысл спорный, на дефолте уже 2) |
| `GameDVR_HonorUserFSEBehaviorMode` | 0 | 1 | Соблюдать пользовательский режим FSE |
| `GameDVR_FSEBehavior` | нет | 2 | PC-Tuning: вместе со следующим помогает получить Legacy Flip, если галочка на exe не помогла |
| `GameDVR_DXGIHonorFSEWindowsCompatible` | 0 | 1 | Соблюдать флаг совместимости FSE для DXGI |
| `GameDVR_EFSEFeatureFlags` | 0 | 0 | Не трогать |

Значения не документированы, смысл в источниках расходится (Microsoft Q&A, Guru3D, PowerShell Gallery `gamedvr-fso-disabler`). → `gaming.fso-global-off` = **experimental**. Для DX12/Vulkan эффекта не ожидается: «эксклюзивный» режим DX12 реализован flip-моделью (НЕ ПРОВЕРЕНО по всем играм). Рекомендуемый путь — **per-exe флаг** (ниже) и проверка через PresentMon.

### 6.4. Per-exe флаг DISABLEDXMAXIMIZEDWINDOWEDMODE (галочка «Отключить оптимизацию во весь экран»)

- Ключ: `HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers` (для текущего пользователя) или `HKLM\...` (для всех; это галочка «Изменить параметры для всех пользователей»).
- Имя значения = **полный путь к exe**, тип REG_SZ, данные = `~ DISABLEDXMAXIMIZEDWINDOWEDMODE` (плюс другие флаги через пробел). Пример с этой машины: `C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe = "~ DISABLEDXMAXIMIZEDWINDOWEDMODE"`.
- Работает на Win10 и Win11. Эффект виден в PresentMon (`Hardware: Legacy Flip` вместо `Independent Flip`) — только для DX9–DX11 игр, которые запрашивают FSE.
- Это **per-game функция**, а не глобальный твик: в каталог JSON не вошла (схема не поддерживает путь к exe как параметр). Реализовать в «профиле игры»:

```csharp
static void SetLayerFlag(RegistryKey root /* HKCU пользователя или HKLM */, string exePath, string flag, bool enable)
{
    using var k = root.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", writable: true);
    var tokens = ((k.GetValue(exePath) as string) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    string prefix = tokens.Count > 0 && (tokens[0] == "~" || tokens[0] == "$") ? tokens[0] : "~";
    var flags = tokens.Where(t => t != "~" && t != "$").ToList();
    flags.RemoveAll(f => f.Equals(flag, StringComparison.OrdinalIgnoreCase));
    if (enable) flags.Add(flag);
    if (flags.Count == 0) k.DeleteValue(exePath, throwOnMissingValue: false);
    else k.SetValue(exePath, prefix + " " + string.Join(' ', flags), RegistryValueKind.String);
}
// SetLayerFlag(InteractiveUser.OpenUserRoot(), @"C:\...\game.exe", "DISABLEDXMAXIMIZEDWINDOWEDMODE", true);
```

Рекомендация в UI: «Для игр: включать для конкретной DX9/DX11 игры, если в эксклюзивном режиме ниже задержка (проверьте PresentMon/Reflex-оверлеем). Для DX12/Vulkan бесполезно».

---

## 7. Графика Windows 11: оптимизации оконного режима, VRR, Auto HDR, GPU для приложения

Все значения — внутри REG_SZ (§3.3).

| Настройка | Где | Пара в строке | Дефолт | ОС |
|---|---|---|---|---|
| Оптимизации для игр в оконном режиме (OWG) | `HKCU\Software\Microsoft\DirectX\UserGpuPreferences\DirectXUserGlobalSettings` | `SwapEffectUpgradeEnable=1;` | 0 / нет (22H2/23H2; 24H2/25H2 — НЕ ПРОВЕРЕНО) | Win11 22H2+ (Insider 22557) |
| Переменная частота обновления (VRR для DX11) | то же | `VRROptimizeEnable=1;` | 0 | Win10 1903+ / Win11 |
| Auto HDR | то же | `AutoHDREnable=…;` (НЕ ПРОВЕРЕНО: точный формат) | выкл | Win11, HDR-монитор |
| GPU для приложения | `HKCU\Software\Microsoft\DirectX\UserGpuPreferences\<полный путь exe>` | `GpuPreference=2;` | нет значения | Win10 1803+/Win11 |

**OWG** (Microsoft Support «Optimizations for windowed games in Windows 11», DirectX blog «Updates in Graphics and Gaming»): DX10/DX11 игры в окне и borderless переводятся с blt-модели на flip-модель. Это «значительно снижает задержку» более чем в 3000 играх и включает Auto HDR и VRR. Если включён Auto HDR, OWG включаются автоматически и выключить их можно только после отключения Auto HDR. Для конкретной игры можно отключить OWG в «Параметры → Система → Дисплей → Графика → <приложение>» (точное имя per-app пары — НЕ ПРОВЕРЕНО). На ElevenForum пользователи отмечали, что на части систем OWG включены по умолчанию, а ключ `HKCU\Software\Microsoft\DirectX\GraphicsSettings\SwapEffectUpgradeCache` появляется только после первого переключения. Этот ключ — кэш, писать его не нужно. → `graphics.windowed-optimizations` = **proven**, low.

**VRR-переключатель** (DirectX blog «OS Variable Refresh Rate»): включает VRR для полноэкранных DX11 игр **без** встроенной поддержки VRR. Нужны Win10 1903+, монитор G-SYNC/FreeSync/Adaptive-Sync и драйвер WDDM 2.6+ с поддержкой функции. Без этого переключатель не показывается. Он «только дополняет» панели вендоров. → `graphics.vrr-optimize` = situational.

**GPU для приложения:** значения `GpuPreference` соответствуют `DXGI_GPU_PREFERENCE`: `0` — Windows решает, `1` — энергосбережение (iGPU), `2` — высокая производительность (dGPU). Нужен только на ПК с несколькими GPU (ноутбуки с гибридной графикой, десктоп с активным iGPU). На десктопе с одной видеокартой бесполезен. Это per-game функция (в JSON не вошла):

```csharp
static void SetGpuPreference(RegistryKey userRoot, string exePath, int pref /* 0,1,2 */)
{
    using var k = userRoot.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", writable: true);
    var cur = k.GetValue(exePath) as string ?? "";
    // в той же строке могут лежать per-app пары (Auto HDR / OWG) — сливаем так же, как в MergeDxGlobal
    var parts = cur.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Where(p => !p.StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase)).ToList();
    parts.Insert(0, $"GpuPreference={pref}");
    k.SetValue(exePath, string.Join(';', parts) + ";", RegistryValueKind.String);
}
```

Win11 24H2+ позволяет выбрать «GPU высокой производительности по умолчанию» (DirectX blog «Navigating the Redesigned Graphics Settings page»). Где это хранится — НЕ ПРОВЕРЕНО.

---

## 8. HAGS (Hardware-accelerated GPU scheduling)

- **Ключ:** `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode` (DWORD): `2` — вкл, `1` — выкл, значения нет — решает ОС и драйвер. Перезагрузка обязательна (с учётом §3.4 про быстрый запуск).
- **Требования:** Windows 10 2004 (19041)+, драйвер WDDM 2.7 с поддержкой. Microsoft (DirectX blog, 30.06.2020): на Win10 опция opt-in, по умолчанию выкл. «Пользователи не должны заметить существенных изменений», регрессий Microsoft не ожидает.
- **Дефолт на Win11:** на машине разработчика (22631, RTX 4060) `HwSchMode` отсутствует, а D3DKMT показывает `HwSchEnabled=1` при `HwSchEnabledByDefault=0`, dxdiag: `DriverSupportState:Stable Enabled:True`. Вывод: на Win11 HAGS включается по умолчанию, если драйвер это поддерживает (подтверждается сторонними гайдами). Почему при этом `EnabledByDefault=0` — НЕ ПРОВЕРЕНО: возможно, флаг означает запрос драйвера, а решение принимает ОС.
- **Где HAGS обязателен:** **DLSS Frame Generation / Multi Frame Generation** — NVIDIA Streamline FAQ: требуется RTX 40+, Win10 20H1 (19041)+ и включённый «Windows Hardware-Accelerated GPU Scheduling». **AMD FSR 3 FG:** GPUOpen (релиз FSR 3.1): на RX 7000 под Windows 11 2022 Update HAGS «should be enabled»; там же AMD просит выключать Radeon Anti-Lag в профиле игры при FSR 3 FG, чтобы не было рывков. Для AFMF (драйверной генерации кадров) HAGS как требование не указан (по вторичному источнику; НЕ ПРОВЕРЕНО).
- **Блок-лист:** на этой машине `GraphicsDrivers\BlockList\Kernel` содержит `Vendor=1002#Device=*#Version=*#Model=*#` и `Vendor=8086#…` = `DISABLE_HWSCH`. По отчёту на Guru3D, dxdiag на AMD в таком случае показывает `DriverSupportState:AlwaysOff Enabled:False Block List: DISABLE_HWSCH`. Совет «удалить BlockList» — **avoid** (`graphics.hags-blocklist-remove`). Как это сочетается с официальной поддержкой HAGS у RX 7000 (Adrenalin 23.12.1+) — НЕ ПРОВЕРЕНО (на машине нет AMD GPU). Возможно, драйвер AMD переопределяет блок-лист.
- **Логика UI:** показывать твик только если для дискретного адаптера `HwSchSupported=1`. Состояние брать из `HwSchEnabled`. Рекомендация: «ВКЛ, если используете DLSS FG / FSR 3 FG». При жалобах на VR/стриминг/статтеры — попробовать выключить.

→ `graphics.hags` (`select` 2/1, revert = удалить значение) = situational, medium.

---

## 9. MPO (Multi-Plane Overlay)

**Что это:** аппаратные плоскости наложения дисплейного контроллера. DWM может вывести окно (видео, игру в borderless, курсор) отдельной плоскостью без композиции: режим `Hardware Composed: Independent Flip`. Экономит GPU и энергию, но на части связок драйвер/монитор/мультимонитор вызывает мерцание, чёрные вспышки, «залипание» и статтеры окон браузеров/Discord/Electron.

**Значения:**

| Значение | Тип | Данные | Источник | Оценка |
|---|---|---|---|---|
| `HKLM\SOFTWARE\Microsoft\Windows\Dwm\OverlayTestMode` | DWORD | `5` = MPO выкл; удалить = вкл | NVIDIA support 5157 (mpo_disable.reg / mpo_restore.reg), PC-Tuning, winutil | situational |
| `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\DisableOverlays` | DWORD | `1`; удалить = дефолт | winutil («Fully Disabled»), Winhance (для 25H2+), отчёты Guru3D | experimental |
| `HKLM\SOFTWARE\Microsoft\Windows\Dwm\OverlayMinFPS` | DWORD | `0` | одиночные отчёты (Guru3D, Winhance #494) — «MPO остаётся, но DWM не переключает окна между режимами по FPS» | **НЕ ПРОВЕРЕНО**, в каталог не включено |

**Работает ли на свежих сборках:**
- NVIDIA-статья 5157 (с драйвера 461.09) — первоисточник для `OverlayTestMode=5`. Страница отдаёт 403 автоматическим клиентам; содержимое (мерцание и статтер у части систем после 461.09, .reg-файлы) известно по многочисленным цитатам.
- BoosterX (статический анализ компонента DWM на 10 22H2 / 11 24H2 26100.863 / 11 25H2 26100.7689 и трассы): значение читается, `5` отключает проверенные пути overlay. Сами авторы оговаривают, что замеров на реальном GPU нет.
- Форумы 2025–2026 (Guru3D «Disabling MPO in 2025», TechPowerUp): часть пользователей сообщает, что на 24H2 `OverlayTestMode` перестал действовать, другая часть — что работает. Winhance утверждает, что для 25H2+ нужен `DisableOverlays`, и пишет оба значения.
- **На этой машине** (23H2) `OverlayTestMode=5` уже стоит, а dxdiag всё равно показывает `MPO MaxPlanes: 4`. Это **ожидаемо**: dxdiag показывает возможности драйвера, а не политику DWM.

**Как проверить (обязательно описать в UI):** запустить игру в borderless/оконном режиме, затем `PresentMon` (CLI: `PresentMon.exe --process_name game.exe --timed 10 --output_file pm.csv`, колонка `PresentMode`; или GUI PresentMon 2.x). При работающем MPO можно увидеть `Hardware Composed: Independent Flip`; при отключённом его быть не должно (`Hardware: Independent Flip` или `Composed: Flip`). PC-Tuning советует отключать MPO, если игра «застряла» в `Hardware Composed: Independent Flip`, а нужен другой режим.

**Побочные эффекты:** окна и borderless-игры теряют аппаратный путь, работы у DWM становится больше. Возможна чуть большая задержка в borderless и рост энергопотребления при воспроизведении видео. Отдельные сообщения о вылете игры (Winhance #494) и о сломанном VRR (с `DisableOverlays`).

→ `graphics.mpo-disable` (situational, medium) + `graphics.mpo-disable-overlays` (experimental, medium, Win11).

---

## 10. VBS / HVCI (Memory Integrity) / VMP и античиты

### 10.1. Ключи (Microsoft Learn «Enable memory integrity»)

| Ключ / значение (HKLM) | Значения |
|---|---|
| `SYSTEM\CurrentControlSet\Control\DeviceGuard\EnableVirtualizationBasedSecurity` | 1 — вкл VBS, 0 — выкл |
| `...\DeviceGuard\RequirePlatformSecurityFeatures` | 1 — Secure Boot, 3 — Secure Boot + DMA |
| `...\DeviceGuard\Locked` | 0 — без UEFI-блокировки, 1 — с UEFI-блокировкой |
| `...\DeviceGuard\Mandatory` | 1 — не загружаться, если гипервизор/secure kernel не загрузились (опасно) |
| `...\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled` | 1 — HVCI вкл, 0 — выкл |
| `...\Scenarios\HypervisorEnforcedCodeIntegrity\Locked` | 0/1 — UEFI-блокировка HVCI |
| `...\Scenarios\HypervisorEnforcedCodeIntegrity\WasEnabledBy` | 2 — UI ведёт себя нормально; удалить → UI серый «управляется администратором» |
| Политика `SOFTWARE\Policies\Microsoft\Windows\DeviceGuard\*` | Если есть — настройка управляется GPO/MDM, твик не применять |

**Перед применением `graphics.hvci-off` / `graphics.vbs-off` приложение обязано проверить:**
1. `DeviceGuard\Locked == 1` или `HVCI\Locked == 1` → UEFI-блокировка: реестром не отключить (Microsoft: нужно отключать Secure Boot в UEFI). Показать «недоступно».
2. Есть ключ политики `HKLM\SOFTWARE\Policies\Microsoft\Windows\DeviceGuard` → «управляется организацией».
3. Включены Hyper-V (`Microsoft-Hyper-V-All`), `VirtualMachinePlatform`, `Containers-DisposableClientVM` (Sandbox), WSL2, Credential Guard → VBS не выключится (PC-Tuning). Этот случай есть на машине разработчика.
4. Установлены Riot Vanguard (службы `vgc`, `vgk`) или FACEIT AC (имя службы — НЕ ПРОВЕРЕНО, предположительно `FACEIT`) → показать жёсткое предупреждение (§10.3).
5. После перезагрузки проверить `Win32_DeviceGuard` (§3.5).

### 10.2. Влияние на производительность

- **Tom's Hardware** (запуск Win11, RTX 3090, 1080p): включение VBS/HVCI в среднем −3…−6% FPS. i7-11700K: VBS −4.9%, HVCI −5.6%; i7-10700K: −5% / −5.7%; Ryzen 7 5800X: −4% / −3.3%; Ryzen 7 3800X: −4.1% / −4.1%. В отдельных играх потери больше (PCGH, 2021: F1 2020 188 против 237 FPS — вероятно, ранние сборки).
- **Microsoft** (Learn): HVCI лучше работает на Intel Kaby Lake+ (MBEC) и AMD Zen 2+ (GMET). На более старых CPU используется эмуляция (Restricted User Mode) с бо́льшими потерями. Наличие MBEC/GMET: `Win32_DeviceGuard.AvailableSecurityProperties` содержит `7`.
- **Microsoft** (удалённая страница «Options to optimize gaming performance in Windows 11», архив 2025-08-18): «в некоторых сценариях и конфигурациях» Memory Integrity и VMP снижают производительность. Геймерам предлагалось выключать их на время игры и включать обратно после.
- Потери проявляются в CPU/памяти, а не на GPU. В GPU-bound сценариях разница минимальна. Свежих замеров на 24H2/25H2 найти не удалось (НЕ ПРОВЕРЕНО).

**Дефолт** (Microsoft Learn «Memory integrity enablement» для OEM): на **чистой** установке Win11 на совместимом железе HVCI включён. Совместимое железо: Intel 8-го поколения+ (для 21H2 — 11-го), AMD Zen 2+, ≥8 ГБ RAM, SSD ≥64 ГБ, совместимые драйверы, виртуализация в BIOS. На апгрейдах и Win10 по умолчанию выкл. Если сразу после включения случается сбой загрузки, есть страховка с автоотключением.

### 10.3. Требования античитов (проверено на 2025–2026)

| Античит / игры | TPM 2.0 | Secure Boot | VBS | HVCI (Memory Integrity) | IOMMU / DMA | Win10 |
|---|---|---|---|---|---|---|
| **Riot Vanguard** (Valorant, LoL, 2XKO) | Win11: обязательно (VAN 9001; статьи Riot «Win 11 Only») | Win11: обязательно (VAN 9003) | проверяется в рамках HVCI (VAN 9005: «Memory Integrity… through VBS») | **может потребоваться** («Depending on your system configuration, Vanguard may prompt you…»); ошибка `VAN: RESTRICTION 5` = Vanguard не смог подтвердить, что HVCI/VBS включён и работает | может потребоваться (Riot «Vanguard security requirements», 31.05.2026) | TPM/SB как обязательные указаны только для Win11; для Win10 условия ограничений — НЕ ПРОВЕРЕНО |
| **FACEIT AC** (CS2 на FACEIT) | обязательно для всех с **25.11.2025** | обязательно для всех с **25.11.2025** | обязательно для части игроков (вместе с IOMMU, поэтапно с апреля 2025, уже ~60% игроков) | «some players may be required to enable Memory Integrity» | обязательно для части игроков, расширяется волнами | с 14.10.2026 — только Win10 22H2 с ESU; **Win11 обязательна с октября 2027** |
| **EA Javelin** (Battlefield 6) | — (НЕ ПРОВЕРЕНО) | обязательно | — | — | — | — |
| **Activision Ricochet** (CoD BO7) | обязательно (по анонсу 08.2025) | обязательно | — | — | — | — |
| VAC (CS2 без FACEIT) | — | — | — | — | — | — |

Дополнительно: AMD fTPM с версией прошивки `3.*.0.*` даёт ошибки аттестации FACEIT (нужно обновить BIOS). На машине разработчика версия 3.94.2.5 — не затронута.

**Вывод для приложения:** `graphics.hvci-off` и `graphics.vbs-off` — `situational`, риск **high**. По умолчанию в рекомендованный набор не включать. При наличии Vanguard/FACEIT показывать предупреждение: «Valorant/FACEIT могут перестать запускаться (VAN: RESTRICTION 5 / требование VBS+IOMMU)».

### 10.4. Virtual Machine Platform

Компонент `VirtualMachinePlatform` нужен WSL2, Windows Subsystem for Android и Docker (WSL2 backend). Его отключение Microsoft прямо упоминала вместе с HVCI. → `graphics.vmp-off` (situational, medium; DISM, 10–60 с). Нюансы:
- если включён Hyper-V или Sandbox, гипервизор всё равно стартует (`bcdedit hypervisorlaunchtype Auto`), и VBS может продолжать работать;
- revert включает компонент обратно. Это «состояние до твика», а не дефолт Windows: по умолчанию VMP выключен.

---

## 11. Визуальные эффекты, анимации, прозрачность

**Почему не реестр:** Raymond Chen (Microsoft, The Old New Thing, 2014): `VisualFXSetting` (0 — Windows выбирает, 1 — наилучший вид, 2 — наилучшее быстродействие, 3 — особые эффекты) **только запоминает выбранную радиокнопку**. Реальное применение делает кнопка «Применить» через `SystemParametersInfo`. `UserPreferencesMask` — недокументированная битовая маска, её раскладка различается между версиями (на этой машине пользователь `9E1E0780 12000000`, `.DEFAULT` `9E3E0380 12000000`). → прямая запись маски = **avoid** (`graphics.userpreferencesmask-raw`).

**Таблица эффектов** (прочитано из `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects\*` на 22631; `DefaultValue` — это и есть значения «Windows выбирает»):

| Эффект | Default | SPI get / set | Как задаётся | В твике |
|---|---|---|---|---|
| AnimateMinMax | 1 | 0x48 / 0x49 (`SPI_SETANIMATION`, `ANIMATIONINFO{cbSize=8,iMinAnimate}`) | pvParam=&struct, uiParam=8 | выкл |
| ControlAnimations | 1 | 0x1042 / 0x1043 (`SPI_SETCLIENTAREAANIMATION`) | pvParam=(BOOL) | выкл |
| ComboBoxAnimation | 1 | 0x1004 / 0x1005 | pvParam=(BOOL) | выкл |
| ListBoxSmoothScrolling | 1 | 0x1006 / 0x1007 | pvParam=(BOOL) | выкл |
| MenuAnimation | 1 | 0x1002 / 0x1003 | pvParam=(BOOL) | выкл |
| SelectionFade | 1 | 0x1014 / 0x1015 | pvParam=(BOOL) | выкл |
| TooltipAnimation | 1 | 0x1016 / 0x1017 | pvParam=(BOOL) | выкл |
| CursorShadow | 0 | 0x101A / 0x101B | pvParam=(BOOL) | выкл |
| DropShadow | 1 | 0x1024 / 0x1025 | pvParam=(BOOL) | выкл |
| DragFullWindows | 1 | 0x26 / 0x25 | **uiParam**=(BOOL) | оставлен |
| FontSmoothing | 1 | 0x4A / 0x4B | uiParam=(BOOL) | оставлен (ClearType) |
| TaskbarAnimations | 1 | реестр `HKCU\...\Explorer\Advanced\TaskbarAnimations` | DWORD | выкл (нужен перезапуск Explorer) |
| ListviewAlphaSelect | 1 | `...\Explorer\Advanced\ListviewAlphaSelect` | DWORD | выкл |
| ListviewShadow | 1 | `...\Explorer\Advanced\ListviewShadow` | DWORD | выкл |
| DWMAeroPeekEnabled | 1 | `HKCU\Software\Microsoft\Windows\DWM\EnableAeroPeek` | DWORD | выкл |
| DWMSaveThumbnailEnabled | 0 | `...\DWM\AlwaysHibernateThumbnails` | DWORD | не трогаем |
| ThumbnailsOrIcon | 1 | (через Explorer IconsOnly) | | оставлен |

C#-эквивалент скрипта каталога:

```csharp
static class Spi
{
    [StructLayout(LayoutKind.Sequential)] struct ANIMATIONINFO { public uint cbSize; public int iMinAnimate; }
    [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint u, IntPtr p, uint f);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint u, ref ANIMATIONINFO p, uint f);
    const uint SPIF = 0x01 | 0x02; // SPIF_UPDATEINIFILE | SPIF_SENDCHANGE
    public static void SetPvBool(uint action, bool on) => SystemParametersInfo(action, 0, on ? (IntPtr)1 : IntPtr.Zero, SPIF);
    public static void SetUiBool(uint action, bool on) => SystemParametersInfo(action, on ? 1u : 0u, IntPtr.Zero, SPIF);
    public static void SetMinMax(bool on) { var ai = new ANIMATIONINFO { cbSize = 8, iMinAnimate = on ? 1 : 0 }; SystemParametersInfo(0x49, 8, ref ai, SPIF); }
}
```

`SPI_SETANIMATION` записывает `HKCU\Control Panel\Desktop\WindowMetrics\MinAnimate` (REG_SZ "0"/"1"). По нему и `TaskbarAnimations` работает detect каталога. Revert берёт `DefaultValue` из HKLM, а не константы.

**Оценка:** `graphics.visual-effects-performance` = situational. PC-Tuning: отключение анимаций улучшает ощущаемую отзывчивость при работе в Windows. На FPS в игре не влияет: в полноэкранной игре композиции почти нет.

**Прозрачность:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\EnableTransparency` (DWORD, дефолт 1). PC-Tuning приводит собственный бенчмарк «небольшой накладной расход CPU». → `graphics.transparency-off` = experimental, low.

**MenuShowDelay** (`HKCU\Control Panel\Desktop`, REG_SZ, дефолт `400`; winutil ставит `200`; на этой машине `100`) — к играм не относится, в каталог не включён.

---

## 12. Фоновые приложения

- **Win10:** глобальный переключатель «Разрешить приложениям работать в фоне» = `HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications\GlobalUserDisabled=1` (+ `HKCU\...\Search\BackgroundAppGlobalToggle=0`). winutil использует первое значение. В Win11 из интерфейса убран; работает ли он на Win11 — НЕ ПРОВЕРЕНО.
- **Win11:** только для каждого приложения (Параметры → Приложения → Установленные → (приложение) → Дополнительные параметры → «Разрешения фоновых приложений»). Это пишется в подключи `BackgroundAccessApplications\<PackageFamilyName>`. Точные имена значений (`Disabled`/`DisabledByUser`) — НЕ ПРОВЕРЕНО: на машине разработчика у подключей есть только `NCBEnabled=1`.
- **Политика (Win10 и Win11, рекомендуемый способ):** `HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy\LetAppsRunInBackground` (DWORD). Microsoft Learn Policy CSP: `0` — пользователь решает (дефолт), `1` — принудительно разрешить, `2` — принудительно запретить. Есть также пер-приложенные списки `LetAppsRunInBackground_ForceAllowTheseApps` / `_ForceDenyTheseApps` / `_UserInControlOfTheseApps` (REG_SZ/MULTI_SZ; PFN через `;`). Если приложение открыто, его нужно перезапустить. **Предупреждение Microsoft:** приложения связи перестанут получать уведомления (сообщения, почта, голосовая почта), может не работать фоновая синхронизация почты.
- Касается **только** UWP/MSIX (Store) приложений, в том числе новых Teams, нового Outlook, Phone Link и приложения Xbox (его фоновых загрузок). Обычные Win32-программы (Steam, Discord) не затрагиваются.

→ `gaming.background-apps-off` = situational, medium. Для улучшения UX можно добавить в `_ForceAllowTheseApps` выбранные пользователем PFN (НЕ ПРОВЕРЕНО на 24H2/25H2).

---

## 13. Задержка запуска программ автозагрузки

- `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize\StartupDelayInMSec` = `0` (DWORD) и `WaitForIdleState` = `0` (DWORD). По умолчанию ключа нет; revert удаляет оба значения.
- На Win11 одного `StartupDelayInMSec` часто мало: Explorer ждёт «простоя» системы, и нужен ещё `WaitForIdleState=0` (ElevenForum, Microsoft Q&A — пользовательские отчёты). Microsoft не документирует. Некоторые накопительные обновления удаляют ключ `Serialize`, поэтому стоит проверять твик после обновлений.
- Действует со следующего входа. На FPS не влияет; на HDD или слабом CPU рабочий стол после входа может ненадолго «залипнуть».

→ `cleanup.startup-delay-off` = situational, low.

---

## 14. Быстрый запуск (Fast Startup / Hiberboot)

- `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power\HiberbootEnabled` (DWORD): `1` — вкл (дефолт при включённой гибернации), `0` — выкл. Действует со следующего «Завершения работы».
- Microsoft Learn: при быстром запуске Windows закрывает приложения и выходит из пользовательских сессий, но **сохраняет образ ядра вместе с загруженными драйверами** в `hiberfil.sys` и при следующем включении восстанавливает его. Отсюда практические следствия для твикера:
  1. «Завершение работы» не применяет изменения, которые требуют перезагрузки драйверов/ядра (`HwSchMode`, `DeviceGuard`, `GraphicsDrivers\*`, драйверные ключи AMD). В UI просить «Перезагрузку».
  2. Аптайм не сбрасывается, утечки памяти драйверов накапливаются.
  3. NDIS отключает wake-возможности сетевых карт при быстром запуске (пример Microsoft), возможны проблемы с WoL и двойной загрузкой.
- `powercfg /h off` выключает гибернацию **и** быстрый запуск и удаляет `hiberfil.sys` (PC-Tuning). Это тема «электропитание»; на этой машине уже выполнено (`powercfg /a`: «Режим гибернации не включен», «Быстрый запуск: Режим гибернации недоступен»).

→ `power.fast-startup-off` = situational, low (на FPS не влияет; польза — предсказуемая чистая загрузка).

---

## 15. AMD Radeon: реестр драйвера

> На машине разработчика AMD GPU нет. Всё ниже — по источникам. Ничего из этого раздела **не проверено на реальном железе**.

### 15.1. Как найти нужный подключ класса

Ключ класса видеоадаптеров: `HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\NNNN`.

Особенности (проверены на этой машине):
- Кроме `0000`, `0001`… там есть подключ `Configuration` (без ProviderName) и, у некоторых ОС, `Properties`. **`Properties` выбрасывает `SecurityException` даже у администратора**, его нужно пропускать в try/catch.
- В подключе адаптера есть значения `ProviderName` (у NVIDIA `NVIDIA`, у AMD — `Advanced Micro Devices, Inc.`; НЕ ПРОВЕРЕНО), `DriverDesc` (`AMD Radeon RX 7800 XT` и т.п.), `MatchingDeviceId` (`pci\ven_1002&dev_XXXX`), `DriverVersion`, `InfPath`.
- **Надёжный способ** — не перебирать по имени, а идти от **присутствующего** устройства: `Win32_VideoController.PNPDeviceID` → `HKLM\SYSTEM\CurrentControlSet\Enum\<PNPDeviceID>\Driver` = `{4d36e968-e325-11ce-bfc1-08002be10318}\0000`. То же отдаёт `DEVPKEY_Device_Driver` (проверено: `PCI\VEN_10DE&DEV_2882&...\4&1FC990D7&0&0019` → `{4d36e968-...}\0000`). Номер подключа меняется после DDU и переустановки драйвера, поэтому вычислять его заново при каждом запуске.
- У систем с Ryzen-APU **и** Radeon будет **два** адаптера с `VEN_1002`. Отличать по `DriverDesc` или `HardwareInformation.qwMemorySize`, а лучше — по DXGI (`DXGI_ADAPTER_DESC1`: дискретный GPU имеет больше выделенной VRAM).

```csharp
using System.Management;
using Microsoft.Win32;

record GpuClassKey(string PnpId, string ClassSubkey, string? Provider, string? Desc, string? MatchingId);

static IEnumerable<GpuClassKey> FindAdapters(string venPrefix /* "PCI\\VEN_1002" */)
{
    using var s = new ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_VideoController");
    foreach (ManagementObject o in s.Get())
    {
        var pnp = o["PNPDeviceID"] as string;
        if (pnp is null || !pnp.StartsWith(venPrefix, StringComparison.OrdinalIgnoreCase)) continue;
        using var enumKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + pnp);
        var driver = enumKey?.GetValue("Driver") as string;              // "{4d36e968-...}\0001"
        if (driver is null || !driver.StartsWith("{4d36e968-e325-11ce-bfc1-08002be10318}\\", StringComparison.OrdinalIgnoreCase)) continue;
        string sub = @"SYSTEM\CurrentControlSet\Control\Class\" + driver;
        using var ck = Registry.LocalMachine.OpenSubKey(sub);
        if (ck is null) continue;
        yield return new(pnp, sub, ck.GetValue("ProviderName") as string, ck.GetValue("DriverDesc") as string, ck.GetValue("MatchingDeviceId") as string);
    }
}
```

### 15.2. KMD-значения (корень подключа `NNNN`)

| Значение | Тип | Дефолт (по дампам) | Что заявляют | Реальность | Вердикт |
|---|---|---|---|---|---|
| `EnableUlps` / `EnableUlps_NA` | DWORD / REG_SZ (НЕ ПРОВЕРЕНО) | 1 | «Убирает просадки, стабилизирует разгон» | ULPS усыпляет **второй** GPU (CrossFire, гибридные ноутбуки). На одиночных RDNA пользы не зафиксировано; на ноутбуках вредно (dGPU не спит). В 2025 году сомневаются, читает ли драйвер флаг на десктопе | **avoid** `amd.ulps-off` |
| `PP_ThermalAutoThrottlingEnable` | DWORD | 1 | «Без троттлинга» | Защитный механизм PowerPlay | **avoid** `amd.thermal-throttling-off` |
| `PP_SclkDeepSleepDisable`, `DisableDrmdmaPowerGating` | DWORD | — | «Меньше задержки выхода из сна» | Отключение энергосбережения ядра и DMA-блока. Замеров нет | avoid (в составе дампов; отдельно в каталоге не заведены) |
| `KMD_EnableComputePreemption` | DWORD | нет | «Меньше задержки» | Без вытеснения долгие вычислительные задачи блокируют GPU: TDR, зависания UI, HAGS опирается на preemption | **avoid** `amd.compute-preemption-off` |
| `DisableDMACopy`, `DisableBlockWrite` | DWORD | нет/1 (противоречиво) | «Против статтеров на Polaris/Vega» | Смысл и дефолт противоречивы, замеров на RDNA нет | **avoid** `amd.dma-copy-off` |
| `StutterMode` | DWORD | 2 (Vega) | «Убирает статтеры» | Это **memory stutter mode** (самообновление VRAM при работе дисплейного контроллера из буфера) — энергосбережение. Linux amdgpu: `PP_STUTTER_MODE` (PowerPlay feature), `DC_DISABLE_STUTTER = 0x2 "disable memory stutter mode"` | **avoid** `amd.stutter-mode-off` |
| `KMD_FRTEnabled`, `KMD_MaxFrameRateRequested` | DWORD | 0 / — | Лимитер FPS | Старый FRTC, в интерфейсе заменён Radeon Chill | **avoid** `amd.frtc-registry` |
| `KMD_ForceWDDMv1.3`, `KMD_PrimitiveShaderSupport`, `KMD_APlusISharedMiniSegment*` | DWORD | — | разное | Отладочные/устаревшие | avoid (не заводились отдельно) |

### 15.3. Подключ `UMD` — формат и значения

**Где:** `...\Class\{4d36e968-...}\NNNN\UMD` (по одному дампу часть значений дублируется в `HKLM\SYSTEM\CurrentControlSet\Services\amdkmdag\UMD`).

**Формат:** «активные» значения — **REG_BINARY с текстом в UTF-16LE**: цифра `"1"` = байты `31 00`. Встречаются два варианта длины: **без** терминатора (`31 00`) и **с** терминатором (`31 00 00 00`). Значения `*_DEF` (например `Main3D_DEF`, `TFQ_DEF`, `VSyncControl_DEF`, `Tessellation_DEF`) — обычные **REG_SZ** (`"1"`): это эталонные значения, которые Adrenalin использует при «Сбросе». Встречаются и REG_SZ-варианты активных значений (`"TFQ"="2"`). Правильная запись: **повторить тип и длину существующего значения**.

```csharp
using System.Text;

static string? ReadUmd(RegistryKey umd, string name) => umd.GetValue(name) switch
{
    byte[] b => Encoding.Unicode.GetString(b).TrimEnd('\0'),
    string s => s,
    _ => null
};

/// Пишет "строку" в формате, совпадающем с текущим значением (BINARY UTF-16LE с/без \0, либо REG_SZ).
static void WriteUmd(RegistryKey umd, string name, string text)
{
    var old = umd.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    if (old is string) { umd.SetValue(name, text, RegistryValueKind.String); return; }
    bool withTerminator = old is byte[] ob && ob.Length >= 4 && ob.Length % 2 == 0 && ob[^1] == 0 && ob[^2] == 0;
    byte[] data = Encoding.Unicode.GetBytes(withTerminator ? text + "\0" : text);
    umd.SetValue(name, data, RegistryValueKind.Binary);
}
// ВНИМАНИЕ: в приложение не включать — см. вердикт ниже. Код приведён для полноты и для чтения/диагностики.
```

**Значения (по дампам 2016–2019; AMD не документирует):**

| Значение | Настройка Adrenalin | Значения по дампам | Согласованность |
|---|---|---|---|
| `ShaderCache` | Кэш шейдеров | `30 00` = выкл, `31 00` = AMD Optimized (дефолт), `32 00` = всегда вкл / кэшировать всё | 2 источника согласуются; на 24.x/25.x НЕ ПРОВЕРЕНО |
| `Tessellation_OPTION` | Режим тесселяции | `0/1/2` ≈ AMD Optimized / Use application settings / Override (порядок НЕ ПРОВЕРЕН) | противоречиво (в дампах `30 00` и `32 00`) |
| `Tessellation` | Максимальный уровень (при Override) | `"1"`, `"2"`… (соответствие уровням 2x…64x НЕ ПРОВЕРЕНО) | — |
| `TFQ` / `TFQ_DEF` | Качество фильтрации текстур (Performance/Standard/High) | `TFQ_DEF="1"`; `"2"` в одном дампе помечено как High, в другом твике ставится как «производительность» | противоречиво |
| `VSyncControl` / `VSyncControl_DEF` | Ожидание вертикального обновления | `30 00` ставят как «всегда выкл»; `_DEF="4"`; «3 = dynamic, 4 = lock to monitor» | противоречиво |
| `TurboSync` | Enhanced Sync | `30 00` / `31 00` | 2 источника (дамп + changelog RadeonMod «UMD -> TurboSync») |
| `Main3D` / `Main3D_DEF` | Очередь кадров (pre-rendered frames) DX9/DX11 | `"0"…"3"` | наследие; на DX12/Vulkan не применимо |
| `FlipQueueSize` | То же для старых API | `31 00` | наследие |

**Вердикт:** `amd.umd-registry-settings` = **avoid**. Причины: недокументированный и меняющийся формат; Adrenalin перезаписывает UMD при любом изменении в интерфейсе и хранит профили игр отдельно (по форумам — `%LOCALAPPDATA%\AMD\CN\gmdb.blb` / `gmdbprof.blb`; НЕ ПРОВЕРЕНО); сброс при переустановке драйвера; риск записать в подключ не того адаптера; часть настроек (TFQ = Performance, ограничение тесселяции) ухудшает картинку. AMD сама (DH-012) рекомендует ограничивать тесселяцию **в интерфейсе** для игр с тяжёлой тесселяцией. Пользователя нужно отправлять в AMD Software (по известным данным, `C:\Program Files\AMD\CNext\CNext\RadeonSoftware.exe`; путь НЕ ПРОВЕРЕН).

### 15.4. Anti-Lag / Anti-Lag 2 / Anti-Lag+ / Chill / Enhanced Sync

| Функция | Что делает | Требования / ограничения | Где хранится |
|---|---|---|---|
| **Radeon Anti-Lag** (драйверный) | Управляет тем, насколько CPU уходит вперёд GPU (меньше очередь кадров) → меньше задержка ввода в GPU-bound сценариях | DX9/DX11/DX12, Win10/11, RX 400+ (текущая страница AMD; старые FAQ противоречат). Нельзя одновременно с Radeon Chill (AMD DH-033). При FSR 3 FG AMD просит выключать | UMD/профиль Adrenalin — имя значения НЕ ПРОВЕРЕНО |
| **Anti-Lag+** (RX 7000, 2023) | Выравнивание кадров внутри игры через **перехват (detour) функций DLL движка** | **VAC-баны в CS2 (октябрь 2023)**, функция отключена AMD | — → **avoid** `amd.anti-lag-plus` |
| **Anti-Lag 2** | SDK, который встраивает разработчик игры: драйверный pacing CPU + выравнивание в игре | RX 5000+/Ryzen 6000+ APU; DX11 с 24.6.1, DX12 с 24.7.1, Vulkan с 24.9.1. Первая игра — CS2; безопасен для античитов (встроен разработчиком) | Включается в настройках игры |
| **Radeon Chill** | Динамический лимитер FPS (min/max) в зависимости от ввода; при min=max — обычный лимитер. Снижает энергопотребление и нагрев | Горячая клавиша F11; не совместим с Anti-Lag | Профиль Adrenalin — НЕ ПРОВЕРЕНО |
| **Enhanced Sync** | Без разрывов, когда FPS **выше** частоты монитора, с меньшей задержкой, чем V-Sync; ниже частоты — как V-Sync off | По умолчанию выкл; задаётся в «Ожидание вертикального обновления». AMD: может вызывать мерцание в мультимедиа-приложениях | `UMD\TurboSync` (по дампам) |
| **HYPR-RX** | Профиль, который включает Anti-Lag(2)/Boost/RSR и т. п. одной кнопкой | — | Adrenalin |

Для приложения: AMD-функции не трогать реестром. Можно показывать справку («Для соревновательных игр: включите Anti-Lag 2 в настройках игры, если есть, иначе Radeon Anti-Lag в Adrenalin; не используйте Anti-Lag+») и кнопку «Открыть AMD Software».

### 15.5. Службы и компоненты AMD

- **AMD External Events Utility** (`atiesrxx.exe`/`atieclxx.exe`): PC-Tuning прямо пишет «(required for VRR)». По отчётам AMD Community, без неё FreeSync в borderless/оконных играх не работает. → **avoid** `amd.external-events-service-off`, чтобы исследователь служб случайно не отключил её. Точное имя службы — НЕ ПРОВЕРЕНО (в отчётах фигурирует как «AMD External Events Utility»).
- **GSDragoon/RadeonSoftwareSlimmer** — открытая утилита: выборочная установка компонентов драйвера, управление службами, задачами планировщика и автозапуском Radeon Software. Реестровых «твиков производительности» не делает. Это хороший ориентир для «облегчения» драйвера (тема служб/задач), а не для реестра.
- **PC-Tuning configure-amd.md** (автор больше не владеет AMD GPU): ставить только `Packages\Drivers\Display\XXXX_INF` через Диспетчер устройств и панель из `ccc-next64.msi`, лишние автозапуски отключать через Autoruns, «не отключать основной KMD».

### 15.6. Итог по AMD

На RDNA2/3/4 реестровых твиков с доказанной пользой не найдено. Всё ценное есть в AMD Software: Adrenalin Edition — Anti-Lag(2), Chill/лимитер, Enhanced Sync/FreeSync, Shader Cache (+ «Сбросить кэш шейдеров»), Tessellation Mode. Каталог содержит для AMD только **avoid**-записи (9 шт.), чтобы защитить приложение от их добавления.

---

## 16. Сводка avoid-записей (почему НЕ включаем)

| ID | Почему |
|---|---|
| `gaming.gamebar-presencewriter-off` | Ключ TrustedInstaller → нужна смена ACL; пользы не измерено |
| `graphics.tdr-off` | Microsoft: ключи TDR только для отладки; TdrLevel=0 → зависание ПК вместо восстановления |
| `graphics.hags-blocklist-remove` | Обход системного блок-листа Microsoft для AMD/Intel |
| `graphics.userpreferencesmask-raw` | Недокументированная маска; VisualFXSetting сам ничего не делает; правильно — SPI |
| `graphics.dwm-disable` | С Windows 8 DWM обязателен |
| `amd.ulps-off` | Эпоха CrossFire; вредно на ноутбуках |
| `amd.thermal-throttling-off` | Опасно для железа |
| `amd.compute-preemption-off` | TDR, зависания, ломает HAGS |
| `amd.dma-copy-off` | Противоречивые данные, нет замеров |
| `amd.stutter-mode-off` | Это энергосбережение памяти, а не «статтеры» |
| `amd.umd-registry-settings` | Недокументированный формат, Adrenalin перезаписывает |
| `amd.frtc-registry` | Устаревший FRTC, заменён Chill |
| `amd.external-events-service-off` | Ломает FreeSync в borderless |
| `amd.anti-lag-plus` | VAC-баны (CS2, 2023) |

Не включены в каталог, но стоит знать: `OverlayMinFPS=0` (НЕ ПРОВЕРЕНО), `GameDVR_EFSEFeatureFlags` (не трогать), `MenuShowDelay` (не игровое), `GlobalUserDisabled` для Win11 (НЕ ПРОВЕРЕНО), `UseNexusForGameBarEnabled` (удобство, не производительность).

---

## 17. НЕ ПРОВЕРЕНО / открытые вопросы

1. **Дефолт «Оптимизаций для игр в оконном режиме» на 24H2/25H2** (на 22H2/23H2 — выкл; на части систем пользователи видели вкл). Точное имя per-app пары для отключения OWG и формат `AutoHDREnable`.
2. **Работа `OverlayTestMode=5` на 24H2/25H2.** Отчёты противоречат друг другу; действует ли `DisableOverlays` и что именно он отключает. Нужна проверка PresentMon на реальной 24H2/25H2 машине.
3. **HAGS на AMD:** как системный `BlockList\Kernel … DISABLE_HWSCH` для `Vendor=1002` сочетается с официальной поддержкой HAGS у RX 7000 (Adrenalin 23.12.1+), и есть ли такой блок-лист на 24H2/25H2. Почему на RTX 4060 `HwSchEnabledByDefault=0` при включённом HAGS.
4. **Vanguard на Windows 10:** обязательны ли TPM/Secure Boot/HVCI (Riot указывает TPM/SB как «Win 11 Only»; условия `VAN: RESTRICTION` для Win10 не описаны).
5. **Имена служб античитов** для детекта (FACEIT: `FACEIT`? `FACEITService`?). Vanguard `vgc`/`vgk` — известно.
6. **Свежие замеры VBS/HVCI на 24H2/25H2** не найдены (данные Tom's Hardware — 2021 год).
7. **AMD:** все значения §15 по дампам; `ProviderName` AMD-драйвера; имена значений Anti-Lag/Chill; путь к RadeonSoftware.exe; место хранения профилей игр в текущем Adrenalin.
8. **Background apps на Win11:** точные имена per-app значений и действует ли `GlobalUserDisabled`.
9. **`UseNexusForGameBarEnabled`** — точный дефолт.
10. **GameConfigStore FSE-значения** — точная семантика (Microsoft не документирует).

---

## 18. Источники

**Microsoft (первоисточники)**
- Use Game Mode while gaming on your Windows device — https://support.xbox.com/en-US/help/games-apps/game-setup-and-play/use-game-mode-gaming-on-pc
- Tips On Windows 10 And Game DVR Optimization (Xbox Wire, 2016) — https://news.xbox.com/en-us/2016/10/14/tips-on-windows-10-and-game-dvr-optimization
- Teams performance is affected if it's remembered as a game by Xbox Game Bar — https://learn.microsoft.com/en-us/microsoftteams/troubleshoot/teams-on-windows/performance-affected-when-teams-remembered-as-game
- Demystifying Fullscreen Optimizations (DirectX blog, 2019) — https://devblogs.microsoft.com/directx/demystifying-full-screen-optimizations/
- For best performance, use DXGI flip model — https://devblogs.microsoft.com/directx/dxgi-flip-model/
- Optimizations for windowed games in Windows 11 — https://support.microsoft.com/en-us/topic/3f006843-2c7e-4ed0-9a5e-f9389e535952
- Updates in Graphics and Gaming (DirectX blog) — https://devblogs.microsoft.com/directx/updates-in-graphics-and-gaming/
- Navigating the Redesigned Graphics Settings page (DirectX blog, 2023) — https://devblogs.microsoft.com/directx/navigating-the-redesigned-graphics-settings-page/
- OS Variable Refresh Rate (DirectX blog) — https://devblogs.microsoft.com/directx/os-variable-refresh-rate/
- Hardware Accelerated GPU Scheduling (DirectX blog, 2020) — https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/
- KMTQUERYADAPTERINFOTYPE (d3dkmthk.h) — https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/ne-d3dkmthk-_kmtqueryadapterinfotype
- Enable memory integrity — https://learn.microsoft.com/en-us/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity
- Memory integrity enablement (OEM) — https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/oem-hvci-enablement
- Options to optimize gaming performance in Windows 11 (удалена; архив) — https://web.archive.org/web/20250818091118/https://support.microsoft.com/en-us/windows/options-to-optimize-gaming-performance-in-windows-11-a255f612-2949-4373-a566-ff6f3f474613
- Policy CSP – Privacy / LetAppsRunInBackground — https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy#letappsruninbackground
- Distinguishing Fast Startup from Wake-from-Hibernation — https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/distinguishing-fast-startup-from-wake-from-hibernation
- TDR registry keys — https://learn.microsoft.com/en-us/windows-hardware/drivers/display/tdr-registry-keys
- SystemParametersInfoW — https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow
- The Old New Thing (Raymond Chen) о VisualFXSetting — https://devblogs.microsoft.com/oldnewthing/?p=1993
- Windows Gaming: Full screen experience (Xbox mode, KB5070297) — https://support.microsoft.com/en-us/servicing/os/windows/docs/2025/10/windows-gaming-full-screen-experience

**Вендоры GPU**
- NVIDIA Streamline SDK (FAQ: требования DLSS Frame Generation, HAGS) — https://developer.nvidia.com/rtx/streamline/get-started
- NVIDIA support 5157 (MPO / OverlayTestMode) — https://nvidia.custhelp.com/app/answers/detail/a_id/5157
- AMD FSR 3.1 release (HAGS для FSR 3 FG, Anti-Lag) — https://gpuopen.com/learn/amd_fsr_3_1_release/
- AMD Radeon Anti-Lag — https://www.amd.com/en/products/software/adrenalin/radeon-software-anti-lag.html
- AMD Anti-Lag 2 (GPUOpen) — https://gpuopen.com/anti-lag-2/
- AMD DH-033 (Radeon Chill и Anti-Lag) — https://www.amd.com/en/resources/support-articles/faqs/DH-033.html
- AMD DH-012 (настройки Radeon) — https://www.amd.com/en/resources/support-articles/faqs/DH-012.html
- AMD Enhanced Sync — https://www.amd.com/en/products/software/adrenalin/software-enhancedsync.html
- Linux amdgpu `amd_shared.h` (PP_STUTTER_MODE, DC_DISABLE_STUTTER) — https://github.com/torvalds/linux/blob/master/drivers/gpu/drm/amd/include/amd_shared.h

**Античиты**
- Riot: Vanguard security requirements — https://support.riotgames.com/en-us/riot/performance/vanguard-security-requirements
- Riot: Error VAN: RESTRICTION 5 — https://support.riotgames.com/en-us/riot/penalties/error-van-restriction-5
- Riot: Error VAN 9005 — https://support.riotgames.com/en-us/riot/client/error-van-9005
- Riot: VAN9001 / TPM 2.0 (Win 11 Only) — https://support-leagueoflegends.riotgames.com/hc/en-us/articles/24170417829651-How-to-Fix-Error-VAN9001-by-Enabling-TPM-2-0-Win-11-Only
- Riot: Error VAN 9003 — https://support.riotgames.com/en-us/riot/client/error-van-9003/
- FACEIT: Windows Security Requirements FAQ — https://support.faceit.com/hc/en-us/articles/23117181142556
- FACEIT: Known issues with Anti-Cheat Requirements — https://support.faceit.com/hc/en-us/articles/22851956652956-Known-issues-with-Anti-Cheat-Requirements
- FACEIT: The history of the FACEIT Anti-cheat — https://support.faceit.com/hc/en-us/articles/9394645990044-The-history-of-the-FACEIT-Anti-cheat
- EA: Battlefield 6 Secure Boot — https://www.ea.com/games/battlefield/battlefield-6/news/secure-boot-information
- BF6 / BO7 требования (TheSixthAxis, 08.2025) — https://www.thesixthaxis.com/2025/08/07/battlefield-6-and-black-ops-7-both-need-secure-boot-and-tpm-2-0-for-pc-anti-cheat/
- Anti-Lag+ и VAC (Guru3D, 10.2023) — https://www.guru3d.com/story/amd-in-counterstrike-2-can-result-in-vac-ban

**Сообщество / исследования / утилиты**
- valleyofdoom/PC-Tuning README — https://github.com/valleyofdoom/PC-Tuning/blob/main/README.md
- PC-Tuning registry-opts.md — https://github.com/valleyofdoom/PC-Tuning/blob/main/docs/registry-opts.md
- PC-Tuning configure-amd.md — https://github.com/valleyofdoom/PC-Tuning/blob/main/docs/configure-amd.md
- ChrisTitusTech/winutil tweaks.json — https://github.com/ChrisTitusTech/winutil/blob/main/config/tweaks.json
- GSDragoon/RadeonSoftwareSlimmer — https://github.com/GSDragoon/RadeonSoftwareSlimmer
- Tom's Hardware: VBS/HVCI benchmarks — https://www.tomshardware.com/news/windows-11-gaming-benchmarks-performance-vbs-hvci-security
- ElevenForum: Game Mode — https://www.elevenforum.com/t/turn-on-or-off-game-mode-in-windows-11.1447/
- ElevenForum: Record what happened — https://www.elevenforum.com/t/enable-or-disable-record-what-happened-for-gaming-captures-in-windows-11.17900/
- ElevenForum: Optimizations for windowed games — https://www.elevenforum.com/t/turn-on-or-off-optimizations-for-windowed-games-in-windows-11.4943/
- ElevenForum: Variable refresh rate — https://www.elevenforum.com/t/enable-or-disable-variable-refresh-rate-for-games-in-windows-11.12052/
- ElevenForum: Startup apps delay — https://www.elevenforum.com/t/enable-or-disable-delay-of-running-startup-apps-in-windows-11.9144/
- PowerShell Gallery: gamedvr-fso-disabler — https://www.powershellgallery.com/packages/gamedvr-fso-disabler/1.0.2/Content/gamedvr-fso-disabler.ps1
- BoosterX: MPO research — https://wiki.boosterx.org/en/windows-research/graphics/multiplane-overlay/
- Winhance issue #494 (MPO, DisableOverlays, OverlayMinFPS) — https://github.com/memstechtips/Winhance/issues/494
- Guru3D: Disabling MPO in 2025 — https://forums.guru3d.com/threads/disabling-mpo-multiplane-overlay-in-2025.455222/
- Guru3D: HAGS BlockList DISABLE_HWSCH — https://forums.guru3d.com/goto/post?id=6139134
- Guru3D: RadeonMod — https://forums.guru3d.com/threads/radeonmod-tweak-utility.403389/
- AMD registry dump 19.7.5 (pastebin) — https://pastebin.com/TRRZxBp1
- AMD debloat registry (rentry) — https://rentry.co/AMDdebloatregistry
- shoober420/windows11-scripts AMD tweaks (deepwiki) — https://deepwiki.com/shoober420/windows11-scripts/3.3-amd-gpu-tweaks
- AMD Community: External Events Utility — https://community.amd.com/t5/pc-drivers-software/amd-external-events-utility-service/m-p/344821
- HW Busters: 7950X3D core parking — https://hwbusters.com/cpu/amd-ryzen-9-7950x3d-core-parking-problem-solution/
- Game Mode tests (MakeUseOf mirror / iGeeks) — https://www.igeeksblog.com/turning-off-windows-game-mode-improved-gaming-performance/
