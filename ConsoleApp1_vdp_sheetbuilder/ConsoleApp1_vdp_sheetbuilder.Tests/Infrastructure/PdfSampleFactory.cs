using System;
using System.IO;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;

namespace ConsoleApp1_vdp_sheetbuilder.Tests.Infrastructure;

public static class PdfSampleFactory
{
    public static Stream CreateSamplePdf(int pageCount = 2)
    {
        if (pageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageCount), "Page count must be positive.");
        }

        using var workingStream = new MemoryStream();
        using (var pdfDocument = new PdfDocument(new PdfWriter(workingStream)))
        using (var document = new Document(pdfDocument, PageSize.A4))
        {
            for (int i = 0; i < pageCount; i++)
            {
                document.Add(new Paragraph($"Sample page {i + 1}"));
                if (i < pageCount - 1)
                {
                    pdfDocument.AddNewPage();
                }
            }
        }

        return new MemoryStream(workingStream.ToArray());
    }
}
