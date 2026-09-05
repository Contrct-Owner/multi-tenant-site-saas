namespace Premise.IntegrationTests;

public sealed class ScaleFactAttribute : FactAttribute
{
    public ScaleFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PREMISE_SCALE_BASELINE") != "1")
            Skip = "Opt-in baseline: run tools/scale-baseline.sh";
    }
}
