using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ConsoleApp1_vdp_sheetbuilder.Models;

namespace ConsoleApp1_vdp_sheetbuilder.Services
{
    public interface IProgressService
    {
        string CreateJob();
        void UpdateProgress(string jobId, ProcessingProgress progress);
        void UpdateStage(string jobId, ProcessingStage stage, string operation = "");
        void CompleteJob(string jobId, PdfProcessingResponse result);
        void FailJob(string jobId, string errorMessage);
        JobStatus? GetJobStatus(string jobId);
        IAsyncEnumerable<ProcessingProgress> SubscribeToProgress(string jobId, CancellationToken cancellationToken);
        void CleanupOldJobs();
    }

    public class ProgressService : IProgressService
    {
        private readonly ConcurrentDictionary<string, JobStatus> _jobs = new();
        private readonly ConcurrentDictionary<string, List<TaskCompletionSource<ProcessingProgress>>> _subscribers = new();
        private readonly ILogger<ProgressService> _logger;
        private readonly Timer _cleanupTimer;

        public ProgressService(ILogger<ProgressService> logger)
        {
            _logger = logger;
            // Clean up old jobs every 5 minutes
            _cleanupTimer = new Timer(CleanupCallback, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public string CreateJob()
        {
            var jobId = Guid.NewGuid().ToString("N")[..12]; // Short job ID
            var job = new JobStatus
            {
                JobId = jobId,
                Stage = ProcessingStage.Initializing,
                StartTime = DateTime.UtcNow
            };

            _jobs[jobId] = job;
            _subscribers[jobId] = new List<TaskCompletionSource<ProcessingProgress>>();

            _logger.LogInformation("📋 Created processing job: {JobId}", jobId);
            return jobId;
        }

        public void UpdateProgress(string jobId, ProcessingProgress progress)
        {
            if (!_jobs.TryGetValue(jobId, out var job)) return;

            progress.JobId = jobId;
            job.Progress = progress;

            // Notify all subscribers
            if (_subscribers.TryGetValue(jobId, out var subscribers))
            {
                lock (subscribers)
                {
                    foreach (var subscriber in subscribers.ToList())
                    {
                        try
                        {
                            subscriber.SetResult(progress);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to notify progress subscriber for job {JobId}", jobId);
                        }
                    }
                    subscribers.Clear();
                }
            }
        }

        public void UpdateStage(string jobId, ProcessingStage stage, string operation = "")
        {
            if (!_jobs.TryGetValue(jobId, out var job)) return;

            job.Stage = stage;

            var progress = job.Progress ?? new ProcessingProgress { JobId = jobId };
            progress.Stage = stage.ToString();
            progress.CurrentOperation = operation;

            UpdateProgress(jobId, progress);

            _logger.LogDebug("🔄 Job {JobId} stage updated: {Stage} - {Operation}", jobId, stage, operation);
        }

        public void CompleteJob(string jobId, PdfProcessingResponse result)
        {
            if (!_jobs.TryGetValue(jobId, out var job)) return;

            job.Stage = ProcessingStage.Completed;
            job.EndTime = DateTime.UtcNow;
            job.Result = result;

            var progress = job.Progress ?? new ProcessingProgress { JobId = jobId };
            progress.Stage = "Completed";
            progress.PercentageComplete = 100;
            progress.CurrentOperation = "Processing completed successfully";

            UpdateProgress(jobId, progress);

            var duration = job.EndTime.Value - job.StartTime;
            _logger.LogInformation("✅ Job {JobId} completed in {Duration:F1}s", jobId, duration.TotalSeconds);
        }

        public void FailJob(string jobId, string errorMessage)
        {
            if (!_jobs.TryGetValue(jobId, out var job)) return;

            job.Stage = ProcessingStage.Failed;
            job.EndTime = DateTime.UtcNow;
            job.ErrorMessage = errorMessage;

            var progress = job.Progress ?? new ProcessingProgress { JobId = jobId };
            progress.Stage = "Failed";
            progress.CurrentOperation = $"Error: {errorMessage}";

            UpdateProgress(jobId, progress);

            _logger.LogError("❌ Job {JobId} failed: {Error}", jobId, errorMessage);
        }

        public JobStatus? GetJobStatus(string jobId)
        {
            return _jobs.TryGetValue(jobId, out var job) ? job : null;
        }

        public async IAsyncEnumerable<ProcessingProgress> SubscribeToProgress(string jobId, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!_jobs.ContainsKey(jobId))
            {
                _logger.LogWarning("Attempted to subscribe to non-existent job: {JobId}", jobId);
                yield break;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                ProcessingProgress? progress = null;

                try
                {
                    var tcs = new TaskCompletionSource<ProcessingProgress>();

                    if (_subscribers.TryGetValue(jobId, out var subscribers))
                    {
                        lock (subscribers)
                        {
                            subscribers.Add(tcs);
                        }
                    }
                    else
                    {
                        // Job might have been cleaned up
                        break;
                    }

                    // Wait for next progress update or timeout
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                    combinedCts.Token.Register(() => tcs.TrySetCanceled());

                    progress = await tcs.Task;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in progress subscription for job {JobId}", jobId);
                    break;
                }

                if (progress != null)
                {
                    yield return progress;

                    // Check if job is completed
                    if (_jobs.TryGetValue(jobId, out var job) &&
                        (job.Stage == ProcessingStage.Completed || job.Stage == ProcessingStage.Failed))
                    {
                        break;
                    }
                }
            }
        }

        public void CleanupOldJobs()
        {
            var cutoff = DateTime.UtcNow.AddHours(-2); // Keep jobs for 2 hours
            var jobsToRemove = _jobs.Where(kvp =>
                kvp.Value.EndTime.HasValue && kvp.Value.EndTime.Value < cutoff ||
                !kvp.Value.EndTime.HasValue && kvp.Value.StartTime < cutoff.AddMinutes(-30) // Remove stuck jobs after 30 minutes
            ).Select(kvp => kvp.Key).ToList();

            foreach (var jobId in jobsToRemove)
            {
                _jobs.TryRemove(jobId, out _);
                _subscribers.TryRemove(jobId, out _);
            }

            if (jobsToRemove.Count > 0)
            {
                _logger.LogInformation("🧹 Cleaned up {Count} old jobs", jobsToRemove.Count);
            }
        }

        private void CleanupCallback(object? state)
        {
            try
            {
                CleanupOldJobs();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during job cleanup");
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }
    }
}