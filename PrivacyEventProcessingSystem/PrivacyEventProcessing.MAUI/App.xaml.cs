namespace PrivacyEventProcessing.MAUI
{
    public partial class App : Application
    {
        private readonly IServiceProvider services;

        public App(IServiceProvider services)
        {
            InitializeComponent();
            this.services = services;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell(services));
        }
    }
}
