# Transcription engines

Dictation uses exactly one engine, selected by `TRANSCRIPTION_ENGINE`:

| Engine | Setting | Where audio goes | Requires |
| --- | --- | --- | --- |
| Local (default) | `TRANSCRIPTION_ENGINE=local` or unset | Whisper sidecar on this host; audio does not leave it | `WHISPER_URL` (loopback only; default `http://127.0.0.1:8771`) |
| External service | `TRANSCRIPTION_ENGINE=service` | An external transcription API, such as a paid service; audio leaves this host | `TRANSCRIPTION_SERVICE_URL` (HTTPS) and `TRANSCRIPTION_SERVICE_TOKEN` |

There is no fallback between engines. The app refuses to start dictation with an
ambiguous configuration: a service URL without `TRANSCRIPTION_ENGINE=service`,
a service engine without a URL or token, a non-loopback `WHISPER_URL`, or an
unknown engine name. Service URLs must use HTTPS, even on loopback; only the
local engine may use loopback HTTP. The dictation screens show which engine is
active, based on this configuration, and say when audio is sent off-host. No vendor SDK runs in the
practice process.

Both engines implement the API below. The local sidecar is described in
[the Whisper setup](../tools/dictation/README.md). The service token is sent as a
Bearer token on every request to the external service and never to the local engine.

- `GET /health` returns JSON such as `{"ok":true,"model":"local"}`.
- `POST /transcribe?task=transcribe|translate&format=<URL-encoded MIME type>`
  receives raw audio with `Content-Type: application/octet-stream`.
- Success returns `{"ok":true,"text":"Example transcript","language":"en","duration":1.2}`.
- Failure returns `{"ok":false,"error":"Description"}` and an error status.

The app applies a 180-second request timeout and does not follow redirects.
The service must enforce its own authentication, request limits, retention, and
supported task/format validation. An external service receives audio; enable it
only with appropriate operator authorization and service arrangements. Transcripts
remain drafts until the user reviews and accepts them through the normal app flow.

The application's `/api/dictation/health` response adds `engine` (`local` or
`service`), `offHost`, and the configured service `host` (null for local), all
computed from startup configuration rather than the engine's response. Recording
remains disabled until this status is known and the engine reports ready. Browser
errors do not echo remote error bodies or network exception details.
