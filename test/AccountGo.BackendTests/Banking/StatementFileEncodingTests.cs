// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Text;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class StatementFileEncodingTests
{
    public StatementFileEncodingTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void Decode_Utf8Bom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("café")).ToArray();
        Assert.Equal("café", StatementFileEncoding.Decode(bytes));
    }

    [Fact]
    public void Decode_Utf16LeBom()
    {
        var payload = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Payee ñ")).ToArray();
        Assert.Equal("Payee ñ", StatementFileEncoding.Decode(payload));
    }

    [Fact]
    public void Decode_Windows1252WhenNotValidUtf8()
    {
        // é in Windows-1252 is 0xE9 — invalid as a lone UTF-8 lead without continuation.
        var bytes = new byte[] { (byte)'J', (byte)'o', (byte)'s', 0xE9 };
        Assert.Equal("José", StatementFileEncoding.Decode(bytes));
    }

    [Fact]
    public void Decode_PlainUtf8WithoutBom()
    {
        Assert.Equal("ok", StatementFileEncoding.Decode(Encoding.UTF8.GetBytes("ok")));
    }
}
