# QLHV photo model and benchmark

## Current status

**BENCHMARK CHƯA THỂ THỰC HIỆN.**

The repository does not contain an ONNX model, a reviewed model-license
manifest, or a representative set of real JP2 student photos. No CPU, RAM,
latency, throughput, confidence, or review-rate result may be inferred from
unit-test fixtures. `PhotoProcessing.Enabled` and
`PhotoProcessing.AutoProcessAfterSync` must remain `false` until every
production-enablement criterion below is satisfied.

QLHV uses:

- `Microsoft.ML.OnnxRuntime` 1.20.1, CPU execution;
- `Magick.NET-Q16-AnyCPU` 14.14.0 for JP2 decoding and JPEG encoding;
- `IBackgroundRemovalEngine` as the application engine boundary;
- `OnnxBackgroundRemovalEngine`, a local MODNet-compatible implementation.

No photo or model is downloaded and no student photo is sent to an external
service.

## Model contract

The reviewed local model must:

- be an ONNX file outside the Git repository;
- accept one float RGB tensor shaped `1 x 3 x InputHeight x InputWidth`;
- accept RGB values normalized to `[-1, 1]`;
- expose the configured `InputName` and `OutputName`;
- return exactly one foreground-alpha value for each input pixel;
- successfully load with ONNX Runtime CPU on the production server.

The engine reads original `.jp2` files through Magick.NET. It writes derived
`.jpg` files only; the current engine does not produce PNG. Original JP2 files
are opened read-only and are never overwritten. Derived files belong under
`D:\QLHV_APP\IM_GPLX`, partitioned by `CSDT_OTO` and `CSDT_MOTO`.

Recommended local locations:

```text
D:\QLHV_APP_RUNTIME\models\person-segmentation.onnx
D:\QLHV_APP_RUNTIME\models\person-segmentation.license.json
```

Neither file may be committed to Git.

## Accepted licenses and reviewed manifest

The built-in allowlist accepts these SPDX identifiers:

- `Apache-2.0`
- `MIT`
- `BSD-2-Clause`
- `BSD-3-Clause`

Legal/organizational review is still required; appearing in this technical
allowlist is not legal approval. `ModelLicense` must equal the reviewed
manifest's `licenseId`.

The UTF-8 JSON manifest must be no larger than 1 MiB and use schema version 1:

```json
{
  "schemaVersion": 1,
  "licenseId": "Apache-2.0",
  "modelSha256": "<64 lowercase hex characters>",
  "modelSource": "<reviewed source URL or provenance reference>",
  "reviewedBy": "<reviewer or approval record>",
  "reviewedAtUtc": "2026-07-23T00:00:00Z"
}
```

Before reporting `READY`, the engine verifies all of the following:

1. photo processing is enabled;
2. the configured license ID is allowlisted;
3. the absolute `.onnx` path exists;
4. the model's computed SHA-256 equals `ModelSha256`;
5. the absolute `.json` manifest path exists;
6. the manifest's computed SHA-256 equals
   `ModelLicenseManifestSha256`;
7. the manifest is valid schema-version-1 JSON;
8. its license ID and model SHA-256 match configuration;
9. its review fields and non-future UTC review timestamp are present;
10. the configured ONNX input/output contract loads successfully.

Calculate and record checksums locally:

```powershell
(Get-FileHash -Algorithm SHA256 -LiteralPath `
  'D:\QLHV_APP_RUNTIME\models\person-segmentation.onnx').Hash.ToLowerInvariant()

