// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace Api.DocumentCaptures.Parsing;

/// <summary>
/// A line in reading order with page-pixel bounds measured from the top-left origin. <see cref="Rotated"/> marks text drawn
/// at an angle to the page's main text (a diagonal stamp or watermark); the worker emits it after that page's body text.
/// </summary>
public sealed record TextLine(int Page, string Text, float X0, float Y0, float X1, float Y1, TextLineSource Source,
    bool Rotated = false);

public enum TextLineSource
{
    PdfTextLayer,
    Ocr
}
