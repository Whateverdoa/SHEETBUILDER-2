using System.Diagnostics;
using System.Linq;
using System.Net;
using iText.Kernel.Pdf;
using iText.Kernel.Geom;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Xobject;
using iText.IO.Source;
using ConsoleApp1_vdp_sheetbuilder.Models;
using System.Collections.Concurrent;

namespace ConsoleApp1_vdp_sheetbuilder.Services
{
    public interface IPdfProcessingService
    {
        Task<PdfProcessingResponse> ProcessPdfAsync(IFormFile pdfFile, int rotationAngle, string order);
        Task<PdfProcessingResponse> ProcessPdfWithProgress(IFormFile pdfFile, int rotationAngle, string order, string jobId, IProgressService progressService);
        Task<PdfProcessingResponse> ProcessPdfWithProgressFromPath(string sourceFilePath, string originalFileName, int rotationAngle, string order, string jobId, IProgressService progressService);
    }

    public class PdfProcessingService : IPdfProcessingService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<PdfProcessingService> _logger;

        // Floating-point tolerance for precision errors (~0.0035mm tolerance)
        private const double EPSILON = 0.01;

        // Pre-calculated rotation matrices for performance optimization
        private static readonly ConcurrentDictionary<int, (float cos, float sin)> _rotationCache = new();

        // Performance tracking for large files
        private struct PageDimensions
        {
            public float Width { get; init; }
            public float Height { get; init; }
        }

        public PdfProcessingService(IWebHostEnvironment environment, ILogger<PdfProcessingService> logger)
        {
            _environment = environment;
            _logger = logger;
        }

