using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Hateeze.Net;

namespace Hateeze.Update;

public sealed record UpdateInfo(SemVer Version, string Tag, Uri AssetUrl, string AssetName, long? Size, string? Sha256, string? NotesMarkdown, string Source);

public enum UpdateCheckStatus { UpToDate, UpdateAvailable, RateLimited, Unreachable, Error }

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Info, string MessageRu, DateTimeOffset? RetryAfterUtc = null);

/// <summary>
/// Проверка обновлений:
///  1) https://github.com/{owner}/{repo}/releases/latest/download/latest.json  — НЕ расходует лимит REST API (60/час на IP);
///  2) REST API /repos/{owner}/{repo}/releases/latest (с ETag-кэшем);
///  3) зеркало (свой CDN/хостинг) с тем же latest.json — на случай проблем GitHub в РФ.
/// </summary>
public sealed partial class GitHubUpdater
{
    private readonly string _owner, _repo;
    private readonly SemVer _current;
    private readonly Uri? _mirrorLatestJson;
    private readonly string _cacheDir;

    public GitHubUpdater(string owner, string repo, SemVer current, Uri? mirrorLatestJson, string cacheDir)
    { _owner = owner; _repo = repo; _current = current; _mirrorLatestJson = mirrorLatestJson; _cacheDir = cacheDir; }

    public static string AssetName(SemVer v) => $"hateeze-{v}-win-x64.exe";

