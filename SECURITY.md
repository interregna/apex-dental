# Security

The web port is an experimental development preview. Known limitations include
process-global upstream user context, memory-only sessions and transcription
drafts, incomplete session lifecycle hardening, and no production deployment
security review. Loopback binding is the documented development default.

Do not use real patient data or expose the development server to the Internet.
Local Whisper is the default transcription engine. Selecting
`TRANSCRIPTION_ENGINE=service` transfers audio to that service's operator, and
the dictation screens say so. The transcription service requires HTTPS, including
for loopback addresses. Local Whisper may use loopback HTTP. Other optional
service clients permit loopback HTTP for development. Service requests do not
follow redirects.

Report vulnerabilities privately to the repository maintainer through GitHub
private vulnerability reporting when enabled. Do not post credentials, patient
information, or exploit data containing private records in public issues. There
is no production support SLA for this preview.
