export interface PdfProcessingRequest {
  pdfFile: File
  rotationAngle: number
  order: 'Norm' | 'Rev'
}

export interface PdfProcessingResponse {
  success: boolean
  message?: string
  outputFileName?: string
  downloadUrl?: string
  processingTime: string
  inputPages: number
  outputPages: number
}

export interface HealthResponse {
  status: string
  timestamp: string
  service: string
}

export interface ProcessingJob {
  id: string
  fileName: string
  status: 'pending' | 'processing' | 'completed' | 'failed'
  progress: number
  startTime: Date
  endTime?: Date
  request: PdfProcessingRequest
  response?: PdfProcessingResponse
  error?: string
}

export interface ProcessingProgressPerformance {
  memoryUsageMB: number
  cacheHitCount: number
  cacheMissCount: number
  cacheHitRatio: number
  xObjectsCached: number
  sheetsGenerated: number
}

export interface ProcessingProgressEvent {
  jobId: string
  stage: string
  currentPage: number
  totalPages: number
  percentageComplete: number
  pagesPerSecond: number
  estimatedTimeRemaining: string
  elapsedTime: string
  currentOperation: string
  performance: ProcessingProgressPerformance
  timestamp: string
}

export interface UploadProgress {
  loaded: number
  total: number
  percentage: number
  /**
   * Optional short label describing the current phase (e.g. "Processing pages 10/200").
   */
  stageLabel?: string
  /**
   * Machine-friendly stage indicator used by UI components.
   */
  stage?: string
  /**
   * Optional message or operation description supplied by the backend.
   */
  message?: string
}

export interface ChunkProgress extends UploadProgress {
  currentChunk: number
  totalChunks: number
  stage: string
}

export interface JobStatusResponse {
  success: boolean
  jobId: string
  stage: string
  startTime: string
  endTime?: string | null
  progress?: ProcessingProgressEvent | null
  result?: PdfProcessingResponse | null
  error?: string | null
}

export interface StartProcessingResponse {
  success: boolean
  jobId: string
  duplicateOf?: boolean
  stage?: string
  result?: PdfProcessingResponse | null
  message?: string
}
