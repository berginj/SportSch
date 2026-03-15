import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import NotificationDropdown from "../../components/NotificationDropdown";

describe("NotificationDropdown", () => {
  it("renders each notification row as an accessible button", () => {
    const onMarkAsRead = vi.fn();

    render(
      <NotificationDropdown
        notifications={[
          {
            notificationId: "n-1",
            message: "Schedule updated",
            isRead: false,
            createdUtc: "2026-03-14T12:00:00Z",
          },
        ]}
        loading={false}
        error={null}
        unreadCount={1}
        onMarkAsRead={onMarkAsRead}
        onMarkAllAsRead={() => {}}
        onClose={() => {}}
        onRefresh={() => {}}
      />
    );

    const notificationButton = screen.getByRole("button", { name: /schedule updated/i });
    fireEvent.click(notificationButton);

    expect(onMarkAsRead).toHaveBeenCalledWith("n-1");
  });
});
