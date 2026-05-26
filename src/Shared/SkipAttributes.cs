using Xunit;

namespace Shared;

public sealed class SkipOnMacOsArm64LocalStackLambdaFactAttribute : FactAttribute
{
    public SkipOnMacOsArm64LocalStackLambdaFactAttribute()
    {
        if (EnvironmentLimitations.IsMacOsArm64LocalStackLambdaUnsupported)
        {
            Skip = EnvironmentLimitations.MacOsArm64LambdaReason;
        }
    }
}
