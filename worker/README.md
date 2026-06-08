# QLHV_APP - worker (placeholder)

This top-level `worker/` folder is a placeholder.

The actual background-job host project, **QLHV.Worker**, lives inside the
backend solution at:

```
server/QLHV.Worker
```

It is a .NET Generic Host intended to run Hangfire background jobs
(sync / data-transfer pipelines) using Hangfire + Polly.

> Real connection strings/secrets must be supplied via user-secrets or
> environment variables, never committed to source control.
