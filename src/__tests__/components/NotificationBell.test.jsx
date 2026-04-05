import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useNotifications } from "../../lib/hooks/useNotifications";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

function NotificationsHarness() {
  const { unreadCount, notifications, isOpen, toggleDropdown } = useNotifications("league-1", 200);

  return (
    <div>
      <button type="button" onClick={toggleDropdown}>
        Toggle notifications
      </button>
      <div data-testid="open-state">{String(isOpen)}</div>
      <div data-testid="unread-count">{String(unreadCount)}</div>
      <div data-testid="notification-items">
        {notifications.map((notification) => notification.notificationId).join(",")}
      </div>
    </div>
  );
}

describe("useNotifications", () => {
  beforeEach(() => {
    vi.clearAllMocks();

    let unreadCallCount = 0;
    api.apiFetch.mockImplementation((path) => {
      const url = String(path || "");

      if (url === "/api/notifications/unread-count") {
        unreadCallCount += 1;
        return Promise.resolve({ count: unreadCallCount === 1 ? 1 : 2 });
      }

      if (url === "/api/notifications?pageSize=20") {
        return Promise.resolve({
          items: [
            {
              notificationId: `note-${unreadCallCount}`,
              message: `Alert ${unreadCallCount}`,
              isRead: false,
              createdUtc: "2026-03-14T12:00:00Z",
            },
          ],
        });
      }

      if (url.endsWith("/read") || url === "/api/notifications/read-all") {
        return Promise.resolve({ ok: true });
      }

      throw new Error(`Unexpected apiFetch call: ${url}`);
    });
  });

  it("refreshes open notification content when the unread badge changes", async () => {
    render(<NotificationsHarness />);

    await waitFor(() => expect(screen.getByTestId("unread-count")).toHaveTextContent("1"));

    fireEvent.click(screen.getByRole("button", { name: /toggle notifications/i }));

    await waitFor(() => expect(screen.getByTestId("notification-items")).toHaveTextContent("note-1"));

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 250));
    });

    await waitFor(() => {
      const listCalls = api.apiFetch.mock.calls.filter(([path]) => path === "/api/notifications?pageSize=20");
      expect(listCalls.length).toBeGreaterThanOrEqual(2);
    });

    await waitFor(() => expect(screen.getByTestId("unread-count")).toHaveTextContent("2"));
    await waitFor(() => expect(screen.getByTestId("notification-items")).not.toHaveTextContent("note-1"));
  });
});
