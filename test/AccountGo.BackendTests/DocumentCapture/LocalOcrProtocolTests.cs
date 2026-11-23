using System.Buffers.Binary;
using Api.DocumentCaptures;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class LocalOcrProtocolTests
{
    [Fact]
    public async Task RequestRoundTripsWithLittleEndianHeaderLength()
    {
        using var stream = new MemoryStream();
        var expected = new CaptureWorkerHeader("application/pdf", 10, 3);
        byte[] bytes = [1, 2, 3, 4];

        await CaptureWorkerProtocol.WriteAsync(stream, expected, bytes, CancellationToken.None);
        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(stream.ToArray());
        Assert.True(headerLength > 0);
        Assert.Equal(stream.Length - 4 - bytes.Length, headerLength);
        stream.Position = 0;

        var result = await CaptureWorkerProtocol.ReadAsync(stream);
        Assert.Equal(expected, result.Header);
        Assert.Equal(bytes, result.Bytes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1025)]
    public async Task RequestRejectsInvalidHeaderLength(int length)
    {
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, length);
        using var stream = new MemoryStream(prefix);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await CaptureWorkerProtocol.ReadAsync(stream));
    }

    [Theory]
    [InlineData("application/pdf", 11, 3)]
    [InlineData("image/jpeg", 2, 3)]
    [InlineData("image/png", 1, 4)]
    [InlineData("text/plain", 1, 3)]
    public async Task RequestRejectsInvalidHeader(string mime, int pages, int ocrPages)
    {
        using var stream = new MemoryStream();
        await CaptureWorkerProtocol.WriteAsync(stream, new(mime, pages, ocrPages), new byte[] { 1 }, CancellationToken.None);
        stream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(async () => await CaptureWorkerProtocol.ReadAsync(stream));
    }

    [Fact]
    public async Task RequestRejectsInputAboveLimit()
    {
        using var stream = new MemoryStream();
        await CaptureWorkerProtocol.WriteAsync(stream, new("image/png", 1, 3),
            new byte[CaptureWorkerProtocol.MaxInputBytes + 1], CancellationToken.None);
        stream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(async () => await CaptureWorkerProtocol.ReadAsync(stream));
    }
}
