using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;

namespace ConsoleApp1_vdp_sheetbuilder
{
    public static class PdfUtilities
    {
        public static void RotateAndReversePdf(string inputFilePath, string outputFilePath, int rotationAngle, bool reverseOrder)
        {
            // Open the source PDF document
            PdfDocument sourceDocument = new PdfDocument(new PdfReader(inputFilePath));
            int totalPages = sourceDocument.GetNumberOfPages();

            // Create a new PDF document
            PdfDocument outputDocument = new PdfDocument(new PdfWriter(outputFilePath));

            // Determine the order of pages
            int[] pageOrder = new int[totalPages];
            for (int i = 0; i < totalPages; i++)
            {
                pageOrder[i] = reverseOrder ? totalPages - i : i + 1;
            }

            // Process each page
            foreach (int pageIndex in pageOrder)
            {
                PdfPage sourcePage = sourceDocument.GetPage(pageIndex);
                PdfPage newPage = outputDocument.AddNewPage(new PageSize(sourcePage.GetPageSizeWithRotation()));

                // Rotate the page
                newPage.SetRotation((sourcePage.GetRotation() + rotationAngle) % 360);

                // Copy content from the source page to the new page
                PdfCanvas canvas = new PdfCanvas(newPage);
                canvas.AddXObjectAt(sourcePage.CopyAsFormXObject(outputDocument), 0, 0);

                // Flush the canvas to release memory
                canvas.Release();
            }

            // Close the documents
            sourceDocument.Close();
            outputDocument.Close();
        }
    }
}