        public async Task<PdfProcessingResponse> ProcessPdfAsync(IFormFile pdfFile, int rotationAngle, string order)
        {
            var stopwatch = Stopwatch.StartNew();
            var response = new PdfProcessingResponse();

            // Variables for file cleanup - declared at method level to ensure availability in finally block
            string sourceFilePath = "";
            string directory = "";
            string fileNameWithoutExtension = "";
            string extension = "";

            try
            {
                // Create uploads directory if it doesn't exist
                var uploadsDir = System.IO.Path.Combine(_environment.WebRootPath, "uploads");
                Directory.CreateDirectory(uploadsDir);

                // Save uploaded file
                sourceFilePath = System.IO.Path.Combine(uploadsDir, $"{Guid.NewGuid()}_{pdfFile.FileName}");
                using (var stream = new FileStream(sourceFilePath, FileMode.Create, FileAccess.Write))
                {
                    await pdfFile.CopyToAsync(stream);
                }

                bool reverseOrder = order.Equals("Rev", StringComparison.OrdinalIgnoreCase);
                directory = System.IO.Path.GetDirectoryName(sourceFilePath)!;
                fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);
                extension = System.IO.Path.GetExtension(sourceFilePath);

                string postfix = $"A{rotationAngle}_{order.ToUpper()}";
                string outputFilePath = System.IO.Path.Combine(directory, $"{fileNameWithoutExtension}_{postfix}{extension}");

                if (reverseOrder)
                {
                    ReversePages(sourceFilePath, "reversed");
                    sourceFilePath = GetNewFilePath(sourceFilePath, "reversed");
                }

                _logger.LogInformation("Opening source PDF document: {SourceFilePath}", sourceFilePath);

                // Create optimized writer properties for file size reduction
                // Memory-efficient LRU cache for XObject management (prevents unbounded memory growth)
                using var xObjectCache = new LRUCache<int, PdfFormXObject>(maxCapacity: 1000);
                int totalPages;

                // Pre-calculate rotation matrix for performance
                var (cosAngle, sinAngle) = GetCachedRotationValues(rotationAngle);

                // Process PDF with optimized memory and performance patterns
                using (PdfDocument sourceDocument = new PdfDocument(CreatePdfReader(sourceFilePath)))
                using (PdfDocument outputDocument = new PdfDocument(CreatePdfWriter(outputFilePath)))
                {
                    totalPages = sourceDocument.GetNumberOfPages();
                    _logger.LogInformation("Source document has {TotalPages} pages.", totalPages);

                    // Pre-calculate page dimensions in batches to reduce GetPage() calls
                    var pageDimensions = new PageDimensions[totalPages];
                    for (int i = 0; i < totalPages; i++)
                    {
                        var page = sourceDocument.GetPage(i + 1);
                        var pageSize = page.GetPageSize();
                        pageDimensions[i] = new PageDimensions
                        {
                            Width = pageSize.GetWidth(),
                            Height = pageSize.GetHeight()
                        };
                    }

                    float widthInPoints = 317 / 25.4f * 72;
                    float maxHeightInPoints = 980 / 25.4f * 72;

                    // PRE-CALCULATE STANDARD SHEET HEIGHT
                    // Simulate sheet calculation to determine consistent height for all sheets
                    float standardSheetHeight = CalculateStandardSheetHeight(pageDimensions, maxHeightInPoints);
                    _logger.LogInformation("Standard sheet height calculated: {HeightMm:F1}mm for consistent sheets",
                        standardSheetHeight * 25.4 / 72);

                    int pageIndex = 0;
                    int outputPageCount = 0;
                    int progressReportInterval = Math.Max(10, totalPages / 50); // Progress every 2% or 10 pages minimum

                    while (pageIndex < totalPages)
                    {
                        float totalHeight = 0;
                        int pagesOnCustomSheet = 0;

                        // Use pre-calculated dimensions instead of repeated GetPage() calls
                        for (int j = pageIndex; j < totalPages; j++)
                        {
                            float sourcePageHeight = pageDimensions[j].Height;

                            // EPSILON tolerance check for floating-point precision
                            if (totalHeight + sourcePageHeight > maxHeightInPoints + EPSILON)
                            {
                                break; // Stop adding pages - would exceed sheet height
                            }

                            totalHeight += sourcePageHeight;  // Accumulate total height
                            pagesOnCustomSheet++;            // Count pages that fit
                        }

                        // USE STANDARD SHEET HEIGHT FOR ALL SHEETS (including last sheet)
                        PdfPage customPage = outputDocument.AddNewPage(new PageSize(widthInPoints, standardSheetHeight));
                        PdfCanvas canvas = new PdfCanvas(customPage);
                        float currentY = standardSheetHeight;  // Start at TOP of the sheet using standard height

                        // Conditional logging to reduce overhead in large file processing
                        if (totalPages <= 100 || outputPageCount % Math.Max(1, totalPages / 50) == 0)
                        {
                            _logger.LogInformation("Sheet {SheetNumber}: {PagesOnSheet} pages, height: {TotalHeightMm:F1}mm",
                                outputPageCount + 1, pagesOnCustomSheet, totalHeight * 25.4 / 72);
                        }

                        for (int j = 0; j < pagesOnCustomSheet && pageIndex < totalPages; j++, pageIndex++)
                        {
                            PdfPage sourcePage = sourceDocument.GetPage(pageIndex + 1);
                            var dimensions = pageDimensions[pageIndex]; // Use pre-calculated dimensions

                            // Calculate horizontal centering
                            float xOffset = (widthInPoints - dimensions.Width) / 2;

                            // CRITICAL Y-POSITION CALCULATION - Move Y position down by page height
                            currentY -= dimensions.Height;

                            // Use memory-efficient LRU cache for XObject management
                            if (!xObjectCache.TryGetValue(pageIndex, out PdfFormXObject? pageCopy))
                            {
                                pageCopy = sourcePage.CopyAsFormXObject(outputDocument);
                                xObjectCache.Set(pageIndex, pageCopy);
                            }

                            // Progress reporting for large files
                            if (pageIndex % progressReportInterval == 0)
                            {
                                var progressPercent = (float)(pageIndex + 1) / totalPages * 100;
                                var currentPagesPerSecond = (pageIndex + 1) / stopwatch.Elapsed.TotalSeconds;
                                _logger.LogInformation("Progress: {Progress:F1}% ({Current}/{Total} pages, {Speed:F1} pages/sec)",
                                    progressPercent, pageIndex + 1, totalPages, currentPagesPerSecond);
                            }

                            // Optimized rotation handling with pre-calculated values
                            if (rotationAngle != 0)
                            {
                                canvas.SaveState();

                                // Calculate center point for rotation
                                float centerX = xOffset + (dimensions.Width / 2);
                                float centerY = currentY + (dimensions.Height / 2);

                                // Use pre-calculated rotation values (no repeated trigonometric calculations)
                                canvas.ConcatMatrix(1, 0, 0, 1, centerX, centerY);
                                canvas.ConcatMatrix(cosAngle, sinAngle, -sinAngle, cosAngle, 0, 0);
                                canvas.ConcatMatrix(1, 0, 0, 1, -centerX, -centerY);

                                canvas.AddXObjectAt(pageCopy, xOffset, currentY);
                                canvas.RestoreState();
                            }
                            else
                            {
                                // No rotation - direct placement
                                canvas.AddXObjectAt(pageCopy, xOffset, currentY);
                            }

                            // Reduced logging for performance in large files
                            if (totalPages <= 100 && _logger.IsEnabled(LogLevel.Debug))
                            {
                                _logger.LogDebug("Page {PageIndex} placed at Y: {CurrentYMm:F1}mm",
                                    pageIndex + 1, currentY * 25.4 / 72);
                            }
                        }

                        canvas.Release();
                        outputPageCount++;
                    }

                    response.OutputPages = outputPageCount;

                    // LRU cache will be disposed automatically via 'using' statement
                    var finalProcessingTime = stopwatch.Elapsed;
                    var pagesPerSecond = totalPages / finalProcessingTime.TotalSeconds;
                    _logger.LogInformation("✅ Optimized processing complete: {TotalPages} pages → {OutputPages} sheets in {ProcessingTime:F1}s ({Speed:F1} pages/sec)",
                        totalPages, outputPageCount, finalProcessingTime.TotalSeconds, pagesPerSecond);
                }

                // File cleanup will be handled in finally block

                var outputFileName = System.IO.Path.GetFileName(outputFilePath);

                // Create clean filename without GUID prefix for download
                var originalFileName = System.IO.Path.GetFileNameWithoutExtension(pdfFile.FileName);
                var fileExtension = System.IO.Path.GetExtension(pdfFile.FileName);
                var cleanFileName = $"{originalFileName}_{postfix}{fileExtension}";

                response.Success = true;
                response.Message = "PDF processed and optimized successfully!";
                response.OutputFileName = cleanFileName; // Clean name for download
                response.DownloadUrl = $"/api/pdf/download/{Uri.EscapeDataString(cleanFileName)}";
                response.InputPages = totalPages;

                _logger.LogInformation("PDF with custom-sized pages created successfully! Output file: {OutputFilePath}", outputFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing PDF");
                response.Success = false;
                response.Message = $"Error processing PDF: {ex.Message}";
            }
            finally
            {
                stopwatch.Stop();
                response.ProcessingTimeSpan = stopwatch.Elapsed;

                // CRITICAL: Simple, reliable file cleanup to prevent system overload
                CleanupTemporaryFiles(sourceFilePath, directory, fileNameWithoutExtension, extension);

                // Removed forced GC.Collect() - let .NET handle memory management naturally for better performance
            }

            return response;
        }

