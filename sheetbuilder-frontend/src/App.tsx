import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ReactQueryDevtools } from '@tanstack/react-query-devtools'
import { BrowserRouter as Router, Routes, Route } from 'react-router-dom'
import { Toaster } from 'react-hot-toast'
import { Home } from './pages'

// Create a client
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
      staleTime: 5 * 60 * 1000, // 5 minutes
    },
    mutations: {
      retry: 1,
    },
  },
})

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <Router>
        <div className="App">
          <Routes>
            <Route path="/" element={<Home />} />
            {/* Add more routes here as needed */}
          </Routes>
          
          {/* Toast notifications */}
          <Toaster
            position="top-right"
            toastOptions={{
              duration: 4000,
              style: {
                background: 'var(--tw-color-slate-800)',
                color: 'var(--tw-color-slate-100)',
                border: '1px solid var(--tw-color-slate-700)',
              },
              success: {
                iconTheme: {
                  primary: 'var(--tw-color-green-500)',
                  secondary: 'var(--tw-color-white)',
                },
              },
              error: {
                iconTheme: {
                  primary: 'var(--tw-color-red-500)',
                  secondary: 'var(--tw-color-white)',
                },
              },
            }}
          />
        </div>
      </Router>
      
      {/* React Query Devtools */}
      <ReactQueryDevtools initialIsOpen={false} />
    </QueryClientProvider>
  )
}

export default App