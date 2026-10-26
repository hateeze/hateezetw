# Установщики драйверов AMD (Radeon Adrenalin, Ryzen Chipset) и Intel — исследование для hateeze

> Ключ темы: `amd-installers`. Дата проверки: **2026-10-26** (запросы ~04:15–04:50 UTC).
> Машина: Windows 11 Pro for Workstations 23H2 (22631), Ryzen 7 5700X, ASRock A520M-HVS, **RTX 4060 (AMD GPU нет)**.
> В системе **ничего не менялось**: только `reg query`, `Get-CimInstance`, `pnputil /enum-drivers`, `powercfg /l`, чтение файлов, HTTP `HEAD`/`GET`.
> Установщики **не скачивались целиком и не запускались**. Структура пакетов получена чтением заголовков 7z через HTTP Range: десятки КБ на пакет, плюс один диапазон 3 МБ для `cccmanifest_64.json`.
> Снимки манифестов AMD для юнит-тестов лежат в `docs/research/amd-installers-fixtures/`.

---

## 0. Главное (TL;DR)

1. **У AMD есть машиночитаемый источник последней версии**, и его использует сам установщик/Adrenalin:
   `https://drivers.amd.com/drivers/installer/json/version.json` ведёт на JSON-файлы `DrvDldDetails_*.json`. В них есть версия, прямая ссылка на полный и минимальный установщик, дата, release notes и **список поддерживаемых `DEV-REV`** (`skus`). Проверено вживую. HTML парсить не нужно.
2. **Без заголовка `Referer` с `www.amd.com` (или `drivers.amd.com`) сервер `drivers.amd.com` отвечает `302` на `https://www.amd.com/en/resources/support-articles/faqs/Download-Incomplete.html`.** Это относится к exe и к JSON. `User-Agent` для `drivers.amd.com` не нужен. Подходит `Referer: https://www.amd.com/` (принимаются `http://www.amd.com/`, `https://www.amd.com` без слэша и любой путь на `www.amd.com`). Не подходят `https://amd.com/` и `https://www.google.com/`. Поддерживаются `Range` (206) и `If-Modified-Since` (304).
3. **`www.amd.com` (HTML-страницы) «вешает» соединение (тарпит, ответа нет даже через 20 с) для UA вида `curl/*`, `Wget/*`, `python-requests/*`.** Без заголовка UA или с честным собственным UA (`hateeze/1.0`) страницы отдаются за ~0.5 с. Выводы: (а) задавать UA `hateeze/<версия>`; (б) **не следовать автоматически** 302 на `Download-Incomplete.html` — это признак отсутствия Referer.
4. **С октября 2025 года драйверы Radeon поставляются в 3+1 пакетах** (проверено на 26.10.1 от 2026-10-20):

   | Ветка | Ключ в `version.json` | Файл 26.10.1 | Драйвер (internal) | Driver Store | Размер |
   |---|---|---|---|---|---|
   | RDNA3/RDNA4 + APU RDNA3/3.5 | `Consumer_Whql_Win10_productionASICs` | `whql-amd-software-adrenalin-edition-26.10.1-win11-b.exe` | 26.20.15.07 | 32.0.32015.7029 | 1 026 657 048 Б |
   | RDNA1/RDNA2 + APU RDNA2 («Maintenance») | `Consumer_Maintenance_Win10` | `...-26.10.1-win11-a.exe` | 25.10.45.16 | 32.0.21045.16010 | 1 002 639 984 Б |
   | Комбинированный (все RDNA, два драйвера внутри) | `Consumer_Whql_Win10_AllAsics` (и `Consumer_Whql_Win10`) | `...-26.10.1-win11-c.exe` | оба | оба | 1 782 243 424 Б |
   | Polaris/Vega + APU Vega (Legacy) | `Consumer_Legacy_Win10` | `whql-amd-software-adrenalin-edition-26.5.2-win11-may-vega-polaris.exe` | 23.19.25.01 | 31.0.21925.1001 | 653 644 328 Б |

   Имена файлов **непредсказуемы**: суффиксы `-a`/`-b`/`-c`/`-s`/`-nov`/`-oct-rdna3`, месяцы, дни. Ссылку всегда брать из JSON (§2.5).
5. **Ветка по Device ID определяется точно**: `skus` в JSON имеют формат `"744C-C8"` (`DEV-REV`). Наборы DEV у веток не пересекаются (проверено). Если в системе есть устройства из RDNA1/2 и из RDNA3/4 (например iGPU Ryzen 7000 `164E` + RX 9070 `7550`), нужен **комбинированный `-c`**.
6. **Внутри пакета (7z-SFX; LZMA2+BCJ)** база драйвера лежит в `Packages\Drivers\Display\WT6A_INF\u0<CL>.inf`, где `<CL>` — 6 цифр из `internalbuildversion` (`...-205306C-...` → `u0205306.inf`). Рядом лежат подпапки INF-расширений (`amdocl`, `amdogl`, `amdvlk`, `amdwin`, `amdfendr`, `amdxe`, `amdafd`, `amdfdans`, `amdpcibridge`). В комбинированном пакете добавляется `Packages\Drivers\Display2\WT6A_INF\u0205305.inf`.
   «Только драйвер» ставится так: `pnputil /add-driver <...>\WT6A_INF\*.inf /subdirs /install`. Флаг `/subdirs` **обязателен**, иначе не встанут ICD OpenGL/Vulkan/OpenCL, которые для RDNA3/4 вынесены в extension-INF.
7. **Тихая установка Adrenalin**: документ AMD (2019, CIM-инсталлятор, ссылка на него до сих пор есть в RSX2-INSTALL) описывает `Setup.exe -INSTALL [-BOOT] [-OUTPUT detail|screen|<file>] [-LOG <file>]` и `-UNINSTALL ALL`. Результат пишется в лог `[ResponseResult] ResultCode=0|1|2`.
   SFX-обёртка `whql-...exe` содержит строки `-INSTALL`, `-SILENT`, `-factoryresetInstall` и распаковывает в `\AMD\AMD-Software-Installer`. **Запуск на 26.x вживую НЕ ПРОВЕРЕН**: на машине нет AMD GPU, а запуск установщиков запрещён.
8. **Чипсет AMD**: последняя версия **8.09.28.1124** (2026-10-21), `https://drivers.amd.com/drivers/amd_chipset_software_8.09.28.1124.exe` (81 551 288 Б, путь регистронезависим). Ссылка одинакова на страницах всех чипсетов AM4/AM5/TRX. JSON-источника нет (проверены 7 вариантов имён), версию берём из HTML регуляркой.
   Установщик — **NSIS** (сигнатура `0xDEADBEEF "NullsoftInst"` проверена), подписан `CN=Advanced Micro Devices`. Тихая установка: `/S` (AMD RN 2.04.04.111; Chocolatey использует `/S` с 8.08.12.551). Тихое удаление: `/S /EXPRESSUNINSTALL=1`.
9. **Нужен ли чипсет на Win11**: для **X3D с двумя CCD** (7900X3D/7950X3D/9900X3D/9950X3D) — **обязательно** (proven): драйвер 3D V-Cache Performance Optimizer + PPM Provisioning + Game Mode + Xbox Game Bar. Для 5700X на A520 — situational: корректные драйверы PSP/SMBus/GPIO/PCI и PPM provisioning, измеренного прироста FPS нет.
10. **Intel** (кратко): Chipset INF 10.1.20658.8883 — `https://downloadmirror.intel.com/925343/SetupChipset.exe`, тихо `SetupChipset.exe /exenoui /qn /norestart`. Intel сама пишет, что обновлять его не нужно. Arc 32.0.101.9150 — `https://downloadmirror.intel.com/930514/gfx_win_101.9150.exe` (RAR5-SFX), тихо `Installer.exe -s`. DSA — `https://dsadata.intel.com/installer`.
    Страницы `www.intel.com/content/...` отдают **403** скриптовым клиентам. Ссылки mirror не угадываются (числовой id), обходить защиту нельзя (§4).

---

## 1. Методика и ограничения

- Все URL проверены `curl 8.22 (Schannel)` с машины разработчика; коды ответов приведены дословно.
- Состав пакетов: по PE-заголовку найден оверлей (`1 785 344` для полных пакетов, `1 899 520` для minimal). В нём сигнатура 7z `37 7A BC AF 27 1C`. Прочитан `NextHeader` (39 байт, `kEncodedHeader`), затем сжатый заголовок (10–12 КБ, LZMA). Он распакован и разобран собственным парсером. Так получен полный список файлов с размерами **без скачивания пакета**.
- Текст INF **прочитать не удалось**: INF лежат глубоко в solid-блоке. Для `u0205306.inf` пришлось бы распаковать ~520 МБ потока. Поэтому всё, что зависит от содержимого INF (AddService, AddSoftware/ccc2_install), помечено **НЕ ПРОВЕРЕНО**.
- Установка драйверов, детект установленного Radeon и реестр Radeon вживую **не проверялись**: на машине NVIDIA.

---

## 2. Radeon Adrenalin

### 2.1. Ветки драйверов (состояние на 2026-10-26)

| Ветка (enum в коде) | Ключ `version.json` | JSON | `externalbuildversion` | `internalbuildversion` | Дата | `fullbuild` | `minimalbuild` |
|---|---|---|---|---|---|---|---|
| `Rdna3Rdna4` | `Consumer_Whql_Win10_productionASICs` | `DrvDldDetails_Consumer_WHQL_Win11_p.json` | 26.10.1 | `26.20.15.07-261015a-205306C-AMD-Software-Adrenalin-Edition` | 10/20/2026 | `.../whql-amd-software-adrenalin-edition-26.10.1-win11-b.exe` | `.../installer/26.20/whql/amd-software-adrenalin-edition-26.10.1-minimalsetup-261018_64bit.exe` (47 859 008 Б) |
| `Rdna1Rdna2` | `Consumer_Maintenance_Win10` | `DrvDldDetails_Consumer_RDNA1_RDNA2_Win11.json` | 26.10.1 | `25.10.45.16-261009a-205305C-...` | 10/20/2026 | `...-26.10.1-win11-a.exe` | `.../installer/25.10/whql/amd-software-adrenalin-edition-26.10.1-minimalsetup-261016_64bit.exe` (43 590 176 Б) |
| `AllRdnaCombined` | `Consumer_Whql_Win10_AllAsics` (+ `Consumer_Whql_Win10` указывает на тот же набор) | `DrvDldDetails_Consumer_WHQL_Win11_c.json` (байт-в-байт = `DrvDldDetails_Consumer_WHQL_Win11.json`) | 26.10.1 | `26.20.15.07-...` | 10/20/2026 | `...-26.10.1-win11-c.exe` | `.../installer/26.20/whql/amd-software-adrenalin-edition-26.10.1-combined-minimalsetup-261018_64bit.exe` (47 868 824 Б) |
| `PolarisVega` | `Consumer_Legacy_Win10` | `DrvDldDetails_Consumer_Legacy_Win10.json` | 26.5.2 | `23.19.25.01-260423a-201039C-...` | 05/21/2026 | `...-26.5.2-win11-may-vega-polaris.exe` | `.../installer/23.19/whql/amd-software-adrenalin-edition-26.5.2-minimalsetup-260520_64bit.exe` |

Прочие ключи `version.json` для потребительских GPU не нужны:
- `Consumer_Whql_Win7` — `21.09.13.02-220802a-382933C-RadeonSoftware` (Adrenalin 22.6.1 для Win7);
- `Workstation_*` — PRO Edition;
- `Falcon_Whql_Win10` — Boot Camp.

Что важно знать о ветках:
- **Legacy (Polaris/Vega) выделена с Adrenalin 23.9.2/23.11.1 (ноябрь 2023)**, а не с 2024. Последний общий драйвер — 23.8.2 (`...-23.8.2-win10-win11-aug31.exe`). Дальше: 23.11.1 `...-nov3-vega-polaris.exe`, 24.3.1, 24.9.1, 25.5.1, 25.8.1, 26.1.1 `...-win11-jan-vega-polaris.exe`, **26.5.2** (текущий). Выходят несколько раз в год.
- **RDNA1/RDNA2 выделены в «maintenance»-ветку с 25.11.1 (2025-11-13)**: RDNA1/2 — `...-25.11.1-win11-s.exe`, RDNA3/4 — `...-25.11.1-win11-nov.exe`. С 25.12.1 используются суффиксы `-a` (RDNA1/2) и `-b` (RDNA3/4). Номер версии Adrenalin у веток общий (26.10.1), а **драйверы разные** (25.10.x против 26.20.x). Новые функции, например FSR 4.1 для RX 6000 в 26.10.1, при этом в RDNA2 всё же приходят.
- 25.10.2 (2025-10-29) был единым combined-пакетом `...-25.10.2-win10-win11-oct-rdna3-combined.exe` (драйвер 25.20.21.01, DS 32.0.22021.1009). С тех пор `DrvDldDetails_Consumer_WHQL_Win10.json` **заморожен** на 25.10.2: `version.json` перенаправляет ключ `Consumer_Whql_Win10` на `..._Win11.json`. **Нельзя хардкодить имя JSON**, нужно идти через `version.json`.
- В страницах продукта и JSON указаны Windows 10 и 11 с одним и тем же файлом `win11-*`. В RN 26.10.1 заявлено: «Windows 11 21H2 и новее, Windows 10 64-bit 21H2 и новее». Известная проблема 26.10.1: «AMD Smart Access Memory may become disabled after driver installation on Windows 10».

### 2.2. Машиночитаемый источник: `version.json` → `DrvDldDetails_*.json`

**Шаг 1.** `GET https://drivers.amd.com/drivers/installer/json/version.json` с заголовком `Referer: https://www.amd.com/`. Ответ: `200`, `Content-Type: application/json`, 2656 Б, `Last-Modified: Tue, 20 Oct 2026 15:06:35 GMT`. BOM нет.

```json
{
  "ADDL_VERSION": "1.0",
  "DLL_URL": "NA",
  "Consumer_Whql_Win10": { "version": "26.20.15.07-261015a-205306C-AMD-Software-Adrenalin-Edition",
                           "url": "https://www2.ati.com/drivers/installer/json/DrvDldDetails_Consumer_WHQL_Win11.json" },
  "Consumer_Legacy_Win10": { "version": "23.19.25.01-260423a-201039C-AMD-Software-Adrenalin-Edition",
                             "url": "https://www2.ati.com/drivers/installer/json/DrvDldDetails_Consumer_Legacy_Win10.json" },
  "Consumer_Whql_Win7": { "version": "21.09.13.02-220802a-382933C-RadeonSoftware", "url": ".../DrvDldDetails_Consumer_WHQL_Win7.json" },
  "Consumer_Whql_Win10_AllAsics": { "version": "26.20.15.07-...", "url": ".../DrvDldDetails_Consumer_WHQL_Win11_c.json" },
  "Consumer_Maintenance_Win10": { "version": "25.10.45.16-261009a-205305C-...", "url": ".../DrvDldDetails_Consumer_RDNA1_RDNA2_Win11.json" },
  "Consumer_Whql_Win10_productionASICs": { "version": "26.20.15.07-...", "url": ".../DrvDldDetails_Consumer_WHQL_Win11_p.json" },
  "Workstation_Whql_Win10": { ... }, "Workstation_Whql_Win10_AllAsics": { ... }, "Workstation_Maintenance_Win10": { ... },
  "Workstation_Whql_Win10_productionASICs": { ... }, "Workstation_Legacy_Win10": { ... }, "Workstation_Whql_Win7": { ... },
  "Falcon_Whql_Win10": { "version": "21.30.45.22", "url": ".../DrvDldDetails_BootCamp_WHQL_Win10.json" }
}
```

