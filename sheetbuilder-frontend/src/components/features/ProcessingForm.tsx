import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Settings, RotateCcw, ArrowUpDown } from 'lucide-react'
import { Card, CardHeader, CardContent, Button, Select, LoadingSpinner, Progress } from '../ui'
import { FileUpload } from './FileUpload'
import { useProcessPdf } from '../../services'
import { formatFileSize } from '../../utils'
import type { PdfProcessingRequest, UploadProgress, ChunkProgress } from '../../types'
import toast from 'react-hot-toast'

const processingSchema = z.object({
  rotationAngle: z.number().min(0).max(360),
  order: z.enum(['Norm', 'Rev']),
})

type ProcessingFormData = z.infer<typeof processingSchema>

interface ProcessingFormProps {
  onProcessingComplete?: (response: any) => void
  disabled?: boolean
}

export function ProcessingForm({ onProcessingComplete, disabled = false }: ProcessingFormProps) {
  const [selectedFiles, setSelectedFiles] = useState<File[]>([])
  const [uploadProgress, setUploadProgress] = useState(0)
  const [currentStage, setCurrentStage] = useState<string>('Ready')
  const [chunkInfo, setChunkInfo] = useState<{ current: number; total: number } | null>(null)
  const [currentlyProcessing, setCurrentlyProcessing] = useState(-1)
  const [processedFiles, setProcessedFiles] = useState<any[]>([])
  const [isQueueProcessing, setIsQueueProcessing] = useState(false)
  const [queueStartTime, setQueueStartTime] = useState<Date | null>(null)
  const [completedCount, setCompletedCount] = useState(0)
  const [isPaused, setIsPaused] = useState(false)
  const [isCancelled, setIsCancelled] = useState(false)
  const [failedFiles, setFailedFiles] = useState<{file: File, error: any, retryCount: number}[]>([])
  
  const { mutate: processPdf, isPending, error, reset } = useProcessPdf()
  
  const {
    handleSubmit,
    watch,
    setValue,
    formState: { isValid },
  } = useForm<ProcessingFormData>({
    resolver: zodResolver(processingSchema),
    defaultValues: {
      rotationAngle: 180,
      order: 'Rev',
    },
  })

  const watchedValues = watch()

  const handleFileSelect = (files: File[]) => {
    setSelectedFiles(files)
    setUploadProgress(0)
    setCurrentStage('Ready')
    setChunkInfo(null)
  }

  const handleFileRemove = (index: number) => {
    setSelectedFiles(files => files.filter((_, i) => i !== index))
  }

  const handleFileRemoveAll = () => {
    setSelectedFiles([])
    setUploadProgress(0)
    setCurrentStage('Ready')
    setChunkInfo(null)
    setCurrentlyProcessing(-1)
    setProcessedFiles([])
  }

  const handlePauseQueue = () => {
    setIsPaused(true)
    setCurrentStage('Queue paused - click Resume to continue')
    toast.info('Queue paused after current file')
  }

  const handleResumeQueue = () => {
    setIsPaused(false)
    setCurrentStage('Resuming queue...')
    toast.info('Queue resumed')
  }

  const handleCancelQueue = () => {
    setIsCancelled(true)
    setCurrentStage('Cancelling queue...')
    toast.error('Queue cancelled - will stop after current file')
  }

  const handleRetryFailed = async () => {
    if (failedFiles.length === 0) return
    
    toast.info(`Retrying ${failedFiles.length} failed files...`)
    const filesToRetry = [...failedFiles]
    setFailedFiles([])
    
    // Add failed files back to queue for retry
    const retryData = {
      rotationAngle: watchedValues.rotationAngle,
      order: watchedValues.order,
    }
    
    for (const {file, retryCount} of filesToRetry) {
      if (retryCount < 3) { // Max 3 retry attempts
        try {
          const request: PdfProcessingRequest = {
            pdfFile: file,
            rotationAngle: retryData.rotationAngle,
            order: retryData.order,
          }
          
          const response = await new Promise((resolve, reject) => {
            processPdf(
              { request, onUploadProgress: handleUploadProgress },
              { onSuccess: resolve, onError: reject }
            )
          })
          
          toast.success(`✅ Retry successful: ${file.name}`)
        } catch (err) {
          console.error(`Retry failed for ${file.name}:`, err)
          setFailedFiles(prev => [...prev, {file, error: err, retryCount: retryCount + 1}])
          toast.error(`Retry failed: ${file.name}`)
        }
      } else {
        toast.error(`Max retries exceeded for: ${file.name}`)
      }
    }
  }

  const handleUploadProgress = (progress: UploadProgress | ChunkProgress) => {
    setUploadProgress(progress.percentage ?? 0)

    if ('currentChunk' in progress) {
      const chunkProgress = progress as ChunkProgress
      setChunkInfo({
        current: chunkProgress.currentChunk,
        total: chunkProgress.totalChunks,
      })

      if (chunkProgress.stageLabel) {
        setCurrentStage(chunkProgress.stageLabel)
      } else {
        switch (chunkProgress.stage) {
          case 'uploading':
            setCurrentStage(`Uploading chunk ${chunkProgress.currentChunk}/${chunkProgress.totalChunks}`)
            break
          case 'processing':
            setCurrentStage('Processing PDF...')
            break
          case 'completing':
            setCurrentStage('Finalizing upload...')
            break
          default:
            setCurrentStage('Processing...')
        }
      }
    } else {
      setChunkInfo(null)

      if (progress.stageLabel) {
        setCurrentStage(progress.stageLabel)
      } else if (progress.stage === 'completed') {
        setCurrentStage('Processing complete')
      } else if (progress.stage === 'failed') {
        setCurrentStage(progress.message ?? 'Processing failed')
      } else if (progress.stage === 'processing') {
        setCurrentStage('Processing...')
      } else if ((progress.percentage ?? 0) < 100) {
        setCurrentStage('Uploading...')
      } else {
        setCurrentStage('Processing...')
      }
    }

    if (progress.message) {
      console.debug('Progress update:', progress.message)
    }
  }

  const processQueue = async (data: ProcessingFormData) => {
    if (selectedFiles.length === 0) return

    setIsQueueProcessing(true)
    setQueueStartTime(new Date())
    setCompletedCount(0)
    setIsPaused(false)
    setIsCancelled(false)
    const results: any[] = []

    try {
      for (let i = 0; i < selectedFiles.length; i++) {
        // Check for cancellation
        if (isCancelled) {
          toast.info(`Queue cancelled after ${i} files`)
          break
        }

        // Handle pause - wait until resumed
        while (isPaused && !isCancelled) {
          await new Promise(resolve => setTimeout(resolve, 100))
        }

        if (isCancelled) break
        const file = selectedFiles[i]
        setCurrentlyProcessing(i)
        setUploadProgress(0)
        
        const queueProgress = Math.round(((i) / selectedFiles.length) * 100)
        setCurrentStage(`Processing file ${i + 1} of ${selectedFiles.length} (${queueProgress}% complete)`)

        const request: PdfProcessingRequest = {
          pdfFile: file,
          rotationAngle: data.rotationAngle,
          order: data.order,
        }

        try {
          console.log(`🚀 Starting processing for file: ${file.name}`)
          const response = await new Promise((resolve, reject) => {
            processPdf(
              { request, onUploadProgress: handleUploadProgress },
              {
                onSuccess: (data) => {
                  console.log(`✅ TanStack Query onSuccess for ${file.name}:`, data)
                  resolve(data)
                },
                onError: (error) => {
                  console.error(`❌ TanStack Query onError for ${file.name}:`, error)
                  reject(error)
                },
              }
            )
          })

          console.log(`🎉 Processing completed successfully for ${file.name}:`, response)
          results.push(response)
          setCompletedCount(prev => prev + 1)
          toast.success(`${file.name} processed successfully!`)
        } catch (err) {
          console.error(`💥 Error processing ${file.name}:`, err)
          toast.error(`Failed to process ${file.name}`)
          results.push({ error: err, fileName: file.name })
          setFailedFiles(prev => [...prev, {file, error: err, retryCount: 0}])
          setCompletedCount(prev => prev + 1) // Count failed files too
        }

        // Reset mutation state and add small delay between files
        reset()
        if (i < selectedFiles.length - 1) {
          await new Promise(resolve => setTimeout(resolve, 1000))
        }
      }

      setProcessedFiles(results)
      onProcessingComplete?.(results)
      
      // Calculate total time and show completion summary
      const totalTime = queueStartTime ? Date.now() - queueStartTime.getTime() : 0
      const totalTimeFormatted = new Date(totalTime).toISOString().substr(11, 8)
      const successCount = results.filter(r => r.success).length
      const failedCount = results.length - successCount
      
      if (failedCount === 0) {
        toast.success(`🎉 All ${selectedFiles.length} files processed successfully in ${totalTimeFormatted}!`)
      } else {
        toast.success(`Queue completed: ${successCount} succeeded, ${failedCount} failed (${totalTimeFormatted})`)
      }
      
      // Reset form
      setSelectedFiles([])
      setUploadProgress(0)
      setCurrentStage('Ready')
      setChunkInfo(null)
      setCurrentlyProcessing(-1)
      setCompletedCount(0)
      setQueueStartTime(null)
    } catch (err) {
      toast.error('Failed to process files')
    } finally {
      setIsQueueProcessing(false)
    }
  }

  const onSubmit = async (data: ProcessingFormData) => {
    if (selectedFiles.length === 0) {
      toast.error('Please select PDF files')
      return
    }

    await processQueue(data)
  }

  const rotationOptions = [
    { value: '0', label: '0° (Normal)' },
    { value: '180', label: '180° (Upside Down)' },
  ]

  const orderOptions = [
    { value: 'Norm', label: 'Normal Order' },
    { value: 'Rev', label: 'Reverse Order' },
  ]

  const isProcessing = isPending || uploadProgress > 0

  return (
    <Card className="w-full max-w-2xl mx-auto">
      <CardHeader>
        <div className="flex items-center space-x-2">
          <Settings className="w-5 h-5 text-primary-600" />
          <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-100">
            PDF Processing
          </h2>
        </div>
        <p className="text-sm text-slate-600 dark:text-slate-400">
          Upload your PDF and configure processing options
        </p>
      </CardHeader>

      <CardContent className="space-y-6">
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-6">
          {/* File Upload */}
          <div>
            <h3 className="text-sm font-medium text-slate-900 dark:text-slate-100 mb-3">
              Select PDF File
            </h3>
            <FileUpload
              onFileSelect={handleFileSelect}
              onFileRemove={handleFileRemove}
              selectedFiles={selectedFiles}
              uploading={isProcessing}
              uploadProgress={uploadProgress}
              disabled={disabled || isProcessing}
              error={error?.message}
              currentlyProcessing={currentlyProcessing}
            />
          </div>

          {/* Processing Options */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div>
              <div className="flex items-center space-x-2 mb-2">
                <RotateCcw className="w-4 h-4 text-slate-600" />
                <label className="text-sm font-medium text-slate-900 dark:text-slate-100">
                  Rotation Angle
                </label>
              </div>
              <Select
                value={watchedValues.rotationAngle.toString()}
                onChange={(value: string) => setValue('rotationAngle', parseInt(value))}
                options={rotationOptions}
                disabled={disabled || isProcessing}
              />
            </div>

            <div>
              <div className="flex items-center space-x-2 mb-2">
                <ArrowUpDown className="w-4 h-4 text-slate-600" />
                <label className="text-sm font-medium text-slate-900 dark:text-slate-100">
                  Page Order
                </label>
              </div>
              <Select
                value={watchedValues.order}
                onChange={(value: string) => setValue('order', value as 'Norm' | 'Rev')}
                options={orderOptions}
                disabled={disabled || isProcessing}
              />
            </div>
          </div>

          {/* Preview Settings */}
          <div className="p-4 bg-slate-50 dark:bg-slate-800 rounded-lg">
            <h4 className="text-sm font-medium text-slate-900 dark:text-slate-100 mb-2">
              Processing Preview
            </h4>
            <div className="text-sm text-slate-600 dark:text-slate-400 space-y-1">
              <p>• Pages will be rotated by {watchedValues.rotationAngle}°</p>
              <p>
                • Page order will be{' '}
                {watchedValues.order === 'Rev' ? 'reversed' : 'maintained'}
              </p>
              <p>• Output will be custom-sized sheets (317mm × variable height)</p>
            </div>
          </div>

          {/* Enhanced Progress Display for Large Files */}
          {isProcessing && (
            <div className="space-y-3 p-4 bg-blue-50 dark:bg-blue-900/20 rounded-lg border border-blue-200 dark:border-blue-800">
              <div className="flex items-center justify-between">
                <span className="text-sm font-medium text-blue-900 dark:text-blue-100">
                  {currentStage}
                </span>
                <div className="flex items-center space-x-2">
                  {/* Queue Control Buttons for Multiple Files */}
                  {selectedFiles.length > 1 && isQueueProcessing && (
                    <div className="flex space-x-2">
                      {!isPaused ? (
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          onClick={handlePauseQueue}
                          className="text-xs"
                        >
                          Pause
                        </Button>
                      ) : (
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          onClick={handleResumeQueue}
                          className="text-xs"
                        >
                          Resume
                        </Button>
                      )}
                      <Button
                        type="button"
                        variant="destructive"
                        size="sm"
                        onClick={handleCancelQueue}
                        className="text-xs"
                      >
                        Cancel
                      </Button>
                    </div>
                  )}
                  <span className="text-sm text-blue-700 dark:text-blue-300">
                    {uploadProgress}%
                  </span>
                </div>
              </div>
              
              {/* Queue Progress for Multiple Files */}
              {selectedFiles.length > 1 && isQueueProcessing && (
                <div>
                  <div className="flex items-center justify-between text-xs text-blue-600 dark:text-blue-400 mb-1">
                    <span>Overall Progress</span>
                    <span>{completedCount} of {selectedFiles.length} files completed</span>
                  </div>
                  <Progress
                    value={Math.round((completedCount / selectedFiles.length) * 100)}
                    variant="secondary"
                    size="sm"
                    className="w-full"
                  />
                </div>
              )}
              
              {/* Current File Progress */}
              <div>
                <div className="flex items-center justify-between text-xs text-blue-600 dark:text-blue-400 mb-1">
                  <span>Current File</span>
                  <span>{currentlyProcessing >= 0 ? selectedFiles[currentlyProcessing]?.name : ''}</span>
                </div>
                <Progress
                  value={uploadProgress}
                  variant="primary"
                  size="md"
                  className="w-full"
                />
              </div>
              
              {chunkInfo && (
                <div className="flex items-center justify-between text-xs text-blue-600 dark:text-blue-400">
                  <span>Chunk {chunkInfo.current} of {chunkInfo.total}</span>
                  <span>Chunked Upload Active</span>
                </div>
              )}
            </div>
          )}

          {/* Submit Button */}
          <Button
            type="submit"
            className="w-full"
            disabled={selectedFiles.length === 0 || !isValid || isProcessing || disabled}
            loading={isProcessing}
          >
            {isProcessing ? (
              <>
                <LoadingSpinner size="sm" />
                {currentStage}
              </>
            ) : (
              selectedFiles.length > 1 ? `Process ${selectedFiles.length} Files` : 'Process PDF'
            )}
          </Button>
          
          {/* Retry Failed Files Button */}
          {failedFiles.length > 0 && !isProcessing && (
            <Button
              type="button"
              variant="destructive"
              className="w-full mt-2"
              onClick={handleRetryFailed}
            >
              Retry {failedFiles.length} Failed Files
            </Button>
          )}
        </form>
      </CardContent>
    </Card>
  )
}