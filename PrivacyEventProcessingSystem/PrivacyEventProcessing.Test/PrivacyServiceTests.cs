using PrivacyEventProcessing.Domain.Models;
using PrivacyEventProcessing.Integration.Services;

namespace PrivacyEventProcessing.Test
{
    public class PrivacyServiceTests
    {
        private readonly PrivacyService privacyService = new(new PrivacyOptions("test-hash-key"));

        [Fact]
        public void HashUserId_IsDeterministic()
        {
            Assert.Equal(privacyService.HashUserId("U12345"), privacyService.HashUserId("U12345"));
        }

        [Fact]
        public void HashUserId_DiffersPerUser()
        {
            Assert.NotEqual(privacyService.HashUserId("U12345"), privacyService.HashUserId("U12346"));
        }

        [Fact]
        public void HashUserId_DoesNotContainOriginal()
        {
            Assert.DoesNotContain("U12345", privacyService.HashUserId("U12345"));
        }

        // Without the key the mapping can't be reproduced, so a hash dictionary built
        // elsewhere is useless against this store
        [Fact]
        public void HashUserId_DiffersPerKey()
        {
            var other = new PrivacyService(new PrivacyOptions("a-different-key"));

            Assert.NotEqual(privacyService.HashUserId("U12345"), other.HashUserId("U12345"));
        }

        [Theory]
        [InlineData("john@example.com", "j***n@example.com")]
        [InlineData("ab@example.com", "***@example.com")]
        [InlineData("a@example.com", "***@example.com")]
        public void MaskEmail_KeepsOnlyFirstAndLastCharacter(string input, string expected)
        {
            Assert.Equal(expected, privacyService.MaskEmail(input));
        }

        [Fact]
        public void MaskIpAddress_DropsHostOctetOfIpV4()
        {
            Assert.Equal("192.168.1.xxx", privacyService.MaskIpAddress("192.168.1.10"));
        }

        [Fact]
        public void MaskIpAddress_KeepsPrefixOfIpV6()
        {
            Assert.Equal(
                "2001:0db8:xxxx:xxxx:xxxx:xxxx:xxxx:xxxx",
                privacyService.MaskIpAddress("2001:0db8:85a3:0000:0000:8a2e:0370:7334"));
        }

        // The old implementation split on ':' and produced nonsense for shortened forms
        [Fact]
        public void MaskIpAddress_HandlesShortenedIpV6()
        {
            Assert.Equal(
                "0000:0000:xxxx:xxxx:xxxx:xxxx:xxxx:xxxx",
                privacyService.MaskIpAddress("::1"));
        }

        [Fact]
        public void MaskIpAddress_ReturnsPlaceholderForGarbage()
        {
            Assert.Equal("xxx.xxx.xxx.xxx", privacyService.MaskIpAddress("not-an-ip"));
        }
    }
}
