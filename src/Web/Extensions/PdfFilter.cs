//-----------------------------------------------------------------------
// <copyright file="PdfFilter.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.IO;
using System.Text;

public class PdfFilter : Stream
{
    private readonly Stream _oldFilter;
    private readonly string _baseUrl;
    private MemoryStream _memStream;

    public override bool CanSeek
    {
        get { return false; }
    }

    public override bool CanWrite
    {
        get { return true; }
    }

    public override bool CanRead
    {
        get { return false; }
    }

    public override long Position
    {
        get { return 0L; }
        set { }
    }

    public override long Length
    {
        get { return 0L; }
    }

    public PdfFilter(Stream oldFilter, string baseUrl)
    {
        _oldFilter = oldFilter;
        _baseUrl = baseUrl;
        _memStream = new MemoryStream();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return 0L;
    }

    public override void SetLength(long value)
    {
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _memStream.Write(buffer, offset, count);
    }

    public override void Flush()
    {
    }

    public override void Close()
    {
        //var converter = new PdfConverter
        //{
        //    MediaType = "print",
        //};
        //converter.PdfDocumentOptions.LiveUrlsEnabled = false;

        //_memStream.Position = 0;

        //converter.SavePdfFromHtmlStreamToStream(_memStream, Encoding.UTF8, _baseUrl, _oldFilter);
        
        var pdfBytes = (new NReco.PdfGenerator.HtmlToPdfConverter()).GeneratePdf(_baseUrl);
        _oldFilter.Write(pdfBytes, 0, pdfBytes.Length);
        _oldFilter.Close();
    }
}
