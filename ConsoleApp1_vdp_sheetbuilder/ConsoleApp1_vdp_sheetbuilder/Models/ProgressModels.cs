namespace ConsoleApp1_vdp_sheetbuilder.Models
{
    public class ProcessingProgress
    {
        public string JobId { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public float PercentageComplete { get; set; }
        public double PagesPerSecond { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public string CurrentOperation { get; set; } = string.Empty;
        public PerformanceMetrics Performance { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class PerformanceMetrics
    {
        public long MemoryUsageMB { get; set; }
        public int CacheHitCount { get; set; }
        public int CacheMissCount { get; set; }
        public double CacheHitRatio => CacheHitCount + CacheMissCount > 0
            ? (double)CacheHitCount / (CacheHitCount + CacheMissCount) * 100
            : 0;
        public int XObjectsCached { get; set; }
        public int SheetsGenerated { get; set; }
    }

    public enum ProcessingStage
    {
        Initializing,
        PreparingDimensions,
        ProcessingPages,
        OptimizingOutput,
        Finalizing,
        Completed,
        Failed
    }

    public class JobStatus
    {
        public string JobId { get; set; } = string.Empty;
        public ProcessingStage Stage { get; set; }
        public ProcessingProgress? Progress { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public PdfProcessingResponse? Result { get; set; }
    }
}