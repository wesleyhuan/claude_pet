using ClaudePet.Services;

namespace ClaudePet.Tests;

public class SubscriptionCredentialReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ClaudePetCredentialTests_" + Guid.NewGuid());

    public SubscriptionCredentialReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string FilePath => Path.Combine(_dir, "credentials.json");

    private void WriteFile(string json) => File.WriteAllText(FilePath, json);

    [Fact]
    public void TryRead_FileDoesNotExist_ReturnsNullAndReportsMissing()
    {
        string? reportedError = null;
        var reader = new SubscriptionCredentialReader(FilePath, err => reportedError = err);

        var result = reader.TryRead();

        Assert.Null(result);
        Assert.Equal("credential file missing", reportedError);
    }

    [Fact]
    public void TryRead_ValidSchema_ReturnsTokenAndExpiry()
    {
        WriteFile("""{"claudeAiOauth":{"accessToken":"sk-ant-oat01-test","refreshToken":"sk-ant-ort01-test","expiresAt":1748276587173,"scopes":["user:inference"]}}""");
        var reader = new SubscriptionCredentialReader(FilePath);

        var result = reader.TryRead();

        Assert.NotNull(result);
        Assert.Equal("sk-ant-oat01-test", result!.Value.AccessToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1748276587173), result.Value.ExpiresAt);
    }

    [Fact]
    public void TryRead_MalformedJson_ReturnsNullAndReportsError()
    {
        WriteFile("{ not valid json ");
        string? reportedError = null;
        var reader = new SubscriptionCredentialReader(FilePath, err => reportedError = err);

        var result = reader.TryRead();

        Assert.Null(result);
        Assert.Equal("credential file malformed", reportedError);
    }

    [Fact]
    public void TryRead_MissingAccessTokenField_ReturnsNull()
    {
        WriteFile("""{"claudeAiOauth":{"expiresAt":1748276587173}}""");
        var reader = new SubscriptionCredentialReader(FilePath);

        var result = reader.TryRead();

        Assert.Null(result);
    }

    [Fact]
    public void TryRead_MissingExpiresAtField_ReturnsNull()
    {
        WriteFile("""{"claudeAiOauth":{"accessToken":"sk-ant-oat01-test"}}""");
        var reader = new SubscriptionCredentialReader(FilePath);

        var result = reader.TryRead();

        Assert.Null(result);
    }

    [Fact]
    public void TryRead_MissingClaudeAiOauthWrapper_ReturnsNull()
    {
        WriteFile("""{"someOtherField":true}""");
        var reader = new SubscriptionCredentialReader(FilePath);

        var result = reader.TryRead();

        Assert.Null(result);
    }

    [Fact]
    public void TryRead_EmptyAccessToken_ReturnsNull()
    {
        WriteFile("""{"claudeAiOauth":{"accessToken":"","expiresAt":1748276587173}}""");
        var reader = new SubscriptionCredentialReader(FilePath);

        var result = reader.TryRead();

        Assert.Null(result);
    }
}
