import type { UploadProgress } from '../types'

type UploadResponseBody = Record<string, unknown>
type UploadErrorBody = { message?: string; title?: string }

export interface ChunkUploadOptions {
  chunkSize?: number
  maxRetries?: number
  timeout?: number
}

export interface ChunkProgress extends UploadProgress {
  currentChunk: number
  totalChunks: number
  stage: 'uploading' | 'processing' | 'completing'
}

export class ChunkedUploadError extends Error {
  public chunkIndex?: number
  public retriesLeft?: number
  
  constructor(
    message: string,
    chunkIndex?: number,
    retriesLeft?: number
  ) {
    super(message)
    this.name = 'ChunkedUploadError'
    this.chunkIndex = chunkIndex
    this.retriesLeft = retriesLeft
  }
}

export class ChunkedUploader {
  private baseUrl: string
  private chunkSize: number
  private maxRetries: number
  private timeout: number

  constructor(
    baseUrl: string = 'http://localhost:5000',
    options: ChunkUploadOptions = {}
  ) {
    this.baseUrl = baseUrl
    this.chunkSize = options.chunkSize || 5 * 1024 * 1024 // 5MB default chunk size
    this.maxRetries = options.maxRetries || 3
    this.timeout = options.timeout || 30000 // 30 seconds
  }

  async uploadFile(
    file: File,
    _endpoint: string,
    additionalData: Record<string, string> = {},
    _onProgress?: (progress: ChunkProgress) => void
  ): Promise<unknown> {
    // For files smaller than chunk size, use regular upload
    if (file.size <= this.chunkSize) {
      return this.uploadSmallFile(file, _endpoint, additionalData, _onProgress)
    }

    return this.uploadLargeFile(file, _endpoint, additionalData, _onProgress)
  }

