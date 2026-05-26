using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Model;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Shared;

namespace Scenarios.StepFunctions.Orchestration;

public class Fixture : LocalStackFixture, IAsyncLifetime
{
  private const string FakeRole = "arn:aws:iam::000000000000:role/local-role";

  public const string FunctionName = "stepfunctions-task";
  public const string TableName = "sf-results";

  public AmazonStepFunctionsClient StepFunctions { get; private set; } = null!;
  public AmazonDynamoDBClient DynamoDB { get; private set; } = null!;
  public string StateMachineArn { get; private set; } = null!;

  protected override async Task InitializeScenarioAsync()
  {
    StepFunctions = AwsClientFactory.StepFunctions();
    DynamoDB = AwsClientFactory.DynamoDB();

    await DynamoDB.CreateTableAsync(new CreateTableRequest
    {
      TableName = TableName,
      AttributeDefinitions =
        [
            new AttributeDefinition("id", ScalarAttributeType.S)
        ],
      KeySchema =
        [
            new KeySchemaElement("id", KeyType.HASH)
        ],
      BillingMode = BillingMode.PAY_PER_REQUEST
    });

    await PollingHelper.WaitUntilAsync(async () =>
    {
      var table = await DynamoDB.DescribeTableAsync(TableName);
      return table.Table.TableStatus == TableStatus.ACTIVE;
    });

    using var lambda = AwsClientFactory.Lambda();
    await new LambdaDeployer(lambda).DeployAsync(FunctionName, "stepfunctions_task");

    var function = await lambda.GetFunctionAsync(new GetFunctionRequest
    {
      FunctionName = FunctionName
    });

    var definition = $$"""
        {
          "Comment": "Example Step Functions workflow",
          "StartAt": "ProcessStep",
          "States": {
            "ProcessStep": {
              "Type": "Task",
              "Resource": "{{function.Configuration.FunctionArn}}",
              "Parameters": { "step": "process", "id.$": "$.id" },
              "ResultPath": "$.result",
              "Next": "CheckResult"
            },
            "CheckResult": {
              "Type": "Choice",
              "Choices": [
                {
                  "Variable": "$.result.processed",
                  "BooleanEquals": true,
                  "Next": "SuccessState"
                }
              ],
              "Default": "FailState"
            },
            "SuccessState": {
              "Type": "Succeed"
            },
            "FailState": {
              "Type": "Fail",
              "Error": "ProcessingFailed"
            }
          }
        }
        """;

    var stateMachine = await StepFunctions.CreateStateMachineAsync(new CreateStateMachineRequest
    {
      Name = "example-workflow",
      Definition = definition,
      RoleArn = FakeRole,
      Type = StateMachineType.STANDARD
    });

    StateMachineArn = stateMachine.StateMachineArn;
  }

  protected override Task DisposeScenarioAsync()
  {
    StepFunctions.Dispose();
    DynamoDB.Dispose();
    return Task.CompletedTask;
  }
}
