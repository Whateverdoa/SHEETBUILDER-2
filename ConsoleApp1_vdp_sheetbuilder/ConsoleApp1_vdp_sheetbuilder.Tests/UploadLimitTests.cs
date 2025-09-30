using System;
using System.Linq;
using ConsoleApp1_vdp_sheetbuilder.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ConsoleApp1_vdp_sheetbuilder.Tests;

public class UploadLimitTests
{
    [Fact]
    public void ProcessPdf_ShouldNotDeclareRequestSizeLimitAttribute()
    {
        var method = typeof(PdfController).GetMethod(nameof(PdfController.ProcessPdf));
        Assert.NotNull(method);

        var limitAttributes = method!.GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: true);
        Assert.False(limitAttributes.Length > 0, "ProcessPdf should not set RequestSizeLimit; rely on global configuration instead.");
    }

    [Fact]
    public void ProcessPdfWithProgress_ShouldNotDeclareRequestSizeLimitAttribute()
    {
        var method = typeof(PdfController).GetMethod(nameof(PdfController.ProcessPdfWithProgress));
        Assert.NotNull(method);

        var limitAttributes = method!.GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: true);
        Assert.False(limitAttributes.Length > 0, "ProcessPdfWithProgress should not set RequestSizeLimit; rely on global configuration instead.");
    }
}
