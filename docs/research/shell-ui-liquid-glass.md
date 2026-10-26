# hateeze — оболочка (.NET 10 + WebView2) и UI в стиле «liquid glass»

> Ключ темы: `shell-ui-liquid-glass` · дата исследования: 2026-10-26 · машина: Win11 Pro for Workstations 23H2 (22631.7652), Ryzen 7 5700X, RTX 4060.
> Документ — единственный источник для разработчиков оболочки и фронтенда. Всё, что не удалось проверить руками, помечено **НЕ ПРОВЕРЕНО** с причиной.
> Сопутствующий файл: [`shell-ui-liquid-glass-demo.html`](./shell-ui-liquid-glass-demo.html) — самодостаточный эталон стекла (CSS/JS), проверен рендером в Chromium 152.

---

## 0. Итоговые решения (TL;DR)

Шкала статусов для решений оболочки (по аналогии со шкалой твиков):
**proven** — подтверждено документацией вендора и/или проверено в этом исследовании; **situational** — работает при условиях (указаны); **experimental** — правдоподобно, но не проверено на нашем стеке, нужен прототип; **avoid** — не делать.

| # | Решение | Статус | Коротко почему |
|---|---|---|---|
| 1 | Хост — **WinForms** (`net10.0-windows`, x64) + контрол `Microsoft.Web.WebView2.WinForms.WebView2` | proven | Проще WPF, нет airspace-проблем WPF, нет нативных DLL WPF (5 шт.) в single-file |
| 2 | NuGet **Microsoft.Web.WebView2 1.0.4314.35** (stable, опубликован 2026-10-23) | proven | Проверено через NuGet API |
| 3 | Окно: `FormBorderStyle.Sizable` + `WM_NCCALCSIZE → 0` (своя «шапка» в HTML), DWM-скругление/рамка/тёмный режим | proven (паттерн Wails/Electron) | Сохраняет тень DWM, Aero Snap, скругление Win11 |
| 4 | Фон окна рисуем **в CSS** (живые градиент-блобы + стекло через `backdrop-filter`) | proven | Единый вид на Win10/Win11; стекло в WebView2 размывает только содержимое страницы, а не рабочий стол |
| 5 | Mica/Acrylic (`DWMWA_SYSTEMBACKDROP_TYPE=38`) под прозрачным WebView2 | experimental | Работает у Wails/Tauri (HWND-хостинг), но в WinForms не проверено; только Win11 22621+ |
| 6 | Перетаскивание: CSS `app-region: drag` + `CoreWebView2Settings.IsNonClientRegionSupportEnabled=true` (stable с SDK 1.0.2420.47) | situational | Нужен свежий Runtime; правый клик по drag-зоне показывает системное меню (by design). Фолбэк — `ReleaseCapture`+`WM_NCLBUTTONDOWN(HTCAPTION)` |
| 7 | Ресайз: невидимые HTML-кромки → мост → `WM_NCLBUTTONDOWN(HTLEFT…HTBOTTOMRIGHT)` через `BeginInvoke` | proven (паттерн Wails v2) | WebView2 перекрывает клиентскую область, родитель не получает `WM_NCHITTEST` |
| 8 | Ассеты UI: **EmbeddedResource → `WebResourceRequested`** на `https://app.hateeze.example/*` | proven | Нет файлов на диске, которые может подменить неповышенный процесс (критично для админ-приложения) |
| 9 | Мост: `chrome.webview.postMessage` ⇄ `WebMessageReceived`/`PostWebMessageAsJson`, протокол `{t,id,m,p}` → `{t,id,ok,r|e}`, события `{t:"evt",ch,d}`, прогресс `{t:"prog"}`, отмена `{t:"cancel"}`; System.Text.Json source-gen | proven | Рекомендация Microsoft — `PostWebMessageAsJson` + JSON-библиотека |
| 10 | Публикация: self-contained single-file, сжатие, **без trimming** | proven | Microsoft: trimming для WinForms отключён в SDK (NETSDK1175) |
| 11 | `WebView2Loader.dll`: встроить как ресурс → извлечь в `%ProgramFiles%\hateeze\bin\<hash>\` → `CoreWebView2Environment.SetLoaderDllFolderPath()` | experimental (не собиралось) | Пакет кладёт loader как Content в `runtimes\win-x64\native\`; извлечение single-file в `%TEMP%` — риск подмены DLL для админ-процесса |
| 12 | UserDataFolder: `%LOCALAPPDATA%\hateeze\WebView2` (явно) | proven | По умолчанию UDF создаётся рядом с exe (`<exe>.WebView2`) |
| 13 | Глобальная клавиша **Ctrl+Alt+Shift+R**: `RegisterHotKey(MOD_CONTROL|MOD_ALT|MOD_SHIFT|MOD_NOREPEAT, 'R')`, двойное нажатие за 3 с | proven (API) | Одиночное нажатие = случайная перезагрузка; двойное — защита |
| 14 | Фронтенд: **Vite 8 + Svelte 5 (runes) + TypeScript 6.0.3**, сборка в `ui/dist` → встраивание в exe | proven (версии проверены) | Scoped CSS, маленький рантайм, авто-регистрация экранов → параллельная работа агентов без конфликтов |
| 15 | Графики: **uPlot 1.6.32** (MIT) для больших графиков + свой canvas-спарклайн для карточек | proven | 51 КБ min / 22 КБ gzip; спарклайн — 40 строк |
| 16 | Иконки **Lucide** (`@lucide/svelte` 1.61.0) | proven | Лицензия **ISC** (не MIT!) + часть иконок MIT (Feather) |
| 17 | Шрифты: Inter Variable + JetBrains Mono Variable (OFL-1.1), только subsets latin+cyrillic, woff2 локально; логотип — SVG-контуры (Unbounded не грузим) | proven | ≈120 КБ шрифтов вместо ≈0,6 МБ |
| 18 | «Жидкая» рефракция (`backdrop-filter: url(#svg)`) | situational | Только Chromium (WebView2 — ок), дорого → 1–2 мелких элемента (кнопка «Применить», индикатор навигации) |
| 19 | Хост WebView2 в **повышенном** процессе | situational / риск | Microsoft рекомендует не повышать хост WebView2; требование проекта «всегда админ» — принимаем с митигациями (§5.8), альтернатива — раздельные процессы |
| 20 | WPF + `AllowsTransparency`, `Form.Opacity`/`TransparencyKey`, regions для скругления | avoid | Слоёные окна ломают WebView2/скругление DWM |

---

## 1. Проверенные версии и окружение

| Что | Значение | Как проверено |
|---|---|---|
| Microsoft.Web.WebView2 (stable) | **1.0.4314.35**, опубликован 2026-10-23 | `api.nuget.org` registration (publish date) |
| Предыдущий stable | 1.0.4258.31 (2026-09-28, для Runtime 154) | Release notes SDK |
| Последний prerelease | 1.0.4312-prerelease (2026-10-12, Runtime 156) | Release notes SDK / NuGet |
| WebView2 Runtime на машине разработчика | **150.0.4078.105** (`HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}\pv`); Edge 151.0.4129.59 | `reg query` |
| .NET SDK / Runtime | SDK 10.0.403; NETCore 10.0.14; WindowsDesktop 10.0.14 | `dotnet --list-sdks/--list-runtimes` |
| Содержимое nupkg 1.0.4314.35 | `runtimes/win-x64/native/WebView2Loader.dll` — **194 912 байт**; `lib_manual/netcoreapp3.0/Microsoft.Web.WebView2.Core.dll` — 703 328 Б; `…WinForms.dll` — 39 264 Б | чтение central directory zip через HTTP Range |
| Evergreen Bootstrapper | `https://go.microsoft.com/fwlink/p/?LinkId=2124703` → **301** → `https://msedge.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/4a41d80d-4493-4463-a466-d0988b97f1c4/MicrosoftEdgeWebview2Setup.exe`, `Content-Length: 1888472`, `application/octet-stream` | `curl -I -L` (2026-10-26) |
| npm: vite / svelte / @sveltejs/vite-plugin-svelte | 8.3.5 / 5.57.2 / 7.3.1 (peer: `vite ^8`, `svelte ^5.46.4`; engines node `^20.19 \|\| ^22.12 \|\| >=24`) | `npm view` |
| npm: typescript | latest **7.0.2** (нативный порт); **6.0.3** — последняя 6.x | `npm view` |
| npm: svelte-check | 4.7.7, peer `typescript ^5 \|\| ^6` → **TS 7 не поддержан** | `npm view` |
| npm: preact / @preact/signals / htm | 11.0.2 (11.0.0 вышел 2026-09-30) / 2.11.3 / 3.1.1 | `npm view` |
| npm: react / react-dom | 19.3.1 | `npm view` |
| npm: uplot | 1.6.32, MIT; `dist/uPlot.iife.min.js` = 51 081 Б (21 991 Б gzip -9); `uPlot.min.css` = 1 857 Б | jsDelivr + `wc -c` |
| npm: lucide / @lucide/svelte | 1.61.0, **ISC**; экспорт `@lucide/svelte/icons/*` есть | package.json |
| npm: @fontsource-variable/* | 5.3.0, OFL-1.1. woff2: Inter cyrillic 18 748 Б, latin 48 256 Б; JetBrains Mono cyrillic 12 108 Б, latin 40 404 Б; Unbounded cyrillic 31 424 Б, latin 50 904 Б; Geist (5.3.0) имеет cyrillic subset | jsDelivr |
| npm: vitest | 5.0.5 (peer vite ^6.4/^7/^8) | `npm view` |
| Chromium для CSS-проверок | Chromium **152** (встроенный браузер Claude/Electron). WebView2 Runtime на машине — 150; поведение CSS-фич ниже не менялось между 150 и 152 (**допущение**) | `navigator.userAgentData` |

---

## 2. Хост и окно

### 2.1 WinForms vs WPF — выбор WinForms

| Критерий | WinForms + WebView2 | WPF + WebView2 |
|---|---|---|
| Контрол | `WebView2` (HWND-хостинг, `Dock=Fill`) | `WebView2` = `HwndHost` → **airspace**: WPF-контент поверх WebView2 невозможен; `AllowsTransparency=True` с HwndHost не работает |
| Обход airspace | не нужен (весь UI в HTML) | `WebView2CompositionControl` (stable с SDK 1.0.2957.106, Runtime ≥132) — рендер через D3D11 capture-сессию → дополнительная GPU-работа и задержка кадра (**бенчмарков нет**) |
| Single-file | WinForms-часть WindowsDesktop **не содержит нативных DLL** (проверено: все 5 нативных DLL WindowsDesktop.App 10.0.14 — WPF: `D3DCompiler_47_cor3`, `PenImc_cor3`, `PresentationNative_cor3`, `vcruntime140_cor3`, `wpfgfx_cor3`) | +5 нативных DLL (~8 МБ) → извлечение во временную папку |
| Trimming | отключён SDK | отключён SDK |
| Сложность | минимальная | выше (XAML, Dispatcher, airspace) |

**Вывод:** WinForms. Форма — только «рамка» и хост для WebView2; никаких WinForms-контролов поверх.

### 2.2 Архитектура окна без системной рамки

Выбираем паттерн, который используют Wails v2, Electron и Windows Terminal: **оставить стили `WS_CAPTION|WS_THICKFRAME`** (WinForms `FormBorderStyle.Sizable`) и «съесть» неклиентскую область через `WM_NCCALCSIZE`. Плюсы против `FormBorderStyle.None`:

- тень DWM и скругление Win11 сохраняются автоматически (у «рамочных» окон Win11 скругляет по политике);
- работают Aero Snap (Win+стрелки, перетаскивание к краю), анимации свернуть/развернуть, `Win+Z`;
- корректное поведение при развороте/мультимониторе (с поправкой ниже).

Правила:

1. `WM_NCCALCSIZE` с `wParam != 0` → вернуть `0` (клиентская область = всё окно).
2. Если окно развёрнуто — подставить в `rgrc[0]` **рабочую область монитора** (`MONITORINFO.rcWork`), иначе окно вылезает за экран на толщину рамки (≈8 px при 100%). Монитор брать через `MonitorFromRect(rgrc[0])` — у Wails зафиксировано, что `MonitorFromWindow` во время разворота на мультимониторе может вернуть не тот монитор.
3. `DwmExtendFrameIntoClientArea` с полями `{1,1,1,1}` — сохраняет «аэро»-тень у окна без видимой рамки (паттерн Wails: «-1 показывает нерабочие кнопки caption при прозрачности, 0 — без тени, 1 — тень и скругление без кнопок»).
4. **Не использовать** `Size`→`ClientSize`: WinForms вычисляет размер окна через `AdjustWindowRectEx`, а у нас клиентская область = окно → задавайте `Size`.
5. **Не трогать во время работы** свойства, которые пересоздают HWND: `ShowInTaskbar`, `FormBorderStyle`, `RightToLeft`, `Opacity`, `TransparencyKey` (последние два ещё и делают окно `WS_EX_LAYERED` → WebView2 и скругление DWM ломаются). Все DWM-атрибуты и hotkey применять в `OnHandleCreated` (сработает и при пересоздании), снимать в `OnHandleDestroyed`.
6. Скрытая панель задач (auto-hide): развёрнутое окно на весь `rcWork` перекрывает 1-px триггер панели — **НЕ ПРОВЕРЕНО**, при жалобах оставить 1 px снизу (как Windows Terminal).

#### 2.2.1 DWM-атрибуты (точные значения, `dwmapi.h`)

| Атрибут | Значение | Тип | Мин. сборка | Для чего |
|---|---|---|---|---|
| `DWMWA_USE_IMMERSIVE_DARK_MODE` | **20** | BOOL | Win11 22000 по докам; фактически работает с Win10 20H1 (18985+, паттерн Wails); до 20H1 было 19 | тёмное системное меню/рамка |
| `DWMWA_WINDOW_CORNER_PREFERENCE` | **33** | `DWM_WINDOW_CORNER_PREFERENCE`: `DEFAULT=0, DONOTROUND=1, ROUND=2, ROUNDSMALL=3` | 22000 | скругление |
| `DWMWA_BORDER_COLOR` | **34** | COLORREF `0x00BBGGRR`; `DWMWA_COLOR_NONE=0xFFFFFFFE`, `DWMWA_COLOR_DEFAULT=0xFFFFFFFF` | 22000 | 1-px рамка в тон UI |
| `DWMWA_CAPTION_COLOR` | 35 | COLORREF | 22000 | не нужен (caption скрыт) |
| `DWMWA_TEXT_COLOR` | 36 | COLORREF | 22000 | не нужен |
| `DWMWA_SYSTEMBACKDROP_TYPE` | **38** | `DWM_SYSTEMBACKDROP_TYPE`: `AUTO=0, NONE=1, MAINWINDOW=2 (Mica), TRANSIENTWINDOW=3 (Acrylic), TABBEDWINDOW=4 (Mica Alt)` | **22621** | системный фон (см. §2.3) |
| `DWMWA_REDIRECTIONBITMAP_ALPHA` | 39 | BOOL | 26100 | не используем |

Скругление **невозможно** (даже с opt-in), если окно использует per-pixel alpha layering (`WS_EX_LAYERED` + `UpdateLayeredWindow`) или window regions (`SetWindowRgn`). Не скругляются по дизайну: развёрнутые, прикреплённые (snapped), в VM/AVD/WDAG. На Win10 скругления нет — окно прямоугольное (это нормально).

В .NET 10 у WinForms есть `Form.FormCornerPreference` (в .NET 9 был экспериментальным, WFO5001; в 10.0 — без атрибута Experimental по reference-доке) — можно использовать вместо P/Invoke для п.33; для 20/34/38 и `DwmExtendFrameIntoClientArea` P/Invoke всё равно нужен.

#### 2.2.2 Код: P/Invoke (LibraryImport, source-generated)

```csharp
// src/Hateeze.App/Native/Win32.cs
// Требует <AllowUnsafeBlocks>true</AllowUnsafeBlocks> (иначе SYSLIB1062 для LibraryImport).
using System.Runtime.InteropServices;

namespace Hateeze.App.Native;

internal static partial class Win32
{
    public const int WM_ACTIVATE = 0x0006, WM_GETMINMAXINFO = 0x0024, WM_NCCALCSIZE = 0x0083,
        WM_NCHITTEST = 0x0084, WM_NCLBUTTONDOWN = 0x00A1, WM_SYSCOMMAND = 0x0112,
        WM_HOTKEY = 0x0312, WM_DPICHANGED = 0x02E0;

    public const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12,
        HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    public const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004,
        MOD_WIN = 0x0008, MOD_NOREPEAT = 0x4000;
    public const uint VK_R = 0x52;
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct NCCALCSIZE_PARAMS { public RECT Rgrc0, Rgrc1, Rgrc2; public nint Lppos; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int CbSize; public RECT RcMonitor, RcWork; public uint DwFlags; }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hWnd, int id);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromRect(ref RECT lprc, uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmExtendFrameIntoClientArea(nint hwnd, ref MARGINS margins);
}

internal static class Dwm
{
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33,
        DWMWA_BORDER_COLOR = 34, DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMWCP_ROUND = 2;
    public const int DWMSBT_NONE = 1, DWMSBT_MAINWINDOW = 2, DWMSBT_TRANSIENTWINDOW = 3, DWMSBT_TABBEDWINDOW = 4;
}
```

#### 2.2.3 Код: оформление окна

```csharp
// src/Hateeze.App/Shell/WindowChrome.cs
using System.Runtime.InteropServices;
using Hateeze.App.Native;

namespace Hateeze.App.Shell;

internal static class WindowChrome
{
    /// Вызывать из OnHandleCreated (срабатывает и при пересоздании HWND).
    public static void Apply(nint hwnd)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18985))
        {
            int dark = 1;
            Win32.DwmSetWindowAttribute(hwnd, Dwm.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            int round = Dwm.DWMWCP_ROUND;
            Win32.DwmSetWindowAttribute(hwnd, Dwm.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            int border = ColorRef(0x2A, 0x28, 0x40);            // тонкая рамка в тон UI (#2A2840)
            Win32.DwmSetWindowAttribute(hwnd, Dwm.DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
        var m = new Win32.MARGINS { Left = 1, Right = 1, Top = 1, Bottom = 1 }; // тень DWM без видимой рамки
        Win32.DwmExtendFrameIntoClientArea(hwnd, ref m);
        // HRESULT'ы намеренно игнорируются: на Win10 атрибуты 33/34 вернут E_INVALIDARG — это нормально.
    }

    /// WM_NCCALCSIZE для развёрнутого окна: клиентская область = рабочая область монитора.
    public static void FitMaximizedToWorkArea(nint lParam)
    {
        var p = Marshal.PtrToStructure<Win32.NCCALCSIZE_PARAMS>(lParam);
        var mon = Win32.MonitorFromRect(ref p.Rgrc0, Win32.MONITOR_DEFAULTTONEAREST);
        var mi = new Win32.MONITORINFO { CbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (mon != 0 && Win32.GetMonitorInfo(mon, ref mi))
        {
            p.Rgrc0 = mi.RcWork;
            Marshal.StructureToPtr(p, lParam, fDeleteOld: false);
        }
    }

    /// Начать системный перенос/ресайз. ВАЖНО: вызывать через BeginInvoke, НЕ внутри обработчика
    /// WebMessageReceived — SendMessage(WM_NCLBUTTONDOWN) запускает вложенный модальный цикл,
    /// а реентерабельность в обработчиках WebView2 не поддерживается (Threading model, «Reentrancy»).
    public static void BeginMoveOrSize(nint hwnd, int hitTest)
    {
        Win32.ReleaseCapture();
        Win32.SendMessage(hwnd, Win32.WM_NCLBUTTONDOWN, hitTest, 0);
    }

    static int ColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);
}
```

#### 2.2.4 Код: главная форма (каркас)

```csharp
// src/Hateeze.App/Shell/MainForm.cs
using Hateeze.App.Native;
using Microsoft.Web.WebView2.WinForms;

namespace Hateeze.App.Shell;

internal sealed partial class MainForm : Form
{
    public static readonly Color Background = Color.FromArgb(6, 7, 11);   // == CSS --bg-0
    static readonly Size MinLogical = new(1024, 640);
    static readonly Size DefaultLogical = new(1280, 800);

    readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Background };
    PanicHotkey? _hotkey;

    public MainForm()
    {
        Text = "hateeze";
        FormBorderStyle = FormBorderStyle.Sizable;   // WS_CAPTION|WS_THICKFRAME: тень, Snap, скругление
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;                      // против белой вспышки до первого кадра WebView2
        Size = LogicalToDeviceUnits(DefaultLogical); // НЕ ClientSize (см. §2.2 п.4)
        MinimumSize = LogicalToDeviceUnits(MinLogical);
        DpiChanged += (_, _) => MinimumSize = LogicalToDeviceUnits(MinLogical);
        Controls.Add(_web);
        Load += async (_, _) => await InitWebViewAsync();   // §3, §4
        Resize += (_, _) => OnVisibilityMaybeChanged();      // §2.7
        Activated += (_, _) => OnVisibilityMaybeChanged();
        Deactivate += (_, _) => OnVisibilityMaybeChanged();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowChrome.Apply(Handle);
        _hotkey = new PanicHotkey(Handle);          // §6
        _hotkey.TryRegister();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _hotkey?.Dispose();
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Win32.WM_NCCALCSIZE when m.WParam != 0:
                if (WindowState == FormWindowState.Maximized)
                    WindowChrome.FitMaximizedToWorkArea(m.LParam);
                m.Result = 0;                       // вся площадь окна — клиентская
                return;
            case Win32.WM_HOTKEY:
                _hotkey?.OnWmHotkey((int)m.WParam);
                return;
        }
        base.WndProc(ref m);
    }
}
```

Минимальный размер — через `Form.MinimumSize` в device-пикселях, пересчитываемый в `DpiChanged` (не перехватывать `WM_GETMINMAXINFO` вручную: базовый `Form.WndProc` его сам обрабатывает и перезапишет значения).

### 2.3 Mica/Acrylic под прозрачным WebView2 — работает ли?

Факты:

- `CoreWebView2Controller.DefaultBackgroundColor` поддерживает **только альфу 0 или 255**: «It will fail for colors with alpha values that don't equal 0 or 255». При альфе 0 WebView2 показывает содержимое хост-окна под собой (на Win7 прозрачность не поддерживается).
- Официальный путь Microsoft для «WebView2 + Mica» — Windows App SDK `MicaController` + WinRT Compositor (пример `WindowsAppSDK-Samples/Samples/Mica/cpp-WebView2`), а не DWM-атрибут. Для WinForms-приложения это лишняя зависимость (Windows App Runtime).
- Работающие в продакшене реализации на **HWND-хостинге** WebView2 + `DWMWA_SYSTEMBACKDROP_TYPE`:
  - **Wails v2** (Go, WebView2 через `CoreWebView2Controller`): для полупрозрачного окна ставит `WS_EX_NOREDIRECTIONBITMAP` (окно без GDI-поверхности — видно только DirectComposition-содержимое WebView2), `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)` при сборке ≥ 22621, и прозрачный фон WebView2; CSS-фон страницы — `rgba(...,0)`.
  - **Tauri** (`window-vibrancy`): `apply_mica` = `DWMWA_SYSTEMBACKDROP_TYPE=DWMSBT_MAINWINDOW` при сборке ≥ 22523, иначе недокументированный `DWMWA_MICA_EFFECT=1029` на 22000; Acrylic на Win10 — через недокументированный `SetWindowCompositionAttribute` с известными **лагами при перетаскивании/ресайзе** (Win10 1903+, Win11 22000).

Ограничения и выводы для нас:

1. На **Win10 22H2 Mica нет**, Acrylic только через недокументированный API с лагами → для Win10 всё равно нужен CSS-фон.
2. `backdrop-filter` внутри страницы размывает **только содержимое страницы**, а не рабочий стол/Mica. При прозрачном WebView2 «стеклянные» панели над Mica будут просто полупрозрачными (Mica сама по себе уже размыта — визуально приемлемо, но «стекло» перестаёт «читаться» на однородной Mica).
3. В WinForms между формой и WebView2 есть дополнительный дочерний HWND (сам контрол). Работает ли схема `WS_EX_NOREDIRECTIONBITMAP` + прозрачный WebView2 + Mica на WinForms — **НЕ ПРОВЕРЕНО** (сборка/запуск запрещены правилами исследования).
4. Mica — «opaque material… falls back to a neutral color when the app is inactive» (Microsoft): при потере фокуса фон станет серым — для приложения, которое большую часть времени в фоне за игрой, эффект малозаметен.

**Решение:** v1 — **фон целиком в CSS** (одинаково на Win10/11, стекло «читается» за счёт живых блобов). Mica — опциональный эксперимент за флагом `Ui.SystemBackdrop` (только Win11 ≥ 22621). Рецепт спайка:

```csharp
// ЭКСПЕРИМЕНТ (НЕ ПРОВЕРЕНО на WinForms). Включать только при Environment.OSVersion.Version.Build >= 22621.
protected override CreateParams CreateParams
{
    get
    {
        var cp = base.CreateParams;
        if (Experiments.SystemBackdrop) cp.ExStyle |= 0x00200000; // WS_EX_NOREDIRECTIONBITMAP
        return cp;
    }
}
// после создания HWND:
int backdrop = Dwm.DWMSBT_MAINWINDOW;   // или TABBEDWINDOW (Mica Alt) / TRANSIENTWINDOW (Acrylic)
Win32.DwmSetWindowAttribute(Handle, Dwm.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
// WebView2: controllerOptions.DefaultBackgroundColor = Color.Transparent; в CSS html,body{background:transparent}
```

Чек-лист спайка: (а) видна ли Mica под прозрачными участками страницы; (б) нет ли чёрного фона у WinForms-контрола; (в) нет ли мерцания при ресайзе; (г) что видно на Win10 (должен включаться CSS-фон); (д) GPU-нагрузка в простое (Диспетчер задач → «GPU» процесса `msedgewebview2.exe`).

### 2.4 Перетаскивание окна из HTML

**Вариант A (основной): CSS `app-region`.**

- `CoreWebView2Settings.IsNonClientRegionSupportEnabled = true` — **stable с SDK 1.0.2420.47** (Win32 `ICoreWebView2Settings9`), по умолчанию `false`, **вступает в силу после следующей навигации** → ставить до `Navigate()`.
- Поведение (из reference): элементы с `app-region: drag` ведут себя как заголовок окна — перетаскивание, **правый клик показывает системное меню**, **двойной клик разворачивает/восстанавливает**. Microsoft на жалобу Wails про правый клик ответил «working as intended» (WebView2Feedback #3565).
- Внутри drag-зоны все интерактивные элементы (кнопки окна, поиск) — `app-region: no-drag`.
- Tauri/wry включили эту настройку по умолчанию для HWND-хостинга (PR #1262, 2024-05) — косвенное подтверждение работы без composition-хостинга.
- В Chromium 152 `CSS.supports('app-region','drag') === true` и `-webkit-app-region` тоже — **проверено**. Работа именно внутри WinForms-контрола (HWND → дочерний HWND → окно Chromium) — **НЕ ПРОВЕРЕНО**.
- На старом Runtime без `ICoreWebView2Settings9` сеттер бросит исключение (QueryInterface fail) → оборачивать в `try/catch` и включать вариант B.

```css
.titlebar { app-region: drag; -webkit-app-region: drag; height: 40px; }
.titlebar button, .titlebar input { app-region: no-drag; -webkit-app-region: no-drag; }
```

**Вариант B (фолбэк, всегда работает): сообщение в хост.**

```ts
// ui/src/lib/ui/TitleBar.svelte (фрагмент)
function onPointerDown(e: PointerEvent) {
  if (e.button !== 0 || (e.target as HTMLElement).closest('[data-no-drag]')) return;
  call('window.startDrag');           // хост: BeginInvoke → ReleaseCapture + WM_NCLBUTTONDOWN(HTCAPTION)
}
function onDblClick() { call('window.toggleMaximize'); }
```

Перенос через `HTCAPTION` даёт стандартный модальный цикл перемещения со Snap. Хостовая часть — §4.6 (`window.startDrag`).

### 2.5 Ресайз окна без рамки

`WebView2` закрывает всю клиентскую область, поэтому `WM_NCHITTEST` получает окно Chromium (другой процесс), а не наша форма — классический `WM_NCHITTEST`-ресайз в родителе **не сработает**. Решения:

1. **(Рекомендуется, паттерн Wails v2)** 8 невидимых HTML-зон по краям (6 px) и углам (12×12 px) с CSS-курсорами; `pointerdown` → `call('window.startResize', {edge})` → хост `BeginInvoke(() => BeginMoveOrSize(hwnd, HTxxx))`. Зоны скрывать, когда окно развёрнуто (событие `window.state`).
2. (Альтернатива) Отступ WebView2 на 4–6 px от краёв формы (`Padding`), а в `WM_NCHITTEST` формы возвращать `HTLEFT…` — нативно, но по краю виден фон формы; приемлемо при `BackColor` = цвет фона UI.

```svelte
<!-- ui/src/lib/ui/ResizeEdges.svelte -->
<script lang="ts">
  import { call } from '$lib/bridge/client';
  import { windowState } from '$lib/state/window.svelte';
  const edges = [
    ['Top','n'],['Bottom','s'],['Left','w'],['Right','e'],
    ['TopLeft','nw'],['TopRight','ne'],['BottomLeft','sw'],['BottomRight','se'],
  ] as const;
</script>
{#if !windowState.maximized}
  {#each edges as [edge, dir]}
    <div class="edge {dir}" aria-hidden="true"
         onpointerdown={(e) => { if (e.button === 0) call('window.startResize', { edge }); }}></div>
  {/each}
{/if}
<style>
  .edge { position: fixed; z-index: 9999; app-region: no-drag; -webkit-app-region: no-drag; }
  .n, .s { left: 12px; right: 12px; height: 6px; cursor: ns-resize; }
  .n { top: 0; } .s { bottom: 0; }
  .w, .e { top: 12px; bottom: 12px; width: 6px; cursor: ew-resize; }
  .w { left: 0; } .e { right: 0; }
  .nw, .ne, .sw, .se { width: 12px; height: 12px; }
  .nw { left: 0; top: 0; cursor: nwse-resize; } .se { right: 0; bottom: 0; cursor: nwse-resize; }
  .ne { right: 0; top: 0; cursor: nesw-resize; } .sw { left: 0; bottom: 0; cursor: nesw-resize; }
</style>
```

Ограничение: Snap Layouts (всплывающая сетка Win11 при наведении на «развернуть») требует, чтобы окно вернуло `HTMAXBUTTON` в `WM_NCHITTEST` — с HTML-кнопкой при HWND-хостинге это невозможно (enum `CoreWebView2NonClientRegionKind.Maximize=9` относится к composition-хостингу). `Win+Z` продолжает работать.

### 2.6 DPI

- `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` в csproj (по умолчанию в .NET — `SystemAware`!) → применяется в сгенерированном `ApplicationConfiguration.Initialize()`.
- В `app.manifest` DPI-настройки **не дублировать** (для .NET Framework Microsoft прямо пишет, что манифестный способ «no longer recommended»; в .NET источник истины — csproj/`Application.SetHighDpiMode`).
- WebView2 сам следит за масштабом монитора; CSS работает в DIP.
- Размеры формы — через `LogicalToDeviceUnits(...)`.

### 2.7 Белая вспышка, видимость и фон

- `CoreWebView2ControllerOptions.DefaultBackgroundColor` (передаётся в `EnsureCoreWebView2Async(env, controllerOptions)`) — инициализирует цвет **до** первой отрисовки и убирает белую вспышку.
- Переменная `WEBVIEW2_DEFAULT_BACKGROUND_COLOR` в **повышенном** процессе ненадёжна: Microsoft — «When the host process is running elevated: `WEBVIEW2_*` environment variable overrides (flags) are ignored». Явно ли это касается именно `…DEFAULT_BACKGROUND_COLOR` — в доке не уточнено → **не полагаться**.
- `Form.BackColor` = тот же цвет, что `--bg-0` в CSS.
- Когда окно свёрнуто/скрыто в трей: `_web.Visible = false` (→ `IsVisible=false`, `document.visibilityState='hidden'`), затем `CoreWebView2.TrySuspendAsync()` (требует `IsVisible == false`, иначе `COMException` с `ERROR_INVALID_STATE`), `CoreWebView2.MemoryUsageTargetLevel = Low`. При показе — `Visible = true`, `Resume()`. Стрим метрик в скрытое окно не отправлять (§4.5).

```csharp
// MainForm (partial): поля _bridge (BridgeHost), _pump (MetricsPump), _assets (WebAssetServer) объявлены в §3.5/§4
async void OnVisibilityMaybeChanged()
{
    var core = _web.CoreWebView2;
    if (core is null) return;
    bool visible = Visible && WindowState != FormWindowState.Minimized;
    if (_pump is not null) _pump.WindowVisible = visible;
    _bridge?.Emit("window.state",
        new WindowStateDto(WindowState == FormWindowState.Maximized, Focused || ContainsFocus, visible, DeviceDpi),
        BridgeJson.Default.WindowStateDto);                    // JS: пауза анимаций, скрытие ресайз-кромок
    try
    {
        if (!visible && !core.IsSuspended)
        {
            _web.Visible = false;                              // IsVisible=false — обязательное условие TrySuspendAsync
            core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            await core.TrySuspendAsync();
        }
        else if (visible && !_web.Visible)
        {
            _web.Visible = true;
            core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
            if (core.IsSuspended) core.Resume();
        }
    }
    catch (System.Runtime.InteropServices.COMException) { /* гонка состояний — не критично */ }
}
```

---

## 3. Ассеты UI и безопасность контента

### 3.1 Варианты загрузки (по документации Microsoft «Using local content»)

| Способ | Origin/secure context | Где резолвится | Плюсы | Минусы для нас |
|---|---|---|---|---|
| `file://` | уникальный origin на путь, **не** secure | процесс WebView2 | быстро | файлы на диске; нет secure context |
| `NavigateToString` | `about:blank`, origin `null` | — | — | лимит 2 МБ, нельзя подгружать JS/CSS |
| `SetVirtualHostNameToFolderMapping` | https-origin, secure | процесс WebView2 (быстрее) | просто | **нужна папка на диске** (распаковка), медиа «can be very slow», source maps не грузятся |
| **`WebResourceRequested`** | https-origin, secure | **UI-поток хоста** (медленнее: межпроцессный вызов на каждый запрос) | ресурсы из памяти exe, свои заголовки (CSP), динамический контент | нужно писать код; source maps не грузятся |

