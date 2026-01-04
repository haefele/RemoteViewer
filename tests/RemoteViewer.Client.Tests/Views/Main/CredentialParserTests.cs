using RemoteViewer.Client.Services;

namespace RemoteViewer.Client.Tests.Views.Main;

public class CredentialParserTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("\t\n")]
    public async Task TryParseNullOrWhitespaceReturnsNullTuple(string? input)
    {
        // Act
        var (id, password) = CredentialParser.TryParse(input);

        // Assert
        await Assert.That(id).IsNull();
        await Assert.That(password).IsNull();
    }

    [Test]
    [Arguments("ID: 1234567890\nPassword: abc123", "1234567890", "abc123")]
    [Arguments("id: 1234567890\npassword: abc123", "1234567890", "abc123")]
    [Arguments("ID: 1234567890\r\nPassword: abc123", "1234567890", "abc123")]
    [Arguments("ID=1234567890\nPassword=abc123", "1234567890", "abc123")]
    [Arguments("ID-1234567890\nPassword-abc123", "1234567890", "abc123")]
    [Arguments("id:1234567890\npass:abc123", "1234567890", "abc123")]
    [Arguments("id:1234567890\npwd:abc123", "1234567890", "abc123")]
    public async Task TryParseLabeledFormatExtractsCredentials(string input, string expectedId, string expectedPassword)
    {
        // Act
        var (id, password) = CredentialParser.TryParse(input);

        // Assert
        await Assert.That(id).IsEqualTo(expectedId);
        await Assert.That(password).IsEqualTo(expectedPassword);
    }

    [Test]
    [Arguments("1234567890 abc123", "1234567890", "abc123")]
    [Arguments("1 234 567 890 abc123", "1 234 567 890", "abc123")]
    [Arguments("  1234567890 abc123  ", "1234567890", "abc123")]
    public async Task TryParseUnlabeledFormatExtractsCredentials(string input, string expectedId, string expectedPassword)
    {
        // Act
        var (id, password) = CredentialParser.TryParse(input);

        // Assert
        await Assert.That(id).IsEqualTo(expectedId);
        await Assert.That(password).IsEqualTo(expectedPassword);
    }

    [Test]
    [Arguments("1234567890\nabc123", "1234567890", "abc123")]
    [Arguments("1234567890\r\nabc123", "1234567890", "abc123")]
    [Arguments("  1234567890  \n  abc123  ", "1234567890", "abc123")]
    public async Task TryParseTwoLineFormatExtractsCredentials(string input, string expectedId, string expectedPassword)
    {
        // Act
        var (id, password) = CredentialParser.TryParse(input);

        // Assert
        await Assert.That(id).IsEqualTo(expectedId);
        await Assert.That(password).IsEqualTo(expectedPassword);
    }

    [Test]
    [Arguments("ID: 1 234 567 890\nPassword: mypass123", "1 234 567 890", "mypass123")]
    [Arguments("ID:  spaced id  \nPassword:  spaced pass  ", "spaced id", "spaced pass")]
    public async Task TryParseLabeledWithSpacesTrimsValues(string input, string expectedId, string expectedPassword)
    {
        // Act
        var (id, password) = CredentialParser.TryParse(input);

        // Assert
        await Assert.That(id).IsEqualTo(expectedId);
        await Assert.That(password).IsEqualTo(expectedPassword);
    }

    [Test]
    [Arguments("onlyoneline")]
    [Arguments("no password here")]
    [Arguments("abc def ghi")]
    public async Task TryParseInvalidFormatReturnsNullTuple(string input)
    {
        // Act
        var (id, password) = CredentialParser.TryParse(input);

        // Assert
        await Assert.That(id).IsNull();
        await Assert.That(password).IsNull();
    }

    [Test]
    public async Task TryParseThreeLinesReturnsNullTuple()
    {
        var input = "line1\nline2\nline3";

        // Act
        var (id, password) = CredentialParser.TryParse(input);

        // Assert
        await Assert.That(id).IsNull();
        await Assert.That(password).IsNull();
    }

    [Test]
    public async Task TryParseLabeledWithExtraWhitespaceHandlesCorrectly()
    {
        var input = "  ID:   1234567890   \n   Password:   abc123   ";

        // Act
        var (id, password) = CredentialParser.TryParse(input);

        // Assert
        await Assert.That(id).IsEqualTo("1234567890");
        await Assert.That(password).IsEqualTo("abc123");
    }

    [Test]
    [Arguments("ID: user@example.com\nPassword: p@ss!word#123", "user@example.com", "p@ss!word#123")]
    [Arguments("ID: test-id_123\nPassword: Test.Pass_456", "test-id_123", "Test.Pass_456")]
    public async Task TryParseSpecialCharactersHandlesCorrectly(string input, string expectedId, string expectedPassword)
    {
        // Act
        var (id, password) = CredentialParser.TryParse(input);

        // Assert
        await Assert.That(id).IsEqualTo(expectedId);
        await Assert.That(password).IsEqualTo(expectedPassword);
    }
}
