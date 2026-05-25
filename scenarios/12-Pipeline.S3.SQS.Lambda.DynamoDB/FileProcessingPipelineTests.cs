using Amazon.DynamoDBv2.Model;
using Amazon.S3.Model;
using Shared;
using Xunit.Abstractions;

namespace Scenarios.Pipeline.S3SqsLambdaDynamoDb;

public class FileProcessingPipelineTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    [SkipOnMacOsArm64LocalStackLambdaFact]
    public async Task UploadToS3_ShouldFlowThroughSqsToLambdaAndIntoDynamoDb()
    {
        output.WriteLine($">>> S3.PutObject: fazendo upload de 'invoice-001.pdf' no bucket '{Fixture.BucketName}'");
        output.WriteLine("    Pipeline: S3 emite evento → SQS recebe notificação → Lambda consome fila → DynamoDB grava resultado");
        await fixture.S3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Fixture.BucketName,
            Key = "invoice-001.pdf",
            ContentBody = "invoice data"
        });

        output.WriteLine($">>> Polling DynamoDB: aguardando até 45s pelo pipeline completo — S3→SQS→Lambda→DynamoDB '{Fixture.TableName}'");
        await PollingHelper.WaitUntilAsync(async () =>
        {
            var scan = await fixture.DynamoDB.ScanAsync(new ScanRequest
            {
                TableName = Fixture.TableName
            });

            return scan.Items.Any(item =>
                item.TryGetValue("body", out var body) &&
                body.S.Contains("invoice-001.pdf", StringComparison.Ordinal));
        }, timeout: TimeSpan.FromSeconds(120));

        output.WriteLine("    Registro encontrado na tabela — pipeline completo executado com sucesso");
    }
}