**Рекомендация: EmbeddedResource → `WebResourceRequested`.** Ключевой довод — безопасность повышенного процесса: при распаковке во `%TEMP%`/`%LOCALAPPDATA%` любой **неповышенный** процесс того же пользователя может подменить `index.js` и получить доступ к мосту, который применяет твики с правами администратора (эскалация привилегий). Производительность не страдает: ~20–40 файлов, ~1 МБ, грузятся один раз при старте.

### 3.2 Имя хоста

- Брать зарезервированный RFC 6761 TLD: **`https://app.hateeze.example/`**. Документация `SetVirtualHostNameToFolderMapping`: «RFC 6761 has reserved several special-use domain names that are guaranteed to not be used by real sites (for example, `.example`, `.test`, and `.invalid`)»; «using `.local` … can cause a delay during navigations».
- `app.hateeze` (без TLD) технически работает, но `hateeze` может однажды стать реальным gTLD — нет причин рисковать.
- Только `https` (secure context, а `http`-страница не получит часть API).

### 3.3 Код: сервер встроенных ассетов

```csharp
// src/Hateeze.App/Shell/WebAssetServer.cs
using System.Reflection;
using Microsoft.Web.WebView2.Core;

namespace Hateeze.App.Shell;

internal sealed class WebAssetServer
{
    public const string Origin = "https://app.hateeze.example";
    const string ResourcePrefix = "ui/";   // LogicalName в csproj: ui/<путь внутри dist>

    // Строгий CSP. 'unsafe-inline' для стилей нужен: Svelte-шаблоны клонируют HTML со style-атрибутами,
    // а inline style-атрибуты подпадают под style-src. Скрипты — только свои файлы.
    const string Csp =
        "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; worker-src 'self' blob:; " +
        "base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'";

    readonly record struct Asset(byte[] Data, string ContentType);
    readonly Dictionary<string, Asset> _assets = new(StringComparer.OrdinalIgnoreCase);

    public WebAssetServer()
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;
            using var s = asm.GetManifestResourceStream(name)!;
            var bytes = new byte[s.Length];
            s.ReadExactly(bytes);
            var path = "/" + name[ResourcePrefix.Length..].Replace('\\', '/'); // %(RecursiveDir) даёт '\'
            _assets[path] = new Asset(bytes, MimeOf(path));
        }
    }

    public void Attach(CoreWebView2 core)
    {
        core.AddWebResourceRequestedFilter(Origin + "/*",
            CoreWebView2WebResourceContext.All,
            CoreWebView2WebResourceRequestSourceKinds.Document);   // перегрузка без SourceKinds — deprecated
        core.WebResourceRequested += (_, e) => Serve(core.Environment, e);
    }

    void Serve(CoreWebView2Environment env, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);
        var path = uri.AbsolutePath == "/" ? "/index.html" : Uri.UnescapeDataString(uri.AbsolutePath);

        if (!string.Equals(e.Request.Method, "GET", StringComparison.OrdinalIgnoreCase)
            || !_assets.TryGetValue(path, out var a))
        {
            // hash-роутинг (#/screen) → неизвестных путей быть не должно
            e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "");
            return;
        }
        // MemoryStream поверх byte[]: управляемый поток, утечки как в WebView2Feedback#2513 (FileStream) нет.
        var resp = env.CreateWebResourceResponse(new MemoryStream(a.Data, writable: false), 200, "OK", "");
        resp.Headers.AppendHeader("Content-Type", a.ContentType);
        resp.Headers.AppendHeader("Cache-Control", "no-store");
        resp.Headers.AppendHeader("X-Content-Type-Options", "nosniff");
        if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            resp.Headers.AppendHeader("Content-Security-Policy", Csp);
        e.Response = resp;
    }

    static string MimeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".woff2" => "font/woff2",
        ".wasm" => "application/wasm",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };
}
```

