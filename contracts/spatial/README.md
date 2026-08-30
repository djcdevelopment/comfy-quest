# Spatial exchange contracts

These strict JSON files are the public, file-shaped handoff between StewardView and Quest
Studio. They are not Runtime experience documents or `.questpack` files.

- `comfy-quest-spatial-anchor/v1` describes one snapshot-backed 3D sphere. StewardView is the
  first producer; Quest Studio validates its hash and lowers it into an Experience v2 area.
- `comfy-quest-spatial-evidence/v1` carries bounded Runtime observations back to a map tool.

`content_sha256` is computed by `SpatialExchangeContract` from the ordered scalar contract
fields. Finite JSON numbers are canonicalized as lowercase, 16-character IEEE-754 binary64
bits, with both signed zeros normalized to `0000000000000000`; this keeps .NET and Java hashes
identical even where their shortest decimal renderings differ. Consumers must recompute the
hash; a schema-valid document with a mismatched hash is invalid. Evidence exports keep the
newest records that fit both the 512-record and 256-KiB document limits, so a producer never
emits a schema-valid file that the bounded consumer must reject for size. Every record carries
bounded `current_count` / `required_count` progress. Positional predicates also carry a point and
verified 3D distance; `count_in_area` uses a null point and distance rather than inventing one.
`exported_utc` is the newest included receipt time, making repeat exports of the same evidence
set byte-identical and therefore idempotent at the content-addressed Steward import boundary.
External consumers pin a released copy by immutable revision, byte count, and SHA-256 rather
than reading this checkout.
