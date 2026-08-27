using PrivacyEventProcessing.Domain.Validation;

namespace PrivacyEventProcessing.MAUI.Behaviors
{
    // One per field, all delegating to the same rules the pipeline enforces
    public class UserIdValidationBehavior : EntryValidationBehavior
    {
        protected override bool Validate(string? value, out string errorMessage)
        {
            return EventValidationRules.IsValidUserId(value, out errorMessage);
        }
    }

    public class EmailValidationBehavior : EntryValidationBehavior
    {
        protected override bool Validate(string? value, out string errorMessage)
        {
            return EventValidationRules.IsValidEmail(value, out errorMessage);
        }
    }

    public class IpAddressValidationBehavior : EntryValidationBehavior
    {
        protected override bool Validate(string? value, out string errorMessage)
        {
            return EventValidationRules.IsValidIpAddress(value, out errorMessage);
        }
    }

    public class EventTypeValidationBehavior : EntryValidationBehavior
    {
        protected override bool Validate(string? value, out string errorMessage)
        {
            return EventValidationRules.IsValidEventType(value, out errorMessage);
        }
    }
}
