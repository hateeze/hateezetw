# Службы, запланированные задачи, приватность и очистка — исследование для «hateeze»

- Ключ темы: `services-tasks-privacy-cleanup`
- Дата исследования: 2026-10-26
- Машинно-читаемый каталог: `services-tasks-privacy-cleanup.tweaks.json` (в той же папке)
- Целевые ОС: Windows 10 22H2 (19045), Windows 11 23H2 (22631) / 24H2 (26100) / 25H2
- Машина, на которой проводились проверки: Windows 11 Pro for Workstations 23H2, build 22631.7652, русская локаль, Ryzen 7 5700X, RTX 4060, 16 ГБ ОЗУ, SATA SSD 240 ГБ + NVMe 1 ТБ. Все проверки — **только чтение** (reg query, Get-*, sc qc, schtasks /query, COM Task Scheduler только `GetFolder/GetTask`, dism /AnalyzeComponentStore). В системе ничего не менялось.

---

## 0. Главное (TL;DR для разработчика)

1. **Честно про FPS.** Ни одна служба, задача или настройка приватности из этой темы не имеет публичного воспроизводимого замера (PresentMon / FrameView / LatencyMon), показывающего прирост FPS или снижение задержки ввода на современном ПК (8+ потоков, SSD, 16+ ГБ ОЗУ). Эффект этих твиков — меньше фоновых всплесков CPU/диска (CompatTelRunner, индексатор, SysMain), меньше сетевой телеметрии, меньше рекламы/уведомлений. В UI этот раздел нужно подавать как «Приватность и чистота системы», а не «+FPS». Оценка `proven` в этом каталоге означает: **эффект на приватность/поведение Windows документирован Microsoft и твик безопасен**, а не «даёт FPS».
2. **Машина разработчика НЕ стоковая** (см. §1): службы DiagTrack, dmwappushservice, Fax, diagsvc, SgrmBroker, WMPNetworkSvc удалены; Defender, SmartScreen, UAC, WpnService выключены; SysMain/WSearch/Xbox отключены; часть задач телеметрии удалена. Поэтому **дефолты взяты из снимков чистых установок** (winhelponline: Windows 10 Pro 22H2 и Windows 11 Pro 23H2, clean install; таблица Shawn Brink на ElevenForum; документация Microsoft), а с машиной сверены только те службы, которые на ней явно не трогали — они совпали со снимками.
3. **Защищённые службы.** У части служб в реестре стоит `LaunchProtected` (WaaSMedicSvc=2, DoSvc=2, AppXSvc=2, sppsvc=1, SecurityHealthService=2, wscsvc=2, Sense=2, WinDefend=3, WdNisSvc=3, MDCoreSvc=3 — прочитано на этой машине). Microsoft прямо пишет, что не-защищённый процесс (даже администратор) не может менять конфигурацию защищённой службы. `ChangeServiceConfig` вернёт `ERROR_ACCESS_DENIED (5)`. Такие службы в каталог не включены (только `avoid`).
4. **Windows сама меняет тип запуска некоторых служб.** В журнале System (Service Control Manager, Event ID 7040) на этой машине BITS переключается «Вручную ⇄ Автоматически» по нескольку раз в час, TrustedInstaller — во время обслуживания, `vgc` (Riot Vanguard) — самим Vanguard. Такие службы нельзя включать в твики и нельзя «чинить» детектом.
5. **Политики CloudContent на Home/Pro не работают.** По Policy CSP `DisableWindowsConsumerFeatures`, `DisableSoftLanding`, `DisableWindowsSpotlightFeatures`, `DisableCloudOptimizedContent` применимы только к Enterprise/Education. Для Home/Pro рекламу/подсказки отключаем пользовательскими значениями `ContentDeliveryManager` (то же, что пишут переключатели в «Параметрах»).
6. **`AllowTelemetry=0` на Home/Pro = 1.** Документировано: значение 0 поддерживается только на Enterprise/Education/Server, на других редакциях «равносильно значению 1».
7. **Задачи могут отсутствовать.** Состояние «задача не найдена» (HRESULT `0x80070002`) — это «неприменимо», а не ошибка. На этой машине найдена аномалия: XML-файлы задач `Application Experience` лежат в `C:\Windows\System32\Tasks\...`, но удалены из `TaskCache` → Планировщик их не видит. Восстанавливать такие задачи не нужно.
8. **«Microsoft Compatibility Appraiser» на актуальной 23H2 больше не запускает CompatTelRunner напрямую** — её действие `sc.exe start InventorySvc`; а задача `MareBackup` запускает `compattelrunner.exe -m:appraiser.dll -f:DoScheduledTelemetryRun`. Для отключения телеметрии совместимости нужно отключать обе задачи.
9. **TaskbarDa (кнопка «Виджеты») защищена драйвером UCPD** (на 24H2 и обновлённой 23H2 запись даёт «Отказано в доступе» даже от SYSTEM). Не пишем `TaskbarDa`; используем документированную политику `HKLM\SOFTWARE\Policies\Microsoft\Dsh\AllowNewsAndInterests=0`. Отключать UCPD — `avoid`.
10. **DPS хостит SRUM.** Проверено: в процессе svchost службы DPS загружены `srumsvc.dll` и `srumapi.dll`. Отключение DPS = нет статистики «Использование данных», «Журнал приложений» в Диспетчере задач, расхода батареи.
11. **cleanmgr содержит обработчик `DownloadsFolder`** (папка «Загрузки» пользователя). При автоматизации `cleanmgr /sagerun` его **никогда** нельзя включать.
12. **Где реальное место на этой машине** (замер): `%TEMP%` — 15,9 ГБ (37 669 файлов); `%LOCALAPPDATA%\NVIDIA\DXCache` — 3,0 ГБ (не чистить без нужды); `C:\Windows\LiveKernelReports` — 1,55 ГБ; `C:\Windows\MEMORY.DMP` — 1,41 ГБ; компонентное хранилище WinSxS — «резервные копии и отключённые компоненты» 7,23 ГБ, «очистка рекомендуется: Yes»; Prefetch — всего 12,9 МБ (чистить бессмысленно).

---

## 1. Состояние машины разработчика (почему её нельзя брать как эталон)

| Наблюдение (только чтение) | Значение на машине | Значение на чистой Win11 Pro 23H2 | Вывод |
|---|---|---|---|
| `HKLM\SYSTEM\CurrentControlSet\Services\DiagTrack` | ключа нет | Start=2 (Auto) | служба **удалена** (`sc delete` или сборка). При этом `diagtrack.dll` на диске есть |
| `dmwappushservice`, `Fax`, `diagsvc`, `WMPNetworkSvc`, `SgrmBroker` | ключей нет | есть (Fax на Win11 — только если установлен FoD `Print.Fax.Scan`; здесь `NotPresent`) | удалены |
| SysMain / WSearch | Start=4 | 2 / 2+Delayed | изменено (Prefetch не обновляется с 02.09.2026 19:14 — последний .pf `SC.EXE-…pf`, т.е. отключено через sc.exe) |
| XblAuthManager / XblGameSave / XboxNetApiSvc / XboxGipSvc | Start=4 | 3 | изменено |
| WinDefend / SecurityHealthService / wscsvc / Sense / WdNisSvc / MDCoreSvc / webthreatdefsvc | Start=4 | Auto / Manual | Defender выключен |
| WpnService | Start=4 | Auto | выключены push-уведомления |
| CDPUserSvc (шаблон) | Disabled | Automatic | изменено |
| StiSvc | Auto | Manual (Triggered) | изменено (вероятно, драйвером сканера/камеры) |
| `EnableLUA` | 0 | 1 | UAC отключён |
| `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection\AllowTelemetry` | 0 | нет значения | политика задана |
| `HKCU\...\ContentDeliveryManager` | почти всё = 0 | значения = 1 / нет | изменено |
| Задачи CEIP / Maps / WinSAT / AnalyzeSystem / QueueReporting / Autochk\Proxy / DiskDiagnosticDataCollector | «Not Found» (папки пустые) | есть | удалены (файл и регистрация) |
| Задачи `Application Experience\*` | файлы на диске есть, в `TaskCache\Tree` нет | зарегистрированы | удалены только из TaskCache |
| `Get-MMAgent` | все `False` (MemoryCompression, ApplicationLaunchPrefetching, …) | `True` | следствие отключения SysMain |

**Совпали со снимками чистой установки** (не трогались): MapsBroker (2 + Delayed=1), PcaSvc (2 + Delayed=1 + TriggerInfo), TrkWks (2), Spooler (2), DPS (2), WerSvc (3 + TriggerInfo), wisvc (3 + Trigger), lfsvc (3 + Trigger), PhoneSvc (3 + Trigger), RetailDemo (3), WpcMonSvc (3), SEMgrSvc (3 + Trigger), WdiServiceHost/WdiSystemHost (3), RemoteRegistry (4), WaaSMedicSvc (3), TextInputManagementService (2 + Trigger), InventorySvc (3). Сервисный DACL WaaSMedicSvc на машине совпадает со снимком чистой 23H2 (`D:(A;;CCLCSWRPLORC;;;AU)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;SY)`), т.е. защита WaaSMedicSvc реализована через `LaunchProtected`, а не через DACL.

Дополнительно: установлены античиты — `vgk` (драйвер, Start=1, `C:\Program Files\Riot Vanguard\vgk.sys`), `vgc` (Start=2), `FACEIT` (драйвер, Start=1), `FACEITService` (Start=3), `BEService` (Start=3). EasyAntiCheat не установлен. Это реальный пример окружения игрока, которое приложение **не должно трогать**.

---

## 2. Службы Windows

### 2.1. Как хранится тип запуска (то, что нужно знать для кода)

Ключ: `HKLM\SYSTEM\CurrentControlSet\Services\<Имя>`

| Значение | Тип | Смысл |
|---|---|---|
| `Start` | DWORD | 0 Boot, 1 System (только драйверы), **2 Automatic**, **3 Manual (Demand)**, **4 Disabled** |
| `DelayedAutostart` | DWORD | 1 = «Автоматически (отложенный запуск)». Имеет смысл **только при Start=2**. При Start=3/4 может оставаться 1 — это нормально, игнорировать |
| подключ `TriggerInfo` | key | наличие = «(запуск по триггеру)». `ChangeServiceConfig` триггеры **не удаляет**; при Start=4 триггеры игнорируются; при Start=3 служба продолжает стартовать по триггеру |
| `Type` | DWORD | 0x10 own process, 0x20 share process, **0x50/0x60 — шаблон пер-пользовательской службы** (флаг `SERVICE_USER_SERVICE` 0x40), 0xD0/0xE0 — экземпляр (`_xxxxx`, флаг 0x80) |
| `LaunchProtected` | DWORD | 0 нет, 1 `WINDOWS`, 2 `WINDOWS_LIGHT`, 3 `ANTIMALWARE_LIGHT`. ≠0 → менять конфигурацию через SCM нельзя |

Соответствие строк UI ↔ JSON `startType`:

| Отображение в services.msc (рус.) | JSON `startType` | Start | DelayedAutostart |
|---|---|---|---|
| Автоматически | `Automatic` | 2 | 0 |
| Автоматически (отложенный запуск) | `AutomaticDelayed` | 2 | 1 |
| Вручную / Вручную (запуск по триггеру) | `Manual` | 3 | — |
| Отключена | `Disabled` | 4 | — |

SCM кэширует конфигурацию в памяти: прямая запись `Start` в реестр **вступает в силу только после перезагрузки**, тогда как `ChangeServiceConfig` — сразу. Изменения типа запуска пишутся в журнал System, источник «Service Control Manager», Event ID **7040** — удобно для диагностики и для показа истории в приложении.

