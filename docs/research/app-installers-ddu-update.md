# Установщик приложений, DDU и автообновление через GitHub — исследование для hateeze

**Ключ темы:** `app-installers-ddu-update`
**Дата проверки:** 2026-10-26, 04:15–04:55 UTC
**Статус:** ссылки, заголовки, winget-манифесты, исходники DDU и поведение GitHub API проверены вживую; места без проверки помечены **НЕ ПРОВЕРЕНО** с причиной.

## Условия проверки (важно для интерпретации)

| Параметр | Значение |
|---|---|
| ОС | Windows 11 Pro for Workstations 23H2 (22631), ru-RU, процесс с правами администратора |
| Инструменты | curl 8.21.0 (Schannel), winget v1.29.380, .NET SDK 10.0.403, Node 24 |
| Внешний IP | **Эстония** (Cloudflare `CF-RAY …-TLL`), **не Россия** — доступность из РФ вживую НЕ проверялась, только по открытым источникам |
| DNS | AdGuard DNS (фильтрующий) — блокирует `get.geo.opera.com` (см. §1.8) |
| Что делалось | только чтение: `curl -sIL`, Range-запросы ≤ 256 КБ (заголовок SFX DDU и таблица подписи), `winget show/search`, `reg query`, `bcdedit /enum`, `Get-AuthenticodeSignature` по **уже установленным** файлам, чтение исходников DDU, компиляция эталонного C#-кода и запуск только read-only проверок |
| Что НЕ делалось | ничего не устанавливалось/не удалялось, не менялись реестр, BCD, службы, настройки питания |

### Сопутствующие файлы

| Файл | Что внутри |
|---|---|
| `docs/research/app-installers-ddu-update/apps-catalog.json` | Машиночитаемый каталог 22 приложений: источники (в порядке приоритета), тихие ключи, scope, правило детекта, ожидаемый подписант (`signerVerified`), заметки по РФ, наблюдённые размеры/даты |
| `docs/research/app-installers-ddu-update/code/*.cs` + `Probe.csproj` | Эталонный C# (net10.0-windows, x64). **Компилируется: 0 ошибок, 0 предупреждений.** Запущены только read-only режимы `semver-test`, `redirect-test`, `ddu-version`, `readonly-probe` — все прошли (вывод в §2.2, §2.6, §3.4, §1.5) |

---

## 0. Главное (TL;DR для разработчиков)

1. **Каталог приложений не зашивать в exe.** Держать `apps-catalog.json` в репозитории и подтягивать так же, как `latest.json` (§3). Ссылки вендоров «плывут»: Telegram переименовал ассеты (`tsetup-x64.X.exe` → `td-setup-win-x64-X.exe`), у ExitLag есть только версионные ссылки, у DDU для 18.1.6.x сменился хост.
2. **Discord: никогда не использовать `https://discord.com/api/download?platform=win`** — сейчас он отдаёт **x86-сборку 1.0.9059 от 2024-09-04**. Правильно: `https://discord.com/api/downloads/distributions/app/installers/latest?channel=stable&platform=win&arch=x64` → 1.0.9265 x64.
3. **Per-user установщики запускать БЕЗ повышения прав** (Discord, Riot/VALORANT/LoL, Telegram, Spotify, Яндекс Браузер per-user, Brave «SilentSetup»). Spotify с правами администратора не ставится вообще (winget: `ElevationRequirement: elevationProhibited`). Реализация — через Explorer (`IShellDispatch2.ShellExecute`), цепочка проверена (§1.5).
4. **Всегда задавать собственный `User-Agent`.** `api.github.com` без UA → 403. `download.wagnardsoft.com` (Cloudflare) с пустым UA или `curl/8.x` → **403 challenge**, с `hateeze/1.0` → 200 (проверено).
5. **DDU:** версия — `https://www.wagnardsoft.com/DDU/currentversion2.txt` (ответ `18.1.6.2`, этот же URL опрашивает сам DDU); файл — `https://download.wagnardsoft.com/DDU/DDU%20v18.1.6.2.exe` (7-Zip SFX, 1 381 040 Б, Authenticode «Wagnardsoft»). Поле `downloadUrl` в `/api/ddu/version.json` ведёт на 404. Форум релизов закрыт Cloudflare challenge — для автоматики непригоден. winget отстаёт (18.1.5.3).
6. **Распаковка DDU:** `"DDU v18.1.6.2.exe" -y "-oC:\ProgramData\hateeze\ddu\18.1.6.2"` — подтверждено исходником 7-Zip `SfxWin.cpp`: `-y` строго без суффикса (иначе «Bad command»), `-o<dir>` слитно.
7. **Аргументы DDU** — актуальный список из исходника `AppLaunchOptions.vb` (ветка `WPF`, push от 2026-10-23), §2.4. Ключей `-logging` и `-RemoveAudio` **не существует**. Аргументы командной строки **полностью перекрывают** сохранённые настройки DDU (все `Remove*` по умолчанию `false`).
8. **Безопасный режим — копируем модель самого DDU:** три страховки (служба, стартующая до входа и в Safe Mode; `RunOnce` с `*`; проверка при старте приложения) и **последним** шагом `bcdedit /set {current} safeboot minimal`. По Microsoft Learn `safeboot` (0x25000080) **не** входит в профиль проверки BitLocker, а `safebootalternateshell` — входит (его не трогать).
9. **Встроенный режим DDU** (опция `EnableSafeModeDialog`) перезагружает в Safe Mode и снова открывает DDU, но **не продолжает очистку автоматически** — пользователь жмёт «Удалить и перезагрузить» сам.
10. **Автообновление:** основной канал — фиксированный ассет `latest.json` по адресу `https://github.com/{owner}/{repo}/releases/latest/download/latest.json` (это не REST API и не тратит лимит 60 запросов/час на IP). На машине разработчика лимит REST API был **исчерпан посторонними процессами за минуты** (общий IP) — полагаться только на API нельзя. В REST-ответе у ассетов есть поле `digest: "sha256:…"` (проверено). Условный запрос с ETag → 304 **без токена всё равно списывает лимит** (проверено: `X-RateLimit-Used` 2 → 3).
11. **РФ:** Discord заблокирован (с 08–09.10.2024), Telegram ограничен (с 10.02.2026), у GitHub (`raw/release-assets.githubusercontent.com`) периодические сбои (май 2026), NVIDIA в 10.2024 блокировала загрузки из РФ, Spotify в РФ не работает. winget-fallback помогает только когда ломается **наша** логика поиска ссылки — если заблокирован сам хост вендора, winget качает с того же хоста.

---

## 1. Установщик приложений

### 1.1 Архитектура конвейера

```
resolve (каталог → финальный URL + версия) → download (resume, stall-timeout, SHA-256 на лету)
→ verify (Authenticode + CN подписанта; SHA-256, если известен) → install (ключи, политика повышения, таймаут, коды выхода)
→ detect (реестр Uninstall + файлы) → report (UI)
```

**Виды `resolve` (поле `sources[].kind` в каталоге):**

| kind | Как получить URL/версию | Примеры |
|---|---|---|
| `static` | URL не меняется, файл «всегда последний» | Steam, FACEIT AC, Chrome MSI, Spotify |
| `redirect` | GET/HEAD с авто-редиректами; финальный URL содержит версию (regex `versionFromFinalUrl`) | Discord, Epic, Battle.net, Firefox, Telegram, Яндекс |
| `static-template` | подстановка `{region}` | VALORANT, LoL |
| `github-release` / `github-latest-asset` | REST API `releases/latest` (есть `digest`) или `…/releases/latest/download/<имя>` | 7-Zip, OBS, Telegram (зеркало), Brave |
| `appcast` | XML Sparkle-фид | Vivaldi |
| `json-api` | официальный JSON с хэшем | Microsoft Edge |
| `html-regex` | регулярка по официальной странице | NVIDIA App, 7-Zip (главная) |
| `catalog-pinned` | версия и URL вписаны в наш каталог вручную | ExitLag |
| `open-page` | открыть страницу в браузере пользователя | ExitLag (fallback) |

**Общие правила реализации:**

