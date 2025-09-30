using System.ComponentModel.DataAnnotations;

namespace ConsoleApp1_vdp_sheetbuilder.Models
{
    public class PdfProcessingRequest
    {
        [Required]
        public IFormFile PdfFile { get; set; } = null!;

        [Range(0, 360)]
        public int RotationAngle { get; set; } = 180;

        [Required]
        [RegularExpression("^(Norm|Rev)$", ErrorMessage = "Order must be either 'Norm' or 'Rev'")]
        public string Order { get; set; } = "Rev";

        public bool ReverseOrder => Order.Equals("Rev", StringComparison.OrdinalIgnoreCase);
    }
}