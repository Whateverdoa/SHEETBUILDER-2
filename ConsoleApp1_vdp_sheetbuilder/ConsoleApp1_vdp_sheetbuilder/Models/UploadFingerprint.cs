using System.Security.Cryptography;
using System.Text;

namespace ConsoleApp1_vdp_sheetbuilder.Models
{
    /// <summary>
    /// Lightweight fingerprint to identify equivalent uploads (same file + parameters).
    /// </summary>
    public readonly record struct UploadFingerprint(string FileName, long FileSizeBytes, int RotationAngle, string Order)
    {
        public static UploadFingerprint From(string fileName, long fileSizeBytes, int rotationAngle, string order)
        {
            var normalizedOrder = string.IsNullOrWhiteSpace(order)
                ? string.Empty
                : order.Trim().ToUpperInvariant();
            var normalizedName = fileName?.Trim() ?? string.Empty;

            return new UploadFingerprint(normalizedName, fileSizeBytes, rotationAngle, normalizedOrder);
        }

        public string ToDeterministicKey()
        {
            // Use SHA256 to avoid storing very long filenames directly in dictionaries/logs.
            var payload = $"{FileName}\n{FileSizeBytes}\n{RotationAngle}\n{Order}";
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash);
        }

        public override string ToString()
        {
            return $"{FileName} ({FileSizeBytes} bytes, {RotationAngle}°, {Order})";
        }
    }
}
