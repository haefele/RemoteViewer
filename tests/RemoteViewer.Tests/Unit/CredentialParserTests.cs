using RemoteViewer.Client.Services;

namespace RemoteViewer.Tests.Unit;

public class CredentialParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void TryParse_NullOrWhitespace_ReturnsNullTuple(string? input)
    {
        var (id, password) = CredentialParser.TryParse(input);

        id.Should().BeNull();
        password.Should().BeNull();
    }

    [Theory]
    [InlineData("ID: 1234567890\nPassword: abc123", "1234567890", "abc123")]
    [InlineData("id: 1234567890\npassword: abc123", "1234567890", "abc123")]
    [InlineData("ID: 1234567890\r\nPassword: abc123", "1234567890", "abc123")]
    [InlineData("ID=1234567890\nPassword=abc123", "1234567890", "abc123")]
    [InlineData("ID-1234567890\nPassword-abc123", "1234567890", "abc123")]
    [InlineData("id:1234567890\npass:abc123", "1234567890", "abc123")]
    [InlineData("id:1234567890\npwd:abc123", "1234567890", "abc123")]
    public void TryParse_LabeledFormat_ExtractsCredentials(string input, string expectedId, string expectedPassword)
    {
        var (id, password) = CredentialParser.TryParse(input);

        id.Should().Be(expectedId);
        password.Should().Be(expectedPassword);
    }

    [Theory]
    [InlineData("1234567890 abc123", "1234567890", "abc123")]
    [InlineData("1 234 567 890 abc123", "1 234 567 890", "abc123")]
    [InlineData("  1234567890 abc123  ", "1234567890", "abc123")]
    public void TryParse_UnlabeledFormat_ExtractsCredentials(string input, string expectedId, string expectedPassword)
    {
        var (id, password) = CredentialParser.TryParse(input);

        id.Should().Be(expectedId);
        password.Should().Be(expectedPassword);
    }

    [Theory]
    [InlineData("1234567890\nabc123", "1234567890", "abc123")]
    [InlineData("1234567890\r\nabc123", "1234567890", "abc123")]
    [InlineData("  1234567890  \n  abc123  ", "1234567890", "abc123")]
    public void TryParse_TwoLineFormat_ExtractsCredentials(string input, string expectedId, string expectedPassword)
    {
        var (id, password) = CredentialParser.TryParse(input);

        id.Should().Be(expectedId);
        password.Should().Be(expectedPassword);
    }

    [Theory]
    [InlineData("ID: 1 234 567 890\nPassword: mypass123", "1 234 567 890", "mypass123")]
    [InlineData("ID:  spaced id  \nPassword:  spaced pass  ", "spaced id", "spaced pass")]
    public void TryParse_LabeledWithSpaces_TrimsValues(string input, string expectedId, string expectedPassword)
    {
        var (id, password) = CredentialParser.TryParse(input);

        id.Should().Be(expectedId);
        password.Should().Be(expectedPassword);
    }

    [Theory]
    [InlineData("onlyoneline")]
    [InlineData("no password here")]
    [InlineData("abc def ghi")]
    public void TryParse_InvalidFormat_ReturnsNullTuple(string input)
    {
        var (id, password) = CredentialParser.TryParse(input);

        id.Should().BeNull();
        password.Should().BeNull();
    }

    [Fact]
    public void TryParse_ThreeLines_ReturnsNullTuple()
    {
        var input = "line1\nline2\nline3";

        var (id, password) = CredentialParser.TryParse(input);

        id.Should().BeNull();
        password.Should().BeNull();
    }

    [Fact]
    public void TryParse_LabeledWithExtraWhitespace_HandlesCorrectly()
    {
        var input = "  ID:   1234567890   \n   Password:   abc123   ";

        var (id, password) = CredentialParser.TryParse(input);

        id.Should().Be("1234567890");
        password.Should().Be("abc123");
    }

    [Theory]
    [InlineData("ID: user@example.com\nPassword: p@ss!word#123", "user@example.com", "p@ss!word#123")]
    [InlineData("ID: test-id_123\nPassword: Test.Pass_456", "test-id_123", "Test.Pass_456")]
    public void TryParse_SpecialCharacters_HandlesCorrectly(string input, string expectedId, string expectedPassword)
    {
        var (id, password) = CredentialParser.TryParse(input);

        id.Should().Be(expectedId);
        password.Should().Be(expectedPassword);
    }
}
