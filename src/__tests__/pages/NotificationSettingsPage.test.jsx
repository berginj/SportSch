import { describe, it, expect, vi, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import NotificationSettingsPage from "../../pages/NotificationSettingsPage";
import { apiFetch } from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

const preferencesFixture = {
  email: "coach@example.com",
  enableInAppNotifications: true,
  enableEmailNotifications: true,
  emailOnSlotCreated: true,
  emailOnSlotCancelled: false,
  emailOnRequestReceived: true,
  emailOnRequestApproved: true,
  emailOnRequestDenied: false,
  emailOnGameReminder: true,
  enableDailyDigest: false,
  digestTime: "08:00",
};

describe("NotificationSettingsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("saves preferences and clears the success timer on unmount", async () => {
    vi.mocked(apiFetch)
      .mockResolvedValueOnce(preferencesFixture)
      .mockResolvedValueOnce({});
    const clearTimeoutSpy = vi.spyOn(globalThis, "clearTimeout");

    const { unmount } = render(<NotificationSettingsPage leagueId="league-1" />);
    await screen.findByText(/master settings/i);

    fireEvent.click(screen.getByRole("button", { name: /save preferences/i }));

    await waitFor(() => {
      const saveCall = vi
        .mocked(apiFetch)
        .mock.calls.find(([url, options]) => url === "/api/notifications/preferences" && options?.method === "PATCH");
      expect(saveCall).toBeTruthy();
    });

    const saveCall = vi
      .mocked(apiFetch)
      .mock.calls.find(([url, options]) => url === "/api/notifications/preferences" && options?.method === "PATCH");
    const payload = JSON.parse(saveCall[1].body);
    expect(payload).toMatchObject({
      enableInAppNotifications: true,
      enableEmailNotifications: true,
      emailOnGameReminder: true,
      digestTime: "08:00",
    });
    expect(screen.getByText(/saved successfully/i)).toBeInTheDocument();

    unmount();
    expect(clearTimeoutSpy).toHaveBeenCalled();
  });

  it("shows league selection prompt when no league is active", () => {
    render(<NotificationSettingsPage leagueId="" />);
    expect(screen.getByText(/please select a league/i)).toBeInTheDocument();
    expect(apiFetch).not.toHaveBeenCalled();
  });
});
