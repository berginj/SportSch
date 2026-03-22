import { describe, it, expect, vi, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import AdminPage from "../../pages/AdminPage";
import { apiFetch } from "../../lib/api";
import { usePromptDialog } from "../../lib/useDialogs";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

vi.mock("../../lib/telemetry", () => ({
  trackEvent: vi.fn(),
}));

vi.mock("../../components/Toast", () => ({
  default: () => null,
}));

vi.mock("../../components/Dialogs", () => ({
  PromptDialog: () => null,
}));

vi.mock("../../lib/useDialogs", () => ({
  usePromptDialog: vi.fn(),
}));

vi.mock("../../pages/AdminDashboard", () => ({
  default: () => <div>Dashboard section</div>,
}));

vi.mock("../../pages/admin/CoachAssignmentsSection", () => ({
  default: () => <div>Coach section</div>,
}));

vi.mock("../../pages/admin/CsvImportSection", () => ({
  default: () => <div>Import section</div>,
}));

vi.mock("../../pages/admin/GlobalAdminSection", () => ({
  default: () => <div>Global section</div>,
}));

vi.mock("../../pages/admin/AccessRequestsSection", () => ({
  default: ({ bulkApproveRequests, bulkDenyRequests }) => (
    <div>
      <button
        type="button"
        onClick={() =>
          bulkApproveRequests?.(
            [{ userId: "user-approve", leagueId: "league-1" }],
            "Coach",
            true
          )
        }
      >
        Trigger bulk approve
      </button>
      <button
        type="button"
        onClick={() =>
          bulkDenyRequests?.([{ userId: "user-deny", leagueId: "league-1" }], true)
        }
      >
        Trigger bulk deny
      </button>
    </div>
  ),
}));

const requestPromptMock = vi.fn();

describe("AdminPage bulk access actions", () => {
  function renderPage() {
    return render(
      <AdminPage
        me={{
          userId: "admin-1",
          email: "admin@example.com",
          memberships: [{ leagueId: "league-1", role: "LeagueAdmin" }],
          isGlobalAdmin: false,
        }}
        leagueId="league-1"
        setLeagueId={vi.fn()}
      />
    );
  }

  beforeEach(() => {
    vi.clearAllMocks();
    window.history.replaceState({}, "", "/#admin");
    requestPromptMock.mockResolvedValue("Not eligible");
    vi.mocked(usePromptDialog).mockReturnValue({
      promptState: null,
      promptValue: "",
      setPromptValue: vi.fn(),
      requestPrompt: requestPromptMock,
      handleConfirm: vi.fn(),
      handleCancel: vi.fn(),
    });
    vi.mocked(apiFetch).mockImplementation(async (url, options) => {
      if (url.startsWith("/api/accessrequests?")) return [];
      if (url === "/api/memberships") return [];
      if (url === "/api/divisions") return [];
      if (url === "/api/teams") return [];
      if (url === "/api/accessrequests/bulk") {
        const payload = JSON.parse(options?.body || "{}");
        const total = Array.isArray(payload?.items) ? payload.items.length : 0;
        return { action: payload.action, total, succeeded: total, failed: 0, results: [] };
      }
      return [];
    });
  });

  it("calls bulk approve endpoint once with selected items", async () => {
    renderPage();

    fireEvent.click(await screen.findByRole("button", { name: /access requests/i }));
    fireEvent.click(await screen.findByRole("button", { name: /trigger bulk approve/i }));

    await waitFor(() => {
      const bulkApproveCall = vi
        .mocked(apiFetch)
        .mock.calls.find(([url, options]) => {
          if (url !== "/api/accessrequests/bulk") return false;
          const payload = JSON.parse(options?.body || "{}");
          return payload.action === "approve";
        });
      expect(bulkApproveCall).toBeTruthy();
    });

    const bulkApproveCall = vi.mocked(apiFetch).mock.calls.find(([url, options]) => {
      if (url !== "/api/accessrequests/bulk") return false;
      const payload = JSON.parse(options?.body || "{}");
      return payload.action === "approve";
    });
    const payload = JSON.parse(bulkApproveCall[1].body);
    expect(payload).toEqual({
      action: "approve",
      items: [{ userId: "user-approve", leagueId: "league-1", role: "Coach" }],
    });
  });

  it("prompts once and calls bulk deny endpoint with shared reason", async () => {
    renderPage();

    fireEvent.click(await screen.findByRole("button", { name: /access requests/i }));
    fireEvent.click(await screen.findByRole("button", { name: /trigger bulk deny/i }));

    await waitFor(() => {
      expect(requestPromptMock).toHaveBeenCalledTimes(1);
    });

    await waitFor(() => {
      const bulkDenyCall = vi
        .mocked(apiFetch)
        .mock.calls.find(([url, options]) => {
          if (url !== "/api/accessrequests/bulk") return false;
          const payload = JSON.parse(options?.body || "{}");
          return payload.action === "deny";
        });
      expect(bulkDenyCall).toBeTruthy();
    });

    const bulkDenyCall = vi.mocked(apiFetch).mock.calls.find(([url, options]) => {
      if (url !== "/api/accessrequests/bulk") return false;
      const payload = JSON.parse(options?.body || "{}");
      return payload.action === "deny";
    });
    const payload = JSON.parse(bulkDenyCall[1].body);
    expect(payload).toEqual({
      action: "deny",
      items: [{ userId: "user-deny", leagueId: "league-1", reason: "Not eligible" }],
    });
  });

  it("keeps admin section changes in query state", async () => {
    const replaceStateSpy = vi.spyOn(window.history, "replaceState");
    renderPage();

    fireEvent.click(await screen.findByRole("button", { name: /coach assignments/i }));
    expect(await screen.findByText("Coach section")).toBeInTheDocument();
    expect(replaceStateSpy).toHaveBeenLastCalledWith(
      {},
      "",
      expect.stringContaining("adminSection=coaches")
    );
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.stringContaining("#admin"));

    fireEvent.click(screen.getByRole("button", { name: /csv import/i }));
    expect(await screen.findByText("Import section")).toBeInTheDocument();
    expect(replaceStateSpy).toHaveBeenLastCalledWith(
      {},
      "",
      expect.stringContaining("adminSection=import")
    );
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.stringContaining("#admin"));
  });
});
