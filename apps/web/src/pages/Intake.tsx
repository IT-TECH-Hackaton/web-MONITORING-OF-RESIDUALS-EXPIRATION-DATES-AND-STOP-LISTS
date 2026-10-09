import type { QrIntakeResult } from "@repo/contracts";
import { Html5Qrcode } from "html5-qrcode";
import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import QRCode from "qrcode";
import { useSession } from "../session";
import { ErrorText, Panel } from "../ui";

export function IntakePage() {
  const { client } = useSession();
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const scannerRef = useRef<Html5Qrcode | null>(null);
  const [payload, setPayload] = useState<string | null>(null);
  const [scanning, setScanning] = useState(false);
  const [result, setResult] = useState<QrIntakeResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  useEffect(() => {
    if (!payload || !canvasRef.current) return;
    void QRCode.toCanvas(canvasRef.current, payload, { width: 280, margin: 1 });
  }, [payload]);

  useEffect(() => () => {
    void stopCamera();
  }, []);

  async function stopCamera() {
    const scanner = scannerRef.current;
    scannerRef.current = null;
    if (!scanner) return;
    try {
      if (scanner.isScanning) await scanner.stop();
      scanner.clear();
    } catch {
      scanner.clear();
    }
    setScanning(false);
  }

  async function loadSample(valid: boolean) {
    setError(null);
    setResult(null);
    const sample = await client.qrSample(valid);
    setPayload(sample.payload);
  }

  async function accept(raw: string) {
    setPending(true);
    setError(null);
    try {
      setResult(await client.scanDelivery(raw));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Сверка не выполнена");
    } finally {
      setPending(false);
    }
  }

  async function startCamera() {
    setError(null);
    await stopCamera();
    const scanner = new Html5Qrcode("qr-reader");
    scannerRef.current = scanner;
    setScanning(true);
    try {
      await scanner.start(
        { facingMode: "environment" },
        { fps: 8, qrbox: 240 },
        (text) => {
          setPayload(text);
          void stopCamera();
          void accept(text);
        },
        () => undefined,
      );
    } catch (caught) {
      setScanning(false);
      setError(caught instanceof Error ? caught.message : "Камера недоступна");
    }
  }

  async function onFile(file: File | undefined) {
    if (!file) return;
    setError(null);
    const scanner = new Html5Qrcode("qr-file");
    try {
      const text = await scanner.scanFile(file, true);
      setPayload(text);
      await accept(text);
    } catch {
      setError("На изображении не найден QR поставки.");
    } finally {
      scanner.clear();
    }
  }

  return (
    <>
      <div className="page-title">
        <div>
          <h1>Приёмка по QR</h1>
          <p>Сканирование накладной сверяет сертификат качества. Просроченный или пустой сертификат партию на склад не пускает.</p>
        </div>
      </div>
      <div className="split">
        <Panel title="Код поставки">
          <div className="btn-row">
            <button className="btn" onClick={() => void loadSample(true)}>Годный сертификат</button>
            <button className="btn-ghost" onClick={() => void loadSample(false)}>Просроченный сертификат</button>
          </div>
          {payload ? <canvas ref={canvasRef} style={{ marginTop: 16 }} /> : <p className="hint">Сначала покажите образец или отсканируйте код поставщика.</p>}
          {payload ? (
            <div className="btn-row" style={{ marginTop: 12 }}>
              <button className="btn" disabled={pending} onClick={() => void accept(payload)}>Сверить и принять</button>
            </div>
          ) : null}
        </Panel>
        <Panel title="Сканер">
          <div className="btn-row">
            <button className="btn" onClick={() => void (scanning ? stopCamera() : startCamera())}>{scanning ? "Остановить камеру" : "Сканировать камерой"}</button>
            <label className="btn-ghost">
              Файл QR
              <input type="file" accept="image/*" hidden onChange={(event) => void onFile(event.target.files?.[0])} />
            </label>
          </div>
          <div id="qr-reader" style={{ marginTop: 12 }} />
          <div id="qr-file" hidden />
          {error ? <ErrorText error={error} /> : null}
          {result ? (
            <div className="stack" style={{ marginTop: 12 }}>
              <p className={result.accepted ? "hint" : "error-text"}>{result.summary}</p>
              {result.lines.map((line) => (
                <article key={`${line.productName}-${line.certificateNumber}`}>
                  <strong>{line.productName}</strong>
                  <p className="reason">{line.ok ? `Сертификат ${line.certificateNumber} подтверждён` : line.reason}</p>
                </article>
              ))}
              {result.delivery ? <Link to={`/deliveries/${result.delivery.id}`}>Открыть поставку</Link> : null}
            </div>
          ) : null}
        </Panel>
      </div>
    </>
  );
}