        public async Task<PdfProcessingResponse> ProcessPdfWithProgressFromPath(
            string sourceFilePath,
            string originalFileName,
            int rotationAngle,
            string order,
            string jobId,
            IProgressService progressService)
        {
            var stopwatch = Stopwatch.StartNew();
            var response = new PdfProcessingResponse();

            // Variables for file cleanup
            string directory = string.Empty;
            string fileNameWithoutExtension = string.Empty;
            string extension = string.Empty;

            // Performance tracking
            int cacheHitCount = 0;
            int cacheMissCount = 0;
            long memoryAtStart = GC.GetTotalMemory(false);

            try
            {
                progressService.UpdateStage(jobId, ProcessingStage.Initializing, "Setting up processing environment...");

                bool reverseOrder = order.Equals("Rev", StringComparison.OrdinalIgnoreCase);
                directory = System.IO.Path.GetDirectoryName(sourceFilePath)!;
                fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);
                extension = System.IO.Path.GetExtension(sourceFilePath);

                string postfix = $"A{rotationAngle}_{order.ToUpper()}";
                string outputFilePath = System.IO.Path.Combine(directory, $"{fileNameWithoutExtension}_{postfix}{extension}");

                if (reverseOrder)
                {
                    progressService.UpdateStage(jobId, ProcessingStage.Initializing, "Reversing page order...");
                    ReversePages(sourceFilePath, "reversed");
                    sourceFilePath = GetNewFilePath(sourceFilePath, "reversed");
                }

                _logger.LogInformation("Opening source PDF document: {SourceFilePath}", sourceFilePath);

                using var xObjectCache = new LRUCache<int, PdfFormXObject>(maxCapacity: 1000);
                int totalPages;
                var (cosAngle, sinAngle) = GetCachedRotationValues(rotationAngle);

                progressService.UpdateStage(jobId, ProcessingStage.PreparingDimensions, "Analyzing document structure...");

                // Use memory-saving reader for large files
                using (PdfDocument sourceDocument = new PdfDocument(CreatePdfReader(sourceFilePath)))
                using (PdfDocument outputDocument = new PdfDocument(CreatePdfWriter(outputFilePath)))
                {
                    totalPages = sourceDocument.GetNumberOfPages();
                    _logger.LogInformation("Source document has {TotalPages} pages.", totalPages);

                    // Initial progress
                    progressService.UpdateProgress(jobId, new ProcessingProgress
                    {
                        JobId = jobId,
                        Stage = "PreparingDimensions",
                        CurrentPage = 0,
                        TotalPages = totalPages,
                        PercentageComplete = 5,
                        PagesPerSecond = 0,
                        EstimatedTimeRemaining = TimeSpan.Zero,
                        ElapsedTime = stopwatch.Elapsed,
                        CurrentOperation = "Pre-calculating page dimensions for optimal processing...",
                        Performance = new PerformanceMetrics
                        {
                            MemoryUsageMB = (GC.GetTotalMemory(false) - memoryAtStart) / 1024 / 1024,
                            CacheHitCount = cacheHitCount,
                            CacheMissCount = cacheMissCount,
                            XObjectsCached = 0,
                            SheetsGenerated = 0
                        }
                    });

                    // Pre-calc dimensions
                    var pageDimensions = new PageDimensions[totalPages];
                    for (int i = 0; i < totalPages; i++)
                    {
                        var page = sourceDocument.GetPage(i + 1);
                        var pageSize = page.GetPageSize();
                        pageDimensions[i] = new PageDimensions
                        {
                            Width = pageSize.GetWidth(),
                            Height = pageSize.GetHeight()
                        };

                        if (i > 0 && i % 100 == 0)
                        {
                            var dimensionProgress = 5 + (i * 5 / totalPages);
                            progressService.UpdateProgress(jobId, new ProcessingProgress
                            {
                                JobId = jobId,
                                Stage = "PreparingDimensions",
                                CurrentPage = i,
                                TotalPages = totalPages,
                                PercentageComplete = dimensionProgress,
                                CurrentOperation = $"Analyzing page dimensions... ({i}/{totalPages})"
                            });
                        }
                    }

                    float widthInPoints = 317 / 25.4f * 72;
                    float maxHeightInPoints = 980 / 25.4f * 72;

                    int pageIndex = 0;
                    int outputPageCount = 0;
                    int progressReportInterval = Math.Max(10, totalPages / 50);

                    progressService.UpdateStage(jobId, ProcessingStage.ProcessingPages, "Processing pages with optimized performance...");

                    // Use a consistent sheet height across all sheets
                    float standardSheetHeight = CalculateStandardSheetHeight(pageDimensions, maxHeightInPoints);

                    while (pageIndex < totalPages)
                    {
                        float totalHeight = 0;
                        int pagesOnCustomSheet = 0;

                        for (int j = pageIndex; j < totalPages; j++)
                        {
                            float sourcePageHeight = pageDimensions[j].Height;
                            if (totalHeight + sourcePageHeight > maxHeightInPoints + EPSILON)
                            {
                                break;
                            }
                            totalHeight += sourcePageHeight;
                            pagesOnCustomSheet++;
                        }

                        PdfPage customPage = outputDocument.AddNewPage(new PageSize(widthInPoints, standardSheetHeight));
                        PdfCanvas canvas = new PdfCanvas(customPage);
                        float currentY = standardSheetHeight;

                        if (totalPages <= 100 || outputPageCount % Math.Max(1, totalPages / 50) == 0)
                        {
                            _logger.LogInformation("Sheet {SheetNumber}: {PagesOnSheet} pages, height: {TotalHeightMm:F1}mm",
                                outputPageCount + 1, pagesOnCustomSheet, totalHeight * 25.4 / 72);
                        }

                        for (int j = 0; j < pagesOnCustomSheet && pageIndex < totalPages; j++, pageIndex++)
                        {
                            PdfPage sourcePage = sourceDocument.GetPage(pageIndex + 1);
                            var dimensions = pageDimensions[pageIndex];
                            float xOffset = (widthInPoints - dimensions.Width) / 2;
                            currentY -= dimensions.Height;

                            if (!xObjectCache.TryGetValue(pageIndex, out PdfFormXObject? pageCopy))
                            {
                                pageCopy = sourcePage.CopyAsFormXObject(outputDocument);
                                xObjectCache.Set(pageIndex, pageCopy);
                                cacheMissCount++;
                            }
                            else
                            {
                                cacheHitCount++;
                            }

                            if (pageIndex % progressReportInterval == 0)
                            {
                                var progressPercent = 10 + (pageIndex * 80 / totalPages);
                                var currentPagesPerSecond = (pageIndex + 1) / stopwatch.Elapsed.TotalSeconds;
                                var remainingPages = totalPages - (pageIndex + 1);
                                var etaSeconds = remainingPages / Math.Max(currentPagesPerSecond, 0.1);

                                progressService.UpdateProgress(jobId, new ProcessingProgress
                                {
                                    JobId = jobId,
                                    Stage = "ProcessingPages",
                                    CurrentPage = pageIndex + 1,
                                    TotalPages = totalPages,
                                    PercentageComplete = progressPercent,
                                    PagesPerSecond = currentPagesPerSecond,
                                    EstimatedTimeRemaining = TimeSpan.FromSeconds(etaSeconds),
                                    ElapsedTime = stopwatch.Elapsed,
                                    CurrentOperation = $"Processing pages {pageIndex - pagesOnCustomSheet + 1}-{pageIndex + 1} (Sheet {outputPageCount + 1})",
                                    Performance = new PerformanceMetrics
                                    {
                                        MemoryUsageMB = (GC.GetTotalMemory(false) - memoryAtStart) / 1024 / 1024,
                                        CacheHitCount = cacheHitCount,
                                        CacheMissCount = cacheMissCount,
                                        XObjectsCached = xObjectCache.Count,
                                        SheetsGenerated = outputPageCount
                                    }
                                });
                            }

                            if (rotationAngle != 0)
                            {
                                canvas.SaveState();
                                float centerX = xOffset + (dimensions.Width / 2);
                                float centerY = currentY + (dimensions.Height / 2);
                                canvas.ConcatMatrix(1, 0, 0, 1, centerX, centerY);
                                canvas.ConcatMatrix(cosAngle, sinAngle, -sinAngle, cosAngle, 0, 0);
                                canvas.ConcatMatrix(1, 0, 0, 1, -centerX, -centerY);
                                canvas.AddXObjectAt(pageCopy, xOffset, currentY);
                                canvas.RestoreState();
                            }
                            else
                            {
                                canvas.AddXObjectAt(pageCopy, xOffset, currentY);
                            }
                        }

                        canvas.Release();
                        outputPageCount++;
                    }

                    response.OutputPages = outputPageCount;

                    progressService.UpdateStage(jobId, ProcessingStage.OptimizingOutput, "Finalizing optimized PDF output...");

                    var finalProcessingTime = stopwatch.Elapsed;
                    var pagesPerSecond = totalPages / finalProcessingTime.TotalSeconds;
                    _logger.LogInformation("✅ Optimized processing complete: {TotalPages} pages → {OutputPages} sheets in {ProcessingTime:F1}s ({Speed:F1} pages/sec)",
                        totalPages, outputPageCount, finalProcessingTime.TotalSeconds, pagesPerSecond);

                    progressService.UpdateProgress(jobId, new ProcessingProgress
                    {
                        JobId = jobId,
                        Stage = "OptimizingOutput",
                        CurrentPage = totalPages,
                        TotalPages = totalPages,
                        PercentageComplete = 95,
                        PagesPerSecond = pagesPerSecond,
                        EstimatedTimeRemaining = TimeSpan.FromSeconds(5),
                        ElapsedTime = stopwatch.Elapsed,
                        CurrentOperation = "Applying final optimizations and generating download...",
                        Performance = new PerformanceMetrics
                        {
                            MemoryUsageMB = (GC.GetTotalMemory(false) - memoryAtStart) / 1024 / 1024,
                            CacheHitCount = cacheHitCount,
                            CacheMissCount = cacheMissCount,
                            XObjectsCached = xObjectCache.Count,
                            SheetsGenerated = outputPageCount
                        }
                    });
                }

                progressService.UpdateStage(jobId, ProcessingStage.Finalizing, "Preparing download and cleanup...");

                var outputFileName = System.IO.Path.GetFileName(outputFilePath);
                var originalBase = System.IO.Path.GetFileNameWithoutExtension(originalFileName);
                var fileExtension = System.IO.Path.GetExtension(originalFileName);
                var cleanFileName = $"{originalBase}_{postfix}{fileExtension}";

                response.Success = true;
                response.Message = "PDF processed and optimized successfully with real-time progress!";
                response.OutputFileName = cleanFileName;
                response.DownloadUrl = $"/api/pdf/download/{Uri.EscapeDataString(cleanFileName)}";
                response.InputPages = totalPages;
                _logger.LogInformation("PDF with custom-sized pages created successfully! Output file: {OutputFilePath}", outputFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing PDF with progress (from path)");
                response.Success = false;
                response.Message = $"Error processing PDF: {ex.Message}";
                progressService.FailJob(jobId, ex.Message);
            }
            finally
            {
                stopwatch.Stop();
                response.ProcessingTimeSpan = stopwatch.Elapsed;
                CleanupTemporaryFiles(sourceFilePath, directory, fileNameWithoutExtension, extension);
            }

            return response;
        }

