import '@testing-library/jest-dom'

// Mock window.URL.createObjectURL
global.URL.createObjectURL = vi.fn(() => 'mocked-url')
global.URL.revokeObjectURL = vi.fn()

// Mock XMLHttpRequest for testing file uploads
class MockXMLHttpRequest {
  public upload = {
    addEventListener: vi.fn(),
    _trigger: vi.fn((event: string, data?: any) => {
      const callbacks = this.upload.addEventListener.mock.calls
        .filter(call => call[0] === event)
        .map(call => call[1])
      callbacks.forEach(callback => callback(data))
    })
  }
  public status = 200
  public statusText = 'OK'
  public responseText = ''
  private listeners: Record<string, Function[]> = {}

  addEventListener(event: string, callback: Function) {
    if (!this.listeners[event]) {
      this.listeners[event] = []
    }
    this.listeners[event].push(callback)
  }

  open() {}
  send() {}
  
  // Helper method to trigger events in tests
  _trigger(event: string, data?: any) {
    if (this.listeners[event]) {
      this.listeners[event].forEach(callback => callback(data))
    }
  }
}

global.XMLHttpRequest = MockXMLHttpRequest as any
// Lightweight localStorage mock for browser-only persistence APIs
const storage = new Map<string, string>()
Object.defineProperty(window, 'localStorage', {
  value: {
    getItem: (key: string) => (storage.has(key) ? storage.get(key)! : null),
    setItem: (key: string, value: string) => { storage.set(key, value) },
    removeItem: (key: string) => { storage.delete(key) },
    clear: () => { storage.clear() },
  },
})
