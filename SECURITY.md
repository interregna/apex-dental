# Security

The web port is an experimental development preview. Use synthetic data on an
isolated host and bind the server to loopback. Do not expose this preview to the
Internet or use real patient data. No production deployment security review has
been completed.

The current web build isolates user identity across asynchronous requests,
clears identity on request completion, enforces eight-hour server-side session
expiry, treats API paths case-insensitively, requires administrator permission for
unmapped APIs, rotates sessions on login, and uses random 256-bit session tokens.
Cookies are HttpOnly, SameSite=Strict and Secure when the request uses HTTPS.
Only a correctly configured trusted TLS proxy may terminate HTTPS upstream;
HTTP loopback development does not set Secure cookies. Login attempts share a
bounded limit of ten per minute, and cross-site browser writes identified by
Fetch Metadata are rejected. API responses use no-store; responses prohibit
framing and MIME sniffing. Request bodies are limited to 16 MiB. Scanner requests
accept only configured/discovered devices, prohibit HTTP redirects, and require
job addresses on the configured scanner origin and path. These controls
do not replace a complete authorization, CSRF, XSS or deployment audit.

Sessions and transcription drafts remain in memory. Sessions do not survive a
restart. Account, password and group changes need explicit session revocation or
a restart; persistent session management, automatic draft retention/purge and a
comprehensive production authorization review remain outstanding.

In-process desktop plug-ins and two obsolete SFTP transports are disabled in the
web build. Known NuGet advisories, including transitive ones, and audit-feed
failures fail restore. Python speech dependencies are hash-locked and audited;
CI runs daily and Dependabot proposes reviewed updates. Unsupported Linux drawing, the final MIT PDFsharp release,
and fourteen inherited binary inputs remain explicit exceptions in
[dependencies](docs/DEPENDENCIES.md). An advisory scan cannot clear these inputs.

Local Whisper is the default transcription engine. Selecting
`TRANSCRIPTION_ENGINE=service` transfers audio to that service's operator, and
the dictation screens say so. The transcription service requires HTTPS, including
for loopback addresses. Local Whisper may use loopback HTTP. Other optional
service clients permit loopback HTTP for development. Service requests do not
follow redirects. The local sidecar bounds request size/concurrency, sanitizes
errors and deletes temporary audio; provision the model before starting its
hardened offline service.

Report vulnerabilities privately to the repository maintainer through GitHub
private vulnerability reporting. Do not post credentials, patient information,
or exploit data containing private records in public issues. There is no
production support SLA for this preview.
