# RT-03 Existing Auto Sync Mutual-Exclusion Record

## Executed pause contract

Before the canary window, the reviewed API launcher was restarted with both
`QlhvAutoSync__Enabled=false` and `QlhvAutoSync__RunOnServerStartup=false`, plus its
explicit disable switch. The production-local configuration file itself was not
edited and remained byte-identical.

Two durable samples and the immediate preflight proved:

- polling disabled and `IsPolling=false`;
- active Auto Sync run/slot/operation 0/0/0;
- no open user transaction;
- latest Auto Sync history remained 10;
- the exact target baseline remained OTO 156 and MOTO 5.

The canary runner then held the Existing Auto Sync global exclusion lock and repeated
the durable-state checks before opening the exact-one target transaction. No writer
overlap occurred.

## Race and recovery behavior

- Auto Sync active before canary: stop before mutation; no marker or checkpoint.
- Auto Sync starts between proof and transaction: global lock and immediate recheck
  reject the transaction.
- Activity detected inside the cycle: abort the target transaction; use only the
  sealed exact rollback if commit is already unambiguous.
- Lock or transaction ambiguity: disable all RT-03 flags, keep both writers stopped,
  capture evidence, and fail closed.

None of these recovery branches was entered. Post-canary proof showed Auto Sync
active rows 0 and exactly one committed learner/marker/checkpoint identity.

## Executed restore contract

After cutover was declined, all six RT-03 feature flags were disabled. The paused API
was stopped and the reviewed launcher was started again without pause overrides. The
unchanged production-local configuration hash was
`9847629CE2D576BB72C23F34AF8B50E8E3F65002DC805C3AF339DDCA8FB5F632`.

Startup run 11 succeeded. The settled state is `PollingEnabled=true`,
`IsPolling=false`, active run/operation 0/0, and no RT-03 realtime writer. Existing
Auto Sync is therefore restored as the sole production writer mode.