- **Один `HttpClient`** на приложение (`code/Http.cs`): `ConnectTimeout = 10 c`, бесконечный `Timeout` + `CancellationToken`, системный прокси (у пользователей из РФ часто VPN/прокси-клиент), докачка по `Range`, детектор «зависания» 30 с без байтов, файл пишется в `*.part` и переименовывается после успеха.
- **Папка загрузок:** `C:\ProgramData\hateeze\downloads\<id>\` (не `%TEMP%` администратора: файл должен читаться и непривилегированным процессом при запуске без повышения; у `ProgramData` по умолчанию группа Users имеет «Чтение и выполнение»).
- **Mark-of-the-Web:** `HttpClient` не ставит `Zone.Identifier` → SmartScreen при запуске не спросит. Поэтому проверка подписи — на нашей стороне (обязательно).
- **Установки строго последовательно.** Параллельные MSI дают `1618 ERROR_INSTALL_ALREADY_RUNNING`. Для MSI: `0` успех, `3010`/`1641` успех + нужна перезагрузка, `1602` отменено пользователем, `1618` — подождать и повторить.
- **Проверка подписи:** `WinVerifyTrust` + CN подписанта из каталога (`code/Authenticode.cs`). Проверка отзыва отключена (`WTD_REVOKE_NONE`): CRL/OCSP-хосты могут не открываться в РФ, а таймауты WinVerifyTrust на CRL достигают десятков секунд. Если в каталоге `signerVerified: false` — принимать любую **валидную** подпись и писать CN в лог, пока CN не подтвердят тестом.
- **SHA-256** известен только для версионных ссылок (GitHub `digest`, Edge API `Hash`, winget-манифест конкретной версии). Для `static`-ссылок «всегда последний» хэш из winget **не применять** — он относится к конкретной сборке и разъедется, когда вендор обновит файл.
- **Ожидание завершения** при запуске без повышения: `IShellDispatch2.ShellExecute` не возвращает PID. Ждать по процессу (имя + путь к скачанному файлу) и/или опрашивать правило детекта с таймаутом. Squirrel (Discord) и Riot сами порождают дочерние процессы и выходят раньше фактического окончания.

### 1.2 Сводная таблица (всё проверено 2026-10-26, если не сказано иное)

| Приложение | winget ID (проверен `winget show --id … --exact`) | Официальный источник → финальный URL | Тип / тихие ключи | Scope / запуск | Детект (Uninstall-ключ) | Подписант Authenticode |
|---|---|---|---|---|---|---|
| Discord (stable) | `Discord.Discord` 1.0.9265 | `discord.com/api/downloads/distributions/app/installers/latest?channel=stable&platform=win&arch=x64` → `stable.dl2.discordapp.net/distro/app/stable/win/x64/1.0.9265/DiscordSetup.exe` (application/x-msdownload, 145 745 848 Б) | Squirrel exe, `-s` | user, **без повышения** | HKCU `Discord` (DisplayVersion `1.0.9265`) | Discord Inc. ✔ |
| ExitLag | — (в winget нет) | `cdn.exitlag.com/SetupExitLag-5.23.3-x64.exe` (application/x-msdownload, 94 003 128 Б); страница `exitlag.com/download` — Cloudflare | Inno 6.5.1, `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-` | machine | HKLM `{58571ef5-099f-447d-bb15-d2350dd0b449}_is1` (DisplayVersion `5.23.3`) | Skowsand Tecnologia Ltda ✔ |
| FACEIT Anti-Cheat | `FACEITLTD.FACEITAC` 2.1 | `anticheat-client.faceit-cdn.net/FACEITInstaller_64.exe` (115 744 104 Б) | Inno, `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` | machine | HKLM `{1419E44C-0EF4-4822-9194-9F1A4D43973D}_is1` (DisplayVersion всегда `2.1`) | ESL FACEIT Group Ltd. ✔ |
| FACEIT (клиент) | `FACEITLTD.FACEITClient` 2.9.2 | `faceit-client.faceit-cdn.net/release/FACEIT-setup-latest.exe` (application/octet-stream, 171 633 640 Б) | exe, `--silent` (из winget) | machine (по winget) | DisplayName `FACEIT` — **НЕ ПРОВЕРЕНО** (не установлен) | **НЕ ПРОВЕРЕНО** |
| Riot Client + VALORANT | `RiotGames.Valorant.EU/NA/AP/KR/BR/LATAM` | `valorant.secure.dyn.riotcdn.net/channels/public/x/installer/current/live.live.{eu,na,ap,kr,br,latam}.exe` — все 200, 75 182 824 Б; `ru` → 404 | exe, `--skip-to-install --disable-auto-launch` (+`--install-path=`, `--log-root=`) — **не полностью тихий** | user, **без повышения** (сам запросит UAC для Vanguard) | HKCU `Riot Game valorant.live`, `Riot Game Riot_Client.` (DisplayVersion **пустой**); HKLM `Riot Vanguard` | Riot Games, Inc. ✔ |
| League of Legends (опц.) | `RiotGames.LeagueOfLegends.{EUW,EUNE,NA,RU,TR,JP,KR,BR,LA1,LA2,OC1,PH2,SG2,TH2,TW2,VN2,PBE}` | `lol.secure.dyn.riotcdn.net/channels/public/x/installer/current/live.{region}.exe` — 200 для euw, eune, na, ru, tr, jp, kr, br, la1, la2, oc1, me1, ph2, sg2, th2, tw2, vn2 | как VALORANT | user, без повышения | HKCU `Riot Game league_of_legends.live` | Riot Games, Inc. ✔ |
| Steam | `Valve.Steam` 2.10.91.91 | `cdn.akamai.steamstatic.com/client/installer/SteamSetup.exe` (2 380 800 Б, от 2024-05-20) | NSIS, `/S` | machine | HKLM(WOW6432Node) `Steam` | Valve Corp. ✔ |
| Epic Games Launcher | `EpicGames.EpicGamesLauncher` 1.3.210.0 | `launcher-public-service-prod06.ol.epicgames.com/launcher/api/installer/download/EpicGamesLauncherInstaller.msi` → 303 → `epicgames-download1.akamaized.net/…/EpicInstaller-20.3.3.msi` (application/x-ole-storage, 264 237 056 Б) | MSI, `msiexec /i … /qn /norestart` | machine | MSI UpgradeCode `{D0769F44-D459-450F-B084-CAE38062C75B}` (проверено) | **НЕ ПРОВЕРЕНО** |
| Battle.net | `Blizzard.BattleNet` 2.0.0.4232 | `downloader.battle.net/download/getInstallerForGame?os=win&gameProgram=BATTLENET_APP&version=Live` → `…/installer/win/1.0.68/Battle.net-Setup.exe` (4 700 880 Б) | exe; **официального тихого режима нет**; `--lang=ruRU --installpath="…"` | machine | HKLM(WOW6432Node) `Battle.net` (по winget) | **НЕ ПРОВЕРЕНО** |
| Google Chrome | `Google.Chrome` 156.0.8078.48 | `dl.google.com/dl/chrome/install/googlechromestandaloneenterprise64.msi` (application/x-msi, 170 979 328 Б) | MSI, `/qn /norestart` | machine | HKLM(WOW6432Node) `Google Chrome` | Google LLC ✔ |
| Mozilla Firefox (ru) | `Mozilla.Firefox.ru` 157.0.1 (отстаёт от сайта) | `download.mozilla.org/?product=firefox-latest-ssl&os=win64&lang=ru` → `…/releases/158.0.1/win64/ru/Firefox%20Setup%20158.0.1.exe` (95 380 224 Б); MSI: `product=firefox-msi-latest-ssl` | NSIS, `/S` (+ опции Mozilla, §1.3) | machine | DisplayName `^Mozilla Firefox`, Publisher `Mozilla` | **НЕ ПРОВЕРЕНО** |
| Brave | `Brave.Brave` 155.1.97.56 | офлайн: `github.com/brave/brave-browser/releases/latest/download/BraveBrowserStandaloneSetup.exe` (→ v1.98.60, 168 513 376 Б); онлайн-загрузчик: `laptop-updates.brave.com/latest/winx64` → `referrals.brave.com/latest/BraveBrowserSetup.exe` (1 277 280 Б) | Omaha, `/silent /install` | machine | `BraveSoftware Brave-Browser` | **НЕ ПРОВЕРЕНО** |
| Opera GX | `Opera.OperaGX` 137.0.6036.65 | онлайн: `net.geo.opera.com/opera_gx/stable/windows` (OperaGXSetup.exe, 4 163 560 Б); офлайн из winget: `get.geo.opera.com/pub/opera_gx/{v}/win/Opera_GX_{v}_Setup_x64.exe` — **НЕ ПРОВЕРЕНО** (DNS-блок AdGuard) | `/silent /allusers=1 /launchopera=0 /setdefaultbrowser=0` | machine (`/allusers=1`) | **НЕ ПРОВЕРЕНО** | **НЕ ПРОВЕРЕНО** |
| Opera | `Opera.Opera` 137.0.6036.39 | онлайн: `net.geo.opera.com/opera/stable/windows` (OperaSetup.exe, 3 278 168 Б) | как Opera GX | machine | **НЕ ПРОВЕРЕНО** | **НЕ ПРОВЕРЕНО** |
| Яндекс Браузер | `Yandex.Browser` 26.10.0.1854 | `browser.yandex.ru/download?os=win&bitness=64&full=1` → `cdn.browser.yandex.net/browser/yandex/26_10_0_1854_119369/ru/Yandex.exe?…` → `cloudcdn-*.cdn.yandex.net` (215 095 504 Б); без трекинга: `download.cdn.yandex.net/browser/yandex/{build}/ru/Yandex.exe` (200) | `--silent --do-not-launch-browser`; коды успеха 0, 1, 3 | user (winget) | `YandexBrowser` (HKCU или HKLM(WOW6432Node)) | YANDEX LLC ✔ |
| Vivaldi | `Vivaldi.Vivaldi` 8.2.4133.94 | фид `update.vivaldi.com/update/1.0/public/appcast.x64.xml` → `downloads.vivaldi.com/stable/Vivaldi.8.2.4133.94.x64.exe` (156 217 256 Б) | `--vivaldi-silent --do-not-launch-chrome [--system-level]` | machine (`--system-level`) или user | `Vivaldi` | **НЕ ПРОВЕРЕНО** |
| Microsoft Edge | `Microsoft.Edge` 156.0.4314.42 | API `edgeupdates.microsoft.com/api/products?view=enterprise` → MSI + SHA-256 (215 257 088 Б) | MSI, `/qn /norestart` | machine | HKLM(WOW6432Node) `Microsoft Edge` | Microsoft Corporation ✔ |
| 7-Zip | `7zip.7zip` 26.05 | GitHub `ip7z/7zip` 26.05: `7z2605-x64.exe` (1 663 506 Б, sha256 `fd99e86f…47d0`); `www.7-zip.org/a/7z2605-x64.exe` → 302 на GitHub | exe `/S`; MSI `/qn` | machine | HKLM `7-Zip` | **не подписан** — только SHA-256 |
| OBS Studio | `OBSProject.OBSStudio` 33.0.0 | GitHub `obsproject/obs-studio` 33.0.0: `OBS-Studio-33.0.0-Windows-x64-Installer.exe` (186 671 888 Б, digest `sha256:3bf55dae…bed3`) | NSIS, `/S`; код 6 = OBS запущен | machine | HKLM(WOW6432Node) `OBS Studio` | OBS Project, LLC ✔ |
| Telegram Desktop | `Telegram.TelegramDesktop` 7.3.3 | `telegram.org/dl/desktop/win64` → `td.telegram.org/win-x64/td-setup-win-x64-7.3.3.exe` (63 102 944 Б); зеркало GitHub `telegramdesktop/tdesktop` | Inno, `/VERYSILENT /NORESTART` | user, без повышения | HKCU `{53F49750-6209-4FBF-9CA8-7A333C87D1ED}_is1` | Telegram FZ-LLC ✔ |
| Spotify | `Spotify.Spotify` 1.3.6.1921 | `download.scdn.co/SpotifyFullSetupX64.exe` (157 180 192 Б) | `/silent /skip-app-launch`; код 26 = запущен | user, **строго без повышения** | HKCU `Spotify` | Spotify AB ✔ |
| NVIDIA App (опц.) | — (нет в winget) | regex по `www.nvidia.com/en-us/software/nvidia-app/` → `us.download.nvidia.com/nvapp/client/11.0.9.565/NVIDIA_app_v11.0.9.565.exe` (184 718 920 Б); зеркало `uk.download.nvidia.com` | тихий режим **НЕ ПРОВЕРЕН** — запускать интерактивно | machine | `{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}_Display.NvApp` (паттерн из исходников DDU; **НЕ ПРОВЕРЕНО**) | NVIDIA Corporation ✔ |

✔ — CN снят `Get-AuthenticodeSignature` с уже установленных на машине бинарников того же вендора (для Chrome/Edge/Яндекса/Spotify — с exe приложения, для Riot — `RiotClientServices.exe`, для NVIDIA — `NVI2.DLL`). Подпись **самого установщика** вендор может делать другим сертификатом той же организации — сравнивать по CN, не по отпечатку.

**Обозначения:** «HKLM» = `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` (64-бит), «HKLM(WOW6432Node)» = `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall`, «HKCU» = `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall` **интерактивного пользователя** (см. §1.4).

### 1.3 Карточки приложений

Тексты `title/description/recommendation` — для UI. Оценка надёжности здесь относится к автоматической установке (это не твики производительности).

#### Discord (stable)
- **URL (проверено):** `https://discord.com/api/downloads/distributions/app/installers/latest?channel=stable&platform=win&arch=x64` → 302 → `https://stable.dl2.discordapp.net/distro/app/stable/win/x64/1.0.9265/DiscordSetup.exe`, `Content-Type: application/x-msdownload`, `Content-Length: 145745848`, `Last-Modified: 2026-10-22`.
- **Ловушка:** `https://discord.com/api/download?platform=win` и `…/api/download/stable?platform=win` редиректят на `…installers/latest?arch=x86…` → `…/win/x86/1.0.9059/DiscordSetup.exe` (107 226 856 Б, **2024-09-04**). Не использовать.
- **Версия:** regex по финальному URL `/win/x64/(\d+\.\d+\.\d+)/DiscordSetup\.exe$`.
- **Тихо:** `DiscordSetup.exe -s` (Squirrel; согласуется с `QuietUninstallString` на машине: `Update.exe --uninstall -s`). После установки Squirrel сам запускает Discord.
- **Куда:** `%LocalAppData%\Discord` (winget: `Scope: user`, `DefaultInstallLocation: %LocalAppData%\Discord`, `RequireExplicitUpgrade: true`).
- **Запуск:** без повышения — иначе Discord стартует с правами администратора (перестанет работать drag&drop из Проводника из-за UIPI), а при повышении под другой учёткой поставится в чужой профиль.
- **Детект:** `HKCU\…\Uninstall\Discord` → `DisplayVersion` (`1.0.9265`), `InstallLocation`.
- **РФ:** заблокирован РКН с октября 2024. winget качает с того же `stable.dl2.discordapp.net` — fallback не поможет.
- **UI:** title «Discord»; description «Голосовой и текстовый чат для игроков»; recommendation «В России Discord заблокирован — для загрузки и работы может понадобиться VPN».

