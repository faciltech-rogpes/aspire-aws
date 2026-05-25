using System.Runtime.InteropServices;
using Xunit;

namespace Shared;

public static class EnvironmentLimitations
{
    public const string MacOsArm64LambdaReason =
        "Skipped on macOS ARM64: LocalStack 3.8 Lambda invocation is unreliable in this environment.";

    public const string LocalStackRdsApiReason =
        "RDS control plane API (CreateDBInstance, DescribeDBInstances, ModifyDBInstance) is a Pro feature — not available in LocalStack Community 3.8.";

    public const string LocalStackEcsApiReason =
        "ECS control plane API (CreateCluster, RegisterTaskDefinition, RunTask) is a Pro feature — not available in LocalStack Community 3.8.";

    public static bool IsMacOsArm64LocalStackLambdaUnsupported =>
        OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
}

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
