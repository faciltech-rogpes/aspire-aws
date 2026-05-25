using Amazon;
using Amazon.DynamoDBv2;
using Amazon.ECS;
using Amazon.EventBridge;
using Amazon.Lambda;
using Amazon.RDS;
using Amazon.Runtime;
using Amazon.Runtime.Internal;
using Amazon.S3;
using Amazon.Scheduler;
using Amazon.SecretsManager;
using Amazon.SecurityToken;
using Amazon.SimpleNotificationService;
using Amazon.SimpleSystemsManagement;
using Amazon.SQS;
using Amazon.StepFunctions;

namespace Shared;

public static class AwsClientFactory
{
    private static BasicAWSCredentials Credentials => new("test", "test");

    private static bool ModoLocalStack =>
        !string.Equals(Environment.GetEnvironmentVariable("AWS_TARGET"), "aws",
            StringComparison.OrdinalIgnoreCase);

    public static AmazonS3Client S3(string endpoint = LocalStackFixture.Endpoint) =>
        ModoLocalStack
            ? new AmazonS3Client(Credentials, Configure(new AmazonS3Config { ForcePathStyle = true }, endpoint))
            : new AmazonS3Client();

    public static AmazonSQSClient SQS(string endpoint = LocalStackFixture.Endpoint) =>
        ModoLocalStack
            ? new AmazonSQSClient(Credentials, Configure(new AmazonSQSConfig(), endpoint))
            : new AmazonSQSClient();

    public static AmazonSimpleNotificationServiceClient SNS(string endpoint = LocalStackFixture.Endpoint) =>
        ModoLocalStack
            ? new AmazonSimpleNotificationServiceClient(Credentials, Configure(new AmazonSimpleNotificationServiceConfig(), endpoint))
            : new AmazonSimpleNotificationServiceClient();

    public static AmazonDynamoDBClient DynamoDB(string endpoint = LocalStackFixture.Endpoint) =>
        ModoLocalStack
            ? new AmazonDynamoDBClient(Credentials, Configure(new AmazonDynamoDBConfig(), endpoint))
            : new AmazonDynamoDBClient();

    public static AmazonLambdaClient Lambda(string endpoint = LocalStackFixture.Endpoint) =>
        ModoLocalStack
            ? new AmazonLambdaClient(Credentials, Configure(new AmazonLambdaConfig(), endpoint))
            : new AmazonLambdaClient();

    public static AmazonSimpleSystemsManagementClient SSM(string endpoint = LocalStackFixture.Endpoint) =>
        ModoLocalStack
            ? new AmazonSimpleSystemsManagementClient(Credentials, Configure(new AmazonSimpleSystemsManagementConfig(), endpoint))
            : new AmazonSimpleSystemsManagementClient();

    public static AmazonSecretsManagerClient SecretsManager(string endpoint = LocalStackFixture.Endpoint) =>
        ModoLocalStack
            ? new AmazonSecretsManagerClient(Credentials, Configure(new AmazonSecretsManagerConfig(), endpoint))
            : new AmazonSecretsManagerClient();

    public static AmazonEventBridgeClient EventBridge(string endpoint = LocalStackFixture.Endpoint) =>
        ModoLocalStack
            ? new AmazonEventBridgeClient(Credentials, Configure(new AmazonEventBridgeConfig(), endpoint))
            : new AmazonEventBridgeClient();

    public static AmazonSchedulerClient Scheduler(string endpoint = LocalStackFixture.Endpoint) =>
        ModoLocalStack
            ? new AmazonSchedulerClient(Credentials, Configure(new AmazonSchedulerConfig(), endpoint))
            : new AmazonSchedulerClient();

    public static AmazonSecurityTokenServiceClient STS(string endpoint = LocalStackFixture.Endpoint) =>
        ModoLocalStack
            ? new AmazonSecurityTokenServiceClient(Credentials, Configure(new AmazonSecurityTokenServiceConfig(), endpoint))
            : new AmazonSecurityTokenServiceClient();

    public static AmazonStepFunctionsClient StepFunctions(string endpoint = LocalStackFixture.Endpoint) =>
        ModoLocalStack
            ? new AmazonStepFunctionsClient(Credentials, Configure(new AmazonStepFunctionsConfig(), endpoint))
            : new AmazonStepFunctionsClient();

    public static AmazonRDSClient RDS(string endpoint = LocalStackFixture.Endpoint) =>
        ModoLocalStack
            ? new AmazonRDSClient(Credentials, Configure(new AmazonRDSConfig(), endpoint))
            : new AmazonRDSClient();

    public static AmazonECSClient ECS(string endpoint = LocalStackFixture.Endpoint) =>
        ModoLocalStack
            ? new AmazonECSClient(Credentials, Configure(new AmazonECSConfig(), endpoint))
            : new AmazonECSClient();

    private static TConfig Configure<TConfig>(TConfig config, string endpoint)
        where TConfig : ClientConfig
    {
        config.ServiceURL = endpoint;
        config.AuthenticationRegion = RegionEndpoint.USEast1.SystemName;
        config.UseHttp = endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        return config;
    }
}
