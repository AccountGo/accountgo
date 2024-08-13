namespace Core.Domain.Error
{
    public sealed record Error(string Code, string Message)
    {
        private static readonly string RecordNotFoundCode = "RecordNotFound";
        private static readonly string ValidationErrorCode = "ValidationError";

        public static readonly Error None = new(string.Empty, string.Empty);
        public static Error RecordNotFound(string message) => new Error(RecordNotFoundCode, message);
        public static Error ValidationError(string message) => new Error(ValidationErrorCode, message);
    }
}