### 2.2. Каталог служб-кандидатов

Дефолты: «Win10 22H2» и «Win11 23H2» — снимки чистой установки Pro (winhelponline, апрель/июнь 2024), подтверждены таблицей Brink (Win11) и неизменёнными службами этой машины. Win11 24H2/25H2 — **НЕ ПРОВЕРЕНО** на отдельной машине; по имеющимся данным для перечисленных служб дефолты не менялись (исключения см. в примечаниях).

| Служба (Display name RU) | Win10 22H2 | Win11 23H2 | Эта машина | Что делаем | Оценка | Что сломается |
|---|---|---|---|---|---|---|
| **DiagTrack** «Функциональные возможности для подключенных пользователей и телеметрия» | Auto | Auto | удалена | → Disabled (revert Automatic) | proven (приватность) | Insider (нужны необязательные данные), Diagnostic Data Viewer. WU/Store работают |
| **dmwappushservice** «Служба маршрутизации push-сообщений WAP» | Manual (Trig) | Manual (Trig) | удалена | → Disabled (revert Manual) | situational: нет MDM/Intune/Autopilot | MDM/Intune, Unified Write Filter (Microsoft) |
| **SysMain** | Auto | Auto | Disabled | → Disabled (revert Automatic) | situational: только все диски SSD/NVMe и ОЗУ ≥ 16 ГБ | префетч запуска приложений, ReadyBoot, **сжатие памяти** (Get-MMAgent показывает False), медленнее «холодный» старт программ; на HDD — заметно хуже |
| **WSearch** «Windows Search» | Auto (Delayed) | Auto (Delayed) | Disabled | → Disabled (revert AutomaticDelayed) | situational: не пользуетесь поиском по содержимому | поиск файлов в Пуске/Проводнике (медленный неиндексированный), Outlook/OneNote; зависимая служба `workfolderssvc` |
| **Spooler** «Диспетчер печати» | Auto | Auto | Auto | → Disabled (revert Automatic) | situational: нет принтера и не нужен «Microsoft Print to PDF» | печать, PDF-принтер, диалоги печати. Плюс безопасности: MSRC (PrintNightmare, CVE-2021-34527) рекомендует отключать Spooler, если не нужен |
| **MapsBroker** «Диспетчер скачанных карт» | Auto (Delayed) | Auto (Delayed) | Auto (Delayed) | → Manual (revert AutomaticDelayed) | situational: не используете офлайн-карты | ничего (стартует по запросу) |
| **XblAuthManager / XblGameSave / XboxNetApiSvc** | Manual / Manual (Trig) / Manual | так же | Disabled | → Disabled (revert Manual) + задача `\Microsoft\XblGameSave\XblGameSaveTask` | situational: нет Game Pass / приложения Xbox / игр из Microsoft Store | вход Xbox в играх (Forza, Halo, Minecraft Bedrock и др.), облачные сохранения, Xbox party/сетевые проверки. **FPS не прибавит** — службы и так стартуют по требованию |
| **XboxGipSvc** «Xbox Accessory Management Service» | Manual (Trig) | Manual (Trig) | Disabled | **avoid** | — | аксессуары Xbox (прошивки, переназначение, Elite, беспроводной адаптер). В winutil есть issue, где после такого твика служба «зависает в Starting» и откат не помогает |
| **lfsvc** «Служба географического положения» | Manual (Trig) | Manual (Trig) | Manual (Running) | → Disabled (revert Manual) | situational | погода, карты, авто-часовой пояс (`tzautoupdate`), «Поиск устройства». Лучше использовать privacy-переключатель «Расположение» |
| **DPS** «Служба политики диагностики» | Auto | Auto | Auto | → Disabled (revert Automatic) | experimental | средства устранения неполадок, сетевая диагностика, **SRUM** (srumsvc.dll в процессе DPS): «Использование данных», «Журнал приложений», расход батареи. Microsoft отключает DPS только в руководстве soft-real-time для IoT |
| **WdiServiceHost / WdiSystemHost** «Узел службы/системы диагностики» | Manual | Manual | Manual | не трогать отдельно (запускаются DPS) | — | — |
| **PcaSvc** «Служба помощника по совместимости программ» | **Manual** | **Auto (Delayed, Trig)** | Auto (Delayed, Trig) | → Disabled (revert: Win11 AutomaticDelayed, Win10 Manual — два id) | experimental | автоматические шимы совместимости для старых программ/установщиков, окно «Программа могла установиться неправильно». Microsoft (Server 2016): «OK to disable» |
| **TrkWks** «Клиент отслеживания изменившихся связей» | Auto | Auto | Auto | → Manual (revert Automatic) | experimental | починка ярлыков на файлы, перемещённые между томами/по сети (NTFS link tracking) |
| **WerSvc** «Служба регистрации ошибок Windows» | Manual (Trig) | Manual (Trig) | Manual | → Disabled (revert Manual) | experimental (лучше политика WER) | локальные отчёты/дампы WER, окна «Программа не отвечает» с поиском решения. Microsoft (Server): «Do not disable» |
| **Fax** «Факс» | Manual | нет на чистой 23H2 (FoD) | нет | **avoid (бесполезно)** | — | служба не работает, пока не используется |
| **RetailDemo**, **wisvc**, **PhoneSvc**, **WpcMonSvc**, **SEMgrSvc**, **diagnosticshub.standardcollector.service** | Manual / Manual(Trig) | так же | см. §1 | **avoid (бесполезно)** | — | все уже «Вручную (по триггеру)» и не работают без запроса; отключение не даёт ничего, но ломает: Insider, звонки Phone Link, Family Safety, NFC/платежи, профилировщик Visual Studio |
| **RemoteRegistry** «Удаленный реестр» | Disabled | Disabled | Disabled | ничего (уже дефолт). Если найдено Manual/Auto — показать «изменено не по умолчанию» | — | — |
| **InventorySvc** «Служба инвентаризации и оценки совместимости» | нет (на снимке 04.2024) | Manual | Manual | не трогаем (запускается задачей Appraiser) | НЕ ПРОВЕРЕНО | официальной документации нет |

Примечания:
- «Оценка» относится к заявленному эффекту (приватность/фоновая активность), **не к FPS**.
- PcaSvc — единственная служба из каталога с разными дефолтами Win10/Win11, поэтому в JSON две записи (`services.pcasvc-disable` для win11 и `services.pcasvc-disable-win10`).
- winutil (`OriginalType`) огрубляет дефолты: «Automatic» вместо «Automatic (Delayed)», `SharedAccess` = «Automatic» (на чистой 23H2 — Manual (Triggered)), `Netlogon`/`TermService`/`VaultSvc` = «Automatic» (на чистых — Manual). Поэтому winutil использовать как ориентир, а не как источник дефолтов.
- Актуальный winutil (тег 26.10.07) сократил список служб до 5 (CscService→Disabled, DiagTrack→Disabled, MapsBroker→Manual, StorSvc→Manual, SharedAccess→Disabled) — сам отказался от массового «перевода в ручной режим».

### 2.3. Службы, которые НЕЛЬЗЯ трогать (в JSON — `avoid`)

| Группа | Службы | Почему |
|---|---|---|
| Античиты | `vgc`, `vgk` (Riot Vanguard), `FACEIT`, `FACEITService`, `EasyAntiCheat`, `EasyAntiCheat_EOS`, `BEService`, `BEDaisy` (BattlEye), любые службы с `ImagePath` в папках античитов | VAN 57 и аналогичные ошибки — игра не запустится; Vanguard сам меняет тип запуска `vgc` (Event 7040 на этой машине). Правило для кода: **никогда не менять службы вне белого списка каталога** |
| Ввод текста | Win10: `TabletInputService`; Win11: `TextInputManagementService` | Microsoft Japan: «не останавливайте эту службу» — ломаются IME/ввод в UWP-приложениях, поиск, сенсорная клавиатура; в Win11 служба переименована и сделана неотключаемой |
| Сеть и безопасность | `BFE`, `mpssvc`, `Dhcp`, `Dnscache`, `nsi`, `NlaSvc`, `netprofm`, `Wcmsvc`, `iphlpsvc`, `WinHttpAutoProxySvc`, `CryptSvc`, `KeyIso` | без BFE не работает брандмауэр/IPsec и многие VPN/античиты; CryptSvc — проверка подписей (установка драйверов, обновления, античиты — **НЕ ПРОВЕРЕНО** для конкретных античитов) |
| Ядро ОС | `RpcSs`, `RpcEptMapper`, `DcomLaunch`, `Winmgmt` (WMI), `EventLog`, `Schedule`, `ProfSvc`, `Power`, `PlugPlay`, `BrokerInfrastructure`, `SystemEventsBroker`, `TimeBrokerSvc`, `CoreMessagingRegistrar`, `StateRepository`, `LSM`, `SamSs`, `gpsvc`, `UserManager`, `Themes`, `FontCache`, `DispBrokerDesktopSvc` | система не загрузится или не войдёт пользователь; WMI нужен самому приложению для детекта |
| Звук и ввод | `Audiosrv`, `AudioEndpointBuilder`, `hidserv`, `MMCSS`/`AudioSrv` зависимости, `GameInputSvc` | нет звука/контроллеров. (Microsoft отключает Audiosrv только в soft-real-time IoT — не для игр) |
| Store / Game Pass / лицензии | `AppXSvc`, `ClipSVC`, `InstallService`, `LicenseManager`, `wlidsvc`, `TokenBroker`, `GamingServices`, `GamingServicesNet` | Store, игры Game Pass, вход в учётную запись Microsoft |
| Обновления | `wuauserv`, `UsoSvc`, `BITS`, `WaaSMedicSvc`, `DoSvc`, `TrustedInstaller` | безопасность, обновления Defender/Store; WaaSMedicSvc/DoSvc защищены (`LaunchProtected=2`); BITS/TrustedInstaller меняют тип запуска сами |
| Защитник | `WinDefend`, `WdNisSvc`, `SecurityHealthService`, `wscsvc`, `Sense`, `MDCoreSvc`, `webthreatdefsvc` | безопасность; `LaunchProtected` + Tamper Protection — штатно не меняется |
| Уведомления | `WpnService`, `WpnUserService` | не будет уведомлений, части Action Center; некоторые приложения (Store, Teams) ломаются |
| Хранилище | `defragsvc`, `StorSvc`, `VSS`, `swprv` | TRIM/оптимизация дисков, Storage Sense, точки восстановления |
| Удаление служб | любые (`sc delete`) | необратимо без переустановки/импорта .reg; именно так сделано на этой машине с DiagTrack — откат твиком невозможен |

### 2.4. Службы с «плавающим» типом запуска (не включать в детект)

Наблюдение на машине (Event ID 7040, 25–26.10.2026): BITS «Вручную → Автоматически → Вручную» с интервалом ≈ 2 мин несколько раз в час (BITS сам поднимает тип запуска, пока у него есть задания); TrustedInstaller «Вручную ⇄ Автоматически» во время обслуживания; `vgc` «Вручную → Автоматически» (меняет Vanguard). Также известно: `edgeupdate`/`GoogleUpdater*` переустанавливают свои службы при обновлении. Код детекта не должен считать такие изменения «откатом твика».

### 2.5. Программная смена типа запуска (C#, .NET 10, x64)

Рекомендация: **SCM API (`ChangeServiceConfigW` + `ChangeServiceConfig2W`)** для конфигурации; `System.ServiceProcess.ServiceController` (NuGet `System.ServiceProcess.ServiceController`, Windows-only) — для остановки/запуска/статуса. `sc.exe` — только как запасной вариант (вывод локализован, парсить нельзя; ориентироваться на код возврата). Прямая запись `Start` в реестр — только как последний шанс для не-защищённых служб и с пометкой «нужна перезагрузка».

