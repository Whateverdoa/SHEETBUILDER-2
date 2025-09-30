import { useState } from 'react'
import { ProcessingForm, ProcessingResults } from '../components/features'
import type { PdfProcessingResponse } from '../types'

export function Home() {
  const [results, setResults] = useState<PdfProcessingResponse[]>([])

  const handleProcessingComplete = (responses: PdfProcessingResponse | PdfProcessingResponse[]) => {
    // Handle both single response and array of responses
    const responseArray = Array.isArray(responses) ? responses : [responses]
    console.log('📥 Received processing results:', responseArray)
    setResults(prev => [...responseArray, ...prev])
  }

  return (
    <div className="min-h-screen bg-slate-50 dark:bg-slate-900">
      {/* Header */}
      <header className="bg-white dark:bg-slate-800 border-b border-slate-200 dark:border-slate-700">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-6">
          <div className="flex items-center justify-between">
            <div>
              <h1 className="text-3xl font-bold text-gradient-primary">
                SheetBuilder
              </h1>
              <p className="text-slate-600 dark:text-slate-400 mt-1">
                Professional PDF processing for Variable Data Printing
              </p>
            </div>
            
            {/* Theme toggle placeholder - will be implemented later */}
            <div className="flex items-center space-x-4">
              <span className="text-sm text-slate-500 dark:text-slate-400">
                v1.0.0
              </span>
            </div>
          </div>
        </div>
      </header>

      {/* Main Content */}
      <main className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-8">
          {/* Processing Form */}
          <div className="space-y-6">
            <ProcessingForm onProcessingComplete={handleProcessingComplete} />
          </div>

          {/* Results */}
          <div className="space-y-6">
            <ProcessingResults results={results} />
          </div>
        </div>
      </main>

      {/* Footer */}
      <footer className="bg-white dark:bg-slate-800 border-t border-slate-200 dark:border-slate-700 mt-16">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-6">
          <div className="flex items-center justify-between">
            <p className="text-sm text-slate-600 dark:text-slate-400">
              © 2024 SheetBuilder. Built with React and TypeScript.
            </p>
            <div className="flex items-center space-x-4 text-sm text-slate-500 dark:text-slate-400">
              <span>API Status: Connected</span>
            </div>
          </div>
        </div>
      </footer>
    </div>
  )
}