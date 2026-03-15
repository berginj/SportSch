import { useState } from "react";
import { describe, it, expect, beforeEach, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import AccessPage from "../../pages/AccessPage";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

describe("AccessPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();

    api.apiFetch.mockImplementation((path, options = {}) => {
      const url = String(path || "");

      if (url === "/api/leagues") {
        return Promise.resolve([{ leagueId: "LEAGUE-1", name: "League One" }]);
      }

      if (url === "/api/accessrequests/mine") {
        return Promise.resolve([]);
      }

      if (url === "/api/accessrequests" && options.method === "POST") {
        return Promise.resolve({ ok: true });
      }

      throw new Error(`Unexpected apiFetch call: ${url}`);
    });
  });

  it("requires an explicit submit action before creating an access request", async () => {
    function TestHarness() {
      const [leagueId, setLeagueId] = useState("");
      return (
        <AccessPage
          me={{ userId: "user-1", email: "user@example.com" }}
          leagueId={leagueId}
          setLeagueId={setLeagueId}
        />
      );
    }

    render(<TestHarness />);

    await waitFor(() => expect(screen.getByText("Request access")).toBeInTheDocument());

    const postCallsBeforeSubmit = api.apiFetch.mock.calls.filter(
      ([path, options]) => path === "/api/accessrequests" && options?.method === "POST"
    );
    expect(postCallsBeforeSubmit).toHaveLength(0);

    fireEvent.change(screen.getByLabelText("League"), {
      target: { value: "LEAGUE-1" },
    });
    fireEvent.click(screen.getByRole("button", { name: /submit request/i }));

    await waitFor(() => {
      const postCalls = api.apiFetch.mock.calls.filter(
        ([path, options]) => path === "/api/accessrequests" && options?.method === "POST"
      );
      expect(postCalls).toHaveLength(1);
    });
  });
});
