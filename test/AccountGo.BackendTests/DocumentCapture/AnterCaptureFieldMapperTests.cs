// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Net;
using System.Text;
using Api.DocumentCaptures;
using Api.DocumentCaptures.Mapping;
using Api.DocumentCaptures.Parsing;
using Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

public sealed class AnterCaptureFieldMapperTests
{
    private const string RuleFields = """{"vendorName":"Northstar Supplies Ltd","date":"2026-11-12"}""";
    private const string MappedFields = """{"vendorName":"Northstar Supplies Ltd","date":"2026-11-12","vendorInvoiceNo":"NS-20457"}""";

    private static CaptureMappingRequest Request(params string[] lines) =>
        CaptureMappingRequestFactory.Create("purchase-invoice", 1, RuleFields,
            lines.Select(text => new TextLine(1, text, 0, 0, 1, 1, TextLineSource.Ocr)).ToArray(), "rules-3");

    private static string Sse(string reply) =>
        "event: started\ndata: {\"sessionId\":\"s\"}\n\n"
        + $"data: {System.Text.Json.JsonSerializer.Serialize(new { type = "content", payload = new { text = reply } })}\n\n";

    private static (AnterCaptureFieldMapper Mapper, StubHandler Handler, LogCollector Log) Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, int timeoutSeconds = 6)
    {
        var handler = new StubHandler(respond);
        var log = new LogCollector();
        var mapper = new AnterCaptureFieldMapper(
            new HttpClient(handler),
            Options.Create(new AnterOptions { BaseUrl = "https://anter.test", ApiKey = "test-key", ProjectId = "proj" }),
            Options.Create(new DocumentCaptureOptions { AiMapping = new DocumentCaptureAiMappingOptions { Enabled = true, TimeoutSeconds = timeoutSeconds } }),
            log);
        return (mapper, handler, log);
    }

    private static Task<HttpResponseMessage> Ok(string sse) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
    });

    private static Task<HttpResponseMessage> Respond(HttpRequestMessage request, Func<Task<HttpResponseMessage>> run) =>
        request.Method == HttpMethod.Delete
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent))
            : run();

    [Fact]
    public async Task ReadsAFinalMessageSplitAcrossManyChunks()
    {
        var sse = Sse(MappedFields);
        var (mapper, handler, _) = Create((request, _) => Respond(request, () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new TrickleStream(Encoding.UTF8.GetBytes(sse), 3)),
        })));

        var result = await mapper.MapAsync(Request("Invoice No: NS-20457"), CancellationToken.None);

        Assert.Equal("""{"vendorName":"Northstar Supplies Ltd","date":"2026-11-12","vendorInvoiceNo":"NS-20457"}""", result.FieldsJson);
        Assert.Equal("anter-capture-mapper", result.MapperName);
        Assert.Equal("accountgo-capture-mapper@mapper-contract-1", result.MapperVersion);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task JoinsContentEventsAndToleratesACodeFence()
    {
        static string Event(string text) => $"data: {System.Text.Json.JsonSerializer.Serialize(new { type = "content", payload = new { text } })}\n\n";
        var sse = Event("```json\n{\"vendorName\":\"A\"") + Event("}\n```");
        var (mapper, _, _) = Create((request, _) => Respond(request, () => Ok(sse)));

        var result = await mapper.MapAsync(Request("A"), CancellationToken.None);

        Assert.Equal("""{"vendorName":"A"}""", result.FieldsJson);
    }

    [Theory]
    [InlineData("Sorry, I could not read that document.")]
    [InlineData("[1,2,3]")]
    [InlineData("")]
    public async Task ANonJsonReplyIsUnusableAndTheRunnerFallsBackToTheRules(string reply)
    {
        var (mapper, handler, _) = Create((request, _) => Respond(request, () => Ok(Sse(reply))));

        await Assert.ThrowsAsync<FormatException>(() => mapper.MapAsync(Request("A"), CancellationToken.None));
        var outcome = await CaptureFieldMappingRunner.RunAsync(mapper, "purchase-invoice", 1, RuleFields,
            [new TextLine(1, "A", 0, 0, 1, 1, TextLineSource.Ocr)], "rules-3", CancellationToken.None);

        Assert.Equal(RuleFields, outcome.FieldsJson);
        Assert.Equal([CaptureMappingGuards.WarningUnavailable], outcome.Warnings);
        Assert.Equal(4, handler.Requests.Count); // two runs, each followed by a session delete
    }

    [Fact]
    public async Task TheSessionIsDeletedAfterSuccess()
    {
        var (mapper, handler, _) = Create((request, _) => Respond(request, () => Ok(Sse(MappedFields))));

        await mapper.MapAsync(Request("Invoice No: NS-20457"), CancellationToken.None);

        AssertSessionDeleted(handler);
    }

    [Fact]
    public async Task TheSessionIsDeletedAfterAFailure()
    {
        var (mapper, handler, _) = Create((request, _) => Respond(request, () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))));

        await Assert.ThrowsAsync<HttpRequestException>(() => mapper.MapAsync(Request("A"), CancellationToken.None));

        AssertSessionDeleted(handler);
    }

    [Fact]
    public async Task TheSessionIsDeletedAfterATimeout_AndTheCallEndsWithinTheBudget()
    {
        var (mapper, handler, _) = Create(async (request, token) =>
        {
            if (request.Method == HttpMethod.Delete)
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, timeoutSeconds: 1);

        var started = DateTime.UtcNow;
        await Assert.ThrowsAsync<TimeoutException>(() => mapper.MapAsync(Request("A"), CancellationToken.None));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        AssertSessionDeleted(handler);
    }

    [Fact]
    public async Task TheSessionIsStillDeletedWhenTheCallersTokenIsCancelled()
    {
        using var cancel = new CancellationTokenSource();
        var (mapper, handler, _) = Create(async (request, token) =>
        {
            if (request.Method == HttpMethod.Delete)
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            await cancel.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mapper.MapAsync(Request("A"), cancel.Token));

        AssertSessionDeleted(handler);
    }

    [Fact]
    public async Task ThePayloadHasNoAccountNumberOrIbanFromTheSourceAndNoImages()
    {
        var (mapper, handler, _) = Create((request, _) => Respond(request, () => Ok(Sse(RuleFields))));

        await CaptureFieldMappingRunner.RunAsync(mapper, "purchase-invoice", 1, RuleFields,
        [
            new TextLine(1, "Pay to GB82 WEST 1234 5698 7654 32 account 123456789012", 0, 0, 1, 1, TextLineSource.Ocr),
            new TextLine(1, "Invoice No: NS-20457", 0, 2, 1, 3, TextLineSource.Ocr),
        ], "rules-3", CancellationToken.None);

        var body = handler.Requests.Single(r => r.Method == HttpMethod.Post).Body!;
        Assert.DoesNotContain("GB82", body, StringComparison.Ordinal);
        Assert.DoesNotContain("123456789012", body, StringComparison.Ordinal);
        Assert.DoesNotContain("7654 32", body, StringComparison.Ordinal);
        Assert.Contains("NS-20457", body, StringComparison.Ordinal);
        Assert.DoesNotContain("image", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheRequestUsesAFreshSessionASyntheticUserTheMapperSlugAndTheKey()
    {
        var (mapper, handler, _) = Create((request, _) => Respond(request, () => Ok(Sse(RuleFields))));
        using (CaptureMappingScope.Begin("ws_01companyabc", "cap_01capture"))
            await mapper.MapAsync(Request("A"), CancellationToken.None);
        using (CaptureMappingScope.Begin("ws_01companyabc", "cap_02capture"))
            await mapper.MapAsync(Request("A"), CancellationToken.None);

        var posts = handler.Requests.Where(r => r.Method == HttpMethod.Post).ToList();
        Assert.All(posts, post =>
        {
            Assert.Equal("https://anter.test/api/v1/external/agent-runner/agents/by-slug/accountgo-capture-mapper/run-stream", post.Uri);
            Assert.Equal("test-key", post.ApiKey);
            Assert.Contains("\"userId\":\"capture-mapper_ws_01companyabc\"", post.Body, StringComparison.Ordinal);
        });
        var sessions = posts.Select(post => System.Text.RegularExpressions.Regex.Match(post.Body!, "\"sessionId\":\"([0-9a-f]{32})\"").Groups[1].Value).ToList();
        Assert.All(sessions, id => Assert.NotEmpty(id));
        Assert.Equal(2, sessions.Distinct().Count());
    }

    [Fact]
    public async Task LogsCarryMetadataOnlyNeverThePayloadTheReplyOrTheMessage()
    {
        var (mapper, _, log) = Create((request, _) => Respond(request, () => Ok(Sse("Northstar secret reply 4111111111111111"))));
        using (CaptureMappingScope.Begin("ws_01companyabc", "cap_01capture"))
            await Assert.ThrowsAsync<FormatException>(() => mapper.MapAsync(Request("Invoice No: NS-20457 Northstar"), CancellationToken.None));

        var all = string.Join('\n', log.Entries);
        Assert.Contains("cap_01capture", all, StringComparison.Ordinal);
        Assert.Contains("Outcome=unusable", all, StringComparison.Ordinal);
        Assert.DoesNotContain("NS-20457", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Northstar", all, StringComparison.Ordinal);
        Assert.DoesNotContain("4111", all, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnconfiguredKeyIsUnavailableWithoutAnyHttpCall()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("no call expected"));
        var mapper = new AnterCaptureFieldMapper(new HttpClient(handler), Options.Create(new AnterOptions { ApiKey = "" }),
            Options.Create(new DocumentCaptureOptions()), new LogCollector());

        await Assert.ThrowsAsync<InvalidOperationException>(() => mapper.MapAsync(Request("A"), CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    private static void AssertSessionDeleted(StubHandler handler)
    {
        var post = handler.Requests.Single(r => r.Method == HttpMethod.Post);
        var sessionId = System.Text.RegularExpressions.Regex.Match(post.Body!, "\"sessionId\":\"([0-9a-f]{32})\"").Groups[1].Value;
        Assert.NotEmpty(sessionId);
        var delete = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Delete);
        Assert.Contains($"/sessions/{sessionId}?userId=", delete.Uri, StringComparison.Ordinal);
    }

    private sealed record Recorded(HttpMethod Method, string Uri, string? Body, string? ApiKey);

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<Recorded> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            request.Headers.TryGetValues("x-api-key", out var key);
            lock (Requests)
                Requests.Add(new Recorded(request.Method, request.RequestUri!.ToString(), body, key?.SingleOrDefault()));
            return await respond(request, cancellationToken);
        }
    }

    /// <summary>Delivers a body a few bytes at a time, like a slow network.</summary>
    private sealed class TrickleStream(byte[] bytes, int chunk) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(Math.Min(chunk, count), bytes.Length - _position);
            Array.Copy(bytes, _position, buffer, offset, n);
            _position += n;
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class LogCollector : ILogger<AnterCaptureFieldMapper>
    {
        public List<string> Entries { get; } = [];
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
                text += " " + string.Join(' ', values.Where(v => v.Key != "{OriginalFormat}").Select(v => $"{v.Key}={v.Value}"));
            Entries.Add(text + (exception is null ? string.Empty : " EXC:" + exception.Message));
        }
    }
}
