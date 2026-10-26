using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Hateeze.Net;

public static class Http
{
    /// <summary>
    /// ВАЖНО: всегда задавать собственный User-Agent.
    ///  - api.github.com отклоняет запросы без User-Agent;
    ///  - download.wagnardsoft.com (Cloudflare) отдаёт 403 "challenge" на пустой UA и на UA "curl/x.y",
    ///    а на "hateeze/1.0" и на браузерный UA — 200 (проверено 2026-10-26).
    /// </summary>
    public static string UserAgent { get; set; } = "hateeze/1.0 (+https://github.com/OWNER/REPO)";

    /// <summary>Один экземпляр на всё приложение (не создавать HttpClient на каждый запрос).</summary>
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),       // медленный/фильтруемый канал в РФ: не ждать 100 c по умолчанию
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,                        // GitHub assets: github.com -> release-assets.githubusercontent.com
            MaxAutomaticRedirections = 10,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            UseProxy = true,                                 // системный прокси (VPN/прокси-клиенты пользователя)
            Proxy = HttpClient.DefaultProxy,
        };
        var c = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }; // таймауты — через CancellationToken
        c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return c;
    }

    public sealed record DownloadResult(string Path, long Bytes, string Sha256Hex);

    /// <summary>
    /// Скачивание с докачкой (Range), детектором "зависания" (нет байтов N секунд) и подсчётом SHA-256 на лету.
    /// Файл пишется во временный *.part и переименовывается только после успешного завершения.
    /// </summary>
    public static async Task<DownloadResult> DownloadAsync(
        Uri url, string targetPath, IProgress<(long done, long? total)>? progress,
        TimeSpan stallTimeout, CancellationToken ct, int maxAttempts = 4)
    {
        string part = targetPath + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetPath))!);
        Exception? last = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                long existing = File.Exists(part) ? new FileInfo(part).Length : 0;
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (existing > 0) req.Headers.Range = new RangeHeaderValue(existing, null);

                using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stallCts.CancelAfter(TimeSpan.FromSeconds(30)); // ожидание заголовков ответа
                using var resp = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stallCts.Token);

                if (existing > 0 && resp.StatusCode == HttpStatusCode.OK) existing = 0;          // сервер не поддержал Range
                else if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) { File.Delete(part); continue; }
                resp.EnsureSuccessStatusCode();

                long? total = resp.Content.Headers.ContentLength is long len ? len + existing : null;
                await using (var fs = new FileStream(part, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
                await using (var net = await resp.Content.ReadAsStreamAsync(ct))
                {
                    var buf = new byte[1 << 16];
                    long done = existing;
                    while (true)
                    {
                        stallCts.CancelAfter(stallTimeout);          // каждый блок продлевает "окно"
                        int n = await net.ReadAsync(buf, stallCts.Token);
                        if (n == 0) break;
                        await fs.WriteAsync(buf.AsMemory(0, n), ct);
                        done += n;
                        progress?.Report((done, total));
                    }
                    if (total is long t && done != t) throw new IOException($"Неполная загрузка: {done} из {t} байт");
                }
                string sha = await Sha256FileAsync(part, ct);
                File.Move(part, targetPath, overwrite: true);
                return new DownloadResult(targetPath, new FileInfo(targetPath).Length, sha);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 << attempt)), ct);
            }
        }
        throw new IOException($"Не удалось скачать {url}: {last?.Message}", last);
    }

    /// <summary>Человекочитаемая причина сетевой ошибки (для UI на русском). HttpRequestError — .NET 8+.</summary>
    public static string DescribeRu(Exception ex, string host) => ex switch
    {
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } =>
            $"Не удалось найти сервер {host} (ошибка DNS). Возможна блокировка провайдером или DNS-фильтром (AdGuard/NextDNS/Pi-hole).",
        HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } =>
            $"Не удалось установить защищённое соединение с {host}. Часто это признак блокировки (DPI) — попробуйте VPN или зеркало.",
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError } =>
            $"Сервер {host} не отвечает (соединение сброшено/отклонено).",
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } =>
            $"{host} отказал в доступе (403). Возможна гео-блокировка или защита от ботов.",
        HttpRequestException { StatusCode: (HttpStatusCode)451 } =>
            $"{host}: доступ ограничен по требованию властей (451).",
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } =>
            $"Файл на {host} не найден (404) — вероятно, ссылка устарела; обновите каталог приложений.",
        TaskCanceledException or TimeoutException or OperationCanceledException =>
            $"{host} слишком долго не отвечает (таймаут). Проверьте интернет/VPN.",
        _ => $"Ошибка сети при обращении к {host}: {ex.Message}",
    };

    public static async Task<string> Sha256FileAsync(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, true);
        var hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexStringLower(hash);
    }
}
