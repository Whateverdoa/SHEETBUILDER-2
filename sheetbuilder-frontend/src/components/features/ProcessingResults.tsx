import { Download, CheckCircle, Clock, FileText, ArrowRight } from 'lucide-react'
import { Card, CardContent, Button } from '../ui'
import type { PdfProcessingResponse } from '../../types'
import { formatDuration, formatRelativeTime } from '../../utils'
import { useDownloadFile } from '../../services'
import toast from 'react-hot-toast'

interface ProcessingResultsProps {
  results: PdfProcessingResponse[]
  className?: string
}

interface ResultCardProps {
  result: PdfProcessingResponse
  timestamp: Date
}

function ResultCard({ result, timestamp }: ResultCardProps) {
  const { mutate: downloadFile, isPending: isDownloading } = useDownloadFile()

  const handleDownload = () => {
    if (result.downloadUrl && result.outputFileName) {
      downloadFile(
        { downloadUrl: result.downloadUrl, fileName: result.outputFileName },
        {
          onSuccess: () => {
            toast.success('Download started!')
          },
          onError: () => {
            toast.error('Failed to download file')
          },
        }
      )
    }
  }

  return (
    <Card className="hover:shadow-lg transition-all duration-200">
      <CardContent className="p-6">
        <div className="flex items-start justify-between">
          <div className="flex items-start space-x-4 flex-1">
            {/* Status Icon */}
            <div className={`w-10 h-10 rounded-full flex items-center justify-center ${
              result.success 
                ? 'bg-green-100 dark:bg-green-900/30' 
                : 'bg-red-100 dark:bg-red-900/30'
            }`}>
              {result.success ? (
                <CheckCircle className="w-5 h-5 text-green-600" />
              ) : (
                <Clock className="w-5 h-5 text-red-600" />
              )}
            </div>

            {/* File Info */}
            <div className="flex-1 min-w-0">
              <div className="flex items-center space-x-2 mb-1">
                <FileText className="w-4 h-4 text-slate-500" />
                <h3 className="text-sm font-medium text-slate-900 dark:text-slate-100 truncate">
                  {result.outputFileName || 'Processing Result'}
                </h3>
              </div>
              
              <p className="text-xs text-slate-500 dark:text-slate-400 mb-2">
                {formatRelativeTime(timestamp)}
              </p>

              {result.success ? (
                <div className="space-y-1">
                  <p className="text-sm text-green-700 dark:text-green-400">
                    ✓ {result.message}
                  </p>
                  
                  <div className="flex items-center space-x-4 text-xs text-slate-600 dark:text-slate-400">
                    <div className="flex items-center space-x-1">
                      <span>Input:</span>
                      <span className="font-medium">{result.inputPages} pages</span>
                    </div>
                    <ArrowRight className="w-3 h-3" />
                    <div className="flex items-center space-x-1">
                      <span>Output:</span>
                      <span className="font-medium">{result.outputPages} sheets</span>
                    </div>
                    <div className="flex items-center space-x-1">
                      <Clock className="w-3 h-3" />
                      <span>{formatDuration(result.processingTime)}</span>
                    </div>
                  </div>
                </div>
              ) : (
                <p className="text-sm text-red-700 dark:text-red-400">
                  ✗ {result.message || 'Processing failed'}
                </p>
              )}
            </div>
          </div>

          {/* Download Button */}
          {result.success && result.downloadUrl && (
            <Button
              variant="secondary"
              size="sm"
              onClick={handleDownload}
              loading={isDownloading}
              className="ml-4 flex-shrink-0"
            >
              <Download className="w-4 h-4" />
              Download
            </Button>
          )}
        </div>
      </CardContent>
    </Card>
  )
}

export function ProcessingResults({ results, className }: ProcessingResultsProps) {
  if (results.length === 0) {
    return (
      <Card className={className}>
        <CardContent className="p-12 text-center">
          <FileText className="w-12 h-12 text-slate-300 dark:text-slate-600 mx-auto mb-4" />
          <h3 className="text-lg font-medium text-slate-900 dark:text-slate-100 mb-2">
            No Processing Results
          </h3>
          <p className="text-slate-600 dark:text-slate-400">
            Upload and process a PDF file to see results here.
          </p>
        </CardContent>
      </Card>
    )
  }

  return (
    <div className={className}>
      <div className="flex items-center justify-between mb-6">
        <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-100">
          Processing Results
        </h2>
        <span className="text-sm text-slate-500 dark:text-slate-400">
          {results.length} result{results.length !== 1 ? 's' : ''}
        </span>
      </div>

      <div className="space-y-4">
        {results.map((result, index) => (
          <ResultCard
            key={index}
            result={result}
            timestamp={new Date(Date.now() - index * 60000)} // Mock timestamps
          />
        ))}
      </div>
    </div>
  )
}