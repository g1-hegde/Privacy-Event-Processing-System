using PrivacyEventProcessing.Domain.Interfaces;
using PrivacyEventProcessing.Domain.Models;
using PrivacyEventProcessing.Domain.Validation;
using System.Net;
using System.Text;

namespace PrivacyEventProcessing.Integration.Services
{
    public class PrivacyService : IPrivacyService
    {
        private readonly byte[] hashKey;

        public PrivacyService(PrivacyOptions options)
        {
            hashKey = Encoding.UTF8.GetBytes(options.HashKey);
        }

        // HMAC rather than plain salted SHA256 - the id space is small enough to brute force
        // if the salt sits in the source. Deterministic within a run, so it's a pseudonym
        // rather than anonymous data. Thread safe: immutable key, static HashData.
        public string HashUserId(string userId)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(userId);
            byte[] hashBytes = System.Security.Cryptography.HMACSHA256.HashData(hashKey, inputBytes);
            return Convert.ToBase64String(hashBytes);
        }

        public string MaskEmail(string email)
        {
            int atIndex = email.IndexOf('@');
            if (atIndex <= 0 || atIndex == email.Length - 1) return "***@domain.com"; //defensive programming, should not happen if validated before

            string userName = email[..atIndex];
            string domain = email[atIndex..];

            // first + last would give away the whole thing at 2 chars
            if (userName.Length <= 2) return $"***{domain}";

            return $"{userName[0]}***{userName[^1]}{domain}";
        }

        public string MaskIpAddress(string ipAddress)
        {
            if (!IPAddress.TryParse(ipAddress, out var ip))
                return "xxx.xxx.xxx.xxx"; //defensive programming, should not happen if validated before

            // Use the parsed bytes, not string splitting - otherwise shortened IPv6 like ::1 breaks
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                Span<byte> bytes = stackalloc byte[4];
                ip.TryWriteBytes(bytes, out _);

                // drop the host octet, keep the network prefix
                return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.xxx";
            }
            else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                Span<byte> bytes = stackalloc byte[16];
                ip.TryWriteBytes(bytes, out _);

                return $"{bytes[0]:x2}{bytes[1]:x2}:{bytes[2]:x2}{bytes[3]:x2}:xxxx:xxxx:xxxx:xxxx:xxxx:xxxx";
            }
            else
            {
                return "xxx.xxx.xxx.xxx";
            }
        }

        public bool ValidateInput(EventRequest request, out string errorMessage)
        {
            return EventValidationRules.IsValidUserId(request.UserId, out errorMessage)
                && EventValidationRules.IsValidEmail(request.Email, out errorMessage)
                && EventValidationRules.IsValidIpAddress(request.IpAddress, out errorMessage)
                && EventValidationRules.IsValidEventType(request.EventType, out errorMessage);
        }
    }
}
