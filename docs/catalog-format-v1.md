# Формат каталога твиков, версия 1

Статус: принят 2026-10-26. Проверяется скриптом `tools/catalog/validate.mjs`.

Описывает файлы `*.tweaks.json`: исследовательские в `docs/research/` и будущие продуктовые в `src/Hateeze.Core/Catalog/`. Документ закрывает расхождения между каталогами исследований и схемой `TweakDef`/`TweakAction` из плана (Task 3). Если план и этот документ расходятся, верен этот документ. План нужно поправить при выполнении Task 3.

## 1. Файл

- UTF-8 без BOM. Корень — массив объектов-твиков.
- Обёртки с номером версии нет. Каталог вшит в exe, поэтому его версия совпадает с версией приложения. Версию получают только пресеты (`.hateeze.json`), которые живут отдельно от exe.
- `id` уникален во всех файлах сразу, а не только в своём.

## 2. Твик

| Поле | Тип | Обязательно | Правило |
|---|---|---|---|
| `id` | string | да | `<category>.<slug>`, `^[a-z][a-z0-9]*\.[a-z0-9][a-z0-9-]*$`; префикс равен `category` |
| `category` | string | да | `power`, `cpu`, `timer`, `scheduler`, `input`, `network`, `gaming`, `graphics`, `nvidia`, `amd`, `services`, `tasks`, `privacy`, `cleanup` |
| `title`, `description` | string | да | непустые, по-русски |
| `recommendation`, `evidenceNote`, `riskNote`, `osNote` | string \| null | нет | — |
| `evidence` | enum | да | `proven`, `situational`, `experimental`, `avoid` |
| `risk` | enum | да | `low`, `medium`, `high` |
| `reboot` | enum | да | `none`, `explorer`, `logoff`, `reboot` |
| `os` | string[] | да | непустой, элементы из `win10`, `win11` |
| `vendor` | enum | да | `any`, `nvidia`, `amd`, `intel` |
| `kind` | enum | да | `toggle`, `value`, `select`, `info` |
| `options` | см. §3 | для `value`/`select` | — |
| `apply`, `revert` | Action[] | да | см. §4 и §6 |
| `detect` | Detect \| null | нет | см. §5 |
| `sources` | string[] | да | для не-`avoid` — хотя бы один URL `http(s)://` |

Других полей нет. Неизвестное поле считается ошибкой: опечатка в имени поля молча выключила бы проверку.

**Новое по сравнению с исследованиями:** `kind: "info"` — карточка-рекомендация без действий (BIOS, X3D, функции, которые делает код приложения). У неё `apply` и `revert` пустые, переключателя в интерфейсе нет.

## 3. `options`

- `select` — массив из двух и более `{ "label": string, "value": number | string }`. Тип `value` одинаковый у всех вариантов.
- `value` — объект `{ "min": number, "max": number, "default": number }`, `min ≤ default ≤ max`. В C# из него строится `Range`. Отдельного поля `range` в файле нет.
- `toggle`, `info` — без `options`.

Строковый ввод, например имя exe для IFEO, — не твик каталога. Это динамическое действие раздела (`ifeo`, Task 12).

## 4. Действия

Каждое действие — объект с полем `type`. Другие поля зависят от типа, лишние поля — ошибка. Для каждой цели вычисляется **ключ цели**: по нему движок ведёт baseline и журнал.

