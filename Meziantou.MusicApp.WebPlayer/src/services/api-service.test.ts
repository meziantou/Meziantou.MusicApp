import { describe, it, expect } from 'vitest';
import { ApiService } from './api-service';

describe('ApiService', () => {
  it('getCoverHeaders prefers modern image formats', () => {
    const service = new ApiService('https://example.com');

    expect(service.getCoverHeaders()).toEqual({
      Accept: 'image/avif,image/webp,image/png,image/jpeg;q=0.8,*/*;q=0.5'
    });
  });
});
