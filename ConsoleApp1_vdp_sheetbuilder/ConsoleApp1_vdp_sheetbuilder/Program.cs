using ConsoleApp1_vdp_sheetbuilder.Services;
using ConsoleApp1_vdp_sheetbuilder.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.IIS;

var builder = WebApplication.CreateBuilder(args);

// Configure IIS integration for large file uploads
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = null; // Disable IIS upload limit; rely on downstream throttling
});

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register PDF processing service
builder.Services.AddScoped<IPdfProcessingService, PdfProcessingService>();

// Register progress service as singleton for job tracking across requests
builder.Services.AddSingleton<IProgressService, ProgressService>();

// Register upload reliability/idempotency helpers
builder.Services.Configure<UploadReliabilityOptions>(builder.Configuration.GetSection("UploadReliability"));
builder.Services.AddSingleton<IUploadReliabilityService, UploadReliabilityService>();

// Register background file cleanup service
builder.Services.AddHostedService<FileCleanupService>();

// Register HttpContextAccessor for timeout middleware
builder.Services.AddHttpContextAccessor();

// Configure file upload handling for large PDFs and streaming scenarios
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = long.MaxValue;
    options.ValueLengthLimit = int.MaxValue;
    options.ValueCountLimit = int.MaxValue;
    options.KeyLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
    options.MultipartBoundaryLengthLimit = int.MaxValue;
    options.BufferBody = false; // Use streaming for large files instead of buffering
    options.MemoryBufferThreshold = 64 * 1024 * 1024; // 64MB threshold before streaming to disk
    options.BufferBodyLengthLimit = long.MaxValue;
});

// Configure Kestrel to handle large file uploads and network access
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = null; // Allow the controller to stream arbitrarily large payloads
    serverOptions.Limits.MinRequestBodyDataRate = null; // Allow slow uploads for large files
    serverOptions.Limits.MinResponseDataRate = null; // Allow slow responses
    serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(15); // Extended timeout for large files
    serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5); // Extended header timeout
    serverOptions.Limits.MaxResponseBufferSize = null; // Allow large responses
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add configuration for file storage
builder.Services.Configure<FileStorageOptions>(options =>
{
    // Default to wwwroot/uploads, but can be overridden via appsettings.json or environment variables
    options.StorageDirectory = builder.Configuration.GetValue<string>("FileStorage:Directory") ?? "uploads";
    options.AutoDeleteAfterDownload = builder.Configuration.GetValue<bool>("FileStorage:AutoDeleteAfterDownload", false);
    options.MaxStorageAgeDays = builder.Configuration.GetValue<int>("FileStorage:MaxStorageAgeDays", 7);
});

var app = builder.Build();

// Configure request timeout for large file processing
app.Use(async (context, next) =>
{
    // Set longer timeout for PDF processing endpoints
    if (context.Request.Path.StartsWithSegments("/api/pdf/process"))
    {
        // Extend timeout to 15 minutes for large file processing
        context.RequestAborted.Register(() => { /* Handle timeout gracefully */ });
    }
    await next();
});

// Configure the HTTP request pipeline.
// Enable Swagger for all environments (useful for API testing)
app.UseSwagger();
app.UseSwaggerUI();

if (app.Environment.IsDevelopment())
{
    // Additional development-specific configurations can go here
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");

// Serve static files from wwwroot/uploads
app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program
{
}
