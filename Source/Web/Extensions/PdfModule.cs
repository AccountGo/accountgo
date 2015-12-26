//-----------------------------------------------------------------------
// <copyright file="PdfModule.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Web;
using System.Web.UI;

public class PdfModule : IHttpModule
{
    public void Init(HttpApplication context)
    {
        context.PreRequestHandlerExecute += context_BeginRequest;
    }

    private void context_BeginRequest(object sender, EventArgs e)
    {
        var httpApplication = (HttpApplication)sender;

        if (httpApplication.Request.Params["Pdf"] == null)
            return;

        httpApplication.Response.Clear();
        httpApplication.Response.AddHeader("Content-Type", "application/pdf");

        var baseUrl = string.Format("{0}://{1}{2}/", httpApplication.Request.Url.Scheme,
            httpApplication.Request.Url.Authority, httpApplication.Request.Path.TrimEnd('/'));
        httpApplication.Response.Filter = new PdfFilter(httpApplication.Response.Filter, baseUrl);
    }

    public void Dispose()
    {
    }
}
