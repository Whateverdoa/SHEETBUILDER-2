import { describe, it, expect } from 'vitest'
import { validatePdfFile, formatFileSize } from '../utils/format'

describe('File Upload Utilities', () => {
  describe('validatePdfFile', () => {
    it('rejects null or undefined files', () => {
      expect(validatePdfFile(null as any)).toEqual({
        valid: false,
        error: 'No file selected'
      })
    })

    it('rejects non-PDF files', () => {
      const file = new File(['content'], 'test.txt', { type: 'text/plain' })
      expect(validatePdfFile(file)).toEqual({
        valid: false,
        error: 'File must be a PDF'
      })
    })

    it('accepts large PDF files without warnings', () => {
      const file = {
        name: 'massive.pdf',
        type: 'application/pdf',
        size: 1.2 * 1024 * 1024 * 1024 // 1.2GB
      } as File

      expect(validatePdfFile(file)).toEqual({
        valid: true
      })
    })

    it('accepts typical PDF files', () => {
      const file = new File(['pdf content'], 'test.pdf', {
        type: 'application/pdf'
      })

      expect(validatePdfFile(file)).toEqual({
        valid: true
      })
    })
  })

  describe('formatFileSize', () => {
    it('formats bytes correctly', () => {
      expect(formatFileSize(0)).toBe('0 Bytes')
      expect(formatFileSize(1024)).toBe('1 KB')
      expect(formatFileSize(1024 * 1024)).toBe('1 MB')
      expect(formatFileSize(500 * 1024 * 1024)).toBe('500 MB')
      expect(formatFileSize(1024 * 1024 * 1024)).toBe('1 GB')
    })

    it('handles decimal values', () => {
      expect(formatFileSize(1536)).toBe('1.5 KB')
      expect(formatFileSize(2.5 * 1024 * 1024)).toBe('2.5 MB')
    })
  })
})
