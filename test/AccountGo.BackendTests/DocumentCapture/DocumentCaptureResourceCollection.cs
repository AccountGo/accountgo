using Xunit;

namespace AccountGo.BackendTests.DocumentCapture;

// These tests deliberately occupy shared decoder slots or measure resource limits.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DocumentCaptureResourceCollection
{
    public const string Name = "Document capture resource tests";
}
