using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConsoleApp1_vdp_sheetbuilder.Controllers;
using ConsoleApp1_vdp_sheetbuilder.Models;
using ConsoleApp1_vdp_sheetbuilder.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConsoleApp1_vdp_sheetbuilder.Tests;

public class DownloadFileTests
{
    [Fact]
    public void DownloadFile_UsingCleanFilename_ResolvesStoredFile()
    {
        var webRoot = CreateTempWebRoot();
        try
        {
            var uploads = Path.Combine(webRoot, "uploads");
            Directory.CreateDirectory(uploads);

            var originalName = "sample_A180_REV.pdf";
            var storedName = $"{Guid.NewGuid():N}_{originalName}";
            var storedPath = Path.Combine(uploads, storedName);
            File.WriteAllText(storedPath, "test");

            var controller = CreateController(webRoot);

            var result = controller.DownloadFile(originalName, deleteAfterDownload: false);

            var physicalFile = Assert.IsType<PhysicalFileResult>(result);
            Assert.Equal(storedPath, physicalFile.FileName);
            Assert.Equal(originalName, physicalFile.FileDownloadName);
            Assert.Equal("application/pdf", physicalFile.ContentType);
        }
        finally
        {
            CleanupTempWebRoot(webRoot);
        }
    }

    [Fact]
    public void DownloadFile_WithStoredFilename_StillWorks()
    {
        var webRoot = CreateTempWebRoot();
        try
        {
            var uploads = Path.Combine(webRoot, "uploads");
            Directory.CreateDirectory(uploads);

            var originalName = "sample_A180_REV.pdf";
            var storedName = $"{Guid.NewGuid():N}_{originalName}";
            var storedPath = Path.Combine(uploads, storedName);
            File.WriteAllText(storedPath, "test");

            var controller = CreateController(webRoot);

            var result = controller.DownloadFile(storedName, deleteAfterDownload: false);

            var physicalFile = Assert.IsType<PhysicalFileResult>(result);
            Assert.Equal(storedPath, physicalFile.FileName);
            Assert.Equal(originalName, physicalFile.FileDownloadName);
        }
        finally
        {
            CleanupTempWebRoot(webRoot);
        }
    }

    private static string CreateTempWebRoot()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "sheetbuilder-tests", Guid.NewGuid().ToString("N"));
        var webRoot = Path.Combine(basePath, "wwwroot");
        Directory.CreateDirectory(webRoot);
        return webRoot;
    }

    private static void CleanupTempWebRoot(string webRoot)
    {
        var basePath = Path.GetDirectoryName(webRoot);
        if (!string.IsNullOrEmpty(basePath) && Directory.Exists(basePath))
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    private static PdfController CreateController(string webRoot)
    {
        var environment = new TestWebHostEnvironment
        {
            WebRootPath = webRoot,
            ContentRootPath = webRoot,
        };

        var controller = new PdfController(
            new StubPdfProcessingService(),
            new StubProgressService(),
            new StubUploadReliabilityService(),
            NullLogger<PdfController>.Instance,
            environment);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class StubPdfProcessingService : IPdfProcessingService
    {
        public Task<PdfProcessingResponse> ProcessPdfAsync(IFormFile pdfFile, int rotationAngle, string order) => throw new NotImplementedException();
        public Task<PdfProcessingResponse> ProcessPdfWithProgress(IFormFile pdfFile, int rotationAngle, string order, string jobId, IProgressService progressService) => throw new NotImplementedException();
        public Task<PdfProcessingResponse> ProcessPdfWithProgressFromPath(string sourceFilePath, string originalFileName, int rotationAngle, string order, string jobId, IProgressService progressService) => throw new NotImplementedException();
    }

    private sealed class StubProgressService : IProgressService
    {
        public string CreateJob() => throw new NotImplementedException();
        public void UpdateProgress(string jobId, ProcessingProgress progress) => throw new NotImplementedException();
        public void UpdateStage(string jobId, ProcessingStage stage, string operation = "") => throw new NotImplementedException();
        public void CompleteJob(string jobId, PdfProcessingResponse result) => throw new NotImplementedException();
        public void FailJob(string jobId, string errorMessage) => throw new NotImplementedException();
        public JobStatus? GetJobStatus(string jobId) => throw new NotImplementedException();
        public IAsyncEnumerable<ProcessingProgress> SubscribeToProgress(string jobId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public void CleanupOldJobs() => throw new NotImplementedException();
    }

    private sealed class StubUploadReliabilityService : IUploadReliabilityService
    {
        public UploadReliabilityOptions Options { get; } = new();

        public JobRegistrationOutcome RegisterOrResolveJob(UploadFingerprint fingerprint, Func<string> jobFactory) => throw new NotImplementedException();
        public void MarkJobCompleted(UploadFingerprint fingerprint, string jobId, PdfProcessingResponse result) => throw new NotImplementedException();
        public void MarkJobFailed(UploadFingerprint fingerprint, string jobId, string? error = null) => throw new NotImplementedException();
        public bool ShouldBlockLegacyEndpoint(long fileSizeBytes) => throw new NotImplementedException();
    }
}
