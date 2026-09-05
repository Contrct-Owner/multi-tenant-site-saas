using Premise.Platform.Messaging;

namespace Premise.Platform.UnitTests;

public class SweepIdentityTests
{
    [Fact]
    public void Identical_simple_names_have_distinct_stable_identities()
    {
        Assert.Equal(nameof(First.Sweep), nameof(Second.Sweep));
        Assert.NotEqual(SweepIdentity.For<First.Sweep>(), SweepIdentity.For<Second.Sweep>());
        Assert.Equal(
            "Premise.Platform.UnitTests:Premise.Platform.UnitTests.SweepIdentityTests+First+Sweep",
            SweepIdentity.For<First.Sweep>()
        );
    }

    private static class First
    {
        public sealed record Sweep;
    }

    private static class Second
    {
        public sealed record Sweep;
    }
}