Заметки:
- `AddWebResourceRequestedFilter(string, CoreWebView2WebResourceContext)` помечен в доке как **deprecated** («does not behave as expected for iframes») — использовать перегрузку с `CoreWebView2WebResourceRequestSourceKinds`.
- Фильтр — glob по всему URI после нормализации (`*` — 0+ символов, `?` — один).
- Заголовки в `CreateWebResourceResponse` — «raw response header string delimited by newline»; надёжнее пустая строка + `resp.Headers.AppendHeader(...)`.
- Source maps через `WebResourceRequested` WebView2 **не загружает** — в Debug используем Vite dev-server (§9.6) или inline source maps.

### 3.4 Блокировка навигации, окон, разрешений, загрузок

```csharp
static bool IsAppUri(string uri) =>
    uri.StartsWith(WebAssetServer.Origin + "/", StringComparison.OrdinalIgnoreCase)
#if DEBUG
    || uri.StartsWith("http://localhost:5173/", StringComparison.OrdinalIgnoreCase)
#endif
    ;

core.NavigationStarting += (_, e) =>
{
    if (IsAppUri(e.Uri)) return;
    e.Cancel = true;
    if (e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ExternalLauncher.Open(e.Uri);
};
core.FrameNavigationStarting += (_, e) => { if (!IsAppUri(e.Uri)) e.Cancel = true; };
core.NewWindowRequested += (_, e) =>
{
    e.Handled = true;                                   // никаких окон WebView2
    if (e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ExternalLauncher.Open(e.Uri);
};
core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
core.DownloadStarting += (_, e) => e.Cancel = true;     // загрузки — только через C# (HttpClient)
```

**Внешние ссылки из повышенного процесса.** `Process.Start(url)` с `UseShellExecute=true` запустит браузер **с правами администратора** — плохо и небезопасно. Варианты:
- MVP: `Process.Start(new ProcessStartInfo("explorer.exe", $"\"{url}\"") { UseShellExecute = false })` — новый `explorer.exe` передаёт запрос уже работающей (неповышенной) оболочке. Широко используемый приём, **НЕ ПРОВЕРЕНО** на всех конфигурациях (например, при «Запускать окна с папками в отдельном процессе»).
```csharp
// src/Hateeze.App/Shell/ExternalLauncher.cs — MVP
internal static class ExternalLauncher
{
    public static void Open(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return; // только https
        // explorer.exe передаёт URL уже запущенной неповышенной оболочке → браузер стартует без прав администратора
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{u.AbsoluteUri}\"")
            { UseShellExecute = false });
    }
}
```
- Надёжно: приём Raymond Chen — через `IShellWindows.FindWindowSW(SWC_DESKTOP)` → `IShellBrowser` → `IShellView.GetItemObject(SVGIO_BACKGROUND)` → `IShellFolderViewDual.Application` → `IShellDispatch2.ShellExecute(url)`; процесс запускается на уровне целостности Explorer (Old New Thing, 2013-11-18; так же сделано в установщике Firefox, Mozilla bug 1448804).

### 3.5 Настройки WebView2 (Release/Debug)

```csharp
async Task InitWebViewAsync()
{
    var udf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hateeze", "WebView2");
    Directory.CreateDirectory(udf);

    var envOptions = new CoreWebView2EnvironmentOptions(additionalBrowserArguments: null, language: "ru-RU")
    {
        AreBrowserExtensionsEnabled = false,
        EnableTrackingPrevention = false,          // контент только локальный
    };
    TrySet(() => envOptions.ScrollBarStyle = CoreWebView2ScrollbarStyle.FluentOverlay); // оверлейные скроллбары; имя enum сверить в IntelliSense

    var env = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: udf, options: envOptions);
    var ctl = env.CreateCoreWebView2ControllerOptions();
    TrySet(() => ctl.DefaultBackgroundColor = Background);   // против белой вспышки (ControllerOptions3)
    await _web.EnsureCoreWebView2Async(env, ctl);

    var core = _web.CoreWebView2;
    var s = core.Settings;
#if DEBUG
    s.AreDevToolsEnabled = true;
    s.AreDefaultContextMenusEnabled = true;
    s.AreBrowserAcceleratorKeysEnabled = true;     // F5/F12 в отладке
#else
    s.AreDevToolsEnabled = false;
    s.AreDefaultContextMenusEnabled = false;
    s.AreBrowserAcceleratorKeysEnabled = false;    // Ctrl+F/P/R, F5, Ctrl±, F12, Ctrl+Shift+C/I и т.п.; Ctrl+C/V/A/Z остаются
    s.AreDefaultScriptDialogsEnabled = false;      // alert/confirm запрещены — только свои модалки
#endif
    s.IsStatusBarEnabled = false;
    s.IsZoomControlEnabled = false;
    s.IsPinchZoomEnabled = false;
    s.IsSwipeNavigationEnabled = false;
    s.IsGeneralAutofillEnabled = false;
    s.IsPasswordAutosaveEnabled = false;
    s.AreHostObjectsAllowed = false;               // используем только web messages
    s.IsBuiltInErrorPageEnabled = false;
    TrySet(() => s.IsReputationCheckingRequired = false);     // SmartScreen для локального контента не нужен
    TrySet(() => s.IsNonClientRegionSupportEnabled = true);   // app-region: drag (до Navigate!)
    core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;

    _assets.Attach(core);
    _bridge.Attach(core);                          // §4
    // + обработчики §3.4, ProcessFailed (§3.6)

#if DEBUG
    var dev = Environment.GetEnvironmentVariable("HATEEZE_UI_DEV");  // напр. http://localhost:5173/
    core.Navigate(string.IsNullOrEmpty(dev) ? WebAssetServer.Origin + "/index.html" : dev);
#else
    core.Navigate(WebAssetServer.Origin + "/index.html");
#endif
}

static void TrySet(Action a) { try { a(); } catch (Exception ex) when (ex is NotImplementedException or InvalidCastException or System.Runtime.InteropServices.COMException or ArgumentException) { /* старый Runtime */ } }
```

`AreBrowserAcceleratorKeysEnabled=false` по доке отключает «Ctrl-F and F3 … Ctrl-P … Ctrl-R and F5 … Ctrl-Plus/Minus … Ctrl-Shift-C and F12…», но **не** «Home, End, Page Up/Down, Ctrl-X/C/V/A/Z».

### 3.6 Устойчивость

- `CoreWebView2.ProcessFailed`: `RenderProcessExited`/`RenderProcessUnresponsive` → `core.Reload()` и тост «Интерфейс перезапущен после сбоя. Применённые твики не затронуты.»; `BrowserProcessExited` → пересоздать контрол (новый `WebView2`, тот же UDF) или показать нативную ошибку.
- `CoreWebView2Environment.NewBrowserVersionAvailable` → в UI баннер «Обновился компонент WebView2 — перезапустите hateeze, чтобы применить обновление безопасности».
- Состояние, критичное для безопасности/отката (снимки, применённые твики), **не хранить в `localStorage`/IndexedDB**: UDF лежит в профиле пользователя и доступен неповышенным процессам. В браузерном хранилище — только UI-настройки (свёрнутые панели, выбранная вкладка).

---

## 4. Мост JS ↔ C#

### 4.1 Протокол

Все сообщения — JSON-объекты. JS → C# через `window.chrome.webview.postMessage(obj)`; C# → JS через `CoreWebView2.PostWebMessageAsJson(json)` (в JS `event.data` — уже объект).

| Направление | Тип | Форма | Пример |
|---|---|---|---|
| JS→C# | запрос | `{t:"req", id:number, m:string, p?:any}` | `{"t":"req","id":7,"m":"tweaks.apply","p":{"ids":["power.ultimate"]}}` |
| JS→C# | отмена | `{t:"cancel", id:number}` | `{"t":"cancel","id":7}` |
| C#→JS | успех | `{t:"res", id, ok:true, r?:any}` | `{"t":"res","id":7,"ok":true,"r":{"applied":1}}` |
| C#→JS | ошибка | `{t:"res", id, ok:false, e:{code, message}}` | `{"t":"res","id":7,"ok":false,"e":{"code":"E_CANCELLED","message":"Операция отменена"}}` |
| C#→JS | прогресс | `{t:"prog", id, d:any}` | `{"t":"prog","id":9,"d":{"stage":"download","received":1048576,"total":734003200,"pct":0.14,"speedBps":5242880}}` |
| C#→JS | событие | `{t:"evt", ch:string, d:any}` | `{"t":"evt","ch":"metrics.sample","d":{"t":1730000000123,"cpuPct":23.1,…}}` |