#### ExitLag
- **Ссылка (проверено):** `https://cdn.exitlag.com/SetupExitLag-5.23.3-x64.exe` — 200, 94 003 128 Б, `Last-Modified: 2026-10-05`. Получена со страницы `https://www.exitlag.com/download`, которая для curl/HttpClient отдаёт **403 (Cloudflare challenge)**; ссылка видна только в настоящем браузере (проверено во встроенном браузере).
- **Нет «latest»-алиаса:** `SetupExitLag.exe`, `SetupExitLag-latest-x64.exe` → 404. Старый `SetupExitLag_v43051.exe` (2025-01-14) — не использовать.
- **Как обновлять:** версию и URL держать в нашем `apps-catalog.json` (`kind: catalog-pinned`), при 404 — кнопка «Открыть страницу загрузки ExitLag». Обходить Cloudflare скрытым WebView2 технически возможно, но это обход защиты от ботов — **не рекомендуется**.
- **Тихо:** Inno Setup (в Uninstall-ключе `Inno Setup: Setup Version = 6.5.1`) → `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-`. Задача ярлыка — `desktopicon` (`Inno Setup: Selected Tasks`), отключить: `/MERGETASKS="!desktopicon"` (**НЕ ПРОВЕРЕНО** запуском).
- **Куда/детект:** `C:\Program Files\ExitLag\`, `HKLM\…\Uninstall\{58571ef5-099f-447d-bb15-d2350dd0b449}_is1`, `DisplayName = "ExitLag version 5.23.3"`, `DisplayVersion = 5.23.3`, `QuietUninstallString = "…\unins000.exe" /SILENT`.
- **Подпись:** `Skowsand Tecnologia Ltda` (ExitLag.exe и unins000.exe).
- **UI:** «ExitLag» / «Платный сервис оптимизации сетевого маршрута до игровых серверов» / «Помогает только если проблема в маршруте провайдера; при хорошем пинге эффекта нет» (оценка **situational**).

#### FACEIT Anti-Cheat и клиент FACEIT
- **AC:** `https://anticheat-client.faceit-cdn.net/FACEITInstaller_64.exe` — 200, 115 744 104 Б, `Last-Modified: 2026-10-19`. Inno → `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`. Детект: `HKLM\…\Uninstall\{1419E44C-0EF4-4822-9194-9F1A4D43973D}_is1`, `DisplayName = FACEIT Anti-Cheat`, файл `C:\Program Files\FACEIT AC\faceitclient.exe`. `DisplayVersion` = `2.1` и **не меняется** между сборками — «актуальность» определять по `ETag`/`Last-Modified` источника или `FileVersion` exe.
- **Клиент:** `https://faceit-client.faceit-cdn.net/release/FACEIT-setup-latest.exe` — 200, `application/octet-stream`, 171 633 640 Б. Тихо: `--silent` (winget). Хост `client-updater.faceit-cdn.net` не отвечает — не использовать. Детект **НЕ ПРОВЕРЕН** (не установлен).
- **Перезагрузка после AC:** **НЕ ПРОВЕРЕНО**; драйвер ядра — предлагать перезагрузку, если клиент FACEIT попросит.

#### Riot Client + VALORANT (регионы)
- **Проверено:** `https://valorant.secure.dyn.riotcdn.net/channels/public/x/installer/current/live.live.{region}.exe` для `eu, na, ap, kr, br, latam` → 200, `application/x-msdownload`, 75 182 824 Б, `Content-Disposition: attachment; filename="Install VALORANT.exe"`, `Last-Modified: 2026-09-01`. `ru` и любой другой → 404. Для пользователей из РФ по умолчанию — `eu`.
- **Ключи (winget):** `--skip-to-install` (пропустить экран выбора), `--disable-auto-launch`, `--install-path="<PATH>"`, `--log-root="<PATH>"`. Режим только `silentWithProgress`: окно установщика видно, игру докачивает Riot Client (десятки ГБ) — успехом считать запуск установщика + появление `RiotClientInstalls.json`, а не скачивание игры.
- **Детект:** `HKCU\…\Uninstall\Riot Game valorant.live` и `Riot Game Riot_Client.`; `DisplayVersion` **пустой** → версию не показывать. Файл `C:\ProgramData\Riot Games\RiotClientInstalls.json` (поля `rc_default`, `rc_live`, `associated_client`). Vanguard: `HKLM\…\Uninstall\Riot Vanguard`.
- **После установки:** Vanguard требует перезагрузки. На Windows 11 Vanguard требует включённых TPM 2.0 и Secure Boot (ошибки VAN 9001/9003, статья поддержки Riot).
- **UI recommendation:** «После установки VALORANT перезагрузите ПК — античит Vanguard загружается при старте Windows. В Windows 11 нужны TPM 2.0 и Secure Boot».

#### League of Legends (опционально)
- `https://lol.secure.dyn.riotcdn.net/channels/public/x/installer/current/live.{region}.exe`: 200 для `euw, eune, na, ru, tr, jp, kr, br, la1, la2, oc1, me1, ph2, sg2, th2, tw2, vn2` (euw: 75 831 040 Б, 2026-08-12); 404 для `lan, las, oce, ph, sg, th, tw, vn`. PBE (из winget): `…/current/pbe.pbe.exe` — **НЕ ПРОВЕРЕНО**. Ключи и детект как у VALORANT (`Riot Game league_of_legends.live`). Работа RU-сервера **НЕ ПРОВЕРЕНА**.

#### Steam
- `https://cdn.akamai.steamstatic.com/client/installer/SteamSetup.exe` — 200, 2 380 800 Б, `Last-Modified: 2024-05-20`, `ETag: "664bcd75-245400"` (загрузчик, клиент докачивается при первом запуске).
- NSIS: `/S`. Ставит в `C:\Program Files (x86)\Steam` (сам повышает права).
- Детект: `HKLM\SOFTWARE\WOW6432Node\…\Uninstall\Steam` (`DisplayVersion 2.10.91.91` — версия **установщика**), `HKLM\SOFTWARE\WOW6432Node\Valve\Steam\InstallPath`, `HKCU\Software\Valve\Steam\SteamExe`.

