import type {
  PdfProcessingRequest,
  PdfProcessingResponse,
  HealthResponse,
  UploadProgress,
  ChunkProgress,
  ProcessingProgressEvent,
  JobStatusResponse,
  StartProcessingResponse,
} from '../types'

export class ApiError extends Error {
  public status?: number
  public response?: Response
  
  constructor(
    message: string,
    status?: number,
    response?: Response
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.response = response
  }
}

const getApiBaseUrl = (): string => {
  if (import.meta.env.VITE_API_URL) {
    return import.meta.env.VITE_API_URL
  }

  if (typeof window !== 'undefined') {
    return `http://${window.location.hostname}:5002`
  }

  return 'http://localhost:5002'
}

interface PersistedJob {
  jobId: string
  status: 'processing' | 'completed'
  updatedAt: number
}

type ProgressHandler = (progress: UploadProgress | ChunkProgress) => void

type ErrorResponseBody = { message?: string; title?: string }

const ONE_HOUR_MS = 60 * 60 * 1000

export class ApiClient {
  private baseUrl: string
  private activeJobPromises = new Map<string, Promise<PdfProcessingResponse>>()
  private memoryJobStore = new Map<string, PersistedJob>()

  constructor(baseUrl: string = getApiBaseUrl()) {
    this.baseUrl = baseUrl
    console.log('🌐 API Client initialized with base URL:', baseUrl)
  }

  private async handleResponse<T>(response: Response): Promise<T> {
    if (!response.ok) {
      let errorMessage = `HTTP error! status: ${response.status}`

      try {
        const errorData = await response.json() as ErrorResponseBody
        errorMessage = errorData.message || errorData.title || errorMessage
      } catch {
        errorMessage = response.statusText || errorMessage
      }

      throw new ApiError(errorMessage, response.status, response)
    }

    const contentType = response.headers.get('content-type')
    if (contentType && contentType.includes('application/json')) {
      return await response.json() as T
    }

    return response.text() as unknown as T
  }

  async get<T>(endpoint: string): Promise<T> {
    const response = await fetch(`${this.baseUrl}${endpoint}`, {
      method: 'GET',
      headers: {
        Accept: 'application/json',
      },
    })

    return this.handleResponse<T>(response)
  }

