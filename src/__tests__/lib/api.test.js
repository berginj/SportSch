import { describe, it, expect, beforeEach, vi } from 'vitest';
import { apiFetch, apiBase } from '../../lib/api';
import { LEAGUE_HEADER_NAME, LEAGUE_STORAGE_KEY } from '../../lib/constants';

describe('api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    global.fetch.mockClear();
  });

  describe('apiBase', () => {
    it('should return empty string in production', () => {
      vi.stubEnv('DEV', false);
      expect(apiBase()).toBe('');
      vi.unstubAllEnvs();
    });

    it('should return API base URL in development', () => {
      vi.stubEnv('DEV', true);
      vi.stubEnv('VITE_API_BASE_URL', 'http://localhost:7071');
      expect(apiBase()).toBe('http://localhost:7071');
      vi.unstubAllEnvs();
    });

    it('should trim trailing slashes', () => {
      vi.stubEnv('DEV', true);
      vi.stubEnv('VITE_API_BASE_URL', 'http://localhost:7071///');
      expect(apiBase()).toBe('http://localhost:7071');
      vi.unstubAllEnvs();
    });
  });

  describe('apiFetch', () => {
    it('should attach x-league-id header from localStorage', async () => {
      localStorage.getItem.mockReturnValue('test-league-123');

      global.fetch.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve(JSON.stringify({ data: { success: true } })),
      });

      const result = await apiFetch('/api/slots');

      expect(result).toEqual({ success: true });
      expect(global.fetch).toHaveBeenCalledWith(
        '/api/slots',
        expect.objectContaining({
          headers: expect.any(Headers),
          credentials: 'include',
        })
      );
      expect(localStorage.getItem).toHaveBeenCalledWith(LEAGUE_STORAGE_KEY);
    });

    it('should set Content-Type header for JSON body', async () => {
      global.fetch.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve(JSON.stringify({ data: { success: true } })),
      });

      await apiFetch('/api/slots', {
        method: 'POST',
        body: JSON.stringify({ gameDate: '2026-01-16' }),
      });

      const callArgs = global.fetch.mock.calls[0];
      const headers = callArgs[1].headers;
      expect(headers.get('Content-Type')).toBe('application/json');
    });

    it('should not set Content-Type header for FormData', async () => {
      global.fetch.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve(JSON.stringify({ data: { success: true } })),
      });

      const formData = new FormData();
      formData.append('file', 'test');

      await apiFetch('/api/upload', {
        method: 'POST',
        body: formData,
      });

      const callArgs = global.fetch.mock.calls[0];
      const headers = callArgs[1].headers;
      expect(headers.has('Content-Type')).toBe(false);
    });

    it('should unwrap data envelope', async () => {
      global.fetch.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve(JSON.stringify({ data: { id: '123', name: 'Test' } })),
      });

      const result = await apiFetch('/api/slots');

      expect(result).toEqual({ id: '123', name: 'Test' });
    });

    it('should return raw data if no envelope', async () => {
      global.fetch.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve(JSON.stringify({ id: '123', name: 'Test' })),
      });

      const result = await apiFetch('/api/slots');

      expect(result).toEqual({ id: '123', name: 'Test' });
    });

    it('should handle error responses with error codes', async () => {
      global.fetch.mockResolvedValue({
        ok: false,
        status: 404,
        text: () => Promise.resolve(JSON.stringify({
          error: {
            code: 'FIELD_NOT_FOUND',
            message: 'Field not found'
          }
        })),
      });

      await expect(apiFetch('/api/fields/invalid')).rejects.toThrow();
    });

    it('should handle error responses without error codes', async () => {
      global.fetch.mockResolvedValue({
        ok: false,
        status: 500,
        text: () => Promise.resolve(JSON.stringify({
          error: 'Internal Server Error'
        })),
      });

      await expect(apiFetch('/api/slots')).rejects.toThrow('Internal Server Error');
    });

    it('should handle network errors', async () => {
      global.fetch.mockRejectedValue(new Error('Network error'));

      await expect(apiFetch('/api/slots')).rejects.toThrow('Network error');
    });

    it('should handle non-JSON responses', async () => {
      global.fetch.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve('plain text response'),
      });

      const result = await apiFetch('/api/export');

      expect(result).toBe('plain text response');
    });

    it('should handle empty responses', async () => {
      global.fetch.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve(''),
      });

      const result = await apiFetch('/api/slots');

      expect(result).toBe(null);
    });

    it('should preserve custom headers', async () => {
      global.fetch.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve(JSON.stringify({ data: {} })),
      });

      await apiFetch('/api/slots', {
        headers: {
          'X-Custom-Header': 'custom-value',
        },
      });

      const callArgs = global.fetch.mock.calls[0];
      const headers = callArgs[1].headers;
      expect(headers.get('X-Custom-Header')).toBe('custom-value');
    });

    it('should include credentials', async () => {
      global.fetch.mockResolvedValue({
        ok: true,
        text: () => Promise.resolve(JSON.stringify({ data: {} })),
      });

      await apiFetch('/api/slots');

      const callArgs = global.fetch.mock.calls[0];
      expect(callArgs[1].credentials).toBe('include');
    });

    it('should attach error details to thrown error', async () => {
      global.fetch.mockResolvedValue({
        ok: false,
        status: 409,
        text: () => Promise.resolve(JSON.stringify({
          error: {
            code: 'SLOT_CONFLICT',
            message: 'Slot conflicts with existing slot',
            details: { conflictingSlotId: 'slot-123' }
          }
        })),
      });

      try {
        await apiFetch('/api/slots');
      } catch (error) {
        expect(error.status).toBe(409);
        expect(error.code).toBe('SLOT_CONFLICT');
        expect(error.details).toEqual({ conflictingSlotId: 'slot-123' });
      }
    });
  });
});
