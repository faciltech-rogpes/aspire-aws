var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
{
    Args = args,
    DisableDashboard = true
});

builder
    .AddContainer("localstack", "localstack/localstack", "3.8")
    .WithEnvironment("AWS_ACCESS_KEY_ID", "test")
    .WithEnvironment("AWS_DEFAULT_REGION", "us-east-1")
    .WithEnvironment("AWS_SECRET_ACCESS_KEY", "test")
    .WithEnvironment("SERVICES", "s3,sqs,sns,dynamodb,lambda,ssm,secretsmanager,events,stepfunctions")
    .WithEnvironment("DOCKER_HOST", "unix:///var/run/docker.sock")
    .WithEnvironment("LAMBDA_RUNTIME_ENVIRONMENT_TIMEOUT", "120")
    .WithEnvironment("LAMBDA_REMOVE_CONTAINERS", "true")
    .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
    .WithHttpEndpoint(port: 4566, targetPort: 4566, name: "gateway", isProxied: false);

builder.Build().Run();
