/// <reference types="vite/client" />

declare module "vosk-browser" {
  export function createModel(modelUrl: string, logLevel?: number): Promise<VoskModel>;

  export type VoskModel = {
    KaldiRecognizer: new (sampleRate: number) => VoskRecognizer;
    terminate(): void;
  };

  export type VoskRecognizer = {
    on(event: "result", listener: (message: { result: { text: string } }) => void): void;
    on(event: "partialresult", listener: (message: { result: { partial: string } }) => void): void;
    acceptWaveform(buffer: AudioBuffer): void;
    retrieveFinalResult(): void;
    remove(): void;
  };
}

interface ImportMetaEnv {
  readonly VITE_API_URL: string;
}
