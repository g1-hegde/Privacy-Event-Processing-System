using PrivacyEventProcessing.Domain.Models;
using PrivacyEventProcessing.Domain.Validation;
using PrivacyEventProcessing.MockData;

namespace PrivacyEventProcessing.Test
{
    public class MockDataGeneratorTests
    {
        // Bad IP generation slipped through twice and only showed up as an odd failure rate
        // on the dashboard, so catch it at the source
        [Fact]
        public void GenerateBulkEvents_ProducesOnlyValidEvents()
        {
            var generator = new MockDataGenerator(seed: 12345);

            foreach (EventRequest request in generator.GenerateBulkEvents(2000))
            {
                Assert.True(
                    EventValidationRules.IsValidUserId(request.UserId, out string userIdError),
                    userIdError);
                Assert.True(
                    EventValidationRules.IsValidEmail(request.Email, out string emailError),
                    $"{emailError} ({request.Email})");
                Assert.True(
                    EventValidationRules.IsValidIpAddress(request.IpAddress, out string ipError),
                    $"{ipError} ({request.IpAddress})");
                Assert.True(
                    EventValidationRules.IsValidEventType(request.EventType, out string typeError),
                    typeError);
            }
        }

        [Fact]
        public void GenerateBulkEvents_ProducesBothAddressFamilies()
        {
            var generator = new MockDataGenerator(seed: 12345);
            var addresses = generator.GenerateBulkEvents(1000).Select(e => e.IpAddress).ToList();

            Assert.Contains(addresses, a => a.Contains('.'));
            Assert.Contains(addresses, a => a.Contains(':'));
        }

        [Fact]
        public void GenerateBulkEvents_ReturnsRequestedCount()
        {
            var generator = new MockDataGenerator(seed: 1);

            Assert.Equal(500, generator.GenerateBulkEvents(500).Count());
        }

        // The malformed share drives the InvalidInput count, so it has to track the ratio
        [Fact]
        public void GenerateBulkEvents_ProducesMalformedEventsAtRoughlyTheRequestedRatio()
        {
            const int count = 20_000;
            var generator = new MockDataGenerator(seed: 4242);

            int invalid = generator
                .GenerateBulkEvents(count, MockDataGenerator.DefaultMalformedRatio)
                .Count(request => !IsValid(request));

            double ratio = (double)invalid / count;

            Assert.InRange(ratio, 0.005, 0.015);
        }

        // Every rule should be reachable, or a broken one could never show up in a bulk run
        [Fact]
        public void GenerateBulkEvents_BreaksEveryValidationRuleAcrossALargeRun()
        {
            var generator = new MockDataGenerator(seed: 99);

            List<EventRequest> malformed = generator
                .GenerateBulkEvents(20_000, malformedRatio: 0.5)
                .Where(request => !IsValid(request))
                .ToList();

            Assert.Contains(malformed, r => !EventValidationRules.IsValidUserId(r.UserId, out _));
            Assert.Contains(malformed, r => !EventValidationRules.IsValidEmail(r.Email, out _));
            Assert.Contains(malformed, r => !EventValidationRules.IsValidIpAddress(r.IpAddress, out _));
            Assert.Contains(malformed, r => !EventValidationRules.IsValidEventType(r.EventType, out _));
        }

        [Fact]
        public void GenerateBulkEvents_RejectsARatioOutsideZeroToOne()
        {
            var generator = new MockDataGenerator(seed: 1);

            // Lazy iterator, so the guard only runs on enumeration
            Assert.Throws<ArgumentOutOfRangeException>(
                () => generator.GenerateBulkEvents(10, -0.1).ToList());
            Assert.Throws<ArgumentOutOfRangeException>(
                () => generator.GenerateBulkEvents(10, 1.5).ToList());
        }

        private static bool IsValid(EventRequest request) =>
            EventValidationRules.IsValidUserId(request.UserId, out _)
            && EventValidationRules.IsValidEmail(request.Email, out _)
            && EventValidationRules.IsValidIpAddress(request.IpAddress, out _)
            && EventValidationRules.IsValidEventType(request.EventType, out _);
    }
}
