import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { ApiClient, ApiError } from '../services/api-client'
import type { PdfProcessingRequest, PdfProcessingResponse, StartProcessingResponse } from '../types'

describe('ApiClient Upload', () => {
  let apiClient: ApiClient
  let mockXHR: any
  let progressCallback: vi.Mock

  beforeEach(() => {
    apiClient = new ApiClient('http://localhost:5000')
    progressCallback = vi.fn()

    mockXHR = new (global.XMLHttpRequest as any)()
    vi.spyOn(global, 'XMLHttpRequest').mockReturnValue(mockXHR)
  })

  afterEach(() => {
    vi.restoreAllMocks()
    window.localStorage.clear()
  })

  describe('postFormData', () => {
    it('should handle successful uploads', async () => {
      const formData = new FormData()
      const file = new File(['pdf content'], 'test.pdf', { type: 'application/pdf' })
      formData.append('pdfFile', file)

      mockXHR.status = 200
      mockXHR.responseText = JSON.stringify({ success: true, message: 'Success' })

      const uploadPromise = apiClient.postFormData('/api/pdf/process', formData, progressCallback)

      mockXHR._trigger('load')

      const result = await uploadPromise
      expect(result).toEqual({ success: true, message: 'Success' })
    })

    it('should track upload progress', async () => {
      const formData = new FormData()
      const file = new File(['pdf content'], 'test.pdf', { type: 'application/pdf' })
      formData.append('pdfFile', file)

      mockXHR.status = 200
      mockXHR.responseText = JSON.stringify({ success: true })

      const uploadPromise = apiClient.postFormData('/api/pdf/process', formData, progressCallback)

      const progressEvent = { lengthComputable: true, loaded: 25, total: 100 }
      mockXHR.upload._trigger('progress', progressEvent)

      expect(progressCallback).toHaveBeenCalledWith(
        expect.objectContaining({
          loaded: 25,
          total: 100,
          percentage: 25,
          stage: 'uploading',
        })
      )

      mockXHR._trigger('load')
      await uploadPromise
    })

    it('should handle network errors', async () => {
      const formData = new FormData()
      const file = new File(['pdf content'], 'test.pdf', { type: 'application/pdf' })
      formData.append('pdfFile', file)

      const uploadPromise = apiClient.postFormData('/api/pdf/process', formData, progressCallback)

      mockXHR._trigger('error')

      await expect(uploadPromise).rejects.toThrow('Network error occurred')
    })

    it('should handle HTTP errors with JSON response', async () => {
      const formData = new FormData()
      const file = new File(['pdf content'], 'test.pdf', { type: 'application/pdf' })
      formData.append('pdfFile', file)

      mockXHR.status = 400
      mockXHR.responseText = JSON.stringify({ message: 'File too large' })

      const uploadPromise = apiClient.postFormData('/api/pdf/process', formData, progressCallback)

      mockXHR._trigger('load')

      await expect(uploadPromise).rejects.toThrow('File too large')
    })

    it('should handle HTTP errors without JSON response', async () => {
      const formData = new FormData()
      const file = new File(['pdf content'], 'test.pdf', { type: 'application/pdf' })
      formData.append('pdfFile', file)

      mockXHR.status = 500
      mockXHR.statusText = 'Internal Server Error'
      mockXHR.responseText = 'Server Error'

      const uploadPromise = apiClient.postFormData('/api/pdf/process', formData, progressCallback)

      mockXHR._trigger('load')

      await expect(uploadPromise).rejects.toThrow('Internal Server Error')
    })

    it('should handle malformed JSON responses', async () => {
      const formData = new FormData()
      const file = new File(['pdf content'], 'test.pdf', { type: 'application/pdf' })
      formData.append('pdfFile', file)

      mockXHR.status = 200
      mockXHR.responseText = 'invalid json'

      const uploadPromise = apiClient.postFormData('/api/pdf/process', formData, progressCallback)

      mockXHR._trigger('load')

      await expect(uploadPromise).rejects.toThrow('Failed to parse response JSON')
    })
  })

  describe('processPdf', () => {
    const createRequest = (): PdfProcessingRequest => ({
      pdfFile: new File(['pdf content'], 'test.pdf', { type: 'application/pdf', lastModified: 1700000000000 }),
      rotationAngle: 180,
      order: 'Rev',
    })

    it('starts a new progress job and waits for completion', async () => {
      const request = createRequest()
      const expected: PdfProcessingResponse = {
        success: true,
        message: 'Done',
        outputFileName: 'output.pdf',
        downloadUrl: '/uploads/output.pdf',
        processingTime: '00:00:05',
        inputPages: 10,
        outputPages: 2,
      }

      const startSpy = vi
        .spyOn(apiClient as any, 'startProgressUpload')
        .mockResolvedValue({ success: true, jobId: 'job-123' } as StartProcessingResponse)

      const waitSpy = vi
        .spyOn(apiClient as any, 'waitForJobCompletion')
        .mockResolvedValue(expected)

      const result = await apiClient.processPdf(request, progressCallback)

      expect(startSpy).toHaveBeenCalled()
      expect(waitSpy).toHaveBeenCalledWith(
        'job-123',
        expect.objectContaining({ onProgress: expect.any(Function) })
      )
      expect(result).toEqual(expected)
    })

    it('returns cached result when start response includes result', async () => {
      const request = createRequest()
      const expected: PdfProcessingResponse = {
        success: true,
        message: 'Cached',
        outputFileName: 'cached.pdf',
        downloadUrl: '/uploads/cached.pdf',
        processingTime: '00:00:03',
        inputPages: 5,
        outputPages: 1,
      }

      vi.spyOn(apiClient as any, 'startProgressUpload').mockResolvedValue({
        success: true,
        jobId: 'job-dup',
        result: expected,
      } as StartProcessingResponse)

      const waitSpy = vi.spyOn(apiClient as any, 'waitForJobCompletion')

      const result = await apiClient.processPdf(request, progressCallback)

      expect(waitSpy).not.toHaveBeenCalled()
      expect(result).toEqual(expected)
    })

    it('resumes existing job from local storage when completed', async () => {
      const request = createRequest()
      const expected: PdfProcessingResponse = {
        success: true,
        message: 'Completed',
        outputFileName: 'done.pdf',
        downloadUrl: '/uploads/done.pdf',
        processingTime: '00:00:04',
        inputPages: 8,
        outputPages: 2,
      }

      const fingerprint = (apiClient as any).computeFingerprint(request)
      const storageKey = `sheetbuilder:job:${fingerprint}`
      window.localStorage.setItem(
        storageKey,
        JSON.stringify({ jobId: 'resume-1', status: 'processing', updatedAt: Date.now() })
      )

      vi.spyOn(apiClient as any, 'getJobStatus').mockResolvedValue({
        success: true,
        jobId: 'resume-1',
        stage: 'Completed',
        startTime: new Date().toISOString(),
        endTime: new Date().toISOString(),
        progress: null,
        result: expected,
        error: null,
      })

      const startSpy = vi.spyOn(apiClient as any, 'startProgressUpload')
      const waitSpy = vi.spyOn(apiClient as any, 'waitForJobCompletion')

      const result = await apiClient.processPdf(request, progressCallback)

      expect(startSpy).not.toHaveBeenCalled()
      expect(waitSpy).not.toHaveBeenCalled()
      expect(result).toEqual(expected)
    })

    it('surfacing backend failures as ApiError', async () => {
      const request = createRequest()
      vi.spyOn(apiClient as any, 'startProgressUpload').mockResolvedValue({
        success: true,
        jobId: 'job-fail',
      } as StartProcessingResponse)

      vi.spyOn(apiClient as any, 'waitForJobCompletion').mockRejectedValue(new ApiError('Processing failed', 500))

      await expect(apiClient.processPdf(request, progressCallback)).rejects.toThrow('Processing failed')
    })
  })
})
