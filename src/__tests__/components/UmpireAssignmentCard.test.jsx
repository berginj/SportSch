import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import UmpireAssignmentCard from '../../components/UmpireAssignmentCard';

describe('UmpireAssignmentCard', () => {
  const mockAssignment = {
    assignmentId: 'assign-1',
    homeTeamId: 'Tigers',
    awayTeamId: 'Lions',
    gameDate: '2026-06-15',
    startTime: '15:00',
    endTime: '16:30',
    fieldDisplayName: 'Park 1 > Field 1',
    division: '10U',
    status: 'Assigned'
  };

  it('renders pending assignment with accept and decline buttons', () => {
    const onAccept = vi.fn();
    const onDecline = vi.fn();

    render(
      <UmpireAssignmentCard
        assignment={mockAssignment}
        onAccept={onAccept}
        onDecline={onDecline}
        showActions={true}
      />
    );

    expect(screen.getByText('Tigers vs Lions')).toBeInTheDocument();
    expect(screen.getByText(/15:00 - 16:30/)).toBeInTheDocument();
    expect(screen.getByText('Park 1 > Field 1')).toBeInTheDocument();

    // Verify action buttons present
    expect(screen.getByRole('button', { name: /accept assignment/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /decline/i })).toBeInTheDocument();

    // Verify status badge
    expect(screen.getByText(/pending/i)).toBeInTheDocument();
  });

  it('calls onAccept when accept button clicked', () => {
    const onAccept = vi.fn();
    const onDecline = vi.fn();

    render(
      <UmpireAssignmentCard
        assignment={mockAssignment}
        onAccept={onAccept}
        onDecline={onDecline}
        showActions={true}
      />
    );

    const acceptButton = screen.getByRole('button', { name: /accept assignment/i });
    fireEvent.click(acceptButton);

    expect(onAccept).toHaveBeenCalledTimes(1);
  });

  it('shows decline modal when decline button clicked', () => {
    const onAccept = vi.fn();
    const onDecline = vi.fn();

    render(
      <UmpireAssignmentCard
        assignment={mockAssignment}
        onAccept={onAccept}
        onDecline={onDecline}
        showActions={true}
      />
    );

    const declineButton = screen.getByRole('button', { name: /decline/i });
    fireEvent.click(declineButton);

    // Modal should open
    expect(screen.getByText(/decline assignment/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /confirm decline/i })).toBeInTheDocument();
  });

  it('renders accepted assignment without action buttons', () => {
    const acceptedAssignment = {
      ...mockAssignment,
      status: 'Accepted',
      responseUtc: '2026-06-10T10:00:00Z'
    };

    render(
      <UmpireAssignmentCard
        assignment={acceptedAssignment}
        showActions={false}
      />
    );

    // Verify confirmed badge exists
    expect(screen.getAllByText(/confirmed/i).length).toBeGreaterThan(0);

    // Verify no action buttons
    expect(screen.queryByRole('button', { name: /accept/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /decline/i })).not.toBeInTheDocument();

    // Verify confirmation message present
    expect(screen.getByText(/you confirmed this assignment/i)).toBeInTheDocument();
  });

  it('shows decline reason when assignment is declined', () => {
    const declinedAssignment = {
      ...mockAssignment,
      status: 'Declined',
      declineReason: 'Schedule conflict with work'
    };

    render(
      <UmpireAssignmentCard
        assignment={declinedAssignment}
        showActions={false}
      />
    );

    // Verify declined badge
    expect(screen.getAllByText(/declined/i).length).toBeGreaterThan(0);

    // Verify decline reason shown
    expect(screen.getByText(/schedule conflict with work/i)).toBeInTheDocument();
  });

  it('displays all game details correctly', () => {
    render(
      <UmpireAssignmentCard
        assignment={mockAssignment}
        showActions={false}
      />
    );

    // Teams
    expect(screen.getByText('Tigers vs Lions')).toBeInTheDocument();

    // Time
    expect(screen.getByText(/15:00 - 16:30/)).toBeInTheDocument();

    // Field
    expect(screen.getByText('Park 1 > Field 1')).toBeInTheDocument();

    // Division
    expect(screen.getByText(/10U Division/)).toBeInTheDocument();
  });
});
