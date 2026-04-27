# Classic Inpaint ZITS Support Gating Design

## Goal

Expose `ZITS` as a classic inpaint backend only when the installed IOPaint CLI actually supports it. If the active IOPaint installation does not support `ZITS`, SwarmUI should remove the option from the frontend and reject it server-side.

## Current Problem

SwarmUI currently includes `ZITS` in the classic inpaint backend list, but marks it unavailable in the frontend and hard-rejects it in the server route as future support. This is misleading because the true source of support should be the installed IOPaint version, not a hardcoded placeholder in Swarm.

## Scope

This change is limited to the classic inpaint backend capability surface.

It should:

- detect whether the installed IOPaint CLI supports `zits`
- expose `ZITS` only when supported
- remove `ZITS` from the frontend when unsupported
- reject unsupported backends on the server based on actual detected capability

It should not:

- add a non-IOPaint-specific `ZITS` integration path
- change `LaMa` or `MAT` invocation behavior
- add speculative support for backends not confirmed by the installed IOPaint

## Design

### Capability Detection

SwarmUI should determine available classic inpaint backends from the installed IOPaint CLI rather than hardcoded assumptions.

The detection should answer:

- whether IOPaint is installed and callable
- whether `zits` is accepted by the current CLI as a model/backend option

This capability check may be implemented by probing CLI help or another lightweight command output that reliably lists supported models.

### Server Authority

The server must remain the source of truth for backend availability.

`ClassicInpaint()` should:

- accept only backends known to be supported by the current IOPaint install
- return a readable JSON error if a backend is requested but unavailable

This prevents stale or spoofed frontend values from enabling unsupported backends.

### Frontend Availability

The image editor backend dropdown should be populated or filtered based on server-known support.

If `ZITS` is unsupported:

- it should not appear as a selectable or disabled placeholder

If `ZITS` is supported:

- it should appear alongside `LaMa` and `MAT`
- no special-case frontend error handling is needed beyond the standard classic inpaint flow

### Behavioral Policy

- `LaMa` and `MAT` remain unchanged.
- `ZITS` becomes environment-dependent instead of hardcoded unavailable.
- Unsupported backends are removed, not merely disabled.

## Risks

- CLI probing must be reliable across supported IOPaint versions; fragile parsing could hide valid support or show invalid support.
- Capability checks should avoid expensive startup costs or heavyweight model downloads.

## Verification

After implementation, manual verification should check:

1. On an IOPaint install without `ZITS`, the backend dropdown omits `ZITS`.
2. On an IOPaint install with `ZITS`, the backend dropdown includes `ZITS`.
3. The server rejects unsupported backend values with a readable JSON error.
4. `LaMa` and `MAT` still behave as before.
