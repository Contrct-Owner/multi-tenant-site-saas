namespace Premise.FleetTests;

public sealed class ReportingFleetFactAttribute : FactAttribute
{
    public ReportingFleetFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PREMISE_REPORTING_FLEET") != "1")
            Skip =
                "Requires tools/replica-stack.sh 2 --reporting; this test kills a generating process.";
    }
}
