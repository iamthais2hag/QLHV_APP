USE [$(Rt03TargetDatabase)];
GO
EXEC sys.sp_set_session_context
    @key=N'RT03_V9_DISPOSABLE_REHEARSAL',@value=1;
EXEC sys.sp_set_session_context
    @key=N'RT03_MASTER_DISPOSABLE_REHEARSAL',@value=1;
GO
:r ..\patches\20260731_add_rt03_v9_reviewed_retained.sql
:r ..\patches\20260731_add_rt03_master_switch.sql
:r ..\patches\20260731_add_rt03_master_switch.sql
:r ..\patches\20260731_verify_rt03_master_switch.sql
GO
