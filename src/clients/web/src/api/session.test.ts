import { beforeEach, describe, expect, it, vi } from "vitest";
import { getBearerToken } from "./rest";
import { clearSession, getAccessToken, login } from "./session";

describe("session", () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
    vi.stubEnv("VITE_BEARER_TOKEN", "");
    clearSession();
  });

  it("starts empty when the build has no bearer", () => {
    expectEmptyBuildToken();
    expect(getAccessToken()).toBe("");
  });

  it("reads a token stored in memory", async () => {
    expectEmptyBuildToken();
    vi.stubGlobal("fetch", async () => jsonResponse({ accessToken: "memory-token" }));

    await login("correct-horse-battery", "laptop");

    expect(getAccessToken()).toBe("memory-token");
  });

  it("clear leaves memory empty", async () => {
    expectEmptyBuildToken();
    vi.stubGlobal("fetch", async () => jsonResponse({ accessToken: "memory-token" }));
    await login("correct-horse-battery", "laptop");

    clearSession();

    expect(getAccessToken()).toBe("");
  });
});

function expectEmptyBuildToken(): void {
  if (getBearerToken()) {
    throw new Error("The test expected an empty VITE_BEARER_TOKEN.");
  }
}

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}
