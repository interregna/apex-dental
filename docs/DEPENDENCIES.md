# External build inputs

Reviewed 2026-10-25 against official stable package feeds and the upstream public
GPL source branch. The source baseline is Open Dental 24.3.63.0 at
`d804c19546233593d8a66af0591ed118a9e2c794`, which remains the latest public GPL
branch tip checked. Newer non-public source is not incorporated.

The supported runtime moves to .NET 10 LTS (SDK 10.0.403, runtime 10.0.14).
Direct NuGet packages use the latest stable versions checked, except for the
Linux drawing and PDF licensing compatibility exceptions below. The resolved direct and transitive
NuGet graphs are recorded in each project's `packages.lock.json`; restores use
only nuget.org and CI enforces locked mode. Restore fails for known advisories
at any severity, or unavailable advisory data (NU1900–NU1905).

The old PKCS 8.0.1 transitive dependency is replaced. The current graph must pass
NuGet's full transitive advisory audit; an audit result covers known advisories,
not proof that a component is free of defects.

The upstream binary restore manifest has been reduced from 93 files to 14.
DotNetZip is replaced with framework ZIP APIs; OpenPop is replaced with current
MailKit and TLS is required for POP3 authentication. Two unmaintained desktop
SFTP integrations (DentiCal and MercuryDE), which disabled SSH host-key checks,
are disabled in the web build. In-process desktop plug-in loading is also
disabled. Unmapped API actions now require administrator permission; additional
non-administrator access requires an explicit reviewed permission mapping. These transports and plug-ins are not supported web features.

## Remaining compatibility and provenance exceptions

- `System.Drawing.Common` 6.0.0 is retained for inherited Linux rendering.
  It is out of support. Microsoft removed Unix support in version 7; installing
  the latest version would break these paths. A maintained cross-platform
  renderer is needed before this exception can be removed. Updating .NET does
  not make this package supported.
- `PDFsharp` 6.2.4 is the latest stable MIT release. Version 6.5.0 changed
  to [AGPL/commercial licensing](https://docs.pdfsharp.net/General/License/License.html)
  and requires explicit license activation. This
  repository retains its existing GPL publication terms and does not select a
  new license or purchase a commercial license. Upstream says the 6.2 branch
  receives no further updates; a compatible maintained replacement or a separate
  licensing review is required. Advisory checks remain mandatory for this package.
- Fourteen inherited binary inputs remain: Bridges, CDT, Dicom.Core,
  Health.Direct.Agent, Health.Direct.Common, Health.Direct.ResolverPlugins,
  Interop.Microsoft.Office.Core, Interop.VBIDE, Interop.Word, ODCrypt, RS232,
  VBbridges, VirtualWeb, and stdole. `dependencies.json` identifies the upstream
  revision and SHA-256 of every file; `tools/restore_dependencies.py` verifies
  them before installation. They are not NuGet packages and are not covered by
  the NuGet advisory audit. Their maintenance and complete source/license
  provenance have not been independently established. A checksum establishes
  reproducibility, not security clearance. The repository distributes source
  only; no DLLs or installers are published.
- The operating system, database, fonts, native image libraries, and provisioned
  speech model are operator-managed inputs. Keep the host and supported database
  patched. The Python dependency lock targets the supported Python 3.12/Linux
  environment and is audited separately; other Python/platform combinations
  require a fresh resolve and audit.

`OpenDental/Resources/*.txt` preserves upstream dependency notices as reference
material, not a complete or verified SBOM. Before shipping binaries, review
licenses, redistribution and corresponding-source obligations for the actual
resolved graph and replace incompatible inputs.

## Keeping the reviewed snapshot current

NuGet and Python package versions/hashes and GitHub Action commit IDs describe
an exact reviewed snapshot. Daily Dependabot checks propose updates; maintainers
review the source inventory and CI before merging. CI also runs daily so newly
published advisories can fail an unchanged dependency graph. There is no automatic
merge of unreviewed versions and no suppression of individual advisories.

To intentionally refresh NuGet locks after reviewing updates, use
`dotnet restore <project> --force-evaluate` for the web app, harness and contract
test project. Commit all four project lock files. For Python 3.12, use a separate
virtual environment with current `pip-tools`, then:

```sh
pip-compile --upgrade --generate-hashes --strip-extras -o tools/dictation/requirements.txt tools/dictation/requirements.in
pip-compile --upgrade --generate-hashes --strip-extras -o tools/security/requirements.txt tools/security/requirements.in
pip-audit --require-hashes -r tools/dictation/requirements.txt
```

Review both the runtime and audit-tool lock changes. Refresh the public source
inventory only after reviewing every changed file.
