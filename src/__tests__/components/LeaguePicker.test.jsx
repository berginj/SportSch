import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import LeaguePicker from '../../components/LeaguePicker';
import * as api from '../../lib/api';

vi.mock('../../lib/api');

describe('LeaguePicker', () => {
  const mockSetLeagueId = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  describe('Regular user (non-global admin)', () => {
    it('renders league options from memberships', () => {
      const mockMe = {
        memberships: [
          { leagueId: 'league-1', role: 'LeagueAdmin' },
          { leagueId: 'league-2', role: 'Coach' }
        ],
        isGlobalAdmin: false
      };

      render(
        <LeaguePicker
          leagueId=""
          setLeagueId={mockSetLeagueId}
          me={mockMe}
        />
      );

      expect(screen.getByText('league-1 (LeagueAdmin)')).toBeInTheDocument();
      expect(screen.getByText('league-2 (Coach)')).toBeInTheDocument();
    });

    it('calls setLeagueId when selection changes', () => {
      const mockMe = {
        memberships: [
          { leagueId: 'league-1', role: 'LeagueAdmin' },
          { leagueId: 'league-2', role: 'Coach' }
        ],
        isGlobalAdmin: false
      };

      render(
        <LeaguePicker
          leagueId=""
          setLeagueId={mockSetLeagueId}
          me={mockMe}
        />
      );

      const select = screen.getByRole('combobox');
      fireEvent.change(select, { target: { value: 'league-1' } });

      expect(mockSetLeagueId).toHaveBeenCalledWith('league-1');
    });

    it('displays selected leagueId', () => {
      const mockMe = {
        memberships: [
          { leagueId: 'league-1', role: 'LeagueAdmin' }
        ],
        isGlobalAdmin: false
      };

      render(
        <LeaguePicker
          leagueId="league-1"
          setLeagueId={mockSetLeagueId}
          me={mockMe}
        />
      );

      const select = screen.getByRole('combobox');
      expect(select.value).toBe('league-1');
    });

    it('renders "No leagues" when memberships is empty', () => {
      const mockMe = {
        memberships: [],
        isGlobalAdmin: false
      };

      render(
        <LeaguePicker
          leagueId=""
          setLeagueId={mockSetLeagueId}
          me={mockMe}
        />
      );

      expect(screen.getByText('No leagues')).toBeInTheDocument();
      const select = screen.getByRole('combobox');
      expect(select).toBeDisabled();
    });

    it('handles memberships without roles', () => {
      const mockMe = {
        memberships: [
          { leagueId: 'league-1' }
        ],
        isGlobalAdmin: false
      };

      render(
        <LeaguePicker
          leagueId=""
          setLeagueId={mockSetLeagueId}
          me={mockMe}
        />
      );

      expect(screen.getByText('league-1')).toBeInTheDocument();
    });

    it('filters out memberships without leagueId', () => {
      const mockMe = {
        memberships: [
          { leagueId: 'league-1', role: 'Coach' },
          { leagueId: '', role: 'Coach' },
          { role: 'Viewer' }
        ],
        isGlobalAdmin: false
      };

      render(
        <LeaguePicker
          leagueId=""
          setLeagueId={mockSetLeagueId}
          me={mockMe}
        />
      );

      const options = screen.getAllByRole('option');
      expect(options).toHaveLength(1);
      expect(screen.getByText('league-1 (Coach)')).toBeInTheDocument();
    });
  });

  describe('Global admin', () => {
    it('fetches and displays all leagues', async () => {
      const mockMe = {
        memberships: [
          { leagueId: 'league-1', role: 'LeagueAdmin' }
        ],
        isGlobalAdmin: true
      };

      const mockGlobalLeagues = [
        { leagueId: 'league-1', name: 'League One' },
        { leagueId: 'league-2', name: 'League Two' },
        { leagueId: 'league-3', name: 'League Three' }
      ];

      vi.spyOn(api, 'apiFetch').mockResolvedValueOnce(mockGlobalLeagues);

      render(
        <LeaguePicker
          leagueId=""
          setLeagueId={mockSetLeagueId}
          me={mockMe}
        />
      );

      await waitFor(() => {
        expect(screen.getByText('League One (league-1) — LeagueAdmin')).toBeInTheDocument();
      });

      expect(screen.getByText('League Two (league-2)')).toBeInTheDocument();
      expect(screen.getByText('League Three (league-3)')).toBeInTheDocument();
    });

    it('shows loading state while fetching leagues', async () => {
      const mockMe = {
        memberships: [],
        isGlobalAdmin: true
      };

      vi.spyOn(api, 'apiFetch').mockImplementation(() => new Promise(() => {})); // Never resolves

      render(
        <LeaguePicker
          leagueId=""
          setLeagueId={mockSetLeagueId}
          me={mockMe}
        />
      );

      await waitFor(() => {
        expect(screen.getByText('Loading leagues...')).toBeInTheDocument();
      });

      const select = screen.getByRole('combobox');
      expect(select).toBeDisabled();
    });

    it('shows error message when API fetch fails', async () => {
      const mockMe = {
        memberships: [],
        isGlobalAdmin: true
      };

      vi.spyOn(api, 'apiFetch').mockRejectedValueOnce(new Error('API Error'));

      render(
        <LeaguePicker
          leagueId=""
          setLeagueId={mockSetLeagueId}
          me={mockMe}
        />
      );

      await waitFor(() => {
        expect(screen.getByText('API Error')).toBeInTheDocument();
      });

      expect(screen.getByText('No leagues')).toBeInTheDocument();
    });

    it('falls back to memberships when global leagues fetch fails', async () => {
      const mockMe = {
        memberships: [
          { leagueId: 'league-1', role: 'Coach' }
        ],
        isGlobalAdmin: true
      };

      vi.spyOn(api, 'apiFetch').mockRejectedValueOnce(new Error('Network error'));

      render(
        <LeaguePicker
          leagueId=""
          setLeagueId={mockSetLeagueId}
          me={mockMe}
        />
      );

      await waitFor(() => {
        expect(screen.getByText('Network error')).toBeInTheDocument();
      });

      // Component falls back to showing memberships when global fetch fails
      expect(screen.getByText('league-1 (Coach)')).toBeInTheDocument();
    });
  });

  describe('Custom labels and titles', () => {
    it('renders custom label', () => {
      const mockMe = {
        memberships: [{ leagueId: 'league-1', role: 'Coach' }],
        isGlobalAdmin: false
      };

      render(
        <LeaguePicker
          leagueId=""
          setLeagueId={mockSetLeagueId}
          me={mockMe}
          label="Select League:"
        />
      );

      expect(screen.getByText('Select League:')).toBeInTheDocument();
    });

    it('renders custom title attribute', () => {
      const mockMe = {
        memberships: [{ leagueId: 'league-1', role: 'Coach' }],
        isGlobalAdmin: false
      };

      const { container } = render(
        <LeaguePicker
          leagueId=""
          setLeagueId={mockSetLeagueId}
          me={mockMe}
          title="Custom title text"
        />
      );

      const label = container.querySelector('.leaguePicker');
      expect(label).toHaveAttribute('title', 'Custom title text');
    });

    it('uses default title when not provided', () => {
      const mockMe = {
        memberships: [{ leagueId: 'league-1', role: 'Coach' }],
        isGlobalAdmin: false
      };

      const { container } = render(
        <LeaguePicker
          leagueId=""
          setLeagueId={mockSetLeagueId}
          me={mockMe}
        />
      );

      const label = container.querySelector('.leaguePicker');
      expect(label).toHaveAttribute('title', 'Switch the active league for this view.');
    });
  });

  describe('Edge cases', () => {
    it('handles null memberships', () => {
      const mockMe = {
        memberships: null,
        isGlobalAdmin: false
      };

      render(
        <LeaguePicker
          leagueId=""
          setLeagueId={mockSetLeagueId}
          me={mockMe}
        />
      );

      expect(screen.getByText('No leagues')).toBeInTheDocument();
    });

    it('handles undefined me prop', () => {
      render(
        <LeaguePicker
          leagueId=""
          setLeagueId={mockSetLeagueId}
          me={undefined}
        />
      );

      expect(screen.getByText('No leagues')).toBeInTheDocument();
    });

    it('trims whitespace from leagueId and role', () => {
      const mockMe = {
        memberships: [
          { leagueId: '  league-1  ', role: '  Coach  ' }
        ],
        isGlobalAdmin: false
      };

      render(
        <LeaguePicker
          leagueId=""
          setLeagueId={mockSetLeagueId}
          me={mockMe}
        />
      );

      expect(screen.getByText('league-1 (Coach)')).toBeInTheDocument();
    });
  });
});
