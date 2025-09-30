# VDP Sheet Builder API

A .NET 8 Web API for processing PDF files for Variable Data Printing (VDP) sheet building. This API converts PDF processing functionality from a console application to a web service.

## Features

- **PDF Processing**: Upload and process PDF files for VDP sheet building
- **Page Rotation**: Rotate pages by specified angles (0°, 180°)
- **Page Reversal**: Option to reverse page order
- **Custom Page Sizing**: Creates custom-sized pages (317mm width, 980mm max height)
- **Page Concatenation**: Combines multiple pages into custom sheets
- **RESTful API**: Clean REST endpoints with proper HTTP status codes
- **Swagger Documentation**: Interactive API documentation
- **File Upload**: Support for large PDF file uploads (up to 100MB)
- **CORS Support**: Cross-origin resource sharing enabled

## API Endpoints

### POST /api/pdf/process
Process a PDF file for VDP sheet building.

**Request:**
- Content-Type: `multipart/form-data`
- Parameters:
  - `pdfFile` (required): PDF file to process
  - `rotationAngle` (optional): Rotation angle in degrees (0 or 180, default: 180)
  - `order` (optional): Page order ("Norm" or "Rev", default: "Rev")

**Response:**
```json
{
  "success": true,
  "message": "PDF processed successfully!",
  "outputFileName": "processed_file_A180_REV.pdf",
  "downloadUrl": "/uploads/processed_file_A180_REV.pdf",
  "processingTime": "00:00:02.345",
  "inputPages": 10,
  "outputPages": 3
}
```

### GET /api/pdf/health
Get API health status.

**Response:**
```json
{
  "status": "Healthy",
  "timestamp": "2024-01-15T10:30:00Z",
  "service": "VDP Sheet Builder API"
}
```

## Getting Started

### Prerequisites
- .NET 8.0 SDK
- Visual Studio 2022, VS Code, or Rider

### Installation

1. Clone the repository:
```bash
git clone <repository-url>
cd ConsoleApp1_vdp_sheetbuilder
```

2. Restore dependencies:
```bash
dotnet restore
```

3. Run the application:
```bash
dotnet run
```

4. Access the API:
- API Base URL: `https://localhost:7001` (or `http://localhost:5000`)
- Swagger UI: `https://localhost:7001/swagger`
- Web Interface: `https://localhost:7001`

### Configuration

The application can be configured through `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

## Usage Examples

### Using cURL
```bash
curl -X POST "https://localhost:7001/api/pdf/process" \
  -H "Content-Type: multipart/form-data" \
  -F "pdfFile=@input.pdf" \
  -F "rotationAngle=180" \
  -F "order=Rev"
```

### Using JavaScript/Fetch
```javascript
const formData = new FormData();
formData.append('pdfFile', fileInput.files[0]);
formData.append('rotationAngle', '180');
formData.append('order', 'Rev');

fetch('/api/pdf/process', {
  method: 'POST',
  body: formData
})
.then(response => response.json())
.then(result => {
  if (result.success) {
    console.log('Processing successful:', result.downloadUrl);
  } else {
    console.error('Processing failed:', result.message);
  }
});
```

### Using Python
```python
import requests

files = {'pdfFile': open('input.pdf', 'rb')}
data = {'rotationAngle': '180', 'order': 'Rev'}

response = requests.post('https://localhost:7001/api/pdf/process', 
                        files=files, data=data)
result = response.json()

if result['success']:
    print(f"Download URL: {result['downloadUrl']}")
else:
    print(f"Error: {result['message']}")
```

## Architecture

### Project Structure
```
ConsoleApp1_vdp_sheetbuilder/
├── Controllers/
│   └── PdfController.cs          # API endpoints
├── Models/
│   ├── PdfProcessingRequest.cs   # Request DTO
│   └── PdfProcessingResponse.cs  # Response DTO
├── Services/
│   └── PdfProcessingService.cs   # Business logic
├── wwwroot/
│   └── index.html               # Web interface
├── Program.cs                   # Application startup
└── README.md                   # This file
```

### Key Components

1. **PdfController**: Handles HTTP requests and responses
2. **PdfProcessingService**: Contains the core PDF processing logic
3. **Models**: Define request/response data structures
4. **Program.cs**: Configures the Web API application

## Error Handling

The API returns appropriate HTTP status codes:

- `200 OK`: Successful processing
- `400 Bad Request`: Invalid input (missing file, wrong file type, etc.)
- `500 Internal Server Error`: Server-side processing errors

## File Management

- Uploaded files are temporarily stored in `wwwroot/uploads/`
- Processed files are saved with unique names to avoid conflicts
- Temporary files are automatically cleaned up after processing
- File size limit: 100MB

## Security Considerations

- File type validation (PDF only)
- File size limits
- CORS configuration for cross-origin requests
- Input validation and sanitization

## Performance

- Asynchronous processing for better scalability
- Memory management with proper disposal of PDF objects
- Garbage collection optimization
- Processing time tracking

## Development

### Adding New Features

1. Create new models in the `Models/` directory
2. Add business logic to `Services/`
3. Create new endpoints in `Controllers/`
4. Update Swagger documentation
5. Add tests (recommended)

### Testing

Run the application and test with:
- Swagger UI at `/swagger`
- Web interface at `/`
- Direct API calls using tools like Postman

## Troubleshooting

### Common Issues

1. **File Upload Fails**: Check file size limit (100MB) and file type (PDF only)
2. **Processing Errors**: Check server logs for detailed error messages
3. **CORS Issues**: Verify CORS configuration in `Program.cs`

### Logs

Enable detailed logging by modifying `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "ConsoleApp1_vdp_sheetbuilder": "Debug"
    }
  }
}
```

## License

[Add your license information here]

## Contributing

[Add contribution guidelines here]