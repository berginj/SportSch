import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import AccessRequestsSection from '../../../pages/admin/AccessRequestsSection';

describe('AccessRequestsSection', () => {
  const mockProps = {
    leagueId: 'test-league',
    setLeagueId: vi.fn(),
    me: { userId: 'user-1', email: 'admin@example.com', memberships: [] },
    isGlobalAdmin: false,
    accessStatus: 'Pending',
    setAccessStatus: vi.fn(),
    accessScope: 'MyLeague',
    setAccessScope: vi.fn(),
    accessLeagueFilter: '',
    setAccessLeagueFilter: vi.fn(),
    accessLeagueOptions: [],
    loading: false,
    err: '',
    sorted: [],
    load: vi.fn(),
    loadMembershipsAndTeams: vi.fn(),
    memLoading: false,
    approve: vi.fn(),
    deny: vi.fn()
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders without crashing', () => {
    render(<AccessRequestsSection {...mockProps} />);
    expect(screen.getByText(/Admin: access requests/i)).toBeInTheDocument();
  });

  it('displays loading state', () => {
    render(<AccessRequestsSection {...mockProps} loading={true} />);
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('displays error message', () => {
    render(<AccessRequestsSection {...mockProps} err="Failed to load requests" />);
    expect(screen.getByText('Failed to load requests')).toBeInTheDocument();
  });

  it('renders access requests list', () => {
    const mockRequests = [
      {
        userId: 'user-2',
        email: 'requester@example.com',
        status: 'Pending',
        leagueId: 'test-league',
        requestedAt: '2026-01-15T10:00:00Z'
      },
      {
        userId: 'user-3',
        email: 'another@example.com',
        status: 'Pending',
        leagueId: 'test-league',
        requestedAt: '2026-01-16T11:00:00Z'
      }
    ];

    render(<AccessRequestsSection {...mockProps} sorted={mockRequests} />);

    expect(screen.getByText('requester@example.com')).toBeInTheDocument();
    expect(screen.getByText('another@example.com')).toBeInTheDocument();
  });

  it('calls approve when approve button is clicked', async () => {
    const mockRequests = [
      {
        userId: 'user-2',
        email: 'requester@example.com',
        status: 'Pending',
        leagueId: 'test-league',
        requestedRole: 'Coach'
      }
    ];

    const mockApprove = vi.fn().mockResolvedValue(undefined);

    render(
      <AccessRequestsSection
        {...mockProps}
        sorted={mockRequests}
        approve={mockApprove}
      />
    );

    const approveButtons = screen.getAllByRole('button', { name: /approve/i });
    fireEvent.click(approveButtons[0]);

    // The approve function is called with the request object
    expect(mockApprove).toHaveBeenCalledWith(mockRequests[0]);
  });

  it('calls deny when deny button is clicked', async () => {
    const mockRequests = [
      {
        userId: 'user-2',
        email: 'requester@example.com',
        status: 'Pending',
        leagueId: 'test-league',
        requestedRole: 'Coach'
      }
    ];

    const mockDeny = vi.fn().mockResolvedValue(undefined);

    render(
      <AccessRequestsSection
        {...mockProps}
        sorted={mockRequests}
        deny={mockDeny}
      />
    );

    const denyButtons = screen.getAllByRole('button', { name: /deny/i });
    fireEvent.click(denyButtons[0]);

    // The deny function is called with the request object
    expect(mockDeny).toHaveBeenCalledWith(mockRequests[0]);
  });

  it('shows empty state when no requests', () => {
    render(<AccessRequestsSection {...mockProps} sorted={[]} />);
    expect(screen.getByText(/No pending requests/i)).toBeInTheDocument();
  });

  it('allows changing access status filter', () => {
    render(<AccessRequestsSection {...mockProps} />);

    const statusSelect = screen.getByRole('combobox', { name: /status/i });
    fireEvent.change(statusSelect, { target: { value: 'Approved' } });

    expect(mockProps.setAccessStatus).toHaveBeenCalledWith('Approved');
  });

  it('shows global admin scope options for global admins', () => {
    render(<AccessRequestsSection {...mockProps} isGlobalAdmin={true} />);

    const scopeSelect = screen.getByRole('combobox', { name: /scope/i });
    expect(scopeSelect).toBeInTheDocument();

    fireEvent.change(scopeSelect, { target: { value: 'all' } });
    expect(mockProps.setAccessScope).toHaveBeenCalledWith('all');
  });

  it('does not show scope selector for non-global admins', () => {
    render(<AccessRequestsSection {...mockProps} isGlobalAdmin={false} />);

    const scopeSelects = screen.queryAllByRole('combobox', { name: /scope/i });
    expect(scopeSelects).toHaveLength(0);
  });

  it('allows bulk selection of requests', () => {
    const mockRequests = [
      { userId: 'user-2', email: 'user2@example.com', status: 'Pending', leagueId: 'test-league' },
      { userId: 'user-3', email: 'user3@example.com', status: 'Pending', leagueId: 'test-league' }
    ];

    render(<AccessRequestsSection {...mockProps} sorted={mockRequests} />);

    const checkboxes = screen.getAllByRole('checkbox');

    // Click first request checkbox
    fireEvent.click(checkboxes[1]); // Skip "select all" checkbox

    // Verify checkbox is checked
    expect(checkboxes[1]).toBeChecked();
  });

  it('select all checkbox selects all requests', () => {
    const mockRequests = [
      { userId: 'user-2', email: 'user2@example.com', status: 'Pending', leagueId: 'test-league' },
      { userId: 'user-3', email: 'user3@example.com', status: 'Pending', leagueId: 'test-league' }
    ];

    render(<AccessRequestsSection {...mockProps} sorted={mockRequests} />);

    const selectAllCheckbox = screen.getByRole('checkbox', { name: /select all/i });
    fireEvent.click(selectAllCheckbox);

    const allCheckboxes = screen.getAllByRole('checkbox');
    // All individual checkboxes should be checked (excluding the select-all checkbox itself)
    allCheckboxes.slice(1).forEach(checkbox => {
      expect(checkbox).toBeChecked();
    });
  });

  it('displays correct count of selected items', () => {
    const mockRequests = [
      { userId: 'user-2', email: 'user2@example.com', status: 'Pending', leagueId: 'test-league' },
      { userId: 'user-3', email: 'user3@example.com', status: 'Pending', leagueId: 'test-league' }
    ];

    render(<AccessRequestsSection {...mockProps} sorted={mockRequests} />);

    const checkboxes = screen.getAllByRole('checkbox');

    // Click first request
    fireEvent.click(checkboxes[1]);

    expect(screen.getByText(/1 request selected/i)).toBeInTheDocument();

    // Click second request
    fireEvent.click(checkboxes[2]);

    expect(screen.getByText(/2 requests selected/i)).toBeInTheDocument();
  });

  it('allows changing role for bulk approval', () => {
    const mockRequests = [
      { userId: 'user-2', email: 'user2@example.com', status: 'Pending', leagueId: 'test-league' }
    ];

    render(<AccessRequestsSection {...mockProps} sorted={mockRequests} />);

    // Select a request first
    const checkboxes = screen.getAllByRole('checkbox');
    fireEvent.click(checkboxes[1]);

    // Find role selector by text content "Assign role:"
    const roleSelect = screen.getByText(/Assign role:/i).parentElement.querySelector('select');
    fireEvent.change(roleSelect, { target: { value: 'LeagueAdmin' } });

    // Verify the value changed
    expect(roleSelect.value).toBe('LeagueAdmin');
  });
});
