# QLHV photo processing

The photo module keeps the original JP2 files read-only under `D:\IM_GPLX`.
Derived JPEG files are written under `D:\QLHV_APP\IM_GPLX`, partitioned by
`CSDT_OTO` or `CSDT_MOTO`.

## Schema prerequisites

Deployment applies reviewed database patches separately; the installer and
launcher never execute SQL. Apply the new patches in this order:

1. `database/patches/20260723_add_app_data_version.sql`
2. `database/patches/20260723_add_qlhv_auto_sync.sql`
3. `database/patches/20260723_add_hocvien_photo_processing.sql`

All three are transactional and idempotent. They create control/history
metadata only and do not refresh BAK, run full sync, or process a real image.

## Local model provisioning

QLHV does not download a model and does not send student photos to a cloud
service. An administrator must provision a locally reviewed,
MODNet-compatible ONNX portrait-matting model outside Git.

**BENCHMARK CHƯA THỂ THỰC HIỆN:** the repository has no reviewed ONNX model,
license manifest, or representative real JP2 fixture set. Keep both photo
flags disabled. The authoritative provisioning and benchmark procedure is
[`deployment/qlhv-photo-model-and-benchmark.md`](deployment/qlhv-photo-model-and-benchmark.md).

The configured model must:

- accept a float tensor shaped `1 x 3 x height x width`;
- use RGB values normalized to `[-1, 1]`;
- return one foreground alpha value per input pixel;
- have input/output names matching `InputName` and `OutputName`;
- have a license reviewed for the organization's intended use.

Configure these values only in the Production Local file outside the
repository:

```json
{
  "PhotoProcessing": {
    "Enabled": false,
    "SourceRoot": "D:\\IM_GPLX",
    "OutputRoot": "D:\\QLHV_APP\\IM_GPLX",
    "ModelPath": "D:\\QLHV_APP_RUNTIME\\models\\person-segmentation.onnx",
    "ModelSha256": "<lowercase SHA-256>",
    "ModelLicense": "Apache-2.0",
    "ModelLicenseManifestPath": "D:\\QLHV_APP_RUNTIME\\models\\person-segmentation.license.json",
    "ModelLicenseManifestSha256": "<lowercase manifest SHA-256>",
    "BackgroundColor": "#0067B1",
    "AutoProcessAfterSync": false,
    "MinimumAutoApprovalConfidence": 0.85
  }
}
```

Calculate the checksum locally:

```powershell
(Get-FileHash -Algorithm SHA256 -LiteralPath `
  'D:\QLHV_APP_RUNTIME\models\person-segmentation.onnx').Hash.ToLowerInvariant()
```

Do not place the model in the repository. The `*.onnx` pattern is ignored.

## Readiness and safety

`GET /api/dong-bo-v2/qlhv/photos/readiness` reports whether the configured
model, accepted SPDX license ID, checksum-verified reviewed license manifest,
and ONNX input/output contract are ready. A missing or invalid model leaves
this module in `NotReady`; it does not prevent the application from starting
and does not roll back a committed database sync. When the module is disabled,
post-sync queueing performs no photo metadata writes and card printing keeps
the original-photo fallback.

The resolver blocks traversal, absolute paths outside the configured roots,
and reparse-point escapes. Original JP2 files are never overwritten. Derived
output uses the following identities:

```text
CSDT_OTO\{MaKhoa}\{MaDK}.jpg
CSDT_MOTO\{MaKhoa}\{MaDK}.jpg
```

Low-confidence images are marked `REVIEW_REQUIRED`. Only `APPROVED` images,
or `SUCCEEDED` images above the configured threshold that do not require
review, are eligible for card printing.