        public async Task<PdfProcessingResponse> ProcessPdfWithProgress(IFormFile pdfFile, int rotationAngle, string order, string jobId, IProgressService progressService)
        {
            var stopwatch = Stopwatch.StartNew();
            var response = new PdfProcessingResponse();

            // Variables for file cleanup - declared at method level to ensure availability in finally block
            string sourceFilePath = "";
            string directory = "";
            string fileNameWithoutExtension = "";
            string extension = "";

            // Performance tracking
            int cacheHitCount = 0;
            int cacheMissCount = 0;
            long memoryAtStart = GC.GetTotalMemory(false);

            try
            {
                progressService.UpdateStage(jobId, ProcessingStage.Initializing, "Setting up processing environment...");

                // Create uploads directory if it doesn't exist
                var uploadsDir = System.IO.Path.Combine(_environment.WebRootPath, "uploads");
                Directory.CreateDirectory(uploadsDir);

                // Save uploaded file
                sourceFilePath = System.IO.Path.Combine(uploadsDir, $"{Guid.NewGuid()}_{pdfFile.FileName}");
                using (var stream = new FileStream(sourceFilePath, FileMode.Create, FileAccess.Write))
                {
                    await pdfFile.CopyToAsync(stream);
                }

                bool reverseOrder = order.Equals("Rev", StringComparison.OrdinalIgnoreCase);
                directory = System.IO.Path.GetDirectoryName(sourceFilePath)!;
                fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);
                extension = System.IO.Path.GetExtension(sourceFilePath);

                string postfix = $"A{rotationAngle}_{order.ToUpper()}";
                string outputFilePath = System.IO.Path.Combine(directory, $"{fileNameWithoutExtension}_{postfix}{extension}");

                if (reverseOrder)
                {
                    progressService.UpdateStage(jobId, ProcessingStage.Initializing, "Reversing page order...");
                    ReversePages(sourceFilePath, "reversed");
                    sourceFilePath = GetNewFilePath(sourceFilePath, "reversed");
                }

                _logger.LogInformation("Opening source PDF document: {SourceFilePath}", sourceFilePath);

                // Create optimized writer properties for file size reduction
                // Memory-efficient LRU cache for XObject management (prevents unbounded memory growth)
                using var xObjectCache = new LRUCache<int, PdfFormXObject>(maxCapacity: 1000);
                int totalPages;

                // Pre-calculate rotation matrix for performance
                var (cosAngle, sinAngle) = GetCachedRotationValues(rotationAngle);

                progressService.UpdateStage(jobId, ProcessingStage.PreparingDimensions, "Analyzing document structure...");

                // Process PDF with optimized memory and performance patterns
                using (PdfDocument sourceDocument = new PdfDocument(CreatePdfReader(sourceFilePath)))
                using (PdfDocument outputDocument = new PdfDocument(CreatePdfWriter(outputFilePath)))
                {
                    totalPages = sourceDocument.GetNumberOfPages();
                    _logger.LogInformation("Source document has {TotalPages} pages.", totalPages);

                    // Update initial progress
                    progressService.UpdateProgress(jobId, new ProcessingProgress
                    {
                        JobId = jobId,
                        Stage = "PreparingDimensions",
                        CurrentPage = 0,
                        TotalPages = totalPages,
                        PercentageComplete = 5,
                        PagesPerSecond = 0,
                        EstimatedTimeRemaining = TimeSpan.Zero,
                        ElapsedTime = stopwatch.Elapsed,
                        CurrentOperation = "Pre-calculating page dimensions for optimal processing...",
                        Performance = new PerformanceMetrics
                        {
                            MemoryUsageMB = (GC.GetTotalMemory(false) - memoryAtStart) / 1024 / 1024,
                            CacheHitCount = cacheHitCount,
                            CacheMissCount = cacheMissCount,
                            XObjectsCached = 0,
                            SheetsGenerated = 0
                        }
                    });

                    // Pre-calculate page dimensions in batches to reduce GetPage() calls
                    var pageDimensions = new PageDimensions[totalPages];
                    for (int i = 0; i < totalPages; i++)
                    {
                        var page = sourceDocument.GetPage(i + 1);
                        var pageSize = page.GetPageSize();
                        pageDimensions[i] = new PageDimensions
                        {
                            Width = pageSize.GetWidth(),
                            Height = pageSize.GetHeight()
                        };

                        // Update progress every 100 pages during dimension calculation
                        if (i > 0 && i % 100 == 0)
                        {
                            var dimensionProgress = 5 + (i * 5 / totalPages); // 5-10% for dimension calculation
                            progressService.UpdateProgress(jobId, new ProcessingProgress
                            {
                                JobId = jobId,
                                Stage = "PreparingDimensions",
                                CurrentPage = i,
                                TotalPages = totalPages,
                                PercentageComplete = dimensionProgress,
                                CurrentOperation = $"Analyzing page dimensions... ({i}/{totalPages})"
                            });
                        }
                    }

                    float widthInPoints = 317 / 25.4f * 72;
                    float maxHeightInPoints = 980 / 25.4f * 72;

                    // PRE-CALCULATE STANDARD SHEET HEIGHT (progress version)
                    float standardSheetHeight = CalculateStandardSheetHeight(pageDimensions, maxHeightInPoints);
                    _logger.LogInformation("Standard sheet height calculated: {HeightMm:F1}mm for consistent sheets",
                        standardSheetHeight * 25.4 / 72);

                    int pageIndex = 0;
                    int outputPageCount = 0;
                    int progressReportInterval = Math.Max(10, totalPages / 50); // Progress every 2% or 10 pages minimum

                    progressService.UpdateStage(jobId, ProcessingStage.ProcessingPages, "Processing pages with optimized performance...");

                    while (pageIndex < totalPages)
                    {
                        float totalHeight = 0;
                        int pagesOnCustomSheet = 0;

                        // Use pre-calculated dimensions instead of repeated GetPage() calls
                        for (int j = pageIndex; j < totalPages; j++)
                        {
                            float sourcePageHeight = pageDimensions[j].Height;

                            // EPSILON tolerance check for floating-point precision
                            if (totalHeight + sourcePageHeight > maxHeightInPoints + EPSILON)
                            {
                                break; // Stop adding pages - would exceed sheet height
                            }

                            totalHeight += sourcePageHeight;  // Accumulate total height
                            pagesOnCustomSheet++;            // Count pages that fit
                        }

                        // USE STANDARD SHEET HEIGHT FOR ALL SHEETS (progress version)
                        PdfPage customPage = outputDocument.AddNewPage(new PageSize(widthInPoints, standardSheetHeight));
                        PdfCanvas canvas = new PdfCanvas(customPage);
                        float currentY = standardSheetHeight;  // Start at TOP using standard height

                        // Conditional logging to reduce overhead in large file processing
                        if (totalPages <= 100 || outputPageCount % Math.Max(1, totalPages / 50) == 0)
                        {
                            _logger.LogInformation("Sheet {SheetNumber}: {PagesOnSheet} pages, height: {TotalHeightMm:F1}mm",
                                outputPageCount + 1, pagesOnCustomSheet, totalHeight * 25.4 / 72);
                        }

                        for (int j = 0; j < pagesOnCustomSheet && pageIndex < totalPages; j++, pageIndex++)
                        {
                            PdfPage sourcePage = sourceDocument.GetPage(pageIndex + 1);
                            var dimensions = pageDimensions[pageIndex]; // Use pre-calculated dimensions

                            // Calculate horizontal centering
                            float xOffset = (widthInPoints - dimensions.Width) / 2;

                            // CRITICAL Y-POSITION CALCULATION - Move Y position down by page height
                            currentY -= dimensions.Height;

                            // Use memory-efficient LRU cache for XObject management
                            if (!xObjectCache.TryGetValue(pageIndex, out PdfFormXObject? pageCopy))
                            {
                                pageCopy = sourcePage.CopyAsFormXObject(outputDocument);
                                xObjectCache.Set(pageIndex, pageCopy);
                                cacheMissCount++;
                            }
                            else
                            {
                                cacheHitCount++;
                            }

                            // Enhanced progress reporting for large files
                            if (pageIndex % progressReportInterval == 0)
                            {
                                var progressPercent = 10 + (pageIndex * 80 / totalPages); // 10-90% for processing
                                var currentPagesPerSecond = (pageIndex + 1) / stopwatch.Elapsed.TotalSeconds;
                                var remainingPages = totalPages - (pageIndex + 1);
                                var etaSeconds = remainingPages / Math.Max(currentPagesPerSecond, 0.1);

                                progressService.UpdateProgress(jobId, new ProcessingProgress
                                {
                                    JobId = jobId,
                                    Stage = "ProcessingPages",
                                    CurrentPage = pageIndex + 1,
                                    TotalPages = totalPages,
                                    PercentageComplete = progressPercent,
                                    PagesPerSecond = currentPagesPerSecond,
                                    EstimatedTimeRemaining = TimeSpan.FromSeconds(etaSeconds),
                                    ElapsedTime = stopwatch.Elapsed,
                                    CurrentOperation = $"Processing pages {pageIndex - pagesOnCustomSheet + 1}-{pageIndex + 1} (Sheet {outputPageCount + 1})",
                                    Performance = new PerformanceMetrics
                                    {
                                        MemoryUsageMB = (GC.GetTotalMemory(false) - memoryAtStart) / 1024 / 1024,
                                        CacheHitCount = cacheHitCount,
                                        CacheMissCount = cacheMissCount,
                                        XObjectsCached = xObjectCache.Count,
                                        SheetsGenerated = outputPageCount
                                    }
                                });
                            }

                            // Optimized rotation handling with pre-calculated values
                            if (rotationAngle != 0)
                            {
                                canvas.SaveState();

                                // Calculate center point for rotation
                                float centerX = xOffset + (dimensions.Width / 2);
                                float centerY = currentY + (dimensions.Height / 2);

                                // Use pre-calculated rotation values (no repeated trigonometric calculations)
                                canvas.ConcatMatrix(1, 0, 0, 1, centerX, centerY);
                                canvas.ConcatMatrix(cosAngle, sinAngle, -sinAngle, cosAngle, 0, 0);
                                canvas.ConcatMatrix(1, 0, 0, 1, -centerX, -centerY);

                                canvas.AddXObjectAt(pageCopy, xOffset, currentY);
                                canvas.RestoreState();
                            }
                            else
                            {
                                // No rotation - direct placement
                                canvas.AddXObjectAt(pageCopy, xOffset, currentY);
                            }

                            // Reduced logging for performance in large files
                            if (totalPages <= 100 && _logger.IsEnabled(LogLevel.Debug))
                            {
                                _logger.LogDebug("Page {PageIndex} placed at Y: {CurrentYMm:F1}mm",
                                    pageIndex + 1, currentY * 25.4 / 72);
                            }
                        }

                        canvas.Release();
                        outputPageCount++;
                    }

                    response.OutputPages = outputPageCount;

                    progressService.UpdateStage(jobId, ProcessingStage.OptimizingOutput, "Finalizing optimized PDF output...");

                    // LRU cache will be disposed automatically via 'using' statement
                    var finalProcessingTime = stopwatch.Elapsed;
                    var pagesPerSecond = totalPages / finalProcessingTime.TotalSeconds;
                    _logger.LogInformation("✅ Optimized processing complete: {TotalPages} pages → {OutputPages} sheets in {ProcessingTime:F1}s ({Speed:F1} pages/sec)",
                        totalPages, outputPageCount, finalProcessingTime.TotalSeconds, pagesPerSecond);

                    // Final progress update before completion
                    progressService.UpdateProgress(jobId, new ProcessingProgress
                    {
                        JobId = jobId,
                        Stage = "OptimizingOutput",
                        CurrentPage = totalPages,
                        TotalPages = totalPages,
                        PercentageComplete = 95,
                        PagesPerSecond = pagesPerSecond,
                        EstimatedTimeRemaining = TimeSpan.FromSeconds(5),
                        ElapsedTime = stopwatch.Elapsed,
                        CurrentOperation = "Applying final optimizations and generating download...",
                        Performance = new PerformanceMetrics
                        {
                            MemoryUsageMB = (GC.GetTotalMemory(false) - memoryAtStart) / 1024 / 1024,
                            CacheHitCount = cacheHitCount,
                            CacheMissCount = cacheMissCount,
                            XObjectsCached = xObjectCache.Count,
                            SheetsGenerated = outputPageCount
                        }
                    });
                }

                progressService.UpdateStage(jobId, ProcessingStage.Finalizing, "Preparing download and cleanup...");

                var outputFileName = System.IO.Path.GetFileName(outputFilePath);

                // Create clean filename without GUID prefix for download
                var originalFileName = System.IO.Path.GetFileNameWithoutExtension(pdfFile.FileName);
                var fileExtension = System.IO.Path.GetExtension(pdfFile.FileName);
                var cleanFileName = $"{originalFileName}_{postfix}{fileExtension}";

                response.Success = true;
                response.Message = "PDF processed and optimized successfully with real-time progress!";
                response.OutputFileName = cleanFileName; // Clean name for download
                response.DownloadUrl = $"/api/pdf/download/{Uri.EscapeDataString(cleanFileName)}";
                response.InputPages = totalPages;

                _logger.LogInformation("PDF with custom-sized pages created successfully! Output file: {OutputFilePath}", outputFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing PDF with progress");
                response.Success = false;
                response.Message = $"Error processing PDF: {ex.Message}";
                progressService.FailJob(jobId, ex.Message);
            }
            finally
            {
                stopwatch.Stop();
                response.ProcessingTimeSpan = stopwatch.Elapsed;

                // CRITICAL: Simple, reliable file cleanup to prevent system overload
                CleanupTemporaryFiles(sourceFilePath, directory, fileNameWithoutExtension, extension);

                // Removed forced GC.Collect() - let .NET handle memory management naturally for better performance
            }

            return response;
        }