```csharp
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;            // NuGet: System.ServiceProcess.ServiceController
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

public enum SvcStart { Automatic, AutomaticDelayed, Manual, Disabled }
public enum SvcOpStatus { Ok, NotFound, AccessDenied, ProtectedService, Failed }

internal sealed class ScHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public ScHandle() : base(true) { }
    protected override bool ReleaseHandle() => ScNative.CloseServiceHandle(handle);
}

internal static class ScNative
{
    public const uint SC_MANAGER_CONNECT = 0x0001;
    public const uint SERVICE_QUERY_CONFIG = 0x0001, SERVICE_CHANGE_CONFIG = 0x0002,
                      SERVICE_QUERY_STATUS = 0x0004;
    public const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    public const uint SERVICE_AUTO_START = 2, SERVICE_DEMAND_START = 3, SERVICE_DISABLED = 4;
    public const uint SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;

    public const int ERROR_ACCESS_DENIED = 5, ERROR_INSUFFICIENT_BUFFER = 122,
                     ERROR_SERVICE_DOES_NOT_EXIST = 1060, ERROR_SERVICE_MARKED_FOR_DELETE = 1072;

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_DELAYED_AUTO_START_INFO { public int fDelayedAutostart; } // BOOL

    [StructLayout(LayoutKind.Sequential)]
    public struct QUERY_SERVICE_CONFIGW
    {
        public uint dwServiceType, dwStartType, dwErrorControl;
        public IntPtr lpBinaryPathName, lpLoadOrderGroup;
        public uint dwTagId;
        public IntPtr lpDependencies, lpServiceStartName, lpDisplayName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ScHandle OpenSCManagerW(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ScHandle OpenServiceW(ScHandle scm, string serviceName, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool ChangeServiceConfigW(ScHandle svc, uint serviceType, uint startType,
        uint errorControl, string? binaryPath, string? loadOrderGroup, IntPtr tagId,
        string? dependencies, string? startName, string? password, string? displayName);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool ChangeServiceConfig2W(ScHandle svc, uint infoLevel,
        ref SERVICE_DELAYED_AUTO_START_INFO info);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryServiceConfigW(ScHandle svc, IntPtr buffer, uint cbBufSize, out uint needed);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryServiceConfig2W(ScHandle svc, uint infoLevel,
        ref SERVICE_DELAYED_AUTO_START_INFO buffer, uint cbBufSize, out uint needed);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool CloseServiceHandle(IntPtr h);
}

public static class ServiceConfigurator
{
    /// null = службы нет в системе (не ошибка, а «неприменимо»).
    public static SvcStart? QueryStart(string name)
    {
        using var scm = ScNative.OpenSCManagerW(null, null, ScNative.SC_MANAGER_CONNECT);
        if (scm.IsInvalid) throw new Win32Exception();
        using var svc = ScNative.OpenServiceW(scm, name, ScNative.SERVICE_QUERY_CONFIG);
        if (svc.IsInvalid)
        {
            int e = Marshal.GetLastWin32Error();
            if (e == ScNative.ERROR_SERVICE_DOES_NOT_EXIST) return null;
            throw new Win32Exception(e);
        }
        ScNative.QueryServiceConfigW(svc, IntPtr.Zero, 0, out uint needed); // ожидаем ERROR_INSUFFICIENT_BUFFER
        IntPtr buf = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!ScNative.QueryServiceConfigW(svc, buf, needed, out _)) throw new Win32Exception();
            var cfg = Marshal.PtrToStructure<ScNative.QUERY_SERVICE_CONFIGW>(buf);
            switch (cfg.dwStartType)
            {
                case ScNative.SERVICE_AUTO_START:
                    var d = new ScNative.SERVICE_DELAYED_AUTO_START_INFO();
                    bool delayed = ScNative.QueryServiceConfig2W(svc, ScNative.SERVICE_CONFIG_DELAYED_AUTO_START_INFO,
                                       ref d, (uint)Marshal.SizeOf<ScNative.SERVICE_DELAYED_AUTO_START_INFO>(), out _)
                                   && d.fDelayedAutostart != 0;
                    return delayed ? SvcStart.AutomaticDelayed : SvcStart.Automatic;
                case ScNative.SERVICE_DEMAND_START: return SvcStart.Manual;
                case ScNative.SERVICE_DISABLED:     return SvcStart.Disabled;
                default: return SvcStart.Manual; // 0/1 — драйверы; в каталоге таких нет
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public static bool IsLaunchProtected(string name)
    {
        using var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
        return k?.GetValue("LaunchProtected") is int v && v != 0;
    }

    public static SvcOpStatus SetStart(string name, SvcStart target, bool stopIfDisabling, out int win32Error)
    {
        win32Error = 0;
        if (IsLaunchProtected(name)) return SvcOpStatus.ProtectedService; // даже не пытаемся

        using var scm = ScNative.OpenSCManagerW(null, null, ScNative.SC_MANAGER_CONNECT);
        if (scm.IsInvalid) { win32Error = Marshal.GetLastWin32Error(); return SvcOpStatus.Failed; }

        using var svc = ScNative.OpenServiceW(scm, name,
            ScNative.SERVICE_QUERY_CONFIG | ScNative.SERVICE_CHANGE_CONFIG | ScNative.SERVICE_QUERY_STATUS);
        if (svc.IsInvalid)
        {
            win32Error = Marshal.GetLastWin32Error();
            return win32Error switch
            {
                ScNative.ERROR_SERVICE_DOES_NOT_EXIST => SvcOpStatus.NotFound,
                ScNative.ERROR_ACCESS_DENIED          => SvcOpStatus.AccessDenied,
                _                                     => SvcOpStatus.Failed
            };
        }

        uint start = target switch
        {
            SvcStart.Automatic or SvcStart.AutomaticDelayed => ScNative.SERVICE_AUTO_START,
            SvcStart.Manual   => ScNative.SERVICE_DEMAND_START,
            _                 => ScNative.SERVICE_DISABLED
        };

        if (!ScNative.ChangeServiceConfigW(svc, ScNative.SERVICE_NO_CHANGE, start, ScNative.SERVICE_NO_CHANGE,
                                           null, null, IntPtr.Zero, null, null, null, null))
        {
            win32Error = Marshal.GetLastWin32Error();
            return win32Error == ScNative.ERROR_ACCESS_DENIED ? SvcOpStatus.AccessDenied : SvcOpStatus.Failed;
        }

        if (start == ScNative.SERVICE_AUTO_START)
        {
            var info = new ScNative.SERVICE_DELAYED_AUTO_START_INFO
                { fDelayedAutostart = target == SvcStart.AutomaticDelayed ? 1 : 0 };
            if (!ScNative.ChangeServiceConfig2W(svc, ScNative.SERVICE_CONFIG_DELAYED_AUTO_START_INFO, ref info))
            { win32Error = Marshal.GetLastWin32Error(); return SvcOpStatus.Failed; }
        }

        if (stopIfDisabling && start == ScNative.SERVICE_DISABLED) TryStop(name, TimeSpan.FromSeconds(30));
        return SvcOpStatus.Ok;
    }

    public static void TryStop(string name, TimeSpan timeout)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending) return;
            if (!sc.CanStop) return;                       // служба не принимает STOP — оставить до перезагрузки
            sc.Stop(stopDependentServices: true);           // перегрузка с bool — .NET 7+ (НЕ ПРОВЕРЕНО на .NET 10 SDK, проверить при сборке)
            sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
        }
        catch (InvalidOperationException) { /* нет службы / нет прав / ERROR_SERVICE_CANNOT_ACCEPT_CTRL (1061) */ }
        catch (System.ServiceProcess.TimeoutException) { /* остановится после перезагрузки */ }
    }
}
```

Важные детали:
- **Порядок для зависимых служб.** XblGameSave зависит от XblAuthManager (проверено `sc enumdepend`); WSearch имеет зависимую `workfolderssvc`. При отключении — сначала зависимые, потом основная; `Stop(stopDependentServices: true)` это делает.
- **Revert и запуск.** Схема JSON не имеет поля `start`. Рекомендуется при откате на `Automatic`/`AutomaticDelayed` попытаться запустить службу (`ServiceController.Start()`), чтобы пользователю не требовалась перезагрузка; ошибки запуска не считать ошибкой отката.
- **Ошибки:** 1060 `ERROR_SERVICE_DOES_NOT_EXIST` → «Нет в этой системе» (n/a); 5 `ERROR_ACCESS_DENIED` → «Служба защищена Windows»; 1072 `ERROR_SERVICE_MARKED_FOR_DELETE` → «Служба помечена на удаление, перезагрузите ПК»; 1061 при остановке → оставить до перезагрузки.
- **Trimming/NativeAOT.** `DllImport` работает в self-contained single-file. Если включён trimming/AOT — предпочтительнее `[LibraryImport]` (source-generated). `ServiceController` совместим с trimming.
- **sc.exe (запасной путь):** `sc.exe config <имя> start= disabled|demand|auto|delayed-auto` (пробел после `=` обязателен). Коды возврата: 0 успех, 5 отказано в доступе, 1060 нет службы. Windows PowerShell 5.1 `Set-Service` **не умеет** `AutomaticDelayedStart` (есть только в PowerShell 6+).
- **Пер-пользовательские службы** (CDPUserSvc, BcastDVRUserService, OneSyncSvc, WpnUserService…). Шаблон — ключ без суффикса (`Type` 0x50/0x60), экземпляры — `<имя>_<LUID>`. По Microsoft: отключение = `Start=4` у шаблона (через `sc config <шаблон> start= disabled` или реестр); экземпляр создаётся при входе уже отключённым; `UserServiceFlags=0` у шаблона запрещает создание экземпляров. В каталоге пер-пользовательские службы **не используются** (их отключение ломает буфер обмена, уведомления, Bluetooth, запись игр).

### 2.6. Защищённые службы — честно

- Признак: `LaunchProtected ≠ 0` в ключе службы. Microsoft: «система не позволяет никакому не-защищённому процессу изменять конфигурацию защищённой службы»; администратору доступны только `sc config start=Auto`, `sc qc`, `sc start`, `sc interrogate`, `sc sdshow`.
- На этой машине у администраторов `FullControl` на **ключ реестра** WaaSMedicSvc/DoSvc/WinDefend, т.е. «обход» через запись `Start=4` в реестр технически возможен (так делают многие твикеры). Мы этого **не делаем**: (1) это обход механизма защиты; (2) WaaSMedicSvc и Update Orchestrator восстанавливают компоненты обновления; (3) WinDefend дополнительно защищён Tamper Protection; (4) пользы для игр ноль. В UI — пометка «Защищено Windows, изменение не поддерживается».
- TrustedInstaller-ACL (а не LaunchProtected) встречается на ключе `Services\TrustedInstaller` (Brink: для восстановления нужен захват владельца) — тоже не трогаем.
- Для управления обновлениями (если когда-нибудь понадобится) — только документированные политики `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate` (тема другого исследования).

### 2.7. Удалённые службы

Если служба из каталога отсутствует (как DiagTrack здесь), твик показывается как «Нет в этой системе», revert недоступен. Автоматически пересоздавать службы (`sc create` + импорт ключа) **не нужно**: параметры (ImagePath `svchost -k utcsvc -p`, RequiredPrivileges, FailureActions, ServiceSidType, TriggerInfo) различаются по билдам; восстановление — через `sfc`/`DISM /RestoreHealth` неэффективно для удалённых ключей, корректный путь — in-place upgrade или .reg с той же сборки (ElevenForum/Brink предоставляет .reg для Win11). Это ответственность пользователя.

