using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using RemoteViewer.Client;
using RemoteViewer.Client.Services;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.ViewModels;
using RemoteViewer.Client.Views.Main;

namespace RemoteViewer.IntegrationTests.UI;

public class UITest
{
    // TODO: This test needs to be rewritten to use actual server processes
    // instead of WebApplicationFactory (which is now in Server.Tests)

    //[Test]
    public async Task Test()
    {
        // ServiceRegistration.CustomizeServices = services =>
        // {
        //     services.Configure<ConnectionHubClientOptions>(options =>
        //     {
        //         options.HttpMessageHandlerFactory = () => ???;
        //     });
        // };

        var taskComplSource = new TaskCompletionSource();
        var uiThread = new Thread(_ =>
        {
            Client.Program.BuildAvaloniaApp()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions
                {
                    UseHeadlessDrawing = false,
                })
                .AfterSetup(_ => taskComplSource.SetResult())
                .StartWithClassicDesktopLifetime(Array.Empty<string>());
        });
        uiThread.Start();

        await taskComplSource.Task;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var client = App.Current.Services.GetRequiredService<ConnectionHubClient>();

            var viewModel = App.Current.Services
                .GetRequiredService<IViewModelFactory>()
                .CreateMainViewModel();
            var view = new MainView
            {
                DataContext = viewModel,
            };

            view.Show();

            await this.Until(() => viewModel.YourUsername is not null, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100));

            var frame = view.CaptureRenderedFrame();
            frame?.Save("UITest_Screenshot.png");
        });
    }

    private async Task Until(Func<bool> condition, TimeSpan timeout, TimeSpan pollInterval)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - start > timeout)
                throw new TimeoutException("Condition was not met within the specified timeout.");
            await Task.Delay(pollInterval);
        }
    }
}
