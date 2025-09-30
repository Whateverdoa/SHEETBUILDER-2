import { useCallback } from 'react'
import { useDropzone } from 'react-dropzone'
import { Upload, File, X, AlertCircle } from 'lucide-react'
import { Card, Button, Progress } from '../ui'
import { cn, formatFileSize, validatePdfFile } from '../../utils'

interface FileUploadProps {
  onFileSelect: (files: File[]) => void
  onFileRemove: (index: number) => void
  selectedFiles?: File[]
  uploading?: boolean
  uploadProgress?: number
  error?: string
  disabled?: boolean
  className?: string
  currentlyProcessing?: number
}

export function FileUpload({
  onFileSelect,
  onFileRemove,
  selectedFiles = [],
  uploading = false,
  uploadProgress = 0,
  error,
  disabled = false,
  className,
  currentlyProcessing = -1,
}: FileUploadProps) {
  const onDrop = useCallback(
    (acceptedFiles: File[]) => {
      if (acceptedFiles.length > 0) {
        const validFiles = acceptedFiles.filter(file => {
          const validation = validatePdfFile(file)
          return validation.valid
        })
        
        if (validFiles.length > 0) {
          onFileSelect([...selectedFiles, ...validFiles])
        }
      }
    },
    [onFileSelect, selectedFiles]
  )

  const { getRootProps, getInputProps, isDragActive, fileRejections } = useDropzone({
    onDrop,
    accept: {
      'application/pdf': ['.pdf'],
    },
    multiple: true,
    // Allow large files; validatePdfFile will surface a non-blocking warning
    disabled: disabled || uploading,
  })

  const rejectionError = fileRejections[0]?.errors[0]?.message

  return (
    <div className={cn('space-y-4', className)}>
      {selectedFiles.length === 0 ? (
        <Card
          {...getRootProps()}
          className={cn(
            'upload-zone cursor-pointer',
            {
              'drag-active': isDragActive,
              'opacity-50 cursor-not-allowed': disabled,
            }
          )}
        >
          <input {...getInputProps()} />
          
          <div className="flex flex-col items-center justify-center space-y-4">
            <div className={cn(
              'w-16 h-16 rounded-full flex items-center justify-center',
              'bg-primary-100 dark:bg-primary-900/30'
            )}>
              <Upload className="w-8 h-8 text-primary-600" />
            </div>
            
            <div className="text-center">
              <h3 className="text-lg font-semibold text-slate-900 dark:text-slate-100">
                {isDragActive ? 'Drop your PDFs here' : 'Upload PDF Files'}
              </h3>
              <p className="text-sm text-slate-600 dark:text-slate-400 mt-1">
                Drag and drop your PDF files here, or{' '}
                <span className="text-primary-600 font-medium">click to browse</span>
              </p>
              <p className="text-xs text-slate-500 dark:text-slate-500 mt-2">
                Multiple files supported • Large PDFs stream directly with progress tracking
              </p>
            </div>
          </div>
        </Card>
      ) : (
        <div className="space-y-3">
          {/* Add more files button */}
          <Card
            {...getRootProps()}
            className={cn(
              'p-3 border-2 border-dashed border-primary-300 dark:border-primary-700 cursor-pointer hover:bg-primary-50 dark:hover:bg-primary-900/20',
              {
                'opacity-50 cursor-not-allowed': disabled || uploading,
              }
            )}
          >
            <input {...getInputProps()} />
            <div className="flex items-center justify-center space-x-2">
              <Upload className="w-4 h-4 text-primary-600" />
              <span className="text-sm text-primary-600 font-medium">
                Add more files
              </span>
            </div>
          </Card>

          {/* File list */}
          <div className="space-y-2">
            {selectedFiles.map((file, index) => (
              <Card key={`${file.name}-${index}`} className="p-3">
                <div className="flex items-center justify-between">
                  <div className="flex items-center space-x-3">
                    <div className={cn(
                      'w-8 h-8 rounded-lg flex items-center justify-center',
                      currentlyProcessing === index 
                        ? 'bg-blue-100 dark:bg-blue-900/30'
                        : 'bg-red-100 dark:bg-red-900/30'
                    )}>
                      <File className={cn(
                        'w-4 h-4',
                        currentlyProcessing === index 
                          ? 'text-blue-600'
                          : 'text-red-600'
                      )} />
                    </div>
                    
                    <div className="flex-1 min-w-0">
                      <p className="text-sm font-medium text-slate-900 dark:text-slate-100 truncate">
                        {file.name}
                      </p>
                      <p className="text-xs text-slate-500 dark:text-slate-400">
                        {formatFileSize(file.size)}
                      </p>
                    </div>
                  </div>
                  
                  <div className="flex items-center space-x-2">
                    {currentlyProcessing === index ? (
                      <span className="text-xs text-blue-600 font-medium">
                        Processing...
                      </span>
                    ) : index < currentlyProcessing ? (
                      <span className="text-xs text-green-600 font-medium">
                        ✓ Done
                      </span>
                    ) : (
                      <span className="text-xs text-slate-500">
                        Queued
                      </span>
                    )}
                    
                    {!uploading && (
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => onFileRemove(index)}
                        className="text-slate-500 hover:text-red-600"
                      >
                        <X className="w-4 h-4" />
                      </Button>
                    )}
                  </div>
                </div>
                
                {uploading && currentlyProcessing === index && (
                  <div className="mt-2">
                    <Progress
                      value={uploadProgress}
                      showLabel
                      label="Processing..."
                      size="sm"
                    />
                  </div>
                )}
              </Card>
            ))}
          </div>
        </div>
      )}
      
      {(error || rejectionError) && (
        <div className="flex items-start space-x-2 p-3 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg">
          <AlertCircle className="w-4 h-4 text-red-600 mt-0.5 flex-shrink-0" />
          <p className="text-sm text-red-700 dark:text-red-400">
            {error || rejectionError}
          </p>
        </div>
      )}
    </div>
  )
}
