using System.Net;
using System.Text;
using ABHive.Web;

namespace ABHive.Tests;

public class VersionServiceTests
{
    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("1.2.3-alpha.1", true)]
    [InlineData("1.2.3+build.5", true)]
    [InlineData("1.2", false)]
    [InlineData("1.2.3.4", false)]
    [InlineData("x.y.z", false)]
    public void SemVersion_TryParse_HandlesExpectedFormats(string value, bool expected)
    {
        var success = SemVersion.TryParse(value, out _);
        Assert.Equal(expected, success);
    }

    [Theory]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0-alpha", "1.0.0", -1)]
    [InlineData("2.0.0", "10.0.0", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11", -1)]
    public void SemVersion_CompareTo_OrdersVersions(string left, string right, int sign)
    {
        Assert.True(SemVersion.TryParse(left, out var leftVersion));
        Assert.True(SemVersion.TryParse(right, out var rightVersion));

        var compare = Math.Sign(leftVersion.CompareTo(rightVersion));
        Assert.Equal(sign, compare);
    }

    [Fact]
    public async Task GetVersionCheckAsync_WhenRemoteIsNewer_ReturnsUpdateAvailable()
    {
        using var fixture = new VersionFixture("1.2.3", """
            {
              "version":"1.3.0",
              "releaseNotesUrl":"https://example.com/releases/v1.3.0"
            }
            """);

        var service = fixture.CreateService();
        var result = await service.GetVersionCheckAsync();

        Assert.True(result.UpdateAvailable);
        Assert.Equal("1.2.3", result.CurrentVersion);
        Assert.Equal("1.3.0", result.LatestVersion);
        Assert.Equal("https://example.com/releases/v1.3.0", result.ReleaseNotesUrl);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task GetVersionCheckAsync_WhenRemoteIsSameVersion_ReturnsNoUpdate()
    {
        using var fixture = new VersionFixture("1.2.3", """
            {
              "version":"1.2.3",
              "releaseNotesUrl":"https://example.com/releases/v1.2.3"
            }
            """);

        var service = fixture.CreateService();
        var result = await service.GetVersionCheckAsync();

        Assert.False(result.UpdateAvailable);
        Assert.Equal("1.2.3", result.CurrentVersion);
        Assert.Equal("1.2.3", result.LatestVersion);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task GetVersionCheckAsync_WhenRemoteManifestIsMalformed_ReturnsErrorWithoutCrash()
    {
        using var fixture = new VersionFixture("1.2.3", "{ invalid json");
        var service = fixture.CreateService();

        var result = await service.GetVersionCheckAsync();

        Assert.False(result.UpdateAvailable);
        Assert.Equal("1.2.3", result.CurrentVersion);
        Assert.Equal("1.2.3", result.LatestVersion);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task GetVersionCheckAsync_WhenRemoteVersionIsInvalid_ReturnsErrorWithoutCrash()
    {
        using var fixture = new VersionFixture("1.2.3", """
            {
              "version":"latest"
            }
            """);
        var service = fixture.CreateService();

        var result = await service.GetVersionCheckAsync();

        Assert.False(result.UpdateAvailable);
        Assert.Equal("1.2.3", result.CurrentVersion);
        Assert.Equal("latest", result.LatestVersion);
        Assert.Contains("not valid SemVer", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class VersionFixture : IDisposable
    {
        private readonly string _manifestPath;
        private readonly string _tempDir;
        private readonly string _remoteBody;

        public VersionFixture(string localVersion, string remoteBody)
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"abhive-version-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);

            _manifestPath = Path.Combine(_tempDir, "version.json");
            _remoteBody = remoteBody;

            File.WriteAllText(_manifestPath, $$"""
                {
                  "appName": "Agentic BeeHive",
                  "version": "{{localVersion}}",
                  "channel": "stable",
                  "updateManifestUrl": "https://updates.example.com/version.json",
                  "releaseNotesUrl": "https://example.com/releases"
                }
                """);
        }

        public VersionService CreateService()
        {
            var settings = new AppSettings
            {
                CurrentVersion = "0.0.0"
            };

            var factory = new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_remoteBody, Encoding.UTF8, "application/json")
            });

            return new VersionService(settings, factory, _manifestPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _client = new HttpClient(new StubHttpMessageHandler(responseFactory));
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