Особенности:
- В корне есть **не-объекты** (`"ADDL_VERSION": "1.0"`, `"DLL_URL": "NA"`). Парсить через `JsonDocument` и брать только `JsonValueKind.Object`.
- `url` указывает на `https://www2.ati.com/...`. Этот хост отвечает **`301` на `https://drivers.amd.com/<тот же путь>`**. В коде проще сразу заменить хост на `drivers.amd.com`.
- Поле `version` равно `internalbuildversion` целевого JSON. По нему можно не скачивать детали, если версия не изменилась.
- Источник упоминается в сообществе AMD как первый запрос, который Adrenalin делает при проверке обновлений: `http://drivers.amd.com/drivers/installer/json/version.json`. Сейчас HTTP отвечает `301` на HTTPS.

**Шаг 2.** `GET` по `url` (тоже с Referer). Ответ — **массив** из одного объекта:

```json
[{
  "buildhighlights": "https://www2.ati.com/drivers/installer/26.20/whql/buildhighlights-26.10.1.0.xml",
  "edition": "AMD Software: Adrenalin Edition",
  "externalbuildversion": "26.10.1",
  "fullbuild": "https://www2.ati.com/drivers/whql-amd-software-adrenalin-edition-26.10.1-win11-b.exe",
  "fullbuildfilesize": "979.096458435059",
  "internalbuildversion": "26.20.15.07-261015a-205306C-AMD-Software-Adrenalin-Edition",
  "manifestfile": "https://www2.ati.com/drivers/installer/26.20/whql/cimmanifest-26.10.1.0.exe",
  "minimalbuild": "https://www2.ati.com/drivers/installer/26.20/whql/amd-software-adrenalin-edition-26.10.1-minimalsetup-261018_64bit.exe",
  "releasedate": "10/20/2026",
  "releasenotes": "https://www.amd.com/en/resources/support-articles/release-notes/rn-rad-win-26-10-1.html",
  "skus": ["1114-00", "1114-C1", "...", "7590-DF"]
}]
```

| Поле | Тип/формат | Замечания |
|---|---|---|
| `externalbuildversion` | `"26.10.1"` | Версия Adrenalin для UI |
| `internalbuildversion` | `"26.20.15.07-261015a-205306C-..."` | Первая часть до `-` = «Driver Version» в RN. Число перед `C` (`205306`) = номер базового INF `u0205306.inf` |
| `fullbuild` | URL на `www2.ati.com` | Полный оффлайн-пакет 7z-SFX |
| `fullbuildfilesize` | **строка**, MiB float | `979.096458435059` × 1048576 = 1 026 657 048 Б (совпадает с `Content-Length`). Точный размер брать из `HEAD` |
| `minimalbuild` | URL | Веб-установщик (~43–48 МБ), докачивает компоненты |
| `manifestfile` | URL `cimmanifest-<ver>.0.exe` | 1 937 472 Б, внутренний манифест CIM; нам не нужен |
| `buildhighlights` | URL XML **UTF-16 LE с BOM** | Локализованные «хайлайты» + списки стран для аналитики; нам не нужен |
| `releasedate` | `MM/dd/yyyy` | Парсить `DateTime.ParseExact(s, "MM/dd/yyyy", CultureInfo.InvariantCulture)` |
| `releasenotes` | URL (lowercase) | Для Legacy указывает на **общие** RN (`rn-rad-win-26-5-2.html`), где драйверы 26.10.07.05/25.10.43.10, а **не** 23.19.25.01. Для Polaris/Vega правильный RN — `RN-RAD-WIN-26-5-2-POLARIS-VEGA.html` (§2.4) |
| `skus` | массив `"DDDD-RR"` (hex, upper) | DEV и REV из `PCI\VEN_1002&DEV_DDDD&...&REV_RR`. REV `00` встречается реально (Pro-карты, Krackan `1114-00`), это не wildcard |

Чего **в JSON нет**: признака «Recommended/Optional», хэшей (SHA), точного размера в байтах. Recommended/Optional виден только в HTML страницы продукта, в тексте `Revision Number`: «Adrenalin 26.10.1 WHQL (Recommended)», «Adrenalin 26.9.2 WHQL (Optional)». Варианты `version_beta.json`, `version_optional.json`, `versions.json`, `version.xml`, `DrvDldDetails_Consumer_Beta_Win11.json`, `DrvDldDetails_Consumer_Optional_Win11.json` дают **404**. Скорее всего, в `version.json` всегда лежит последняя версия, Optional она или Recommended (**НЕ ПРОВЕРЕНО**: неизвестно, попадали ли туда Optional 26.9.1/26.9.2).

RSS-ленты release notes не найдено. `update.amd.com` не отвечает (нет ответа/не резолвится). GPUOpen источника версий не даёт.

### 2.3. Резервный источник: HTML страницы продукта

Если JSON сломается, ту же информацию можно получить со страницы продукта:
`https://www.amd.com/en/support/downloads/drivers.html/graphics/<серия>/<подсерия>/<модель>.html`
(например `.../graphics/radeon-rx/radeon-rx-9000-series/amd-radeon-rx-9070-xt.html`).

- UA должен быть **не** curl/wget/python-requests (§2.4). Страница ~190 КБ.
- Ссылки на exe в атрибутах `href`. Регулярка: `https://drivers\.amd\.com/drivers/[^"'\s<>]+\.exe`.
- Рядом в тексте: `Revision Number Adrenalin 26.10.1 WHQL (Recommended) File Size 887 MB Release Date 2026-10-20`. «887 MB» на сайте не совпадает ни с одним файлом (`-b` = 979 MiB), этому полю не доверять.
- Секции «Windows 11 - 64-Bit Edition» и «Windows 10 - 64-Bit Edition» дают **один и тот же** файл.
- Дерево всех продуктов встроено в `https://www.amd.com/en/support/download/drivers.html` как HTML-экранированный JSON (`{&#34;productTypes&#34;:...}`, 825 КБ, ~1500 URL). Пути Polaris нестандартные: `.../graphics/radeon-600-500-400/radeon-rx-500-series/radeon-rx-580.html`.
- Архив версий: `.../support/downloads/previous-drivers.html/<тот же путь>.html`.

### 2.4. HTTP-требования (проверено вживую)

| Запрос | Результат |
|---|---|
| `HEAD/GET drivers.amd.com/...exe` **без Referer** (любой UA) | `302 Moved Temporarily`, `Content-Length: 0`, `Location: https://www.amd.com/en/resources/support-articles/faqs/Download-Incomplete.html`, `Server: AkamaiGHost` |
| То же с `Referer: https://www.amd.com/` | `200 OK`, `Accept-Ranges: bytes`, `Content-Type: application/octet-stream`, `ETag`, `Last-Modified`, `Server: AkamaiNetStorage` |
| Referer `https://www.amd.com/en/support/download/drivers.html`, `https://www.amd.com/ru/...`, `http://www.amd.com/`, `https://www.amd.com`, `https://drivers.amd.com/` | `200` |
| Referer `https://amd.com/`, `https://www.google.com/` | `302` → Download-Incomplete |
| Только `Origin: https://www.amd.com` без Referer | `302` |
| `User-Agent`: отсутствует / `curl/8.22.0` / `Mozilla/5.0...` при наличии Referer | `200` во всех случаях. **UA для drivers.amd.com не нужен** |
| Несуществующий файл **с** Referer | `404 Not Found`, тело `Not found` (10 Б) |
| Несуществующий файл **без** Referer | `302` → Download-Incomplete (существование файла без Referer не определить) |
| `Range: bytes=0-15` с Referer | `206 Partial Content`; файл начинается с `MZ` |
| `If-Modified-Since: <Last-Modified>` на `version.json` | `304 Not Modified` (годится для дешёвого опроса) |
| `If-None-Match: <ETag>` на `version.json` | `200` (ETag-валидация не срабатывает, использовать IMS) |
| `http://drivers.amd.com/...`, `http(s)://www2.ati.com/...` | `301` → `https://drivers.amd.com/<тот же путь>` (Referer сохраняется при ручном переходе) |
| Регистр пути | Не важен: `AMD_Chipset_Software_8.09.28.1124.exe` = `amd_chipset_software_8.09.28.1124.exe` (оба `200`, одинаковые `Last-Modified`/длина) |
| `GET www.amd.com/...html` с UA `curl/8.22.0`, `Wget/1.21`, `python-requests/2.31.0` | **ответа нет: соединение висит до таймаута** (20 с и 15 с) |
| То же без заголовка UA, с `hateeze/1.0` или браузерным UA | `200` за ~0.55 с |
| `curl -L` без Referer на exe/JSON | 302 → `www.amd.com/...Download-Incomplete.html`; с UA curl этот шаг **зависает** (наблюдалось: команда ушла за 120 с) |

Рекомендации для `HttpClient`:
- `SocketsHttpHandler.AllowAutoRedirect = false`. Редиректы обрабатывать вручную: `301` с `www2.ati.com` → следовать, `302` на `Download-Incomplete` → ошибка «AMD отклонил загрузку (нет Referer)».
- `DefaultRequestHeaders.Referrer = new Uri("https://www.amd.com/")`.
- `User-Agent: hateeze/<версия>` — честный UA, работает и на `www.amd.com`. Подделывать браузер не нужно.
- Таймауты: connect 15 с; для HTML — общий 30 с (страховка от тарпита); для exe — без общего таймаута, со своей логикой stall-детекта.
- Для докачки использовать `Range: bytes=<n>-` и проверять `206` + `Content-Range`.

### 2.5. Шаблоны имён файлов (история; угадывать не нужно)

| Период | RDNA (общий/новые) | Отдельные ветки | Пример |
|---|---|---|---|
| 2022-12 … 2023-08 | `whql-amd-software-adrenalin-edition-<ver>-win10-win11-<mmm><d>.exe` | — | `...-23.8.2-win10-win11-aug31.exe` |
| 2023-10 … 2025-09 | `...-<ver>-win10-win11-<mmm><d>-rdna.exe` | Polaris/Vega: `...-<ver>-win10-win11-<mmm><d>-vega-polaris.exe` | `...-24.12.1-win10-win11-dec-rdna.exe`, `...-24.9.1-win10-win11-vega-polaris.exe` |
| Optional-релизы | префикс **без** `whql-`: `amd-software-adrenalin-edition-<ver>-...` | | `amd-software-adrenalin-edition-25.9.2-win10-win11-sep-rdna.exe`, `...-26.2.1-win11-c.exe` |
| 2025-10-29 | `...-25.10.2-win10-win11-oct-rdna3.exe` (RDNA3/4), `...-25.10.2-win10-win11-oct-rdna.exe` (RDNA1/2), `...-oct-rdna3-combined.exe` | | |
| 2025-11-13 | RDNA3/4 `...-25.11.1-win11-nov.exe`; RDNA1/2 `...-25.11.1-win11-s.exe` | | |
| 2025-12 … сейчас | RDNA3/4 `-win11-b.exe`, RDNA1/2 `-win11-a.exe`, combined `-win11-c.exe` | Polaris/Vega `-win11-<mmm>-vega-polaris.exe` | `...-26.10.1-win11-b.exe`, `...-26.5.2-win11-may-vega-polaris.exe` |
| Minimal/Auto-Detect | `installer/<branch, напр. 26.20>/whql/amd-software-adrenalin-edition-<ver>-minimalsetup-<yymmdd>_web.exe` (страница продукта) или `_64bit.exe` (JSON); combined: `-combined-minimalsetup-` | | `installer/26.20/whql/...-26.10.1-minimalsetup-261018_web.exe` (47 859 040 Б) |
| Win7 (финал) | `radeon-software-adrenalin-2020-22.6.1-win7-64bit-june23-2022.exe` | | |

### 2.6. Определение GPU и выбор ветки

**Алгоритм:**
1. Перечислить PCI-устройства `VEN_1002` дисплейного класса. В `HardwareID` должен быть `&CC_03` (0300 VGA, 0380 Display), иначе это HDMI-аудио AMD (`CC_0403`). WMI `Win32_PnPEntity.HardwareID`/`CompatibleID` на Win11 23H2 проверены: на RTX 4060 они содержат `PCI\VEN_10DE&DEV_2882&CC_030000`.
2. Из `DeviceID` (`PCI\VEN_1002&DEV_744C&SUBSYS_...&REV_C8\...`) получить `DEV` и `REV`. Регулярка: `VEN_1002&DEV_([0-9A-F]{4}).*?&REV_([0-9A-F]{2})`.
3. Для каждой ветки (`_p`, `RDNA1_RDNA2`, `Legacy`) проверить точное совпадение `DEV-REV` в `skus`, затем совпадение только `DEV`.
   - Точное совпадение означает «официально поддерживается».
   - Совпадение только по DEV означает «та же архитектура, ревизии нет в списке». Это типично для OEM-ноутбуков и handheld. AMD прямо пишет: «AMD Software: Adrenalin Edition does not include support for handheld gaming devices». Таким пользователям предлагать драйвер OEM.
4. Итог по набору найденных веток:
   - {RDNA3/4} → `-b`; {RDNA1/2} → `-a`; {RDNA3/4, RDNA1/2} → **combined `-c`** (iGPU Ryzen 7000/9000 + RDNA3/4 dGPU — частый случай!);
   - {Legacy} → Polaris/Vega;
   - Legacy + любая RDNA → **конфликт**: два разных стека. Предупредить и предложить выбрать главный GPU (**НЕ ПРОВЕРЕНО**, как AMD ведёт себя в такой системе);
   - ничего не найдено → «AMD не поддерживает этот GPU в Adrenalin». Для GCN1–3 (HD 7000 … R9 Fury) последний драйвер — 22.6.1 «legacy» (`Consumer_Whql_Win7` не про них). Сюда же относятся Navi 12 (`7360`, Apple Pro 5600M) и Van Gogh `163F` (Steam Deck): их нет в списках.
5. Проверено скриптом на снимке манифестов: `_c == _p ∪ RDNA1_RDNA2`, пересечений DEV между `_p`/`RDNA1_RDNA2`/`Legacy` нет. 227 + 170 = 397 SKU, Legacy — 204 SKU.

**Таблица DEV ID по веткам** (снимок 2026-10-26; источник истины — `skus`; названия — `pci.ids`, справочно для UI):

RDNA3/RDNA4 и APU RDNA3/3.5 (`_p`, пакет `-b`):

