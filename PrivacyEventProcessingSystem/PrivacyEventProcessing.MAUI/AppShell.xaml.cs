using Microsoft.Extensions.DependencyInjection;
using PrivacyEventProcessing.MAUI.Views;

namespace PrivacyEventProcessing.MAUI
{
    public partial class AppShell : Shell
    {
        // Templates are set here rather than with ContentTemplate in XAML so the pages come
        // from the container and get their view models injected
        public AppShell(IServiceProvider services)
        {
            InitializeComponent();

            DashboardContent.ContentTemplate = new DataTemplate(services.GetRequiredService<DashboardPage>);
            EventEntryContent.ContentTemplate = new DataTemplate(services.GetRequiredService<EventEntryPage>);
        }
    }
}