#### Epic Games Launcher (MSI)
- `https://launcher-public-service-prod06.ol.epicgames.com/launcher/api/installer/download/EpicGamesLauncherInstaller.msi` → **303** → `https://epicgames-download1.akamaized.net/Builds/UnrealEngineLauncher/Installers/Windows/EpicInstaller-20.3.3.msi?launcherfilename=EpicInstaller-20.3.3.msi`, `Content-Type: application/x-ole-storage`, 264 237 056 Б.
- `msiexec /i "<msi>" /qn /norestart /l*v "<log>"`.
- Детект: `ProductCode` меняется с версией (на машине `{D4744863-43C4-4FB1-99BA-1E81D3FC8585}` = 1.3.193.0, в winget для 1.3.210.0 — `{0716D12C-C7A6-4C1B-A5BB-E23B7160250D}`). Стабилен `UpgradeCode {D0769F44-D459-450F-B084-CAE38062C75B}` — проверено: `HKLM\SOFTWARE\Classes\Installer\UpgradeCodes\44F9670D954DF0540B48AC3E08267CB5` указывает на установленный продукт. В коде — `MsiEnumRelatedProducts(upgradeCode, …)` или `DisplayName == "Epic Games Launcher"`.
- Путь: winget пишет `%ProgramFiles(x86)%\Epic Games`, на машине разработчика `InstallLocation = C:\Program Files\Epic Games\` — путь не хардкодить.

#### Battle.net
- `https://downloader.battle.net/download/getInstallerForGame?os=win&gameProgram=BATTLENET_APP&version=Live` → 302 → `https://downloader.battle.net/download/installer/win/1.0.68/Battle.net-Setup.exe` (4 700 880 Б). URL из winget (`…/getInstaller?os=win&installer=Battle.net-Setup.exe`) ведёт туда же.
- **Официального тихого режима нет.** winget передаёт `--lang=enUS --installpath="<path>"` (`InstallLocationRequired: true`); для РФ — `--lang=ruRU`. Окно установщика показывается — **НЕ ПРОВЕРЕНО**, считать «полуавтоматической» установкой.

#### Google Chrome (enterprise MSI x64)
- `https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise64.msi` (также `https://dl.google.com/chrome/install/GoogleChromeStandaloneEnterprise64.msi`, тот же ETag) — `application/x-msi`, 170 979 328 Б.
- `msiexec /i … /qn /norestart`. Детект: `HKLM\SOFTWARE\WOW6432Node\…\Uninstall\Google Chrome` (у 64-битного Chrome ключ всё равно в WOW6432Node — проверено), плюс возможная per-user установка в `HKCU\…\Uninstall\Google Chrome`.

#### Mozilla Firefox (ru)
- exe: `https://download.mozilla.org/?product=firefox-latest-ssl&os=win64&lang=ru` → `…/pub/firefox/releases/158.0.1/win64/ru/Firefox%20Setup%20158.0.1.exe`; MSI: `product=firefox-msi-latest-ssl` → `…Firefox%20Setup%20158.0.1.msi` (`application/x-msi`, 95 518 720 Б).
- Ключи полного установщика (документация Mozilla): `/S`, `/InstallDirectoryPath=`, `/TaskbarShortcut=false`, `/DesktopShortcut=false`, `/StartMenuShortcut=false`, `/PrivateBrowsingShortcut=false`, `/MaintenanceService=false`, `/PreventRebootRequired=true`, `/RegisterDefaultAgent=false`, `/INI=`. Любая опция включает тихий режим. Для MSI эти опции **не работают**.
- winget-версия отстаёт (157.0.1 против 158.0.1 на сайте) — предпочитать прямой редирект.

#### Brave
- Офлайн (machine): `https://github.com/brave/brave-browser/releases/latest/download/BraveBrowserStandaloneSetup.exe` (302 → v1.98.60, 168 513 376 Б), `/silent /install`. Per-user: `BraveBrowserStandaloneSilentSetup.exe` без аргументов (запускать без повышения).
- Онлайн-загрузчик: `https://laptop-updates.brave.com/latest/winx64` → 301 → 302 → `https://referrals.brave.com/latest/BraveBrowserSetup.exe` (1 277 280 Б).
- Детект: `BraveSoftware Brave-Browser` (HKLM или HKCU). Коды (winget): `-2147219416` = уже установлен, `-2147219440` = отменено.

#### Opera GX / Opera
- Онлайн-загрузчики (проверено): `https://net.geo.opera.com/opera_gx/stable/windows` (OperaGXSetup.exe, 4 163 560 Б), `https://net.geo.opera.com/opera/stable/windows` (OperaSetup.exe, 3 278 168 Б).
- Офлайн (winget): `https://get.geo.opera.com/pub/opera_gx/{ver}/win/Opera_GX_{ver}_Setup_x64.exe`, `https://get.geo.opera.com/pub/opera/desktop/{ver}/win/Opera_{ver}_Setup_x64.exe` — **НЕ ПРОВЕРЕНО**: на машине разработчика `get.geo.opera.com` блокируется DNS-фильтром AdGuard (SOA `fake-for-negative-caching.adguard.com`). То же будет у пользователей с AdGuard/NextDNS — нужен fallback на `net.geo.opera.com`.
- Ключи: `/silent /allusers=1 /launchopera=0 /setdefaultbrowser=0` (сторонние гайды; winget использует `/silent` и `/allusers=0|1`); для GX дополнительно `/desktopshortcut=0`. Детект и подписант **НЕ ПРОВЕРЕНЫ**.

#### Яндекс Браузер
- `https://browser.yandex.ru/download?os=win&bitness=64&full=1` → 302 → `https://cdn.browser.yandex.net/browser/yandex/26_10_0_1854_119369/ru/Yandex.exe?…` (в URL трекинговые параметры `yandexuid`, `banerid`, `browser=<из User-Agent>`) → 302 → `cloudcdn-*.cdn.yandex.net` → 200, 215 095 504 Б. Без трекинга (проверено): `https://download.cdn.yandex.net/browser/yandex/{build}/ru/Yandex.exe`; winget использует `…/browser/int/{build}/en/Yandex.exe` (международная сборка).
- Ключи (winget): `--silent --do-not-launch-browser`; `InstallerSuccessCodes: 1, 3` помимо 0.
- Детект: ключ `YandexBrowser`. На машине разработчика — системная установка `HKLM\SOFTWARE\WOW6432Node\…\YandexBrowser`, `DisplayName = "Yandex (All Users)"`, `InstallLocation = C:\Program Files\Yandex\YandexBrowser\Application`. Ключ системной установки (Chromium: `--system-level`) — **НЕ ПРОВЕРЕН**.

#### Vivaldi
- Фид обновлений (официальный, Sparkle): `https://update.vivaldi.com/update/1.0/public/appcast.x64.xml` → `<enclosure url="https://downloads.vivaldi.com/stable-auto/Vivaldi.8.2.4133.94.x64.exe" sparkle:version="8.2.4133.94"/>`.
- Файл: `https://downloads.vivaldi.com/stable/Vivaldi.{ver}.x64.exe` (156 217 256 Б; тот же ETag, что и `stable-auto`). Неверсионный `…/stable/Vivaldi.x64.exe` сейчас тоже отдаёт этот файл, но это недокументировано — не полагаться.
- Ключи: `--vivaldi-silent --do-not-launch-chrome` (+ `--system-level` для всех пользователей, `--vivaldi-install-dir="<path>"`). Коды (winget): 60 = идёт установка, 22/7 = отменено, 4 = downgrade.

#### Microsoft Edge
- Обычно предустановлен (на машине: `HKLM\SOFTWARE\WOW6432Node\…\Uninstall\Microsoft Edge`, 151.0.4129.59). Пункт — только «Восстановить Edge».
- `GET https://edgeupdates.microsoft.com/api/products?view=enterprise` → `[{Product:"Dev"|"Beta"|"Stable"|"EdgeUpdate"|"Policy", Releases:[{ReleaseId, Platform, Architecture, ProductVersion, PublishedTime, Artifacts:[{ArtifactName:"msi", Location, Hash, HashAlgorithm:"SHA256", SizeInBytes}]}]}]`. Выбор: `Product=="Stable"`, `Platform=="Windows"`, `Architecture=="x64"`, максимальный `ReleaseId`. На 2026-10-26: 156.0.4314.42, `MicrosoftEdgeEnterpriseX64.msi`, SHA-256 `324D226C5ADE08C2776645CFC2A7B12AF9C08214ABF18C6499410CD8ED92D1B3` (совпадает с winget).
- Установка: `msiexec /i … /qn /norestart`.

#### 7-Zip
- Последняя версия: GitHub `ip7z/7zip` `releases/latest` (26.05, 2026-10-21) — у каждого ассета есть `digest`. Либо главная `https://www.7-zip.org/` — первая ссылка вида `https://github.com/ip7z/7zip/releases/download/26.05/7z2605-x64.exe`. **Не парсить `download.html`** — первой там стоит 23.01. `https://www.7-zip.org/a/7z2605-x64.exe` теперь 302 → GitHub (7-Zip целиком зависит от доступности GitHub).
- exe: `/S` (`/D="<path>"`); MSI: `msiexec /i 7z2605-x64.msi /qn` (`INSTALLDIR=`). SHA-256 26.05: exe `fd99e86f57d8f429ef5ed045bf98315af962e2309b0f1ba87a147a44c71647d0`, msi `3fe4ab99fa15629c652944309a2b2bc6e41fc933fc6b63f5df1ebcf3aea68263` (совпадают с winget).
- **Не подписан Authenticode** (`Get-AuthenticodeSignature` → NotSigned; WinVerifyTrust → `0x800B0100`) — проверять только SHA-256.
- Детект: exe-установка → `HKLM\…\Uninstall\7-Zip` (`DisplayName "7-Zip 26.02 (x64)"`, `DisplayVersion 26.02`); MSI → `ProductCode {23170F69-40C1-2702-2605-000001000000}` (версия зашита в GUID), `UpgradeCode {23170F69-40C1-2702-0000-000004000000}`.

#### OBS Studio
- GitHub `obsproject/obs-studio` 33.0.0 (2026-10-23): `OBS-Studio-33.0.0-Windows-x64-Installer.exe`, 186 671 888 Б, `digest: sha256:3bf55dae6d9db651505828090de74540296c742ed2a94a28cf055cb6ddc6bed3`.
- NSIS `/S`; код выхода 6 = OBS запущен. Детект: `HKLM\SOFTWARE\WOW6432Node\…\Uninstall\OBS Studio` (`DisplayVersion 32.2.2` на машине), `QuietUninstallString "…\uninstall.exe" /S`.

