import { describe, expect, it } from "vitest";
import { date, label, money } from "../format";

describe("money", () => {
  it("formats KES and USD the way the spec shows them", () => {
    expect(money(45000)).toBe("KES 45,000.00");
    expect(money(4410, "USD")).toBe("USD 4,410.00");
    expect(money(4862.025, "KES")).toBe("KES 4,862.03");
  });
  it("shows a dash for missing values", () => {
    expect(money(null)).toBe("—");
  });
});

describe("date", () => {
  it("shows plain dates as Nairobi calendar dates", () => {
    expect(date("2026-11-01")).toBe("01 Nov 2026");
  });
  it("converts timestamps to Nairobi time", () => {
    expect(date("2026-11-15T22:30:00Z")).toBe("16 Nov 2026");
  });
});

describe("label", () => {
  it("humanises enum values", () => {
    expect(label("off_market")).toBe("Off market");
    expect(label("2br")).toBe("2 BR");
  });
});
