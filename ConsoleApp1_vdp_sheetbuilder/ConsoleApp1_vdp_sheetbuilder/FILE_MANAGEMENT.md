# File Management Guide

This document explains how processed PDF files are handled and the available options for managing file storage and cleanup.

## Current Behavior

### Default File Storage
- **Location**: `wwwroot/uploads/` directory
- **Naming**: Files get a GUID prefix to avoid conflicts: `{GUID}_{originalname}_A90_REV.pdf`
- **Access**: Files are accessible via static file serving at `/uploads/{filename}`

### File Lifecycle
1. **Input files**: Uploaded temporarily, then **deleted** after processing
2. **Output files**: Remain in storage for download
3. **Intermediate files**: Cleaned up automatically (reversed files, temp files)

## Configuration Options

### Via appsettings.json
```json
{
  "FileStorage": {
    "Directory": "uploads",
    "AutoDeleteAfterDownload": false,
    "MaxStorageAgeDays": 7
  }
}
```

### Via Environment Variables
```bash
FileStorage__Directory=processed-pdfs
FileStorage__AutoDeleteAfterDownload=true
FileStorage__MaxStorageAgeDays=3
```

## Download Options

### Option 1: Direct Static File Access (Current Default)
- **URL**: `/uploads/{filename}`
- **Behavior**: Files remain after download
- **Use Case**: When users need to download files multiple times

### Option 2: Download with Optional Auto-Delete
- **URL**: `/api/pdf/download/{filename}?deleteAfterDownload=true`
- **Behavior**: File is deleted after successful download
- **Use Case**: One-time downloads to save storage space

## Storage Directory Options

### Change Storage Location
You can change where files are stored by modifying the configuration:

**Example 1: Different subdirectory**
```json
{
  "FileStorage": {
    "Directory": "processed-files"
  }
}
```
Files will be stored in `wwwroot/processed-files/`

**Example 2: Absolute path (outside wwwroot)**
For security or performance reasons, you might want to store files outside the web-accessible directory. This would require additional code changes to serve files through the download endpoint instead of static file serving.

## Automatic Cleanup

The system includes a background service that automatically cleans up old files:

- **Frequency**: Runs every 6 hours
- **Criteria**: Files older than `MaxStorageAgeDays` (default: 7 days)
- **Logging**: Reports cleanup activity with file counts and freed space

## API Endpoints

### Process PDF (keeps files)
```http
POST /api/pdf/process
POST /api/pdf/process-with-progress
```

### Download with auto-delete option
```http
GET /api/pdf/download/{filename}?deleteAfterDownload=true
```

### Check file status
Files are accessible until:
- Manually deleted via the download endpoint
- Automatically cleaned up by the background service
- Manually removed from the file system

## Best Practices

### For Development
- Keep `AutoDeleteAfterDownload=false` for debugging
- Set `MaxStorageAgeDays=1` for quick cleanup

### For Production
- Set `AutoDeleteAfterDownload=true` if downloads are one-time only
- Configure appropriate `MaxStorageAgeDays` based on your use case
- Monitor disk space usage
- Consider implementing additional security measures for file access

### For High-Volume Environments
- Use the auto-delete download endpoint
- Set shorter `MaxStorageAgeDays`
- Consider implementing file compression
- Monitor the cleanup service logs for performance

## Security Considerations

- Files are stored with GUID prefixes making them hard to guess
- Static file serving exposes files directly via URL
- Consider implementing authentication for sensitive files
- The download endpoint provides more control over file access

## Troubleshooting

### Files Not Being Cleaned Up
- Check the background service logs
- Verify `MaxStorageAgeDays` configuration
- Ensure the application has write permissions to the storage directory

### Download Endpoint Returns 404
- Verify the filename includes the GUID prefix
- Check if the file was already deleted by auto-cleanup
- Ensure the file exists in the configured storage directory

### Storage Directory Issues
- Ensure the directory exists and is writable
- Check that the path is correctly configured
- Verify permissions for both read and write access
