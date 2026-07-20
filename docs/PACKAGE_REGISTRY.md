# Sollang Package Registry Protocol

Status: implemented read protocol
Version: 1

Sollang registries are static, cache-friendly HTTPS resources. A registry does
not execute resolver code and does not need a custom client library.

## Manifest Syntax

```sollang
dependencies: {
    syntax: {
        registry: "https://packages.example.com"
        version: "^1.2.0"
    }
}
```

Exactly one of `path`, `git`, or `registry` identifies a dependency source.
Registry locations must use HTTPS. `file:` is accepted as an explicit local
mirror and for deterministic offline testing. Credentials, query strings, and
fragments are forbidden in manifests, and redirects may not downgrade HTTPS.

## Read Protocol

For package `syntax`, a client reads:

```text
GET <registry>/v1/syntax/index.slg
GET <registry>/v1/syntax/1.2.3.zip
```

The index is language-shaped and independently parseable by the self-hosted
compiler:

```sollang
registry {
    package: "syntax"
    versions: [
        {
            version: "1.2.3"
            checksum: "sha256:<64 lowercase hexadecimal digits>"
            yanked: false
        }
    ]
}
```

Versions are canonical SemVer. Duplicate precedence-equivalent versions are
invalid. Resolution selects the highest compatible non-yanked release and does
not select a prerelease unless the requirement explicitly mentions one.

## Archive Contract

Each ZIP has `sollang.project` at its root and contains the complete confined
package tree. Before extraction, the client verifies the index or lock SHA-256
against the exact archive bytes. Extraction rejects:

- absolute paths, `..` traversal, backslashes, and empty paths;
- symbolic links and case-insensitive path collisions;
- more than 100,000 entries, archives over 256 MiB, individual files over
  256 MiB, or expanded content over 512 MiB.

The extracted source tree is hashed independently. A changed cache is rejected
instead of silently repaired. Relative path packages may live inside an archive
but cannot escape it; they inherit the registry version and checksum identity.

## Lock And Update Rules

Lock format 2 records:

```sollang
source: "registry:https://packages.example.com#1.2.3"
checksum: "sha256:<archive digest>"
```

An ordinary build reuses the compatible locked version and works from a valid
cache without reading the index. `--locked` rejects a missing, incompatible, or
stale resolution. `sollang resolve` deliberately ignores existing registry pins,
selects the newest compatible non-yanked release, verifies it, and atomically
rewrites the lock.

## Security Boundary

The implemented protocol is read-only. Publishing, authentication providers,
package signing, transparency logs, and namespace ownership are server/tooling
layers and are not inferred from download support. HTTPS authenticates the first
resolution; the checked-in lock authenticates repeat builds by exact SHA-256.

## Completion Checklist

- [x] SemVer index and yanked releases
- [x] Highest-compatible deterministic selection
- [x] Prerelease exclusion unless explicitly requested
- [x] Exact archive SHA-256 verification
- [x] Safe bounded ZIP extraction
- [x] Content-addressed cache and mutation rejection
- [x] Lock-preserving normal and `--locked` builds
- [x] Explicit `sollang resolve` update
- [x] Self-host index selection and lock parsing
- [x] Windows and Linux integration coverage
- [ ] Publishing and private-registry authentication tooling
- [ ] Signed namespace policy and transparency service