#### Telegram Desktop
- `https://telegram.org/dl/desktop/win64` → 302 → `https://td.telegram.org/win-x64/td-setup-win-x64-7.3.3.exe` (63 102 944 Б, 2026-10-21). Портативная: `/dl/desktop/win64_portable` → `td-portable-win-x64-7.3.3.zip`.
- Зеркало: GitHub `telegramdesktop/tdesktop` (`v7.3.3`, `td-setup-win-x64-7.3.3.exe`, sha256 `865cd800…5a06`). **Имя ассета сменилось** (раньше `tsetup-x64.X.Y.Z.exe`) — искать по regex `^td-setup-win-x64-[\d.]+\.exe$` с запасным `^tsetup-x64\.[\d.]+\.exe$`.
- Inno, per-user (`%AppData%\Telegram Desktop`): `/VERYSILENT /NORESTART`. Детект: `HKCU\…\Uninstall\{53F49750-6209-4FBF-9CA8-7A333C87D1ED}_is1`.

#### Spotify
- `https://download.scdn.co/SpotifyFullSetupX64.exe` (157 180 192 Б, 2026-10-21). `SpotifySetup.exe` — онлайн-загрузчик (1 075 688 Б). `SpotifyFullSetup.exe` — старая x86-сборка (2026-02-22), не использовать.
- `/silent /skip-app-launch` (`/log-file "<path>"`); код 26 = Spotify запущен.
- **Только без повышения прав** (winget `elevationProhibited`). Детект: `HKCU\…\Uninstall\Spotify`, `QuietUninstallString "%AppData%\Spotify\uninstall.exe" /silent`.
- РФ: сервис ушёл в 2022 — показывать предупреждение.

#### NVIDIA App (опционально)
- Версия: `GET https://www.nvidia.com/en-us/software/nvidia-app/` (работает с любым UA, включая пустой) → regex `https://us\.download\.nvidia\.com/nvapp/client/(\d+(?:\.\d+){3})/NVIDIA_app_v\1\.exe` → 11.0.9.565 (184 718 920 Б, 2026-10-07). Страница `ru-ru` даёт зеркало `uk.download.nvidia.com` (тоже 200).
- Тихий режим **НЕ ПРОВЕРЕН** (в сети советуют `-s`) — запускать интерактивно.
- Детект: Uninstall-ключ `{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}_Display.NvApp` — шаблон `_display.nvapp` взят из исходника DDU (`GPUCleanup.vb`); на машине разработчика NVIDIA App не установлен — **НЕ ПРОВЕРЕНО**. Внимание: `DisplayName` пакетов NVIDIA **локализован** (на машине: «NVIDIA Графический драйвер 617.14») — сопоставлять по имени ключа, а не по DisplayName.

### 1.4 Обнаружение установленных приложений (C#)

Полный код: `code/InstalledApps.cs` (проверен запуском: найдено 162 Uninstall-записи, Discord найден в HKCU, NVIDIA-драйвер 617.14 — по имени ключа).

- Читать **три корня**: `HKLM` 64-бит (`RegistryView.Registry64`), `HKLM` 32-бит (`RegistryView.Registry32` = WOW6432Node) и `HKEY_USERS\<SID>` **владельца рабочего стола**, а не `HKCU` процесса. Из повышенного процесса `HKCU` — профиль того, кто ввёл учётные данные в UAC; если повышали под другим админом, `HKCU` будет чужим.
- SID владельца рабочего стола: `GetShellWindow()` → `GetWindowThreadProcessId` → `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` → `OpenProcessToken(TOKEN_QUERY)` → `WindowsIdentity.User`. На машине: `S-1-5-21-…-1001`, `ElevatedAsDifferentUser() = False`.
- Сопоставление: точное имя подключа (`Discord`, `Steam`, `{58571ef5-…}_is1`) → regex по имени ключа → regex по `DisplayName` + `Publisher`.
- Пустой `DisplayVersion` (Riot) → `FileVersionInfo.ProductVersion` по `DisplayIcon` (формат `"C:\path\app.exe,0"`) или только «Установлено».

```csharp
public static List<InstalledApp> Enumerate()
{
    var list = new List<InstalledApp>();
    Scan(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64), "HKLM64", list);
    Scan(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32), "HKLM32", list);
    var sid = InteractiveUser.GetShellUserSid() ?? WindowsIdentity.GetCurrent().User?.Value;
    if (sid is not null)
    {
        using var hku = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
        using var userRoot = hku.OpenSubKey(sid);
        if (userRoot is not null) Scan(userRoot, "HKCU", list);
    }
    return list;
}
```

### 1.5 Запуск без повышения прав (C#)

Полный код: `code/Unelevated.cs`. Техника Raymond Chen (Microsoft, The Old New Thing, 2013): попросить **Explorer** запустить процесс — он работает с обычным токеном владельца рабочего стола. Цепочка:
`ShellWindows.FindWindowSW(CSIDL_DESKTOP=0, SWC_DESKTOP=8, SWFO_NEEDDISPATCH=1)` → `IServiceProvider.QueryService(SID_STopLevelBrowser, IID_IShellBrowser)` → `IShellBrowser.QueryActiveShellView` → `IShellView.GetItemObject(SVGIO_BACKGROUND, IID_IDispatch)` → `IShellFolderViewDual.Application` → `IShellDispatch2.ShellExecute(file, args, dir, "open", show)`.

Проверка (режим `readonly-probe`): объект `Shell.Application` из процесса Explorer **успешно получен**; сам `ShellExecute` не вызывался (правило «ничего не менять»).

```csharp
public static void Start(string file, string arguments, string? workingDir = null, int showCmd = 1)
{
    dynamic shell = GetDesktopShellApplication();
    // Порядок параметров IShellDispatch2.ShellExecute: (File, vArgs, vDir, vOperation, vShow)
    shell.ShellExecute(file, arguments, workingDir ?? Path.GetDirectoryName(file) ?? "", "open", showCmd);
}
```

Ограничения: нужен запущенный `explorer.exe` (не работает из службы/SYSTEM); PID и код выхода не возвращаются. **Запасной путь** (Explorer заменён/упал): временная задача Планировщика с `<LogonType>InteractiveToken</LogonType>` и `<RunLevel>LeastPrivilege</RunLevel>` для SID владельца рабочего стола (`schtasks /create /xml …`, `/run`, `/delete`) — **НЕ ПРОВЕРЕНО** (создание задачи меняет систему).

Когда запускать без повышения: `launch: "unelevated"` / `"unelevated-required"` в каталоге. Если `InteractiveUser.ElevatedAsDifferentUser() == true` — обязательно для всех per-user пакетов. winget-fallback для них тоже запускать без повышения (иначе Spotify через winget упадёт).

### 1.6 winget как запасной путь

- **Проверено:** все ID из §1.2 существуют (`winget show --id <ID> --exact --source winget`); ExitLag и NVIDIA App в winget **нет**; `Wagnardsoft.DisplayDriverUninstaller` есть (18.1.5.3 — отстаёт от 18.1.6.2).
- **Команда:**
  ```
  winget install --id <ID> --exact --source winget --silent --disable-interactivity
         --accept-package-agreements --accept-source-agreements [--scope machine|user] [--locale ru-RU]
  ```
  `--accept-*-agreements` юридически означает согласие пользователя — в UI перед кнопкой: «Нажимая «Установить», вы принимаете лицензионные условия разработчика приложения».
- **Наличие winget:** вызывать по алиасу `%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe` (App Execution Alias; работает из повышенного процесса **того же** пользователя, из SYSTEM — нет). На чистой Windows 10 22H2 «Установщик приложений» может быть устаревшим → `winget --version` с таймаутом 15 с; при ошибке: «Обновите «Установщик приложений» в Microsoft Store».
- **Когда winget НЕ поможет:** блокирован хост вендора — winget качает с того же URL (Discord, Riot, FACEIT). Помогает при смене схемы ссылок (Telegram), сломанном «latest»-редиректе, DNS-блоке одного хоста при другом хосте в манифесте (Brave: GitHub vs `laptop-updates`; Яндекс: `download.cdn.yandex.net` vs `browser.yandex.ru`).
- **Коды возврата winget** (официальный `returnCodes.md`):

| Hex | Символ | Смысл → действие |
|---|---|---|
| `0x8A150061` | `PACKAGE_ALREADY_INSTALLED` | уже установлено |
| `0x8A15002B` | `UPDATE_NOT_APPLICABLE` | обновлений нет |
| `0x8A150014` | `NO_APPLICATIONS_FOUND` | ID не найден → проверить каталог |
| `0x8A150010` | `NO_APPLICABLE_INSTALLER` | нет установщика под систему |
| `0x8A150008` | `DOWNLOAD_FAILED` | ошибка загрузки (часто блокировка) |
| `0x8A150011` | `INSTALLER_HASH_MISMATCH` | вендор обновил файл раньше манифеста — повторить позже |
| `0x8A150041` / `0x8A150046` | соглашения не приняты | добавить `--accept-*` |
| `0x8A15003A` / `0x8A15010F` | заблокировано политикой | сообщить пользователю |
| `0x8A150101` / `0x8A150103` | приложение/файл занято | «Закройте приложение» |
| `0x8A150102` | идёт другая установка | подождать |
| `0x8A150109` | нужна перезагрузка для завершения | предложить перезагрузку |
| `0x8A15010A` | перезагрузите и повторите | — |
| `0x8A15010C` | отменено пользователем | — |

### 1.7 Обработка сетевых ошибок (C#)

`code/Http.cs` → `DescribeRu(Exception, host)` различает через `HttpRequestException.HttpRequestError` (.NET 8+): `NameResolutionError` → «возможна блокировка провайдером или DNS-фильтром»; `SecureConnectionError` → «часто признак DPI-блокировки»; `ConnectionError`; HTTP 403 / 451 / 404; таймаут.

### 1.8 Доступность из РФ

Из России вживую **НЕ ПРОВЕРЯЛОСЬ** (IP проверки — Эстония). По открытым источникам (2026):