    // ---- формат latest.json (публикуется ассетом в КАЖДОМ релизе под фиксированным именем) ----
    public sealed record LatestJson(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("tag")] string Tag,
        [property: JsonPropertyName("asset")] string Asset,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("mirrors")] string[]? Mirrors,
        [property: JsonPropertyName("notes_ru")] string? NotesRu,
        [property: JsonPropertyName("min_os_build")] int? MinOsBuild,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt);

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
    {
        var errors = new List<string>();

        // 1) latest.json через github.com (redirect на release-assets.githubusercontent.com)
        var r = await TryLatestJsonAsync(new Uri($"https://github.com/{_owner}/{_repo}/releases/latest/download/latest.json"), "github-latest.json", errors, ct);
        if (r is not null) return r;

        // 2) REST API
        r = await TryApiAsync(errors, ct);
        if (r is not null && r.Status != UpdateCheckStatus.RateLimited) return r;
        var rateLimited = r;

        // 3) зеркало
        if (_mirrorLatestJson is not null)
        {
            var m = await TryLatestJsonAsync(_mirrorLatestJson, "mirror", errors, ct);
            if (m is not null) return m;
        }

        if (rateLimited is not null) return rateLimited;
        return new UpdateCheckResult(UpdateCheckStatus.Unreachable, null,
            "Не удалось проверить обновления: GitHub не отвечает (таймаут или блокировка). " +
            "Проверьте интернет/VPN или скачайте новую версию вручную со страницы релизов.\n" + string.Join("\n", errors));
    }

    private async Task<UpdateCheckResult?> TryLatestJsonAsync(Uri url, string source, List<string> errors, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            using var resp = await Http.Client.GetAsync(url, cts.Token);
            if (resp.StatusCode == HttpStatusCode.NotFound) { errors.Add($"{source}: 404"); return null; }
            resp.EnsureSuccessStatusCode();
            var lj = await resp.Content.ReadFromJsonAsync<LatestJson>(cancellationToken: cts.Token)
                     ?? throw new InvalidDataException("пустой latest.json");
            if (!SemVer.TryParse(lj.Version, out var v)) throw new InvalidDataException($"плохая версия '{lj.Version}'");
            if (!Sha256Regex().IsMatch(lj.Sha256)) throw new InvalidDataException("плохой sha256");
            var assetUrl = lj.Url is { Length: > 0 } u ? new Uri(u)
                : new Uri($"https://github.com/{_owner}/{_repo}/releases/download/{Uri.EscapeDataString(lj.Tag)}/{Uri.EscapeDataString(lj.Asset)}");
            var info = new UpdateInfo(v, lj.Tag, assetUrl, lj.Asset, lj.Size, lj.Sha256.ToLowerInvariant(), lj.NotesRu, source);
            return Decide(info);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            errors.Add($"{source}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // ---- REST API ----
    private sealed record GhAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
        [property: JsonPropertyName("digest")] string? Digest);   // "sha256:<hex>" (GitHub считает сам при загрузке ассета)

    private sealed record GhRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("assets")] GhAsset[] Assets);

    private async Task<UpdateCheckResult?> TryApiAsync(List<string> errors, CancellationToken ct)
    {
        string etagFile = Path.Combine(_cacheDir, "gh-latest.etag"), bodyFile = Path.Combine(_cacheDir, "gh-latest.json");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            req.Headers.Add("X-GitHub-Api-Version", "2022-11-28"); // поддерживается до 2028-03-10; новая — 2026-03-10
            if (File.Exists(etagFile) && File.Exists(bodyFile))
                req.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(File.ReadAllText(etagFile)));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            using var resp = await Http.Client.SendAsync(req, cts.Token);

            if ((resp.StatusCode == HttpStatusCode.Forbidden || (int)resp.StatusCode == 429) &&
                resp.Headers.TryGetValues("x-ratelimit-remaining", out var rem) && rem.FirstOrDefault() == "0")
            {
                DateTimeOffset? reset = resp.Headers.TryGetValues("x-ratelimit-reset", out var rs) && long.TryParse(rs.FirstOrDefault(), out var epoch)
                    ? DateTimeOffset.FromUnixTimeSeconds(epoch) : null;
                return new UpdateCheckResult(UpdateCheckStatus.RateLimited, null,
                    $"GitHub временно ограничил число запросов с вашего IP (лимит 60 в час). Повторите после {reset?.ToLocalTime():HH:mm}.", reset);
            }

            string json;
            if (resp.StatusCode == HttpStatusCode.NotModified) json = await File.ReadAllTextAsync(bodyFile, ct);
            else
            {
                resp.EnsureSuccessStatusCode();
                json = await resp.Content.ReadAsStringAsync(cts.Token);
                Directory.CreateDirectory(_cacheDir);
                if (resp.Headers.ETag is { } etag) { await File.WriteAllTextAsync(etagFile, etag.ToString(), ct); await File.WriteAllTextAsync(bodyFile, json, ct); }
            }

            var rel = JsonSerializer.Deserialize<GhRelease>(json) ?? throw new InvalidDataException("пустой ответ API");
            if (rel.Draft || rel.Prerelease) throw new InvalidDataException("latest вернул draft/prerelease");
            if (!SemVer.TryParse(rel.TagName, out var v)) throw new InvalidDataException($"тег '{rel.TagName}' не SemVer");
            var name = AssetName(v);
            var asset = rel.Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException($"в релизе нет ассета {name}");

            string? sha = null;
            if (asset.Digest is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) sha = d[7..].ToLowerInvariant();
            sha ??= await TryFetchSidecarShaAsync(rel.Assets, name, ct);   // hateeze-x.y.z-win-x64.exe.sha256
            sha ??= TryParseShaFromBody(rel.Body, name);                     // строка "SHA256: <hex>" в описании релиза
            if (sha is null) throw new InvalidDataException("не найден SHA-256 для ассета — обновление запрещено");

            return Decide(new UpdateInfo(v, rel.TagName, new Uri(asset.BrowserDownloadUrl), asset.Name, asset.Size, sha, rel.Body, "github-api"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or FormatException)
        {
            errors.Add($"github-api: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> TryFetchSidecarShaAsync(GhAsset[] assets, string name, CancellationToken ct)
    {
        var side = assets.FirstOrDefault(a => a.Name.Equals(name + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (side is null) return null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        var text = await Http.Client.GetStringAsync(side.BrowserDownloadUrl, cts.Token);
        var m = Sha256Regex().Match(text);   // формат sha256sum: "<hex>  <file>" или просто "<hex>"
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    private static string? TryParseShaFromBody(string? body, string name)
    {
        if (string.IsNullOrEmpty(body)) return null;
        foreach (var line in body.Split('\n'))
            if (line.Contains(name, StringComparison.OrdinalIgnoreCase) || line.Contains("SHA256", StringComparison.OrdinalIgnoreCase))
            {
                var m = Sha256Regex().Match(line);
                if (m.Success) return m.Value.ToLowerInvariant();
            }
        return null;
    }

    private UpdateCheckResult Decide(UpdateInfo info) =>
        info.Version > _current
            ? new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, info, $"Доступна новая версия {info.Version} (у вас {_current}).")
            : new UpdateCheckResult(UpdateCheckStatus.UpToDate, info, $"У вас последняя версия ({_current}).");

    [GeneratedRegex(@"\b[0-9a-fA-F]{64}\b")]
    private static partial Regex Sha256Regex();

    /// <summary>
    /// Резервный способ узнать тег без API: GET https://github.com/{o}/{r}/releases/latest отвечает 302
    /// Location: https://github.com/{o}/{r}/releases/tag/{tag}. (Проверено 2026-10-26 на obsproject/obs-studio.)
    /// </summary>
    public static async Task<string?> GetLatestTagViaRedirectAsync(string owner, string repo, CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(10) };
        using var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(Http.UserAgent);
        using var resp = await c.GetAsync($"https://github.com/{owner}/{repo}/releases/latest", HttpCompletionOption.ResponseHeadersRead, ct);
        var loc = resp.Headers.Location?.ToString();
        var m = loc is null ? null : Regex.Match(loc, @"/releases/tag/([^/?#]+)$");
        return m is { Success: true } ? Uri.UnescapeDataString(m.Groups[1].Value) : null;
    }
}
