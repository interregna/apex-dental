# External build inputs

`dependencies.json` pins upstream binary inputs to Open Dental revision
`d804c19546233593d8a66af0591ed118a9e2c794` with a SHA-256 for each file.
`tools/restore_dependencies.py` verifies bytes before installing them locally.
The public source does not include those DLLs. Vendor cloud SDK binaries are excluded from both the manifest and project references.
NuGet dependencies and versions are declared in the project files.

The inherited headless port depends on legacy assemblies without replacements
in this repository. Downloading those inputs is necessary to compile the current
port; their availability upstream does not establish blanket redistribution or
GPL compatibility. `OpenDental/Resources/*.txt` preserves upstream dependency
notices as reference material, not a complete or verified SBOM.

No compiled application or installer is published by this repository. Before
shipping binaries, inventory the resolved dependency graph, verify compatible
licenses and redistribution/source obligations, and replace incompatible inputs.
A source publication check is not a binary license clearance.

The public port updates MailKit to 4.16.0 for the STARTTLS fix described in
[GHSA-9j88-vvj5-vhgr](https://github.com/advisories/GHSA-9j88-vvj5-vhgr).
