using Amazon.StepFunctions.Model;
using Shared;
using System.Text.Json;
using Xunit.Abstractions;

namespace Scenarios.StepFunctions.Orchestration;

public class StepFunctionsTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    private const string SkipReason = "Requires LocalStack Step Functions support, which is often unavailable in Community edition.";

    [Fact(Skip = SkipReason)]
    public async Task StartExecution_ShouldCompleteSuccessfully()
    {
        var input = JsonSerializer.Serialize(new { id = "exec-001" });
        output.WriteLine($">>> StepFunctions.StartExecution: iniciando execução na state machine '{fixture.StateMachineArn}'");
        output.WriteLine($"    Input: {input}");
        var execution = await fixture.StepFunctions.StartExecutionAsync(new StartExecutionRequest
        {
            StateMachineArn = fixture.StateMachineArn,
            Name = "test-execution-001",
            Input = input
        });
        output.WriteLine($"    ExecutionArn: {execution.ExecutionArn}");

        output.WriteLine(">>> Polling StepFunctions.DescribeExecution: aguardando até 60s pelo status SUCCEEDED ou FAILED");
        await PollingHelper.WaitUntilAsync(async () =>
        {
            var description = await fixture.StepFunctions.DescribeExecutionAsync(new DescribeExecutionRequest
            {
                ExecutionArn = execution.ExecutionArn
            });

            return string.Equals(description.Status, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(description.Status, "FAILED", StringComparison.OrdinalIgnoreCase);
        }, timeout: TimeSpan.FromSeconds(60));

        output.WriteLine(">>> StepFunctions.DescribeExecution: lendo status final da execução");
        var final = await fixture.StepFunctions.DescribeExecutionAsync(new DescribeExecutionRequest
        {
            ExecutionArn = execution.ExecutionArn
        });
        output.WriteLine($"    Status final: {final.Status}");

        Assert.Equal("SUCCEEDED", final.Status);
    }

    [Fact(Skip = SkipReason)]
    public async Task ListExecutions_ShouldIncludeStartedExecution()
    {
        output.WriteLine($">>> StepFunctions.StartExecution: iniciando execução 'test-execution-list' na state machine '{fixture.StateMachineArn}'");
        var execution = await fixture.StepFunctions.StartExecutionAsync(new StartExecutionRequest
        {
            StateMachineArn = fixture.StateMachineArn,
            Name = "test-execution-list",
            Input = """{"id":"exec-list"}"""
        });
        output.WriteLine($"    ExecutionArn: {execution.ExecutionArn}");

        output.WriteLine(">>> StepFunctions.ListExecutions: listando execuções da state machine para verificar se a nova aparece");
        var list = await fixture.StepFunctions.ListExecutionsAsync(new ListExecutionsRequest
        {
            StateMachineArn = fixture.StateMachineArn
        });
        output.WriteLine($"    Execuções encontradas: {list.Executions.Count}");

        Assert.Contains(list.Executions, item => item.ExecutionArn == execution.ExecutionArn);
    }
}
