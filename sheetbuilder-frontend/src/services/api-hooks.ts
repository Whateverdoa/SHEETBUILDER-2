import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiClient } from './api-client'
import type { PdfProcessingRequest, UploadProgress } from '../types'

// Query keys for consistent caching
export const queryKeys = {
  health: ['health'] as const,
  processingJobs: ['processing-jobs'] as const,
}

// Health check hook
export function useHealth() {
  return useQuery({
    queryKey: queryKeys.health,
    queryFn: () => apiClient.getHealth(),
    refetchInterval: 30000, // Check health every 30 seconds
    retry: 3,
  })
}

// PDF processing mutation
export function useProcessPdf() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({
      request,
      onUploadProgress,
    }: {
      request: PdfProcessingRequest
      onUploadProgress?: (progress: UploadProgress) => void
    }) => apiClient.processPdf(request, onUploadProgress),
    onSuccess: () => {
      // Invalidate related queries
      queryClient.invalidateQueries({ queryKey: queryKeys.processingJobs })
    },
  })
}

// Download file hook
export function useDownloadFile() {
  return useMutation({
    mutationFn: ({ downloadUrl, fileName }: { downloadUrl: string; fileName: string }) =>
      apiClient.downloadFile(downloadUrl, fileName),
  })
}