# Apex Dental — community practice software

<!-- Modified by Apex Dental Systems, 2026-10-25; see NOTICE. -->

An experimental Linux/.NET 8 business-layer port and browser interface derived
from Open Dental 24.3.63.0. Includes practice-local scheduling, patient/family
workflows, charting, billing, reporting, and local voice dictation with clinician
review before filing. This is an independent fork, not an upstream release.

Dentists may opt in to share practice data with an optional external analytics
service under an explicit data-sharing agreement. The intended benefits are
aggregated fee and reimbursement benchmarks, insight into payer denials and
payment delays, and better evidence for each practice's independent decisions.
Shared benchmark results are intended to be de-identified and aggregated.
Participation is optional; the software and local dictation remain usable without
sharing data. Data contribution and benchmarking are planned capabilities and
are not implemented in this preview.

The web port is a development preview. Authentication still uses a process-global
upstream user context, and drafts and sessions are memory-only. Use synthetic
data in an isolated development environment. See [SECURITY.md](SECURITY.md).

## Build

The `global.json` file selects an installed .NET 8 SDK; the business core uses
C# 12 so newer compiler keywords cannot change inherited source semantics.
On Linux, install the .NET 8 SDK and Python 3. Runtime PDF/image support also
requires `libgdiplus`, `fontconfig`, and `fonts-liberation`.

```sh
python3 tools/restore_dependencies.py
python3 tools/publication/check_boundary.py
dotnet build OpenDentalWebDemo/OpenDentalWebDemo.csproj -c Release
```

The dependency restore downloads checksum-pinned upstream DLLs into the ignored
`Required dlls/` directory. No binaries or database dumps are distributed in this
repository. See [dependencies and attribution](docs/DEPENDENCIES.md).

## Run with a development database

Supply your own compatible **synthetic** Open Dental database. The web app expects
a schema matching 24.3.63.0 and existing user/security configuration; it does not
provision a database. The optional harness can upgrade a schema and writes test
records, so run it only against a disposable copy.

```sh
export OD_DB_SERVER=127.0.0.1
export OD_DB_NAME=opendental
export OD_DB_USER=your_database_user
read -r -s -p 'Database password: ' OD_DB_PASSWORD; echo
export OD_DB_PASSWORD
dotnet run --project OpenDentalWebDemo --no-launch-profile --urls http://127.0.0.1:5080
```

Sign in with an existing database account. There is no bundled admin password.
Demo persona seeding is disabled by default and never resets the Admin account;
see [.env.example](.env.example) for opt-in configuration. Environment variables
must be set by your shell or service manager; the app does not load `.env` files.

Dictation uses either a local Whisper engine (the default; audio stays on your
server, see [the Whisper setup](tools/dictation/README.md)) or, if you choose, an
external transcription API that receives the audio. See
[transcription engines](docs/TRANSCRIPTION-API.md).
Other optional integrations use the [practice service contract](docs/SERVICE-API.md).

## Source and licensing

The inherited application source retains its original notices and the upstream
[GPL version 2 license](LICENSE). See [NOTICE](NOTICE) for provenance and changes.
Third-party inputs retain their own licenses; this repository does not grant rights
to those components or to upstream trademarks. Binary releases require a separate
dependency/license and corresponding-source review. This repository publishes
source, not a production installer or a clinical safety certification.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the public/private boundary and checks.


The [public site source](site/apex-dental.html) uses the same preview status and
optional data-sharing description as this repository.