(Get-FileHash -Algorithm SHA256 -LiteralPath `
  'D:\QLHV_APP_RUNTIME\models\person-segmentation.license.json').Hash.ToLowerInvariant()
```

## Production Local configuration

Keep the module disabled while provisioning:

```json
{
  "PhotoProcessing": {
    "Enabled": false,
    "AutoProcessAfterSync": false,
    "SourceRoot": "D:\\IM_GPLX",
    "OutputRoot": "D:\\QLHV_APP\\IM_GPLX",
    "ModelPath": "D:\\QLHV_APP_RUNTIME\\models\\person-segmentation.onnx",
    "ModelSha256": "<model SHA-256>",
    "ModelLicense": "Apache-2.0",
    "ModelLicenseManifestPath": "D:\\QLHV_APP_RUNTIME\\models\\person-segmentation.license.json",
    "ModelLicenseManifestSha256": "<manifest SHA-256>",
    "BackgroundColor": "#0067B1",
    "MinimumAutoApprovalConfidence": 0.85
  }
}
```

The setting belongs only in
`D:\QLHV_APP_RUNTIME\config\appsettings.Production.Local.json`, which is
outside source control. Do not put model paths or approval records in either
Development appsettings file.

When the module is disabled, post-sync photo queueing performs no metadata
writes and card printing continues to use the existing original-photo
fallback. Missing/invalid models leave the module `NotReady`; they do not
prevent QLHV from starting and do not undo a committed data sync.

## Read-only-source benchmark

The opt-in benchmark invokes the exact `OnnxBackgroundRemovalEngine`. It does
not start the API, connect to SQL Server, call a POST endpoint, or alter source
JP2 files. Before and after processing, it compares each source file's SHA-256,
length, and last-write timestamp. Derived JPEGs and the measured JSON report
are written only below a unique directory in
`%TEMP%\qlhv-photo-benchmark`.

Run only after the model, manifest, and a representative read-only JP2 fixture
directory are available:

```powershell
cd D:\QLHV_APP

.\scripts\windows\qlhv-lan\Invoke-QLHV-PhotoBenchmark.ps1 `
  -ModelPath 'D:\QLHV_APP_RUNTIME\models\person-segmentation.onnx' `
  -ModelSha256 '<model SHA-256>' `
  -LicenseId 'Apache-2.0' `
  -LicenseManifestPath 'D:\QLHV_APP_RUNTIME\models\person-segmentation.license.json' `
  -LicenseManifestSha256 '<manifest SHA-256>' `
  -InputRoot 'D:\QLHV_PHOTO_BENCHMARK_INPUT' `
  -MaxImages 50 `
  -MinimumConfidence 0.85
```

Without explicit opt-in supplied by that script, the benchmark test is
reported as skipped. The script validates both configured hashes before
running. Do not point `InputRoot` at an unreviewed live folder; prepare a
representative read-only copy with both OTO/MOTO, current/legacy paths,
different lighting, hair/background complexity, and known difficult cases.
Do not commit the model, JP2 fixtures, derived JPEGs, or benchmark report.

## Result record to complete after a real run

| Field | Measured result |
|---|---|
| Date/server/CPU | Not measured |
| ONNX model SHA-256 | Not provided |
| License ID and manifest SHA-256 | Not provided |
| JP2 sample count and selection notes | Not provided |
| Successful/failed images | Not measured |
| p50/p95 latency per image | Not measured |
| Process peak working set | Not measured |
| Total managed allocation | Not measured |
| Average/minimum confidence | Not measured |
| Review-required count/rate | Not measured |
| Visual-review pass/fail notes | Not performed |
| Approved production threshold | Not approved |

Do not replace `Not measured` with estimates.

## Production-enablement criteria

Enable `PhotoProcessing.Enabled` only after all of these are recorded:

- model provenance and organizational license approval are documented;
- model and manifest checksums match the installed files;
- readiness reports `READY` with the intended production configuration;
- the benchmark completes without source JP2 changes or processing failures;
- measured latency and memory are acceptable on the production server;
- a human reviews representative OTO/MOTO outputs, including difficult cases;
- the confidence threshold yields an acceptable review-required rate and
  never auto-approves known bad samples;
- `D:\IM_GPLX` is read-only for the service identity and the derived-output
  root is writable;
- card printing uses only `APPROVED`, or high-confidence `SUCCEEDED` images
  that are not marked for review;
- rollback has been rehearsed by setting both photo flags back to `false`.

Only after those checks may `Enabled` be set to `true`. Keep
`AutoProcessAfterSync=false` for a controlled manual pilot; enable it only
after the pilot and operational capacity review pass.
