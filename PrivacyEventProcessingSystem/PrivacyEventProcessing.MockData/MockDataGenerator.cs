using PrivacyEventProcessing.Domain.Models;

namespace PrivacyEventProcessing.MockData
{
    // Single producer, not thread safe - Random has mutable state and concurrent use
    // degrades its output silently. One generator task fills the queue; the workers are the
    // parallel half. Instance Random rather than Random.Shared so tests can seed it.
    public sealed class MockDataGenerator
    {
        private static readonly string[] EventTypes =
        [
            "Login",
            "Logout",
            "Purchase",
            "ViewProduct",
            "PasswordChanged",
            "ProfileUpdated",
        ];

        private static readonly string[] Domains =
        [
            "gmail.com",
            "outlook.com",
            "facebook.com",
            "twitter.com",
        ];

        private const int DistinctUsers = 5_000;

        private readonly Random random;

        public MockDataGenerator(int? seed = null)
        {
            random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        // Share of a bulk run that is deliberately malformed, so InvalidInput on the
        // dashboard reflects real bad data rather than being decoration.
        public const double DefaultMalformedRatio = 0.01;

        // Defaults to 0 so the "everything generated is valid" test still guards the happy path
        public IEnumerable<EventRequest> GenerateBulkEvents(int count, double malformedRatio = 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(malformedRatio);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(malformedRatio, 1);

            for (int i = 0; i < count; i++)
            {
                yield return malformedRatio > 0 && random.NextDouble() < malformedRatio
                    ? GenerateMalformedEvent()
                    : GenerateRandomEvent();
            }
        }

        // One broken field per event, cycling all four rules, so validation doesn't always
        // trip on the same check
        private EventRequest GenerateMalformedEvent()
        {
            EventRequest request = GenerateRandomEvent();

            switch (random.Next(4))
            {
                case 0:
                    request.UserId = string.Empty;
                    break;
                case 1:
                    request.Email = "not-an-email";
                    break;
                case 2:
                    request.IpAddress = "999.1.1.1";
                    break;
                default:
                    request.EventType = string.Empty;
                    break;
            }

            return request;
        }

        private EventRequest GenerateRandomEvent()
        {
            var userNumber = random.Next(1, DistinctUsers + 1);
            return new EventRequest
            {
                UserId = $"U:{userNumber}",
                EventType = EventTypes[random.Next(EventTypes.Length)],
                Email = $"U{userNumber}@{Domains[random.Next(Domains.Length)]}",
                IpAddress = random.Next(100) < 40 ? // 40% chance to generate an IPv4 address, 60% chance to generate an IPv6 address
                string.Join('.', new[] { random.Next(1, 256) }.Concat(Enumerable.Range(0, 3).Select(_ => random.Next(256))))
                : string.Join(':', Enumerable.Range(0, 8).Select(_ => random.GetHexString(4, true))),
                CreatedAt = DateTime.UtcNow.AddSeconds(-random.Next(0, 3600))
            };
        }

    }
}
