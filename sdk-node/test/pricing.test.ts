import { describe, expect, it } from "vitest";
import { calculateCost } from "../src/pricing/calculateCost.js";
import { getAdapter } from "../src/providers/registry.js";
import { OpenAiAdapter } from "../src/providers/openai/OpenAiAdapter.js";

describe("calculateCost", () => {
  it("calcula custo de gpt-4o-mini por milhao de tokens", () => {
    // 1000 * 0.15/1M + 500 * 0.60/1M
    expect(calculateCost("openai", "gpt-4o-mini", 1000, 500)).toBeCloseTo(0.00045, 12);
  });

  it("calcula custo de gpt-4o", () => {
    // 1M * 2.5/1M + 1M * 10/1M
    expect(calculateCost("openai", "gpt-4o", 1_000_000, 1_000_000)).toBeCloseTo(12.5, 10);
  });

  it("ignora caixa de provedor e modelo", () => {
    expect(calculateCost("OpenAI", "GPT-4o-Mini", 1000, 500)).toBeCloseTo(0.00045, 12);
  });

  it("retorna null (nao zero) para modelo fora da tabela", () => {
    expect(calculateCost("openai", "gpt-9-ultra", 1000, 500)).toBeNull();
  });

  it("retorna null para provedor fora da tabela", () => {
    expect(calculateCost("anthropic", "claude-sonnet-5", 1000, 500)).toBeNull();
  });

  it("zero tokens em modelo conhecido custa zero, nao null", () => {
    expect(calculateCost("openai", "gpt-4o", 0, 0)).toBe(0);
  });
});

describe("registry", () => {
  it("resolve o adapter da OpenAI independente de caixa", () => {
    expect(getAdapter("openai")).toBeInstanceOf(OpenAiAdapter);
    expect(getAdapter("OpenAI")).toBeInstanceOf(OpenAiAdapter);
  });

  it("lanca erro claro para provedor nao suportado", () => {
    expect(() => getAdapter("gemini")).toThrow('Provedor "gemini" não é suportado pelo SDK.');
  });
});
