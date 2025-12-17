// Utility functions for DOM manipulation and formatting

export function formatDuration(seconds: number): string {
  if (!seconds || !isFinite(seconds)) return '0:00';
  
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const secs = Math.floor(seconds % 60);
  
  if (hours > 0) {
    return `${hours}:${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
  }
  return `${minutes}:${secs.toString().padStart(2, '0')}`;
}

export function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 B';
  
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
}

export function formatDate(dateString: string): string {
  const date = new Date(dateString);
  return date.toLocaleDateString();
}

export function createElement<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  attributes?: Record<string, string>,
  ...children: (Node | string)[]
): HTMLElementTagNameMap[K] {
  const element = document.createElement(tag);
  
  if (attributes) {
    for (const [key, value] of Object.entries(attributes)) {
      if (key === 'className') {
        element.className = value;
      } else if (key.startsWith('data-')) {
        element.setAttribute(key, value);
      } else {
        element.setAttribute(key, value);
      }
    }
  }
  
  for (const child of children) {
    if (typeof child === 'string') {
      element.appendChild(document.createTextNode(child));
    } else {
      element.appendChild(child);
    }
  }
  
  return element;
}

export function debounce<T extends (...args: never[]) => void>(
  func: T,
  wait: number
): (...args: Parameters<T>) => void {
  let timeout: ReturnType<typeof setTimeout> | null = null;
  
  return function (this: unknown, ...args: Parameters<T>) {
    if (timeout) clearTimeout(timeout);
    timeout = setTimeout(() => func.apply(this, args), wait);
  };
}

export function throttle<T extends (...args: never[]) => void>(
  func: T,
  limit: number
): (...args: Parameters<T>) => void {
  let inThrottle = false;
  
  return function (this: unknown, ...args: Parameters<T>) {
    if (!inThrottle) {
      func.apply(this, args);
      inThrottle = true;
      setTimeout(() => inThrottle = false, limit);
    }
  };
}

export function normalizeSearch(text: string): string {
  return text
    .toLowerCase()
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, ''); // Remove diacritics
}

export function matchesSearch(text: string | null | undefined, query: string): boolean {
  if (!text) return false;
  if (!query) return true;
  
  const normalizedText = normalizeSearch(text);
  const normalizedQuery = normalizeSearch(query);
  
  return normalizedText.includes(normalizedQuery);
}

export function getNetworkType(): 'normal' | 'low-data' | 'unknown' {
  const connection = (navigator as Navigator & { 
    connection?: { 
      effectiveType?: string; 
      type?: string;
      saveData?: boolean;
    } 
  }).connection;
  
  if (!connection) return 'unknown';
  
  // If user explicitly requested data saving
  if (connection.saveData) {
    return 'low-data';
  }
  
  // If we know it's cellular
  if (connection.type === 'cellular') {
    return 'low-data';
  }
  
  // If the connection is slow, treat as cellular/low quality
  // Note: '4g' effective type is returned for fast connections (including Wifi/Ethernet)
  if (connection.effectiveType === 'slow-2g' || 
      connection.effectiveType === '2g' || 
      connection.effectiveType === '3g') {
    return 'low-data';
  }
  
  return 'normal'; // Default to normal for better quality
}

export function escapeHtml(text: string): string {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

export function generateId(): string {
  return Math.random().toString(36).substring(2, 11);
}
