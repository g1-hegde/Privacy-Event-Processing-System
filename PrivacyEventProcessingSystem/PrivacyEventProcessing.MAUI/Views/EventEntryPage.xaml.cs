using PrivacyEventProcessing.MAUI.ViewModels;

namespace PrivacyEventProcessing.MAUI.Views
{
    public partial class EventEntryPage : ContentPage
    {
        private readonly EventEntryViewModel viewModel;

        public EventEntryPage(EventEntryViewModel viewModel)
        {
            InitializeComponent();
            this.viewModel = viewModel;
            BindingContext = viewModel;
        }

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
    }
}