Правила:
- `id` — монотонный счётчик на стороне JS (`number`, в C# `long`).
- Имена методов `domain.action` (`window.*`, `app.*`, `metrics.*`, `tweaks.*`, `snapshot.*`, `driver.*`). Нейминг JSON — camelCase (`JsonSerializerDefaults.Web`); enum — строки с именами как в C# (`"TopLeft"`).
- Коды ошибок (стабильный контракт, текст `message` — на русском для показа пользователю):

| code | Когда |
|---|---|
| `E_BAD_REQUEST` | битый конверт (нет `t`/`m`) |
| `E_NO_METHOD` | неизвестный метод |
| `E_BAD_PARAMS` | параметры не десериализуются/не проходят валидацию |
| `E_CANCELLED` | отменено пользователем (`cancel`) или при выходе |
| `E_TIMEOUT` | таймаут операции |
| `E_ACCESS` | `UnauthorizedAccessException` и т.п. |
| `E_FORBIDDEN` | политика (например, загрузка с неразрешённого домена) |
| `E_BUSY` | конкурирующая операция (например, уже идёт применение твиков) |
| `E_INTEGRITY` | не совпала контрольная сумма/подпись загруженного файла |
| `E_NO_HOST` | (только JS) UI запущен вне hateeze и без мока |
| `E_INTERNAL` | всё остальное; подробности — только в журнал |

- Безопасность (рекомендации Microsoft «Develop secure WebView2 apps»): проверять `e.Source` у каждого входящего сообщения; не делать «generic proxy» (никаких `exec(cmd)`/`reg.set(path,value)` из JS — только конкретные доменные методы); перед `PostWebMessageAsJson` проверять, что в WebView наш origin; JSON строить библиотекой, а не конкатенацией. Лимит размера входящего сообщения (1 МБ).

### 4.2 C#: контракты и source-generated JSON

```csharp
// src/Hateeze.App/Bridge/Contracts.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hateeze.App.Bridge;

public readonly record struct Empty;
public enum ResizeEdge { Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }
public sealed record StartResizeRequest(ResizeEdge Edge);
public sealed record WindowStateDto(bool Maximized, bool Focused, bool Visible, int Dpi);
public sealed record MetricsSubscribeRequest(int Hz);
public sealed record MetricsSample(long T, float CpuPct, float GpuPct, float FrameTimeMs, float DpcMaxUs);
public sealed record DownloadRequest(string Url, string FileName, string? Sha256);
public sealed record DownloadProgress(string Stage, long Received, long? Total, double? Pct, double SpeedBps);
public sealed record DownloadResult(string Path, long Bytes);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Empty))]
[JsonSerializable(typeof(StartResizeRequest))]
[JsonSerializable(typeof(WindowStateDto))]
[JsonSerializable(typeof(MetricsSubscribeRequest))]
[JsonSerializable(typeof(MetricsSample))]
[JsonSerializable(typeof(DownloadRequest))]
[JsonSerializable(typeof(DownloadProgress))]
[JsonSerializable(typeof(DownloadResult))]
internal partial class BridgeJson : JsonSerializerContext { }
```

В csproj `<JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>` — любая попытка сериализации без source-gen упадёт сразу (а не «тихо» через reflection). Хотя trimming для WinForms недоступен, source-gen даёт быстрый старт и готовность к будущему trimming/AOT.

### 4.3 C#: BridgeHost

```csharp
// src/Hateeze.App/Bridge/BridgeHost.cs
using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Hateeze.App.Bridge;

public sealed class BridgeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class BridgeCall
{
    readonly BridgeHost _host;
    long _lastProgress;
    internal BridgeCall(BridgeHost host, long id, CancellationToken ct) { _host = host; Id = id; CancellationToken = ct; }
    public long Id { get; }
    public CancellationToken CancellationToken { get; }

    /// Прогресс долгой операции. Троттлинг ≤10 Гц; force=true — отправить сразу (смена этапа/финал).
    public void Progress<T>(T data, JsonTypeInfo<T> type, bool force = false)
    {
        long now = Environment.TickCount64;
        if (!force && now - Interlocked.Read(ref _lastProgress) < 100) return;
        Interlocked.Exchange(ref _lastProgress, now);
        _host.Post(w =>
        {
            w.WriteString("t", "prog"); w.WriteNumber("id", Id);
            w.WritePropertyName("d"); JsonSerializer.Serialize(w, data, type);
        });
    }
}

public sealed class BridgeHost(WebView2 view, string allowedOrigin, CancellationToken appStopping)
{
    delegate Task<Action<Utf8JsonWriter>?> Route(JsonElement p, BridgeCall call);
    readonly Dictionary<string, Route> _routes = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<long, CancellationTokenSource> _inflight = new();

    /// Метод с параметрами и результатом (оба — типы из BridgeJson).
    public void Map<TReq, TRes>(string method, JsonTypeInfo<TReq> req, JsonTypeInfo<TRes> res,
                                Func<TReq, BridgeCall, Task<TRes>> handler)
        => _routes.Add(method, async (p, call) =>
        {
            TReq arg = p.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? throw new BridgeException("E_BAD_PARAMS", "Не переданы параметры")
                : p.Deserialize(req) ?? throw new BridgeException("E_BAD_PARAMS", "Пустые параметры");
            TRes result = await handler(arg, call);
            return w => JsonSerializer.Serialize(w, result, res);
        });

    /// Метод без параметров и результата.
    public void Map(string method, Func<BridgeCall, Task> handler)
        => _routes.Add(method, async (_, call) => { await handler(call); return null; });

    public void Attach(CoreWebView2 core) => core.WebMessageReceived += OnMessage;

    async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(allowedOrigin, StringComparison.OrdinalIgnoreCase)) return;
        string json = e.WebMessageAsJson;
        if (json.Length > 1_000_000) return;

        long id = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? t = root.GetProperty("t").GetString();
            if (root.TryGetProperty("id", out var idEl)) id = idEl.GetInt64();

            if (t == "cancel") { if (_inflight.TryGetValue(id, out var c)) c.Cancel(); return; }
            if (t != "req" || id <= 0) throw new BridgeException("E_BAD_REQUEST", "Некорректное сообщение");

            string m = root.GetProperty("m").GetString() ?? "";
            if (!_routes.TryGetValue(m, out var route))
                throw new BridgeException("E_NO_METHOD", $"Неизвестный метод: {m}");

            JsonElement p = root.TryGetProperty("p", out var pEl) ? pEl.Clone() : default;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(appStopping);
            _inflight[id] = cts;
            try
            {
                var write = await route(p, new BridgeCall(this, id, cts.Token));
                long rid = id;
                Post(w =>
                {
                    w.WriteString("t", "res"); w.WriteNumber("id", rid); w.WriteBoolean("ok", true);
                    if (write is not null) { w.WritePropertyName("r"); write(w); }
                });
            }
            finally { _inflight.TryRemove(id, out _); }
        }
        catch (Exception ex) { PostError(id, ex); }
    }

    public void Emit<T>(string channel, T data, JsonTypeInfo<T> type) => Post(w =>
    {
        w.WriteString("t", "evt"); w.WriteString("ch", channel);
        w.WritePropertyName("d"); JsonSerializer.Serialize(w, data, type);
    });

    void PostError(long id, Exception ex)
    {
        (string code, string msg) = ex switch
        {
            BridgeException b => (b.Code, b.Message),
            OperationCanceledException => ("E_CANCELLED", "Операция отменена"),
            TimeoutException => ("E_TIMEOUT", "Превышено время ожидания"),
            UnauthorizedAccessException => ("E_ACCESS", "Недостаточно прав для операции"),
            JsonException or KeyNotFoundException or InvalidOperationException => ("E_BAD_PARAMS", "Некорректные параметры"),
            _ => ("E_INTERNAL", "Внутренняя ошибка. Подробности — в журнале."),
        };
        // Log.Error(ex, "bridge {Id}", id);  — подключить логгер проекта
        if (id <= 0) return;
        Post(w =>
        {
            w.WriteString("t", "res"); w.WriteNumber("id", id); w.WriteBoolean("ok", false);
            w.WriteStartObject("e"); w.WriteString("code", code); w.WriteString("message", msg); w.WriteEndObject();
        });
    }

    /// Можно вызывать из любого потока: JSON строится на вызывающем потоке, отправка — на UI-потоке.
    internal void Post(Action<Utf8JsonWriter> body)
    {
        var buf = new ArrayBufferWriter<byte>(512);
        using (var w = new Utf8JsonWriter(buf)) { w.WriteStartObject(); body(w); w.WriteEndObject(); }
        string json = Encoding.UTF8.GetString(buf.WrittenSpan);
        if (view.InvokeRequired) view.BeginInvoke(() => Send(json)); else Send(json);
    }

    void Send(string json)
    {
        if (view.IsDisposed) return;
        var core = view.CoreWebView2;
        if (core is null || !core.Source.StartsWith(allowedOrigin, StringComparison.OrdinalIgnoreCase)) return;
        core.PostWebMessageAsJson(json);
    }
}
```

Потоки: события WebView2 приходят на UI-поток; `await` в обработчике возвращается на UI-поток (WinForms `SynchronizationContext`). Долгая работа внутри обработчиков — через `Task.Run`/асинхронный I/O; **никаких модальных окон и `SendMessage(WM_NCLBUTTONDOWN)` синхронно внутри обработчика** (реентерабельность не поддерживается — «Threading model for WebView2 apps»).

### 4.4 C#: регистрация методов окна

```csharp
// src/Hateeze.App/Bridge/Modules/WindowModule.cs
static void MapWindow(BridgeHost b, MainForm f)
{
    var J = BridgeJson.Default;
    b.Map("window.minimize", _ => { f.BeginInvoke(() => f.WindowState = FormWindowState.Minimized); return Task.CompletedTask; });
    b.Map("window.toggleMaximize", _ =>
    {
        f.BeginInvoke(() => f.WindowState = f.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized);
        return Task.CompletedTask;
    });
    b.Map("window.close", _ => { f.BeginInvoke(() => f.Close()); return Task.CompletedTask; });
    b.Map("window.startDrag", _ =>
    {
        f.BeginInvoke(() => WindowChrome.BeginMoveOrSize(f.Handle, Win32.HTCAPTION));
        return Task.CompletedTask;
    });
    b.Map("window.startResize", J.StartResizeRequest, J.Empty, (r, _) =>
    {
        int ht = r.Edge switch
        {
            ResizeEdge.Left => Win32.HTLEFT, ResizeEdge.Right => Win32.HTRIGHT,
            ResizeEdge.Top => Win32.HTTOP, ResizeEdge.Bottom => Win32.HTBOTTOM,
            ResizeEdge.TopLeft => Win32.HTTOPLEFT, ResizeEdge.TopRight => Win32.HTTOPRIGHT,
            ResizeEdge.BottomLeft => Win32.HTBOTTOMLEFT, _ => Win32.HTBOTTOMRIGHT,
        };
        if (f.WindowState == FormWindowState.Normal)
            f.BeginInvoke(() => WindowChrome.BeginMoveOrSize(f.Handle, ht));
        return Task.FromResult(default(Empty));
    });
}
// Событие состояния окна (максимизация/фокус/видимость/DPI) → JS прячет ресайз-кромки и ставит анимации на паузу:
// bridge.Emit("window.state", new WindowStateDto(max, focused, visible, f.DeviceDpi), BridgeJson.Default.WindowStateDto);
```

### 4.5 Стрим метрик (1–10 Гц)

Принципы:
- Сборщик метрик (PDH/ETW/PresentMon — отдельная тема) пишет **последний** снимок в поле; UI-таймер отправляет его с частотой подписки. Очереди нет → нет накопления задержки, если UI занят.
- Частота: карточки 1–2 Гц, графики 4 Гц (по умолчанию), «детальный» режим 10 Гц. Максимум — 10 Гц.
- Не отправлять, если окно свёрнуто/скрыто или подписчиков 0.
- Размер сообщения < 1 КБ; массивы истории держит JS (кольцевые буферы), C# шлёт только новые точки.

```csharp
// src/Hateeze.App/Bridge/MetricsPump.cs
internal sealed class MetricsPump : IDisposable
{
    readonly BridgeHost _bridge;
    readonly System.Windows.Forms.Timer _timer = new();   // тикает на UI-потоке
    volatile MetricsSample? _latest;
    MetricsSample? _sent;
    public bool WindowVisible { get; set; } = true;

    public MetricsPump(BridgeHost bridge)
    {
        _bridge = bridge;
        _timer.Tick += (_, _) =>
        {
            var s = _latest;
            if (!WindowVisible || s is null || ReferenceEquals(s, _sent)) return;
            _sent = s;
            _bridge.Emit("metrics.sample", s, BridgeJson.Default.MetricsSample);
        };
    }
    public void Publish(MetricsSample s) => _latest = s;                 // из любого потока
    public void SetRate(int hz)                                           // 0 = стоп
    {
        _timer.Enabled = false;
        if (hz <= 0) return;
        _timer.Interval = 1000 / Math.Clamp(hz, 1, 10);
        _timer.Enabled = true;
    }
    public void Dispose() => _timer.Dispose();
}
// bridge.Map("metrics.subscribe", J.MetricsSubscribeRequest, J.Empty, (r, _) => { pump.SetRate(r.Hz); return Task.FromResult(default(Empty)); });
// bridge.Map("metrics.unsubscribe", _ => { pump.SetRate(0); return Task.CompletedTask; });
```

### 4.6 Долгая операция с прогрессом и отменой (пример: загрузка драйвера)

```csharp
// src/Hateeze.App/Bridge/Modules/DriverModule.cs (фрагмент)
static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
{ "us.download.nvidia.com", "international.download.nvidia.com" };  // список — из темы драйверов

b.Map("driver.download", J.DownloadRequest, J.DownloadResult, async (req, call) =>
{
    var uri = new Uri(req.Url);
    if (uri.Scheme != Uri.UriSchemeHttps || !AllowedHosts.Contains(uri.Host))
        throw new BridgeException("E_FORBIDDEN", "Загрузка с этого адреса запрещена");

    var target = Path.Combine(AppPaths.Downloads, Path.GetFileName(req.FileName));
    var part = target + ".part";
    var ct = call.CancellationToken;
    try
    {
        using var resp = await Http.Shared.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct); // Http.Shared — единый HttpClient приложения
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength;
        long received = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var buf = System.Buffers.ArrayPool<byte>.Shared.Rent(1 << 16);
        try
        {
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            int n;
            while ((n = await src.ReadAsync(buf.AsMemory(), ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                sha.AppendData(buf, 0, n);
                received += n;
                call.Progress(new DownloadProgress("download", received, total,
                    total > 0 ? (double)received / total.Value : null,
                    received / Math.Max(sw.Elapsed.TotalSeconds, 0.001)), J.DownloadProgress);
            }
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(buf); }

        call.Progress(new DownloadProgress("verify", received, total, 1, 0), J.DownloadProgress, force: true);
        if (req.Sha256 is { } expected &&
            !Convert.ToHexString(sha.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new BridgeException("E_INTEGRITY", "Контрольная сумма файла не совпала");
        File.Move(part, target, overwrite: true);
        return new DownloadResult(target, received);
    }
    catch
    {
        try { File.Delete(part); } catch { /* файл может быть занят */ }
        throw;   // OperationCanceledException → E_CANCELLED
    }
});
```

### 4.7 TypeScript-клиент

```ts
// ui/src/lib/bridge/contracts.ts — ЕДИНСТВЕННОЕ место описания API (зеркало C# Contracts.cs)
export type ResizeEdge = 'Left'|'Right'|'Top'|'Bottom'|'TopLeft'|'TopRight'|'BottomLeft'|'BottomRight';
export interface WindowState { maximized: boolean; focused: boolean; visible: boolean; dpi: number }
export interface MetricsSample { t: number; cpuPct: number; gpuPct: number; frameTimeMs: number; dpcMaxUs: number }
export interface DownloadProgress { stage: 'download'|'verify'; received: number; total?: number; pct?: number; speedBps: number }

export interface Methods {
  'window.minimize':       { req: void; res: void };
  'window.toggleMaximize': { req: void; res: void };
  'window.close':          { req: void; res: void };
  'window.startDrag':      { req: void; res: void };
  'window.startResize':    { req: { edge: ResizeEdge }; res: Record<string, never> };
  'metrics.subscribe':     { req: { hz: number }; res: Record<string, never> };
  'metrics.unsubscribe':   { req: void; res: void };
  'driver.download':       { req: { url: string; fileName: string; sha256?: string }; res: { path: string; bytes: number } };
}
export interface Events {
  'window.state': WindowState;
  'metrics.sample': MetricsSample;
}
export interface ProgressOf { 'driver.download': DownloadProgress }
```

```ts
// ui/src/lib/bridge/client.ts
import type { Methods, Events, ProgressOf } from './contracts';

export class BridgeError extends Error {
  constructor(readonly code: string, message: string) { super(message); this.name = 'BridgeError'; }
}

interface Transport {
  postMessage(msg: unknown): void;
  addEventListener(type: 'message', fn: (e: { data: any }) => void): void;
}
type Incoming =
  | { t: 'res'; id: number; ok: true; r?: unknown }
  | { t: 'res'; id: number; ok: false; e: { code: string; message: string } }
  | { t: 'prog'; id: number; d: unknown }
  | { t: 'evt'; ch: string; d: unknown };

const native = (globalThis as any).chrome?.webview as Transport | undefined;
let transport: Transport | undefined = native;
export const isNative = !!native;

// В браузере без WebView2 (npm run dev) — мок с фейковыми данными; в продакшене без WebView2 — ошибка.
const ready: Promise<Transport> = native
  ? Promise.resolve(native)
  : import.meta.env.DEV
    ? import('./mock').then((m) => (transport = m.createMockTransport()))
    : Promise.reject(new BridgeError('E_NO_HOST', 'Интерфейс запущен вне hateeze'));

type Pending = { resolve(v: unknown): void; reject(e: unknown): void; progress?(d: unknown): void; timer?: number };
const pending = new Map<number, Pending>();
const listeners = new Map<string, Set<(d: unknown) => void>>();
let seq = 0;

void ready.then((tr) => tr.addEventListener('message', (ev) => {
  const msg = ev.data as Incoming;
  if (msg.t === 'res') {
    const p = pending.get(msg.id); if (!p) return;
    pending.delete(msg.id); clearTimeout(p.timer);
    msg.ok ? p.resolve(msg.r) : p.reject(new BridgeError(msg.e.code, msg.e.message));
  } else if (msg.t === 'prog') {
    pending.get(msg.id)?.progress?.(msg.d);
  } else if (msg.t === 'evt') {
    listeners.get(msg.ch)?.forEach((fn) => fn(msg.d));
  }
}));

export interface CallOptions<M extends keyof Methods> {
  signal?: AbortSignal;
  timeoutMs?: number;                                   // по умолчанию без таймаута (долгие операции)
  onProgress?: (d: M extends keyof ProgressOf ? ProgressOf[M] : never) => void;
}
type Args<M extends keyof Methods> = Methods[M]['req'] extends void
  ? [p?: undefined, o?: CallOptions<M>]
  : [p: Methods[M]['req'], o?: CallOptions<M>];

export async function call<M extends keyof Methods>(m: M, ...[p, o]: Args<M>): Promise<Methods[M]['res']> {
  const tr = await ready;
  const id = ++seq;
  return new Promise((resolve, reject) => {
    const entry: Pending = { resolve: resolve as (v: unknown) => void, reject, progress: o?.onProgress as any };
    if (o?.signal?.aborted) return reject(new BridgeError('E_CANCELLED', 'Операция отменена'));
    o?.signal?.addEventListener('abort', () => tr.postMessage({ t: 'cancel', id }), { once: true });
    if (o?.timeoutMs) entry.timer = window.setTimeout(() => {
      pending.delete(id); tr.postMessage({ t: 'cancel', id });
      reject(new BridgeError('E_TIMEOUT', 'Превышено время ожидания'));
    }, o.timeoutMs);
    pending.set(id, entry);
    tr.postMessage({ t: 'req', id, m, p });
  });
}

export function on<C extends keyof Events>(ch: C, fn: (d: Events[C]) => void): () => void {
  let set = listeners.get(ch);
  if (!set) listeners.set(ch, (set = new Set()));
  set.add(fn as (d: unknown) => void);
  return () => set!.delete(fn as (d: unknown) => void);
}
```

Пример долгой операции в экране:

```ts
const ac = new AbortController();
progress = 0;
try {
  const r = await call('driver.download', { url, fileName: 'driver.exe', sha256 }, {
    signal: ac.signal,
    onProgress: (d) => { progress = d.pct ?? 0; stage = d.stage; },
  });
} catch (e) {
  if (e instanceof BridgeError && e.code === 'E_CANCELLED') toast('Загрузка отменена');
  else toast((e as Error).message);
}
// кнопка «Отмена»: ac.abort();
```

---

## 5. Публикация

### 5.1 csproj (хост)

```xml
<!-- src/Hateeze.App/Hateeze.App.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>                 <!-- LibraryImport -->
    <AssemblyName>hateeze</AssemblyName>
    <RootNamespace>Hateeze.App</RootNamespace>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <ApplicationIcon>Resources\app.ico</ApplicationIcon>
    <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
    <ApplicationVisualStyles>true</ApplicationVisualStyles>      <!-- нужно для TaskDialog -->
    <PlatformTarget>x64</PlatformTarget>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SatelliteResourceLanguages>ru;en</SatelliteResourceLanguages> <!-- не тащить локализации WinForms на 13 языков -->
    <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
    <!-- не копировать WebView2Loader.dll как Content в runtimes\win-x64\native (см. §5.4) -->
    <WebView2NeverCopyLoaderDllToOutputDirectory>true</WebView2NeverCopyLoaderDllToOutputDirectory>
    <UiDir>$(MSBuildProjectDirectory)\..\..\ui\</UiDir>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract> <!-- см. §5.8 о рисках -->
    <PublishTrimmed>false</PublishTrimmed>                       <!-- WinForms: trimming отключён SDK -->
    <DebugType>embedded</DebugType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.4314.35" GeneratePathProperty="true" />
  </ItemGroup>

  <ItemGroup>
    <!-- Нативный загрузчик WebView2 встраиваем сами (§5.4) -->
    <EmbeddedResource Include="$(PkgMicrosoft_Web_WebView2)\runtimes\win-x64\native\WebView2Loader.dll"
                      LogicalName="native/WebView2Loader.dll" Visible="false" />
  </ItemGroup>

  <!-- Сборка фронтенда и встраивание ui/dist/** -->
  <Target Name="BuildUi" Condition="'$(Configuration)' == 'Release' And '$(SkipUi)' != 'true'">
    <Exec Command="npm ci --no-audit --no-fund" WorkingDirectory="$(UiDir)" Condition="!Exists('$(UiDir)node_modules')" />
    <Exec Command="npm run build" WorkingDirectory="$(UiDir)" />
  </Target>
  <!-- Элементы создаются ВНУТРИ цели: wildcard в статической ItemGroup вычислился бы до npm run build -->
  <Target Name="EmbedUi" BeforeTargets="BeforeBuild" DependsOnTargets="BuildUi">
    <ItemGroup>
      <EmbeddedResource Include="$(UiDir)dist\**\*">
        <LogicalName>ui/%(RecursiveDir)%(Filename)%(Extension)</LogicalName>
      </EmbeddedResource>
    </ItemGroup>
  </Target>
</Project>
```

Команда публикации:

```powershell
dotnet publish src/Hateeze.App -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

После первой публикации **проверить папку `publish`**: должен быть только `hateeze.exe` (и, возможно, `.pdb`, если `DebugType` не `embedded`). Любые лишние `*.dll` означают нативные библиотеки, не попавшие в bundle (**НЕ ПРОВЕРЕНО**, какие нативные DLL рантайма .NET 10 остаются снаружи для WinForms-приложения; в shared framework NETCore.App 10.0.14 на диске есть нативные `clrgc.dll`, `clrgcexp.dll`, `msquic.dll`, `System.IO.Compression.Native.dll`, `Microsoft.DiaSymReader.Native.amd64.dll`, `mscordaccore.dll`, `mscordbi.dll` и др.).

### 5.2 Trimming

Подтверждено документацией .NET («Known trimming incompatibilities»): «almost no Windows Forms apps are runnable without built-in COM marshalling, so **trimming support for Windows Forms apps is disabled in the .NET SDK** currently». При `PublishTrimmed`/`PublishAot` SDK выдаёт ошибку **NETSDK1175**. Дополнительно `Microsoft.Web.WebView2.Core.dll` сам построен на встроенном COM-маршалинге (тоже trim-несовместимо). Недокументированный обход `_SuppressWinFormsTrimError` — **avoid**.

### 5.3 Размер exe

Оценка (**НЕ ПРОВЕРЕНО сборкой** — `dotnet new`/`restore` запрещены правилами исследования):

| Компонент | Размер на диске (несжатый) |
|---|---|
| Microsoft.NETCore.App 10.0.14 (весь shared framework) | 76,3 МБ (измерено) |
| WinForms-часть WindowsDesktop.App (`System.Windows.Forms` 13,1 МБ, `.Design` 5,8, `.Primitives` 3,5, `System.Private.Windows.Core` 1,8, `System.Drawing.Common` 0,9 и др.) | ≈26,5 МБ (измерено по именам) |
| WebView2 managed (Core + WinForms) | ≈0,75 МБ |
| WebView2Loader.dll | 0,19 МБ |
| UI (`dist`) | ≈0,6–1,2 МБ |
| **Итого несжатый** | **≈105 МБ** |

С `EnableCompressionInSingleFile` (Deflate; ReadyToRun-сборки фреймворка жмутся примерно вдвое) ожидаемо **≈45–65 МБ**. `SatelliteResourceLanguages=ru;en` срезает локализованные ресурсы WinForms. Компрессия стоит времени запуска (распаковка в память) — Microsoft советует мерить; ожидаемо +100–300 мс (**НЕ ИЗМЕРЕНО**).

### 5.4 WebView2Loader.dll в single-file

Что выяснено из `build/Common.targets` пакета 1.0.4314.35 (прочитано из nupkg):
- Для managed-проектов loader добавляется как **`Content`** с `Link=runtimes\win-x64\native\WebView2Loader.dll` (при `PlatformTarget=x64`), `CopyToOutputDirectory=PreserveNewest`; `Microsoft.Web.WebView2.Core.dll` «handles loading the right DLL at runtime … under those sub folders».
- Свойство `WebView2NeverCopyLoaderDllToOutputDirectory=true` отключает это копирование.
- В single-file управляемые DLL загружаются из памяти (`Assembly.Location` = пустая строка), а нативные — либо остаются рядом с exe (без `IncludeNativeLibrariesForSelfExtract`), либо извлекаются в `%TEMP%\.net\<app>\<hash>\` (с ним). Найдёт ли `Core.dll` загрузчик в извлечённой подпапке `runtimes\win-x64\native` — **НЕ ПРОВЕРЕНО** (именно здесь у многих случается `DllNotFoundException: WebView2Loader.dll`).

**Рекомендация (детерминированно и безопасно):** не полагаться на эвристику, а явно встроить loader ресурсом (csproj выше) и при старте:

```csharp
// src/Hateeze.App/Shell/WebView2Bootstrap.cs (фрагмент)
using System.Security.Cryptography;
using Microsoft.Web.WebView2.Core;

internal static partial class WebView2Bootstrap
{
    /// Вызывать ДО любого обращения к CoreWebView2Environment (иначе InvalidOperationException).
    public static void PrepareLoader()
    {
        using var res = typeof(WebView2Bootstrap).Assembly.GetManifestResourceStream("native/WebView2Loader.dll")
                        ?? throw new InvalidOperationException("WebView2Loader.dll не встроен в сборку");
        var bytes = new byte[res.Length];
        res.ReadExactly(bytes);
        var hash = SHA256.HashData(bytes);

        // %ProgramFiles%\hateeze\bin\<hash>: запись только для Administrators/SYSTEM (наследуемый ACL Program Files),
        // неповышенный процесс не может заранее создать папку или подменить DLL.
        var dir = Path.Combine(AppPaths.ProtectedRoot, "bin", Convert.ToHexString(hash)[..16]);
        var path = Path.Combine(dir, "WebView2Loader.dll");
        Directory.CreateDirectory(dir);
        if (!File.Exists(path) || !SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(hash))
            File.WriteAllBytes(path, bytes);

        CoreWebView2Environment.SetLoaderDllFolderPath(dir);  // «path of a folder containing WebView2Loader.dll»
    }
}

internal static class AppPaths
{
    public static string ProtectedRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "hateeze");
    public static string Downloads { get; } = Path.Combine(ProtectedRoot, "downloads");
}
```

`SetLoaderDllFolderPath` (SDK ≥ 1.0.1293.44): путь к **папке**; архитектура DLL должна совпадать с процессом; после успешной загрузки повторный вызов бросает `InvalidOperationException`; при отсутствии файла первый вызов любого API `CoreWebView2Environment` бросит `DllNotFoundException`.

Если команда предпочтёт простоту для MVP: убрать `WebView2NeverCopyLoaderDllToOutputDirectory` и встраивание, оставить `IncludeNativeLibrariesForSelfExtract=true` и **проверить на чистой ВМ**, что exe стартует (если нет — вернуться к схеме выше).

### 5.5 app.manifest

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="hateeze.app" />
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 и Windows 11 (один GUID на оба) -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <longPathAware xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">true</longPathAware>
      <!-- DPI НЕ задаём здесь: источник истины — ApplicationHighDpiMode в csproj -->
    </windowsSettings>
  </application>
</assembly>
```