---

## 3. Запланированные задачи

### 3.1. Каталог задач

Статус «на машине» — результат `ITaskService.GetFolder/GetTask` (COM, только чтение) на этой 23H2. «Стоковая 23H2» — по наличию папки в `TaskCache\Tree`/XML-файла и по источникам (VDOT от Microsoft для Win10 20H2, ElevenForum, winutil). Где нет уверенности — **НЕ ПРОВЕРЕНО**.

| Путь задачи | Что делает (действие) | На этой машине | Стоковая 23H2 / Win10 22H2 | Рекомендация | Оценка |
|---|---|---|---|---|---|
| `\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser` | на 23H2: `sc.exe start InventorySvc` (TimeTrigger) | XML есть, в TaskCache нет → «Not Found» | есть / есть (CompatTelRunner) | отключить | proven (телеметрия совместимости) |
| `…\Application Experience\Microsoft Compatibility Appraiser Exp` | вариант Appraiser (есть в VDOT для Win10 20H2) | нет | НЕ ПРОВЕРЕНО / есть на части сборок | отключить, если есть | proven |
| `…\Application Experience\ProgramDataUpdater` | сбор данных программ для CEIP | нет (и XML нет) | нет на Win11 23H2 (проверено по XML) / НЕ ПРОВЕРЕНО на Win10 22H2 | отключить, если есть | proven |
| `…\Application Experience\MareBackup` | `compattelrunner.exe -m:aeinv.dll -f:UpdateSoftwareInventoryW invsvc`, `-m:appraiser.dll -f:DoScheduledTelemetryRun`, `-m:aemarebackup.dll -f:BackupMareData` | XML есть, не зарегистрирована | есть (Win11 22H2+) | отключить (вместе с Appraiser) | proven; побочный эффект: список приложений для восстановления через «Архивация Windows» может не обновляться (НЕ ПРОВЕРЕНО) |
| `…\Application Experience\PcaPatchDbTask` | `rundll32 PcaSvc.dll,PcaPatchSdbTask` — обновление базы совместимости | XML есть | есть | **не трогать** | avoid (не телеметрия, пользы нет) |
| `…\Application Experience\StartupAppTask` | `rundll32 Startupscan.dll,SusRunTask` — оценка «влияния на запуск» | XML есть | есть | можно отключить | experimental (Диспетчер задач перестанет считать «Влияние на запуск») |
| `…\Application Experience\SdbinstMergeDbTask`, `PcaWallpaperAppDetect` | слияние SDB, детект обоев-приложений | XML есть | есть (23H2) | не трогать | — |
| `\Microsoft\Windows\Customer Experience Improvement Program\Consolidator` | CEIP: сбор и отправка данных использования | Not Found | есть / есть | отключить | proven |
| `…\Customer Experience Improvement Program\UsbCeip` | CEIP по USB | Not Found | есть / есть | отключить | proven |
| `…\Customer Experience Improvement Program\KernelCeipTask` | CEIP ядра | Not Found (ключ-остаток в TaskCache) | НЕ ПРОВЕРЕНО / есть | отключить, если есть | proven |
| `\Microsoft\Windows\Autochk\Proxy` | сбор SQM-данных autochk для CEIP | Not Found (папка пустая) | есть / есть | отключить | proven |
| `\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector` | отправка общих сведений о диске/системе (CEIP) | Not Found | есть / есть | отключить | proven |
| `\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticResolver` | уведомления о проблемах диска | есть, Disabled | по умолчанию отключена | не трогать | — |
| `\Microsoft\Windows\Feedback\Siuf\DmClient` | `dmclient.exe` — обновление строк опросов обратной связи | есть, Disabled | есть / есть | отключить | proven |
| `\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload` | то же, по сценарию | есть, Disabled | есть / есть | отключить | proven |
| `\Microsoft\Windows\Device Information\Device` и `Device User` | `devicecensus.exe SystemCxt/UserCxt` — перепись устройства (Census) для диагностических данных | есть, Ready | есть | можно отключить | experimental (при выключенной DiagTrack данные и так не уходят; влияние на подбор обновлений — НЕ ПРОВЕРЕНО) |
| `\Microsoft\Windows\Maps\MapsToastTask`, `MapsUpdateTask` | уведомления/обновление офлайн-карт | Not Found (папка пустая) | есть (MapsUpdateTask — НЕ ПРОВЕРЕНО на 23H2) | отключить | situational (не пользуетесь картами) |
| `\Microsoft\Windows\Maintenance\WinSAT` | оценка производительности (WinSAT) при обслуживании | Not Found | есть / есть | можно отключить | experimental (обслуживание и так не идёт при активном пользователе) |
| `\Microsoft\Windows\Power Efficiency Diagnostics\AnalyzeSystem` | анализ энергопотребления | Not Found | есть / есть | можно отключить на ПК | experimental |
| `\Microsoft\Windows\Windows Error Reporting\QueueReporting` | отправка очереди отчётов WER | Not Found | есть / есть | отключить вместе с политикой WER | situational |
| `\Microsoft\Windows\CloudExperienceHost\CreateObjectTask` | COM-обработчик CloudExperienceHost (OOBE, «завершите настройку») | есть, Ready | есть | можно отключить | experimental (назначение официально не документировано) |
| `\Microsoft\Windows\DiskFootprint\Diagnostics` | `disksnapshot.exe -z` | есть, Ready | есть | можно отключить | experimental |
| `\Microsoft\Windows\DiskFootprint\StorageSense` | Контроль памяти | есть | есть | **не трогать** | — |
| `\Microsoft\Windows\Shell\FamilySafetyMonitor`, `FamilySafetyRefreshTask` | `wpcmon.exe` — Microsoft Family | есть, Ready | есть | отключить, если нет детских учётных записей | situational |
| `\Microsoft\XblGameSave\XblGameSaveTask` | `XblGameSaveTask.exe standby` (IdleTrigger) | есть, Disabled | есть | вместе со службами Xbox | situational |
| `\Microsoft\Windows\Flighting\FeatureConfig\UsageData*`, `\Microsoft\Windows\PI\Sqm-Tasks` | отчёты об использовании функций / сведения о TPM, Secure Boot | есть | есть | **не трогать** | НЕ ПРОВЕРЕНО (влияние на поэтапное включение функций, на проверки TPM/Secure Boot, важные для Vanguard на Win11) |
| `\Microsoft\Windows\Defrag\ScheduledDefrag` | `defrag.exe -c -h -o -$` | есть, Ready (последний запуск 26.10.2026, результат 0) | есть | **никогда не отключать** | avoid: для SSD выполняет **ReTrim** (Microsoft: оптимизация SSD раз в месяц, «traditional defrag» и retrim) |
| `\Microsoft\Windows\Servicing\StartComponentCleanup` | очистка WinSxS (с 30-дневной задержкой) | есть | есть | **не отключать** | avoid: Microsoft «strongly recommends not disabling component cleanup» |
| `\Microsoft\Windows\UpdateOrchestrator\*`, `\Microsoft\Windows\WaaSMedic\*`, `\Microsoft\Windows\WindowsUpdate\*` | обновления | частично Disabled | есть | не трогать | avoid (у многих — ACL только SYSTEM, админ получит «Отказано в доступе») |
| `\Microsoft\Windows\Windows Defender\*` | обслуживание Защитника | есть | есть | не трогать | avoid |
| `\Microsoft\Windows\SystemRestore\SR` | точки восстановления по расписанию | есть | есть | не трогать | avoid |
| `\Microsoft\Windows\AppxDeploymentClient\UCPD velocity` | `UCPDMgr.exe` (поддержка драйвера UCPD) | есть | есть | не трогать | avoid |
| `\Microsoft\Windows\Chkdsk\ProactiveScan`, `\Microsoft\Windows\DiskCleanup\SilentCleanup` | проактивная проверка ФС, очистка при нехватке места | есть | есть | не трогать | — |

Важно про VDOT: Microsoft Virtual Desktop Optimization Tool отключает в том числе `ScheduledDefrag`, `SR`, `StartComponentCleanup`, `RegIdleBackup` — **это рекомендации для VDI** (виртуальные диски, образы), их нельзя переносить на физический игровой ПК.

### 3.2. Программное управление задачами

Основной путь — COM Task Scheduler 2.0 (`Schedule.Service` → `ITaskService` → `ITaskFolder.GetTask` → `IRegisteredTask.Enabled`). Запасной — `schtasks.exe` (парсить только `/XML` и код возврата; текстовый вывод локализован).

```csharp
using System;
using System.Runtime.InteropServices;

public enum TaskOpStatus { Ok, NotFound, AccessDenied, Failed }

public static class TaskToggle
{
    const int E_FILE_NOT_FOUND = unchecked((int)0x80070002); // нет папки или задачи (проверено на этой машине)
    const int E_PATH_NOT_FOUND = unchecked((int)0x80070003);
    const int E_ACCESSDENIED   = unchecked((int)0x80070005);

    /// enabledNow: null — задачи нет.
    public static TaskOpStatus Get(string fullPath, out bool? enabledNow) => Run(fullPath, null, out enabledNow);
    public static TaskOpStatus Set(string fullPath, bool enabled) => Run(fullPath, enabled, out _);

    static TaskOpStatus Run(string fullPath, bool? setEnabled, out bool? enabledNow)
    {
        enabledNow = null;
        int i = fullPath.LastIndexOf('\\');
        string folderPath = i <= 0 ? "\\" : fullPath[..i];
        string taskName = fullPath[(i + 1)..];

        Type t = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
        dynamic ts = Activator.CreateInstance(t)!;
        try
        {
            ts.Connect();
            dynamic task;
            try { task = ts.GetFolder(folderPath).GetTask(taskName); }
            catch (Exception e) when (e.HResult is E_FILE_NOT_FOUND or E_PATH_NOT_FOUND)
            { return TaskOpStatus.NotFound; }   // .NET мапит 0x80070002 в FileNotFoundException — ловим по HResult

            enabledNow = (bool)task.Enabled;
            if (setEnabled is null || enabledNow == setEnabled) return TaskOpStatus.Ok;
            try { task.Enabled = setEnabled.Value; enabledNow = setEnabled; return TaskOpStatus.Ok; }
            catch (Exception e) when (e.HResult == E_ACCESSDENIED || e is UnauthorizedAccessException)
            { return TaskOpStatus.AccessDenied; }
        }
        catch (COMException) { return TaskOpStatus.Failed; }
        finally { Marshal.FinalReleaseComObject(ts); }
    }
}
```

- `dynamic` COM-позднее связывание работает в .NET (Windows), но **отключается при trimming** (`BuiltInComInteropSupport=false`). Если сборка тримится — использовать `schtasks.exe` или source-generated COM (`[GeneratedComInterface]`) для `ITaskService/ITaskFolder/IRegisteredTask` из `taskschd.h`.
- Запасной путь: `schtasks.exe /Change /TN "\Microsoft\Windows\Feedback\Siuf\DmClient" /DISABLE` (или `/ENABLE`), код 0 — успех, 1 — ошибка (в т.ч. «не найдено»). Проверка состояния: `schtasks.exe /Query /TN "<путь>" /XML` → элемент `<Settings><Enabled>false</Enabled>` (если элемента нет — включена). На этой машине `schtasks /query` для несуществующей задачи печатает «ERROR: The system cannot find the file specified.» и выходит с кодом 1.
- Состояние `IRegisteredTask.State`: 1 Disabled, 2 Queued, 3 Ready, 4 Running.
- **Not Found** → в UI «Нет в этой системе», в детекте — пропуск (задача считается «в целевом состоянии»), revert для неё — пропуск. Никогда не создавать задачу заново.
- **Обновления функций Windows** (23H2→24H2 и т.п.) пересоздают/включают задачи и могут вернуть службы к дефолтам — при старте приложения перепроверять состояние применённых твиков и предлагать переприменить.
- Задачи `UpdateOrchestrator`/`WaaSMedic` и др. часто имеют дескриптор безопасности «только SYSTEM» — `AccessDenied` для них ожидаем; в каталоге их нет.

