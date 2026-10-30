# Custom Xray-core build (allowInsecure)

Xray-core removed the TLS `allowInsecure` option (it errors out since 2026-06-01).
This fork ships its own Xray-core build with the option restored, and v2rayN writes
`"allowInsecure": true` to the Xray config again when a node has **Allow Insecure**
turned on and no certificate or certificate pin is set.

- `XRAY_VERSION` – the upstream Xray-core tag the patch is applied to.
- `patches/` – patches applied on top of that tag.
- `build-xray.sh <GOOS> <GOARCH> <out-dir>` – clones, patches and builds Xray-core.

`.github/workflows/build-custom.yml` builds everything (patched Xray + v2rayN for
Windows, Linux and macOS, x64 and arm64), adds the other cores and geo files from
[v2rayN-core-bin](https://github.com/2dust/v2rayN-core-bin), and publishes the zips
as a GitHub release. It runs on every push to `master` (and can be started by hand
from the Actions tab).

Because the bundled Xray-core and v2rayN are custom, the in-app updater no longer
offers updates for either of them; sing-box, mihomo and geo files still update.

## Moving to a newer Xray-core

1. Put the new tag in `XRAY_VERSION`.
2. Run `bash xray-custom/build-xray.sh linux amd64 out`. If the patch no longer
   applies, re-create it on the new tag (the change is small: add
   `bool allow_insecure = 1;` back to `transport/internet/tls/config.proto`,
   regenerate `config.pb.go`, set `config.AllowInsecure` in
   `infra/conf/transport_security.go`, and set `InsecureSkipVerify` from it in
   `transport/internet/tls/config.go`).
3. Push. The workflow publishes a new release.

`allowInsecure` turns off server certificate checks, which leaves the connection
open to man-in-the-middle attacks. Prefer certificate pinning where you can.
