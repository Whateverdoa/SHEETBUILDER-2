using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ConsoleApp1_vdp_sheetbuilder.Models;
using ConsoleApp1_vdp_sheetbuilder.Tests.Infrastructure;
using Xunit;

namespace ConsoleApp1_vdp_sheetbuilder.Tests;

public sealed class PdfProcessingIntegrationTests
{
    [Fact]
    public async Task ProcessPdf_CreatesDownloadableResult()
    {
        await using var factory = new SheetBuilderWebApplicationFactory();
        using var client = factory.CreateClient();

        using var pdfStream = PdfSampleFactory.CreateSamplePdf(pageCount: 3);
        using var form = BuildMultipartForm(pdfStream, fileName: "integration-sample.pdf", rotation: "180", order: "Rev");

        using var response = await client.PostAsync("/api/pdf/process", form);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<PdfProcessingResponse>();
        Assert.NotNull(payload);
        Assert.True(payload!.Success);
        Assert.False(string.IsNullOrWhiteSpace(payload.DownloadUrl));
        Assert.NotNull(payload.OutputFileName);

        using var download = await client.GetAsync(payload.DownloadUrl);
        download.EnsureSuccessStatusCode();
        var bytes = await download.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task ProcessPdfWithProgress_CompletesAndProvidesResult()
    {
        await using var factory = new SheetBuilderWebApplicationFactory();
        using var client = factory.CreateClient();

        using var pdfStream = PdfSampleFactory.CreateSamplePdf(pageCount: 5);
        using var form = BuildMultipartForm(pdfStream, fileName: "progress-sample.pdf", rotation: "0", order: "Norm");

        using var response = await client.PostAsync("/api/pdf/process-with-progress", form);
        response.EnsureSuccessStatusCode();

        var start = await response.Content.ReadFromJsonAsync<StartProcessingResponseDto>();
        Assert.NotNull(start);
        Assert.True(start!.Success);
        Assert.False(string.IsNullOrWhiteSpace(start.JobId));

        var final = await WaitForCompletionAsync(client, start.JobId!);
        Assert.NotNull(final);
        Assert.True(final!.Success);
        Assert.NotNull(final.DownloadUrl);

        using var download = await client.GetAsync(final.DownloadUrl);
        download.EnsureSuccessStatusCode();
        var bytes = await download.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
    }

    private static MultipartFormDataContent BuildMultipartForm(Stream pdfStream, string fileName, string rotation, string order)
    {
        var content = new MultipartFormDataContent();

        var streamContent = new StreamContent(pdfStream);
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(streamContent, "pdfFile", fileName);
        content.Add(new StringContent(rotation), "rotationAngle");
        content.Add(new StringContent(order), "order");

        return content;
    }

    private static async Task<PdfProcessingResponse?> WaitForCompletionAsync(HttpClient client, string jobId)
    {
        const int maxAttempts = 50;
        const int delayMilliseconds = 200;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            using var statusResponse = await client.GetAsync($"/api/pdf/status/{jobId}");
            statusResponse.EnsureSuccessStatusCode();

            var status = await statusResponse.Content.ReadFromJsonAsync<JobStatusResponseDto>();
            if (status is null)
            {
                await Task.Delay(delayMilliseconds);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(status.Error))
            {
                throw new InvalidOperationException($"Processing failed: {status.Error}");
            }

            if (string.Equals(status.Stage, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                return status.Result;
            }

            await Task.Delay(delayMilliseconds);
        }

        throw new TimeoutException("Timed out waiting for PDF processing to complete.");
    }

    private sealed class StartProcessingResponseDto
    {
        public bool Success { get; set; }
        public string? JobId { get; set; }
        public PdfProcessingResponse? Result { get; set; }
    }

    private sealed class JobStatusResponseDto
    {
        public bool Success { get; set; }
        public string? Stage { get; set; }
        public PdfProcessingResponse? Result { get; set; }
        public string? Error { get; set; }
    }
}
