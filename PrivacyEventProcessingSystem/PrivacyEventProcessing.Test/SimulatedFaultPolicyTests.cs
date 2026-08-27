using PrivacyEventProcessing.Domain.Models;
using PrivacyEventProcessing.Integration.Engine;

namespace PrivacyEventProcessing.Test
{
    public class SimulatedFaultPolicyTests
    {
        private const int Samples = 200_000;

        private static (int processing, int unknown, int none) Sample(FaultInjectionOptions options)
        {
            var policy = new SimulatedFaultPolicy(options);
            int processing = 0, unknown = 0, none = 0;

            for (int i = 0; i < Samples; i++)
            {
                ProcessingFault? fault = policy.NextFault();

                switch (fault?.Reason)
                {
                    case FailureReasonType.ProcessingError: processing++; break;
                    case FailureReasonType.UnknownError: unknown++; break;
                    case null: none++; break;
                    default: Assert.Fail($"unexpected reason {fault!.Reason}"); break;
                }
            }

            return (processing, unknown, none);
        }

        // Rates are a share of all events, not of the failures. At 200k samples the SD of an
        // observed 3.5% rate is ~0.04%, so a 1% tolerance is well outside noise.
        [Fact]
        public void DefaultRatesProduceRoughlyFivePercentFailuresSplitAcrossTwoReasons()
        {
            var (processing, unknown, none) = Sample(new FaultInjectionOptions());

            Assert.Equal(Samples, processing + unknown + none);

            Assert.InRange((double)processing / Samples, 0.030, 0.040);
            Assert.InRange((double)unknown / Samples, 0.002, 0.008);
            Assert.InRange((double)(processing + unknown) / Samples, 0.035, 0.045);

            // ProcessingError is the dominant category by design
            Assert.True(processing > unknown * 3);
        }

        [Fact]
        public void ZeroRatesNeverProduceAFault()
        {
            var (processing, unknown, none) = Sample(new FaultInjectionOptions
            {
                ProcessingErrorRate = 0,
                UnknownErrorRate = 0
            });

            Assert.Equal(0, processing);
            Assert.Equal(0, unknown);
            Assert.Equal(Samples, none);
        }

        [Fact]
        public void ARateOfOneAlwaysProducesAFault()
        {
            var (processing, unknown, none) = Sample(new FaultInjectionOptions
            {
                ProcessingErrorRate = 1,
                UnknownErrorRate = 0
            });

            Assert.Equal(Samples, processing);
            Assert.Equal(0, unknown);
            Assert.Equal(0, none);
        }

        // Adjacent bands off one roll, so raising one rate cannot silently eat the other
        [Fact]
        public void RatesDoNotOverlap()
        {
            var (processing, unknown, none) = Sample(new FaultInjectionOptions
            {
                ProcessingErrorRate = 0.5,
                UnknownErrorRate = 0.5
            });

            Assert.Equal(0, none);
            Assert.InRange((double)processing / Samples, 0.48, 0.52);
            Assert.InRange((double)unknown / Samples, 0.48, 0.52);
        }

        [Fact]
        public void FailureMessagesVaryWithinAReason()
        {
            var policy = new SimulatedFaultPolicy(new FaultInjectionOptions
            {
                ProcessingErrorRate = 1,
                UnknownErrorRate = 0
            });

            var messages = new HashSet<string>();
            for (int i = 0; i < 500; i++)
            {
                messages.Add(policy.NextFault()!.Message);
            }

            // 100 identical rows on the dashboard would tell an operator nothing
            Assert.True(messages.Count > 1, "every ProcessingError carried the same message");
            Assert.All(messages, message => Assert.False(string.IsNullOrWhiteSpace(message)));
        }

        [Theory]
        [InlineData(-0.1, 0)]
        [InlineData(0, -0.1)]
        [InlineData(0.7, 0.7)]
        public void InvalidRatesAreRejected(double processingRate, double unknownRate)
        {
            var options = new FaultInjectionOptions
            {
                ProcessingErrorRate = processingRate,
                UnknownErrorRate = unknownRate
            };

            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulatedFaultPolicy(options));
        }
    }
}
