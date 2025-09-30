using System.ComponentModel.DataAnnotations;

namespace ConsoleApp1_vdp_sheetbuilder.Models
{
    public class UploadReliabilityOptions
    {
        private const int DefaultThresholdMb = 200;
        private const int DefaultTtlMinutes = 30;

        /// <summary>
        /// If true, the legacy synchronous endpoint rejects files larger than <see cref="LargeFileThresholdMb"/>.
        /// </summary>
        public bool EnforceProgressForLarge { get; set; } = true;

        /// <summary>
        /// Size threshold (in megabytes) used to steer large uploads toward the progress API.
        /// </summary>
        [Range(1, 2048)]
        public int LargeFileThresholdMb { get; set; } = DefaultThresholdMb;

        /// <summary>
        /// Enables deduplication of active jobs and reuse of recent results based on upload fingerprints.
        /// </summary>
        public bool IdempotencyActive { get; set; } = true;

        /// <summary>
        /// Minutes that completed job results remain available for reuse.
        /// </summary>
        [Range(1, 1440)]
        public int RecentResultTtlMinutes { get; set; } = DefaultTtlMinutes;

        public long LargeFileThresholdBytes => (long)Math.Max(1, LargeFileThresholdMb) * 1024L * 1024L;
        public TimeSpan RecentResultTtl => TimeSpan.FromMinutes(Math.Max(1, RecentResultTtlMinutes));
    }
}
