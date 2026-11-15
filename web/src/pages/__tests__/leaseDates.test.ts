import { describe, expect, it } from "vitest";
import { addYears } from "../LeaseWizard";

describe("lease term shortcuts", () => {
  it("ends the day before the anniversary", () => {
    expect(addYears("2026-12-01", 1)).toBe("2027-11-30");
    expect(addYears("2026-01-01", 3)).toBe("2028-12-31");
  });
  it("handles a leap-day start", () => {
    expect(addYears("2028-03-01", 1)).toBe("2029-02-28");
  });
});