| `type` | Поля | Ключ цели | Baseline |
|---|---|---|---|
| `reg` | `hive` (`HKLM`\|`HKCU`), `path`, `name`, `valueType` (`DWORD`, `QWORD`, `SZ`, `EXPAND_SZ`, `BINARY`, `MULTI_SZ`), `op` (`set`\|`delete`), `value` | `reg:<hive>\<path>\<name>` | да |
| `bcd` | `op` (`set`\|`deletevalue`), `name`, `value` (только для `set`, string) | `bcd:<name>` | да |
| `service` | `name`, `startType` (`Boot`, `System`, `Automatic`, `AutomaticDelayed`, `Manual`, `Disabled`), `stop`? (bool) | `svc:<name>` | да |
| `task` | `path` (начинается с `\`), `enabled` (bool) | `task:<path>` | да |
| `power` | `subgroup`, `setting` (GUID), `ac`, `dc` (uint32), `alias`? (string, справочно) | `power:<схема>/<subgroup>/<setting>` | да |
| `nic` | `keyword`; `value` (string) или `op: "reset"` без `value` — сброс к значению драйвера | `nic:<GUID адаптера>/<keyword>` | да |
| `nvdrs` | `settingId` (uint32); `value` (uint32) или `op: "restoreDefault"` без `value` | `nvdrs:global/<settingId>` | да |
| `powershell` | `script` (непустой) | — | **нет** |

Уточнения:
- **`reg`.** `path` указывается без куста и без ведущего `\`. `op: "delete"` удаляет значение, а не ключ; `value` при этом отсутствует или `null`. Для `set` значение обязательно: `DWORD` — целое 0…4294967295; `QWORD` — целое ≥ 0 (числа больше 2^53 записываются десятичной строкой); `SZ`/`EXPAND_SZ` — строка; `BINARY` — hex-строка чётной длины; `MULTI_SZ` — массив строк.
- **`HKCU` = куст интерактивного пользователя** (`HKEY_USERS\<SID>` владельца сессии), а не куст учётной записи, от которой запущен повышенный процесс. Это разные кусты при UAC с паролем другого администратора. Решение — в `WindowsRegistry`: интерфейс `IRegistry` из плана не меняется. Код определения SID — в `docs/research/gaming-graphics-amdgpu.md` §3.1.
- **`power`** пишет в активную схему, если не задана `scheme` (появится в Task 10).
- **`nic`** раскрывается движком на все физические адаптеры, у которых есть `keyword`. Адаптер без такого свойства пропускается. Если свойства нет ни у одного адаптера, твик получает статус «не найдено». `op: "reset"` возвращает значение по умолчанию драйвера (аналог `Reset-NetAdapterAdvancedProperty`): дефолт свой у каждого драйвера, поэтому числом в каталоге его не записать.
- **`service`/`task`.** Отсутствие службы или задачи — статус «не найдено», а не ошибка. Пакет при этом продолжает выполняться.
- **`nvdrs`.** Все действия пакета выполняются в одной DRS-сессии с одним `Save()` (подробности — в `docs/research/nvidia-drs-tweaks.md` §2.6).
- **`powershell`** — крайний случай. Скрипт выполняется только из каталога, вшитого в exe. Перед запуском `HKCU:\` в скрипте заменяется на `Registry::HKEY_USERS\<SID>\`, поэтому скрипт обращается к HKCU только через `HKCU:\`. Для таких твиков движок не может снять baseline: откат выполняет `revert`-скрипт, то есть возвращает дефолт Windows.

Тип действия в каталоге — только один из восьми выше. Динамические действия разделов (`msi`, `affinity`, `ifeo`, `powerScheme`) передаются в `TweakEngine.ApplyActions` из кода раздела и в файлах каталога недопустимы.

## 5. Плейсхолдеры и `detect`

Плейсхолдеры:
- `{{value}}` — выбранное значение. Только для `kind: value|select`; встречается в `apply`, может встречаться в `revert`. Если у `value`/`select` нет `{{value}}` в `apply`, выбор пользователя ни на что не влияет — это ошибка.
- `{{nvidiaGpuIndex}}` — только в `reg.path`. Движок раскрывает действие на каждый подключ `0000`…`9999` класса дисплеев `{4d36e968-e325-11ce-bfc1-08002be10318}` с NVIDIA (`ProviderName`). Ключ цели строится для каждого подключа. Твик применён, если совпали все подключи.

Другие плейсхолдеры недопустимы.

`detect`:
- `null` — твик применён, если каждое `apply`-действие совпадает с текущим состоянием.
- `{ "type": "reg", …поля reg без op/value или с ними…, "equals": <значение> }` — текущее значение цели сравнивается с `equals`.
- `{ "type": "powershell", "script": string, "equals": string }` — stdout скрипта без пробелов по краям сравнивается с `equals` без учёта регистра. Скрипт detect только читает систему.

## 6. Откат

1. Цель, для которой есть baseline, возвращается к baseline — значению до первого касания hateeze. Отсутствовавшее значение удаляется.
2. Если baseline для цели нет, а также для `powershell` выполняются действия `revert` из каталога. Они возвращают дефолт Windows.
3. `revert` должен покрывать все цели `apply` (для типов с ключом цели). Иначе откат без baseline оставит часть изменений.

Правила `apply`/`revert` по видам:
- `toggle`, `value`, `select` с `evidence ≠ avoid` — непустые `apply` и `revert`.
- `info` — оба пустые.
- `avoid` — никогда не выполняется и не показывается. Действия могут быть пустыми или заполненными справочно; их структура всё равно проверяется.

## 7. Запрещённые цели

Не-`avoid` твик не может трогать службу из списка ниже — ни действием `service`, ни через `reg` по пути `SYSTEM\CurrentControlSet\Services\<имя>`. Список собран из ограничений плана и `docs/research/services-tasks-privacy-cleanup.md` §2.3. Его копия будет в C#-классе `SafetyGuard`; обе копии меняются вместе.

- Античиты: `vgc`, `vgk`, `FACEIT`, `FACEITService`, `EasyAntiCheat`, `EasyAntiCheat_EOS`, `BEService`, `BEDaisy`.
- Ввод текста: `TabletInputService`, `TextInputManagementService`.
- Сеть и безопасность: `BFE`, `mpssvc`, `Dhcp`, `Dnscache`, `nsi`, `NlaSvc`, `netprofm`, `Wcmsvc`, `iphlpsvc`, `WinHttpAutoProxySvc`, `CryptSvc`, `KeyIso`.
- Ядро: `RpcSs`, `RpcEptMapper`, `DcomLaunch`, `Winmgmt`, `EventLog`, `Schedule`, `ProfSvc`, `Power`, `PlugPlay`, `BrokerInfrastructure`, `SystemEventsBroker`, `TimeBrokerSvc`, `CoreMessagingRegistrar`, `StateRepository`, `LSM`, `SamSs`, `gpsvc`, `UserManager`, `Themes`, `FontCache`, `DispBrokerDesktopSvc`.
- Звук и ввод: `Audiosrv`, `AudioEndpointBuilder`, `hidserv`, `MMCSS`, `GameInputSvc`.
- Store и лицензии: `AppXSvc`, `ClipSVC`, `InstallService`, `LicenseManager`, `wlidsvc`, `TokenBroker`, `GamingServices`, `GamingServicesNet`.
- Обновления: `wuauserv`, `UsoSvc`, `BITS`, `WaaSMedicSvc`, `DoSvc`, `TrustedInstaller`.
- Защитник: `WinDefend`, `WdNisSvc`, `SecurityHealthService`, `wscsvc`, `Sense`, `MDCoreSvc`, `webthreatdefsvc`.
- Уведомления: `WpnService`, `WpnUserService`.
- Хранилище: `defragsvc`, `StorSvc`, `VSS`, `swprv`.

Имя службы FACEIT для детекта не подтверждено (`gaming-graphics-amdgpu.md` §17, п. 5). В список внесены оба варианта.

## 8. Проверка

```bash
node tools/catalog/validate.mjs                 # все docs/research/*.tweaks.json
node tools/catalog/validate.mjs путь/к/file.json  # выбранные файлы
node --test tools/catalog/*.test.mjs            # тесты валидатора
```

Код выхода 1 — есть ошибки. Предупреждения на код выхода не влияют. Предупреждения отмечают то, что формат допускает, но спецификация считает нежелательным:
- `W_POWERSHELL` — твик выполняется PowerShell-скриптом: отката к baseline нет. В подсказке указан нативный исполнитель на замену (SPI, netsh, `nic`).
- `W_PS_USER_CONTEXT` — скрипт работает с профилем текущего процесса (SystemParametersInfo, `[Registry]::CurrentUser`, `HKEY_CURRENT_USER`), и подмена `HKCU:\` его не исправит.
- `W_PS_OWN_BACKUP` — скрипт ведёт свой бэкап мимо журнала движка.

Исследовательские каталоги приводятся к формату при импорте в продукт (Task 6). Сами файлы исследований остаются как есть.