| DEV | Чип / продукты |
|---|---|
| 7550 | Navi 48 — RX 9070 / 9070 XT / 9070 GRE |
| 7551 | Navi 48 — Radeon AI PRO R9700 |
| 7590 | Navi 44 — RX 9050 / 9060 XT (и 9060) |
| 744C | Navi 31 — RX 7900 XT / XTX / GRE / 7900M |
| 7448, 7449, 744A, 744B, 745E | Navi 31 — PRO W7900 / W7800 48GB / W7900 Dual Slot / W7900D / W7800 |
| 747E | Navi 32 — RX 7700 XT / 7800 XT |
| 7470 | Navi 32 — PRO W7700 |
| 7480 | Navi 33 — RX 7600 / 7600 XT / 7600M XT / 7600S / 7700S / PRO W7600 |
| 7481, 7483, 7489, 748B, 7499 | Navi 33 — «Steam Machine» / RX 7600M(XT) / PRO W7500 / Radeon Graphics / RX 7400, 7300, PRO W7400 |
| 15BF, 15C8 | Phoenix1/2 — Radeon 740M/760M/780M |
| 1900, 1901 | Hawk Point 1/2 — 760M/780M, 740M |
| 150E | Strix — 880M/890M |
| 1586 | Strix Halo — 8050S/8060S |
| 1114, 1902 | Krackan/Krackan2 — 820M/840M/860M |

RDNA1/RDNA2 и APU RDNA2 (`RDNA1_RDNA2`, пакет `-a`):

| DEV | Чип / продукты |
|---|---|
| 731F | Navi 10 — RX 5600 XT / 5700 / 5700 XT |
| 7312 | Navi 10 — PRO W5700 |
| 7340, 7341, 7347 | Navi 14 — RX 5500 / 5500M / PRO W5500(M) |
| 73BF, 73A5, 73AF, 73A3 | Navi 21 — RX 6800 / 6800 XT / 6900 XT / 6950 XT / PRO W6800 |
| 73DF | Navi 22 — RX 6700 / 6700 XT / 6750 XT / 6800M |
| 73FF, 73EF, 73E0, 73E1, 73E3 | Navi 23 — RX 6600 / 6600 XT / 6650 XT / W6600 |
| 743F, 7421–7424 | Navi 24 — RX 6400 / 6500 XT / 6300 / W6300 / W6400 |
| 1681 | Rembrandt — 660M/680M |
| 164E | Raphael — iGPU Ryzen 7000 desktop |
| 13C0 | Granite Ridge — iGPU Ryzen 9000 desktop |
| 1506 | Mendocino — 610M |

Polaris/Vega и APU Vega (`Legacy`, пакет `-vega-polaris`):

| DEV | Чип / продукты |
|---|---|
| 67DF, 6FDF | Polaris 10/20 — RX 470/480/570/580/590, 580 2048SP |
| 67EF, 67FF | Polaris 11/21 — RX 460/560/550 640SP |
| 6981, 6985, 6987, 6995, 699F | Polaris 12 «Lexa» — RX 540/550, WX 2100/3100/3200 |
| 67C0, 67C2, 67C4, 67C7, 67E0, 67E3, 67E8, 67EB | Pro WX 4100–7100 и V-серия |
| 687F | Vega 10 — RX Vega 56/64 |
| 6861–6868 | Vega FE / WX 8100/9100 / SSG |
| 66AF | Vega 20 — Radeon VII |
| 66A1 | Vega 20 — Pro VII / MI50 |
| 15DD, 15D8 | Raven Ridge / Picasso |
| 1636 | Renoir |
| 1638 | Cezanne |
| 164C | Lucienne |
| 15E7 | Barcelo |

### 2.7. Устройство пакета (прочитано из заголовков 7z)

- Формат: PE-стаб (x64, 7 секций, 1 785 344 Б). Собран из исходников 7-Zip SFX: в нём путь PDB `...\SelfExtractingExe\src\CPP\7zip\Bundles\SFXWin\x64\Release\SFXWin.pdb`. Дальше идёт **архив 7z без отступа** и Authenticode-подпись в конце файла.
- Методы сжатия полных пакетов: **LZMA2** (prop `0x18` → словарь 16 МиБ), для блока с exe — **LZMA2 + BCJ x86** (`03030103`). Minimal-установщик: LZMA + LZMA2 (prop `0x1C`) + **BCJ2** (`0303011B`). Распаковщик должен поддерживать LZMA2/BCJ/BCJ2: 7-Zip ≥ 9.20 и 7za, SharpCompress — **НЕ ПРОВЕРЕНО** на этих архивах.
- Объём после распаковки (для проверки свободного места):

  | Пакет | Файлов (записей) | Распаковано | Скачивается |
  |---|---|---|---|
  | `-b` | 732 | 3 094 120 566 Б (~2.9 ГиБ) | 0.96 ГиБ |
  | `-a` | 780 | 2 872 755 023 Б (~2.7 ГиБ) | 0.93 ГиБ |
  | `-c` | 930 | 5 093 703 328 Б (~4.7 ГиБ) | 1.66 ГиБ |
  | Polaris/Vega 26.5.2 | 410 | 2 037 296 508 Б (~1.9 ГиБ) | 0.61 ГиБ |
  | minimal | 108 | 147 305 688 Б | 46 МиБ |

  Минимум свободного места: пакет + распаковка + ~1.5 ГиБ на DriverStore (`B0xxxxx` у `-b` весит 1.31 ГБ). Для `-b` требовать **≥ 6 ГиБ**, для `-c` — **≥ 9 ГиБ**.

Структура (`-b`, 26.10.1):

```
Setup.exe                                  (1 038 600 Б; входная точка CIM-инсталлятора)
Bin64\  7z.exe 7z.dll ADDL_Common.dll AMDBugReportTool.exe AMDCleanupUtility.exe AMDInstallUEP.exe
        AMDSoftwareCompatibilityTool.exe AMDSoftwareInstaller.exe (49.5 МБ, GUI) AMDSplashScreen.exe
        ATISetup.exe InstallManagerApp.exe Setup.exe cccmanifest_64.json localization\*\INext_*.qm resources*.rcc
Config\ InstallManifest.json (732 595 Б) cccmanifest_64.json InstallLegacy.json CIMTracker.JSON BuildHighlights.xml
        AMDAUEPInstaller.xml AMDCOMPUTE.xml AMDLinkDriverUpdate.xml AMDRELAUNCHER.xml AMDScoSupportTypeUpdate.xml
        AMDUpdater.xml AMDUWPLauncher.xml EnableWindowsDriverSearch.xml InstallUEP.xml ModifyLinkUpdate.xml
        workaround.xml Monet*.xml   ← *.xml (кроме Monet*) = определения Планировщика заданий
Packages\Apps\  AUEP64\AUEP64.msi  CCC2\Branding\Branding.msi  CNBranding\CNBranding64.msi
                RyzenMasterSDK\RyzenMasterSDK.msi  VC22RTx64\vcredist_x64\VC_redist.x64.exe
Packages\Drivers\Display\WT6A_INF\
        u0205306.inf (1 119 492 Б) + u0205306.cat      ← БАЗОВЫЙ INF дисплейного драйвера
        B026676\  amdkmdag.sys (90 МБ), amdxc64.dll, amdxx64.dll, ..., atiesrxx.exe, atieclxx.exe,
                  ccc2_install.exe (246 879 880 Б — установщик Adrenalin UI), amdinstallmanager.msi (67.8 МБ), ...
        amdocl\amdocl.inf    amdogl\amdogl.inf    amdvlk\amdvlk.inf     ← OpenCL / OpenGL / Vulkan (extension INF)
        amdwin\amdwin-u0205306.inf   amdfendr\amdfendr.inf (Crash Defender)   amdxe\amdxe.inf (AMD Link XInput)
        amdafd\amdafd.inf   amdfdans\AMDFDANS.inf   amdpcibridge\amdpcibridgeextension.inf
Packages\Drivers\Audio\hdmi\WT64A\AtihdWT6.inf   ← HDMI/DP-аудио GPU (+ ACP/I2S/SoundWire — только для APU/ноутбуков)
Packages\Drivers\SBDrv\AMDUCSI\WT64A\amducsi.inf ← USB-C UCSI
Packages\Drivers\AMDISP\..., AMDNPUMCDM\..., AMDNPUWDF\...   ← камера ISP и NPU Ryzen AI (ноутбуки)
```

Отличия других пакетов:
- **`-a` (RDNA1/2, 25.10.45.16)**:
  - база `Display\WT6A_INF\u0205305.inf` + `B026653\`;
  - **нет** подпапок `amdogl` и `amdvlk` (OpenGL/Vulkan, видимо, в базовом INF — **НЕ ПРОВЕРЕНО**);
  - есть `Packages\Drivers\RadeonLED\WT64A\amdusbledfd.inf` и `Packages\Apps\RadeonLED` (подсветка референсных RX 6800 XT/6900 XT).
- **`-c` (combined)**:
  - `Display\WT6A_INF\u0205306.inf` (26.20, RDNA3/4) **и** `Display2\WT6A_INF\u0205305.inf` (25.10, RDNA1/2);
  - Radeon Software Slimmer прямо учитывает `Display` и `Display2`.
- **Polaris/Vega 26.5.2**:
  - `Display\WT6A_INF\u0201039.inf` + `B025980\`, без `amdogl`/`amdvlk`;
  - аудио-папки в другом регистре (`Audio\HDMI\WT64A`, `ACPAfd`, `I2STDM`, `SAFD`);
  - нет `AMDISP`/NPU, есть `Packages\Apps\USBCPDFW`.
- **minimal**: только `Bin64`, `Config`, `Setup.exe`, без `Packages`.
- Имя `WT6A_INF` одинаково во всех четырёх пакетах 2026 года. Valleyofdoom PC-Tuning предупреждает: «The folder may be named differently on other driver versions». В коде искать `Packages\Drivers\Display*\*_INF\*.inf` (верхний уровень), а не хардкодить `WT6A_INF`.
- `Bin64\cccmanifest_64.json` (прочитан из `-a`, диапазон 3 МБ) — манифест вложенного `ccc2_install`. Пакеты:
  - `AMD DVR64` (`\CN\amddvr\amddvr64\amddvr64.msi`, 2026.1009.2240.2068);
  - `AMD Settings` (`\CN\cnext\cnext64\ccc-next64.msi` — это и есть UI Adrenalin);
  - `Microsoft Visual C++ 2022 Redistributable 64 bit` (`..\VC22RTx64\VC_redist.x64.exe`);
  - `AMD WVR64` (`\CN\wvr64\WVR64.msi`, OpenVR).

  В каждом указаны `"DriverVersion": "32.0.21045.16010"` и `"CNVersion": "25.10.45.16"`, то есть манифест содержит Driver Store-версию.

### 2.8. Установка

#### 2.8.1. Официальный путь (GUI) и режимы

Статья AMD RSX2-INSTALL (обновлена 2026-02-09). Типы установки в «Additional Options»:
- **Default** — все функции;
- **Minimal** — «only the basic controls… does not include advanced features such as performance tweaking or recording»;
- **Driver Only** — «Provides no user interface for AMD Software features».

Там же описан **Factory Reset**: удаляет все предыдущие версии, «temporarily pauses Windows Updates», перезагружает и продолжает установку. С 26.1.1 есть опция **AI Bundle**: известная проблема — установка падает в регионах с ограниченным доступом к HuggingFace/GitHub. Для slim-установки AI Bundle не ставить.

Коды ошибок установщика с официальными статьями: 1604, 1603, 213, 212, 211, 207, 206, 205, 202, 196, 195, 194, 193, 192, 186, 184, 183. Например, 183 — «third party graphics drivers installed», 184 — «unsupported Operating System», 206 — «pending Windows install or update».

#### 2.8.2. Командная строка CIM-инсталлятора (`Setup.exe`)

Источник — «Command Line Installation User Guide» (AMD, 2019, Radeon Pro Software): `https://drivers.amd.com/relnotes/command-line-installation.pdf`. Отдаётся **без** Referer (`200`, 219 882 Б). Новая ссылка из RSX2-INSTALL (`www.amd.com/system/files/documents/radeon-software-command-line-installation-user-guide.pdf`) **мертва** (редирект на хаб документации). Применимость к Adrenalin 26.x **НЕ ПРОВЕРЕНА вживую**. Структура `Setup.exe`, `Bin64\ATISetup.exe`, `Packages\` и `Program Files\AMD\CIM` та же.