Определение версии ОС — `OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)`; Win11 23H2 = 22631, 24H2 = 26100, 25H2 = 26200, Win10 22H2 = 19045.

### 5.6 UserDataFolder

- По умолчанию для .NET/Win32 UDF = `<путь exe>.WebView2` рядом с exe (доки: «D:\WebView2App\WebView2.exe.WebView2\»). Для портативного exe в «Загрузках» это мусор рядом с файлом, в Program Files — может не создаться → **всегда задавать явно**: `%LOCALAPPDATA%\hateeze\WebView2` (`CoreWebView2Environment.CreateAsync(null, udf, options)`).
- Под UAC-повышением `%LOCALAPPDATA%` — профиль **того же** пользователя (если он сам админ). Если стандартный пользователь повышается чужими учётными данными (OTS elevation), профиль будет администратора — **ожидаемое поведение**, просто знать.
- Не менять ACL UDF (Microsoft: изменения ACL ломают LowIL/AppContainer-процессы рендера → «blank screens, initialization failures, crashes»).
- WebView2 **нельзя** запускать от SYSTEM (Microsoft: «WebView2 cannot be run as a system user») → автозапуск делать задачей планировщика «от имени пользователя, с наивысшими правами», не от SYSTEM.

### 5.7 Проверка и установка WebView2 Runtime

Microsoft: Runtime входит в Windows 11; на Windows 10 он есть у «the vast majority», но не у всех → проверять при запуске.

Алгоритм:
1. Реестр (без загрузки DLL): `pv (REG_SZ)` в
   `HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}` (per-machine) и
   `HKCU\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}` (per-user).
   Runtime отсутствует, если обоих нет или значение `null`/пусто/`0.0.0.0`.
2. После `PrepareLoader()` — `CoreWebView2Environment.GetAvailableBrowserVersionString()`; бросает `WebView2RuntimeNotFoundException`, если Runtime нет.
3. Нет Runtime → нативный `TaskDialog` (WebView2 ещё нет, HTML показать нельзя) → загрузка bootstrapper → **проверка Authenticode-подписи Microsoft** → `MicrosoftEdgeWebview2Setup.exe /silent /install`. Из повышенного процесса установка **per-machine**.
4. Повторная проверка (п.1–2). Неуспех → сообщение со ссылкой на ручную установку `https://developer.microsoft.com/microsoft-edge/webview2/consumer/`.

```csharp
// WebView2Bootstrap.cs (продолжение)
using System.Diagnostics;
using Microsoft.Win32;

public static bool IsRuntimeInstalled(out string? version)
{
    const string sub = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    version = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\" + sub, "pv", null) as string;
    if (!IsValid(version)) version = Registry.GetValue(@"HKEY_CURRENT_USER\Software\" + sub, "pv", null) as string;
    if (!IsValid(version)) return false;
    try { version = CoreWebView2Environment.GetAvailableBrowserVersionString(); return true; }
    catch (WebView2RuntimeNotFoundException) { return false; }
    static bool IsValid(string? v) => !string.IsNullOrWhiteSpace(v) && v != "0.0.0.0";
}

const string BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703"; // 301 → MicrosoftEdgeWebview2Setup.exe, ~1.9 МБ

static async Task InstallRuntimeAsync(Action<string> status, CancellationToken ct)
{
    var dir = Path.Combine(AppPaths.ProtectedRoot, "tmp");
    Directory.CreateDirectory(dir);
    var exe = Path.Combine(dir, "MicrosoftEdgeWebview2Setup.exe");
    status("Загрузка установщика WebView2 (≈2 МБ)…");
    using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })   // редиректы следуются по умолчанию
    {
        await using var src = await http.GetStreamAsync(BootstrapperUrl, ct);
        await using var dst = File.Create(exe);
        await src.CopyToAsync(dst, ct);
    }
    if (!Authenticode.IsTrustedAndSignedBy(exe, "Microsoft Corporation"))
        throw new InvalidDataException("Подпись установщика WebView2 не прошла проверку");
    status("Установка Microsoft Edge WebView2 Runtime… Это может занять несколько минут.");
    using var p = Process.Start(new ProcessStartInfo(exe, "/silent /install") { UseShellExecute = false, CreateNoWindow = true })!;
    await p.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromMinutes(10), ct);
    try { File.Delete(exe); } catch { }
    // Коды выхода bootstrapper'а публично не документированы — успех определяем повторной проверкой IsRuntimeInstalled.
}
```

Порядок запуска и нативный диалог установки (TaskDialog из WinForms; требует `ApplicationVisualStyles=true`):

```csharp
// src/Hateeze.App/Program.cs
internal static class Program
{
    [STAThread]
    static int Main()
    {
        using var mutex = new Mutex(true, @"Local\hateeze.single-instance", out bool isNew);
        if (!isNew) { SingleInstance.ActivateExisting(); return 0; }     // найти окно и SetForegroundWindow

        ApplicationConfiguration.Initialize();      // PerMonitorV2, visual styles — из csproj
        WebView2Bootstrap.PrepareLoader();           // ДО любого CoreWebView2Environment.*
        if (!WebView2Bootstrap.EnsureRuntimeInteractive()) return 1;
        Application.Run(new MainForm());
        return 0;
    }
}

// WebView2Bootstrap.cs (продолжение)
public static bool EnsureRuntimeInteractive()
{
    if (IsRuntimeInstalled(out _)) return true;
    var install = new TaskDialogButton("Установить (≈2 МБ)") { AllowCloseDialog = false };
    var exit = new TaskDialogButton("Выход");
    var ask = new TaskDialogPage
    {
        Caption = "hateeze",
        Heading = "Требуется компонент Microsoft Edge WebView2",
        Text = "Интерфейс hateeze работает на компоненте Microsoft Edge WebView2. Он не найден на этом компьютере. Установить его сейчас с серверов Microsoft?",
        Icon = TaskDialogIcon.Warning,
        Buttons = { install, exit },
    };
    var cts = new CancellationTokenSource();
    var progress = new TaskDialogPage
    {
        Caption = "hateeze",
        Heading = "Установка WebView2 Runtime",
        Text = "Подготовка…",
        ProgressBar = new TaskDialogProgressBar(TaskDialogProgressBarState.Marquee),
        Buttons = { TaskDialogButton.Cancel },
    };
    progress.Buttons[0].Click += (_, _) => cts.Cancel();
    bool ok = false;
    install.Click += (_, _) => ask.Navigate(progress);
    progress.Created += async (_, _) =>
    {
        try { await InstallRuntimeAsync(t => progress.Text = t, cts.Token); ok = IsRuntimeInstalled(out _); }
        catch (Exception) { ok = false; /* лог */ }
        progress.BoundDialog?.Close();
    };
    TaskDialog.ShowDialog(ask);
    if (!ok)
        TaskDialog.ShowDialog(new TaskDialogPage
        {
            Caption = "hateeze", Icon = TaskDialogIcon.Error,
            Heading = "Не удалось установить WebView2",
            Text = "Скачайте «Evergreen Bootstrapper» вручную: https://developer.microsoft.com/microsoft-edge/webview2/consumer/ и запустите hateeze снова.",
        });
    return ok;
}
```

Проверка подписи (WinVerifyTrust; типовой код, **не компилировался**):

