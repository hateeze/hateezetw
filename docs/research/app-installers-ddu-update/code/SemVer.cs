using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Hateeze.Update;

/// <summary>
/// Минимальная реализация SemVer 2.0.0 (https://semver.org/#spec-item-11):
/// MAJOR.MINOR.PATCH[-prerelease][+build]. Build-метаданные игнорируются при сравнении.
/// Допускается префикс "v"/"V" у тега (v1.2.3).
/// </summary>
public sealed partial class SemVer : IComparable<SemVer>, IEquatable<SemVer>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public IReadOnlyList<string> Prerelease { get; }
    public string Build { get; }

    private SemVer(int major, int minor, int patch, IReadOnlyList<string> pre, string build)
    { Major = major; Minor = minor; Patch = patch; Prerelease = pre; Build = build; }

    public bool IsPrerelease => Prerelease.Count > 0;

    [GeneratedRegex(@"^[vV]?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$")]
    private static partial Regex SemVerRegex();

    public static bool TryParse(string? s, [NotNullWhen(true)] out SemVer? v)
    {
        v = null;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var m = SemVerRegex().Match(s.Trim());
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups[1].Value, out var a) ||
            !int.TryParse(m.Groups[2].Value, out var b) ||
            !int.TryParse(m.Groups[3].Value, out var c)) return false;
        var pre = m.Groups[4].Success ? m.Groups[4].Value.Split('.') : Array.Empty<string>();
        v = new SemVer(a, b, c, pre, m.Groups[5].Success ? m.Groups[5].Value : "");
        return true;
    }

    public static SemVer Parse(string s) =>
        TryParse(s, out var v) ? v : throw new FormatException($"Not a SemVer: '{s}'");

    public int CompareTo(SemVer? other)
    {
        if (other is null) return 1;
        int c = Major.CompareTo(other.Major); if (c != 0) return c;
        c = Minor.CompareTo(other.Minor); if (c != 0) return c;
        c = Patch.CompareTo(other.Patch); if (c != 0) return c;
        // Версия без prerelease СТАРШЕ версии с prerelease (1.0.0 > 1.0.0-rc.1)
        if (!IsPrerelease && other.IsPrerelease) return 1;
        if (IsPrerelease && !other.IsPrerelease) return -1;
        for (int i = 0; i < Math.Min(Prerelease.Count, other.Prerelease.Count); i++)
        {
            string x = Prerelease[i], y = other.Prerelease[i];
            bool xn = int.TryParse(x, out var xi), yn = int.TryParse(y, out var yi);
            if (xn && yn) { c = xi.CompareTo(yi); }
            else if (xn) { c = -1; }          // числовые идентификаторы младше буквенных
            else if (yn) { c = 1; }
            else { c = string.CompareOrdinal(x, y); }
            if (c != 0) return c;
        }
        return Prerelease.Count.CompareTo(other.Prerelease.Count);
    }

    public bool Equals(SemVer? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemVer o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', Prerelease));
    public static bool operator ==(SemVer? a, SemVer? b) => a is null ? b is null : a.Equals(b);
    public static bool operator !=(SemVer? a, SemVer? b) => !(a == b);
    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;
    public static bool operator >=(SemVer a, SemVer b) => a.CompareTo(b) >= 0;
    public static bool operator <=(SemVer a, SemVer b) => a.CompareTo(b) <= 0;

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (IsPrerelease ? "-" + string.Join('.', Prerelease) : "") + (Build.Length > 0 ? "+" + Build : "");
}
