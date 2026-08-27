using Microsoft.Extensions.Logging;
using PrivacyEventProcessing.Domain.Interfaces;
using PrivacyEventProcessing.Domain.Models;
using PrivacyEventProcessing.Integration.Engine;
using PrivacyEventProcessing.Integration.Services;
using PrivacyEventProcessing.Integration.Storage;
using PrivacyEventProcessing.MAUI.ViewModels;
using PrivacyEventProcessing.MAUI.Views;
using PrivacyEventProcessing.MockData;
using System.Security.Cryptography;

namespace PrivacyEventProcessing.MAUI
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

#if WINDOWS
            Microsoft.Maui.Handlers.SwitchHandler.Mapper.AppendToMapping("CompactSwitch", (handler, view) =>
            {
                handler.PlatformView.MinWidth = 0;
                handler.PlatformView.OnContent = null;
                handler.PlatformView.OffContent = null;
            });
#endif
            // 32 random bytes per run, never persisted - the key shouldn't outlive the
            // in-memory store it pseudonymises. Injected so tests can pin a known key.
            builder.Services.AddSingleton(new PrivacyOptions(
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

            builder.Services.AddSingleton(new FaultInjectionOptions());
            builder.Services.AddSingleton<IFaultPolicy, SimulatedFaultPolicy>();

            builder.Services.AddSingleton<IEventQueue, ChannelEventQueue>();
            builder.Services.AddSingleton<IEventRepository, InMemoryEventRepository>();
            builder.Services.AddSingleton<IPrivacyService, PrivacyService>();
            builder.Services.AddSingleton<IProcessingMetrics, ProcessingMetrics>();
            builder.Services.AddSingleton<IEventProcessor, BackgroundWorkerPool>();
            builder.Services.AddSingleton<MockDataGenerator>();

            // Registered so the view models can take IDispatcher rather than reaching for
            // Application.Current themselves
            builder.Services.AddSingleton<IDispatcher>(_ =>
                Application.Current?.Dispatcher
                ?? Dispatcher.GetForCurrentThread()
                ?? throw new InvalidOperationException("No dispatcher available."));

            builder.Services.AddSingleton<MainDashboardViewModel>();
            builder.Services.AddSingleton<EventEntryViewModel>();

            builder.Services.AddSingleton<DashboardPage>();
            builder.Services.AddSingleton<EventEntryPage>();

            return builder.Build();
        }
    }
}
