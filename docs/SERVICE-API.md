# Optional practice service contract

Practice-local workflows work independently of external services. Dentists may
separately opt in to share practice data under an explicit data-sharing agreement
for planned aggregated benchmarks and insights into reimbursement and payer
performance. Data contribution and benchmarking are not implemented in this
preview. Configuring a service connection below does not enroll a practice in
data contribution or authorize reuse of its data for analytics.

This document describes optional HTTP service clients. Provider implementations
and operating infrastructure are maintained separately.

Each capability is disabled by default. Enable and configure it independently:

| Capability | Explicit enablement | Service URL | Scoped token |
| --- | --- | --- | --- |
| Mail | `APEX_MAIL_ENABLED=true` | `APEX_MAIL_URL` | `APEX_MAIL_TOKEN` |
| Document storage | `APEX_STORAGE_ENABLED=true` | `APEX_STORAGE_URL` | `APEX_STORAGE_TOKEN` |
| Collections file transfers | `APEX_TRANSFER_ENABLED=true` | `APEX_TRANSFER_URL` | `APEX_TRANSFER_TOKEN` |
| Tax computation | `APEX_TAX_ENABLED=true` | `APEX_TAX_URL` | `APEX_TAX_TOKEN` |

Enabling mail never enables storage, collections transfers, tax, or transcription.
Setting a URL or token alone does not enable a capability. Each enabled capability
requires its own URL and token settings; use distinct tokens scoped to the practice
and capability. The former shared URL/token settings are not read or used as a fallback.
A common server may be configured explicitly for multiple capabilities, but enabling
one never supplies another's configuration or consent.

The base URL requires HTTPS, except for loopback development. Requests do not
follow redirects and have a 180-second timeout. No default external host or automatic
service enrollment exists. Provider account credentials and OAuth lifecycle belong
at the service; existing provider-specific account/token fields are not sent.

Clients POST JSON to `<base>/v1/<capability>/<operation>`. Responses are plain JSON
values matching the operation below. Non-success statuses fail the operation;
error response bodies are not copied into local errors. Services must authenticate
and isolate practices and authorize each operation. The token must be scoped to
one practice; the public client has no network-wide query or analytics implementation.

| Operation | Request | Response |
| --- | --- | --- |
| `mail/validate` | `account` (mailbox address) | JSON object |
| `mail/send` | `account`, `rawMime` (MIME including locally loaded attachments) | JSON object |
| `mail/receive` | `account`, `query` | Array of `{id, rawMime}` |
| `storage/upload` | `folder`, `fileName`, `data` (base64) | JSON object |
| `storage/download`, `storage/thumbnail` | `folder`, `fileName` | Base64 JSON string |
| `storage/delete`, `storage/exists` | `path` | JSON object / boolean |
| `storage/move`, `storage/copy` | `fromPath`, `toPath` | Boolean / JSON object |
| `storage/list` | `folderPath` | Array of path strings |
| `transfer/validate` | `connection`, `clinicNum` | Boolean |
| `transfer/upload` | `connection`, `clinicNum`, `fileName`, `data` (base64) | JSON object |
| `tax/health` | Empty object | Boolean |
| `tax/estimate` | `code`, `amount`, `postalCode`, `region` | Decimal number |

Mail reception and record updates remain local. The service returns raw MIME;
the app records the message UID after successful local processing so a failed
message can be retried. Service-returned IDs must be stable for that mailbox.
Local SMTP and local/database document storage do not use this HTTP service.
The remote storage root can be configured with `APEX_STORAGE_ROOT`.

Legacy cloud mailbox and document-store installations require a separately
implemented service conforming to this contract before those integrations can be
used. No such service is bundled here. Legacy vendor authentication entry points
and tax prepayment fail explicitly pending migration; do not rely on them in this
preview. Legacy tax computation must also be migrated to the optional service.

Service requests can contain mail, documents, or other sensitive practice data.
Operators must configure and authorize the service before enabling an integration.
Local dictation and optional external transcription retain their separate
[transcription contract](TRANSCRIPTION-API.md).