---

## 4. Приватность и телеметрия

### 4.1. Важные правила

1. **Влияние на производительность — практически нулевое.** Это настройки приватности/рекламы. Единственное заметное исключение — Widgets (процессы `Widgets.exe`, `WidgetService.exe`, `msedgewebview2.exe` держат десятки–сотни МБ ОЗУ) и Recall на Copilot+ PC (снимки экрана, NPU/диск). Замеров FPS нет.
2. **Редакции.** Ниже для каждой политики указано, работает ли она на Home/Pro. Политики, применимые только к Enterprise/Education, на Pro молча игнорируются.
3. **Policies-ключи vs значения «Параметров».** Ключи `…\Policies\…` — групповые политики (переключатель в «Параметрах» становится серым «Управляется организацией»). Значения вне Policies — то же, что пишет переключатель в «Параметрах». Для revert политики — `delete` (политики по умолчанию нет); для значений «Параметров» — явное значение по умолчанию.
4. **Локальная групповая политика.** Если пользователь сам настроил тот же параметр в gpedit.msc, `registry.pol` перезапишет наше значение при `gpupdate` (каждые ~90 мин). На доменных ПК доменная GPO имеет приоритет.
5. **HKCU из повышенного процесса** — см. §4.4.

### 4.2. Каталог настроек

| Настройка | Ключ / значение (apply) | Дефолт (revert) | ОС / редакции | Эффект и оценка |
|---|---|---|---|---|
| Уровень диагностических данных | `HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection` `AllowTelemetry` DWORD = 1 (или 0) | delete | Win10/11; **0 работает только Ent/Edu/Server, на Home/Pro = 1**. Значение 2 (Enhanced) в Win11 отсутствует | proven: блокирует «Необязательные данные». По умолчанию без политики: Required + выбор пользователя. Не трогаем `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection` — это значения, которые пишет «Параметры» (на этой машине `AllowTelemetry=0`, `MaxTelemetryAllowed=1`) |
| Ограничить дампы и журналы | `…\DataCollection` `LimitDiagnosticLogCollection`=1, `LimitDumpCollection`=1 | delete | MDM: только Win11/Server 2022; GP — по PC-Tuning с Win10 1909 (**НЕ ПРОВЕРЕНО** на Win10 22H2) | situational: имеет смысл, только если разрешены необязательные данные |
| Уведомления/опросы обратной связи | `HKLM\…\DataCollection` `DoNotShowFeedbackNotifications`=1; `HKCU\Software\Microsoft\Siuf\Rules` `NumberOfSIUFInPeriod`=0, `PeriodInNanoSeconds`=0 (DWORD; на машине так и записано) | delete все три («Автоматически») | Win10/11, Pro+ (политика); Siuf — все редакции | proven (Microsoft: Never = 0/0, Automatically = удалить значения) |
| Рекламный идентификатор | `HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo` `Enabled`=0; `HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo` `DisabledByGroupPolicy`=1 | `Enabled`=1; policy delete | Win10/11, Pro+ для политики | proven |
| Журнал действий | `HKLM\SOFTWARE\Policies\Microsoft\Windows\System` `PublishUserActivities`=0, `UploadUserActivities`=0 | delete | Win10/11 | proven. `EnableActivityFeed` **не трогаем**: актуальный winutil намеренно оставляет `EnableActivityFeed=1` «чтобы сохранить журнал буфера обмена» (механизм **НЕ ПРОВЕРЕН**) |
| Персонализированные возможности (Tailored experiences) | `HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy` `TailoredExperiencesWithDiagnosticDataEnabled`=0; `HKCU\Software\Policies\Microsoft\Windows\CloudContent` `DisableTailoredExperiencesWithDiagnosticData`=1 | 1; policy delete | Win10 1703+/Win11, Pro+ (политика) | proven |
| Bing/веб-результаты в поиске Пуска | `HKCU\Software\Policies\Microsoft\Windows\Explorer` `DisableSearchBoxSuggestions`=1 | delete | Win10 2004+/Win11 (на 24H2 build 26100.9278+ также появился штатный переключатель) | proven (воспроизводимое поведение; Microsoft описывает политику как «Отключить отображение последних поисковых запросов в поле поиска Проводника» — побочный эффект: нет истории поиска в Проводнике). Перезапуск Проводника |
| Виджеты (Win11) | `HKLM\SOFTWARE\Policies\Microsoft\Dsh` `AllowNewsAndInterests`=0 | delete | Win11 21H2+, Pro+ (документировано) | proven; RAM-эффект — situational. **Не писать `TaskbarDa`** — защищён UCPD |
| Новости и интересы (Win10) | `HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds` `EnableFeeds`=0 | delete | Win10 | proven |
| Copilot (старая панель) | `HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot` `TurnOffWindowsCopilot`=1 | delete | Win10 22H2 (KB5032278+), Win11 22H2/23H2. **Политика объявлена устаревшей** и не действует на новое приложение Copilot (24H2+); удаление приложения — отдельное действие (политика `RemoveMicrosoftCopilotApp` — только Ent/Edu) | situational. В России Copilot недоступен (winget msstore по ID 9NHT9RB2F4HD на этой машине не находит пакет) |
| Recall (Copilot+ PC) | `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI` `DisableAIDataAnalysis`=1, `AllowRecallEnablement`=0 | delete оба | Win11 24H2 26100.3915+ (KB5055627), Pro+; работает только на Copilot+ PC | situational. `AllowRecallEnablement=0` удаляет компонент Recall и снимки, **нужна перезагрузка**. По умолчанию Recall и так выключен (opt-in) |
| Реклама, советы, предложения (Home/Pro) | `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager`: `SubscribedContent-338393Enabled`, `-353694Enabled`, `-353696Enabled` (предложения в «Параметрах»), `-338389Enabled` (советы), `-310093Enabled` (экран приветствия после обновлений), `-338388Enabled` и `SystemPaneSuggestionsEnabled` (предложения в Пуске Win10), `SilentInstalledAppsEnabled` (авто-установка продвигаемых приложений), `SoftLandingEnabled`, `RotatingLockScreenOverlayEnabled` + `-338387Enabled` (факты на экране блокировки) = 0; `HKCU\…\UserProfileEngagement` `ScoobeSystemSettingEnabled`=0; `HKCU\…\Explorer\Advanced` `Start_IrisRecommendations`=0, `Start_AccountNotifications`=0, `ShowSyncProviderNotifications`=0 | все = 1 (так пишет «Enable_all_of_the_above_ads.reg» Brink — «This is the default setting»; на чистой системе части значений просто нет, что равносильно 1) | Win10/11, все редакции | proven (соответствует переключателям «Параметров»). Перезапуск Проводника |
| Политика «Отключить возможности потребителя» | `HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent` `DisableWindowsConsumerFeatures`=1 | delete | **только Enterprise/Education** (Policy CSP: Pro ❌) | situational — на Home/Pro не работает; на этой машине стоит `=0` (кем-то записано) |
| Cortana (Win10) | `HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search` `AllowCortana`=0 | delete | Win10 (Pro+). В Win11 приложение Cortana снято с поддержки | proven |
| Расположение | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location` `Value`(SZ)=`Deny` | `Allow` | Win10/11 | situational: ломает погоду, карты, авто-часовой пояс, «Поиск устройства» |
| Оптимизация доставки (P2P) | `HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization` `DODownloadMode`=0 (HTTP only) | delete (дефолт: LAN = 1) | Win10/11. Режим 100 (Bypass) в Win11 **устарел**, не использовать; 99 (Simple) — без облачных служб DO | situational: по умолчанию DO и так раздаёт только в локальной сети; эффект есть, если в LAN несколько ПК или режим был Internet (3) |
| Отчёты об ошибках | `HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting` `Disabled`=1 | delete | Win10/11 | situational: нет отправки отчётов; теряется диагностика вылетов |
| Рукописный ввод и ввод с клавиатуры | `HKCU\Software\Microsoft\InputPersonalization` `RestrictImplicitInkCollection`=1, `RestrictImplicitTextCollection`=1; `HKCU\Software\Microsoft\InputPersonalization\TrainedDataStore` `HarvestContacts`=0; `HKCU\Software\Microsoft\Personalization\Settings` `AcceptedPrivacyPolicy`=0 | 0, 0, 1, 1 | Win10/11 | proven |
| Отслеживание запусков и язык для сайтов | `HKCU\…\Explorer\Advanced` `Start_TrackProgs`=0; `HKCU\Control Panel\International\User Profile` `HttpAcceptLanguageOptOut`=1 | `Start_TrackProgs`=1; `HttpAcceptLanguageOptOut` delete | Win10/11 | proven («Параметры → Конфиденциальность → Общие»). `Start_TrackProgs=0` убирает «Часто используемые» в Пуске |
| Телеметрия приложений / Inventory Collector | `HKLM\SOFTWARE\Policies\Microsoft\Windows\AppCompat` `AITEnable`=0, `DisableInventory`=1 | delete | Win10/11 | experimental: устаревшие механизмы; актуальность для 23H2+ **НЕ ПРОВЕРЕНА** |
| Старый CEIP | `HKLM\SOFTWARE\Policies\Microsoft\SQMClient\Windows` `CEIPEnable`=0 | delete | Win10/11 | experimental: CEIP заменён диагностическими данными; вероятно no-op |

### 4.3. Особые случаи

- **Widgets / TaskbarDa / UCPD.** `HKCU\…\Explorer\Advanced\TaskbarDa` (кнопка виджетов) на 24H2 и обновлённой 23H2 защищён драйвером `UCPD.sys` (на этой машине: `Services\UCPD Start=1`, есть задача `UCPD velocity` → `UCPDMgr.exe`). Запись даёт «Отказано в доступе» даже от LocalSystem. Политика `AllowNewsAndInterests=0` отключает всю функцию виджетов, включая кнопку, — документировано. Отключать UCPD (`sc config UCPD start=disabled` + задача) — **avoid** (защитный компонент, применяет и к ассоциациям файлов).
- **Copilot.** На 23H2 — `TurnOffWindowsCopilot` (HKCU-политика; Microsoft: «не для нового Copilot»). На 24H2+ Copilot — обычное приложение Store (`Get-AppxPackage *Copilot*`); удаление — отдельным действием с подтверждением, revert — переустановка из Store (автоматически не гарантируется). На этой машине Copilot не установлен.
- **Recall.** Только Copilot+ PC. Проверить наличие: `Get-WindowsOptionalFeature -Online -FeatureName Recall` (на этой машине компонента нет). Если компонента нет — твик «неприменим».
- **Bing.** На Win10 до 2004 работал `HKCU\Software\Microsoft\Windows\CurrentVersion\Search\BingSearchEnabled=0` (+`CortanaConsent=0`); на современных сборках надёжен `DisableSearchBoxSuggestions`. В каталог включён только он.
- **Delivery Optimization.** Это не «+пинг»: по умолчанию (LAN) раздача идёт только соседям по локальной сети (Microsoft). Полностью отключать службу DoSvc нельзя (защищена, ломает загрузку обновлений/Store).

### 4.4. HKCU, когда приложение запущено от администратора

Обычно (администратор + UAC) процесс работает под той же учётной записью, и `Registry.CurrentUser` — правильный куст. Если пользователь — стандартный, а повышение выполнено учёткой другого администратора, `HKCU` будет кустом **администратора**. Решение: определить SID владельца `explorer.exe` в сессии нашего процесса и писать в `HKEY_USERS\<SID>`.

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

static class InteractiveUser
{
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);
    const uint TOKEN_QUERY = 0x0008;

    /// Возвращает куст пользователя рабочего стола. Не освобождать, если вернулся Registry.CurrentUser.
    public static RegistryKey OpenHive(out bool mustDispose)
    {
        mustDispose = false;
        int session = Process.GetCurrentProcess().SessionId;   // корректно и для RDP
        string mySid = WindowsIdentity.GetCurrent().User!.Value;
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            using (p)
            {
                if (p.SessionId != session) continue;
                if (!OpenProcessToken(p.Handle, TOKEN_QUERY, out var tok)) continue;
                try
                {
                    using var id = new WindowsIdentity(tok);
                    string sid = id.User!.Value;
                    if (sid == mySid) return Registry.CurrentUser;
                    var k = Registry.Users.OpenSubKey(sid, writable: true);
                    if (k != null) { mustDispose = true; return k; }
                }
                finally { CloseHandle(tok); }
            }
        }
        return Registry.CurrentUser;
    }
}
```

