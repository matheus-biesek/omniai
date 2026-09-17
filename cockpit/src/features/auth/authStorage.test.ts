import { afterEach, describe, expect, it, vi } from "vitest";
import { clearToken, readToken, writeToken } from "./authStorage";

function memoryStorage(): Storage {
  const data = new Map<string, string>();
  return {
    get length() {
      return data.size;
    },
    clear: () => data.clear(),
    getItem: (k) => data.get(k) ?? null,
    key: (i) => [...data.keys()][i] ?? null,
    removeItem: (k) => void data.delete(k),
    setItem: (k, v) => void data.set(k, String(v)),
  };
}

describe("authStorage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("grava, le e limpa o token no sessionStorage", () => {
    const storage = memoryStorage();
    vi.stubGlobal("sessionStorage", storage);

    expect(readToken()).toBeNull();
    writeToken("jwt-abc");
    expect(readToken()).toBe("jwt-abc");
    expect(storage.getItem("omniai_token")).toBe("jwt-abc");
    clearToken();
    expect(readToken()).toBeNull();
  });

  it("nao quebra quando sessionStorage esta bloqueado", () => {
    const blocked = memoryStorage();
    const boom = () => {
      throw new DOMException("bloqueado", "SecurityError");
    };
    blocked.getItem = boom;
    blocked.setItem = boom;
    blocked.removeItem = boom;
    vi.stubGlobal("sessionStorage", blocked);

    expect(() => writeToken("x")).not.toThrow();
    expect(readToken()).toBeNull();
    expect(() => clearToken()).not.toThrow();
  });
});
