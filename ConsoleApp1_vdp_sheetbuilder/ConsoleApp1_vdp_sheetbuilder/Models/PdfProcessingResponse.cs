using System.Text.Json.Serialization;

namespace ConsoleApp1_vdp_sheetbuilder.Models
{
    public class PdfProcessingResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? OutputFileName { get; set; }
        public string? DownloadUrl { get; set; }

        [JsonIgnore]
        public TimeSpan ProcessingTimeSpan { get; set; }

        [JsonPropertyName("processingTime")]
        public string ProcessingTime => ProcessingTimeSpan.ToString(@"hh\:mm\:ss\.fff");

        public int InputPages { get; set; }
        public int OutputPages { get; set; }
    }
}