  async post<T>(endpoint: string, data?: unknown): Promise<T> {
    const response = await fetch(`${this.baseUrl}${endpoint}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Accept: 'application/json',
      },
      body: data ? JSON.stringify(data) : undefined,
    })

    return this.handleResponse<T>(response)
  }

  async postFormData<T>(
    endpoint: string,
    formData: FormData,
    onUploadProgress?: ProgressHandler
  ): Promise<T> {
    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest()

      xhr.upload.addEventListener('progress', (event) => {
        if (event.lengthComputable && onUploadProgress) {
          onUploadProgress({
            loaded: event.loaded,
            total: event.total,
            percentage: Math.round((event.loaded / event.total) * 100),
            stage: 'uploading',
            stageLabel: 'Uploading file',
          } as ChunkProgress)
        }
      })

      xhr.addEventListener('load', () => {
        if (xhr.status >= 200 && xhr.status < 300) {
          try {
            const result = JSON.parse(xhr.responseText) as T
            resolve(result)
          } catch {
            reject(new ApiError('Failed to parse response JSON'))
          }
        } else {
          let errorMessage = `HTTP error! status: ${xhr.status}`
          try {
            const errorData = JSON.parse(xhr.responseText) as ErrorResponseBody
            errorMessage = errorData.message || errorData.title || errorMessage
          } catch {
            errorMessage = xhr.statusText || errorMessage
          }
          reject(new ApiError(errorMessage, xhr.status))
        }
      })

      xhr.addEventListener('error', () => {
        reject(new ApiError('Network error occurred'))
      })

      xhr.addEventListener('timeout', () => {
        reject(new ApiError('Upload timed out'))
      })

      xhr.timeout = 300000
      xhr.open('POST', `${this.baseUrl}${endpoint}`)
      xhr.send(formData)
    })
  }

  async getHealth(): Promise<HealthResponse> {
    return this.get<HealthResponse>('/api/pdf/health')
  }

  async processPdf(
    request: PdfProcessingRequest,
    onUploadProgress?: ProgressHandler
  ): Promise<PdfProcessingResponse> {
    const fingerprint = this.computeFingerprint(request)

    if (this.activeJobPromises.has(fingerprint)) {
      return this.activeJobPromises.get(fingerprint)!
    }

    const jobPromise = this.executeProcessPdf(request, fingerprint, onUploadProgress)
      .finally(() => {
        this.activeJobPromises.delete(fingerprint)
      })

    this.activeJobPromises.set(fingerprint, jobPromise)
    return jobPromise
  }

  private async executeProcessPdf(
    request: PdfProcessingRequest,
    fingerprint: string,
    onUploadProgress?: ProgressHandler
  ): Promise<PdfProcessingResponse> {
    const resumed = await this.tryResumeJob(fingerprint, request, onUploadProgress)
    if (resumed) {
      return resumed
    }

    const formData = new FormData()
    formData.append('pdfFile', request.pdfFile)
    formData.append('rotationAngle', request.rotationAngle.toString())
    formData.append('order', request.order)

    onUploadProgress?.({
      loaded: 0,
      total: request.pdfFile.size,
      percentage: 0,
      stage: 'uploading',
      stageLabel: 'Preparing upload...',
    } as ChunkProgress)

    const startResponse = await this.startProgressUpload(request, formData, onUploadProgress)
    const jobId = startResponse.jobId

    if (!jobId) {
      throw new ApiError('Progress endpoint did not return a jobId')
    }

    if (startResponse.result) {
      this.setPersistedJob(fingerprint, {
        jobId,
        status: 'completed',
        updatedAt: Date.now(),
      })

      onUploadProgress?.({
        loaded: 100,
        total: 100,
        percentage: 100,
        stage: 'completed',
        stageLabel: 'Processing complete',
      })

      return startResponse.result
    }

    this.setPersistedJob(fingerprint, {
      jobId,
      status: 'processing',
      updatedAt: Date.now(),
    })

    if (startResponse.duplicateOf) {
      onUploadProgress?.({
        loaded: 0,
        total: request.pdfFile.size,
        percentage: 0,
        stage: 'processing',
        stageLabel: 'Reattaching to existing job...',
      })
    } else {
      onUploadProgress?.({
        loaded: request.pdfFile.size,
        total: request.pdfFile.size,
        percentage: 10,
        stage: 'processing',
        stageLabel: 'Processing started...',
      })
    }

    try {
      const result = await this.waitForJobCompletion(jobId, {
        onProgress: onUploadProgress,
      })

      this.setPersistedJob(fingerprint, {
        jobId,
        status: 'completed',
        updatedAt: Date.now(),
      })

      onUploadProgress?.({
        loaded: 100,
        total: 100,
        percentage: 100,
        stage: 'completed',
        stageLabel: 'Processing complete',
      })

      return result
    } catch (error) {
      this.clearPersistedJob(fingerprint)
      throw error
    }
  }

  private async startProgressUpload(
    request: PdfProcessingRequest,
    formData: FormData,
    onUploadProgress?: ProgressHandler
  ): Promise<StartProcessingResponse> {
    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest()

      xhr.upload.addEventListener('progress', (event) => {
        if (event.lengthComputable && onUploadProgress) {
          onUploadProgress({
            loaded: event.loaded,
            total: event.total,
            percentage: Math.round((event.loaded / event.total) * 100),
            currentChunk: 1,
            totalChunks: 1,
            stage: 'uploading',
            stageLabel: `Uploading ${request.pdfFile.name}`,
          } as ChunkProgress)
        }
      })

      xhr.addEventListener('load', () => {
        if (xhr.status >= 200 && xhr.status < 300) {
          try {
            const result = JSON.parse(xhr.responseText) as StartProcessingResponse
            resolve(result)
          } catch {
            reject(new ApiError('Failed to parse response JSON'))
          }
        } else {
          let errorMessage = `HTTP error! status: ${xhr.status}`
          try {
            const errorData = JSON.parse(xhr.responseText) as ErrorResponseBody
            errorMessage = errorData.message || errorData.title || errorMessage
          } catch {
            errorMessage = xhr.statusText || errorMessage
          }
          reject(new ApiError(errorMessage, xhr.status))
        }
      })

      xhr.addEventListener('error', () => {
        reject(new ApiError('Network error occurred'))
      })

      xhr.addEventListener('timeout', () => {
        reject(new ApiError('Upload timed out'))
      })

      xhr.timeout = 300000
      xhr.open('POST', `${this.baseUrl}/api/pdf/process-with-progress`)
      xhr.send(formData)
    })
  }

  private async waitForJobCompletion(
    jobId: string,
    options: { onProgress?: ProgressHandler; signal?: AbortSignal } = {}
  ): Promise<PdfProcessingResponse> {
    const { onProgress, signal } = options

    return new Promise((resolve, reject) => {
      let finished = false
      let eventSource: EventSource | null = null
      let pollTimer: ReturnType<typeof setInterval> | null = null

      const cleanup = () => {
        if (eventSource) {
          eventSource.close()
          eventSource = null
        }
        if (pollTimer) {
          clearInterval(pollTimer)
          pollTimer = null
        }
      }

      const finalizeSuccess = (result: PdfProcessingResponse) => {
        if (finished) return
        finished = true
        cleanup()
        resolve(result)
      }

      const finalizeError = (error: unknown) => {
        if (finished) return
        finished = true
        cleanup()
        reject(error)
      }

      const handleStatus = async () => {
        try {
          const status = await this.getJobStatus(jobId)

          if (!status.success) {
            finalizeError(new ApiError(status.error ?? 'Processing failed'))
            return
          }

          if (status.progress) {
            onProgress?.(this.toUploadProgress(status.progress))
          }

          if (status.stage === 'Completed' && status.result) {
            finalizeSuccess(status.result)
            return
          }

          if (status.stage === 'Failed') {
            finalizeError(new ApiError(status.error ?? 'Processing failed'))
          }
        } catch (error) {
          finalizeError(error)
        }
      }

      const startPolling = () => {
        if (pollTimer) return
        pollTimer = setInterval(() => {
          void handleStatus()
        }, 3000)
        void handleStatus()
      }

      if (typeof EventSource !== 'undefined') {
        try {
          eventSource = new EventSource(`${this.baseUrl}/api/pdf/progress/${jobId}`)

          eventSource.onmessage = async (event) => {
            try {
              const payload = JSON.parse(event.data) as ProcessingProgressEvent
              onProgress?.(this.toUploadProgress(payload))

              if (payload.stage === 'Completed') {
                const status = await this.getJobStatus(jobId)
                if (status.result) {
                  finalizeSuccess(status.result)
                } else {
                  finalizeError(new ApiError('Processing completed without result'))
                }
              } else if (payload.stage === 'Failed') {
                const status = await this.getJobStatus(jobId)
                finalizeError(new ApiError(status.error ?? 'Processing failed'))
              }
            } catch {
              startPolling()
            }
          }

          eventSource.onerror = () => {
            startPolling()
            if (eventSource) {
              eventSource.close()
              eventSource = null
            }
          }
        } catch {
          startPolling()
        }
      } else {
        startPolling()
      }

      if (signal) {
        if (signal.aborted) {
          finalizeError(new DOMException('Aborted', 'AbortError'))
          return
        }
        signal.addEventListener('abort', () => {
          finalizeError(new DOMException('Aborted', 'AbortError'))
        })
      }
    })
  }

  private async tryResumeJob(
    fingerprint: string,
    request: PdfProcessingRequest,
    onUploadProgress?: ProgressHandler
  ): Promise<PdfProcessingResponse | null> {
    const persisted = this.getPersistedJob(fingerprint)
    if (!persisted) {
      return null
    }

    onUploadProgress?.({
      loaded: 0,
      total: request.pdfFile.size,
      percentage: 0,
      stage: 'processing',
      stageLabel: 'Reattaching to existing job...',
    })

    try {
      const status = await this.getJobStatus(persisted.jobId)

      if (!status.success) {
        this.clearPersistedJob(fingerprint)
        throw new ApiError(status.error ?? 'Processing failed')
      }

      if (status.stage === 'Completed' && status.result) {
        this.setPersistedJob(fingerprint, {
          jobId: persisted.jobId,
          status: 'completed',
          updatedAt: Date.now(),
        })

        onUploadProgress?.({
          loaded: 100,
          total: 100,
          percentage: 100,
          stage: 'completed',
          stageLabel: 'Processing complete',
        })

        return status.result
      }

      if (status.stage === 'Failed') {
        this.clearPersistedJob(fingerprint)
        throw new ApiError(status.error ?? 'Processing failed')
      }

      const result = await this.waitForJobCompletion(persisted.jobId, {
        onProgress: onUploadProgress,
      })

      this.setPersistedJob(fingerprint, {
        jobId: persisted.jobId,
        status: 'completed',
        updatedAt: Date.now(),
      })

      return result
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        this.clearPersistedJob(fingerprint)
        return null
      }

      throw error
    }
  }

  private async getJobStatus(jobId: string): Promise<JobStatusResponse> {
    const response = await fetch(`${this.baseUrl}/api/pdf/status/${jobId}`, {
      method: 'GET',
      headers: {
        Accept: 'application/json',
      },
    })

    if (response.status === 404) {
      throw new ApiError('Job not found', 404, response)
    }

    return this.handleResponse<JobStatusResponse>(response)
  }

  private computeFingerprint(request: PdfProcessingRequest): string {
    const parts = [
      request.pdfFile.name ?? 'unknown',
      request.pdfFile.size.toString(),
      (request.pdfFile.lastModified ?? 0).toString(),
      request.rotationAngle.toString(),
      request.order,
    ]

    return parts.join('|')
  }

  private storageKey(fingerprint: string): string {
    return `sheetbuilder:job:${fingerprint}`
  }

  private getPersistedJob(fingerprint: string): PersistedJob | null {
    const key = this.storageKey(fingerprint)

    if (typeof window !== 'undefined' && window.localStorage) {
      const raw = window.localStorage.getItem(key)
      if (!raw) return null

      try {
        const parsed = JSON.parse(raw) as PersistedJob
        if (!parsed?.jobId) {
          this.clearPersistedJob(fingerprint)
          return null
        }

        if (Date.now() - parsed.updatedAt > ONE_HOUR_MS) {
          this.clearPersistedJob(fingerprint)
          return null
        }

        return parsed
      } catch {
        this.clearPersistedJob(fingerprint)
        return null
      }
    }

    const value = this.memoryJobStore.get(key)
    if (!value) return null

    if (Date.now() - value.updatedAt > ONE_HOUR_MS) {
      this.memoryJobStore.delete(key)
      return null
    }

    return value
  }

  private setPersistedJob(fingerprint: string, job: PersistedJob): void {
    const key = this.storageKey(fingerprint)

    if (typeof window !== 'undefined' && window.localStorage) {
      window.localStorage.setItem(key, JSON.stringify(job))
      return
    }

    this.memoryJobStore.set(key, job)
  }

  private clearPersistedJob(fingerprint: string): void {
    const key = this.storageKey(fingerprint)

    if (typeof window !== 'undefined' && window.localStorage) {
      window.localStorage.removeItem(key)
      return
    }

    this.memoryJobStore.delete(key)
  }

  private toUploadProgress(progress: ProcessingProgressEvent): UploadProgress {
    const percentage = Math.max(0, Math.min(100, Math.round(progress.percentageComplete)))
    const stage = this.mapStage(progress.stage)
    const stageLabel = this.describeStage(progress)

    return {
      loaded: percentage,
      total: 100,
      percentage,
      stage,
      stageLabel,
      message: progress.currentOperation,
    }
  }

  private mapStage(stage: string): string {
    switch (stage) {
      case 'Initializing':
      case 'PreparingDimensions':
        return 'initializing'
      case 'ProcessingPages':
      case 'OptimizingOutput':
        return 'processing'
      case 'Finalizing':
        return 'finalizing'
      case 'Completed':
        return 'completed'
      case 'Failed':
        return 'failed'
      default:
        return stage.toLowerCase()
    }
  }

  private describeStage(progress: ProcessingProgressEvent): string {
    const { stage, currentPage, totalPages, currentOperation } = progress

    switch (stage) {
      case 'Initializing':
        return 'Initializing processing...'
      case 'PreparingDimensions':
        return 'Analyzing document dimensions...'
      case 'ProcessingPages':
        if (totalPages > 0) {
          return `Processing pages ${Math.min(currentPage, totalPages)}/${totalPages}`
        }
        return 'Processing pages...'
      case 'OptimizingOutput':
        return 'Optimizing output...'
      case 'Finalizing':
        return 'Finalizing output...'
      case 'Completed':
        return 'Processing complete'
      case 'Failed':
        return currentOperation || 'Processing failed'
      default:
        return currentOperation || stage
    }
  }

  getDownloadUrl(path: string): string {
    return `${this.baseUrl}${path}`
  }

  async downloadFile(downloadUrl: string, fileName: string): Promise<void> {
    try {
      // Use native browser download/streaming to avoid loading large files into memory
      const url = this.getDownloadUrl(downloadUrl)
      const link = document.createElement('a')
      link.href = url
      // Let server-sent Content-Disposition set filename; provide a hint as fallback
      link.download = fileName
      link.rel = 'noopener'
      document.body.appendChild(link)
      link.click()
      document.body.removeChild(link)
    } catch (error) {
      throw new ApiError(
        `Failed to download file: ${error instanceof Error ? error.message : 'Unknown error'}`
      )
    }
  }
}

export const apiClient = new ApiClient()