| Команда | Описание (по документу) |
|---|---|
| `Setup.exe /?` или `/Help` | Краткая справка |
| `Setup.exe -INSTALL` | Установка драйвера и приложений из `<build>\Packages`, «No output to the screen» |
| `Setup.exe -INSTALL -BOOT` | Тихая установка с автоперезагрузкой |
| `Setup.exe -INSTALL -USE "<путь>"` или `-USE <путь\pkg.msi>` | Искать пакеты в указанной папке / ставить конкретный MSI (зависимости соблюдаются) |
| `Setup.exe -INSTALL -OUTPUT screen` / `-OUTPUT detail` / `-OUTPUT "<файл>"` | Вывод прогресса в консоль (кратко/подробно) или в файл |
| `Setup.exe -INSTALL -LOG "<путь\файл>"` | Файл результата. Без пути пишется в `<Program Files>\AMD\CIM\Reports\` (по умолчанию `Setup.log`) |
| `Setup.exe -UNINSTALL ALL [-BOOT]` | Тихое удаление всех пакетов («There is no option to specify which packages to uninstall») |
| `Setup.exe -USE <path> -CFG <file.cfg> -INSTALL` | Свой конфиг |
| `Setup.exe -UI` | Запустить GUI (`RadeonInstaller.exe`; в 2026 — `Bin64\AMDSoftwareInstaller.exe`) |

Формат файла `-LOG`:

```
[ResponseResult]
ResultCode = <0|1|2>        ; 0 PASS (возможно, нужен reboot), 1 FAIL, 2 FAIL + нужен reboot
[Details]
Package Name = ...
Package PID = ...
Version = ...
ErrorCode = <0|1|2|3>       ; 0 PASS, 1 FAIL, 2 FAIL+reboot, 3 PASS+reboot
```

Код выхода процесса в документе **не описан**. Решение принимать по `ResultCode` в логе.

Наблюдения из сообщества: «`-INSTALL` запускает setup, но ничего не ставит», в логе вызов `ATISetup.exe -LOG ... -INSTALL`. Ветка `community.amd.com` закрыта и редиректит на `www.amd.com/en/site-notifications/community-updates.html`. Это **риск**: тихий режим может зависеть от версии.

Режим «Driver Only» через CLI **не документирован**. Поэтому для driver-only — §2.8.4.

#### 2.8.3. SFX-обёртка `whql-...exe`

Строки в стабе (UTF-16):
- `-INSTALL`, `-SILENT`, `-factoryresetInstall`;
- `AMD-Software-Installer`, `\AMD\`, `\setup.exe`, `-CleanAfterInstall -LaunchedFromNSIS`;
- `Specify a location for extracted files.`, `Incorrect output directory path`.

Отсюда **гипотеза (НЕ ПРОВЕРЕНО)**:
- двойной клик спрашивает «распаковать?» и распаковывает в `%SystemDrive%\AMD\AMD-Software-Installer\…`, затем запускает `setup.exe -CleanAfterInstall -LaunchedFromNSIS`;
- ключи `-INSTALL`/`-SILENT`/`-factoryresetInstall`, переданные самому SFX, вероятно, пропускают диалог и уходят в `setup.exe`.

**Не полагаться на это без теста на машине с Radeon.** Надёжный путь — распаковать самим (§2.8.5) и запускать `Setup.exe` из своей папки.

#### 2.8.4. «Только драйвер» через pnputil (рекомендуемый для твикера путь)

Так же делает valleyofdoom PC-Tuning, только через Диспетчер устройств: «Move `Packages\Drivers\Display\XXXX_INF`… install the driver… select the driver folder». Панель управления при желании ставится отдельно: «Extract `XXXX_INF\ccc2_install.exe` with 7-Zip and run `CN\cnext\cnext64\ccc-next64.msi`». Это согласуется с `cccmanifest_64.json`.

Команды (Microsoft Learn, `/add-driver` доступен с Win10 1607):

```
pnputil /add-driver "<X>\Packages\Drivers\Display\WT6A_INF\u0205306.inf" /install
pnputil /add-driver "<X>\Packages\Drivers\Display\WT6A_INF\*.inf" /subdirs /install      ← база + все extension-INF
pnputil /add-driver "<X>\Packages\Drivers\Audio\hdmi\WT64A\AtihdWT6.inf" /install        ← звук по HDMI/DP (опционально)
```

- **Порядок**: сначала базовый INF, затем каждый INF из подпапок по одному. Microsoft: «to get actionable return values we recommend specifying `/install /add-driver` with one INF at a time».
- Combined-пакет: повторить то же для `Display2`. PnP сам применит INF к своему железу (25.10 — к RDNA1/2, 26.20 — к RDNA3/4).
- Коды возврата pnputil (Microsoft Learn «PnPUtil Return Values»):
  - `0` — успех;
  - `259` (`ERROR_NO_MORE_ITEMS`) — «No devices match the supplied driver or the target device is already using a better or newer driver»;
  - `3010` — успех, нужна перезагрузка;
  - `1641` — успех, перезагрузка начата (`/reboot`).

  Диагностика: `%windir%\inf\setupapi.dev.log`.
- `259` при **понижении версии** — нормальная ситуация («already using a better or newer driver»). Для отката нужен AMD Cleanup Utility (§2.8.7) или DDU (тема другого агента), затем установка.
- Последствия driver-only:
  - нет `RadeonSoftware.exe`/`AMDRSServ.exe`;
  - нет настроек Anti-Lag/Chill/Boost/RSR/AFMF/FreeSync UI/разгона/андервольта/записи;
  - FreeSync включается драйвером по умолчанию — **НЕ ПРОВЕРЕНО** на 26.x.
- **НЕ ПРОВЕРЕНО**: (а) содержит ли базовый INF/`amdwin` `AddSoftware`/SoftwareComponent, из-за которого Windows сама запустит `ccc2_install.exe` или подтянет из Store UWP «AMD Radeon Software» (`AdvancedMicroDevicesInc-RSXCM_fhmx3h6dzfmvj`, который упоминает RSS). RSS wiki: «In the display driver INF file… are references to `ccc2_install.exe`». PC-Tuning подразумевает, что UI сам не ставится. (б) Как ведёт себя Windows Update после такой установки. Сам AMD-установщик держит для этого задание `Config\EnableWindowsDriverSearch.xml`.

#### 2.8.5. Распаковка

- Проверенный путь (RSS): `7z.exe x "<installer.exe>" -o"<dir>" -y` (7-Zip открывает SFX как 7z).
  Коды 7-Zip: 0 OK, 1 warning, 2 fatal, 7 ошибка командной строки, 8 нет памяти, 255 прервано.
- Приложение — single-file exe. Варианты:
  - **(а)** встроить `7za.exe` из «7-Zip Extra» (LGPL-2.1) и распаковывать его во временную папку;
  - **(б)** использовать установленный 7-Zip (`HKLM\SOFTWARE\7-Zip` → `Path`/`Path64`), но у пользователя его может не быть;
  - **(в)** SharpCompress (MIT): открыть поток со смещения сигнатуры 7z — **НЕ ПРОВЕРЕНО** на этих архивах (BCJ2 в minimal).

  `Bin64\7z.exe` лежит **внутри** архива, использовать его до распаковки нельзя.
- Папку распаковки выбирать короткой (`C:\ProgramData\hateeze\amd\<ver>\`). Глубина путей в пакете большая, например `Packages\Drivers\AMDNPUMCDM\WT64A\rml\include\rml\...`.

#### 2.8.6. Проверка целостности и подписи

- Хэшей AMD не публикует. Проверять: (1) длину против `Content-Length` из `HEAD`; (2) Authenticode (`WinVerifyTrust`); (3) издателя.
- Издатель обоих пакетов (Adrenalin 26.10.1 `-b` и chipset 8.09.28.1124) — `CN=Advanced Micro Devices, O=Advanced Micro Devices, S=California, C=US`, issuer `CN=Sectigo Public Code Signing CA R36, O=Sectigo Limited, C=GB`, timestamp DigiCert G4 (2025 CA1).
  - Отпечаток сертификата Adrenalin: `33D35682079E201671B738B7209B4586103BC271` (NotAfter 2027-04-27);
  - отпечаток сертификата чипсета: `BF2E317BEE75478F8693BA0E29B757AEF5F9A34C`.

  **Не пинить отпечатки**: сертификаты меняются. Проверять `CN`/`O` == `Advanced Micro Devices` и успех `WinVerifyTrust`.

#### 2.8.7. Откат / очистка

- AMD в RN каждой версии: «When downgrading to a previous version… it is recommended to use the AMD Cleanup Utility».
- Отдельная утилита: `https://drivers.amd.com/drivers/amdcleanuputility.exe` (2 621 712 Б, Last-Modified 2026-01-26, **Referer обязателен**). Также есть копия в пакете: `Bin64\AMDCleanupUtility.exe`.
- Утилита предлагает перезагрузку в безопасный режим (рекомендуется AMD) и требует перезагрузки после работы. CLI-ключи **не документированы** (НЕ ПРОВЕРЕНО). В приложении запускать её только интерактивно.

#### 2.8.8. Как узнать установленную версию

- Надёжно: `Win32_PnPSignedDriver` (или `Win32_VideoController.DriverVersion`) для устройства `PCI\VEN_1002&DEV_...` даёт Driver Store-версию, например `32.0.32015.7029`.
- Последнюю Driver Store-версию берём из RN по `releasenotes`. Регулярка по тексту без тегов (после `WebUtility.HtmlDecode`):
  `Driver Version\s+([\d.]+)\s+for\s+Windows.{0,60}?\(Windows Driver Store Version\s+([\d.]+)\)`.
  Проверено:
  - `rn-rad-win-26-10-1.html` → `26.20.15.07 -> 32.0.32015.7029`, `25.10.45.16 -> 32.0.21045.16010`;
  - `RN-RAD-WIN-26-5-2-POLARIS-VEGA.html` → `23.19.25.01 -> 31.0.21925.1001`.

  Выбирать пару, где «Driver Version» равна части `internalbuildversion` до первого `-`. Для Legacy, если пары нет в `releasenotes` (ссылка ведёт на общий RN), запросить `.../RN-RAD-WIN-<x>-<y>-<z>-POLARIS-VEGA.html`, собрав имя из `externalbuildversion`.
- Вывести DS-версию из internal-версии **нельзя** (25.10.45.16 → 32.0.21045.16010, 26.20.15.07 → 32.0.32015.7029, 23.19.25.01 → 31.0.21925.1001: суффикс не выводится).
- Значения реестра класса дисплея `HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\00xx` (`RadeonSoftwareVersion`, `ReleaseVersion` и т.п.) — **НЕ ПРОВЕРЕНО** (нет AMD GPU; первоисточника не найдено).
- Путь установки UI: `HKLM\SOFTWARE\AMD\CN` → `InstallDir` (используется в RSS; по умолчанию `C:\Program Files\AMD\CNext\CNext`). CLI UI — `cncmd.exe` (`cncmd exit`/`cncmd restart`).

### 2.9. Radeon Software Slimmer (RSS): что удаляет и что безопасно

Репозиторий `GSDragoon/RadeonSoftwareSlimmer`, последний коммит 2026-10-13. Собирается под .NET Framework 4.8 / .NET 10.

