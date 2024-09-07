namespace Core.Domain.Error
{
    public sealed record Error(string Code, string Message)
    {
        private static readonly string RecordNotFoundCode = "RecordNotFound";
        private static readonly string ValidationErrorCode = "ValidationError";
        private static readonly string NullErrorCode = "NullError";

        public static readonly Error None = new(string.Empty, string.Empty);
        public static Error RecordNotFound(string message) => new Error(RecordNotFoundCode, message);
        public static Error ValidationError(string message) => new Error(ValidationErrorCode, message);
        public static Error NullError(string message) => new Error(NullErrorCode, message);
    }
}
