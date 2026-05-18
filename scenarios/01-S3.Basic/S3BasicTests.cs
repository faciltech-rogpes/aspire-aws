using Amazon.S3.Model;
using Xunit.Abstractions;

namespace Scenarios.S3.Basic;

public class S3BasicTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    [Fact]
    public async Task CreateBucket_ShouldSucceed()
    {
        output.WriteLine(">>> S3.PutBucket: criando bucket 'test-bucket-create'");
        await fixture.S3.PutBucketAsync("test-bucket-create");

        output.WriteLine(">>> S3.ListBuckets: verificando se o bucket aparece na listagem da conta");
        var buckets = await fixture.S3.ListBucketsAsync();
        output.WriteLine($"    Buckets encontrados: {string.Join(", ", buckets.Buckets.Select(b => b.BucketName))}");

        Assert.Contains(buckets.Buckets, bucket => bucket.BucketName == "test-bucket-create");
    }

    [Fact]
    public async Task PutAndGetObject_ShouldRoundTrip()
    {
        output.WriteLine(">>> S3.PutBucket: criando bucket 'test-bucket-rw'");
        await fixture.S3.PutBucketAsync("test-bucket-rw");

        output.WriteLine(">>> S3.PutObject: fazendo upload de 'hello.txt' com conteúdo 'hello world'");
        await fixture.S3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket-rw",
            Key = "hello.txt",
            ContentBody = "hello world"
        });

        output.WriteLine(">>> S3.GetObject: baixando 'hello.txt' e lendo o stream de resposta");
        using var response = await fixture.S3.GetObjectAsync("test-bucket-rw", "hello.txt");
        using var reader = new StreamReader(response.ResponseStream);
        var content = await reader.ReadToEndAsync();
        output.WriteLine($"    Conteúdo recebido: '{content}'");

        Assert.Equal("hello world", content);
    }

    [Fact]
    public async Task ListObjects_ShouldReturnUploadedKeys()
    {
        output.WriteLine(">>> S3.PutBucket: criando bucket 'test-bucket-list'");
        await fixture.S3.PutBucketAsync("test-bucket-list");

        output.WriteLine(">>> S3.PutObject: fazendo upload de 'a.txt'");
        await fixture.S3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket-list",
            Key = "a.txt",
            ContentBody = "a"
        });

        output.WriteLine(">>> S3.PutObject: fazendo upload de 'b.txt'");
        await fixture.S3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket-list",
            Key = "b.txt",
            ContentBody = "b"
        });

        output.WriteLine(">>> S3.ListObjectsV2: listando todos os objetos do bucket");
        var response = await fixture.S3.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = "test-bucket-list"
        });
        output.WriteLine($"    Objetos encontrados: {string.Join(", ", response.S3Objects.Select(o => o.Key))}");

        Assert.Equal(2, response.S3Objects.Count);
    }

    [Fact]
    public async Task GetPresignedUrl_ShouldReturnAccessibleUrl()
    {
        output.WriteLine(">>> S3.PutBucket: criando bucket 'test-bucket-presign'");
        await fixture.S3.PutBucketAsync("test-bucket-presign");

        output.WriteLine(">>> S3.PutObject: fazendo upload de 'file.txt'");
        await fixture.S3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket-presign",
            Key = "file.txt",
            ContentBody = "data"
        });

        output.WriteLine(">>> S3.GetPreSignedURL: gerando URL temporária válida por 5 minutos");
        var url = fixture.S3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = "test-bucket-presign",
            Key = "file.txt",
            Protocol = Amazon.S3.Protocol.HTTP,
            Expires = DateTime.UtcNow.AddMinutes(5)
        });
        output.WriteLine($"    URL gerada: {url}");

        output.WriteLine(">>> HttpClient.Get: acessando a URL pré-assinada via HTTP sem credenciais");
        using var http = new HttpClient();
        using var response = await http.GetAsync(url);
        output.WriteLine($"    HTTP status: {(int)response.StatusCode} {response.StatusCode}");

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task DeleteObject_ShouldRemoveKey()
    {
        output.WriteLine(">>> S3.PutBucket: criando bucket 'test-bucket-delete'");
        await fixture.S3.PutBucketAsync("test-bucket-delete");

        output.WriteLine(">>> S3.PutObject: fazendo upload de 'to-delete.txt'");
        await fixture.S3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket-delete",
            Key = "to-delete.txt",
            ContentBody = "x"
        });

        output.WriteLine(">>> S3.DeleteObject: removendo 'to-delete.txt' do bucket");
        await fixture.S3.DeleteObjectAsync("test-bucket-delete", "to-delete.txt");

        output.WriteLine(">>> S3.ListObjectsV2: confirmando que o bucket está vazio");
        var response = await fixture.S3.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = "test-bucket-delete"
        });
        output.WriteLine($"    Objetos restantes: {response.S3Objects.Count}");

        Assert.Empty(response.S3Objects);
    }
}