Пути вида `HKCU\Software\Policies\…` администратор записать может (стандартный пользователь — нет).

### 4.5. Применение без перезагрузки

- Значения `Explorer\Advanced` (`Start_*`, `ShowSyncProviderNotifications`), политики `Explorer`, `Dsh`, `Windows Feeds`, `WindowsCopilot` — нужен **перезапуск Проводника** (в JSON `reboot: "explorer"`). Рекомендуемый способ: завершить `explorer.exe` в сессии пользователя; при `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\AutoRestartShell=1` (дефолт) Winlogon перезапустит оболочку сам в контексте пользователя. Если через 5 с оболочки нет — запустить `explorer.exe`. ВНИМАНИЕ: при включённом UAC оболочка, запущенная из повышенного процесса, может оказаться повышенной — **НЕ ПРОВЕРЕНО**, надёжнее предложить «Выйти из системы».
- Политики `DataCollection`, `AdvertisingInfo`, `System`, `Siuf`, `ConsentStore` — читаются компонентами при обращении; перезагрузка не обязательна (`reboot: "none"`).
- Recall (`AllowRecallEnablement`) — перезагрузка обязательна.

### 4.6. Что делать НЕЛЬЗЯ (в JSON — avoid)

- Блокировать телеметрию через `hosts`/брандмауэр: Microsoft прямо указывает, что `settings-win.data.microsoft.com` нужен для удалённой конфигурации и **не должен блокироваться**, а `login.live.com` — для аутентификации устройства; блок-листы ломают Store, активацию, вход в учётную запись; Defender помечает модификацию hosts как угрозу (**НЕ ПРОВЕРЕНО** для актуальных сигнатур).
- Удалять службы телеметрии (`sc delete DiagTrack`) — необратимо (см. §2.7).
- Отключать UCPD ради `TaskbarDa`.
- Ставить `DisableWindowsConsumerFeatures` на Home/Pro как «работающий» твик — он не действует.

---

## 5. Очистка (действия, не твики)

### 5.1. Общие правила реализации

1. **Сначала замер, потом подтверждение, потом удаление.** Для каждого пункта показывать размер и количество файлов; удалять только после явного согласия пользователя (удаление необратимо).
2. **Не следовать reparse points** (junction/symlink/OneDrive-заглушки): `EnumerationOptions.AttributesToSkip = FileAttributes.ReparsePoint`. `Directory.Delete(path, recursive: true)` в .NET не рекурсирует через reparse point (удаляет саму ссылку), но при собственном обходе это нужно задать явно.
3. **Занятые файлы пропускать молча** (`IOException` c HResult `0x80070020` ERROR_SHARING_VIOLATION / `0x80070021` ERROR_LOCK_VIOLATION), считать «пропущено (занято)». `UnauthorizedAccessException` — «нет доступа».
4. **Фильтр по возрасту** для временных папок: не трогать файлы, изменённые/созданные за последние 24 ч (установщики, распаковки, наши собственные файлы). cleanmgr для «Временных файлов» использует порог 7 дней (**НЕ ПРОВЕРЕНО** точное правило).
5. **Не трогать собственные файлы приложения**: папку данных WebView2 нашего приложения (UserDataFolder), `%TEMP%\.net\<AppName>` (распаковка нативных библиотек single-file, если включено `IncludeNativeLibrariesForSelfExtract`), временные файлы установщика обновлений.
6. **Проверять процессы**: для кэшей приложений (браузеры, Discord, Steam) — только если процесс закрыт. Важно: `msedgewebview2.exe` — это WebView2 (в т.ч. наш интерфейс), а не браузер Edge; браузер Edge — `msedge.exe`.
7. **Не удалять** `C:\Windows\Installer\*` (кэш MSI и `$PatchCache$` — ломает восстановление/удаление программ), `C:\ProgramData\Package Cache` (на машине 304 МБ — кэш бандлов VC++/VS, ломает repair/uninstall), `C:\Windows\WinSxS` вручную, `SoftwareDistribution\DataStore` (история обновлений).
8. Логировать: путь, удалено файлов/байт, пропущено (занято/нет доступа/свежие).

### 5.2. Сводная таблица (замеры на этой машине 26.10.2026)

| Пункт | Путь(и) | На машине | Безопасность | Что закрыть / остановить |
|---|---|---|---|---|
| Временные файлы пользователя | `%TEMP%` (`%LOCALAPPDATA%\Temp`) | **15 947 МБ**, 37 669 файлов | безопасно с фильтром возраста | ничего; занятые пропускаются |
| Временные файлы Windows | `C:\Windows\Temp` (+ `C:\Windows\SystemTemp` на Win11 — пусть чистит cleanmgr) | 12,5 МБ | безопасно с фильтром возраста | — |
| Prefetch | `C:\Windows\Prefetch` | 12,9 МБ, 497 файлов | **не чистить** | — |
| Кэш загрузок Windows Update | `C:\Windows\SoftwareDistribution\Download` | 156 МБ | безопасно при остановленных wuauserv/BITS и без ожидающей перезагрузки | остановить `wuauserv`, `bits` |
| Кэш оптимизации доставки | `C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache` | 0 байт (`Get-DeliveryOptimizationPerfSnap` → CacheSizeBytes=0) | безопасно | через `Delete-DeliveryOptimizationCache -Force` |
| NVIDIA DX/GL кэш шейдеров | `%LOCALAPPDATA%\NVIDIA\DXCache`, `%LOCALAPPDATA%\NVIDIA\GLCache`, `%USERPROFILE%\AppData\LocalLow\NVIDIA\DXCache` (на новых драйверах также `LocalLow\NVIDIA\PerDriverVersion\…`) | 3 080 МБ + 136 МБ + 363 МБ | безопасно, но **вызывает статтеры** до перекомпиляции | закрыть игры и все 3D-приложения |
| NVIDIA NV_Cache (устар.) | `%LOCALAPPDATA%\NVIDIA Corporation\NV_Cache`, `%PROGRAMDATA%\NVIDIA Corporation\NV_Cache` | нет | безопасно | закрыть CUDA/3D-приложения |
| DirectX кэш шейдеров | `%LOCALAPPDATA%\D3DSCache` | 1,7 МБ | безопасно, но бессмысленно | закрыть игры |
| AMD кэш шейдеров | `%LOCALAPPDATA%\AMD\DxCache`, `DxcCache`, `VkCache`, `GLCache` | нет (NVIDIA) | безопасно; официальный путь — «Сбросить кэш шейдеров» в Adrenalin | закрыть игры |
| Steam shadercache | `<библиотека>\steamapps\shadercache\<appid>` | 0,3 МБ (1 библиотека) | безопасно, вызывает перекомпиляцию/перекачку | закрыть Steam (`steam.exe`) |
| Дампы ядра | `C:\Windows\MEMORY.DMP`, `C:\Windows\Minidump\*.dmp`, `C:\Windows\LiveKernelReports\**` | 1 413 МБ; 4,8 МБ (4 файла); **1 549 МБ** | безопасно, но теряется диагностика BSOD/TDR | — |
| Дампы приложений | `%LOCALAPPDATA%\CrashDumps` | 77 МБ | безопасно | — |
| Журналы обслуживания | `C:\Windows\Logs\CBS` (`CbsPersist_*.cab/.log`), `C:\Windows\Logs\DISM` | 20 МБ; 2,4 МБ | безопасно для старых; `CBS.log`/`dism.log` могут быть заняты | не во время установки обновлений |
| WER | `C:\ProgramData\Microsoft\Windows\WER\ReportArchive`, `ReportQueue`, `Temp` | 0,1 / 7,8 / 0,5 МБ | безопасно | — |
| Корзина | все тома | ~0 | необратимо | — |
| Компонентное хранилище | WinSxS через DISM | «Backups and Disabled Features: 7.23 GB», «Cleanup Recommended: Yes» | безопасно (`/StartComponentCleanup`), `/ResetBase` — необратимо | без ожидающей перезагрузки |
| Браузеры | Chrome `…\User Data\<Profile>\Cache`, Edge, Яндекс | 377 / 19 / 97 МБ | безопасно (только кэш) | закрыть браузер |
| Discord | `%APPDATA%\discord\Cache` (`Cache_Data`), `Code Cache`, `GPUCache`, `DawnGraphiteCache`, `DawnWebGPUCache` | 57 + 1,7 + 0,5 МБ | безопасно | закрыть `Discord.exe` |
| Миниатюры | `%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db` (и `iconcache_*.db`) | 78 МБ (15 файлов) | безопасно | файлы держит `explorer.exe` |

### 5.3. Подробно по пунктам

**%TEMP% и C:\Windows\Temp.** Перечислять файлы с `IgnoreInaccessible=true`, `AttributesToSkip=ReparsePoint`, удалять файлы старше 24 ч (по `max(LastWriteTimeUtc, CreationTimeUtc)`), сбрасывать `ReadOnly`; затем удалять пустые подпапки снизу вверх (кроме корня). Занятые — пропуск. Именно здесь основной выигрыш (15,9 ГБ на этой машине).

**Prefetch — НЕ чистить.** Файлы `.pf` создаёт SysMain/префетчер для ускорения запуска программ и загрузки; Windows сама ограничивает их количество и перезаписывает; после очистки загрузка и первый запуск программ медленнее, пока данные не накопятся заново. Объём ничтожен (12,9 МБ на этой машине). Если SysMain отключён, папка просто перестаёт пополняться (на этой машине последние `.pf` от 02.09.2026). В каталоге — `cleanup.prefetch-wipe` (avoid).

**SoftwareDistribution\Download.**
1. Отказаться, если ожидается перезагрузка: существует `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired` или `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending`, либо запущен `TrustedInstaller`/идёт установка.
2. Запомнить, какие службы работали; остановить `wuauserv`, затем `bits` (Microsoft в процедуре сброса: `net stop bits`, `net stop wuauserv`, `net stop cryptsvc`; для очистки кэша загрузок `cryptsvc` останавливать не нужно).
3. Удалить **содержимое** `C:\Windows\SoftwareDistribution\Download` (не саму папку, не `DataStore` — там история обновлений; полный `rd SoftwareDistribution` Microsoft даёт только как крайнюю меру сброса агента).
4. Запустить ранее работавшие службы (обе — «Вручную (по триггеру)», Windows всё равно поднимет их по требованию).

