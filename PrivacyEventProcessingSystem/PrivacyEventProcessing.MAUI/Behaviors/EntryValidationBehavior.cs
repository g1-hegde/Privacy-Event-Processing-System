namespace PrivacyEventProcessing.MAUI.Behaviors
{
    // Base for the entry form validation behaviours. One rule per subclass.
    public abstract class EntryValidationBehavior : Behavior<Entry>
    {
        private static readonly BindablePropertyKey IsValidPropertyKey = BindableProperty.CreateReadOnly(
            nameof(IsValid), typeof(bool), typeof(EntryValidationBehavior), false, BindingMode.OneWay);

        private static readonly BindablePropertyKey ErrorMessagePropertyKey = BindableProperty.CreateReadOnly(
            nameof(ErrorMessage), typeof(string), typeof(EntryValidationBehavior), string.Empty, BindingMode.OneWay);

        private static readonly BindablePropertyKey HasErrorPropertyKey = BindableProperty.CreateReadOnly(
            nameof(HasError), typeof(bool), typeof(EntryValidationBehavior), false, BindingMode.OneWay);

        public static readonly BindableProperty IsValidProperty = IsValidPropertyKey.BindableProperty;
        public static readonly BindableProperty ErrorMessageProperty = ErrorMessagePropertyKey.BindableProperty;
        public static readonly BindableProperty HasErrorProperty = HasErrorPropertyKey.BindableProperty;

        private Entry? entry;
        private bool hasBeenEdited;

        public bool IsValid => (bool)GetValue(IsValidProperty);

        public string ErrorMessage => (string)GetValue(ErrorMessageProperty);

        // Separate from IsValid so an untouched empty field doesn't open in red
        public bool HasError => (bool)GetValue(HasErrorProperty);

        protected abstract bool Validate(string? value, out string errorMessage);

        protected override void OnAttachedTo(Entry bindable)
        {
            base.OnAttachedTo(bindable);

            entry = bindable;
            bindable.TextChanged += OnTextChanged;
            bindable.Unfocused += OnUnfocused;

            // Behaviours don't inherit the binding context of the view they attach to, so it
            // has to be forwarded for bindings on the behaviour to resolve
            bindable.BindingContextChanged += OnEntryBindingContextChanged;
            BindingContext = bindable.BindingContext;

            Evaluate(bindable.Text);
        }

        protected override void OnDetachingFrom(Entry bindable)
        {
            bindable.TextChanged -= OnTextChanged;
            bindable.Unfocused -= OnUnfocused;
            bindable.BindingContextChanged -= OnEntryBindingContextChanged;

            entry = null;
            base.OnDetachingFrom(bindable);
        }

        private void OnEntryBindingContextChanged(object? sender, EventArgs e)
        {
            BindingContext = entry?.BindingContext;
        }

        // Does not mark the field edited: binding the view model's empty string over the
        // Entry's null default raises TextChanged before the user has touched anything,
        // which would show every error the moment the page opens.
        private void OnTextChanged(object? sender, TextChangedEventArgs e)
        {
            // Emptied while unfocused means the view model cleared it, not the user - that
            // happens after a successful submit, and the form should come back pristine
            if (string.IsNullOrEmpty(e.NewTextValue) && sender is Entry source && !source.IsFocused)
            {
                hasBeenEdited = false;
            }

            Evaluate(e.NewTextValue);
        }

        // Leaving a field counts as having filled it in; after that the error updates live
        private void OnUnfocused(object? sender, FocusEventArgs e)
        {
            hasBeenEdited = true;
            Evaluate((sender as Entry)?.Text);
        }

        private void Evaluate(string? value)
        {
            bool valid = Validate(value, out string error);

            SetValue(IsValidPropertyKey, valid);
            SetValue(ErrorMessagePropertyKey, error);
            SetValue(HasErrorPropertyKey, !valid && hasBeenEdited);
        }
    }
}
