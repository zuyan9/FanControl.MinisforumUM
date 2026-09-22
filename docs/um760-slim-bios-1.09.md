# UM760 Slim BIOS 1.09 compatibility

[Issue #14](https://github.com/zuyan9/FanControl.MinisforumUM/issues/14) reports
UM760 Slim BIOS `1.09`, EC `0.05`, working CPU control after modifying the
plugin's BIOS gate, and a system fan staying near 3,500 RPM. This note records
the offline comparison performed on 2026-09-22 and the remaining validation.

## Firmware evidence

The issue links the vendor-hosted
[F7BSI_PHX_1.09_260724A package](https://pc-file.s3.us-west-1.amazonaws.com/UM760+Slim_UM780+Slim_UM790+Slim_UM860+Slim/BIOS/F7BSI_PHX_1.09_260724A.7z).
Its release notes date BIOS `1.09` to **2026-07-24**, updating PI to `1.2.0.0a`
and the setup specification to `2.4`. The previous EC update remains the
`0.05` entry for BIOS `1.08` dated 2024-11-05. The comparison baseline is the
vendor's [BIOS 1.08 package](https://pc-file.s3.us-west-1.amazonaws.com/UM760+Slim_UM780+Slim_UM790+Slim_UM860+Slim/BIOS/UM890_880+_870_760+Slim__BIOS+V1.08.7z).

| Artifact | SHA-256 |
|---|---|
| BIOS 1.08 archive | `d168d5239c690e4833c8e96e572401d12fbf788e0efa937df944e9a89baa6010` |
| BIOS 1.09 archive | `684140102fe4695403a80f2f440aeae006f99d6141c0aed50cc5aac6c44b405f` |
| BIOS 1.08 `F7BSI.BIN` | `ca9bc49e892989067c0fa48237e4a6dc5e9a53184ab4b5a98120d60bff89838a` |
| BIOS 1.09 `F7BSI.BIN` | `3d856d3b9d299da18e50597970d3e4d55d2ffc37810ba1549fd7e59b71fc0352` |
| Shared 128 KiB EC payload | `05a976553a3e7d71fb6d313bb74c638241e7da8dc7590b1d75b1ab6898f187cc` |

Both BIOS images are 33,554,432 bytes. Their first 131,072 bytes, at image
offsets `0x00000..0x1ffff`, are identical: **zero changed bytes**. This is the
previously catalogued EC candidate window, with `CMXG_EC-V14.6` at `0x50`,
project `F7BSI` at `0x41bf`, and `ECVer:00.00.00.05$` at `0x41c6`.
The fixed window's endpoint is a prior extraction convention, not a newly
verified flash-region boundary.

This supports adding the exact BIOS string `1.09` to the existing F7BSI
profile. EC version alone would not be sufficient evidence. The board, SKU,
system family/version, EC version, physical/controller signatures, policy
fingerprints, target bounds, and restoration checks still have to match.
Other boards and unlisted BIOS strings remain rejected.

The comparison does not prove that host BIOS behavior is unchanged, that a
particular machine contains these EC bytes, or that the system-fan report is
resolved. No firmware utility was executed and no hardware was accessed.

## Reproduce the byte comparison

With the two downloaded archives in a private working directory, a
libarchive-based `tar` (such as macOS `tar` or `bsdtar`) can stream only the
firmware member without executing vendor utilities:

```sh
tar -xOf 'UM890_880+_870_760+Slim__BIOS+V1.08.7z' \
    'F7BSI_PHX_1.08_241105A/F7BSI.BIN' > F7BSI-1.08.bin
tar -xOf F7BSI_PHX_1.09_260724A.7z \
    'F7BSI_PHX_1.09_260724A/F7BSI.BIN' > F7BSI-1.09.bin
python3 - <<'PY'
from hashlib import sha256
from pathlib import Path

old = Path('F7BSI-1.08.bin').read_bytes()
new = Path('F7BSI-1.09.bin').read_bytes()
assert len(old) == len(new) == 33_554_432
assert sha256(old).hexdigest() == 'ca9bc49e892989067c0fa48237e4a6dc5e9a53184ab4b5a98120d60bff89838a'
assert sha256(new).hexdigest() == '3d856d3b9d299da18e50597970d3e4d55d2ffc37810ba1549fd7e59b71fc0352'
assert old[:131_072] == new[:131_072]
print('Identical EC window:', sha256(new[:131_072]).hexdigest())
PY
```

Keep the archives and extracted firmware out of Git.

## System-fan investigation

The plugin has no 3,500-RPM limit. It writes a system target code in `0..51`
after verifying the firmware's fixed-target handoff, then reads the target and
ownership state back. A successful write verifies that transaction; it does
not prove the physical fan responds. A flat RPM reading could reflect an
incorrectly paired sensor, a physical speed limit, or a control problem. The
issue does not yet distinguish these possibilities.

Startup logging includes the actual board, BIOS, and EC identity. After a
changed system target has stayed stable for two seconds, normal telemetry
updates log the accepted/read-back code and current system RPM, at most once
per five seconds. The RPM is a sample, not confirmation of a settled speed or
a fresh ownership check. This diagnostic uses existing reads and adds no EC
transactions.

For a supervised follow-up on the exact admitted UM760 Slim:

1. Use an unmodified build with BIOS `1.09` support, stop other EC-writing
   utilities, and retain the initialization message and any errors.
2. At idle with plausible temperatures, confirm that **System Fan Control** is
   enabled and paired with the plugin's **System Fan** RPM sensor. Briefly
   compare manual requests of `100%`, `60%`, and `40%` (codes `51`, `31`, and
   `20`), allowing about ten seconds per setting while watching temperatures
   and the physical fan. Stop if temperatures rise unexpectedly or a control
   error occurs; do not test fan stop.
3. Record requested percentages, plugin RPM, both temperatures, physical fan
   response, and the corresponding log lines. A high-end RPM plateau differs
   from a fan that stays unchanged even at lower targets.
4. Disable/reset system control and close Fan Control normally. Verify that
   firmware control resumes and include the cleanup result. If restoration is
   uncertain, fully power off before another test.

The system fan has no automatic firmware temperature fallback during the
manual handoff. Hardware results, including restoration, are required before
claiming that the reported behavior is fixed.