**Pre-install** (правит распакованный установщик, бэкап в `<X>\RSS_Backup\`):
1. **Packages**: удаляет записи из `Bin64\cccmanifest_64.json` и `Config\InstallManifest.json` (массив `Packages.Package[]`, поля `Info.productName/url/ptype/Description`). Сам автор пишет: «Unfortunately this doesn't work as well as one would hope. There's also a InstallByDefault property, but that doesn't seem to do anything».
2. **Display components**: каждую подпапку `Packages\Drivers\{Display,Display2}\WT6A_INF\<comp>` ровно с одним `*.inf` можно «не оставлять». Тогда она перемещается в `RSS_Backup\DisplayComponents\<Display|Display2>\`. Описание берётся из `[Strings]` INF (`*desc*`, `ExtendedGraphics`, `AMDFDANSName`, `AMDOCLName`, `AMDWINName`, `AMDOGLName`, `AMDVLKName`).
3. **Scheduled tasks**: в `Config\*.xml` (кроме `Monet*`) ставит `<Settings><Enabled>false</Enabled><Hidden>false</Hidden>`. Задания всё равно создаются, но выключенными.
4. Правка INF или `ccc2_install` **ломает подпись** (RSS wiki). RSS этого не делает, мы тоже не делаем.

**Post-install** (после установки):
- **Службы/драйверы** (`HKLM\SYSTEM\CurrentControlSet\Services\<name>\Start`, 4 = Disabled):

  | Группа | Имена |
  |---|---|
  | Ядро/устройства | `amdfendr`, `amdfendrmgr`, `amdlog`, `AMDXE` |
  | Аудио | `amdacpbus`, `AMDAcpBtAudioService`, `AMDAfdAudioService`, `AMDHDAudBusService`, `amdi2stdmafd`, `AMDSoundWireAudioService`, `AtiHDAudioService`, `AMDSAFD`, `amdacphpdsvc`, `AMDAcpUsbAudioService`, `AMDI2SAudioService`, `AMDKSLFilterService`, `amdsdwc`, `amdsdws` |
  | NT-службы | `AMD Crash Defender Service`, `AMD External Events Utility`, `AMD Log Utility`, `AUEPLauncher` |
  | Прочее | `AMDRadeonsettings`, `AMDFDANS`, `amducsi`, `SSGService` |
  | ISP (камера) | `AmdCamera`, `ispBridgeDevice`, `sensorGC1029`, `sensorHM1092`, `sensorOV05C`, `sensorOV08X`, `sensorOV13B`, `sensorVD55G0` |
  | NPU | `IpuMcdmDriver`, `kipudrv` |

  Сознательно **не трогает** `amdkmdag`/`amdwddmg`/`amduw23g` (ядро драйвера) и `amdkmpfd`/`amdkmdap` (фильтр PCI root bus для SAM/ReBAR). В 26.10.1 ядро называется `amdkmdag.sys`.
- **Задания**: все с Author = `Advanced Micro Devices` + `DVRAnalytics`, `StartAUEP`, `StartCN`, `StartCNBM`, `StartDVR`. Wiki перечисляет `AMD COMPUTE`, `AMDInstallLauncher`, `AMDInstallUEP`, `AMD UWP Launcher`, `AMD Link Driver`, `AMDRelauncher`, `AMDRyzenMasterSDKTask`, `AMD Updater`, `ModifyLinkUpdate`, `StartCN` (`cncmd.exe startwithdelay`), `StartCNBM`, `StartDVR` (`RSServCmd.exe`).
- **MSI-записи**: всё с Publisher `Advanced Micro Devices, Inc.`, **кроме** чипсетных (имя содержит `Chipset`, `GPIO`, `PCI`, `PSP`, `Ryzen`, `SMBus`, `3D V-Cache`, `AMD Application Compatibility Database Driver`, `PPM Provisioning`, `AMD Interface`, `I2C`). Этот фильтр стоит повторить, чтобы при «чистке Radeon» не снести чипсет.
- **Автозапуск UWP-оболочки**: `HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\AdvancedMicroDevicesInc-RSXCM_fhmx3h6dzfmvj\launcherrsxruntimeTask` → `State`.
- **Хост-процессы**: `RadeonSoftware`, `AMDRSServ`, `amdow`, `AMDRSSrcExt`, остановка через `cncmd exit`.
- **Временные файлы/кэши**:
  - `C:\AMD`;
  - `Program Files\AMD\AMDInstallManager\Logs`, `CIM\Log`, `CIM\Reports`;
  - `%LOCALAPPDATA%\AMD_Common`, `AMDIdentifyWindow\cache`, `AMDInstallManager\cache`, `AMDSoftwareInstaller\cache`, `RadeonInstaller\cache`;
  - `%LOCALAPPDATA%\AMD\{AMDLink\cache, CN\Analytics, CN\NewsFeed, LINK\game_cache, Radeonsoftware\cache}`;
  - шейдер-кэши `{cl.cache, Dx9Cache, DxCache, DxcCache, GLCache, oglcache, OglpCache, VkCache}` в `Local` и `LocalLow`;
  - `System32\AMD\{EeuDumps, MmdDumps}`.

**Оценка пунктов для hateeze** (см. также каталог §5):

| Пункт | Безопасность | Комментарий |
|---|---|---|
| AUEP (`AUEP64.msi`, служба `AUEPLauncher`, задания `AMDInstallUEP`/`StartAUEP`/`AMDAUEPInstaller`) | безопасно | Телеметрия «AMD User Experience Program»; влияния на FPS нет |
| Задания `AMDLinkDriverUpdate`/`ModifyLinkUpdate`/`AMD Link Driver`, служба `AMDXE` | безопасно | AMD Link / эмуляция XInput; без AMD Link бесполезно |
| `AMDUpdater` (проверка обновлений) | безопасно | Если обновлениями управляет hateeze |
| `RyzenMasterSDK.msi`, `AMDRyzenMasterSDKTask` | безопасно | Метрики CPU в оверлее Adrenalin |
| `RadeonLED` (драйвер+приложение) | безопасно, если карта не референсная RX 6800 XT/6900 XT | |
| `AMD WVR64` | безопасно без VR | RSS: «Causes Radeonsoftware.exe to load …settings.json… every 1 second» |
| `AMD DVR64` | ситуативно | Нужен для записи/оверлея и для Bug Report Tool |
| Crash Defender (`amdfendr`, `amdfendrmgr`, `AMD Crash Defender Service`, `amdlog`) | ситуативно/эксперимент | Логи в минидампы для AMD; прироста производительности не доказано |
| `StartCN`/`StartDVR` (автозапуск Radeon Software и хост-сервиса) | ситуативно | Пропадут оверлей, горячие клавиши, запись. Профили драйвера применяются драйвером — **НЕ ПРОВЕРЕНО** для всех функций |
| `EnableWindowsDriverSearch` | **не трогать** | Возвращает поиск драйверов в Windows Update после установки/Factory Reset |
| **AMD External Events Utility** (`atiesrxx.exe`) | **avoid** | PC-Tuning: «(required for VRR)»; RSS цитирует, что без неё ломается FreeSync |
| `amdkmpfd` | **avoid** | Фильтр PCI root bus: нужен для SAM/ReBAR; RSS: «Installing/uninstalling causes flicking and devices… to disconnect» |
| `AtiHDAudioService` (HDMI/DP-звук) | ситуативно | Нужен, если звук идёт в монитор/ТВ |
| ACP/I2S/SoundWire/ISP/NPU | n/a на десктопе | Ставятся только при наличии железа (ноутбуки/APU); на ноутбуке не трогать |
| `amducsi` | ситуативно | USB-C на видеокарте |
| Очистка шейдер-кэшей | **avoid** как «ускорение» | Вызывает перекомпиляцию и статтеры; допустимо только при диагностике |
| Правка INF/ccc2_install | **avoid** | Ломает подпись (античит, Secure Boot/HVCI) |

---

## 3. AMD Chipset Software (Ryzen)

### 3.1. Актуальная версия и ссылка

- **8.09.28.1124**, Release Date **2026-10-21** (RN «Bug fixes included»).
  `https://drivers.amd.com/drivers/amd_chipset_software_8.09.28.1124.exe`: 81 551 288 Б, `Last-Modified: Wed, 21 Oct 2026 17:46:00 GMT`, Referer **обязателен**.
  Предыдущая — 8.08.12.551 (81 537 472 Б, 2026-08-14).
- Ссылка **одна и та же** на страницах A520, X570 (AM4), X870E (AM5) и TRX50: `https://www.amd.com/en/support/downloads/drivers.html/chipsets/<am4|am5|str5>/<чипсет>.html`. Страница CPU (`.../processors/ryzen/ryzen-5000-series/amd-ryzen-7-5700x.html`) даёт только Ryzen Master, чипсет на ней не ищем.
- На странице A520 есть также `amd_raid_software_2.20.19.037.exe` (2021-11-15, 142 107 376 Б). Нужен только пользователям RAIDXpert, по умолчанию не предлагать.
- **JSON-источника нет**: `chipset.json`, `Chipset.json`, `chipset_version.json`, `DrvDldDetails_Chipset*.json`, `DrvDldDetails_Consumer_Chipset_Win11.json`, `ryzen_chipset.json` в `installer/json/` дают 404.
- Программно версию берём из HTML: `GET https://www.amd.com/en/support/downloads/drivers.html/chipsets/am4/a520.html` (UA `hateeze/…`) → регулярка `https://drivers\.amd\.com/drivers/amd_chipset_software_(\d+\.\d+\.\d+\.\d+)\.exe` (IgnoreCase). Берём максимум через `System.Version` (`8.09.28.1124` → `8.9.28.1124`).
  Release notes: `https://www.amd.com/en/resources/support-articles/release-notes/RN-RYZEN-CHIPSET-<a>-<bb>-<cc>-<dddd>.html` (точки заменить на дефисы, ведущие нули оставить: `RN-RYZEN-CHIPSET-8-09-28-1124`).

### 3.2. Тип установщика и тихая установка

- Тип: **NSIS**. После PE-стаба (оверлей с 326 144) идут `flags=0x00000000`, `0xDEADBEEF`, `"NullsoftInst"`. Флаг принудительной тишины не выставлен, стандартный `/S` NSIS доступен.
  Подпись: `CN=Advanced Micro Devices` (Sectigo R36), PE32. Cert table: offset 81 539 760, 11 528 Б.
- Официальные ключи (AMD, RN 2.04.04.111; в RN 3.x–8.x раздел больше не публикуется):

  | Задача | Команда |
  |---|---|
  | Тихая установка | `START /WAIT "" <exe> /S` |
  | Тихое удаление | `START /WAIT "" <exe> /S /EXPRESSUNINSTALL=1` |
  | Выборочная установка | `/S /CUSTOMINSTALL="<feature,feature>"` |
  | Выборочное удаление | `/S /CUSTOMUNINSTALL="<...>"` |

  Имена фич (2.04): `CIR`, `I2C`, `PSP`, `RYZENPPKG` (AMD Ryzen Power Plan), `USBHUB`, `USBCNTRL`, `USBHUB_PT`, `USBCNTRL_PT`, `PCI`, `UART`, `GPIO2`, `GPIO3` (PT GPIO), `IOV_WT`, `USBHUB_HD`, `USBCNTRL_HD`, `SFHI2C`, `FILTERUSB`, `SFHDRVR`, `SATA`, `SMBUS`, `AS4ACPI`. Для 8.x список имён **НЕ ПРОВЕРЕН**: появились PPM, 3D V-Cache, PMF, AMS и др.
- Подтверждение `/S` на 8.x: пакет Chocolatey `amd-ryzen-chipset` 2026.8.14 (8.08.12.551, Approved) вызывает `Start-Process -FilePath "$installerFilePath" -ArgumentList "/S" -Wait`, качает с `Referer: https://www.amd.com/en/support/chipsets/amd-socket-am4/a320` и проверяет, что CPU — «Ryzen». Вживую `/S` на 8.09 **НЕ ПРОВЕРЕН** (запрет на запуск установщиков).
- Коды выхода: в сообществе сообщали, что некритичные части установки «silently fail… cause the return code… to be set to 1603». Принимать `0` и `3010` как успех. При `1603` проверить версию в реестре (§3.5) и логи `C:\AMD\Chipset_Software\Logs\` (на этой машине там `Device_ID.log` и копия `setupapi.dev.log`). Перезагрузка после `/S`: **НЕ ПРОВЕРЕНО**, считать, что нужна, и предлагать её.
- Известные проблемы 8.09.28.1124:
  - после установки 7.x нельзя поставить 6.x и старее (лечится удалением 7.x+ и папки `C:\Program Files (x86)\AMD\Chipset_Software\Qt_Dependencies`);
  - на неанглийской ОС часть имён на английском;
  - «Occasionally, Ryzen PPKG may not install or upgrade».

### 3.3. Что ставит (8.09.28.1124, из RN; Win10 / Win11)

| Компонент | Win10 | Win11 |
|---|---|---|
| AMD Ryzen Power Plan / AMD Processor Power Management Support | 8.0.1.13 | 8.0.1.13 |
| AMD PCI Device Driver | 1.0.0.90 | 1.0.0.90 |
| AMD I2C | 1.2.0.133 | 1.2.0.133 |
| AMD UART | 1.2.0.121 | 1.2.0.121 |
| AMD GPIO2 | 2.2.0.137 | 2.2.0.137 |
| PT GPIO (Promontory, A520/B550/X570) | 3.0.3.0 | 3.0.5.0 |
| **AMD PSP Driver** | 5.46.0.0 | 5.46.0.0 |
| AMD IOV | 1.2.0.52 | — |
| **AMD SMBUS** | 5.12.0.44 | 5.12.0.44 |
| AMD AS4 ACPI | 1.2.0.46 | — |
| AMD SFH I2C / SFH / SFH1.1 | 1.0.0.86 / 1.0.0.341 / 1.1.0.26 | 1.0.0.86 / 1.0.0.341 / 1.1.0.37 |
| AMD USB Filter | 2.1.11.304 | — |
| AMD CIR | 3.2.4.135 | — |
| AMD MicroPEP | 1.0.47.1 | 1.0.47.1 |
| AMD Wireless Button | 1.0.0.6 | 1.0.0.6 |
| AMD PMF-6000 / 7040 / 7736 / 8000 / Ryzen AI 300 / AI MAX 300 / AI 400 / PMF | 25.0.6.0 / 25.2.6.0 / — / 25.5.25.0 / — | 25.0.7.0 / 26.2.8.0 / 24.1.20.0 / 26.10.15.0 … |
| **AMD PPM Provisioning File Driver** | 8.0.0.65 | 8.0.0.65 |
| AMD USB4 CM | 1.0.0.43 | — |
| AMD AMS Mailbox | 5.0.0.1075 | 6.0.0.13 |
| AMD S0i3 Filter | 1.0.0.12 | 1.0.0.12 |
| **AMD 3D V-Cache Performance Optimizer** | 1.0.0.12 | 1.0.0.12 |
| AMD Interface Driver (AMD PCI, SMBus, Hetero, RCEC) | 2.0.0.29 | 2.0.0.29 |
| AMD Provisioning for OEM | 1.0.0.6 | 1.0.0.6 |
| AMD Application Compatibility Database Driver | 1.0.0.3 | 1.0.0.3 |
| AMD NULL Driver for Microsoft Pluton 1/2 | 1.0.0.0 / 1.0.0.2 | — |
| AMD HSMP | 1.0.0.9 | 1.0.0.9 |

Установщик ставит только то, для чего есть железо. Список чипсетов: WRX90/TRX50, WRX80, TRX40, X870E/X870, B850, B840, X670E/X670, B650E/B650, X570, B550, **A520**, X399, X470, B450, X370, B350, A320 (Win10/Win11). CPU: Ryzen 1000–9000, G-серии, мобильные. 1-е поколение Ryzen, 2000G, TR 1000/2000 и 7-е поколение A-Series поддерживаются **только на Win10**. Ryzen AI 300/400/MAX — **только на Win11**.

Состояние машины разработчика (A520 + 5700X, только чтение):
- Устройства:
  - `PCI\VEN_1022&DEV_790B&REV_61` (SMBus, драйвер AMD 5.12.0.44, oem21);
  - `ACPI\AMDI0030` (GPIO2 2.2.0.137, oem19);
  - `ACPI\AMDIF030` (PT GPIO 3.0.5.0, oem9);
  - `PCI\VEN_1022&DEV_1485/148A` (AMD PCI 1.0.0.90, oem20);
  - **`PCI\VEN_1022&DEV_1486` = PSP на inbox-драйвере `machine.inf` («Platform Security Processor»)** — AMD PSP-драйвер не установлен;
  - PPM provisioning в `pnputil /enum-drivers` нет, плана питания AMD в `powercfg /l` нет.
- В `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\AMD_Chipset_IODrivers` есть `DisplayName=AMD Chipset Software`, `DisplayVersion=8.08.12.551`, `UninstallString="C:\Program Files (x86)\AMD\Chipset_Software\QT_Dependencies\Setup.exe" /U {5af2fedb-5a3e-4b58-882b-67587757e90b}`. Но **файла нет** (`Test-Path` = False). Рядом MSI `{5af2fedb-5a3e-4b58-882b-67587757e90b}` «AMD_Chipset_Drivers» 8.08.12.551.
  `C:\AMD\Chipset_Software\Logs\Device_ID.log` от 17.09.2026 содержит строки удаления/очистки папок и определение CPU `AMD64 Family 25 Model 33 Stepping 2` (Zen 3 Vermeer).
  **Вывод для кода: записи в Uninstall бывают «мёртвыми»**. Проверять существование файла из `UninstallString`, а для фактической версии смотреть драйверы (`Win32_PnPSignedDriver`).
- `InstallSource` указывает на `...\AppData\...\Roaming\AMD\Chipset_Software\{17D6B7B8-4B00-422D-A64B-453DE49286FB}\`. Установщик распаковывается в `%APPDATA%\AMD\Chipset_Software\{GUID}\`; при запуске из упакованного приложения путь виртуализирован.

### 3.4. Нужен ли чипсет на Windows 11

| Конфигурация | Оценка | Обоснование |
|---|---|---|
| **X3D с двумя CCD** (7900X3D, 7950X3D, 9900X3D, 9950X3D; эвристика: имя содержит `X3D` и ядер ≥ 12) | **proven** | Гайд AMD для рецензентов 7000X3D (Tom's Hardware, PCPer, HWBusters): нужен чипсет ≥ 5.02.16.347 с **3D V-Cache Performance Optimizer** и **PPM Provisioning**, включённый **Game Mode** и **Xbox Game Bar** ≥ 5.823.1271.0. Драйвер по сигналу Game Bar «паркует» CCD без V-Cache, когда запущена игра. Без этого игра может попасть на «частотный» CCD, и FPS проседает (воспроизводимо). **Отключать Game Bar на таких CPU нельзя** — флаг для других модулей твикера |
| X3D с одним CCD (5800X3D, 5700X3D, 7800X3D, 9800X3D) | situational | Парковать нечего (один CCD). Чипсет полезен как набор системных драйверов, прироста FPS от него нет |
| Zen 5 (Family 0x1A, Ryzen 9000/AI) | situational (рекомендуется) | PPM Provisioning обновляется под новые CPU («New program support added»). Отдельных замеров у AMD не найдено, **НЕ ПРОВЕРЕНО** |
| Прочие Ryzen (в т.ч. 5700X на A520) | situational | Windows 11 имеет inbox-драйверы для большинства устройств (PSP на этой машине — inbox «Platform Security Processor»). AMD-пакет даёт PSP 5.46, SMBus, GPIO, PCI, Interface driver и PPM provisioning для штатной схемы «Сбалансированная». Измеренного влияния на FPS/задержки не найдено. Вендор рекомендует ставить, вреда нет |
| Windows 10 22H2 | situational | Тот же набор + «AMD Ryzen Power Plan». Создаёт ли 8.09 отдельный план «AMD Ryzen Balanced» на Win10 22H2 — **НЕ ПРОВЕРЕНО**. Исторически план вошёл в чипсет 17.10 (2017) и менял пороги P-state/парковку для Zen/Zen+ |
| Intel CPU | n/a | Не предлагать |

### 3.5. Как узнать установленную версию чипсета

1. `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\AMD_Chipset_IODrivers` → `DisplayVersion`, `UninstallString`, `Publisher=Advanced Micro Devices, Inc.`. Обязательно проверить `File.Exists` пути из `UninstallString` (см. «мёртвую» запись выше).
2. Фактические драйверы: `Win32_PnPSignedDriver` с `DriverProviderName`/`Manufacturer` AMD:
   - `amdpsp.inf`/PSP — 5.46.0.0;
   - `smbusamd.inf` — 5.12.0.44;
   - `amdgpio2.inf` — 2.2.0.137;
   - `amdgpio3.inf` — 3.0.x;
   - `amdpcidev.inf` — 1.0.0.90.

   Имя INF PSP — **НЕ ПРОВЕРЕНО** (здесь PSP-драйвер не установлен).
3. Сравнение: `Version.Parse(display) < Version.Parse(latest)`.

---

## 4. Intel (кратко, на будущее)

| Что | Версия (2026-10-26) | Ссылка | Тихо | Примечания |
|---|---|---|---|---|
| Intel Chipset Device Software (INF Utility) | 10.1.20658.8883 (страница: 1/8/2026; PDF RN: 2026-07-24) | Страница `https://www.intel.com/content/www/us/en/download/19347/chipset-inf-utility.html`; файл `https://downloadmirror.intel.com/925343/SetupChipset.exe` (102 896 464 Б, LM 2026-08-03, **заголовки не нужны**); RN `https://downloadmirror.intel.com/925343/Release_notes_10.1.20658.8883-Public-Client.pdf` | `SetupChipset.exe /exenoui /qn /norestart`; удаление `SetupChipset.exe /x // /qn`; распаковка `SetupChipset.exe /exenoui /qn ONLY_EXTRACT="c:\temp\drivers" REINSTAL=ALL REINSTALMODE=vomus`; лог `/exelog <path>` | По readme (пакеты 10.1.20266.8668 и 10.1.20440.8804): ставит **только INF-файлы**, служит для опознания устройств и Modern Standby. Дата INF символическая (07/18/1968), чтобы не перекрывать другие драйверы: «Updating Intel(R) Chipset Device Software is not needed». SHA256 на странице: `F20B9F97E9A81E613D4D529AAD5D3948F5E449998CD118C6DEF4037CA6536446`. Признак установки: `HKLM\Software\Intel\InfInst` |
| Intel Driver & Support Assistant | 26.2.0.7 (winget `Intel.IntelDriverAndSupportAssistant`) | `https://dsadata.intel.com/installer` → `200`, `Content-Disposition: attachment; filename=Intel-Driver-and-Support-Assistant-Installer.exe`, 8 724 776 Б, LM 2026-07-14, заголовки не нужны | Тип установщика по winget — **WiX Burn** → `/quiet /norestart` (стандарт Burn; **НЕ ПРОВЕРЕНО**) | Ставит фоновую службу и трей. Для твикера — только по запросу |
| Intel Arc / Core Ultra Graphics | 32.0.101.9150 WHQL (страница: «10/2/2026») | Страница `https://www.intel.com/content/www/us/en/download/785597/intel-arc-graphics-windows.html`; файл `https://downloadmirror.intel.com/930514/gfx_win_101.9150.exe` (904 501 568 Б, LM 2026-10-14; id `930514` взят из ссылок на RN/readme на странице) | Распаковать (RAR5-SFX: сигнатура `Rar!\x1A\x07\x01\x00` после стаба 592 384 Б; 7-Zip распаковывает RAR5) и запустить `Installer.exe -s` (или `--silent`). Пример Intel: `Installer.exe --overwrite -s`. Полный список ключей — `Installer.exe --help`. Ключ `--noExtras` в официальной статье не найден | Коды выхода (Intel, драйверы ≥ 30.0.100.9684): 0 SUCCESS, 1 ERROR (лог `C:\ProgramData\Intel\GFXInstaller\IntelGfx.log`), 2 REBOOTING_SYSTEM, 5 PLATFORM_UNSUPPORTED, 6 CLOSED_BY_USER, 7 ERROR_BAD_COMMAND, 8 DRIVER_FILE_NOT_FOUND, 9 DRIVER_DIGITAL_SIGNATURE_MISSING, 10 EXTRAS_DIGITAL_SIGNATURE_MISSING, 11 INSUFFICIENT_DISK_SPACE, 13 REBOOT_REQUIRED_BEFORE_SETUP, 14 REBOOT_REQUIRED. В старых сборках «тихий» режим всё равно показывал прогресс-бар распаковки, Intel заявила исправление в 32.0.101.6647 |

Ограничение: `www.intel.com/content/...` отвечает **403** на `HEAD` и `GET` из curl даже с браузерным UA (бот-защита). Попытку подстроить «браузерные» заголовки отклонил классификатор безопасности, и **обходить защиту нельзя**. Поэтому:
- (а) скриптово актуальную версию Arc с intel.com не получить;
- (б) `downloadmirror.intel.com/<id>/` содержит непредсказуемый числовой id.

Варианты для приложения: открывать страницу Intel в браузере пользователя; предложить DSA; для Chipset INF — ничего не делать (Intel: обновлять не нужно). Device ID Intel GPU: `VEN_8086&DEV_56xx` (Alchemist), `VEN_8086&DEV_E20B/E20C` (Battlemage) — справочно, **НЕ ПРОВЕРЕНО** против списков драйвера.

---

## 5. Каталог твиков/действий для приложения (с оценкой доказательности)

Формат: `id` — evidence — условия; тексты для UI (RU).

### 5.1. Radeon

**`drivers.amd.gpu.update`** — **proven**
- Условия: найден AMD GPU, ветка определена (§2.6), установленная DS-версия < последней.
- title: «Обновить драйвер Radeon»
- description: «Установит последнюю версию AMD Software: Adrenalin Edition для вашей видеокарты (ветка определяется автоматически по ID устройства). В каждой версии AMD исправляет вылеты и статтеры и добавляет оптимизации под новые игры — см. release notes.»
- recommendation: «Рекомендуется, если драйвер старше 2–3 месяцев или есть проблемы в конкретной игре. Перед понижением версии сначала выполните очистку (AMD Cleanup Utility).»

**`drivers.amd.gpu.mode.full`** — **proven** (штатный путь вендора)
- title: «Полная установка (по умолчанию)»
- description: «Драйвер + панель AMD Software со всеми функциями: Anti-Lag, Chill, Radeon Boost, RSR/AFMF, запись, оверлей, разгон/андервольт.»
- recommendation: «Выбирайте, если пользуетесь функциями панели AMD.»

**`drivers.amd.gpu.mode.minimal`** — **situational**
- title: «Минимальная установка»
- description: «Официальный режим AMD «Minimal»: базовые настройки без записи, оверлея и тонкой настройки производительности.»
- recommendation: «Компромисс между функциональностью и числом фоновых процессов. Прирост FPS не доказан.»

**`drivers.amd.gpu.mode.driver_only`** — **situational**
- Условия: пользователь не нуждается в панели AMD. Реализация: распаковка + `pnputil /add-driver ...\*.inf /subdirs /install`.
- title: «Только драйвер (без панели AMD)»
- description: «Устанавливает только сам драйвер видеокарты (включая OpenGL/Vulkan/OpenCL), без приложения AMD Software и его фоновых служб. Пропадут Anti-Lag, Chill, FSR/AFMF в драйвере, запись и разгон.»
- recommendation: «Только для тех, кто настраивает всё в играх. Измеримого прироста FPS нет — выигрыш лишь в меньшем числе фоновых процессов.»

**`drivers.amd.gpu.cleanup_before_downgrade`** — **proven** (вендор рекомендует в RN)
- title: «Очистка перед откатом драйвера»
- description: «Запускает AMD Cleanup Utility, которая удаляет все компоненты драйвера AMD. AMD рекомендует её при переходе на более старую версию. Потребуется перезагрузка (желательно в безопасном режиме).»
- recommendation: «Используйте при откате версии или при переходе между ветками (RDNA ↔ Polaris/Vega).»

**`drivers.amd.gpu.factory_reset`** — **situational**
- title: «Установка с «Factory Reset»»
- description: «Режим установщика AMD: удаляет все прежние версии, временно приостанавливает Windows Update, перезагружает ПК и продолжает установку. Откат на предыдущий драйвер после этого невозможен.»
- recommendation: «При сбоях после обновления или смене видеокарты AMD.»

**`drivers.amd.gpu.slim.telemetry`** — **situational** (приватность, не производительность)
- Объекты: служба `AUEPLauncher`, задания `AMDInstallUEP`/`StartAUEP`, MSI AUEP.
- title: «Отключить телеметрию AMD (User Experience Program)»
- description: «Отключает программу сбора данных AMD User Experience Program. На работу драйвера и FPS не влияет.»
- recommendation: «Безопасно. Делайте ради приватности, не ради производительности.»

**`drivers.amd.gpu.slim.autostart`** — **situational**
- Объекты: задания `StartCN`, `StartDVR`, автозапуск UWP `launcherrsxruntimeTask`.
- title: «Не запускать AMD Software при входе в Windows»
- description: «Панель AMD и хост-служба не будут стартовать автоматически. Оверлей, запись и горячие клавиши AMD перестанут работать, пока вы не откроете панель вручную.»
- recommendation: «Если не пользуетесь оверлеем и записью AMD.»

**`drivers.amd.gpu.slim.crash_defender`** — **experimental**
- Объекты: `amdfendr`, `amdfendrmgr`, `AMD Crash Defender Service`, `amdlog`.
- title: «Отключить AMD Crash Defender»
- description: «Служба собирает расширенную диагностику при сбоях драйвера для отчётов в AMD. Влияние на производительность не доказано.»
- recommendation: «Эксперимент. Не отключайте, если отправляете отчёты об ошибках в AMD.»

**`drivers.amd.gpu.slim.amdlink_ryzenmaster`** — **experimental**
- Объекты: `AMDXE`, `AMDLinkDriverUpdate`/`ModifyLinkUpdate`, `RyzenMasterSDK`, `AMDRyzenMasterSDKTask`, `AMD WVR64`.
- title: «Убрать AMD Link, VR-модуль и метрики Ryzen Master»
- description: «Не устанавливает или отключает компоненты для AMD Link (эмуляция геймпада), VR-стриминга и CPU-метрик в оверлее AMD.»
- recommendation: «Безопасно, если эти функции не используются. Прироста FPS не ожидается.»

**`avoid`** — в приложение **не включать**, фиксируем причину:
- `drivers.amd.gpu.disable_external_events` — отключение «AMD External Events Utility» ломает FreeSync/VRR (PC-Tuning: «required for VRR»; RSS).
- `drivers.amd.gpu.remove_amdkmpfd` — фильтр PCI root bus нужен для Smart Access Memory; переустановка «дёргает» USB.
- `drivers.amd.gpu.clear_shader_cache_for_fps` — очистка кэшей шейдеров вызывает перекомпиляцию и статтеры.
- `drivers.amd.gpu.modify_inf` / «кастомные» INF / установка драйвера чужой ветки через Have Disk — ломает подпись, античиты, HVCI; INF другой ветки не содержит ваш DEV.
- `drivers.amd.gpu.disable_enable_windows_driver_search` — задание AMD возвращает поиск драйверов в WU; отключать незачем.

### 5.2. Чипсет AMD

**`drivers.amd.chipset.install`** — **situational** (для всех Ryzen); **proven** в связке с `x3d` ниже
- Условия: CPU AMD Ryzen/Threadripper, чипсет из списка; Win10 22H2 / Win11.
- title: «Установить драйверы чипсета AMD»
- description: «Официальный пакет AMD Chipset Software: драйверы PSP, SMBus, GPIO, PCI, файл настроек управления питанием процессора (PPM Provisioning) и др. Ставится только то, для чего есть оборудование.»
- recommendation: «Рекомендуется AMD для всех систем Ryzen. Для обычных CPU прирост FPS не измерен; для X3D с двумя CCD — обязателен.»

**`drivers.amd.chipset.x3d_dual_ccd`** — **proven**
- Условия: `Win32_Processor.Name` содержит `X3D` и `NumberOfCores >= 12`.
- title: «Оптимизация X3D (два CCD): чипсет + Game Bar»
- description: «Для Ryzen 9 7900X3D/7950X3D/9900X3D/9950X3D AMD требует свежий драйвер чипсета (3D V-Cache Performance Optimizer, PPM Provisioning), включённый «Игровой режим» и установленный Xbox Game Bar. Тогда игры работают на кристалле с увеличенным кэшем.»
- recommendation: «Обязательно для этих процессоров. Не отключайте Xbox Game Bar и «Игровой режим» — иначе игры могут попасть на «медленный» для них CCD.»

**`drivers.amd.chipset.ryzen_power_plan_win10`** — **situational**
- title: «План питания AMD Ryzen (Windows 10)»
- description: «На Windows 10 пакет чипсета содержит компонент AMD Ryzen Power Plan / Processor Power Management.»
- recommendation: «Актуально для Windows 10 и старых Ryzen (1000/2000). На Windows 11 используется штатная схема с параметрами из PPM Provisioning.»

**`avoid`**:
- удаление компонентов чипсета через Диспетчер устройств или сторонние «чистильщики» (AMD: «will have undesirable effects»; GPIO не удаляются даже штатно);
- установка чипсета 6.x поверх 7.x/8.x (известная проблема);
- AMD RAID без RAIDXpert.

### 5.3. Intel

**`drivers.intel.chipset_inf`** — **situational**
- title: «Intel Chipset Device Software»
- description: «Устанавливает только INF-файлы, чтобы Windows правильно называла устройства чипсета Intel. Драйверов и влияния на производительность нет.»
- recommendation: «Нужно только при «неизвестных устройствах» в Диспетчере. Intel сама пишет, что обновлять пакет не требуется.»

**`drivers.intel.arc.update`** — **proven** (вендорские исправления)
- title: «Обновить драйвер Intel Arc»
- description: «Последний WHQL-драйвер Intel Arc/Core Ultra Graphics с официального зеркала Intel, тихая установка.»
- recommendation: «Рекомендуется при проблемах в играх. Версию проверяйте на странице Intel (автоматическая проверка невозможна).»

**`drivers.intel.dsa`** — **situational**
- title: «Intel Driver & Support Assistant»
- description: «Официальная утилита Intel для поиска драйверов. Устанавливает фоновую службу.»
- recommendation: «Удобно, но держит процесс в фоне. Ставьте по желанию.»

---

## 6. Эталонная реализация (C#, .NET 10, x64, single-file)

Замечания по сборке:
- `System.Management` — NuGet-пакет, только Windows. При `PublishTrimmed=true` WMI/COM может сломаться: не тримить эти сборки или перейти на CfgMgr32/SetupAPI P/Invoke.
- Для `System.Text.Json` при тримминге/AOT использовать source-gen (`JsonSerializerContext`).

### 6.1. HTTP-клиент AMD

```csharp
using System.Net;
using System.Net.Http.Headers;

namespace Hateeze.Drivers.Amd;

public sealed class AmdHotlinkRejectedException(Uri url)
    : Exception($"AMD отклонил запрос (нет/неверный Referer): {url}");

public static class AmdHttp
{
    public static readonly Uri Referer = new("https://www.amd.com/");

    public static HttpClient Create(string appVersion)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,                       // 302 → Download-Incomplete нужно увидеть самим
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        // Честный UA: www.amd.com тарпитит curl/wget/python-requests, но пропускает собственный UA.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("hateeze", appVersion));
        http.DefaultRequestHeaders.Referrer = Referer;       // обязателен для drivers.amd.com
        return http;
    }

    /// www2.ati.com и http:// всё равно отдают 301 на https://drivers.amd.com/<тот же путь>.
    public static Uri Normalize(string url)
    {
        var b = new UriBuilder(url);
        if (b.Host.Equals("www2.ati.com", StringComparison.OrdinalIgnoreCase)) b.Host = "drivers.amd.com";
        if (b.Host.EndsWith("amd.com", StringComparison.OrdinalIgnoreCase)) { b.Scheme = "https"; b.Port = -1; }
        return b.Uri;
    }

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http, HttpMethod method, Uri url, CancellationToken ct,
        Action<HttpRequestHeaders>? headers = null, TimeSpan? timeout = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } t) cts.CancelAfter(t);
        for (var hop = 0; hop < 5; hop++)
        {
            var req = new HttpRequestMessage(method, url);
            headers?.Invoke(req.Headers);
            var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var code = (int)resp.StatusCode;
            if (code is 301 or 302 or 303 or 307 or 308 && resp.Headers.Location is { } loc)
            {
                var next = loc.IsAbsoluteUri ? loc : new Uri(url, loc);
                resp.Dispose();
                if (next.AbsolutePath.Contains("Download-Incomplete", StringComparison.OrdinalIgnoreCase))
                    throw new AmdHotlinkRejectedException(url);
                url = Normalize(next.ToString());
                continue;
            }
            return resp;
        }
        throw new HttpRequestException("Слишком много перенаправлений");
    }
}
```

### 6.2. Каталог версий

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hateeze.Drivers.Amd;

public enum AmdBranch { Rdna3Rdna4, Rdna1Rdna2, AllRdnaCombined, PolarisVega }

public sealed class AmdDriverDetails
{
    [JsonPropertyName("externalbuildversion")] public string ExternalVersion { get; init; } = "";
    [JsonPropertyName("internalbuildversion")] public string InternalVersion { get; init; } = "";
    [JsonPropertyName("fullbuild")]            public string FullBuildUrl { get; init; } = "";
    [JsonPropertyName("fullbuildfilesize")]    public string FullBuildSizeMiB { get; init; } = "";
    [JsonPropertyName("minimalbuild")]         public string MinimalBuildUrl { get; init; } = "";
    [JsonPropertyName("releasedate")]          public string ReleaseDateRaw { get; init; } = "";   // MM/dd/yyyy
    [JsonPropertyName("releasenotes")]         public string ReleaseNotesUrl { get; init; } = "";
    [JsonPropertyName("skus")]                 public string[] Skus { get; init; } = [];

    [JsonIgnore] public string DriverVersion => InternalVersion.Split('-')[0];          // "26.20.15.07"
    [JsonIgnore] public string InfChangelist =>                                           // "205306" → u0205306.inf
        InternalVersion.Split('-').Skip(2).FirstOrDefault()?.TrimEnd('C', 'c') ?? "";
    [JsonIgnore] public DateOnly? ReleaseDate =>
        DateOnly.TryParseExact(ReleaseDateRaw, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}

public static class AmdCatalog
{
    public const string VersionIndexUrl = "https://drivers.amd.com/drivers/installer/json/version.json";

    public static readonly IReadOnlyDictionary<AmdBranch, string> IndexKeys = new Dictionary<AmdBranch, string>
    {
        [AmdBranch.Rdna3Rdna4]      = "Consumer_Whql_Win10_productionASICs",
        [AmdBranch.Rdna1Rdna2]      = "Consumer_Maintenance_Win10",
        [AmdBranch.AllRdnaCombined] = "Consumer_Whql_Win10_AllAsics",
        [AmdBranch.PolarisVega]     = "Consumer_Legacy_Win10",
    };

    public static async Task<Dictionary<AmdBranch, AmdDriverDetails>> LoadAsync(HttpClient http, CancellationToken ct)
    {
        using var idxResp = await AmdHttp.SendAsync(http, HttpMethod.Get, new Uri(VersionIndexUrl), ct, timeout: TimeSpan.FromSeconds(30));
        idxResp.EnsureSuccessStatusCode();
        using var idx = await JsonDocument.ParseAsync(await idxResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        var result = new Dictionary<AmdBranch, AmdDriverDetails>();
        foreach (var (branch, key) in IndexKeys)
        {
            if (!idx.RootElement.TryGetProperty(key, out var e) || e.ValueKind != JsonValueKind.Object) continue;
            if (!e.TryGetProperty("url", out var u) || u.GetString() is not { } url) continue;

            using var r = await AmdHttp.SendAsync(http, HttpMethod.Get, AmdHttp.Normalize(url), ct, timeout: TimeSpan.FromSeconds(30));
            r.EnsureSuccessStatusCode();
            var arr = await JsonSerializer.DeserializeAsync<AmdDriverDetails[]>(await r.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (arr is { Length: > 0 }) result[branch] = arr[0];
        }
        return result;
    }
}
```

### 6.3. Поиск GPU и выбор ветки

```csharp
using System.Management;
using System.Text.RegularExpressions;

namespace Hateeze.Drivers.Amd;

public sealed record AmdGpu(string InstanceId, string Name, string Dev, string Rev);

public enum SkuMatch { None, DevOnly, Exact }

public sealed record BranchDecision(AmdBranch? Branch, string Reason, IReadOnlyList<(AmdGpu Gpu, AmdBranch? Branch, SkuMatch Match)> Details);

public static partial class AmdGpuDetector
{
    [GeneratedRegex(@"VEN_1002&DEV_(?<dev>[0-9A-F]{4}).*?&REV_(?<rev>[0-9A-F]{2})", RegexOptions.IgnoreCase)]
    private static partial Regex PciId();

    public static List<AmdGpu> Enumerate()
    {
        var list = new List<AmdGpu>();
        using var s = new ManagementObjectSearcher(
            @"SELECT DeviceID, Name, HardwareID FROM Win32_PnPEntity WHERE DeviceID LIKE 'PCI\\VEN_1002%'");
        foreach (ManagementObject o in s.Get())
        {
            var hw = o["HardwareID"] as string[] ?? [];
            // только дисплейный класс 03xx (0300 VGA, 0380 Display); HDMI-аудио AMD = CC_0403
            if (!hw.Any(h => h.Contains("&CC_03", StringComparison.OrdinalIgnoreCase))) continue;
            var id = (string)o["DeviceID"];
            var m = PciId().Match(id);
            if (!m.Success) continue;
            list.Add(new AmdGpu(id, o["Name"] as string ?? "", m.Groups["dev"].Value.ToUpperInvariant(), m.Groups["rev"].Value.ToUpperInvariant()));
        }
        return list;
    }

    public static BranchDecision Resolve(IReadOnlyList<AmdGpu> gpus, IReadOnlyDictionary<AmdBranch, AmdDriverDetails> cat)
    {
        AmdBranch[] order = [AmdBranch.Rdna3Rdna4, AmdBranch.Rdna1Rdna2, AmdBranch.PolarisVega];
        var details = new List<(AmdGpu, AmdBranch?, SkuMatch)>();
        foreach (var g in gpus)
        {
            (AmdBranch? b, SkuMatch m) best = (null, SkuMatch.None);
            foreach (var b in order)
            {
                if (!cat.TryGetValue(b, out var d)) continue;
                if (d.Skus.Contains($"{g.Dev}-{g.Rev}", StringComparer.OrdinalIgnoreCase)) { best = (b, SkuMatch.Exact); break; }
                if (best.m == SkuMatch.None && d.Skus.Any(s => s.StartsWith(g.Dev + "-", StringComparison.OrdinalIgnoreCase)))
                    best = (b, SkuMatch.DevOnly);
            }
            details.Add((g, best.b, best.m));
        }

        var set = details.Where(x => x.Item2 is not null).Select(x => x.Item2!.Value).ToHashSet();
        if (set.Count == 0)
            return new(null, "Видеокарта AMD не найдена или не поддерживается Adrenalin (старый GCN, Steam Deck и т.п.)", details);
        if (set.Contains(AmdBranch.PolarisVega) && set.Count > 1)
            return new(null, "В системе одновременно Polaris/Vega и RDNA: разные стеки драйверов, выберите основную видеокарту", details);
        if (set.SetEquals([AmdBranch.Rdna3Rdna4, AmdBranch.Rdna1Rdna2]))
            return new(AmdBranch.AllRdnaCombined, "RDNA1/2 + RDNA3/4 (например iGPU Ryzen 7000/9000 + RX 7000/9000) → комбинированный пакет", details);
        var only = set.Single();
        var reason = details.Any(x => x.Item3 == SkuMatch.DevOnly)
            ? "Ревизия устройства не в официальном списке AMD (ноутбук/handheld?) — рекомендуется драйвер производителя ПК"
            : "Официально поддерживается";
        return new(only, reason, details);
    }
}
```

### 6.4. Загрузка с докачкой и проверкой подписи

```csharp
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Hateeze.Drivers.Amd;

public static class AmdDownloader
{
    public static async Task<long> GetLengthAsync(HttpClient http, Uri url, CancellationToken ct)
    {
        using var r = await AmdHttp.SendAsync(http, HttpMethod.Head, url, ct, timeout: TimeSpan.FromSeconds(30));
        if (r.StatusCode == HttpStatusCode.NotFound) throw new FileNotFoundException("Файл на сервере AMD не найден", url.ToString());
        r.EnsureSuccessStatusCode();
        return r.Content.Headers.ContentLength ?? throw new InvalidDataException("Нет Content-Length");
    }

    public static async Task DownloadAsync(HttpClient http, Uri url, string path, IProgress<double>? progress, CancellationToken ct)
    {
        var total = await GetLengthAsync(http, url, ct);
        long have = File.Exists(path) ? new FileInfo(path).Length : 0;
        if (have > total) { File.Delete(path); have = 0; }

        using var resp = await AmdHttp.SendAsync(http, HttpMethod.Get, url, ct,
            h => { if (have > 0) h.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null); });
        resp.EnsureSuccessStatusCode();
        if (have > 0 && resp.StatusCode != HttpStatusCode.PartialContent) have = 0;   // сервер проигнорировал Range

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(path, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
        var buf = new byte[1 << 20];
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            have += n;
            progress?.Report((double)have / total);
        }
        if (have != total) throw new IOException($"Размер не совпал: {have} из {total}");
    }
}

public static class Authenticode
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO { public uint cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct; public IntPtr pPolicyCallbackData; public IntPtr pSIPClientData;
        public uint dwUIChoice; public uint fdwRevocationChecks; public uint dwUnionChoice; public IntPtr pFile;
        public uint dwStateAction; public IntPtr hWVTStateData; public IntPtr pwszURLReference;
        public uint dwProvFlags; public uint dwUIContext; public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

    public static bool IsTrustedAndSignedBy(string path, string expectedCn = "Advanced Micro Devices")
    {
        var fi = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
        var pFi = Marshal.AllocHGlobal(Marshal.SizeOf(fi));
        try
        {
            Marshal.StructureToPtr(fi, pFi, false);
            var wd = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2,            // WTD_UI_NONE
                fdwRevocationChecks = 0,   // WTD_REVOKE_NONE (офлайн-безопасно)
                dwUnionChoice = 1,         // WTD_CHOICE_FILE
                pFile = pFi,
                dwStateAction = 1,         // WTD_STATEACTION_VERIFY
            };
            var g = GenericVerifyV2;
            var hr = WinVerifyTrust(IntPtr.Zero, ref g, ref wd);
            wd.dwStateAction = 2;          // WTD_STATEACTION_CLOSE
            WinVerifyTrust(IntPtr.Zero, ref g, ref wd);
            if (hr != 0) return false;
        }
        finally { Marshal.FreeHGlobal(pFi); }

        // Издатель (на 2026-10: CN=Advanced Micro Devices, O=Advanced Micro Devices, S=California, C=US)
        // Если CreateFromSignedFile окажется помечен obsolete в вашей версии SDK — заменить на CryptQueryObject.
        using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
        return string.Equals(cert.GetNameInfo(X509NameType.SimpleName, false), expectedCn, StringComparison.OrdinalIgnoreCase);
    }
}
```

### 6.5. Распаковка и установка

```csharp
using System.Diagnostics;

namespace Hateeze.Drivers.Amd;

public static class AmdInstaller
{
    public static async Task<int> RunAsync(string exe, string args, CancellationToken ct, string? workDir = null)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = workDir ?? Path.GetDirectoryName(exe)!,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var so = p.StandardOutput.ReadToEndAsync(ct);
        var se = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        await Task.WhenAll(so, se);
        return p.ExitCode;
    }

    /// 7za.exe — встроенный ресурс приложения (7-Zip Extra, LGPL) или установленный 7-Zip.
    public static async Task ExtractAsync(string sevenZip, string package, string outDir, CancellationToken ct)
    {
        Directory.CreateDirectory(outDir);
        var code = await RunAsync(sevenZip, $"x \"{package}\" -o\"{outDir}\" -y -bso0 -bsp0", ct);
        if (code is not (0 or 1)) throw new IOException($"7-Zip завершился с кодом {code}");
        if (!File.Exists(Path.Combine(outDir, "Setup.exe")) || !Directory.Exists(Path.Combine(outDir, "Packages", "Drivers")))
            throw new InvalidDataException("Неожиданная структура пакета AMD");
    }

    /// Полная установка через CIM (документ AMD 2019). Результат — по ResultCode в логе.
    public static async Task<int?> InstallFullAsync(string extracted, string logDir, CancellationToken ct)
    {
        Directory.CreateDirectory(logDir);
        var log = Path.Combine(logDir, "amd_install_result.log");
        var outp = Path.Combine(logDir, "amd_install_output.txt");
        await RunAsync(Path.Combine(extracted, "Setup.exe"), $"-INSTALL -OUTPUT \"{outp}\" -LOG \"{log}\"", ct, extracted);
        if (!File.Exists(log)) return null;                                     // НЕ ПРОВЕРЕНО на 26.x: может не создаться
        foreach (var line in File.ReadLines(log))
            if (line.TrimStart().StartsWith("ResultCode", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line.Split('=').Last().Trim(), out var rc))
                return rc;                                                      // 0 PASS, 1 FAIL, 2 FAIL+reboot
        return null;
    }

    /// Только драйвер: база + extension-INF (+Display2 в combined) + опционально HDMI-аудио.
    public static async Task<List<(string Inf, int Code)>> InstallDriverOnlyAsync(string extracted, bool hdmiAudio, CancellationToken ct)
    {
        var pnputil = Path.Combine(Environment.SystemDirectory, "pnputil.exe");
        var results = new List<(string, int)>();
        var drivers = Path.Combine(extracted, "Packages", "Drivers");
        foreach (var display in Directory.EnumerateDirectories(drivers, "Display*"))       // Display, Display2
        foreach (var infDir in Directory.EnumerateDirectories(display, "*_INF"))            // WT6A_INF (имя может меняться)
        {
            // 1) базовый INF верхнего уровня (u0xxxxxx.inf), 2) INF-расширения в подпапках
            var infs = Directory.EnumerateFiles(infDir, "*.inf", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateDirectories(infDir)
                    .SelectMany(d => Directory.EnumerateFiles(d, "*.inf", SearchOption.TopDirectoryOnly)));
            foreach (var inf in infs)
                results.Add((inf, await RunAsync(pnputil, $"/add-driver \"{inf}\" /install", ct)));
        }
        if (hdmiAudio)
            foreach (var inf in Directory.EnumerateFiles(Path.Combine(drivers, "Audio"), "AtihdWT6.inf", SearchOption.AllDirectories))
                results.Add((inf, await RunAsync(pnputil, $"/add-driver \"{inf}\" /install", ct)));
        // 0 = ок, 3010 = ок+reboot, 259 = не подошло/уже установлен драйвер лучше/новее, иное = ошибка (см. setupapi.dev.log)
        return results;
    }
}
```

### 6.6. Чипсет AMD

```csharp
using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace Hateeze.Drivers.Amd;

public static partial class AmdChipset
{
    public const string ProbePage = "https://www.amd.com/en/support/downloads/drivers.html/chipsets/am4/a520.html";

    [GeneratedRegex(@"https://drivers\.amd\.com/drivers/amd_chipset_software_(?<v>\d+\.\d+\.\d+\.\d+)\.exe", RegexOptions.IgnoreCase)]
    private static partial Regex Link();

    public static async Task<(Version Version, Uri Url)?> GetLatestAsync(HttpClient http, CancellationToken ct)
    {
        using var r = await AmdHttp.SendAsync(http, HttpMethod.Get, new Uri(ProbePage), ct, timeout: TimeSpan.FromSeconds(30));
        r.EnsureSuccessStatusCode();
        var html = await r.Content.ReadAsStringAsync(ct);
        return Link().Matches(html)
            .Select(m => (V: Version.Parse(m.Groups["v"].Value), U: new Uri(m.Value)))
            .OrderByDescending(x => x.V).Cast<(Version, Uri)?>().FirstOrDefault();
    }

    public static Version? GetInstalledVersion()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\AMD_Chipset_IODrivers");
        if (k?.GetValue("DisplayVersion") is not string v) return null;
        // запись может быть «мёртвой»: проверяем, что деинсталлятор существует
        if (k.GetValue("UninstallString") is string us)
        {
            var exe = us.StartsWith('"') ? us[1..us.IndexOf('"', 1)] : us.Split(' ')[0];
            if (!File.Exists(exe)) return null;
        }
        return Version.TryParse(v, out var ver) ? ver : null;
    }

    /// NSIS: /S. Успех: 0 (и 3010). 1603 бывает при частичных сбоях — перепроверить версию и логи C:\AMD\Chipset_Software\Logs.
    public static Task<int> InstallSilentAsync(string exe, CancellationToken ct) => AmdInstaller.RunAsync(exe, "/S", ct);
}
```

---

## 7. Риски и ловушки (сводка)

1. **Referer**: без него любые exe/JSON на `drivers.amd.com` отдают 302 на HTML-страницу. Если клиент слепо следует редиректам, получит HTML вместо exe (или зависнет на тарпите `www.amd.com` с bot-UA). Обязательно `AllowAutoRedirect=false` + проверка `Content-Type: application/octet-stream` + длина + подпись.
2. **Имена файлов и JSON меняются**: `-a/-b/-c/-s/-nov`, `Win10.json` заморожен. Всегда идти через `version.json`, ключи брать по смыслу и иметь фолбэк на HTML страницы продукта.
3. **Смешанные GPU** (iGPU RDNA2 + dGPU RDNA3/4) требуют `-c` (1.7 ГиБ + 4.7 ГиБ распаковки). Иначе одно из устройств останется на Basic Display Adapter.
4. **Legacy RN-ссылка** в JSON ведёт на чужие RN (см. §2.2).
5. **Driver-only без `/subdirs`** (или без перебора подпапок) — для RDNA3/4 не будет OpenGL/Vulkan/OpenCL ICD (`amdogl`/`amdvlk`/`amdocl` — отдельные INF).
6. **Понижение версии через pnputil** вернёт 259. Нужна предварительная очистка (AMD Cleanup Utility / DDU).
7. **Тихий режим CIM** документирован только в документе 2019 года. Были жалобы «-INSTALL ничего не ставит». Без теста на Radeon-машине включать в UI только с пометкой «экспериментально» или вообще не включать, а для полной установки запускать штатный GUI.
8. **Windows Update** может «поверх» поставить свой драйвер AMD. AMD-установщик управляет поиском драйверов в WU (`EnableWindowsDriverSearch`). Управление WU — тема другого модуля, но конфликт учитывать.
9. **Ноутбуки/AIO/handheld**: AMD сама рекомендует OEM-драйверы. Ревизии вне `skus` → предупреждать.
10. **X3D dual-CCD**: другие модули hateeze (отключение Game Bar/Game Mode, «Максимальная производительность» с запретом парковки) могут **сломать** механизм выбора CCD. Нужен кросс-модульный запрет.
11. **Чипсет**: «мёртвые» записи Uninstall; код 1603 при фактическом успехе; 6.x поверх 7.x+ не ставится; GPIO нельзя удалять.
12. **Intel**: скриптовая проверка версий Arc с intel.com невозможна (403, обход запрещён); id в `downloadmirror` непредсказуем.

---

## 8. НЕ ПРОВЕРЕНО (и почему)

| Пункт | Почему |
|---|---|
| Работа `whql-...exe -INSTALL/-SILENT/-factoryresetInstall` (SFX) | Запуск установщиков запрещён; вывод сделан по строкам стаба |
| Работа `Setup.exe -INSTALL -OUTPUT/-LOG` на 26.x, коды выхода процесса | Запуск запрещён + нет AMD GPU; документ 2019 года |
| Ставит ли pnputil-установка Adrenalin UI сама (SoftwareComponent/AddSoftware → `ccc2_install.exe`) или подтягивает UWP из Store | Текст INF в solid-блоке (~520 МБ распаковки); установка запрещена |
| Где OpenGL/Vulkan в пакетах `-a`/Polaris (нет `amdogl`/`amdvlk`) | Нужен текст INF |
| Значения реестра Radeon (`RadeonSoftwareVersion` и т.п.), DisplayName/UninstallString «AMD Software» | Нет AMD GPU на машине |
| Попадают ли Optional-драйверы в `version.json` | Наблюдали только текущее состояние (26.10.1 Recommended) |
| `/S` и `/CUSTOMINSTALL` на чипсете 8.09.28.1124, перезагрузка после `/S` | Запуск запрещён; есть только документ 2.04 и скрипт Chocolatey (8.08) |
| Создаёт ли чипсет на Win10 22H2 план «AMD Ryzen Balanced» | Нет Win10-машины, запуск запрещён |
| Эффект PPM Provisioning на Zen 5 (замеры) | Первоисточник AMD с цифрами не найден |
| CLI-ключи AMD Cleanup Utility | Не документированы, запуск запрещён |
| SharpCompress на AMD-архивах (LZMA2+BCJ/BCJ2 с оффсетом) | Не тестировалось |
| Intel DSA `/quiet /norestart`, Intel Arc `gfx_win_*.exe -s` без распаковки, `--noExtras` | Запуск запрещён; страница Intel недоступна скриптам |
| Списки Device ID Intel Arc | Не исследовалось (вне фокуса) |

---

## 9. Источники

AMD — машиночитаемые манифесты (проверено вживую 2026-10-26; снимки в `amd-installers-fixtures/`):
- https://drivers.amd.com/drivers/installer/json/version.json
- https://drivers.amd.com/drivers/installer/json/DrvDldDetails_Consumer_WHQL_Win11.json
- https://drivers.amd.com/drivers/installer/json/DrvDldDetails_Consumer_WHQL_Win11_c.json
- https://drivers.amd.com/drivers/installer/json/DrvDldDetails_Consumer_WHQL_Win11_p.json
- https://drivers.amd.com/drivers/installer/json/DrvDldDetails_Consumer_RDNA1_RDNA2_Win11.json
- https://drivers.amd.com/drivers/installer/json/DrvDldDetails_Consumer_Legacy_Win10.json
- https://drivers.amd.com/drivers/installer/json/DrvDldDetails_Consumer_WHQL_Win10.json (заморожен на 25.10.2)
- https://drivers.amd.com/drivers/installer/26.20/whql/buildhighlights-26.10.1.0.xml
- Упоминание `version.json` в сообществе AMD: https://community.amd.com/t5/pc-drivers-software/amd-software-adrenalin-edition-is-checking-for-updates-over/m-p/706368/highlight/true

AMD — файлы (HEAD/Range):
- https://drivers.amd.com/drivers/whql-amd-software-adrenalin-edition-26.10.1-win11-a.exe
- https://drivers.amd.com/drivers/whql-amd-software-adrenalin-edition-26.10.1-win11-b.exe
- https://drivers.amd.com/drivers/whql-amd-software-adrenalin-edition-26.10.1-win11-c.exe
- https://drivers.amd.com/drivers/whql-amd-software-adrenalin-edition-26.5.2-win11-may-vega-polaris.exe
- https://drivers.amd.com/drivers/installer/26.20/whql/amd-software-adrenalin-edition-26.10.1-minimalsetup-261018_web.exe
- https://drivers.amd.com/drivers/amd_chipset_software_8.09.28.1124.exe
- https://drivers.amd.com/drivers/amd_raid_software_2.20.19.037.exe
- https://drivers.amd.com/drivers/amdcleanuputility.exe
- https://drivers.amd.com/relnotes/command-line-installation.pdf

AMD — страницы:
- https://www.amd.com/en/support/download/drivers.html
- https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/radeon-rx-9000-series/amd-radeon-rx-9070-xt.html
- https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/radeon-rx-9000-series/amd-radeon-rx-9060-xt.html
- https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/radeon-rx-7000-series/amd-radeon-rx-7900-xtx.html
- https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/radeon-rx-6000-series/amd-radeon-rx-6800-xt.html
- https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/radeon-rx-5000-series/amd-radeon-rx-5700-xt.html
- https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-600-500-400/radeon-rx-500-series/radeon-rx-580.html
- https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/radeon-rx-vega-series/radeon-rx-vega-64.html
- https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/radeon-rx-vega-series/amd-radeon-vii.html
- https://www.amd.com/en/support/downloads/previous-drivers.html/graphics/radeon-rx/radeon-rx-7000-series/amd-radeon-rx-7900-xtx.html
- https://www.amd.com/en/support/downloads/previous-drivers.html/graphics/radeon-rx/radeon-rx-6000-series/amd-radeon-rx-6800-xt.html
- https://www.amd.com/en/support/downloads/previous-drivers.html/graphics/radeon-600-500-400/radeon-rx-500-series/radeon-rx-580.html
- https://www.amd.com/en/resources/support-articles/release-notes/RN-RAD-WIN-26-10-1.html
- https://www.amd.com/en/resources/support-articles/release-notes/rn-rad-win-26-5-2.html
- https://www.amd.com/en/resources/support-articles/release-notes/RN-RAD-WIN-26-5-2-POLARIS-VEGA.html
- https://www.amd.com/en/resources/support-articles/release-notes/RN-RAD-WIN-25-10-2.html
- https://www.amd.com/en/resources/support-articles/faqs/RSX2-INSTALL.html
- https://www.amd.com/en/resources/support-articles/faqs/GPU-601.html
- https://www.amd.com/en/resources/support-articles/faqs/Download-Incomplete.html
- https://www.amd.com/en/support/downloads/drivers.html/chipsets/am4/a520.html
- https://www.amd.com/en/support/downloads/drivers.html/chipsets/am4/x570.html
- https://www.amd.com/en/support/downloads/drivers.html/chipsets/am5/x870e.html
- https://www.amd.com/en/support/downloads/drivers.html/chipsets/str5/trx50.html
- https://www.amd.com/en/support/downloads/drivers.html/processors/ryzen/ryzen-5000-series/amd-ryzen-7-5700x.html
- https://www.amd.com/en/resources/support-articles/release-notes/RN-RYZEN-CHIPSET-8-09-28-1124.html
- https://www.amd.com/en/resources/support-articles/release-notes/RN-RYZEN-CHIPSET-8-08-12-551.html
- https://www.amd.com/en/resources/support-articles/release-notes/RN-RYZEN-CHIPSET-7-06-02-123.html
- https://www.amd.com/en/resources/support-articles/release-notes/RN-RYZEN-CHIPSET-5-08-02-027.html
- https://www.amd.com/en/resources/support-articles/release-notes/RN-RYZEN-CHIPSET-3-08-17-735.html
- https://www.amd.com/en/resources/support-articles/release-notes/amd-ryzen--chipset-driver-release-notes--2-04-04-111-.html
- https://www.amd.com/en/resources/support-articles/faqs/chipset-install.html

Сторонние/открытые:
- https://github.com/GSDragoon/RadeonSoftwareSlimmer (коммит cec9196, 2026-10-13): `src/RadeonSoftwareSlimmer.Core/PreInstall/*`, `PostInstall/*`
- https://github.com/GSDragoon/RadeonSoftwareSlimmer/wiki (Installer-Packages, System-Services-and-Drivers, Scheduled-Tasks, Host-Services, Installer-Entries, Logging-and-Temp-Files)
- https://github.com/valleyofdoom/PC-Tuning/blob/main/docs/configure-amd.md (коммит d991648, 2026-09-29)
- https://community.chocolatey.org/packages/amd-ryzen-chipset
- https://pci-ids.ucw.cz/v2.2/pci.ids
- https://www.tomshardware.com/news/amd-ryzen-7000x3d-install-guide
- https://hwbusters.com/cpu/amd-ryzen-9-7950x3d-core-parking-problem-solution/3/
- https://pcper.com/2023/03/amd-ryzen-9-7950x3d-initial-performance-am5-enters-the-x3d-era/
- https://www.notebookcheck.net/AMD-Ryzen-9-9950X3D-Review-Ryzen-7-9800X3D-levels-of-gaming-performance-and-power-efficiency-for-50-markup-over-the-Ryzen-9-9950X.976049.0.html
- https://hothardware.com/news/amds-chipset-driver-for-v-cache-cpus
- https://bit-tech.net/news/tech/cpus/amd-ryzen-power-plan-release/1
- https://community.amd.com:443/t5/pc-drivers-software/motherboard-chipset-drivers-set-return-code-to-1603-even-on/m-p/93558/highlight/true
- https://community.amd.com/t5/pc-drivers-software/trouble-using-cmd-to-silent-install-radeon-software/m-p/603452/highlight/true (ветка закрыта, редирект)
- https://gpuopen.com/decoding-radeon-vulkan-versions/

Microsoft:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax
- https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-return-values

Intel:
- https://www.intel.com/content/www/us/en/download/19347/chipset-inf-utility.html
- https://downloadmirror.intel.com/925343/SetupChipset.exe
- https://downloadmirror.intel.com/925343/Release_notes_10.1.20658.8883-Public-Client.pdf
- https://downloadmirror.intel.com/860883/Readme.txt (10.1.20266.8668)
- https://downloadmirror.intel.com/916808/Readme.txt (10.1.20440.8804)
- https://www.intel.com/content/www/us/en/download/785597/intel-arc-graphics-windows.html
- https://downloadmirror.intel.com/930514/gfx_win_101.9150.exe
- https://downloadmirror.intel.com/930514/readme.txt
- https://downloadmirror.intel.com/930514/ReleaseNotes__101.9150_WHQL.pdf
- https://www.intel.com/content/www/us/en/support/articles/000006773.html
- https://www.intel.com/content/www/us/en/support/articles/000091180.html
- https://www.intel.com/content/www/us/en/support/articles/000091369.html
- https://community.intel.com/t5/Graphics/Silent-Installation-Parameters-for-quot-Intel-R-Iris-R-Xe/m-p/1575542
- https://dsadata.intel.com/installer
- winget: `Intel.IntelDriverAndSupportAssistant` (26.2.0.7)
