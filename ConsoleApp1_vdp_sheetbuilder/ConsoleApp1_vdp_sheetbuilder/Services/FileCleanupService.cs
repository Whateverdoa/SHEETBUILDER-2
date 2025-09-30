using ConsoleApp1_vdp_sheetbuilder.Models;
using Microsoft.Extensions.Options;

namespace ConsoleApp1_vdp_sheetbuilder.Services
{
    public class FileCleanupService : BackgroundService
    {
        private readonly ILogger<FileCleanupService> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly FileStorageOptions _storageOptions;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(6); // Run cleanup every 6 hours

        public FileCleanupService(
            ILogger<FileCleanupService> logger,
            IWebHostEnvironment environment,
            IOptions<FileStorageOptions> storageOptions)
        {
            _logger = logger;
            _environment = environment;
            _storageOptions = storageOptions.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("File cleanup service started. Running every {Interval} hours", _cleanupInterval.TotalHours);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PerformCleanup();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during file cleanup");
                }

                await Task.Delay(_cleanupInterval, stoppingToken);
            }
        }

        private async Task PerformCleanup()
        {
            var storageDirectory = _storageOptions.GetStoragePath(_environment.WebRootPath);

            if (!Directory.Exists(storageDirectory))
            {
                _logger.LogDebug("Storage directory does not exist: {Directory}", storageDirectory);
                return;
            }

            var cutoffDate = DateTime.UtcNow.AddDays(-_storageOptions.MaxStorageAgeDays);
            var files = Directory.GetFiles(storageDirectory, "*.pdf");
            var deletedCount = 0;
            var totalSize = 0L;

            _logger.LogInformation("Starting file cleanup. Checking {FileCount} files older than {CutoffDate}",
                files.Length, cutoffDate);

            foreach (var filePath in files)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);

                    if (fileInfo.CreationTimeUtc < cutoffDate)
                    {
                        totalSize += fileInfo.Length;
                        File.Delete(filePath);
                        deletedCount++;

                        _logger.LogDebug("Deleted old file: {FileName} (Created: {CreationTime})",
                            fileInfo.Name, fileInfo.CreationTimeUtc);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete file: {FilePath}", filePath);
                }
            }

            if (deletedCount > 0)
            {
                _logger.LogInformation("File cleanup completed. Deleted {DeletedCount} files, freed {SizeMB:F1} MB",
                    deletedCount, totalSize / 1024.0 / 1024.0);
            }
            else
            {
                _logger.LogDebug("File cleanup completed. No files needed cleanup.");
            }
        }
    }
}
