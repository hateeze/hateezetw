# Состояние исследований

Сверка от 2026-10-26: спецификация, план и `docs/research/`. Проверка каталогов — `node tools/catalog/validate.mjs` по формату `docs/catalog-format-v1.md`.

## Темы

| Тема | Материалы | Открытые вопросы | Следующий шаг |
|---|---|---|---|
| Питание, CPU, таймер, планировщик | `power-cpu-timer-scheduler.md`, `.tweaks.json` (45) | Запись `PowerWrite*`, дублирование схемы и откат не проверены. Нет данных Win10 22H2, Win11 24H2/25H2. X3D и Intel hybrid — без замеров | При импорте: 4 карточки → `kind: info`; `timer.resolution-service` — функция кода, а не твик; IFEO → динамический раздел (Task 12). Запись — проверка на тестовой машине |
| Игры, графика, AMD GPU | `gaming-graphics-amdgpu.md`, `.tweaks.json` (32) | Дефолт оптимизаций оконного режима и MPO на 24H2/25H2; блок-лист HAGS для AMD; **имена служб FACEIT** | Уточнить имена служб античитов для `SafetyGuard`; остальное — проверка PresentMon на 24H2/25H2 |
| NVIDIA DRS | `nvidia-drs-tweaks.md`, `.tweaks.json` (30) | Запись через внутренний API (ULL «Ультра», фоновый лимит FPS); формат бэкапа DRS; связка «Вкл/Ультра» ↔ 3 ID | Проверка записи на тестовой машине с NVIDIA |
| Службы, задачи | `services-tasks-privacy-cleanup.md`, `.tweaks.json` (44) | Права на изменение задач; дефолты 24H2/25H2 | — |
| Приватность, очистка | тот же отчёт, §4–5 | **В JSON нет ~21 записи `privacy.*` и 5 `cleanup.*` (avoid)**, хотя они перечислены в §6 отчёта | Перенести записи из §4.2 и §5 в JSON и прогнать валидатор |
| Ввод и сеть | только `input-network.tweaks.json` (57) | Отчёта нет, источники не сверены. Больше половины действий — PowerShell | Написать отчёт: источники, проверка оценок, нативные исполнители `spi`, `netsh`, `nic reset`, DNS |
| Мониторинг и прерывания | только `lowlevel-monitoring-interrupts-code/` | Нет отчёта `lowlevel-monitoring-interrupts.md` (на него ссылается спецификация §8): имя ETW-сессии, сопоставление адресов драйверам, список опасных устройств для MSI | Написать отчёт по коду зонда и первоисточникам |
| **Установщик драйвера NVIDIA** | **нет** | Нет `nvidia-driver-installer.md` (спецификация §6, Task 16): поиск драйвера, выбор компонентов, патч `setup.cfg`, тихая установка | Новое исследование. Критерий успеха №3 |
| Установщики AMD | `amd-installers.md`, фикстуры | Тихая установка Adrenalin 26.x и чипсета не проверена | В v1 — полная установка через штатный GUI, тихая — «эксперимент» |
| Приложения, DDU, обновления | `app-installers-ddu-update.md`, `apps-catalog.json` (22), код | Подписанты и детект части приложений; доступность из РФ только по источникам | Решение D6 по каталогу приложений |
| Оболочка и UI | `shell-ui-liquid-glass.md`, демо | Код не компилировался; `app-region: drag` и Mica в WinForms не проверены | Решения D1–D5, D7; переписать Task 5 плана под Svelte |

## Проверка каталогов

`node tools/catalog/validate.mjs`: 5 файлов, 208 записей, 16 ошибок, 51 предупреждение.

| Код | Кол-во | Где | Что сделать при импорте |
|---|---|---|---|
| `E_REVERT_COVERAGE` | 10 | `input-network`: `network.nic-*`, `network.wifi-*` | Откат через PowerShell `Reset-NetAdapterAdvancedProperty` заменить на `{type: nic, keyword, op: reset}` |
| `E_ACTIONS_EMPTY` | 4 | `cpu.cppc-preferred-cores-bios`, `cpu.smt-disable-bios`, `cpu.x3d-dual-ccd-keep-balanced`, `timer.resolution-service` | `kind: info` |
| `E_PLACEHOLDER_MISSING` | 1 | `timer.resolution-service` | вместе с предыдущим |
| `E_OPTIONS` | 1 | `scheduler.ifeo-cpu-priority-high` | Перенести в динамический раздел IFEO (Task 12) |
| `W_POWERSHELL` | 39 | 32 в `input-network`, 4 в `gaming-graphics-amdgpu`, 3 в `power-cpu-timer-scheduler` | Заменить нативными исполнителями по подсказке: `spi` (11), `nic` (11), `netsh` (3), `dns` (2), `iface-reg` (2), `powerScheme` (1); для 9 записей подсказки нет — решать по одной |
| `W_PS_USER_CONTEXT` | 11 | SystemParametersInfo в `input.*` и `graphics.visual-effects-performance` | Исполнитель `spi` должен писать в куст интерактивного пользователя и рассылать `WM_SETTINGCHANGE` |
| `W_PS_OWN_BACKUP` | 1 | `input.usb-power-saving-off` | Перевести на журнал движка |

Повторов id между файлами нет. Запрещённые службы в не-`avoid` записях не найдены. `nvidia-drs-tweaks` и `services-tasks-privacy-cleanup` проходят без замечаний.

## Порядок работ

1. Записи приватности и очистки в JSON — данные уже есть в отчёте.
2. Исследование установщика драйвера NVIDIA.
3. Отчёты по вводу/сети и мониторингу/прерываниям.
4. Фундамент (Task 2–4) на поддельных сервисах — нужен .NET 10 SDK (решение D10).
