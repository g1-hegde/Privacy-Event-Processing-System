using System.Net;
using System.Text.RegularExpressions;

namespace PrivacyEventProcessing.Domain.Validation
{
    // Static because the MAUI validation behaviours are built by the XAML parser and can't
    // take constructor injection. Shared by the entry form and the worker loop.
    public static class EventValidationRules
    {
        private static readonly Regex EmailRegex = new(
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));

        public static bool IsValidUserId(string? userId, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                errorMessage = "UserId is required.";
                return false;
            }

            if (userId.Length > 64)
            {
                errorMessage = "UserId must be 64 characters or fewer.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        public static bool IsValidEmail(string? email, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
            {
                errorMessage = "Invalid email format.";
                return false;
            }

            try
            {
                if (!EmailRegex.IsMatch(email))
                {
                    errorMessage = "Invalid email format.";
                    return false;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                errorMessage = "Invalid email format.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        public static bool IsValidIpAddress(string? ipAddress, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.IsValid(ipAddress))
            {
                errorMessage = "Invalid IP address format.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        public static bool IsValidEventType(string? eventType, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                errorMessage = "EventType is required.";
                return false;
            }

            if (eventType.Length > 64)
            {
                errorMessage = "EventType must be 64 characters or fewer.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }
    }
}