  private async uploadSmallFile(
    file: File,
    endpoint: string,
    additionalData: Record<string, string>,
    _onProgress?: (progress: ChunkProgress) => void
  ): Promise<unknown> {
    const formData = new FormData()
    formData.append('pdfFile', file)
    
    Object.entries(additionalData).forEach(([key, value]) => {
      formData.append(key, value)
    })

    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest()

      xhr.upload.addEventListener('progress', (event) => {
        if (event.lengthComputable && _onProgress) {
          _onProgress({
            loaded: event.loaded,
            total: event.total,
            percentage: Math.round((event.loaded / event.total) * 100),
            currentChunk: 1,
            totalChunks: 1,
            stage: 'uploading'
          })
        }
      })

      xhr.addEventListener('load', () => {
        if (xhr.status >= 200 && xhr.status < 300) {
          try {
            const result = JSON.parse(xhr.responseText) as UploadResponseBody
            resolve(result)
          } catch {
            reject(new ChunkedUploadError('Failed to parse response JSON'))
          }
        } else {
          let errorMessage = `HTTP error! status: ${xhr.status}`
          try {
            const errorData = JSON.parse(xhr.responseText) as UploadErrorBody
            errorMessage = errorData.message || errorData.title || errorMessage
          } catch {
            errorMessage = xhr.statusText || errorMessage
          }
          reject(new ChunkedUploadError(errorMessage))
        }
      })

      xhr.addEventListener('error', () => {
        reject(new ChunkedUploadError('Network error occurred'))
      })

      xhr.addEventListener('timeout', () => {
        reject(new ChunkedUploadError('Upload timed out'))
      })

      xhr.timeout = this.timeout
      xhr.open('POST', `${this.baseUrl}${endpoint}`)
      xhr.send(formData)
    })
  }

  private async uploadLargeFile(
    file: File,
    _endpoint: string,
    additionalData: Record<string, string>,
    __onProgress?: (progress: ChunkProgress) => void
  ): Promise<unknown> {
    const totalChunks = Math.ceil(file.size / this.chunkSize)
    const uploadId = this.generateUploadId()
    
    __onProgress?.({
      loaded: 0,
      total: file.size,
      percentage: 0,
      currentChunk: 0,
      totalChunks,
      stage: 'uploading'
    })

    // Upload chunks sequentially for better reliability
    for (let chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++) {
      const start = chunkIndex * this.chunkSize
      const end = Math.min(start + this.chunkSize, file.size)
      const chunk = file.slice(start, end)

      await this.uploadChunkWithRetry(
        chunk,
        chunkIndex,
        totalChunks,
        uploadId,
        file.name,
        additionalData
      )

      // Update progress after each successful chunk
      const loaded = end
      const percentage = Math.round((loaded / file.size) * 100)
      
      __onProgress?.({
        loaded,
        total: file.size,
        percentage,
        currentChunk: chunkIndex + 1,
        totalChunks,
        stage: chunkIndex === totalChunks - 1 ? 'processing' : 'uploading'
      })
    }

    // Complete the upload
    __onProgress?.({
      loaded: file.size,
      total: file.size,
      percentage: 100,
      currentChunk: totalChunks,
      totalChunks,
      stage: 'completing'
    })

    return this.completeUpload(uploadId, file.name, additionalData)
  }

  private async uploadChunkWithRetry(
    chunk: Blob,
    chunkIndex: number,
    totalChunks: number,
    uploadId: string,
    fileName: string,
    additionalData: Record<string, string>
  ): Promise<void> {
    let retries = this.maxRetries

    while (retries > 0) {
      try {
        await this.uploadChunk(chunk, chunkIndex, totalChunks, uploadId, fileName, additionalData)
        return // Success, exit retry loop
      } catch (error) {
        retries--
        
        if (retries === 0) {
          throw new ChunkedUploadError(
            `Failed to upload chunk ${chunkIndex} after ${this.maxRetries} attempts: ${error}`,
            chunkIndex,
            0
          )
        }

        // Wait before retry (exponential backoff)
        const delay = Math.pow(2, this.maxRetries - retries) * 1000
        await this.delay(delay)
        
        console.warn(`Retrying chunk ${chunkIndex}, ${retries} attempts left`)
      }
    }
  }

  private async uploadChunk(
    chunk: Blob,
    chunkIndex: number,
    totalChunks: number,
    uploadId: string,
    fileName: string,
    additionalData: Record<string, string>
  ): Promise<void> {
    const formData = new FormData()
    formData.append('chunk', chunk)
    formData.append('chunkIndex', chunkIndex.toString())
    formData.append('totalChunks', totalChunks.toString())
    formData.append('uploadId', uploadId)
    formData.append('fileName', fileName)
    
    Object.entries(additionalData).forEach(([key, value]) => {
      formData.append(key, value)
    })

    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest()

      xhr.addEventListener('load', () => {
        if (xhr.status >= 200 && xhr.status < 300) {
          resolve()
        } else {
          let errorMessage = `HTTP error! status: ${xhr.status}`
          try {
            const errorData = JSON.parse(xhr.responseText) as UploadErrorBody
            errorMessage = errorData.message || errorMessage
          } catch {
            errorMessage = xhr.statusText || errorMessage
          }
          reject(new Error(errorMessage))
        }
      })

      xhr.addEventListener('error', () => {
        reject(new Error('Network error occurred'))
      })

      xhr.addEventListener('timeout', () => {
        reject(new Error('Chunk upload timed out'))
      })

      xhr.timeout = this.timeout
      xhr.open('POST', `${this.baseUrl}/api/upload/chunk`)
      xhr.send(formData)
    })
  }

  private async completeUpload(
    uploadId: string,
    fileName: string,
    additionalData: Record<string, string>
  ): Promise<UploadResponseBody> {
    const formData = new FormData()
    formData.append('uploadId', uploadId)
    formData.append('fileName', fileName)
    
    Object.entries(additionalData).forEach(([key, value]) => {
      formData.append(key, value)
    })

    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest()

      xhr.addEventListener('load', () => {
        if (xhr.status >= 200 && xhr.status < 300) {
          try {
            const result = JSON.parse(xhr.responseText) as UploadResponseBody
            resolve(result)
          } catch {
            reject(new ChunkedUploadError('Failed to parse final response JSON'))
          }
        } else {
          let errorMessage = `HTTP error! status: ${xhr.status}`
          try {
            const errorData = JSON.parse(xhr.responseText) as UploadErrorBody
            errorMessage = errorData.message || errorMessage
          } catch {
            errorMessage = xhr.statusText || errorMessage
          }
          reject(new ChunkedUploadError(errorMessage))
        }
      })

      xhr.addEventListener('error', () => {
        reject(new ChunkedUploadError('Network error during completion'))
      })

      xhr.addEventListener('timeout', () => {
        reject(new ChunkedUploadError('Upload completion timed out'))
      })

      xhr.timeout = this.timeout
      xhr.open('POST', `${this.baseUrl}/api/upload/complete`)
      xhr.send(formData)
    })
  }

  private generateUploadId(): string {
    return `upload_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`
  }

  private delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms))
  }
}
