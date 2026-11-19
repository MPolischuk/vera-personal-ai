import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const avatarDir = path.dirname(fileURLToPath(import.meta.url));

const forbidden = /from\s+['"][^'"]*(\/api\/|features\/voice)/;

function collectSourceFiles(dir: string): string[] {
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  const files: string[] = [];
  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      files.push(...collectSourceFiles(full));
      continue;
    }
    if (/\.(ts|tsx)$/.test(entry.name) && !entry.name.endsWith(".test.ts")) {
      files.push(full);
    }
  }
  return files;
}

describe("avatar module isolation (G6)", () => {
  it("does not import api/ or features/voice", () => {
    const sources = collectSourceFiles(avatarDir);
    expect(sources.length).toBeGreaterThan(0);

    const violations: string[] = [];
    for (const file of sources) {
      const text = fs.readFileSync(file, "utf8");
      if (forbidden.test(text)) {
        violations.push(path.relative(avatarDir, file));
      }
    }

    expect(violations).toEqual([]);
  });
});
