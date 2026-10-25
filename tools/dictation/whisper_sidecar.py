#!/usr/bin/env python3
"""Local Whisper sidecar for the Open Dental web demo.

Holds one faster-whisper model in memory and exposes a tiny loopback-only HTTP
endpoint so the .NET app can transcribe/translate dictation audio WITHOUT any
audio ever leaving this host. PHI stays on-box: no cloud STT, no third party.

  POST /transcribe?task=transcribe|translate   body: raw audio bytes (webm/opus,
       wav, m4a, …). Returns JSON {ok, text, language, duration, task}.
  GET  /health                                  -> {ok, model}

Bound to 127.0.0.1 only. Model + compute type come from env (WHISPER_MODEL,
WHISPER_COMPUTE). Multilingual model by default so task=translate (any language
speech -> English text) works.
"""
import json, os, tempfile, sys, threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs
from faster_whisper import WhisperModel

MODEL_NAME = os.environ.get("WHISPER_MODEL", "base")
COMPUTE    = os.environ.get("WHISPER_COMPUTE", "int8")
PORT       = int(os.environ.get("WHISPER_PORT", "8771"))
MAX_AUDIO_BYTES = 16 * 1024 * 1024
MODEL_SLOT = threading.BoundedSemaphore(1)

class LimitedServer(ThreadingHTTPServer):
    daemon_threads = True
    request_queue_size = 8
    def __init__(self, *args, **kwargs):
        self.slots = threading.BoundedSemaphore(8)
        super().__init__(*args, **kwargs)

    def get_request(self):
        connection, address = super().get_request()
        connection.settimeout(10)
        return connection, address

    def process_request(self, connection, address):
        if not self.slots.acquire(blocking=False):
            self.shutdown_request(connection)
            return
        try:
            super().process_request(connection, address)
        except BaseException:
            self.slots.release()
            raise

    def process_request_thread(self, connection, address):
        try:
            super().process_request_thread(connection, address)
        finally:
            self.slots.release()


print(f"[whisper] loading model={MODEL_NAME} compute={COMPUTE} …", flush=True)
MODEL = WhisperModel(MODEL_NAME, device="cpu", compute_type=COMPUTE)
print("[whisper] model ready", flush=True)

class H(BaseHTTPRequestHandler):
    def _send(self, code, obj):
        body = json.dumps(obj).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *a):  # keep the journal quiet
        pass

    def do_GET(self):
        if urlparse(self.path).path == "/health":
            self._send(200, {"ok": True, "model": MODEL_NAME, "compute": COMPUTE})
        else:
            self._send(404, {"ok": False, "error": "not found"})

    def do_POST(self):
        u = urlparse(self.path)
        if u.path != "/transcribe":
            self._send(404, {"ok": False, "error": "not found"}); return
        task = (parse_qs(u.query).get("task", ["transcribe"])[0]).lower()
        if task not in ("transcribe", "translate"):
            task = "transcribe"
        if self.headers.get("Transfer-Encoding"):
            self._send(400, {"ok": False, "error": "content length required"}); return
        try:
            n = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            self._send(400, {"ok": False, "error": "invalid content length"}); return
        if n <= 0:
            self._send(400, {"ok": False, "error": "empty body"}); return
        if n > MAX_AUDIO_BYTES:
            self._send(413, {"ok": False, "error": "audio clip too large"}); return
        if not MODEL_SLOT.acquire(blocking=False):
            self._send(429, {"ok": False, "error": "transcriber busy"}); return
        tmp = None
        try:
            data = self.rfile.read(n)
            if len(data) != n:
                self._send(400, {"ok": False, "error": "incomplete audio"}); return
            tmp = tempfile.NamedTemporaryFile(suffix=".audio", delete=False)
            tmp.write(data); tmp.flush(); tmp.close()
            segments, info = MODEL.transcribe(tmp.name, task=task, beam_size=1,
                                              vad_filter=True)
            text = "".join(s.text for s in segments).strip()
            if not text:
                # VAD can strip quiet/short phone audio to nothing — retry without it
                # before giving up, so real speech isn't silently discarded.
                segments, info = MODEL.transcribe(tmp.name, task=task, beam_size=1,
                                                  vad_filter=False)
                text = "".join(s.text for s in segments).strip()
            dur = round(getattr(info, "duration", 0.0), 2)
            print(f"[whisper] {task} bytes={len(data)} dur={dur}s chars={len(text)}", flush=True)
            self._send(200, {"ok": True, "text": text,
                             "language": getattr(info, "language", ""),
                             "duration": dur,
                             "task": task})
        except Exception:
            self._send(500, {"ok": False, "error": "transcription failed"})
        finally:
            if tmp is not None:
                tmp.close()
                try: os.unlink(tmp.name)
                except OSError: pass
            MODEL_SLOT.release()

if __name__ == "__main__":
    srv = LimitedServer(("127.0.0.1", PORT), H)
    print(f"[whisper] listening on 127.0.0.1:{PORT}", flush=True)
    srv.serve_forever()
