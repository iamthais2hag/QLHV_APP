:ON ERROR EXIT
EXEC sys.sp_set_session_context
    @key=N'RT03_V9_DISPOSABLE_REHEARSAL',@value=1,@read_only=1;
GO
:r .\database\patches\20260731_rollback_rt03_v9_reviewed_retained.sql
GO
