using Microsoft.AspNetCore.Mvc;
using ConsoleApp1_vdp_sheetbuilder.Models;
using ConsoleApp1_vdp_sheetbuilder.Services;
using System.Text.Json;
using System.Text;
using System.IO;
using System.Linq;

namespace ConsoleApp1_vdp_sheetbuilder.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PdfController : ControllerBase
    {
        private readonly IPdfProcessingService _pdfProcessingService;
        private readonly IProgressService _progressService;
        private readonly IUploadReliabilityService _uploadReliabilityService;
        private readonly ILogger<PdfController> _logger;
        private readonly IWebHostEnvironment _environment;

        public PdfController(IPdfProcessingService pdfProcessingService, IProgressService progressService, IUploadReliabilityService uploadReliabilityService, ILogger<PdfController> logger, IWebHostEnvironment environment)
        {
            _pdfProcessingService = pdfProcessingService;
            _progressService = progressService;
            _uploadReliabilityService = uploadReliabilityService;
            _logger = logger;
            _environment = environment;
        }

        /// <summary>
        /// Process a PDF file for VDP sheet building
        /// WARNING: For files larger than 200MB or 10,000+ pages, use /process-with-progress instead
        /// </summary>
        /// <param name="request">PDF processing request containing file and parameters</param>
        /// <returns>Processing result with download URL</returns>
        [HttpPost("process")]
        public async Task<ActionResult<PdfProcessingResponse>> ProcessPdf([FromForm] PdfProcessingRequest request)
        {
            try
            {
                if (request.PdfFile == null || request.PdfFile.Length == 0)
                {
                    return BadRequest(new PdfProcessingResponse
                    {
                        Success = false,
                        Message = "No PDF file provided"
                    });
                }

                if (!request.PdfFile.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new PdfProcessingResponse
                    {
                        Success = false,
                        Message = "File must be a PDF"
                    });
                }

                var fileLengthBytes = request.PdfFile.Length;
                if (fileLengthBytes <= 0 && Request.Headers.ContentLength.HasValue)
                {
                    fileLengthBytes = Math.Max(fileLengthBytes, Request.Headers.ContentLength.Value);
                }

                if (_uploadReliabilityService.ShouldBlockLegacyEndpoint(fileLengthBytes))
                {
                    var thresholdMb = _uploadReliabilityService.Options.LargeFileThresholdMb;
                    _logger.LogWarning("Rejecting legacy upload for {FileName} ({SizeMB:F1}MB); files above {Threshold}MB must use progress endpoint",
                        request.PdfFile.FileName, fileLengthBytes / 1024.0 / 1024.0, thresholdMb);

                    return Conflict(new
                    {
                        success = false,
                        message = $"Files larger than {thresholdMb}MB must use /api/pdf/process-with-progress",
                        requiredEndpoint = "/api/pdf/process-with-progress"
                    });
                }

                _logger.LogInformation("Processing PDF: {FileName} ({SizeMB:F1}MB), Rotation: {Rotation}, Order: {Order}",
                    request.PdfFile.FileName, fileLengthBytes / 1024.0 / 1024.0, request.RotationAngle, request.Order);

                var result = await _pdfProcessingService.ProcessPdfAsync(
                    request.PdfFile,
                    request.RotationAngle,
                    request.Order);

                if (result.Success)
                {
                    return Ok(result);
                }
                else
                {
                    return BadRequest(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing PDF request");
                return StatusCode(500, new PdfProcessingResponse
                {
                    Success = false,
                    Message = "Internal server error occurred while processing the PDF"
                });
            }
        }

        /// <summary>
        /// Start PDF processing with progress tracking
        /// </summary>
        /// <param name="request">PDF processing request</param>
        /// <returns>Job ID for progress tracking</returns>
        [HttpPost("process-with-progress")]
        public async Task<ActionResult<object>> ProcessPdfWithProgress([FromForm] PdfProcessingRequest request)
        {
            UploadFingerprint? fingerprint = null;
            string jobId = string.Empty;

            try
            {
                if (request.PdfFile == null || request.PdfFile.Length == 0)
                {
                    return BadRequest(new { success = false, message = "No PDF file provided" });
                }

                if (!request.PdfFile.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { success = false, message = "File must be a PDF" });
                }

                fingerprint = UploadFingerprint.From(
                    request.PdfFile.FileName,
                    request.PdfFile.Length,
                    request.RotationAngle,
                    request.Order);

                var registration = _uploadReliabilityService.RegisterOrResolveJob(fingerprint.Value, () => _progressService.CreateJob());

                if (registration.State == JobRegistrationState.DuplicateActive)
                {
                    var existingJob = _progressService.GetJobStatus(registration.JobId);
                    return Ok(new
                    {
                        success = true,
                        jobId = registration.JobId,
                        duplicateOf = true,
                        stage = existingJob?.Stage.ToString(),
                        message = "Existing job is still processing"
                    });
                }

                if (registration.State == JobRegistrationState.DuplicateCompleted && registration.CompletedResult != null)
                {
                    return Ok(new
                    {
                        success = true,
                        jobId = registration.JobId,
                        duplicateOf = true,
                        stage = ProcessingStage.Completed.ToString(),
                        result = registration.CompletedResult
                    });
                }

                jobId = registration.JobId;
                _logger.LogInformation("🚀 Starting PDF processing with progress tracking: {FileName} (Job: {JobId})",
                    request.PdfFile.FileName, jobId);

                string sourceFilePath;

                try
                {
                    var uploadsDir = Path.Combine(_environment.WebRootPath, "uploads");
                    Directory.CreateDirectory(uploadsDir);

                    sourceFilePath = Path.Combine(uploadsDir, $"{Guid.NewGuid()}_{request.PdfFile.FileName}");
                    using (var fileStream = new FileStream(sourceFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await request.PdfFile.CopyToAsync(fileStream);
                    }
                }
                catch (Exception ex)
                {
                    if (fingerprint.HasValue)
                    {
                        _uploadReliabilityService.MarkJobFailed(fingerprint.Value, jobId, "Failed to save uploaded file");
                    }
                    _logger.LogError(ex, "Failed to persist uploaded file to disk for job {JobId}", jobId);
                    return StatusCode(500, new { success = false, message = "Failed to save uploaded file" });
                }

                var originalFileName = request.PdfFile.FileName;
                var rotationAngle = request.RotationAngle;
                var order = request.Order;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await _pdfProcessingService.ProcessPdfWithProgressFromPath(
                            sourceFilePath,
                            originalFileName,
                            rotationAngle,
                            order,
                            jobId,
                            _progressService);

                        _progressService.CompleteJob(jobId, result);
                        if (fingerprint.HasValue)
                        {
                            _uploadReliabilityService.MarkJobCompleted(fingerprint.Value, jobId, result);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background processing failed for job {JobId}", jobId);
                        _progressService.FailJob(jobId, ex.Message);
                        if (fingerprint.HasValue)
                        {
                            _uploadReliabilityService.MarkJobFailed(fingerprint.Value, jobId, ex.Message);
                        }
                    }
                });

                return Ok(new { success = true, jobId });
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(jobId) && fingerprint.HasValue)
                {
                    _uploadReliabilityService.MarkJobFailed(fingerprint.Value, jobId, ex.Message);
                }

                _logger.LogError(ex, "Error starting PDF processing with progress");
                return StatusCode(500, new { success = false, message = "Failed to start processing" });
            }
        }

        [HttpGet("progress/{jobId}")]
        public async Task<IActionResult> GetProgress(string jobId)
        {
            var job = _progressService.GetJobStatus(jobId);
            if (job == null)
            {
                return NotFound(new { success = false, message = "Job not found" });
            }

            Response.Headers["Content-Type"] = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["Connection"] = "keep-alive";
            Response.Headers["Access-Control-Allow-Origin"] = "*";

            var cancellationToken = HttpContext.RequestAborted;

            try
            {
                await foreach (var progress in _progressService.SubscribeToProgress(jobId, cancellationToken))
                {
                    var json = JsonSerializer.Serialize(progress, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    var data = $"data: {json}\n\n";
                    var bytes = Encoding.UTF8.GetBytes(data);

                    await Response.Body.WriteAsync(bytes, cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);

                    // Check if processing is complete
                    if (progress.Stage == "Completed" || progress.Stage == "Failed")
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Progress stream cancelled for job {JobId}", jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error streaming progress for job {JobId}", jobId);
            }

            return new EmptyResult();
        }

        /// <summary>
        /// Get job status and result
        /// </summary>
        /// <param name="jobId">Job ID</param>
        /// <returns>Job status</returns>
        [HttpGet("status/{jobId}")]
        public ActionResult<object> GetJobStatus(string jobId)
        {
            var job = _progressService.GetJobStatus(jobId);
            if (job == null)
            {
                return NotFound(new { success = false, message = "Job not found" });
            }

            return Ok(new
            {
                success = true,
                jobId = job.JobId,
                stage = job.Stage.ToString(),
                startTime = job.StartTime,
                endTime = job.EndTime,
                progress = job.Progress,
                result = job.Result,
                error = job.ErrorMessage
            });
        }

        /// <summary>
        /// Download processed PDF and optionally delete after download
        /// </summary>
        /// <param name="filename">Name of the file to download</param>
        /// <param name="deleteAfterDownload">Whether to delete the file after download</param>
        /// <returns>File download or error</returns>
        [HttpGet("download/{filename}")]
        public ActionResult DownloadFile(string filename, [FromQuery] bool deleteAfterDownload = false)
        {
            try
            {
                var uploadsDir = Path.Combine(_environment.WebRootPath, "uploads");
                var filePath = ResolveFilePath(uploadsDir, filename);

                if (filePath is null)
                {
                    return NotFound(new { success = false, message = "File not found or may have already been downloaded" });
                }

                filename = Path.GetFileName(filePath);

                var contentType = "application/pdf";
                var cleanFilename = GetCleanFilename(filename);

                if (deleteAfterDownload)
                {
                    // Delete after response completes to keep storage tidy while still streaming
                    HttpContext.Response.OnCompleted(() =>
                    {
                        try
                        {
                            System.IO.File.Delete(filePath);
                            _logger.LogInformation("File deleted after download: {Filename}", filename);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete file after download: {Filename}", filename);
                        }
                        return Task.CompletedTask;
                    });
                }

                return PhysicalFile(filePath, contentType, cleanFilename, enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading file: {Filename}", filename);
                return StatusCode(500, new { success = false, message = "Error downloading file" });
            }
        }

        private static string? ResolveFilePath(string uploadsDir, string requestedFilename)
        {
            if (string.IsNullOrWhiteSpace(requestedFilename))
            {
                return null;
            }

            if (!Directory.Exists(uploadsDir))
            {
                return null;
            }

            var safeFilename = Path.GetFileName(requestedFilename);
            var directPath = Path.Combine(uploadsDir, safeFilename);

            if (System.IO.File.Exists(directPath))
            {
                return directPath;
            }

            var searchPattern = $"*_{safeFilename}";
            var matchingFile = Directory.EnumerateFiles(uploadsDir, searchPattern, SearchOption.TopDirectoryOnly)
                .OrderByDescending(path => System.IO.File.GetLastWriteTimeUtc(path))
                .FirstOrDefault();

            return matchingFile;
        }

        private string GetCleanFilename(string guidFilename)
        {
            // Remove GUID prefix from filename
            // Format: {GUID}_{originalname}_A90_REV.pdf -> {originalname}_A90_REV.pdf
            var parts = guidFilename.Split('_', 2);
            return parts.Length > 1 ? parts[1] : guidFilename;
        }

        /// <summary>
        /// Get API health status
        /// </summary>
        /// <returns>Health status</returns>
        [HttpGet("health")]
        public ActionResult<object> Health()
        {
            return Ok(new
            {
                Status = "Healthy",
                Timestamp = DateTime.UtcNow,
                Service = "VDP Sheet Builder API with Performance Optimization"
            });
        }
    }
}