| Ресурс | Статус | Что делать в hateeze |
|---|---|---|
| `discord.com`, `*.discordapp.net` | Заблокирован РКН с 08–09.10.2024 | Предупреждение «нужен VPN»; winget не поможет |
| `telegram.org`, `td.telegram.org` | Работа Telegram официально ограничена с 10.02.2026 (задержки, звонки); доступность сайта — нет данных | Источник 1 — telegram.org, источник 2 — GitHub |
| `github.com`, `api.github.com`, `release-assets.githubusercontent.com`, `raw.githubusercontent.com` | Официально не блокируется (РКН, май 2026), но пользователи и OONI фиксировали сбои, особенно `*.githubusercontent.com` | Таймауты, понятная ошибка, зеркало для `latest.json` и exe (§3) |
| Steam | РКН 02.06.2026 опроверг блокировку; сбои 01.06.2026 | — |
| `nvidia.com`, `*.download.nvidia.com` | 10.2024: гео-блок загрузок драйверов («Access Forbidden», Edgecast WAF), позднее частично снят | Ошибка 403 → текст про гео-блок; зеркало `uk.download.nvidia.com` |
| Spotify | Сервис ушёл из РФ (2022) | Предупреждение |
| Epic Games | Штрафы РКН (2026), блокировки нет | — |
| Яндекс | Доступно | — |
| riotcdn.net, faceit-cdn.net, exitlag, battle.net, opera, vivaldi, brave | Данных нет | Общая обработка ошибок |

**DNS-фильтры у пользователя** (AdGuard, NextDNS, Pi-hole, родительский контроль) — реальный риск: на машине разработчика AdGuard DNS «обнулил» `get.geo.opera.com`. Сообщение об ошибке должно отличать DNS от блокировки/таймаута.

---

## 2. DDU (Display Driver Uninstaller, Wagnardsoft)

### 2.1 Когда это нужно (честная оценка)

| Сценарий | Оценка | Обоснование |
|---|---|---|
| Смена производителя видеокарты (AMD ↔ NVIDIA ↔ Intel), сбои установки драйвера, артефакты/чёрный экран после обновления, откат на старую версию | **situational** | Назначение по автору (Readme.txt DDU): «DDU should be used when having a problem uninstalling / installing a driver or when switching GPU brand» |
| DDU перед **каждым** обновлением драйвера «для FPS» | **avoid** (как рекомендацию не включать) | Readme.txt DDU: «DDU should not be used every time you install a new driver unless you know what you are doing». Измеримого прироста производительности нет |
| Блокировка автоустановки драйвера через Windows Update на время переустановки (`-PreventWinUpdate`) | **proven** как механизм (штатная настройка «Параметры установки устройств», `SearchOrderConfig`) | Readme.txt DDU: «You MUST disconnect your internet or completely block Windows Update when running DDU until you have re-installed your new drivers». После установки драйвера — вернуть |

**UI:**
- title: «Чистое удаление видеодрайвера (DDU)»
- description: «Полностью удаляет драйвер NVIDIA/AMD/Intel и его остатки с помощью Display Driver Uninstaller. ПК перезагрузится в безопасный режим, очистка пройдёт автоматически, затем ПК вернётся в обычный режим.»
- recommendation: «Используйте при смене видеокарты или проблемах с драйвером. Перед каждым обновлением драйвера это не нужно. Заранее скачайте новый драйвер. В безопасном режиме вход по PIN-коду Windows Hello может не работать — убедитесь, что помните пароль учётной записи.»

### 2.2 Официальный источник и последняя версия (проверено)

| Что | URL | Результат 2026-10-26 |
|---|---|---|
| Страница продукта | `https://www.wagnardsoft.com/display-driver-uninstaller-ddu` (старый адрес `…/display-driver-uninstaller-DDU-` → 301) | 200, без challenge; ссылки `/content/Download-Display-Driver-Uninstaller-DDU-18162` и т.д. |
| Страница релиза | `https://www.wagnardsoft.com/content/Download-Display-Driver-Uninstaller-DDU-18162` | «DDU 18.1.6.2», `datePublished 2026-10-21`; кнопка «DOWNLOAD & SUPPORT» → форум |
| Форум (ссылки, SHA-256) | `https://www.wagnardsoft.com/forums/viewtopic.php?t=5798` | **403, `Cf-Mitigated: challenge`** (Cloudflare managed challenge) — для автоматики непригоден |
| **Версия (машинный источник)** | `https://www.wagnardsoft.com/DDU/currentversion2.txt` | `text/plain`, 8 байт: `18.1.6.2`; этот же URL опрашивает сам DDU (`CheckUpdate.vb`, UA `DDU/<ver> (Display Driver Uninstaller)`, таймаут 5 с) |
| Версия (JSON) | `https://www.wagnardsoft.com/api/ddu/version.json` | `{"latestVersion":"18.1.6.2","releaseDate":"2026-10-21","downloadUrl":"https://download.wagnardsoft.com/DDU/DDU_v18.1.6.2.exe","releaseNotesUrl":"https://www.wagnardsoft.com/ddu/download/changelog.md","minimumSupportedVersion":"18.1.1.4"}` — **`downloadUrl` → 404, `releaseNotesUrl` → 404**; DDU сам этот ответ пока не использует |
| Устаревший файл версии | `https://www.wagnardsoft.com/DDU/currentversion.txt` | `9.9.3.0` (2016) — не использовать |
| **Портативный DDU** | `https://download.wagnardsoft.com/DDU/DDU%20v18.1.6.2.exe` | 200, `application/x-msdownload`, **1 381 040 Б**, `ETag "47e384b6e7eb279a7f2d32c628a83f92"`, `Last-Modified 2026-10-21 18:31:38 GMT` |
| Установщик DDU (NSIS) | `https://download.wagnardsoft.com/DDU/DDU%20v18.1.6.2_setup.exe` | 200, 1 852 984 Б |
| Старый путь | `https://www.wagnardsoft.com/DDU/download/DDU%20v{ver}.exe` | работает для ≤ 18.1.5.4 (18.1.5.3 и 18.1.5.4 → 200), для 18.1.5.5+ → 404 |
| winget | `Wagnardsoft.DisplayDriverUninstaller` | 18.1.5.3 (NSIS setup, `www.wagnardsoft.com/DDU/download/DDU%20v18.1.5.3_setup.exe`) — **отстаёт** |

**User-Agent обязателен:** без UA и с `curl/8.21.0` хосты Wagnardsoft отвечают `403` + `Cf-Mitigated: challenge`; с `hateeze/1.0` и браузерным UA — 200. `HttpClient` по умолчанию UA не шлёт → обязательно `DefaultRequestHeaders.UserAgent.ParseAdd("hateeze/1.0 …")`.

**Алгоритм** (`code/Ddu.cs`, `DduDownloader`): `currentversion2.txt` → `Version.TryParse` (сравнение покомпонентно, как в DDU: `18.1.5.10 < 18.1.6.0`) → при ошибке `version.json.latestVersion` → URL `download.wagnardsoft.com/DDU/DDU%20v{ver}.exe` → при 404 старый путь → при неудаче — winget-версия (с пометкой «устаревшая»). Проверка запуском (`ddu-version`): `DDU latest: 18.1.6.2`, аргументы по умолчанию `-silent -NoSafeModeMsg -CleanNvidia -RemovePhysx -PreventWinUpdate -NoRestorePoint`.

**Целостность:** SHA-256 публикуются только на форуме (закрыт challenge) → проверять **Authenticode**. Подпись 18.1.6.2 (разобрана из таблицы сертификатов PE, Range-запрос 11 536 Б): подписант **`CN=Wagnardsoft, O=Wagnardsoft, S=Quebec, C=CA`**, издатель «Sectigo Public Code Signing CA R36», сертификат действует 2026-05-15 … 2027-08-13, метка времени DigiCert 2026-10-21. После 2027-08 сертификат сменится — сравнивать **CN**, не отпечаток.

### 2.3 Формат файла и распаковка (проверено)

- `DDU v18.1.6.2.exe` — **стандартный GUI-модуль 7-Zip «7z.sfx»**: в ресурсах `FileDescription "7z SFX"`, `OriginalFilename "7z.sfx.exe"`, `Copyright (c) 1999-2026 Igor Pavlov`; сигнатура 7z `37 7A BC AF 27 1C` на смещении **215 040**.
- Ключи модуля (исходник `CPP/7zip/Bundles/SFXWin/SfxWin.cpp`):
  - `-y` — «да на всё», без диалога выбора папки, перезапись; **строго два символа** (`-yes`, `-y1` → «Bad command», код 1);
  - `-o<dir>` — папка распаковки **слитно** (`-oC:\x`; с пробелами — `"-oC:\Program Data\x"`, кавычки снимает `SplitCommandLine`);
  - `-p<pwd>` — пароль (не нужен).
  - Коды выхода: `0` успех, `2` ошибка извлечения (`kFatalError`), `255` отмена (`kUserBreak`), `8` нехватка памяти.
