using PrivacyEventProcessing.Domain.Models;
using PrivacyEventProcessing.Domain.Validation;
using PrivacyEventProcessing.Integration.Services;

namespace PrivacyEventProcessing.Test
{
    public class EventValidationRulesTests
    {
        private static PrivacyService CreatePrivacyService() => new(new PrivacyOptions("test-hash-key"));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void IsValidUserId_RejectsEmpty(string? userId)
        {
            Assert.False(EventValidationRules.IsValidUserId(userId, out string error));
            Assert.NotEmpty(error);
        }

        [Theory]
        [InlineData("user@example.com", true)]
        [InlineData("user.name@sub.example.co.uk", true)]
        [InlineData("no-at-sign", false)]
        [InlineData("no@domain", false)]
        [InlineData("spaces in@example.com", false)]
        [InlineData("", false)]
        public void IsValidEmail_ChecksFormat(string email, bool expected)
        {
            Assert.Equal(expected, EventValidationRules.IsValidEmail(email, out _));
        }

        [Theory]
        [InlineData("192.168.1.10", true)]
        [InlineData("2001:0db8:85a3:0000:0000:8a2e:0370:7334", true)]
        [InlineData("::1", true)]
        [InlineData("999.1.1.1", false)]
        [InlineData("192:168:1:10", false)]
        [InlineData("", false)]
        public void IsValidIpAddress_ChecksFormat(string ipAddress, bool expected)
        {
            Assert.Equal(expected, EventValidationRules.IsValidIpAddress(ipAddress, out _));
        }

        [Fact]
        public void IsValidEventType_RejectsEmpty()
        {
            Assert.False(EventValidationRules.IsValidEventType("  ", out string error));
            Assert.NotEmpty(error);
        }

        // The pipeline entry point has to agree with the per-field rules the form uses
        [Fact]
        public void ValidateInput_AcceptsAWellFormedEvent()
        {
            var request = new EventRequest
            {
                UserId = "U12345",
                Email = "user@example.com",
                IpAddress = "192.168.1.10",
                EventType = "Login"
            };

            Assert.True(CreatePrivacyService().ValidateInput(request, out string error));
            Assert.Empty(error);
        }

        [Fact]
        public void ValidateInput_ReportsTheFirstProblem()
        {
            var request = new EventRequest
            {
                UserId = "U12345",
                Email = "broken",
                IpAddress = "not-an-ip",
                EventType = "Login"
            };

            Assert.False(CreatePrivacyService().ValidateInput(request, out string error));
            Assert.Contains("email", error, StringComparison.OrdinalIgnoreCase);
        }
    }
}
