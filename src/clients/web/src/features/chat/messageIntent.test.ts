import { describe, expect, it } from "vitest";
import { classifySpeechText, classifyUserText, messageIntentCue } from "./messageIntent";

describe("classifyUserText", () => {
  it.each([
    ["Hola Vera!", "greeting"],
    ["Buen día, ¿cómo va?", "greeting"],
    ["Muchas gracias por todo", "thanks"],
    ["¡Aprobé el examen! 🎉", "goodNews"],
    ["Hoy estoy triste, tuve un problema", "sad"],
    ["jajaja qué bueno", "funny"],
    ["¿En serio? no te puedo creer", "surprise"],
    ["¿Cuánto es 2 + 2?", "message"],
  ])("%s → %s", (text, kind) => {
    expect(classifyUserText(text)).toBe(kind);
  });
});

describe("classifySpeechText", () => {
  it("detects lists, options, unknown and closing lines", () => {
    expect(classifySpeechText("Tres pasos:\n1. Abrí\n2. Editá\n3. Guardá").kind).toBe("list");
    expect(classifySpeechText("Podés usar Vite o bien Webpack.").kind).toBe("options");
    expect(classifySpeechText("La verdad, no sé cuál es.").kind).toBe("unknown");
    expect(classifySpeechText("Está ahí, a un costado.").kind).toBe("side");
    expect(classifySpeechText("Listo.\nCualquier cosa avisame.").closing).toBe(true);
    expect(classifySpeechText("Son las tres.")).toEqual({ kind: "none", closing: false });
  });
});

describe("messageIntentCue", () => {
  it("keys the cue by message id and role", () => {
    expect(messageIntentCue({ id: "7", role: "User", content: "gracias" })).toEqual({
      playId: "u:7", source: "user", kind: "thanks",
    });
    expect(messageIntentCue({ id: "8", role: "Assistant", content: "No sé." })?.playId).toBe("a:8");
    expect(messageIntentCue(undefined)).toBeNull();
    expect(messageIntentCue({ id: "9", role: "User", content: "  " })).toBeNull();
  });
});
