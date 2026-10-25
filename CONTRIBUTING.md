# Contributing

Keep this repository limited to standalone, practice-local software and generic
clients for documented network APIs. Do not add private service backends,
network-wide aggregation, payer documents, real patient
records, browser profiles, credentials, or internal business strategy.

Use synthetic examples and placeholder configuration. New integrations should
use generic HTTP contracts; do not embed cloud vendor SDKs. Preserve upstream
copyright and licensing notices in source files.

Before proposing a change:

```sh
python3 tools/publication/check_boundary.py
python3 tools/restore_dependencies.py --check
dotnet restore OpenDentalWebDemo/OpenDentalWebDemo.csproj --locked-mode
dotnet build OpenDentalWebDemo/OpenDentalWebDemo.csproj -c Release --no-restore
dotnet run --project tools/tests/TranscriptionContract -c Release
python3 -m unittest discover -s tools/dictation -p 'test_*.py'
```

The CI boundary check verifies a reviewed, hashed source inventory as well as
index and working-tree content (including untracked source), project references,
dependency inputs, and paths. New or changed
files require an explicit inventory update and maintainer review. It is a practical guard,
not a complete secret or licensing audit. Require all three CI jobs in GitHub branch
protection, and enable secret scanning/push protection when configuring the host.
Keep releases source-only until binary dependencies and corresponding source have
been reviewed. Do not upload the original development repository's history.

After reviewing every added/changed file for public suitability, update the source
inventory with `python3 tools/publication/update_inventory.py`, stage it together
with the reviewed changes, and run the boundary check. The inventory prevents
new backend code or strategy documents from passing merely because they were
placed under an allowed directory. Maintainers must review inventory changes;
a regenerated inventory is an explicit approval record, not a semantic IP audit.

By default the checker validates the index and working tree independently. Use
`--source head` to check the committed release snapshot. The inventory must match
each checked snapshot; an unchanged index cannot hide unreviewed working files.

Dependency updates must preserve locks, pass advisory checks, and document any
remaining compatibility exceptions. See [dependencies](docs/DEPENDENCIES.md).
