namespace ConsoleApp1_vdp_sheetbuilder.Models
{
    public class FileStorageOptions
    {
        /// <summary>
        /// Directory where processed files are stored (relative to wwwroot)
        /// Default: "uploads"
        /// </summary>
        public string StorageDirectory { get; set; } = "uploads";

        /// <summary>
        /// Whether to automatically delete files after download
        /// Default: false
        /// </summary>
        public bool AutoDeleteAfterDownload { get; set; } = false;

        /// <summary>
        /// Maximum age in days before files are eligible for cleanup
        /// Default: 7 days
        /// </summary>
        public int MaxStorageAgeDays { get; set; } = 7;

        /// <summary>
        /// Get the full storage path (wwwroot/StorageDirectory)
        /// </summary>
        /// <param name="webRootPath">Web root path from IWebHostEnvironment</param>
        /// <returns>Full storage directory path</returns>
        public string GetStoragePath(string webRootPath)
        {
            return Path.Combine(webRootPath, StorageDirectory);
        }
    }
}
