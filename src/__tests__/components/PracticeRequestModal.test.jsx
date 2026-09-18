import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import PracticeRequestModal from "../../components/PracticeRequestModal";
import { apiFetch } from "../../lib/api";

vi.mock("../../lib/api", () => ({ apiFetch: vi.fn() }));

describe("canonical calendar practice booking", () => {
  it("prefills on opening and submits only an inventory key with server policy", async () => {
    const onSuccess = vi.fn();
    apiFetch.mockImplementation(async (path) => {
      if (path.endsWith("/coach")) return { seasonLabel: "Fall 2026" };
      if (path.includes("availability/options")) return { options: [{ practiceSlotKey: "inventory-1", fieldName: "Field 1", startTime: "18:00", endTime: "19:00", isAvailable: true, bookingPolicy: "commissioner_review" }] };
      if (path.endsWith("/requests")) return { requests: [{ practiceSlotKey: "inventory-1", requestId: "request-1", status: "Pending" }] };
      throw new Error(`Unexpected path: ${path}`);
    });
    const props = { leagueId: "league-1", onClose: vi.fn(), onSuccess };
    const { rerender } = render(<PracticeRequestModal {...props} isOpen={false} />);
    rerender(<PracticeRequestModal {...props} isOpen initialData={{ date: "2026-10-01" }} />);
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByLabelText("Date")).toHaveValue("2026-10-01");
    await screen.findByRole("option", { name: /Field 1/ });
    fireEvent.change(screen.getByLabelText("Available field and time"), { target: { value: "inventory-1" } });
    fireEvent.click(screen.getByRole("button", { name: "Request practice" }));
    await waitFor(() => expect(onSuccess).toHaveBeenCalledWith(expect.objectContaining({ status: "Pending", autoApproved: false })));
    const call = apiFetch.mock.calls.find(([path]) => path === "/api/field-inventory/practice/requests");
    expect(JSON.parse(call[1].body)).toEqual({ seasonLabel: "Fall 2026", practiceSlotKey: "inventory-1", notes: "" });
    expect(apiFetch.mock.calls.some(([path]) => path === "/api/practice/requests")).toBe(false);
  });

  it("keeps submission unavailable when inventory cannot be checked", async () => {
    apiFetch.mockRejectedValue(new Error("Storage unavailable"));
    const close = vi.fn();
    render(<PracticeRequestModal isOpen leagueId="league-1" onClose={close} onSuccess={vi.fn()} />);
    await screen.findByRole("alert");
    expect(screen.getByRole("button", { name: "Request practice" })).toBeDisabled();
    fireEvent.keyDown(screen.getByRole("dialog"), { key: "Escape" });
    expect(close).toHaveBeenCalledOnce();
  });
});