        private void CleanupTemporaryFiles(string sourceFilePath, string directory, string fileNameWithoutExtension, string extension)
        {
            try
            {
                // Clean up source file
                SafeDeleteFile(sourceFilePath, "source file");

                // Clean up potential reversed file
                if (!string.IsNullOrEmpty(directory) && !string.IsNullOrEmpty(fileNameWithoutExtension))
                {
                    var reversedFilePath = System.IO.Path.Combine(directory, $"{fileNameWithoutExtension}_reversed{extension}");
                    SafeDeleteFile(reversedFilePath, "reversed file");
                }

                // Clean up any optimization temp files (from previous versions)
                if (!string.IsNullOrEmpty(directory) && !string.IsNullOrEmpty(fileNameWithoutExtension))
                {
                    CleanupFilesByPattern(directory, $"{fileNameWithoutExtension}*_optimized{extension}");
                }

                _logger.LogInformation("✅ File cleanup completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ File cleanup encountered errors, but processing continued");
            }
        }

        private void SafeDeleteFile(string filePath, string fileType)
        {
            try
            {
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogDebug("🗑️ Deleted {FileType}: {FileName}", fileType, System.IO.Path.GetFileName(filePath));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to delete {FileType}: {FilePath}", fileType, filePath);
            }
        }

