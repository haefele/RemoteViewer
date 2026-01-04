using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RemoteViewer.Client.Controls.Toasts;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.ViewModels;
using RemoteViewer.Client.Views.Main;

namespace RemoteViewer.IntegrationTests.UI;

public class MainViewTests
{
    private MainViewModel CreateViewModel()
    {
        var options = Options.Create(new ConnectionHubClientOptions());
        var hubClient = Substitute.For<ConnectionHubClient>(
            Substitute.For<ILogger<ConnectionHubClient>>(),
            Substitute.For<IServiceProvider>(),
            options);

        var viewModelFactory = Substitute.For<IViewModelFactory>();
        viewModelFactory.CreateToastsViewModel().Returns(new ToastsViewModel());

        var logger = Substitute.For<ILogger<MainViewModel>>();

        return new MainViewModel(hubClient, viewModelFactory, logger);
    }

    [Test]
    public async Task MainViewModelStatusTextDefaultsToConnecting()
    {
        var viewModel = this.CreateViewModel();

        await Assert.That(viewModel.StatusText).IsEqualTo("Connecting...");
    }

    [Test]
    public async Task MainViewModelCanSetTargetUsername()
    {
        var viewModel = this.CreateViewModel();
        viewModel.TargetUsername = "1234567890";

        await Assert.That(viewModel.TargetUsername).IsEqualTo("1234567890");
    }

    [Test]
    public async Task MainViewModelCanSetTargetPassword()
    {
        var viewModel = this.CreateViewModel();
        viewModel.TargetPassword = "testpass";

        await Assert.That(viewModel.TargetPassword).IsEqualTo("testpass");
    }

    [Test]
    public async Task MainViewModelYourCredentialsCanBeSet()
    {
        var viewModel = this.CreateViewModel();
        viewModel.YourUsername = "...";
        viewModel.YourPassword = "...";

        await Assert.That(viewModel.YourUsername).IsEqualTo("...");
        await Assert.That(viewModel.YourPassword).IsEqualTo("...");
    }

    [Test]
    public async Task MainViewModelHasVersionMismatchDefaultsFalse()
    {
        var viewModel = this.CreateViewModel();

        await Assert.That(viewModel.HasVersionMismatch).IsFalse();
    }

    [Test]
    public async Task MainViewModelIsConnectedDefaultsFalse()
    {
        var viewModel = this.CreateViewModel();

        await Assert.That(viewModel.IsConnected).IsFalse();
    }

    [Test]
    public async Task MainViewModelToastsIsCreated()
    {
        var viewModel = this.CreateViewModel();

        await Assert.That(viewModel.Toasts).IsNotNull();
    }
}