```csharp
// src/Hateeze.App/Native/Authenticode.cs
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

internal static class Authenticode
{
    static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WINTRUST_FILE_INFO { public uint cbStruct; public string pcwszFilePath; public nint hFile; public nint pgKnownSubject; }

    [StructLayout(LayoutKind.Sequential)]
    struct WINTRUST_DATA
    {
        public uint cbStruct; public nint pPolicyCallbackData, pSIPClientData;
        public uint dwUIChoice, fdwRevocationChecks, dwUnionChoice; public nint pFile;
        public uint dwStateAction; public nint hWVTStateData, pwszURLReference;
        public uint dwProvFlags, dwUIContext; public nint pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    static extern int WinVerifyTrust(nint hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WINTRUST_DATA data);

    public static bool IsTrustedAndSignedBy(string path, string organization)
    {
        var fi = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
        nint pFi = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fi, pFi, false);
            var d = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2,            // WTD_UI_NONE
                fdwRevocationChecks = 0,   // WTD_REVOKE_NONE (оффлайн-устойчиво)
                dwUnionChoice = 1,         // WTD_CHOICE_FILE
                pFile = pFi,
                dwStateAction = 1,         // WTD_STATEACTION_VERIFY
            };
            int hr = WinVerifyTrust(-1, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref d);
            d.dwStateAction = 2;           // WTD_STATEACTION_CLOSE
            WinVerifyTrust(-1, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref d);
            if (hr != 0) return false;
            // Сертификат подписанта (сам метод подпись НЕ проверяет — это сделал WinVerifyTrust выше).
            #pragma warning disable SYSLIB0057 // если метод помечен устаревшим в .NET 10
            var cert = X509Certificate.CreateFromSignedFile(path);
            #pragma warning restore SYSLIB0057
            return cert.Subject.Contains($"O={organization}", StringComparison.Ordinal);
        }
        finally { Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFi); Marshal.FreeHGlobal(pFi); }
    }
}
```

UI-тексты нативного диалога (RU):

| Ключ | Текст |
|---|---|
| `wv2.missing.heading` | Требуется компонент Microsoft Edge WebView2 |
| `wv2.missing.text` | Интерфейс hateeze работает на компоненте Microsoft Edge WebView2. Он не найден на этом компьютере. Установить его сейчас с серверов Microsoft? |
| `wv2.missing.install` | Установить (≈2 МБ) |
| `wv2.missing.exit` | Выход |
| `wv2.progress.download` | Загрузка установщика WebView2 (≈2 МБ)… |
| `wv2.progress.install` | Установка Microsoft Edge WebView2 Runtime… Это может занять несколько минут. |
| `wv2.failed` | Не удалось установить WebView2 автоматически. Скачайте «Evergreen Bootstrapper» вручную: https://developer.microsoft.com/microsoft-edge/webview2/consumer/ и запустите hateeze снова. |

### 5.8 Повышенный процесс + WebView2: риски и митигации

Microsoft («Develop secure WebView2 apps»): «we recommend hosting WebView2 in a process that runs at standard (non-elevated) user integrity… If your application requires elevated privileges… isolating that work in a separate, dedicated process and keeping the WebView2 host component de-elevated». Требование проекта — один exe, всегда админ. Принимаем осознанно, с митигациями:

| Риск | Митигация |
|---|---|
| Подмена UI-файлов неповышенным процессом → вызов привилегированных методов моста | UI только из ресурсов exe (§3), CSP, блокировка навигации, проверка `Source` |
| Single-file извлекает нативные DLL в `%TEMP%\.net\…` (запись доступна неповышенному процессу того же пользователя → DLL planting). Доки .NET: «these directories shouldn't be writable by users or services with different privileges» | Loader — в `%ProgramFiles%\hateeze\bin\<hash>` (§5.4); проверить, какие ещё нативные DLL извлекаются; при необходимости — `DOTNET_BUNDLE_EXTRACT_BASE_DIR` (задаётся только окружением **до** старта процесса → возможен перезапуск самого себя с этой переменной) |
| `WEBVIEW2_*` переменные и HKCU-политики игнорируются в повышенном процессе | Флаги Chromium — только через `CoreWebView2EnvironmentOptions.AdditionalBrowserArguments` в коде; отладка `--remote-debugging-port` тоже только так |
| Внешние ссылки открываются браузером с правами админа | `ExternalLauncher` через оболочку Explorer (§3.4) |
| Drag&drop файлов из Проводника не работает (UIPI: неповышенный → повышенный) | Только файловые диалоги (`<input type=file>` работает) |
| Состояние в `localStorage` доступно для подмены | Критичное состояние — только в C#, в защищённой папке |
| Неподписанный повышенный exe → жёлтый UAC «Неизвестный издатель», SmartScreen, ложные срабатывания AV у «твикеров» | Подпись кода (OV/EV или Azure Trusted Signing) — вне рамок исследования, но настоятельно |

Альтернатива (если требование «всегда админ» пересмотрят): `asInvoker`-UI-процесс с WebView2 + повышенный «движок» (тот же exe с `--engine`, запуск через `runas`) с IPC по именованному каналу с ACL. Один запрос UAC при старте, WebView2 — без повышения.

---

## 6. Глобальная горячая клавиша Ctrl+Alt+Shift+R

API: `RegisterHotKey(hWnd, id, fsModifiers, vk)`; модификаторы `MOD_ALT=0x0001`, `MOD_CONTROL=0x0002`, `MOD_SHIFT=0x0004`, `MOD_WIN=0x0008`, `MOD_NOREPEAT=0x4000` (без автоповтора); `vk` для R = `0x52`; приложение обязано использовать `id` в диапазоне **0x0000–0xBFFF**; `WM_HOTKEY = 0x0312` (wParam = id). Если сочетание занято — `ERROR_HOTKEY_ALREADY_REGISTERED (1409)`. F12 зарезервирована отладчиком (нам не мешает). Hotkey привязан к потоку окна → работает, пока жив UI-поток хоста, даже если WebView2 упал; срабатывает и когда в фокусе полноэкранная игра (системная обработка до доставки окну), но **НЕ ПРОВЕРЕНО** с играми/античитами, перехватывающими клавиатуру низкоуровнево.

Семантика (решение): **двойное нажатие в течение 3 секунд** — первое нажатие даёт звуковой сигнал и тост, второе запускает откат. Одиночное нажатие легко сделать случайно, а последствие — перезагрузка ПК.

```csharp
// src/Hateeze.App/Shell/PanicHotkey.cs
using System.Diagnostics;
using System.Media;
using Hateeze.App.Native;

namespace Hateeze.App.Shell;

internal sealed class PanicHotkey(nint hwnd) : IDisposable
{
    const int Id = 0xB001;                       // 0x0000–0xBFFF для приложений
    long _armedAt;
    bool _registered;

    public event Action<bool>? Armed;            // true = ждём второе нажатие (UI покажет тост)
    public Func<Task>? RollbackAsync { get; set; }   // откат к снимку — из модуля отката

    public bool TryRegister()
    {
        _registered = Win32.RegisterHotKey(hwnd, Id,
            Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_SHIFT | Win32.MOD_NOREPEAT, Win32.VK_R);
        // false + Marshal.GetLastPInvokeError() == 1409 → сочетание занято: показать предупреждение в UI
        return _registered;
    }

    public async void OnWmHotkey(int id)
    {
        if (id != Id) return;
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _armedAt) > 3000)
        {
            Interlocked.Exchange(ref _armedAt, now);
            SystemSounds.Exclamation.Play();
            Armed?.Invoke(true);
            return;
        }
        Interlocked.Exchange(ref _armedAt, 0);
        Armed?.Invoke(false);
        try { if (RollbackAsync is not null) await RollbackAsync(); }
        catch (Exception)
        {
            // async void: исключение НЕЛЬЗЯ выпускать наружу (уйдёт в Application.ThreadException).
            // Залогировать. Перезагружать ли после неудачного отката — открытый вопрос (§11.2 п.4).
        }
        finally
        {
            // Перезагрузка через 10 с; пользователь может отменить командой «shutdown /a».
            Process.Start(new ProcessStartInfo("shutdown.exe",
                "/r /t 10 /d p:4:1 /c \"hateeze: твики откатены к снимку, перезагрузка через 10 секунд. Отмена: shutdown /a\"")
            { UseShellExecute = false, CreateNoWindow = true });
        }
    }

    public void Dispose()
    {
        if (_registered) Win32.UnregisterHotKey(hwnd, Id);
        _registered = false;
    }
}
```

