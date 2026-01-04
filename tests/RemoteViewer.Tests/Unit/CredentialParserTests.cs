using RemoteViewer.Client.Services;

namespace RemoteViewer.Tests.Unit;

public class CredentialParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void TryParseNullOrWhitespaceReturnsNullTuple(string? input)
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
    public void TryParseLabeledFormatExtractsCredentials(string input, string expectedId, string expectedPassword)
    {
        var (id, password) = CredentialParser.TryParse(input);

        id.Should().Be(expectedId);
        password.Should().Be(expectedPassword);
    }

    [Theory]
    [InlineData("1234567890 abc123", "1234567890", "abc123")]
    [InlineData("1 234 567 890 abc123", "1 234 567 890", "abc123")]
    [InlineData("  1234567890 abc123  ", "1234567890", "abc123")]
    public void TryParseUnlabeledFormatExtractsCredentials(string input, string expectedId, string expectedPassword)
    {
        var (id, password) = CredentialParser.TryParse(input);

        id.Should().Be(expectedId);
        password.Should().Be(expectedPassword);
    }

    [Theory]
    [InlineData("1234567890\nabc123", "1234567890", "abc123")]
    [InlineData("1234567890\r\nabc123", "1234567890", "abc123")]
    [InlineData("  1234567890  \n  abc123  ", "1234567890", "abc123")]
    public void TryParseTwoLineFormatExtractsCredentials(string input, string expectedId, string expectedPassword)
    {
        var (id, password) = CredentialParser.TryParse(input);

        id.Should().Be(expectedId);
        password.Should().Be(expectedPassword);
    }

    [Theory]
    [InlineData("ID: 1 234 567 890\nPassword: mypass123", "1 234 567 890", "mypass123")]
    [InlineData("ID:  spaced id  \nPassword:  spaced pass  ", "spaced id", "spaced pass")]
    public void TryParseLabeledWithSpacesTrimsValues(string input, string expectedId, string expectedPassword)
    {
        var (id, password) = CredentialParser.TryParse(input);

        id.Should().Be(expectedId);
        password.Should().Be(expectedPassword);
    }

    [Theory]
    [InlineData("onlyoneline")]
    [InlineData("no password here")]
    [InlineData("abc def ghi")]
    public void TryParseInvalidFormatReturnsNullTuple(string input)
    {
        var (id, password) = CredentialParser.TryParse(input);

        id.Should().BeNull();
        password.Should().BeNull();
    }

    [Fact]
    public void TryParseThreeLinesReturnsNullTuple()
    {
        var input = "line1\nline2\nline3";

        var (id, password) = CredentialParser.TryParse(input);

        id.Should().BeNull();
        password.Should().BeNull();
    }

    [Fact]
    public void TryParseLabeledWithExtraWhitespaceHandlesCorrectly()
    {
        var input = "  ID:   1234567890   \n   Password:   abc123   ";

        var (id, password) = CredentialParser.TryParse(input);

        id.Should().Be("1234567890");
        password.Should().Be("abc123");
    }

    [Theory]
    [InlineData("ID: user@example.com\nPassword: p@ss!word#123", "user@example.com", "p@ss!word#123")]
    [InlineData("ID: test-id_123\nPassword: Test.Pass_456", "test-id_123", "Test.Pass_456")]
    public void TryParseSpecialCharactersHandlesCorrectly(string input, string expectedId, string expectedPassword)
    {
        var (id, password) = CredentialParser.TryParse(input);

        id.Should().Be(expectedId);
        password.Should().Be(expectedPassword);
    }
}
