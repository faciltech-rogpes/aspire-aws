using Amazon.S3.Model;
using Xunit.Abstractions;

namespace Scenarios.S3.Basic;

public class S3BasicTests(Fixture fixture, ITestOutputHelper output) : IClassFixture<Fixture>
{
    [Fact]
    public async Task CreateBucket_ShouldSucceed()
    {
        output.WriteLine("Criando bucket...");
        await fixture.S3.PutBucketAsync("test-bucket-create");

        var buckets = await fixture.S3.ListBucketsAsync();
        output.WriteLine($"Buckets encontrados: {buckets.Buckets.Count}");

        Assert.Contains(buckets.Buckets, bucket => bucket.BucketName == "test-bucket-create");
    }

    [Fact]
    public async Task PutAndGetObject_ShouldRoundTrip()
    {
        await fixture.S3.PutBucketAsync("test-bucket-rw");
        await fixture.S3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket-rw",
            Key = "hello.txt",
            ContentBody = "hello world"
        });

        using var response = await fixture.S3.GetObjectAsync("test-bucket-rw", "hello.txt");
        using var reader = new StreamReader(response.ResponseStream);

        Assert.Equal("hello world", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task ListObjects_ShouldReturnUploadedKeys()
    {
        await fixture.S3.PutBucketAsync("test-bucket-list");
        await fixture.S3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket-list",
            Key = "a.txt",
            ContentBody = "a"
        });
        await fixture.S3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket-list",
            Key = "b.txt",
            ContentBody = "b"
        });

        var response = await fixture.S3.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = "test-bucket-list"
        });

        Assert.Equal(2, response.S3Objects.Count);
    }

    [Fact]
    public async Task GetPresignedUrl_ShouldReturnAccessibleUrl()
    {
        await fixture.S3.PutBucketAsync("test-bucket-presign");
        await fixture.S3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket-presign",
            Key = "file.txt",
            ContentBody = "data"
        });

        var url = fixture.S3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = "test-bucket-presign",
            Key = "file.txt",
            Protocol = Amazon.S3.Protocol.HTTP,
            Expires = DateTime.UtcNow.AddMinutes(5)
        });

        using var http = new HttpClient();
        using var response = await http.GetAsync(url);

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task DeleteObject_ShouldRemoveKey()
    {
        await fixture.S3.PutBucketAsync("test-bucket-delete");
        await fixture.S3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket-delete",
            Key = "to-delete.txt",
            ContentBody = "x"
        });

        await fixture.S3.DeleteObjectAsync("test-bucket-delete", "to-delete.txt");

        var response = await fixture.S3.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = "test-bucket-delete"
        });

        Assert.Empty(response.S3Objects);
    }
}
