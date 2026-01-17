import { describe, it, expect, beforeEach, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { useAccessRequests } from '../../../lib/hooks/useAccessRequests';
import * as api from '../../../lib/api';

vi.mock('../../../lib/api', () => ({
  apiFetch: vi.fn(),
}));

describe('useAccessRequests', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('should initialize with loading state', () => {
    api.apiFetch.mockResolvedValue([]);
    const { result } = renderHook(() =>
      useAccessRequests('league-1', false, 'Pending', 'league', '')
    );

    expect(result.current.loading).toBe(true);
    expect(result.current.items).toEqual([]);
    expect(result.current.err).toBe('');
  });

  it('should load access requests for league scope', async () => {
    const mockRequests = [
      { userId: 'user-1', email: 'test@example.com', status: 'Pending' },
      { userId: 'user-2', email: 'test2@example.com', status: 'Pending' },
    ];
    api.apiFetch.mockResolvedValue(mockRequests);

    const { result } = renderHook(() =>
      useAccessRequests('league-1', false, 'Pending', 'league', '')
    );

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
    });

    expect(api.apiFetch).toHaveBeenCalledWith(
      '/api/accessrequests?status=Pending'
    );
    expect(result.current.items).toEqual(mockRequests);
    expect(result.current.err).toBe('');
  });

  it('should load all access requests for global admin', async () => {
    const mockRequests = [
      { userId: 'user-1', email: 'test@example.com', status: 'Pending', leagueId: 'league-1' },
      { userId: 'user-2', email: 'test2@example.com', status: 'Pending', leagueId: 'league-2' },
    ];
    api.apiFetch.mockResolvedValue(mockRequests);

    const { result } = renderHook(() =>
      useAccessRequests('league-1', true, 'Pending', 'all', '')
    );

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
    });

    expect(api.apiFetch).toHaveBeenCalledWith(
      '/api/accessrequests?status=Pending&all=true'
    );
    expect(result.current.items).toEqual(mockRequests);
  });

  it('should filter by leagueId for global admin', async () => {
    const mockRequests = [
      { userId: 'user-1', email: 'test@example.com', status: 'Pending', leagueId: 'league-1' },
      { userId: 'user-2', email: 'test2@example.com', status: 'Pending', leagueId: 'league-2' },
    ];
    api.apiFetch.mockResolvedValue(mockRequests);

    const { result } = renderHook(() =>
      useAccessRequests('league-1', true, 'Pending', 'all', 'league-1')
    );

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
    });

    expect(result.current.items).toHaveLength(1);
    expect(result.current.items[0].leagueId).toBe('league-1');
  });

  it('should handle load errors', async () => {
    const error = new Error('Network error');
    api.apiFetch.mockRejectedValue(error);

    const { result } = renderHook(() =>
      useAccessRequests('league-1', false, 'Pending', 'league', '')
    );

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
    });

    expect(result.current.err).toBe('Network error');
    expect(result.current.items).toEqual([]);
  });

  it('should approve access request', async () => {
    const mockRequests = [
      { userId: 'user-1', email: 'test@example.com', status: 'Pending' },
    ];
    api.apiFetch
      .mockResolvedValueOnce(mockRequests) // Initial load
      .mockResolvedValueOnce({ success: true }) // Approve
      .mockResolvedValueOnce([]); // Reload after approve

    const { result } = renderHook(() =>
      useAccessRequests('league-1', false, 'Pending', 'league', '')
    );

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
    });

    const approveResult = await result.current.approve('user-1');

    expect(approveResult.success).toBe(true);
    expect(api.apiFetch).toHaveBeenCalledWith(
      '/api/accessrequests/user-1/approve',
      {
        method: 'PATCH',
        body: JSON.stringify({}),
      }
    );
  });

  it('should deny access request with reason', async () => {
    const mockRequests = [
      { userId: 'user-1', email: 'test@example.com', status: 'Pending' },
    ];
    api.apiFetch
      .mockResolvedValueOnce(mockRequests) // Initial load
      .mockResolvedValueOnce({ success: true }) // Deny
      .mockResolvedValueOnce([]); // Reload after deny

    const { result } = renderHook(() =>
      useAccessRequests('league-1', false, 'Pending', 'league', '')
    );

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
    });

    const denyResult = await result.current.deny('user-1', 'Not eligible');

    expect(denyResult.success).toBe(true);
    expect(api.apiFetch).toHaveBeenCalledWith(
      '/api/accessrequests/user-1/deny',
      {
        method: 'PATCH',
        body: JSON.stringify({ reason: 'Not eligible' }),
      }
    );
  });

  it('should handle approve errors', async () => {
    const mockRequests = [
      { userId: 'user-1', email: 'test@example.com', status: 'Pending' },
    ];
    const error = new Error('Approval failed');
    api.apiFetch
      .mockResolvedValueOnce(mockRequests) // Initial load
      .mockRejectedValueOnce(error); // Approve fails

    const { result } = renderHook(() =>
      useAccessRequests('league-1', false, 'Pending', 'league', '')
    );

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
    });

    const approveResult = await result.current.approve('user-1');

    expect(approveResult.success).toBe(false);
    expect(approveResult.error).toBe('Approval failed');
  });

  it('should not load when no leagueId and scope is league', async () => {
    const { result } = renderHook(() =>
      useAccessRequests('', false, 'Pending', 'league', '')
    );

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
    });

    expect(api.apiFetch).not.toHaveBeenCalled();
    expect(result.current.items).toEqual([]);
  });
});
