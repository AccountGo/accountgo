#nullable enable
using Core.Domain.Sales;
using Xunit;

namespace AccountGo.BackendTests.Sales
{
    public class DocumentNumberFormatTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Parse_NullOrWhitespace_ReturnsDefault(string? raw)
        {
            var fmt = DocumentNumberFormat.Parse(raw);
            Assert.True(fmt.IsDefault);
            Assert.False(fmt.UsesFiscalYear);
            Assert.Equal("1", fmt.Render(1));
            Assert.Equal("1043", fmt.Render(1043));
            Assert.True(fmt.TryParseSequence("42", null, out var parsed));
            Assert.Equal(42, parsed);
            Assert.False(fmt.TryParseSequence("INV-2026-0042", null, out _));
        }

        [Fact]
        public void Parse_And_Render_WithFiscalYearAndPadding()
        {
            var fmt = DocumentNumberFormat.Parse("INV-{FY}-{####}");
            Assert.False(fmt.IsDefault);
            Assert.True(fmt.UsesFiscalYear);
            Assert.Equal(4, fmt.SequenceWidth);

            Assert.Equal("INV-2026-0001", fmt.Render(1, "FY2026"));
            Assert.Equal("INV-2026-27-0012", fmt.Render(12, "FY2026-27"));
            Assert.Equal("INV-2027T-0005", fmt.Render(5, "FY2027T"));

            // Grows past width without truncation.
            Assert.Equal("INV-2026-12345", fmt.Render(12345, "FY2026"));
        }

        [Fact]
        public void Parse_And_Render_ContinuousWithPrefix()
        {
            var fmt = DocumentNumberFormat.Parse("INV-{#####}");
            Assert.False(fmt.IsDefault);
            Assert.False(fmt.UsesFiscalYear);
            Assert.Equal("INV-00001", fmt.Render(1));
            Assert.Equal("INV-123456", fmt.Render(123456));
        }

        [Fact]
        public void TryParseSequence_MatchesSameYear_AndRejectsOtherYearsOrFormats()
        {
            var fmt = DocumentNumberFormat.Parse("INV-{FY}-{####}");

            Assert.True(fmt.TryParseSequence("INV-2026-0007", "FY2026", out var seq));
            Assert.Equal(7, seq);

            // Case-insensitive match
            Assert.True(fmt.TryParseSequence("inv-2026-0042", "FY2026", out var seq2));
            Assert.Equal(42, seq2);

            // Over-width sequence parses back cleanly
            Assert.True(fmt.TryParseSequence("INV-2026-12345", "FY2026", out var seq3));
            Assert.Equal(12345, seq3);

            // Different fiscal year does not match
            Assert.False(fmt.TryParseSequence("INV-2027-0007", "FY2026", out _));

            // Plain integer does not match
            Assert.False(fmt.TryParseSequence("7", "FY2026", out _));
        }

        [Theory]
        [InlineData("INV-{FY}", "exactly one sequence token")]
        [InlineData("INV-{##}-{###}", "exactly one sequence token")]
        [InlineData("INV-{FY}-{FY}-{###}", "at most one {FY}")]
        [InlineData("INV-{YYYY}-{###}", "Unknown token")]
        [InlineData("INV-{FY-{###}", "unclosed")]
        [InlineData("INV-FY}-{###}", "unmatched")]
        [InlineData("-INV-{###}", "start with a letter or digit")]
        [InlineData("INV *{###}", "letters, digits")]
        [InlineData("LONG-PREFIX-12345-{FY}-{###}", "exceed 30 characters")]
        public void Parse_RejectsInvalidFormats(string format, string expectedMessagePart)
        {
            var ex = Assert.Throws<InvoiceNumberException>(() => DocumentNumberFormat.Parse(format));
            Assert.Equal(InvoiceNumberError.Invalid, ex.Error);
            Assert.Contains(expectedMessagePart, ex.Message);
        }
    }
}