        private void CleanupFilesByPattern(string directory, string pattern)
        {
            try
            {
                var files = Directory.GetFiles(directory, pattern);
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                        _logger.LogDebug("🗑️ Deleted temp file: {FileName}", System.IO.Path.GetFileName(file));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Failed to delete temp file: {FilePath}", file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to cleanup files by pattern: {Pattern}", pattern);
            }
        }

        private static void ReversePages(string filePath, string postfix)
        {
            var stopwatch = Stopwatch.StartNew();
            string newFilePath = GetNewFilePath(filePath, postfix);

            // Create optimized writer properties for file size reduction
            using (PdfDocument sourceDocument = new PdfDocument(CreatePdfReader(filePath)))
            using (PdfDocument newDocument = new PdfDocument(CreatePdfWriter(newFilePath)))
            {
                int pageCount = sourceDocument.GetNumberOfPages();

                for (int i = pageCount; i >= 1; i--)
                {
                    PdfPage page = sourceDocument.GetPage(i);
                    newDocument.AddPage(page.CopyTo(newDocument));
                }
            }

            stopwatch.Stop();
            Console.WriteLine($"ReversePages took {stopwatch.ElapsedMilliseconds} ms");
        }

        private static string GetNewFilePath(string filePath, string postfix)
        {
            string directory = System.IO.Path.GetDirectoryName(filePath)!;
            string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(filePath);
            string extension = System.IO.Path.GetExtension(filePath);
            return System.IO.Path.Combine(directory, $"{fileNameWithoutExtension}_{postfix}{extension}");
        }

        private static WriterProperties CreateWriterProperties()
        {
            return new WriterProperties()
                .SetCompressionLevel(CompressionConstants.BEST_COMPRESSION)
                .SetFullCompressionMode(true);
        }

        private static PdfWriter CreatePdfWriter(string path)
        {
            var writer = new PdfWriter(path, CreateWriterProperties());
            writer.SetSmartMode(true);
            return writer;
        }

        private static PdfReader CreatePdfReader(string path)
        {
            return new PdfReader(path);
        }


        /// <summary>
        /// Calculate consistent sheet height based on the most common page arrangement
        /// This ensures all sheets (including the last one) have the same height
        /// </summary>
        private float CalculateStandardSheetHeight(PageDimensions[] pageDimensions, float maxHeightInPoints)
        {
            if (pageDimensions.Length == 0)
                return maxHeightInPoints;

            // Simulate the first few sheets to find the most common pattern
            var sheetHeights = new List<float>();
            int simulatedPageIndex = 0;
            int maxSheetsToSimulate = Math.Min(10, (int)Math.Ceiling(pageDimensions.Length / 10.0)); // Sample first 10 sheets or 10% of pages

            while (simulatedPageIndex < pageDimensions.Length && sheetHeights.Count < maxSheetsToSimulate)
            {
                float simulatedTotalHeight = 0;
                int simulatedPagesOnSheet = 0;

                // Calculate how many pages fit on this simulated sheet
                for (int j = simulatedPageIndex; j < pageDimensions.Length; j++)
                {
                    float pageHeight = pageDimensions[j].Height;

                    if (simulatedTotalHeight + pageHeight > maxHeightInPoints + EPSILON)
                    {
                        break;
                    }

                    simulatedTotalHeight += pageHeight;
                    simulatedPagesOnSheet++;
                }

                if (simulatedPagesOnSheet > 0)
                {
                    sheetHeights.Add(simulatedTotalHeight);
                    simulatedPageIndex += simulatedPagesOnSheet;
                }
                else
                {
                    break;
                }
            }

            // Find the most common height (or use the first one if all are similar)
            if (sheetHeights.Count == 0)
                return maxHeightInPoints;

            // Use the height from the first full sheet as the standard
            // This works well for consistent page sizes
            float standardHeight = sheetHeights[0];

            // If the first sheet seems too small (less than 50% of max), try to find a better one
            if (standardHeight < maxHeightInPoints * 0.5f && sheetHeights.Count > 1)
            {
                standardHeight = sheetHeights.Where(h => h >= maxHeightInPoints * 0.5f).FirstOrDefault(sheetHeights[0]);
            }

            return standardHeight;
        }

        /// <summary>
        /// Get cached rotation values to avoid repeated trigonometric calculations
        /// Significant performance improvement for large files with rotation
        /// </summary>
        private static (float cos, float sin) GetCachedRotationValues(int rotationAngle)
        {
            return _rotationCache.GetOrAdd(rotationAngle, angle =>
            {
                var radians = Math.PI * angle / 180.0;
                return ((float)Math.Cos(radians), (float)Math.Sin(radians));
            });
        }
    }
}
