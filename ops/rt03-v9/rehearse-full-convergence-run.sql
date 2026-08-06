:ON ERROR EXIT
EXEC sys.sp_set_session_context
    @key=N'QLHV_DISPOSABLE_REHEARSAL',@value=1,@read_only=1;
GO
:r .\ops\rt03-v9\rehearse-full-convergence-preservation.sql
GO
