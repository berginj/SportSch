import { describe, it, expect, beforeEach, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import NotificationCenterPage from "../../pages/NotificationCenterPage";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

describe("NotificationCenterPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.history.replaceState({}, "", "/#notifications");

    api.apiFetch.mockImplementation((path, options) => {
      const url = String(path || "");
      if (url === "/api/notifications?pageSize=50") {
        return Promise.resolve({
          data: {
            items: [
              {
                notificationId: "notif-1",
                type: "SlotCreated",
                message: "New opening in AAA",
                createdUtc: "2026-03-20T12:00:00Z",
                isRead: false,
                link: "calendar",
              },
            ],
            continuationToken: null,
          },
        });
      }
      if (url === "/api/notifications/notif-1/read" && options?.method === "PATCH") {
        return Promise.resolve({ data: { success: true } });
      }
      throw new Error(`Unexpected apiFetch call: ${url}`);
    });
  });

  it("supports keyboard activation for unread notifications", async () => {
    render(<NotificationCenterPage leagueId="league-1" />);

    const notificationButton = await screen.findByRole("button", {
      name: /notification: new opening in aaa/i,
    });

    expect(notificationButton).toHaveAttribute("tabindex", "0");

    fireEvent.keyDown(notificationButton, { key: "Enter" });

    await waitFor(() => {
      expect(api.apiFetch).toHaveBeenCalledWith("/api/notifications/notif-1/read", { method: "PATCH" });
    });
    expect(window.location.hash).toBe("#calendar");
  });
});
