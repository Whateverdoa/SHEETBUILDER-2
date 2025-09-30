import '@testing-library/jest-dom'
import { vi } from 'vitest'

type EventCallback = (data?: unknown) => void

type EventCallbackRegistration = (event: string, callback: EventCallback) => void

global.URL.createObjectURL = vi.fn(() => 'mocked-url')
global.URL.revokeObjectURL = vi.fn()

class MockXMLHttpRequest {
  public status = 200
  public statusText = 'OK'
  public responseText = ''
  public timeout = 0

  private listeners: Record<string, EventCallback[]> = {}
  private uploadListeners: Record<string, EventCallback[]> = {}

  public upload = {
    addEventListener: vi.fn<EventCallbackRegistration>((event, callback) => {
      if (!this.uploadListeners[event]) {
        this.uploadListeners[event] = []
      }
      this.uploadListeners[event].push(callback)
    }),
    _trigger: (event: string, data?: unknown) => {
      for (const callback of this.uploadListeners[event] ?? []) {
        callback(data)
      }
    },
  }

  public addEventListener(event: string, callback: EventCallback): void {
    if (!this.listeners[event]) {
      this.listeners[event] = []
    }
    this.listeners[event].push(callback)
  }

  public open(): void {
    // no-op for tests
  }

  public send(): void {
    // no-op for tests
  }

  public _trigger(event: string, data?: unknown): void {
    for (const callback of this.listeners[event] ?? []) {
      callback(data)
    }
  }
}

type TriggerableXMLHttpRequest = XMLHttpRequest & {
  _trigger: (event: string, data?: unknown) => void
  upload: {
    addEventListener: ReturnType<typeof vi.fn<EventCallbackRegistration>>
    _trigger: (event: string, data?: unknown) => void
  }
}

global.XMLHttpRequest = MockXMLHttpRequest as unknown as {
  new (): TriggerableXMLHttpRequest
}

const storage = new Map<string, string>()
Object.defineProperty(window, 'localStorage', {
  value: {
    getItem: (key: string) => (storage.has(key) ? storage.get(key)! : null),
    setItem: (key: string, value: string) => {
      storage.set(key, value)
    },
    removeItem: (key: string) => {
      storage.delete(key)
    },
    clear: () => {
      storage.clear()
    },
  },
})
