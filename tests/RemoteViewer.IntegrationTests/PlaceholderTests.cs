namespace RemoteViewer.IntegrationTests;

public class PlaceholderTests
{
    [Test]
    public async Task PlaceholderTest()
    {
        // This is a placeholder test to ensure the project has at least one test.
        // Real integration tests will be added as the UI testing infrastructure is developed.
        var value = 1 + 1;
        await Assert.That(value).IsEqualTo(2);
    }
}