**Delivery Optimization.** Размер: `Get-DeliveryOptimizationPerfSnap` (поле `CacheSizeBytes`). Очистка: `powershell.exe -NoProfile -NonInteractive -Command "Delete-DeliveryOptimizationCache -Force"` (модуль `DeliveryOptimization`, проверено наличие на этой машине) или обработчик cleanmgr «Delivery Optimization Files». Ручное удаление файлов в профиле NetworkService не рекомендуется.

**Кэши шейдеров (NVIDIA / DirectX / AMD / Steam).** Это **не оптимизация**: кэш нужен, чтобы игра не компилировала шейдеры заново (именно компиляция даёт статтеры). Удалять только как действие «устранение неполадок» (после смены драйвера, при артефактах/вылетах, нехватке места) с предупреждением «первые запуски игр будут с подтормаживаниями». Драйвер NVIDIA сам ограничивает размер кэша (параметр «Размер кэша шейдеров» в Панели управления NVIDIA; точное значение «по умолчанию драйвера» — **НЕ ПРОВЕРЕНО**). Требует закрыть игры/3D-приложения; занятые `.nvph/.toc` пропускать. AMD — предпочтительно через Adrenalin (Графика → Дополнительно → «Сбросить кэш шейдеров»). D3DSCache управляется Windows (обработчик cleanmgr «D3D Shader Cache»; PC-Tuning рекомендует его **не** отмечать). Steam: путь библиотеки — из `HKCU\Software\Valve\Steam\SteamPath` → `steamapps\libraryfolders.vdf` (поля `"path"`) → `<path>\steamapps\shadercache\<appid>`; удалять при закрытом Steam; Steam может заново скачать предкомпилированные шейдеры (Shader Pre-Caching) или «Обработать шейдеры Vulkan» при запуске.

**Дампы и LiveKernelReports.** `MEMORY.DMP` (полный/ядерный дамп BSOD), `Minidump\*.dmp`, `LiveKernelReports\` (живые дампы ядра, в т.ч. `WATCHDOG` — тайм-ауты GPU/TDR). Безопасно удалять, но это единственные данные для разбора BSOD и зависаний видеодрайвера. Рекомендация UI: показывать дату последнего дампа; по умолчанию оставлять минидампы за последние 30 дней. Обработчики cleanmgr: «System error memory dump files», «System error minidump files».

**Журналы CBS/DISM.** Удалять `CbsPersist_*.log/.cab` и прочие файлы, кроме занятых; `CBS.log` и `dism.log` во время обслуживания заняты TrustedInstaller/DISM — пропуск.

**WER.** `ReportArchive`, `ReportQueue`, `Temp` — содержимое безопасно удалить (обработчик cleanmgr «Windows Error Reporting Files»).

**Корзина.** Размер: `SHQueryRecycleBinW(null, ref info)`; очистка: `SHEmptyRecycleBinW(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND)`. Вызывать из STA-потока. Очищается корзина текущего пользователя на всех томах. При пустой корзине функция может вернуть ошибку (`E_UNEXPECTED`) — считать успехом (**НЕ ПРОВЕРЕНО**).

**DISM (компонентное хранилище).** Анализ: `dism.exe /English /Online /Cleanup-Image /AnalyzeComponentStore` (без `/English` вывод в OEM-кодировке cp866 и по-русски — на этой машине нечитаем; `/English` проверен). Очистка: `dism.exe /English /Online /Cleanup-Image /StartComponentCleanup` — удаляет заменённые версии компонентов сразу (без 30-дневной задержки задачи). `/ResetBase` — только отдельной опцией с предупреждением: «установленные обновления станет невозможно удалить». Коды: 0 — успех, 3010 — успех, нужна перезагрузка; ошибка `0x800F0806` (CBS_E_PENDING) — есть ожидающие операции, сначала перезагрузка. Длительность — от минут до десятков минут; показывать прогресс по stdout. Не запускать параллельно с Windows Update.

**Кэши браузеров и Discord.** Удалять только каталоги кэша: `Cache` (`Cache_Data`), `Code Cache`, `GPUCache`, `ShaderCache`/`GrShaderCache`/`GraphiteDawnCache` (Chromium), Firefox `%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\cache2`. **Никогда**: `Cookies`, `Network\Cookies`, `Local Storage`, `IndexedDB`, `Login Data`, `Web Data`, `Session Storage` (разлогинит/потеряет данные). Профили Chromium: `Default`, `Profile 1…N`. Процессы: `chrome.exe`, `msedge.exe`, `browser.exe` (Яндекс), `firefox.exe`, `opera.exe`; Discord — `Discord.exe`, `DiscordPTB.exe`, `DiscordCanary.exe` (папки `discord`, `discordptb`, `discordcanary`).

**Миниатюры.** `thumbcache_*.db` заняты `explorer.exe`. Варианты: (а) обработчик cleanmgr «Thumbnail Cache» (рекомендуется), (б) завершить Проводник → удалить → перезапустить. Эффект — только место; миниатюры пересоздадутся.

### 5.4. Код C# для очистки

```csharp
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics;

public sealed class CleanStats { public long Bytes; public int Deleted, Locked, Denied, SkippedRecent; }

public static class Cleaner
{
    static readonly EnumerationOptions Opts = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint, // не входить в junction/symlink, пропускать облачные заглушки
        ReturnSpecialDirectories = false
    };

    public static (long bytes, int files) Measure(string root)
    {
        if (!Directory.Exists(root)) return (0, 0);
        long total = 0; int n = 0;
        foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", Opts))
        { try { total += f.Length; n++; } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        return (total, n);
    }

    public static CleanStats DeleteContents(string root, TimeSpan minAge, Func<string, bool>? exclude = null)
    {
        var st = new CleanStats();
        if (!Directory.Exists(root)) return st;
        DateTime cutoff = DateTime.UtcNow - minAge;

        foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", Opts))
        {
            if (exclude?.Invoke(f.FullName) == true) continue;
            try
            {
                DateTime newest = f.LastWriteTimeUtc > f.CreationTimeUtc ? f.LastWriteTimeUtc : f.CreationTimeUtc;
                if (newest > cutoff) { st.SkippedRecent++; continue; }
                if (f.Attributes.HasFlag(FileAttributes.ReadOnly)) f.Attributes &= ~FileAttributes.ReadOnly;
                long len = f.Length;
                f.Delete();
                st.Deleted++; st.Bytes += len;
            }
            catch (IOException) { st.Locked++; }                 // 0x80070020 / 0x80070021 — файл занят
            catch (UnauthorizedAccessException) { st.Denied++; }
        }

        // пустые папки снизу вверх; корень не удаляем
        foreach (var d in new DirectoryInfo(root).EnumerateDirectories("*", Opts)
                                                 .OrderByDescending(x => x.FullName.Length))
        {
            if (exclude?.Invoke(d.FullName) == true) continue;
            try { if (!d.EnumerateFileSystemInfos().Any()) d.Delete(false); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return st;
    }

    public static bool IsRunning(params string[] processNames) =>
        processNames.Any(n => Process.GetProcessesByName(n).Length > 0);   // без ".exe"
}

public static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential)]                 // x64: cbSize(4)+pad(4)+8+8 = 24 байта
    struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHQueryRecycleBinW(string? pszRootPath, ref SHQUERYRBINFO info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    const uint SHERB_NOCONFIRMATION = 0x1, SHERB_NOPROGRESSUI = 0x2, SHERB_NOSOUND = 0x4;

    public static (long bytes, long items) Query()
    {
        var i = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBinW(null, ref i) == 0 ? (i.i64Size, i.i64NumItems) : (0, 0);
    }

    public static int Empty() =>
        SHEmptyRecycleBinW(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
}

public static class Dism
{
    public static int Run(string args, Action<string> onLine)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "dism.exe"),
                                       "/English " + args)   // напр. "/Online /Cleanup-Image /StartComponentCleanup"
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        p.OutputDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };
        p.ErrorDataReceived  += (_, e) => { if (e.Data != null) onLine(e.Data); };
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        p.WaitForExit();
        return p.ExitCode;   // 0 ок, 3010 ок+перезагрузка; иначе ошибка (0x800F0806 — ожидается перезагрузка)
    }
}
```

### 5.5. Автоматизация cleanmgr (опционально)

Обработчики: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\<Handler>`. Для профиля N записать DWORD `StateFlagsNNNN = 2` (выбран) в нужные обработчики и запустить `cleanmgr.exe /sagerun:NNNN`. Список на этой машине (23H2): `Active Setup Temp Folders`, `BranchCache`, `Content Indexer Cleaner`, `D3D Shader Cache`, `Delivery Optimization Files`, `Device Driver Packages`, `Diagnostic Data Viewer database files`, `Downloaded Program Files`, **`DownloadsFolder` (папка «Загрузки» — НИКОГДА не выбирать)**, `Feedback Hub Archive log files`, `Internet Cache Files`, `Language Pack`, `Offline Pages Files`, `Old ChkDsk Files`, `Previous Installations`, `Recycle Bin`, `RetailDemo Offline Content`, `Setup Log Files`, `System error memory dump files`, `System error minidump files`, `Temporary Files` (папки: `%TEMP%`, `C:\Windows\Temp`, `C:\Windows\Logs`, `C:\Windows\System32\LogFiles`, `C:\Windows\SystemTemp`, `C:\ProgramData\Microsoft\Windows\HiveUpload`), `Temporary Setup Files`, `Thumbnail Cache`, `Update Cleanup`, `Upgrade Discarded Files`, `User file versions`, `Windows Defender`, `Windows Error Reporting Files`, `Windows ESD installation files`, `Windows Reset Log Files`, `Windows Upgrade Log Files`. Не выбирать также `D3D Shader Cache` (см. выше), `Previous Installations` (удалит возможность отката обновления функций — только с явным подтверждением), `Language Pack`, `Device Driver Packages` (откат драйверов). Запись `StateFlags` — изменение реестра; после выполнения удалять свои `StateFlagsNNNN`, чтобы не оставлять следов.

---

## 6. Соответствие JSON-каталогу

