using System.Collections.Concurrent;
using ConsoleApp1_vdp_sheetbuilder.Models;
using Microsoft.Extensions.Options;

namespace ConsoleApp1_vdp_sheetbuilder.Services
{
    public interface IUploadReliabilityService
    {
        UploadReliabilityOptions Options { get; }
        JobRegistrationOutcome RegisterOrResolveJob(UploadFingerprint fingerprint, Func<string> jobFactory);
        void MarkJobCompleted(UploadFingerprint fingerprint, string jobId, PdfProcessingResponse result);
        void MarkJobFailed(UploadFingerprint fingerprint, string jobId, string? error = null);
        bool ShouldBlockLegacyEndpoint(long fileSizeBytes);
    }

    public enum JobRegistrationState
    {
        Registered,
        DuplicateActive,
        DuplicateCompleted
    }

    public sealed record JobRegistrationOutcome(JobRegistrationState State, string JobId, bool DuplicateOfExisting, PdfProcessingResponse? CompletedResult = null)
    {
        public static JobRegistrationOutcome Registered(string jobId) => new(JobRegistrationState.Registered, jobId, false, null);
        public static JobRegistrationOutcome ActiveDuplicate(string jobId) => new(JobRegistrationState.DuplicateActive, jobId, true, null);
        public static JobRegistrationOutcome CompletedDuplicate(string jobId, PdfProcessingResponse result) => new(JobRegistrationState.DuplicateCompleted, jobId, true, result);
    }

    internal sealed record ActiveJobInfo(string JobId, UploadFingerprint Fingerprint, DateTime StartedAt);

    internal sealed record CompletedJobInfo(string JobId, UploadFingerprint Fingerprint, DateTime CompletedAt, PdfProcessingResponse Result);

    public class UploadReliabilityService : IUploadReliabilityService, IDisposable
    {
        private readonly ConcurrentDictionary<string, ActiveJobInfo> _activeJobs = new();
        private readonly ConcurrentDictionary<string, CompletedJobInfo> _recentResults = new();
        private readonly ILogger<UploadReliabilityService> _logger;
        private readonly UploadReliabilityOptions _options;
        private readonly Timer _cleanupTimer;
        private bool _disposed;

        public UploadReliabilityService(IOptions<UploadReliabilityOptions> options, ILogger<UploadReliabilityService> logger)
        {
            _logger = logger;
            _options = options.Value ?? new UploadReliabilityOptions();

            // Run cleanup every five minutes to expire completed jobs beyond TTL
            _cleanupTimer = new Timer(_ => CleanupExpiredResults(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public UploadReliabilityOptions Options => _options;

        public bool ShouldBlockLegacyEndpoint(long fileSizeBytes)
        {
            if (!_options.EnforceProgressForLarge)
            {
                return false;
            }

            return fileSizeBytes >= _options.LargeFileThresholdBytes;
        }

        public JobRegistrationOutcome RegisterOrResolveJob(UploadFingerprint fingerprint, Func<string> jobFactory)
        {
            if (!_options.IdempotencyActive)
            {
                var freshJobId = jobFactory();
                var key = fingerprint.ToDeterministicKey();
                _activeJobs[key] = new ActiveJobInfo(freshJobId, fingerprint, DateTime.UtcNow);
                return JobRegistrationOutcome.Registered(freshJobId);
            }

            var now = DateTime.UtcNow;
            var fingerprintKey = fingerprint.ToDeterministicKey();

            if (_activeJobs.TryGetValue(fingerprintKey, out var active))
            {
                _logger.LogInformation("♻️ Duplicate upload detected for {Fingerprint}; reusing active job {JobId}", fingerprint, active.JobId);
                return JobRegistrationOutcome.ActiveDuplicate(active.JobId);
            }

            if (_recentResults.TryGetValue(fingerprintKey, out var completed))
            {
                if (now - completed.CompletedAt <= _options.RecentResultTtl)
                {
                    _logger.LogInformation("📦 Serving cached result for {Fingerprint}; completed job {JobId} is still fresh", fingerprint, completed.JobId);
                    return JobRegistrationOutcome.CompletedDuplicate(completed.JobId, CloneResponse(completed.Result));
                }

                // Remove stale result so future requests can progress.
                _recentResults.TryRemove(fingerprintKey, out _);
            }

            var jobId = jobFactory();
            var info = new ActiveJobInfo(jobId, fingerprint, now);

            if (!_activeJobs.TryAdd(fingerprintKey, info))
            {
                // Another request won the race; return the existing job instead of running twice.
                var existing = _activeJobs[fingerprintKey];
                _logger.LogInformation("⚠️ Race detected. Returning existing job {JobId} for fingerprint {Fingerprint}", existing.JobId, fingerprint);
                return JobRegistrationOutcome.ActiveDuplicate(existing.JobId);
            }

            _logger.LogInformation("🆕 Registered job {JobId} for fingerprint {Fingerprint}", jobId, fingerprint);
            return JobRegistrationOutcome.Registered(jobId);
        }

        public void MarkJobCompleted(UploadFingerprint fingerprint, string jobId, PdfProcessingResponse result)
        {
            ReleaseActiveJob(fingerprint, jobId);

            if (!_options.IdempotencyActive)
            {
                return;
            }

            var key = fingerprint.ToDeterministicKey();
            var completion = new CompletedJobInfo(jobId, fingerprint, DateTime.UtcNow, CloneResponse(result));
            _recentResults[key] = completion;

            _logger.LogInformation("✅ Cached completion for job {JobId} ({Fingerprint})", jobId, fingerprint);
        }

        public void MarkJobFailed(UploadFingerprint fingerprint, string jobId, string? error = null)
        {
            ReleaseActiveJob(fingerprint, jobId);

            if (!_options.IdempotencyActive)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogWarning("🛑 Clearing active job {JobId} ({Fingerprint}) after failure: {Error}", jobId, fingerprint, error);
            }
            else
            {
                _logger.LogWarning("🛑 Clearing active job {JobId} ({Fingerprint}) after failure", jobId, fingerprint);
            }
        }

        private void ReleaseActiveJob(UploadFingerprint fingerprint, string jobId)
        {
            var key = fingerprint.ToDeterministicKey();
            if (_activeJobs.TryGetValue(key, out var existing) && existing.JobId == jobId)
            {
                _activeJobs.TryRemove(key, out _);
            }
        }

        private void CleanupExpiredResults()
        {
            if (!_options.IdempotencyActive)
            {
                return;
            }

            var expirationThreshold = DateTime.UtcNow - _options.RecentResultTtl;

            foreach (var entry in _recentResults)
            {
                if (entry.Value.CompletedAt < expirationThreshold)
                {
                    _recentResults.TryRemove(entry.Key, out _);
                    _logger.LogDebug("🧹 Expired cached result for {Fingerprint}", entry.Value.Fingerprint);
                }
            }
        }

        private static PdfProcessingResponse CloneResponse(PdfProcessingResponse source)
        {
            return new PdfProcessingResponse
            {
                Success = source.Success,
                Message = source.Message,
                OutputFileName = source.OutputFileName,
                DownloadUrl = source.DownloadUrl,
                ProcessingTimeSpan = source.ProcessingTimeSpan,
                InputPages = source.InputPages,
                OutputPages = source.OutputPages
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _cleanupTimer.Dispose();
            _disposed = true;
        }
    }
}