Замечания:
- Регистрировать в `OnHandleCreated`, снимать в `OnHandleDestroyed` (HWND может пересоздаться).
- Если окно в трее (форма скрыта), HWND жив — hotkey работает.
- Откат должен быть **синхронно надёжным** и не зависеть от WebView2 (логика — в C#-модуле отката, другая тема). Если откат упал — всё равно перезагружать? Решение за владельцем модуля отката (см. «Открытые вопросы»).

UI-тексты (RU): «Нажмите Ctrl+Alt+Shift+R ещё раз в течение 3 секунд, чтобы откатить все твики и перезагрузить ПК.» · «Откат твиков к снимку… Перезагрузка через 10 секунд. Отменить перезагрузку: Win+R → shutdown /a» · «Не удалось зарегистрировать Ctrl+Alt+Shift+R: сочетание занято другой программой. Аварийный откат доступен из меню в трее.»

---

## 7. Liquid glass в CSS для Chromium/WebView2

### 7.1 Что реально даёт «стекло» на вебе (и чего не даёт)

- `backdrop-filter` (Chrome/Edge 76+, Firefox 103+, Safari 18+ без префикса) размывает **то, что нарисовано на странице под элементом**. Нет фона на странице — нечего размывать → «стекло» не читается. Поэтому фон — живой: тёмная база + 3 больших радиальных «блоба» + зерно.
- Настоящая «жидкая» рефракция (как у Apple Liquid Glass) на вебе = SVG `feDisplacementMap` через `backdrop-filter: url(#filter)`. **Только Chromium** («Only Chrome currently supports using SVG filters as backdrop-filter» — kube.io; README liquid-glass-react: «Safari and Firefox only partially support the effect (displacement will not be visible)»). Для WebView2 — подходит.
- **Проверено в этом исследовании (Chromium 152):**
  - `CSS.supports('backdrop-filter','url(#lg)') === true`, вычисленное значение `blur(2px) url("#lg")` сохраняется; рендер с `feTurbulence`+`feDisplacementMap` даёт видимое искажение полос фона (скриншот).
  - Линза на SDF скруглённого прямоугольника (метод shuding/liquid-glass) рендерится корректно; генерация карты 260×80 — **9,7 мс**, 1200×700 — **55 мс** (JS, Ryzen 7 5700X).
  - `mask-composite: exclude` и `-webkit-mask-composite: xor` поддерживаются (рамка-блик).
  - `prefers-reduced-transparency` распознаётся (`matchMedia(...).media` не `not all`).
  - **Backdrop Root**: стекло внутри предка с `opacity:.98`, `filter:brightness(1)` или внутри другого элемента с `backdrop-filter` выглядит **иначе** (теряет фон страницы — тёмная однотонная заливка вместо размытых полос), а внутри предка с `transform` — правильно. Вывод: **не вкладывать стекло в стекло**, не анимировать `opacity`/`filter` у предков стеклянных панелей (переходы экранов — через `transform`, либо `opacity` на самом стекле).

### 7.2 Токены и материал (перенос в `ui/src/styles/`)

Полный рабочий эталон — [`shell-ui-liquid-glass-demo.html`](./shell-ui-liquid-glass-demo.html). Ключевые части:

```css
/* tokens.css */
:root {
  color-scheme: dark;
  --bg-0: #06070b;                          /* = Form.BackColor = DefaultBackgroundColor */
  --text-1: rgb(255 255 255 / .94); --text-2: rgb(255 255 255 / .68); --text-3: rgb(255 255 255 / .46);
  --accent: #7c6cff; --accent-2: #33d1ff;
  --ok: #3ddc97; --warn: #ffb547; --danger: #ff5c7a; --exp: #b78cff;   /* proven / situational / avoid / experimental */
  --glass-tint-top: rgb(255 255 255 / .10); --glass-tint-bot: rgb(255 255 255 / .035);
  --glass-blur: 18px; --glass-sat: 170%; --glass-bright: 1.04;
  --rim-hi: rgb(255 255 255 / .55); --rim-lo: rgb(255 255 255 / .06);
  --r-xl: 24px; --r-lg: 18px; --r-md: 12px; --r-pill: 999px;
  --dur-fast: 120ms; --dur: 220ms; --ease: cubic-bezier(.2,.8,.2,1);
}
```

```css
/* glass.css — материал */
.glass {
  position: relative; isolation: isolate;               /* stacking context для псевдо-слоёв с z-index:-1 */
  border-radius: var(--r, var(--r-xl));
  background: linear-gradient(180deg, var(--glass-tint-top), var(--glass-tint-bot));
  backdrop-filter: blur(var(--glass-blur)) saturate(var(--glass-sat)) brightness(var(--glass-bright));
  box-shadow:
    inset 0 1px 0 rgb(255 255 255 / .16),               /* верхний внутренний блик */
    inset 0 -1px 0 rgb(255 255 255 / .03),
    0 1px 2px rgb(0 0 0 / .30),
    0 22px 48px -18px rgb(0 0 0 / .65);                 /* «подъём» над фоном */
}
/* specular rim: градиентная 1px-рамка (mask-composite) */
.glass::before {
  content: ""; position: absolute; inset: 0; z-index: -1; border-radius: inherit; padding: 1px; pointer-events: none;
  background: linear-gradient(135deg, var(--rim-hi) 0%, var(--rim-lo) 28%, rgb(255 255 255 / 0) 52%,
                                      rgb(255 255 255 / .10) 78%, rgb(255 255 255 / .32) 100%);
  -webkit-mask: linear-gradient(#000 0 0) content-box, linear-gradient(#000 0 0);
  -webkit-mask-composite: xor; mask-composite: exclude;
}
/* блик за курсором: --mx/--my ставит JS только при pointermove над элементом */
.glass::after {
  content: ""; position: absolute; inset: 0; z-index: -1; border-radius: inherit; pointer-events: none;
  background: radial-gradient(420px circle at var(--mx, 20%) var(--my, -30%), rgb(255 255 255 / .09), transparent 46%);
}
```

Фон:

```css
.bg { position: fixed; inset: 0; z-index: 0; pointer-events: none; overflow: hidden;
  background: radial-gradient(1100px 700px at 8% -12%, #1a1342 0%, transparent 60%),
              radial-gradient(900px 700px at 110% 115%, #082a3a 0%, transparent 60%), var(--bg-0); }
/* Блобы — радиальные градиенты БЕЗ filter:blur (дешевле), анимируется только transform */
.blob { position: absolute; width: 52vmax; aspect-ratio: 1; border-radius: 50%; opacity: .75;
  will-change: transform; animation: drift var(--t, 32s) ease-in-out infinite alternate; }
.b1 { left: -14vmax; top: -18vmax; --t: 34s; background: radial-gradient(circle, rgb(124 108 255 / .55), rgb(124 108 255 / 0) 62%); }
.b2 { right: -18vmax; top: 10vmax; --t: 41s; background: radial-gradient(circle, rgb(51 209 255 / .38), rgb(51 209 255 / 0) 60%); animation-direction: alternate-reverse; }
.b3 { left: 18vmax; bottom: -26vmax; --t: 47s; background: radial-gradient(circle, rgb(255 92 168 / .30), rgb(255 92 168 / 0) 60%); }
@keyframes drift { to { transform: translate3d(10vmax, 6vmax, 0) scale(1.18); } }
/* Зерно: статичная альфа-текстура из feTurbulence, без mix-blend-mode */
.grain { position: fixed; inset: 0; z-index: 1; pointer-events: none; opacity: .05;
  background-image: url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='160' height='160'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='.9' numOctaves='2' stitchTiles='stitch'/><feColorMatrix values='0 0 0 0 1  0 0 0 0 1  0 0 0 0 1  0 0 0 1.4 -0.2'/></filter><rect width='100%' height='100%' filter='url(%23n)'/></svg>"); }
```

(CSP: `img-src 'self' data:` нужен и для зерна, и для карты линзы.)

### 7.3 «Жидкая линза» (рефракция) — Chromium-only

Техника (shuding/liquid-glass, MIT; kube.io описывает физику Снелла): карта смещения строится в canvas по SDF скруглённого прямоугольника (в центре смещения нет, к краю — сжатие к центру), кодируется в R/G (`128 ± 127`), подаётся в `<feImage>`, далее `<feDisplacementMap in="SourceGraphic" in2="map" xChannelSelector="R" yChannelSelector="G" scale=…>`. Важно:
- `color-interpolation-filters="sRGB"` на `<filter>` (по умолчанию linearRGB искажает значения карты);
- `filterUnits="userSpaceOnUse"`, `width/height` фильтра и `feImage` = размер элемента в CSS-px; **при ресайзе карту пересобирать** (фильтр не масштабируется сам — kube.io: «The backdrop-filter dimensions does not adjust automatically to the element size»);
- смещение 8-битное → максимум ≈ ±128 px; `scale` = максимальное смещение (shuding: `maxScale*0.5` нормировка).

Портируемый код — функции `buildLensMap()` и `attachLiquidLens(el)` в демо-файле (≈50 строк). В Svelte оформить как action:

```ts
// ui/src/lib/fx/liquidLens.svelte.ts — action: <button use:liquidLens>…</button>
// Имя файла *.svelte.ts обязательно: руны ($effect) разрешены только в .svelte и .svelte.ts.
import { attachLiquidLens } from './lens-core';   // перенос из демо (MIT-атрибуция shuding/liquid-glass)
import { fx } from '$lib/state/fx.svelte';
export function liquidLens(node: HTMLElement) {
  $effect(() => {                                 // actions в Svelte 5 выполняются в контексте эффекта
    if (fx.level !== 'full') return;              // режим «Упрощённые эффекты» — без рефракции
    return attachLiquidLens(node);                // возвращаемая функция = очистка (svg + ResizeObserver)
  });
}
```

Где применять: 1–2 элемента (кнопка «Применить», ползунок-индикатор активного пункта навигации). **Не** на больших панелях (карта 1200×700 строится 55 мс, а сам фильтр дорог на GPU).

Альтернатива, совместимая со всеми движками (gentpan/liquidglass, MIT): `filter: url(#displace)` на **собственном** слое элемента (копия фона внутри элемента) — для WebView2 не нужна.

### 7.4 Производительность (игровой контекст!)

Приложение работает рядом с игрой → интерфейс не должен отнимать GPU/CPU, когда пользователь играет.

Жёсткие правила:
1. **Стекло только на крупных контейнерах**: навигация, карточки, модалки, тосты. Строки таблиц, элементы списков, чипы — обычные полупрозрачные фоны **без** `backdrop-filter`. Бюджет: ≤ 6–8 стеклянных поверхностей на экране.
2. Blur 12–24 px для панелей; не анимировать радиус blur (самая дорогая операция); анимировать только `transform`/`opacity` не-стеклянных слоёв.
3. Пока фон анимируется, каждое стекло пересчитывает backdrop **каждый кадр** — на мониторе 240 Гц это 240 пересчётов/с. Поэтому:
   - анимация блобов — медленная (30–50 с цикл) и **ставится на паузу**, когда окно не в фокусе/свёрнуто/скрыто (`html[data-paused]`), при `prefers-reduced-motion`, в режиме «Упрощённые эффекты»;
   - хост шлёт `window.state` (фокус/видимость) → JS ставит/снимает `data-paused`;
   - при свёрнутом окне — `IsVisible=false` + `TrySuspendAsync()` (§2.7).
4. `will-change: transform` — только на 3 движущихся блобах (каждый слой — VRAM ≈ ширина×высота×4 байта; блоб 52vmax на 1920 px ≈ 1000×1000×4 ≈ 4 МБ).
5. Не использовать `filter: blur()` на анимируемых блобах — радиальный градиент уже мягкий.
6. Рефракция — ≤ 2 мелких элемента; пересборка карты только по `ResizeObserver` с дебаунсом 120 мс.
7. Таблицы с частыми обновлениями (метрики 4–10 Гц) — `font-variant-numeric: tabular-nums`, обновлять `textContent`, не пересоздавать DOM.
8. Режим **«Упрощённые эффекты»** (`html[data-fx="low"]`): `backdrop-filter: none`, фон панелей `rgb(22 24 34 / .92)`, анимации выключены. Включать по умолчанию, если нет аппаратного ускорения (WebGL renderer = SwiftShader/Microsoft Basic Render) — проверка в JS через `WEBGL_debug_renderer_info` (**НЕ ПРОВЕРЕНО**, что WebView2 отдаёт имя рендерера).
9. GPU-нагрузку UI мерить: Диспетчер задач → процессы `msedgewebview2.exe` (GPU-движок 3D), PresentMon по `msedgewebview2.exe`. Целевой бюджет в простое на RTX 4060 — < 1–2 % GPU при паузе анимаций; **НЕ ИЗМЕРЕНО**.

Доступность:

```css
@media (prefers-reduced-motion: reduce) { .blob { animation: none; } *, *::before, *::after { transition-duration: 1ms !important; } }
@media (prefers-reduced-transparency: reduce) { .glass { backdrop-filter: none; background: rgb(22 24 34 / .95); } }
@media (prefers-contrast: more) { .glass::before { background: rgb(255 255 255 / .6); } :root { --text-2: rgb(255 255 255 / .85); } }
html[data-fx="low"] .glass { backdrop-filter: none !important; background: rgb(22 24 34 / .92); }
html[data-fx="low"] .blob, html[data-paused] .blob { animation-play-state: paused; }
```

Контраст: у полупрозрачных поверхностей нет фиксированного коэффициента контраста — проверять текст на **худшем** фоне (самый светлый участок блоба). Плотные данные (таблицы твиков, числа метрик) — на подложке с тинтом ≥ 6–10 % и без анимированного фона непосредственно под ними.

### 7.5 Найденные реализации и лицензии

| Источник | Что взять | Лицензия |
|---|---|---|
| github.com/shuding/liquid-glass (Shu Ding, 2025) | генерация карты смещения из SDF скруглённого прямоугольника + `backdrop-filter: url()` | **MIT** — можно переносить с атрибуцией |
| github.com/rdev/liquid-glass-react (Max Rovensky, ~6,5k ★) | хроматическая аберрация: три `feDisplacementMap` с разным `scale` по каналам + `feBlend mode=screen` + маска по краю; приём «`filter:url()` на слое, у которого `backdrop-filter: blur() saturate()`» | **MIT** — идеи/фрагменты фильтров с атрибуцией; сам React-пакет не нужен |
| github.com/gentpan/liquidglass (2026) | кросс-движковая рефракция через `filter` на собственных пикселях | MIT |
| github.com/rizroze/liquid-glass, yichen08092025/liquid-glass, samasante/liquid-glass (2026) | однофайловые реализации, заметка «max shift = scale/2» | MIT |
| kube.io «Liquid Glass in the browser: refraction with CSS and SVG» | теория (Снелл, профили поверхности, rim-light через второй `feImage`+`feBlend`), ограничения | лицензии нет («needs cleanup before any possible open-source release») → **только идеи, код не копировать** |
| dev.to/vitalbadjo «Liquid glass CSS…» (2026) | рецепт базового материала, `@supports` для `url()` | лицензия не указана → только идеи |
| buildmvpfast.com «Liquid Glass on the Web…» (2026) | правила производительности/доступности (blur 12–30 px, 3–4 слоя на вьюпорт, не анимировать blur) | «All rights reserved» → только факты |

Атрибуции MIT положить в `THIRD-PARTY-NOTICES.md` (и в «О программе»).

---

## 8. Графики, иконки, шрифты (всё офлайн)

### 8.1 Графики: uPlot + свой спарклайн

| | uPlot 1.6.32 | Свой canvas |
|---|---|---|
| Размер | 51 КБ min (22 КБ gzip) + 1,9 КБ CSS | ~1–3 КБ |
| Оси, шкалы времени, легенда, курсор, зум, несколько серий | да | писать самим |
| Производительность | рассчитан на стриминг и 100k+ точек | достаточно для 120–600 точек |
| Лицензия | MIT | своя |

Решение: **uPlot** — для полноразмерных графиков (frametime, загрузка ЦП/ГП, DPC/ISR) с осями и курсором; **canvas-спарклайн** — для мини-графиков в карточках (код в демо; баг «после смены `canvas.width` холст очищается — нужно перерисовать» найден при проверке и исправлен).

Svelte-обёртка:

```svelte
<!-- ui/src/lib/charts/UPlotChart.svelte -->
<script lang="ts">
  import uPlot from 'uplot';
  import 'uplot/dist/uPlot.min.css';
  import { onMount } from 'svelte';
  import { on } from '$lib/bridge/client';
  import type { MetricsSample } from '$lib/bridge/contracts';

  let { field, label, unit = '', color = '#7c6cff', windowSec = 60, hz = 4, height = 180 }:
    { field: keyof MetricsSample; label: string; unit?: string; color?: string; windowSec?: number; hz?: number; height?: number } = $props();

  let host: HTMLDivElement;

  onMount(() => {
    const cap = windowSec * hz;
    const xs: number[] = [], ys: number[] = [];
    const css = getComputedStyle(document.documentElement);
    const axis = { stroke: css.getPropertyValue('--text-3').trim(), grid: { stroke: 'rgba(255,255,255,.06)' }, ticks: { show: false } };
    const u = new uPlot({
      width: host.clientWidth, height,
      scales: { x: { time: true } },
      axes: [axis, { ...axis, values: (_u, v) => v.map((n) => `${n}${unit}`) }],
      series: [{}, { label, stroke: color, width: 1.5, fill: color + '22', points: { show: false } }],
      legend: { show: false },
      cursor: { drag: { x: false, y: false } },
    }, [xs, ys], host);

    const ro = new ResizeObserver(([e]) => u.setSize({ width: e.contentRect.width, height }));
    ro.observe(host);
    const off = on('metrics.sample', (s) => {
      xs.push(s.t / 1000); ys.push(s[field] as number);
      if (xs.length > cap) { xs.shift(); ys.shift(); }
      u.setData([xs, ys]);                      // ≤10 Гц — дёшево
    });
    return () => { off(); ro.disconnect(); u.destroy(); };
  });
</script>
<div bind:this={host} class="chart"></div>
<style> .chart { width: 100%; } .chart :global(.u-legend) { display: none; } </style>
```

### 8.2 Иконки: Lucide

- `@lucide/svelte` 1.61.0 (peer `svelte ^5`), импорт поштучно: `import Cpu from '@lucide/svelte/icons/cpu'` (экспорт `./icons/*` есть) → в бандл попадают только используемые.
- Лицензия: **ISC** (© Lucide Icons and Contributors) **+ MIT** (© Cole Bemis) для иконок, происходящих из Feather (перечень в LICENSE пакета) → включить оба текста в `THIRD-PARTY-NOTICES.md`. В задании указано «MIT» — это неточно.
- Стиль: `stroke-width` 1.75–2, размер 18–20 px, `color: currentColor`.

### 8.3 Шрифты (OFL-1.1, локально)

| Роль | Шрифт | Файлы (subsets latin + cyrillic, variable wght) | Размер |
|---|---|---|---|
| UI | Inter Variable | `inter-latin-wght-normal.woff2`, `inter-cyrillic-wght-normal.woff2` | 48 256 + 18 748 Б |
| Цифры/моно | JetBrains Mono Variable | `jetbrains-mono-latin-…`, `…-cyrillic-…` | 40 404 + 12 108 Б |
| Логотип | Unbounded (или Geist) | **не грузить** — логотип как SVG-контуры (OFL разрешает) | 0 |

Итого ≈ 120 КБ. Файлы взять из `@fontsource-variable/inter@5.3.0` и `@fontsource-variable/jetbrains-mono@5.3.0` (`files/*.woff2`), положить в `ui/src/assets/fonts/` вместе с `OFL.txt` (детерминированно, без «магии» резолва `url()` из node_modules). `@font-face` (unicode-range — как в fontsource):

```css
/* ui/src/styles/fonts.css */
@font-face { font-family: "Inter Variable"; font-style: normal; font-weight: 100 900; font-display: block;
  src: url("../assets/fonts/inter-cyrillic-wght-normal.woff2") format("woff2-variations");
  unicode-range: U+0301, U+0400-045F, U+0490-0491, U+04B0-04B1, U+2116; }
@font-face { font-family: "Inter Variable"; font-style: normal; font-weight: 100 900; font-display: block;
  src: url("../assets/fonts/inter-latin-wght-normal.woff2") format("woff2-variations");
  unicode-range: U+0000-00FF, U+0131, U+0152-0153, U+02BB-02BC, U+02C6, U+02DA, U+02DC, U+0304, U+0308, U+0329, U+2000-206F, U+20AC, U+2122, U+2191, U+2193, U+2212, U+2215, U+FEFF, U+FFFD; }
/* JetBrains Mono Variable — те же два блока с файлами jetbrains-mono-*-wght-normal.woff2 */
```

`font-display: block` — локальные шрифты грузятся мгновенно, нет «прыжка» с системного шрифта. Fallback-стек: `"Inter Variable", "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif`.

---

## 9. Фронтенд-стек и структура

### 9.1 Сравнение

| Критерий | Без сборщика: ES-модули + Preact + htm | Vite + Preact 11 (+signals) | Vite + React 19 | **Vite + Svelte 5** |
|---|---|---|---|---|
| Типизация/проверка | нет (JS, шаблоны-строки htm) | TS/TSX | TS/TSX | TS + `svelte-check` |
| Изоляция стилей для 20 экранов | вручную | CSS Modules | CSS Modules | **scoped `<style>` из коробки** |
| Реактивность | хуки | хуки + signals | хуки | **runes (`$state/$derived/$effect`)**, без stale-closure ловушек хуков |
| Рантайм (gzip) | preact ≈5 КБ + htm ≈0,7 КБ | ≈5 КБ (+signals ≈2 КБ) | ≈60 КБ | компилируемый, малый |
| Зрелость версий | — | **Preact 11.0.0 вышел 2026-09-30** (свежий major) | стабильно | Svelte 5 стабилен с 2024, 5.57.2 |
| Знакомство агентов/LLM | среднее | высокое (React-подобно) | максимальное | высокое; риск смешивания синтаксиса Svelte 4 — ловится `runes: true` (ошибка компиляции) |
| Параллельная разработка | конфликты в общих файлах | норм. | норм. | **авто-регистрация экранов + scoped CSS** |

**Рекомендация: Vite 8.3.5 + Svelte 5.57.2 (runes) + TypeScript 6.0.3.** TS 7.0.2 (latest) пока **не брать**: `svelte-check@4.7.7` требует `typescript ^5 || ^6`. Запасной вариант — Vite + Preact (10.29.x или 11.x после пары патчей) + `@preact/signals`.

### 9.2 Версии (зафиксировать в `package.json`, с `package-lock.json`)

```json
{
  "name": "hateeze-ui",
  "private": true,
  "type": "module",
  "engines": { "node": ">=24" },
  "scripts": {
    "dev": "vite",
    "build": "svelte-check --fail-on-warnings && vite build",
    "check": "svelte-check",
    "test": "vitest run"
  },
  "dependencies": {
    "@lucide/svelte": "1.61.0",
    "uplot": "1.6.32"
  },
  "devDependencies": {
    "@sveltejs/vite-plugin-svelte": "7.3.1",
    "svelte": "5.57.2",
    "svelte-check": "4.7.7",
    "typescript": "6.0.3",
    "vite": "8.3.5",
    "vitest": "5.0.5"
  }
}
```

`vite.config.ts`:

```ts
import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import { fileURLToPath, URL } from 'node:url';

export default defineConfig({
  plugins: [svelte()],
  resolve: { alias: { $lib: fileURLToPath(new URL('./src/lib', import.meta.url)) } },
  base: '/',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    target: 'chrome120',             // WebView2 Evergreen — всегда свежий Chromium
    modulePreload: { polyfill: false }, // без inline-скрипта → совместимо с CSP script-src 'self'
    sourcemap: false,                // WebView2 не грузит внешние source maps при WebResourceRequested
    assetsInlineLimit: 0,            // шрифты/картинки — файлами (детерминированно)
  },
  server: { port: 5173, strictPort: true },
});
```

`svelte.config.js`:

```js
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';
export default { preprocess: vitePreprocess(), compilerOptions: { runes: true } }; // запрет синтаксиса Svelte 4
```

### 9.3 Структура репозитория

```
hateeze/
├─ hateeze.sln
├─ THIRD-PARTY-NOTICES.md              # MIT/ISC/OFL тексты: uPlot, Lucide(+Feather), Inter, JetBrains Mono, shuding/liquid-glass, rdev/liquid-glass-react
├─ src/
│  ├─ Hateeze.App/                     # WinForms-хост (exe)
│  │  ├─ Hateeze.App.csproj  app.manifest  Program.cs
│  │  ├─ Native/        Win32.cs  Dwm.cs  Authenticode.cs
│  │  ├─ Shell/         MainForm.cs  WindowChrome.cs  WebAssetServer.cs  WebView2Bootstrap.cs
│  │  │                 PanicHotkey.cs  ExternalLauncher.cs  SingleInstance.cs  AppPaths.cs
│  │  ├─ Bridge/        BridgeHost.cs  Contracts.cs  MetricsPump.cs
│  │  │  └─ Modules/    WindowModule.cs  AppModule.cs  MetricsModule.cs  TweaksModule.cs  DriverModule.cs …
│  │  └─ Resources/     app.ico
│  ├─ Hateeze.Core/                    # твики, снимки, откат, метрики (другие темы)
│  └─ Hateeze.Core.Tests/
├─ ui/
│  ├─ package.json  package-lock.json  vite.config.ts  svelte.config.js  tsconfig.json  index.html
│  └─ src/
│     ├─ main.ts                       # mount(App, { target: document.getElementById('app')! })
│     ├─ App.svelte                    # каркас: Background, TitleBar, ResizeEdges, Nav, <Router/>, Toasts
│     ├─ router.ts                     # hash-роутер + авто-регистрация экранов
│     ├─ styles/   tokens.css  base.css  glass.css  fonts.css  utilities.css
│     ├─ assets/fonts/   *.woff2  OFL.txt
│     ├─ lib/
│     │  ├─ bridge/   client.ts  contracts.ts  mock.ts
│     │  ├─ state/    window.svelte.ts  fx.svelte.ts  metrics.svelte.ts  tweaks.svelte.ts  toasts.svelte.ts
│     │  ├─ ui/       GlassPanel  Button  Switch  Slider  EvidenceBadge  DataTable  Tooltip  Modal  Toast
│     │  │            ProgressBar  TitleBar  ResizeEdges  Icon  EmptyState  ConfirmDialog   (*.svelte)
│     │  ├─ charts/   UPlotChart.svelte  Sparkline.svelte
│     │  ├─ fx/       Background.svelte  lens-core.ts  liquidLens.svelte.ts  sheen.ts
│     │  └─ i18n/     ru.ts            # общие строки интерфейса (тексты твиков приходят из C#-каталога)
│     └─ screens/                      # ~20 экранов, каждый — своя папка
│        ├─ dashboard/  meta.ts  Screen.svelte  (+ локальные компоненты)
│        ├─ latency/    meta.ts  Screen.svelte
│        ├─ cpu/ gpu/ power/ network/ input/ … experiment/ rollback/ settings/ about/
│        └─ _kit/       meta.ts  Screen.svelte   # витрина UI-кита (только DEV)
└─ docs/research/…
```

Авто-регистрация экранов (агенты **не правят** общий роутер):

```ts
// ui/src/router.ts
import type { Component } from 'svelte';
export interface ScreenMeta { id: string; title: string; section: 'overview'|'tweaks'|'experiment'|'system'; order: number; icon: string; devOnly?: boolean }

const metas = import.meta.glob<ScreenMeta>('./screens/*/meta.ts', { eager: true, import: 'default' });
const loaders = import.meta.glob<{ default: Component }>('./screens/*/Screen.svelte');

export const screens = Object.entries(metas)
  .map(([path, meta]) => ({ ...meta, load: loaders[path.replace('meta.ts', 'Screen.svelte')] }))
  .filter((s) => !s.devOnly || import.meta.env.DEV)
  .sort((a, b) => a.order - b.order);
```

```ts
// ui/src/screens/cpu/meta.ts
import type { ScreenMeta } from '../../router';
export default { id: 'cpu', title: 'Процессор', section: 'tweaks', order: 20, icon: 'cpu' } satisfies ScreenMeta;
```

### 9.4 Состояние и стрим метрик в Svelte 5

```ts
// ui/src/lib/state/metrics.svelte.ts
import { call, on } from '$lib/bridge/client';
import type { MetricsSample } from '$lib/bridge/contracts';

class Metrics {
  latest = $state.raw<MetricsSample | null>(null);   // .raw: без глубоких прокси, объект заменяется целиком
  #subs = 0; #off: (() => void) | null = null;

  async acquire(hz = 4) {                            // счётчик ссылок: первый подписчик включает поток
    if (this.#subs++ === 0) {
      this.#off = on('metrics.sample', (s) => (this.latest = s));
      await call('metrics.subscribe', { hz });
    }
  }
  async release() {
    if (--this.#subs === 0) { this.#off?.(); this.#off = null; await call('metrics.unsubscribe'); }
  }
}
export const metrics = new Metrics();
```

Правила для агентов:
- История/кольцевые буферы графиков — **вне** реактивности (`uPlot.setData`, typed arrays); `$state` на больших массивах с частыми push = лишние прокси и перерисовки.
- Экран при монтировании `metrics.acquire()`, при размонтировании `metrics.release()`.
- `window.svelte.ts` хранит `maximized/focused/visible` из события `window.state`, выставляет `document.documentElement.dataset.paused`.
- `fx.svelte.ts` — уровень эффектов (`full|low`), сохраняется через мост в настройках C# (не в `localStorage`).

### 9.5 Правила параллельной разработки (агентам)

1. Один экран = одна папка `screens/<id>/`; общие файлы (`router.ts`, `App.svelte`, `styles/*`, `lib/ui/*`, `contracts.ts`) — только через владельца «UI-кита».
2. Цвета/радиусы/тени — только токены из `tokens.css`; новые токены — через владельца.
3. Стекло — только компонентом `GlassPanel` (он знает про `data-fx` и бюджет); `backdrop-filter` руками в экранах запрещён.
4. Новые методы моста: сначала `contracts.ts` + `Contracts.cs` + запись в `docs/bridge-contract.md`, потом мок в `mock.ts`, потом экран. Изменения контрактов — только добавлением полей.
5. Все строки интерфейса — по-русски; термины («твик», «снимок», «откат», «доказательность: Проверено/Ситуативно/Эксперимент») — из `i18n/ru.ts`.
6. Новые npm-зависимости — только с согласованием (лицензия, размер, офлайн).
7. Каждый экран обязан работать на моке (`npm run dev` в обычном Edge/Chrome) — это позволяет разрабатывать UI без сборки C#.

### 9.6 Dev-цикл

- UI отдельно: `cd ui && npm run dev` → `http://localhost:5173` в Edge; мост — мок.
- UI в реальном хосте: Debug-сборка C# с `HATEEZE_UI_DEV=http://localhost:5173/` → хост навигирует на dev-сервер (HMR, DevTools, внешние source maps работают, т.к. это обычный http). Проверка `e.Source` в Debug разрешает этот origin (§3.4). Внимание: в **повышенном** процессе переменные `WEBVIEW2_*` игнорируются, но наша собственная `HATEEZE_UI_DEV` читается обычным `Environment.GetEnvironmentVariable` — это нормально.
- Release: `dotnet publish` вызывает `npm ci && npm run build`, `dist/**` встраивается в exe.

---

## 10. Тексты оболочки для интерфейса (RU)

| Ключ | Текст |
|---|---|
| `fx.title` | Упрощённые эффекты |
| `fx.description` | Отключает размытие «стекла» и анимацию фона. Интерфейс меньше нагружает видеокарту — полезно на ноутбуках, встроенной графике и во время игры. |
| `fx.recommendation` | Включите, если интерфейс подтормаживает или вы держите hateeze открытым во время игры. |
| `window.minimize` / `maximize` / `restore` / `close` | Свернуть / Развернуть / Восстановить / Закрыть |
| `crash.renderer` | Интерфейс перезапущен после сбоя. Применённые твики не затронуты. |
| `wv2.updated` | Обновился компонент WebView2. Перезапустите hateeze, чтобы применить обновление безопасности. |
| `hotkey.armed` | Нажмите Ctrl+Alt+Shift+R ещё раз в течение 3 секунд, чтобы откатить все твики и перезагрузить ПК. |
| `hotkey.running` | Откат твиков к снимку… Перезагрузка через 10 секунд. Отменить: Win+R → shutdown /a |
| `hotkey.busy` | Не удалось зарегистрировать Ctrl+Alt+Shift+R: сочетание занято другой программой. Аварийный откат доступен из меню в трее. |
| `bridge.E_CANCELLED` | Операция отменена |
| `bridge.E_TIMEOUT` | Превышено время ожидания |
| `bridge.E_ACCESS` | Недостаточно прав для операции |
| `bridge.E_FORBIDDEN` | Действие запрещено политикой безопасности hateeze |
| `bridge.E_INTERNAL` | Внутренняя ошибка. Подробности — в журнале. |
| `evidence.proven` | Проверено |
| `evidence.situational` | Ситуативно |
| `evidence.experimental` | Эксперимент |
| `external.open` | Ссылка откроется в браузере по умолчанию |

---

## 11. Риски, открытые вопросы, что не проверено

### 11.1 Риски

1. **Повышенный хост WebView2** против рекомендации Microsoft (§5.8) — главный архитектурный риск; митигации описаны, альтернатива — раздельные процессы.
2. **Извлечение нативных DLL single-file в `%TEMP%`** для админ-процесса (DLL planting) — закрываем loader'ом в Program Files; остаток зависит от того, какие ещё DLL извлекаются (проверить publish).
3. GPU-нагрузка стеклянного UI на мониторах 144–240 Гц во время игры — обязательна политика паузы (§7.4) и режим «Упрощённые эффекты».
4. Mica под прозрачным WebView2 в WinForms не проверена — поэтому не в v1.
5. `app-region: drag` внутри WinForms-контрола не проверен — есть фолбэк B.
6. Неподписанный повышенный single-file exe → SmartScreen/UAC «Неизвестный издатель», ложные срабатывания AV.
7. Свежие мажоры экосистемы (Preact 11, TypeScript 7, Vite 8/Rolldown) — пиннинг версий, TS 6.0.3.
8. Snap Layouts на кастомной кнопке «Развернуть» недоступны при HWND-хостинге — UX-ограничение.
9. CSP требует `style-src 'unsafe-inline'` (Svelte style-атрибуты) — приемлемо при отсутствии стороннего контента.

### 11.2 Открытые вопросы (решить владельцу проекта)

1. Остаёмся с «один повышенный процесс с WebView2» или делаем UI asInvoker + повышенный движок?
2. Финальное имя origin: `https://app.hateeze.example` (рекомендовано) — ок?
3. Svelte 5 (рекомендовано) или Preact — финальное решение лида фронтенда.
4. Паник-клавиша: двойное нажатие (рекомендовано) или одиночное + 10-секундная отменяемая перезагрузка? Перезагружать ли, если откат завершился ошибкой?
5. Нужна ли работа в трее + автозапуск (планировщик «с наивысшими правами», не SYSTEM)?
6. Mica как опция для Win11 — делать ли спайк после v1?
7. Сертификат подписи кода — кто и какой.
8. Минимальная поддерживаемая версия WebView2 Runtime (предложение: ≥ 120; ниже — предлагать обновление).

### 11.3 НЕ ПРОВЕРЕНО (и почему)

| Пункт | Почему не проверено |
|---|---|
| Весь C#-код (компиляция/запуск) | правила исследования: `dotnet new`/restore (установка пакетов в кэш NuGet) и запуск приложений запрещены |
| Итоговый размер exe и время старта | нет сборки |
| Поиск `WebView2Loader.dll` по умолчанию в single-file | нет сборки; вывод сделан по `build/Common.targets` пакета |
| `app-region: drag` во WinForms-контроле | нет запуска; подтверждено только для Chromium CSS и wry/Tauri (HWND-хостинг) |
| Прозрачный WebView2 + Mica в WinForms | нет запуска; подтверждено у Wails/Tauri (не WinForms) |
| Трюк `explorer.exe <url>` для неповышенного открытия ссылок | нет запуска |
| Коды выхода bootstrapper'а | не документированы публично |
| Hotkey в эксклюзивном полноэкранном режиме игр с античитом | нет игровых тестов |
| GPU-нагрузка UI в процентах | не измерялась (рендер проверялся во встроенном браузере Claude, не в WebView2) |
| CSS-фичи именно в WebView2 Runtime 150 | проверено в Chromium 152; расхождения для используемых фич не ожидаются |
| `X509Certificate.CreateFromSignedFile` в .NET 10 (не помечен ли устаревшим) | нет компиляции; предусмотрен `#pragma` |

---

## 12. Источники

WebView2 (Microsoft Learn / GitHub):
- Release notes SDK: https://learn.microsoft.com/en-us/microsoft-edge/webview2/release-notes/sdk/
- NuGet Microsoft.Web.WebView2: https://www.nuget.org/packages/Microsoft.Web.WebView2 ; API: https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/index.json
- IsNonClientRegionSupportEnabled: https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2settings.isnonclientregionsupportenabled
- ICoreWebView2Settings9 (1.0.2420.47): https://github.com/MicrosoftDocs/webview2-win32-reference/blob/live/1-0-2420-47/icorewebview2settings9.md
- CoreWebView2NonClientRegionKind: https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2nonclientregionkind
- CoreWebView2Controller.DefaultBackgroundColor: https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2controller.defaultbackgroundcolor
- CoreWebView2ControllerOptions: https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2controlleroptions
- WinForms WebView2.EnsureCoreWebView2Async: https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.winforms.webview2.ensurecorewebview2async
- Using local content: https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/working-with-local-content
- SetVirtualHostNameToFolderMapping: https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.setvirtualhostnametofoldermapping
- AddWebResourceRequestedFilter: https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.addwebresourcerequestedfilter
- CreateWebResourceResponse: https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2environment.createwebresourceresponse
- Stream disposal issue: https://github.com/MicrosoftEdge/WebView2Feedback/issues/2513
- Threading model (Reentrancy): https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/threading-model
- Develop secure WebView2 apps (elevation, overrides ignored): https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security
- User data folder: https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder
- Distribution / runtime detection / bootstrapper: https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution
- GetAvailableBrowserVersionString: https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2environment.getavailablebrowserversionstring
- SetLoaderDllFolderPath: https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2environment.setloaderdllfolderpath
- CoreWebView2EnvironmentOptions: https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2environmentoptions
- TrySuspendAsync: https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.trysuspendasync
- WPF WebView2CompositionControl / samples: https://github.com/MicrosoftEdge/WebView2Samples
- Evergreen Bootstrapper: https://go.microsoft.com/fwlink/p/?LinkId=2124703 ; ручная установка: https://developer.microsoft.com/microsoft-edge/webview2/consumer/

Windows / DWM / Win32:
- DWMWINDOWATTRIBUTE: https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
- Apply rounded corners: https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-rounded-corners
- Mica in Win32 apps: https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-mica-win32
- WindowsAppSDK Mica + WebView2 sample: https://github.com/microsoft/WindowsAppSDK-Samples/tree/main/Samples/Mica/cpp-WebView2
- RegisterHotKey: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey
- Unelevated launch (Raymond Chen): https://devblogs.microsoft.com/oldnewthing/20131118-00/?p=2643
- Form.FormCornerPreference: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.form.formcornerpreference

Реализации frameless/backdrop с WebView2/Chromium:
- Wails v2 window.go: https://github.com/wailsapp/wails/blob/master/v2/internal/frontend/desktop/windows/window.go
- Wails options (WebviewIsTransparent/WindowIsTranslucent/BackdropType): https://github.com/wailsapp/wails/blob/master/website/docs/reference/options.mdx
- tauri-apps/window-vibrancy: https://github.com/tauri-apps/window-vibrancy (src/windows.rs)
- wry issue/PR non-client region: https://github.com/tauri-apps/wry/issues/1245

.NET:
- Trimming incompatibilities (WinForms/WPF/COM): https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities
- Single-file deployment: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- WinForms .NET 6 project-level settings (ApplicationHighDpiMode default SystemAware): https://learn.microsoft.com/en-us/dotnet/desktop/winforms/whats-new/net60
- dotnet/sdk single-file native libs discussion: https://github.com/dotnet/sdk/issues/24181

CSS / liquid glass:
- MDN BCD backdrop-filter: https://github.com/mdn/browser-compat-data/blob/main/css/properties/backdrop-filter.json
- Filter Effects Level 2 (Backdrop Root): https://drafts.csswg.org/filter-effects-2/
- kube.io Liquid Glass (CSS+SVG): https://kube.io/blog/liquid-glass-css-svg/
- shuding/liquid-glass (MIT): https://github.com/shuding/liquid-glass
- rdev/liquid-glass-react (MIT): https://github.com/rdev/liquid-glass-react
- gentpan/liquidglass (MIT): https://github.com/gentpan/liquidglass
- rizroze/liquid-glass (MIT): https://github.com/rizroze/liquid-glass
- yichen08092025/liquid-glass (MIT): https://github.com/yichen08092025/liquid-glass
- dev.to «Liquid glass CSS…»: https://dev.to/vitalbadjo/liquid-glass-css-the-apple-liquid-glass-effect-with-css-only-plus-svg-refraction-1hkd
- BuildMVPFast «Liquid Glass on the Web…»: https://www.buildmvpfast.com/blog/liquid-glass-css-backdrop-filter-recipes-2026
- LogRocket «How to create liquid glass effects with CSS and SVG»: https://blog.logrocket.com/how-create-liquid-glass-effects-css-and-svg/

Библиотеки:
- uPlot: https://github.com/leeoniya/uPlot (LICENSE: MIT)
- Lucide: https://github.com/lucide-icons/lucide (LICENSE: ISC + MIT для Feather-иконок)
- Fontsource Inter / JetBrains Mono / Unbounded / Geist: https://fontsource.org , https://cdn.jsdelivr.net/npm/@fontsource-variable/inter@5.3.0/
- Svelte: https://svelte.dev ; Vite: https://vite.dev ; @sveltejs/vite-plugin-svelte: https://github.com/sveltejs/vite-plugin-svelte