| id | category | evidence | Коротко |
|---|---|---|---|
| services.diagtrack-disable | services | proven | DiagTrack → Disabled |
| services.dmwappush-disable | services | situational | без MDM |
| services.sysmain-disable | services | situational | только SSD + ≥16 ГБ |
| services.wsearch-disable | services | situational | без поиска по содержимому |
| services.spooler-disable | services | situational | без принтера |
| services.mapsbroker-manual | services | situational | без офлайн-карт |
| services.xbox-live-disable | services | situational | без Game Pass/Xbox/Store-игр |
| services.lfsvc-disable | services | situational | без функций расположения |
| services.dps-disable | services | experimental | SRUM/диагностика |
| services.pcasvc-disable / services.pcasvc-disable-win10 | services | experimental | разные дефолты Win11/Win10 |
| services.trkwks-manual | services | experimental | |
| services.wersvc-disable | services | experimental | лучше политика WER |
| services.xboxgip-disable, services.demand-start-noise, services.anticheat-never-touch, services.text-input-never-touch, services.core-never-touch, services.waasmedic-disable, services.wuauserv-disable, services.bits-disable, services.dosvc-disable, services.defender-disable, services.wpnservice-disable, services.delete-services | services | avoid | защита от вредных твиков |
| tasks.compat-appraiser-off, tasks.ceip-off, tasks.feedback-siuf-off | tasks | proven | |
| tasks.maps-off, tasks.family-safety-off, tasks.wer-queue-off | tasks | situational | |
| tasks.device-census-off, tasks.winsat-off, tasks.power-efficiency-off, tasks.cloudexperiencehost-off, tasks.diskfootprint-off, tasks.startupapp-off | tasks | experimental | |
| tasks.pcapatchdb-off, tasks.scheduled-defrag-off, tasks.component-cleanup-off, tasks.update-orchestrator-off, tasks.ucpd-velocity-off, tasks.defender-tasks-off, tasks.system-restore-off | tasks | avoid | |
| privacy.telemetry-level (select), privacy.feedback-off, privacy.advertising-id-off, privacy.activity-history-off, privacy.tailored-experiences-off, privacy.start-web-search-off, privacy.widgets-off, privacy.news-interests-off, privacy.suggestions-ads-off, privacy.cortana-off, privacy.inking-typing-off, privacy.general-tracking-off | privacy | proven | |
| privacy.diagnostic-limits, privacy.copilot-off, privacy.recall-off, privacy.location-off, privacy.delivery-optimization-p2p-off, privacy.wer-off, privacy.consumer-features-policy | privacy | situational | |
| privacy.appcompat-telemetry-off, privacy.ceip-legacy-off | privacy | experimental | |
| privacy.hosts-block-telemetry, privacy.ucpd-disable-taskbar | privacy | avoid | |
| cleanup.prefetch-wipe, cleanup.shader-cache-periodic, cleanup.winsxs-manual-delete, cleanup.resetbase-default, cleanup.softwaredistribution-full-delete | cleanup | avoid | защита от вредных «оптимизаций» |

Правила интерпретации JSON (для разработчиков):
- `apply` — применить рекомендуемое; `revert` — вернуть дефолт Windows. Для `op: "delete"` поле `value` опущено.
- Детект «применено» = все действия `apply`, **применимые на этой системе**, совпадают с текущим состоянием. Отсутствующая служба/задача = «неприменимо» (не учитывается; если неприменимы все — твик скрыть или показать «нет в системе»).
- Для `service`: сравнивать только `startType` (`stop` при детекте игнорировать). `AutomaticDelayed` ≡ Start=2 и DelayedAutostart=1.
- Для `reg`, `op:"set"`: значение должно существовать и совпадать по типу и данным; для `op:"delete"`: значение отсутствует.
- HKCU-действия выполнять в кусте интерактивного пользователя (§4.4).
- Записи `avoid` имеют пустые `apply`/`revert` и не должны отображаться как переключатели — только как справка («почему мы этого не делаем»).

---

## 7. НЕ ПРОВЕРЕНО / открытые вопросы

1. Дефолты служб для Win11 24H2/25H2 — не проверены на отдельной машине (использованы снимки 23H2 + Brink; для служб каталога изменений не обнаружено в источниках).
2. Win10 22H2: наличие задач `ProgramDataUpdater`, `KernelCeipTask`, `Microsoft Compatibility Appraiser Exp`, действие Appraiser (CompatTelRunner напрямую или через InventorySvc) — нет машины Win10.
3. Состояние задач по умолчанию на стоковой системе (особенно `DiskDiagnosticDataCollector`, `KernelCeipTask`) — на этой машине задачи удалены; revert = `enabled: true` по источникам.
4. Может ли администратор менять `Enabled` у всех задач каталога (ACL задач) — запись не выполнялась (правило «только чтение»).
5. `ServiceController.Stop(bool stopDependentServices)` — перегрузка .NET 7+; проверить на целевом SDK .NET 10.
6. Механизм связи `EnableActivityFeed` и журнала буфера обмена (утверждение winutil) — не проверен.
7. Влияние отключения `Device Information\Device` (DeviceCensus) на подбор драйверов/обновлений — не проверено.
8. `InventorySvc` — нет официальной документации; не включён в каталог.
9. Поведение `SHEmptyRecycleBinW` при пустой корзине (код возврата) — не проверено.
10. Перезапуск Проводника из повышенного процесса при включённом UAC (на этой машине UAC выключен, проверить нельзя).
11. Работает ли поиск приложений в Пуске Win11 при отключённой WSearch (на этой машине WSearch отключена, но UI-проверка не проводилась).
12. Точный дефолт «Размер кэша шейдеров» NVIDIA и пути AMD-кэшей на актуальных Adrenalin (нет AMD GPU).
13. Политики `AITEnable`/`DisableInventory` — актуальность на 23H2+.
14. Блокировка hosts Защитником — актуальность сигнатур.
15. Copilot-политики в регионах, где Copilot недоступен (Россия) — твик вероятно не нужен; проверить на машине из поддерживаемого региона.

---

## 8. Источники

Microsoft (первоисточники):
- Configure Windows diagnostic data in your organization — https://learn.microsoft.com/en-us/windows/privacy/configure-windows-diagnostic-data-in-your-organization
- Policy CSP – System (AllowTelemetry: 0 только Enterprise/Education/Server; «on other devices is equivalent to setting the value of 1») — https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-system
- Manage connections from Windows OS components to Microsoft services — https://learn.microsoft.com/en-us/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services
- Policy CSP – Experience (редакции CloudContent-политик) — https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-experience
- Policy CSP – WindowsAI (Recall, TurnOffWindowsCopilot, RemoveMicrosoftCopilotApp) — https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai
- Policy CSP – NewsAndInterests (AllowNewsAndInterests) — https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-newsandinterests
- Delivery Optimization reference — https://learn.microsoft.com/en-us/windows/deployment/do/waas-delivery-optimization-reference
- Security guidelines for system services in Windows Server 2016 — https://learn.microsoft.com/en-us/windows-server/security/windows-services/security-guidelines-for-disabling-system-services-in-windows-server
- How to set up a Device for Real-Time Performance (SysMain, DPS) — https://learn.microsoft.com/en-us/windows/iot/iot-enterprise/soft-real-time/soft-real-time-device
- Per-user services — https://learn.microsoft.com/en-us/windows/application-management/per-user-services-in-windows
- Protecting anti-malware services (защищённые службы) — https://learn.microsoft.com/en-us/windows/win32/services/protecting-anti-malware-services-
- ChangeServiceConfigW — https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-changeserviceconfigw
- ChangeServiceConfig2W — https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-changeserviceconfig2w
- SERVICE_DELAYED_AUTO_START_INFO — https://learn.microsoft.com/en-us/windows/win32/api/winsvc/ns-winsvc-service_delayed_auto_start_info
- sc config — https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sc-config
- IRegisteredTask::put_Enabled — https://learn.microsoft.com/en-us/windows/win32/api/taskschd/nf-taskschd-iregisteredtask-put_enabled
- schtasks change — https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/schtasks-change
- defrag (оптимизация SSD, retrim, scheduled task) — https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/defrag
- Clean Up the WinSxS Folder — https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/clean-up-the-winsxs-folder?view=windows-11
- DISM global options (/English) — https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism-global-options-for-command-line-syntax
- Additional resources for Windows Update (сброс компонентов) — https://learn.microsoft.com/en-us/troubleshoot/windows-client/installing-updates-features-roles/additional-resources-for-windows-update
- Delete-DeliveryOptimizationCache — https://learn.microsoft.com/en-us/powershell/module/deliveryoptimization/delete-deliveryoptimizationcache
- SHEmptyRecycleBinW — https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shemptyrecyclebinw
- SHQueryRecycleBinW — https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shqueryrecyclebinw
- Directory.Delete (reparse points) — https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.delete
- EnumerationOptions — https://learn.microsoft.com/en-us/dotnet/api/system.io.enumerationoptions
- MSRC CVE-2021-34527 (PrintNightmare, отключение Spooler) — https://msrc.microsoft.com/update-guide/vulnerability/CVE-2021-34527
- Windows Blog Japan, «そのサービスを止めないで!» (TabletInputService) — https://blogs.windows.com/japan/2020/10/19/dont-stop-the-service-or-how-to-disable-touch-keyboard-correctly/
- Microsoft Virtual Desktop Optimization Tool (VDI-контекст!) — https://github.com/The-Virtual-Desktop-Team/Virtual-Desktop-Optimization-Tool (файлы `2009/ConfigurationFiles/ScheduledTasks.json`, `Services.json`)
- Microsoft Q&A: InventorySvc — https://learn.microsoft.com/fr-fr/answers/questions/3876932/service-inventorysvc

Снимки дефолтов и сообщество:
- winhelponline: Windows 11 Default Services Configuration and Permissions (Win11 Pro 23H2, clean install) — https://www.winhelponline.com/blog/windows-11-default-services-configuration/
- winhelponline: Windows 10 Default Services Configuration (Win10 Pro 22H2, clean install) — https://www.winhelponline.com/blog/windows-10-default-services-configuration/
- ElevenForum (Shawn Brink): Restore Default Services in Windows 11 — https://www.elevenforum.com/t/restore-default-services-in-windows-11.3109/
- ElevenForum: Disable Ads in Windows 11 (значения ContentDeliveryManager и дефолты) — https://www.elevenforum.com/t/disable-ads-in-windows-11.8004/
- ElevenForum: Suggested Content in Settings — https://www.elevenforum.com/t/enable-or-disable-suggested-content-in-settings-in-windows-11.3791/
- ElevenForum: Bing web search results — https://www.elevenforum.com/t/enable-or-disable-bing-web-suggested-search-results-in-windows-search-on-windows-11.5309/
- ElevenForum: Widgets button / TaskbarDa — https://www.elevenforum.com/t/add-or-remove-widgets-button-on-taskbar-in-windows-11.32/
- oofhours: What is Windows 11’s new UCPD feature — https://oofhours.com/2025/05/02/what-is-windows-11s-new-ucpd-feature/
- NTLite forum: Access denied for TaskbarDa (UCPD) — https://ntlite.com/community/threads/error-5-access-is-denied-when-disabling-set-news-and-interests-taskbar-widget.4852/
- valleyofdoom/PC-Tuning (README: Search Indexing, Superfetch/Prefetch, Scheduled Tasks, Cleanup) — https://github.com/valleyofdoom/PC-Tuning
- valleyofdoom/PC-Tuning registry options — https://github.com/valleyofdoom/PC-Tuning/blob/main/docs/registry-opts.md
- ChrisTitusTech/winutil tweaks.json (актуальный, тег 26.10.07) — https://github.com/ChrisTitusTech/winutil/blob/main/config/tweaks.json
- ChrisTitusTech/winutil tweaks.json (25.05.23, полный список служб с OriginalType) — https://github.com/ChrisTitusTech/winutil/blob/25.05.23/config/tweaks.json
- winutil issue #1733 (XboxGipSvc зависает после твика) — https://github.com/ChrisTitusTech/winutil/issues/1733
- AMD: Customize Graphics Settings (Reset Shader Cache) — https://www.amd.com/en/resources/support-articles/faqs/DH3-012.html
- Gaijin support: How to clear NVIDIA and DirectX cache — https://support.gaijin.net/hc/en-us/articles/30268449678994-How-to-clear-NVIDIA-and-DirectX-cache
- Kkthnx/NvidiaShaderCleanup (пути LocalLow\NVIDIA\PerDriverVersion) — https://github.com/Kkthnx/NvidiaShaderCleanup
- Ed Bott: Do not clean out your Prefetch folder — https://edbott.com/2005/06/01/one-more-time-do-not-clean-out-your-prefetch-folder/
- Wikipedia: Prefetcher — https://en.wikipedia.org/wiki/Prefetcher
- ExitLag blog: Vanguard errors (VAN 57 — отключённая служба vgc; сторонний источник) — https://www.exitlag.com/blog/league-of-legends-vanguard-errors/
