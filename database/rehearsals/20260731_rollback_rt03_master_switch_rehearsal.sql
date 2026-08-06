USE [$(Rt03TargetDatabase)];
GO
EXEC sys.sp_set_session_context
    @key=N'RT03_MASTER_DISPOSABLE_REHEARSAL',@value=1;
GO
:r ..\patches\20260731_rollback_rt03_master_switch.sql
GO
