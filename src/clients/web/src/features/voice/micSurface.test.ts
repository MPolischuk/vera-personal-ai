import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const voiceDir = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(voiceDir, "../../..");

const skipDirs = new Set([
  "node_modules",
  "dist",
  "dist-ssr",
  "coverage",
  "certs",
]);

/** Call expression. A regex literal keeps the escaped dot out of a plain-text search. */
const mediaDevicesGetUserMedia = /mediaDevices\.getUserMedia/;

const micButtonImport = /from\s+['"][^'"]*\/MicButton['"]/;

// Split so this file is not itself a hit for the voice-directory guard.
const shellImport = "shell" + "/";
const phoneFlag = "phone" + "Shell";

function collectSourceFiles(dir: string): string[] {
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  const files: string[] = [];
  for (const entry of entries) {
    if (entry.isDirectory()) {
      if (skipDirs.has(entry.name) || entry.name.startsWith(".")) {
        continue;
      }
      files.push(...collectSourceFiles(path.join(dir, entry.name)));
      continue;
    }
    if (!/\.(ts|tsx)$/.test(entry.name) || entry.name.endsWith(".test.ts")) {
      continue;
    }
    files.push(path.join(dir, entry.name));
  }
  return files;
}

function relativeHits(files: string[], matches: (text: string) => boolean): string[] {
  return files
    .filter((file) => matches(fs.readFileSync(file, "utf8")))
    .map((file) => path.relative(webRoot, file))
    .sort();
}

describe("microphone surface (PT-51)", () => {
  const sources = collectSourceFiles(webRoot);

  it("keeps the capture call site in micCapture.ts only", () => {
    expect(sources.length).toBeGreaterThan(0);
    expect(relativeHits(sources, (text) => mediaDevicesGetUserMedia.test(text))).toEqual([
      path.join("src", "features", "voice", "micCapture.ts"),
    ]);
  });

  it("imports MicButton only from ChatView and StageChrome", () => {
    expect(relativeHits(sources, (text) => micButtonImport.test(text))).toEqual(
      [
        path.join("src", "features", "chat", "ChatView.tsx"),
        path.join("src", "features", "chat", "StageChrome.tsx"),
      ].sort(),
    );
  });

  it("shows the stage mic only while the chat panel is hidden", () => {
    const stageChrome = fs.readFileSync(
      path.join(webRoot, "src", "features", "chat", "StageChrome.tsx"),
      "utf8",
    );
    expect(stageChrome).toContain("{!stageChatVisible && <MicButton />}");
  });

  it("mounts MicButton in the chat composer", () => {
    const chatView = fs.readFileSync(
      path.join(webRoot, "src", "features", "chat", "ChatView.tsx"),
      "utf8",
    );
    expect(chatView).toContain("<MicButton />");
  });

  it("freezes the three secure-context strings", () => {
    const micButton = fs.readFileSync(path.join(voiceDir, "MicButton.tsx"), "utf8");
    const micCapture = fs.readFileSync(path.join(voiceDir, "micCapture.ts"), "utf8");
    const voiceController = fs.readFileSync(
      path.join(voiceDir, "voiceController.ts"),
      "utf8",
    );

    expect(micButton).toContain(
      "El micrófono requiere un contexto seguro: usá http://localhost o HTTPS.",
    );
    expect(micCapture).toContain(
      "El micrófono requiere un contexto seguro (localhost o HTTPS).",
    );
    expect(voiceController).toContain(
      "El micrófono requiere un contexto seguro: abrí la app en localhost o con HTTPS (npm run dev:https).",
    );
  });

  it("publishes insecure_context before toggleRecording", () => {
    const micButton = fs.readFileSync(path.join(voiceDir, "MicButton.tsx"), "utf8");
    const codeAt = micButton.indexOf("insecure_context");
    const toggleAt = micButton.indexOf("toggleRecording(");
    expect(codeAt).toBeGreaterThanOrEqual(0);
    expect(toggleAt).toBeGreaterThan(codeAt);
  });

  it("does not import the shell or read the phone flag from voice", () => {
    const voiceSources = collectSourceFiles(voiceDir);
    expect(voiceSources.length).toBeGreaterThan(0);
    expect(
      relativeHits(
        voiceSources,
        (text) => text.includes(shellImport) || text.includes(phoneFlag),
      ),
    ).toEqual([]);
  });
});
