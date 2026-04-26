using System.Text.Json;
using System.Text.RegularExpressions;

namespace ABHive.Web;

public sealed class VersionManifest
{
    public string AppName { get; set; } = "Agentic BeeHive";
    public string Version { get; set; } = "0.0.0";
    public string Channel { get; set; } = "stable";
    public string UpdateManifestUrl { get; set; } = "";
    public string ReleaseNotesUrl { get; set; } = "";
    public string PublishedAtUtc { get; set; } = "";
    public Dictionary<string, string> Assets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class VersionCheckResult
{
    public string CurrentVersion { get; set; } = "0.0.0";
    public string LatestVersion { get; set; } = "0.0.0";
    public bool UpdateAvailable { get; set; }
    public string ReleaseNotesUrl { get; set; } = "";
    public string? Error { get; set; }
}

public sealed class VersionService
{
    private readonly AppSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _manifestPathOverride;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RemoteTimeout = TimeSpan.FromSeconds(5);

    private DateTimeOffset? _cachedAt;
    private VersionCheckResult? _cachedCheck;

    public VersionService(AppSettings settings, IHttpClientFactory httpClientFactory, string? manifestPathOverride = null)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _manifestPathOverride = manifestPathOverride;
    }

    public VersionManifest GetLocalVersionManifest()
    {
        var manifestPath = FindVersionManifestPath();
        if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
        {
            try
            {
                var json = File.ReadAllText(manifestPath);
                var parsed = ParseVersionManifest(json);
                if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Version))
                {
                    parsed.AppName = string.IsNullOrWhiteSpace(parsed.AppName) ? "Agentic BeeHive" : parsed.AppName.Trim();
                    parsed.Channel = string.IsNullOrWhiteSpace(parsed.Channel) ? "stable" : parsed.Channel.Trim();
                    parsed.Version = parsed.Version.Trim();
                    parsed.UpdateManifestUrl = parsed.UpdateManifestUrl?.Trim() ?? "";
                    parsed.ReleaseNotesUrl = parsed.ReleaseNotesUrl?.Trim() ?? "";
                    parsed.PublishedAtUtc = parsed.PublishedAtUtc?.Trim() ?? "";
                    return parsed;
                }
            }
            catch
            {
                // Fall through to fallback manifest.
            }
        }

        return new VersionManifest
        {
            Version = string.IsNullOrWhiteSpace(_settings.CurrentVersion) ? "0.0.0" : _settings.CurrentVersion.Trim(),
            Channel = "stable"
        };
    }

    public async Task<VersionCheckResult> GetVersionCheckAsync(CancellationToken ct = default)
    {
        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_cachedCheck != null && _cachedAt.HasValue && DateTimeOffset.UtcNow - _cachedAt.Value < CacheDuration)
            {
                return _cachedCheck;
            }
        }
        finally
        {
            _cacheLock.Release();
        }

        var local = GetLocalVersionManifest();
        var result = new VersionCheckResult
        {
            CurrentVersion = string.IsNullOrWhiteSpace(local.Version) ? "0.0.0" : local.Version.Trim(),
            LatestVersion = string.IsNullOrWhiteSpace(local.Version) ? "0.0.0" : local.Version.Trim(),
            UpdateAvailable = false,
            ReleaseNotesUrl = local.ReleaseNotesUrl ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(local.UpdateManifestUrl))
        {
            result.Error = "No update manifest URL configured.";
            await CacheResultAsync(result, ct);
            return result;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(RemoteTimeout);

            var client = _httpClientFactory.CreateClient();
            using var response = await client.GetAsync(local.UpdateManifestUrl, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                result.Error = $"Remote manifest request failed with HTTP {(int)response.StatusCode}.";
                await CacheResultAsync(result, ct);
                return result;
            }

            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var remote = ParseVersionManifest(json);

            if (remote == null || string.IsNullOrWhiteSpace(remote.Version))
            {
                result.Error = "Remote manifest is missing a valid version.";
                await CacheResultAsync(result, ct);
                return result;
            }

            result.LatestVersion = remote.Version.Trim();
            result.ReleaseNotesUrl = string.IsNullOrWhiteSpace(remote.ReleaseNotesUrl)
                ? result.ReleaseNotesUrl
                : remote.ReleaseNotesUrl.Trim();

            if (!SemVersion.TryParse(result.CurrentVersion, out var current))
            {
                result.Error = $"Current version '{result.CurrentVersion}' is not valid SemVer.";
                await CacheResultAsync(result, ct);
                return result;
            }

            if (!SemVersion.TryParse(result.LatestVersion, out var latest))
            {
                result.Error = $"Latest version '{result.LatestVersion}' is not valid SemVer.";
                await CacheResultAsync(result, ct);
                return result;
            }

            result.UpdateAvailable = latest.CompareTo(current) > 0;
            await CacheResultAsync(result, ct);
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Error = "Update check timed out.";
            await CacheResultAsync(result, ct);
            return result;
        }
        catch (Exception ex)
        {
            result.Error = $"Update check failed: {ex.Message}";
            await CacheResultAsync(result, ct);
            return result;
        }
    }

    private async Task CacheResultAsync(VersionCheckResult result, CancellationToken ct)
    {
        await _cacheLock.WaitAsync(ct);
        try
        {
            _cachedCheck = result;
            _cachedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static VersionManifest? ParseVersionManifest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<VersionManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    private static string? FindSolutionRootFrom(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return null;
        }

        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            var solutionPath = Path.Combine(directory.FullName, "ABHive.sln");
            if (File.Exists(solutionPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private string? FindVersionManifestPath()
    {
        if (!string.IsNullOrWhiteSpace(_manifestPathOverride) && File.Exists(_manifestPathOverride))
        {
            return _manifestPathOverride;
        }

        var directCandidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "version.json"),
            Path.Combine(AppContext.BaseDirectory, "version.json")
        };

        foreach (var directCandidate in directCandidates)
        {
            if (File.Exists(directCandidate))
            {
                return directCandidate;
            }
        }

        var roots = new[]
        {
            FindSolutionRootFrom(Directory.GetCurrentDirectory()),
            FindSolutionRootFrom(AppContext.BaseDirectory)
        };

        foreach (var root in roots.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(root!, "version.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

public readonly struct SemVersion : IComparable<SemVersion>
{
    private static readonly Regex Pattern = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string PreRelease { get; }

    private SemVersion(int major, int minor, int patch, string preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    public static bool TryParse(string? value, out SemVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = Pattern.Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, out var major) ||
            !int.TryParse(match.Groups[2].Value, out var minor) ||
            !int.TryParse(match.Groups[3].Value, out var patch))
        {
            return false;
        }

        version = new SemVersion(major, minor, patch, match.Groups[4].Value);
        return true;
    }

    public int CompareTo(SemVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;

        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0) return minor;

        var patch = Patch.CompareTo(other.Patch);
        if (patch != 0) return patch;

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    private static int ComparePreRelease(string left, string right)
    {
        var leftEmpty = string.IsNullOrWhiteSpace(left);
        var rightEmpty = string.IsNullOrWhiteSpace(right);
        if (leftEmpty && rightEmpty) return 0;
        if (leftEmpty) return 1;
        if (rightEmpty) return -1;

        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var count = Math.Max(leftParts.Length, rightParts.Length);
        for (var index = 0; index < count; index++)
        {
            if (index >= leftParts.Length) return -1;
            if (index >= rightParts.Length) return 1;

            var leftPart = leftParts[index];
            var rightPart = rightParts[index];
            var leftNumeric = int.TryParse(leftPart, out var leftNumber);
            var rightNumeric = int.TryParse(rightPart, out var rightNumber);

            if (leftNumeric && rightNumeric)
            {
                var numberCompare = leftNumber.CompareTo(rightNumber);
                if (numberCompare != 0) return numberCompare;
                continue;
            }

            if (leftNumeric && !rightNumeric) return -1;
            if (!leftNumeric && rightNumeric) return 1;

            var textCompare = string.Compare(leftPart, rightPart, StringComparison.Ordinal);
            if (textCompare != 0) return textCompare;
        }

        return 0;
    }
}
