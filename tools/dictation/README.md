# Dictation (voice-to-note) — local Whisper sidecar

Staged, non-production. Records clinical audio in the browser, transcribes/translates
it **on this host** with a local [faster-whisper](https://github.com/SYSTRAN/faster-whisper)
model, and runs a human-in-the-loop review (transcribe → editable draft → accept) before
anything is filed. With this local engine (`TRANSCRIPTION_ENGINE=local`, the default),
audio never leaves the box and no third party receives it, so no BAA is required for
the transcription itself. The alternative external engine is described in
[transcription engines](../../docs/TRANSCRIPTION-API.md).

## Pieces
- `whisper_sidecar.py` — loopback-only HTTP server (127.0.0.1:8771) holding one Whisper
  model in memory. `POST /transcribe?task=transcribe|translate` (raw audio) → JSON text.
  Each clip is decoded and deleted immediately; nothing is persisted here.
- `odwhisper.service` — systemd unit to keep it running.
- Backend: `OpenDentalWebDemo/Modules/DictationModule.cs` (`/api/dictation/*`).
  Server-side drafts (in memory, keyed by patient+user), accept writes through the real
  Commlogs / PatientNotes / Procedures business layer with SecurityLogs audit entries.
- Front end: `OpenDentalWebDemo/wwwroot/modules/clinical-dictation.js` (Clinical ▸ Dictation).

## Setup (Linux host)
```
sudo apt-get install -y python3-venv
python3 -m venv ~/whisper-venv
~/whisper-venv/bin/pip install --require-hashes --only-binary=:all: -r tools/dictation/requirements.txt
# One-time model provisioning (network access required, no patient audio):
~/whisper-venv/bin/python -c 'from faster_whisper import WhisperModel; WhisperModel("base", device="cpu", compute_type="int8")'
cp whisper_sidecar.py ~/whisper_sidecar.py
sudo cp odwhisper.service /etc/systemd/system/   # edit User= and paths
sudo systemctl daemon-reload && sudo systemctl enable --now odwhisper
curl http://127.0.0.1:8771/health
```
Model (`WHISPER_MODEL`) is multilingual `base` so the `translate` task can turn non-English
speech directly into English text. Swap to `small`/`medium` for accuracy, `*.en` for
English-only (disables translate). The app finds the sidecar at `WHISPER_URL`
(default `http://127.0.0.1:8771`).

## Before production (not done here)
TLS at the origin (Secure cookies are set on HTTPS requests); encrypt drafts at rest (or persist to an encrypted
store instead of memory); per-user permission already gated (ChartModule); consent-to-record
notice; retention/purge policy; formal HIPAA risk assessment. If you select the external
engine (`TRANSCRIPTION_ENGINE=service`), that service receives patient audio and needs
a BAA and your practice's authorization.

The Python 3.12/Linux dependency snapshot is hash-locked and checked by CI with
`pip-audit`. Refresh it through the reviewed process in
[dependencies](../../docs/DEPENDENCIES.md). The sidecar limits audio to 16 MiB,
allows one inference at a time, bounds HTTP workers to eight and times out idle
connections. Its systemd unit drops capabilities, restricts filesystem writes,
limits CPU/memory, permits only loopback IP traffic, and disables model-hub
telemetry after provisioning. The IP filter requires host systemd/cgroup support;
verify the deployed unit on the target host.
Temporary audio is written to a private temporary directory and removed after
processing. The unit requires its model cache to be provisioned under the same
service account before it starts.