- Команда: `"C:\ProgramData\hateeze\ddu\DDU v18.1.6.2.exe" -y "-oC:\ProgramData\hateeze\ddu\18.1.6.2"` (в C# — `ProcessStartInfo.ArgumentList`).
- Структура внутри архива (есть ли подпапка `DDU v18.1.6.2\`) **НЕ ПРОВЕРЕНА** (нужно скачать файл целиком) → искать `Display Driver Uninstaller.exe` рекурсивно. По `AppPaths.vb`: настройки — `<папка DDU>\Settings\Settings.xml`, языки — `<папка DDU>\Settings\Languages\`, логи — `<папка DDU>\DDU Logs\`, cfg-списки — `<папка DDU>\settings\NVIDIA|AMD|INTEL…\*.cfg`; служба-копия — `C:\ProgramData\DisplayDriverUninstaller\`.
- Требования DDU 18.1.6.2: .NET Framework 4.8, Windows 7 SP1–11 x86/x64 (ARM64 — через эмуляцию x64), GPU NVIDIA/AMD/Intel/Qualcomm/Lisuan. Автор советует исключить папку DDU из антивируса и не запускать с сетевого диска.

```csharp
var psi = new ProcessStartInfo(sfx) { UseShellExecute = false, CreateNoWindow = true };
psi.ArgumentList.Add("-y");                 // 7z.sfx: только ровно "-y"
psi.ArgumentList.Add("-o" + outDir);        // "-o<путь>" слитно
using var p = Process.Start(psi)!;
if (!p.WaitForExit(120_000) || p.ExitCode != 0) throw new InvalidOperationException("Распаковка DDU не удалась");
var exe = Directory.EnumerateFiles(outDir, "Display Driver Uninstaller.exe", SearchOption.AllDirectories).First();
```

### 2.4 Аргументы командной строки — полный актуальный список

Источник: `display-driver-uninstaller/Display Driver Uninstaller/AppLaunchOptions.vb`, `github.com/Wagnard/display-drivers-uninstaller`, ветка `WPF` (push 2026-10-23). Разбор: `StrContainsAny(arg, True, "-X")` — **регистр не важен, проверяется вхождение подстроки**, побеждает первый совпавший `Case`. Неизвестные аргументы молча игнорируются.

| Аргумент | Действие |
|---|---|
| `-Silent` | Скрытое окно (при наличии `-Clean*`), без диалогов, без проверки обновлений DDU, **без проверки «ожидается перезагрузка Windows Update»**; после очистки — выход |
| `-Restart` | После очистки — перезагрузка (DDU запускает второй экземпляр `-CleanComplete -Restart`, первый выходит; второй ждёт до 15 с и перезагружает) |
| `-Shutdown` | После очистки — выключение |
| `-NoSafeModeMsg` | Не предлагать безопасный режим при запуске в обычном режиме |
| `-CleanNvidia` / `-CleanAmd` / `-CleanIntel` | Очистка драйвера GPU производителя |
| `-CleanQualcomm` / `-CleanLisuan` | GPU Qualcomm (Snapdragon X) / Lisuan |
| `-CleanRealtek` / `-CleanSoundBlaster` | **Аудио**-драйверы Realtek / Creative |
| `-cleanallgpus` | Все GPU + почти все `Remove*` (включая Vulkan и мониторы) — **агрессивно, не использовать** |
| `-PreventWinUpdate` | Отключает поиск драйверов в Windows Update: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching\SearchOrderConfig = 0` (Win7+). **Сохраняется после DDU** — вернуть `1` (значение по умолчанию; на машине разработчика сейчас `1`) |
| `-NoRestorePoint` | Не создавать точку восстановления |
| `-RemovePhysx` | NVIDIA: удалить PhysX (`display.physx`) |
| `-RemoveGFE` | NVIDIA: удалить GeForce Experience **и NVIDIA App** (`_display.gfexperience`, `_display.nvapp`, `_display.update`, ShadowPlay, «nvidia virtual audio device», службы из `settings\NVIDIA\gfeservice.cfg`) |
| `-RemoveNVBROADCAST` | NVIDIA: удалить NVIDIA Broadcast (службы `nvbservice.cfg`, устройства «NVIDIA Broadcast») |
| `-RemoveNVCP` | NVIDIA: удалить Панель управления NVIDIA (Win10+, Appx) |
| `-KeepNVCPopt` | NVIDIA: сохранить настройки Панели управления |
| `-RemoveNvidiaCache` | NVIDIA: кэш шейдеров |
| `-RemoveNvidiaDirs` | NVIDIA: папки NVIDIA |
| `-Remove3DTVPlay` | NVIDIA: 3DTV Play |
| `-RemoveAMDCP` / `-RemoveAMDDirs` / `-RemoveCrimsonCache` | AMD: панель / папки / кэш |
| `-RemoveAMDKMPFD` | AMD: фильтр-драйвер PCI (KMPFD) |
| `-RemoveAudioBus` | AMD: аудиошина (это **не** «удалить аудио») |
| `-RemoveINTELCP` / `-RemoveINTELIGS` / `-RemoveOneAPI` / `-RemoveEnduranceGaming` / `-RemoveIntelNpu` / `-RemoveIntelExt` | Intel: панель / IGS / oneAPI / Endurance Gaming / NPU / extension-пакеты производителя ноутбука |
| `-RemoveQualcommCP` / `-RemoveQualcommExt` / `-RemoveQualcommNpu` | Qualcomm: панель / extension / NPU |
| `-RemoveVulkan` | Удалить Vulkan runtime (общий для всех производителей — **не трогать**) |
| `-removemonitors` | Удалить устройства мониторов (сбросит профили) |
| `-ResetDisplayScale` | Сбросить масштаб дисплея |
| `-NoSetupAPI` | Не использовать SetupAPI (отладка) |
| `-cleancache` | Флаг `CleanCache` |
| `-CleanComplete` | Служебный (второй экземпляр после очистки) — **не передавать** |
| `-processkilled`, `-5648674614687`, `-visit*` | Служебные/ссылки — не передавать |

**Ключей нет:** `-logging`, `-RemoveAudio`, `-RemoveNvidiaFolders` (правильно `-RemoveNvidiaDirs`). Логи DDU пишет всегда (`DDU Logs\`).

**CLI перекрывает настройки GUI.** `ThreadSettings.New(fromCmdLineArgs:=True)` копирует сначала `Application.Settings`, затем **все** свойства `AppLaunchOptions` (по умолчанию `False`). Итог: при запуске с `-CleanNvidia` удаляется только то, что явно указано в командной строке, независимо от галочек в GUI.

**Ловушка подстрок:** из-за проверки по вхождению не добавлять «своих» аргументов, содержащих чужие имена.

**Рекомендованные наборы hateeze (переключатели в UI; по умолчанию — минимальный):**

| Производитель | Аргументы |
|---|---|
| NVIDIA (по умолчанию) | `-silent -NoSafeModeMsg -CleanNvidia -RemovePhysx -PreventWinUpdate -NoRestorePoint` |
| NVIDIA + удалить NVIDIA App/GFE | + `-RemoveGFE` |
| NVIDIA + Broadcast | + `-RemoveNVBROADCAST` (по умолчанию **выкл.**; на машине разработчика Broadcast 2.3.0 установлен) |
| AMD | `-silent -NoSafeModeMsg -CleanAmd -PreventWinUpdate -NoRestorePoint` (+ `-RemoveAMDCP` по желанию) |
| Intel | `-silent -NoSafeModeMsg -CleanIntel -PreventWinUpdate -NoRestorePoint` (extension-пакеты ноутбука DDU по умолчанию сохраняет — `-RemoveIntelExt` не включать) |

`-Restart` **не передаём**: после выхода DDU перезагружает hateeze (иначе гонка: DDU порождает второй экземпляр, и надёжно дождаться завершения нельзя).

Производитель — по `PNPDeviceID` видеоадаптера (`Win32_VideoController` или `HKLM\SYSTEM\CurrentControlSet\Enum\PCI`): `VEN_10DE` NVIDIA, `VEN_1002` AMD, `VEN_8086` Intel. На ПК с двумя GPU чистить только ту, чей драйвер переустанавливается.

### 2.5 Собственный режим DDU «перезагрузиться в безопасный режим и продолжить»

Есть — опция **`EnableSafeModeDialog`** (GUI: Options → «Enable Safe Mode dialog»; в `Settings\Settings.xml` элемент `<EnableSafeModeDialog>`; по умолчанию `False`). Устройство (по `Application.xaml.vb`: `HandleBootModeStartup`, `RestartToSafemode`, `InstallSafeBootService`, `AllowSafeBootServiceToRunInSafemode`):

1. При запуске DDU **в обычном режиме** (админ, не SYSTEM, без `-NoSafeModeMsg`) показывается окно: «Обычный / Безопасный режим / Безопасный режим с сетью».
2. При выборе Safe Mode DDU **сначала ставит страховки, `bcdedit` — последним**:
   - пытается создать точку восстановления;
   - копирует себя в `C:\ProgramData\DisplayDriverUninstaller\DDUSafeBootHandler.exe` и выполняет `sc create DDUSafeBootHandler binPath= "<path> /service" start= auto` (1073 «уже есть» → удалить и повторить; 1072 «помечена на удаление» → отмена). **Если службу создать не удалось — перезагрузка в Safe Mode отменяется**, машина не тронута;
   - регистрирует службу для Safe Mode: `HKLM\SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal\DDUSafeBootHandler` и `…\Network\DDUSafeBootHandler`, значение по умолчанию `"Service"`;
   - `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce`: `*Display Driver Uninstaller` = путь к DDU (**без аргументов**) и `*UndoSM` = `C:\Windows\system32\BCDEDIT /deletevalue safeboot`;
   - `bcdedit /set {current} safeboot minimal` (или `network`), сохраняет `UsedBCD=True`, перезагружает.
3. В Safe Mode служба `DDUSafeBootHandler` (`DDUSafeBootService.vb`) стартует **до входа пользователя**, ждёт 2 с, делает до 3 попыток `bcdedit /deletevalue safeboot` с проверкой кода выхода (каждая ≤ 20 с), затем удаляет себя (`sc delete`). Не удалось **и** система в Safe Mode — остаётся до следующей загрузки. Смысл (комментарий автора): единственная страховка для того, кто **не может войти** в Safe Mode (забыл пароль, PIN недоступен).
4. После входа DDU открывается (RunOnce), сам ещё раз снимает safeboot (если `UsedBCD`), а **пользователь** жмёт «Удалить и перезагрузить». Автоматического продолжения очистки нет.

Вывод: встроенный режим годится как «ручной» вариант. Для полностью автоматической очистки — своя оркестрация по той же схеме (§2.6). При нашей схеме DDU safeboot **не снимет** (`UsedBCD=False`) — это делаем мы.

### 2.6 Рекомендуемый сценарий hateeze (автоматический)

Код: `code/Ddu.cs` (`DduOrchestrator`, `SafeBoot`, `DduDownloader`) и `code/GuardService.cs`. Состояние — `C:\ProgramData\hateeze\ddu-session.json` (атомарная запись `.tmp` + `File.Move`). Стадии: `Armed → InSafeMode → Cleaned → Done` (+ `Failed`, `Cancelled`).

```
[Обычный режим]                      [Safe Mode, до входа]            [Safe Mode, после входа админа]       [Обычный режим]
Preflight → скачать DDU → служба     служба hateezeSafeBootGuard:     RunOnce *hateezeDDU:                  при старте hateeze:
+ SafeBoot\Minimal + RunOnce *       bcdedit /deletevalue safeboot    снять safeboot (повторно) →           уборка, «установите драйвер»,
+ state=Armed → bcdedit /set         ×3 → удалить себя                DDU -silent … (ждать ≤30 мин) →       вернуть SearchOrderConfig
safeboot minimal → shutdown /r                                         уборка → shutdown /r
```

**Шаг 0. Предпроверки** (`DduOrchestrator.Preflight()`):
- `GetSystemMetrics(SM_CLEANBOOT = 67) == 0` (0 — обычный, 1 — безопасный, 2 — с сетью);
- нет ожидающей перезагрузки: ключи `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired`, `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending`, значение `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\PendingFileRenameOperations` (DDU с `-Silent` эту проверку **пропускает**);
- нет незавершённой сессии;
- предложить заранее скачать новый драйвер (в Safe Mode сети нет, а с `-PreventWinUpdate` Windows не подтянет драйвер сама);
- предупредить о PIN-коде Windows Hello;
- BitLocker: `safeboot` (0x25000080) по Microsoft Learn — в списке **игнорируемых** BitLocker параметров BCD → приостанавливать BitLocker не нужно; ключ восстановления иметь под рукой всё равно советуем;
- точку восстановления создавать **до** перезагрузки (в Safe Mode System Restore точки не создаёт); Windows ограничивает частоту (по умолчанию раз в 24 ч, `SystemRestorePointCreationFrequency`) — **НЕ ПРОВЕРЕНО** на этой машине.

**Шаг 1. Скачать/распаковать DDU** (§2.2–2.3), проверить Authenticode `Wagnardsoft` у SFX и у `Display Driver Uninstaller.exe`.

**Шаг 2. Взвести безопасный режим** (`DduOrchestrator.ArmAndReboot()`), порядок как у DDU:
1. копия своего exe в `C:\ProgramData\hateeze\guard\hateeze.exe` (не `%TEMP%` — его чистят);
2. `sc create hateezeSafeBootGuard binPath= "\"C:\ProgramData\hateeze\guard\hateeze.exe\" --safeboot-guard" start= auto DisplayName= "hateeze Safe Mode guard"` (код 1073 — допустим);
3. `HKLM\SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal\hateezeSafeBootGuard` и `…\Network\hateezeSafeBootGuard`, `(Default) = "Service"`;
4. `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce` → `*hateezeDDU` = `"C:\ProgramData\hateeze\guard\hateeze.exe" --ddu-stage2` (≤ 260 символов — лимит из документации; `*` = выполнять и в Safe Mode);
5. запись сессии (`Armed`, исходное `SearchOrderConfig`, аргументы DDU, путь к DDU);
6. **точка невозврата:** `bcdedit /set {current} safeboot minimal`; код ≠ 0 ⇒ откат пунктов 1–4 и ошибка в UI;
7. `shutdown /r /t 10 /c "hateeze: перезагрузка в безопасный режим для DDU" /d p:4:1`.

**Шаг 3. Safe Mode, до входа** — служба `--safeboot-guard` (P/Invoke SCM, без NuGet): через 2 с `bcdedit /deletevalue {current} safeboot` до 3 раз с проверкой по кусту BCD; затем удаляет свои ключи SafeBoot и себя. Текущая загрузка остаётся безопасной, **следующая — обычной при любом исходе**.

**Шаг 4. Safe Mode, после входа администратора** — `hateeze.exe --ddu-stage2` (`DduOrchestrator.Stage2()`, без WebView2):
1. ещё раз (идемпотентно) снять safeboot;
2. если вдруг не Safe Mode → `Failed`, уборка, выход;
3. запустить `Display Driver Uninstaller.exe <аргументы>` и ждать до 30 мин;
4. записать результат, удалить службу/ключи/RunOnce;
5. если safeboot точно снят — `shutdown /r /t 15 …`.

**Шаг 5. Обычный режим** — `DduOrchestrator.OnAppStartNormalMode()` при каждом старте:
- `Armed` + safeboot стоит + обычный режим ⇒ перезагрузки не было (пользователь отменил `shutdown /a`) ⇒ спросить «Перезагрузиться сейчас» / «Отменить» (отмена = `SafeBoot.Disarm()` + `RemoveGuardAndRunOnce()`);
- иначе — идемпотентно снять safeboot и убрать службу/RunOnce;
- `Cleaned` ⇒ экран «Установите новый драйвер» (+ кнопка открыть заранее скачанный файл); после установки вернуть `SearchOrderConfig` к сохранённому (по умолчанию `1`).

**Почему надёжно:** (а) служба снимает safeboot до входа — спасает забывших пароль; (б) RunOnce с `*` выполняется в Safe Mode, но **только при входе члена группы «Администраторы»** (Microsoft Learn); (в) при каждом старте приложения — проверка состояния; (г) `bcdedit /set` — последним: сбой на любом раньшем шаге не меняет загрузку.

**Ключевые сигнатуры (C#):**

```csharp
// Ddu.cs
public static class SafeBoot
{
    public static int CurrentBootMode { get; }          // GetSystemMetrics(SM_CLEANBOOT = 67): 0/1/2
    public static bool IsSafeBootArmed();               // HKLM\BCD00000000\Objects\{guid}\Elements\25000080 существует
    public static string? CurrentLoaderGuid();          // первый GUID из "bcdedit /enum {current} /v" (подписи полей локализованы!)
    public static bool Arm(bool withNetwork);           // bcdedit /set {current} safeboot minimal|network → exit 0
    public static bool Disarm();                        // bcdedit /deletevalue {current} safeboot ×3 + проверка по кусту BCD
}
public static class DduOrchestrator
{
    public static List<string> Preflight();
    public static void ArmAndReboot(string dduExe, DduOptions o);
    public static int  Stage2();                        // из RunOnce: hateeze.exe --ddu-stage2
    public static void OnAppStartNormalMode();
    public static void RemoveGuardAndRunOnce();
}
// GuardService.cs
public static class GuardService { public static int Run(); } // hateeze.exe --safeboot-guard (запускает SCM)

// Program.Main — ДО инициализации WebView2:
if (args.Contains("--safeboot-guard")) return GuardService.Run();
if (args.Contains("--ddu-stage2"))     return DduOrchestrator.Stage2();
```

Проверка запуском (`readonly-probe`, ничего не меняет): `BootMode(SM_CLEANBOOT): 0`, `Current loader GUID: {d76ea61e-8cf9-11f1-98a5-bdd1ac23bfe5}` (совпадает с `HKLM\BCD00000000\Objects\{9dea862c-5cdd-4e70-acc1-f32b344d4795}\Elements\23000003` — default у bootmgr), `safeboot armed: False`, `reboot pending: False`.

**Почему не парсим вывод `bcdedit`:** на русской Windows подписи полей локализованы («идентификатор», «устройство»…). Куст `HKLM\BCD00000000` читается из админ-процесса (проверено `reg query`), элемент `25000080` = `safeboot` (0 Minimal, 1 Network, 2 DsRepair).

**Альтернатива — одноразовая загрузка `bcdedit /bootsequence`.** Скопировать запись (`bcdedit /copy {current} /d "hateeze Safe Mode"`), поставить ей `safeboot minimal`, не добавлять в `displayorder` и выполнить `bcdedit /bootsequence {guid}` — Boot Manager загрузит её **один раз** (Microsoft Learn: «sets the one-time boot sequence»), следующая загрузка вернётся к записи по умолчанию — застрять невозможно. Минусы: у копии другой GUID, поведение с BitLocker/Secure Boot не подтверждено, запись нужно удалять. **НЕ ПРОВЕРЕНО** (запрещено менять BCD) — рассматривать только после тестов на VM с BitLocker.

### 2.7 Аварийный возврат — тексты для пользователя (справка в UI)

1. Обычно делать ничего не нужно: следующая перезагрузка будет в обычном режиме (страховочная служба снимает безопасный режим ещё до входа).
2. Если ПК всё равно загружается в безопасный режим: войдите с паролем → Win+R → `msconfig` → вкладка «Загрузка» → снимите «Безопасный режим» → ОК → перезагрузка. Или командная строка от администратора: `bcdedit /deletevalue {current} safeboot`.
3. Если войти невозможно: на экране входа удерживайте Shift и выберите «Перезагрузка» (или загрузитесь с установочной флешки) → «Поиск и устранение неисправностей» → «Дополнительные параметры» → «Командная строка» → `bcdedit /deletevalue {default} safeboot` (в среде восстановления — `{default}`, а не `{current}`; при нескольких ОС найдите идентификатор командой `bcdedit`). Это официальная инструкция DDU (`Issues and solutions.txt`); крайняя мера — `bootrec /rebuildbcd`.
4. Если после возврата Windows «ведёт себя как в безопасном режиме» (нет сети, низкая производительность): убедиться, что в `msconfig` нет галочки, а в `bcdedit` нет строки `safeboot`; затем удалить подключ `HKLM\SYSTEM\CurrentControlSet\Control\SafeBoot\Option` и перезагрузиться. (В `Issues and solutions.txt` опечатка «Options» — код DDU удаляет именно `Option`.)
5. Не использовать `safebootalternateshell` (входит в профиль проверки BitLocker → риск запроса ключа восстановления).

### 2.8 Проверка результата очистки

- Коды выхода DDU (WPF) и формат лога **НЕ ПРОВЕРЕНЫ** (нельзя запускать DDU). Ориентироваться на:
  - лог в `<папка DDU>\DDU Logs\`;
  - состояние после перезагрузки: у видеоадаптера с `VEN_10DE`/`VEN_1002`/`VEN_8086` драйвер «Microsoft Basic Display Adapter»; в `pnputil /enum-drivers /class Display` нет INF производителя (например `nv_dispi.inf`); у NVIDIA пропал ключ `{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}_Display.Driver`.
- Для отчёта сохранять `ddu-session.json` и путь к логу DDU.

---

<!-- PART7 -->
