using PrivacyEventProcessing.MAUI.ViewModels;

namespace PrivacyEventProcessing.MAUI.Views
{
    public partial class DashboardPage : ContentPage
    {
        // Keep in sync with the MinWindowWidth on the AdaptiveTriggers in the XAML
        private const double WideBreakpoint = 900;

        private readonly MainDashboardViewModel viewModel;

        private bool isTogglingWorkers;

        public DashboardPage(MainDashboardViewModel viewModel)
        {
            InitializeComponent();
            this.viewModel = viewModel;
            BindingContext = viewModel;
        }

        // Timer only runs while the page is on screen, so a hidden dashboard isn't waking
        // the UI thread twice a second for nothing
        protected override void OnAppearing()
        {
            base.OnAppearing();
            viewModel.Activate();
        }

        protected override void OnDisappearing()
        {
            viewModel.Deactivate();
            base.OnDisappearing();
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            if (width > 0)
            {
                viewModel.IsNarrowLayout = width < WideBreakpoint;
            }
        }

        // Fires when the user flips the switch and when the view model changes it;
        // SetProcessingAsync ignores the second case. The guard is needed because a Toggled
        // handler has no re-entry protection, unlike the commands behind the other controls.
        private async void OnWorkersToggled(object? sender, ToggledEventArgs e)
        {
            if (sender is not Switch toggle || isTogglingWorkers)
            {
                return;
            }

            isTogglingWorkers = true;
            toggle.IsEnabled = false;

            try
            {
                await viewModel.SetProcessingAsync(e.Value);
            }
            catch (Exception ex)
            {
                viewModel.StatusMessage = $"Could not change the worker state: {ex.Message}";
            }
            finally
            {
                toggle.IsEnabled = true;
                isTogglingWorkers = false;
            }
        }

        // Android hardware back should close the popup rather than leave the page
        protected override bool OnBackButtonPressed()
        {
            if (viewModel.DetailEvent is not null)
            {
                viewModel.CloseEventDetailCommand.Execute(null);
                return true;
            }

            return base.OnBackButtonPressed();
        }
    }
}
