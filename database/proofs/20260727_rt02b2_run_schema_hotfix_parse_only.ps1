$ErrorActionPreference = 'Stop'

$sqlcmdPath = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
$inputPath = 'D:\QLHV_APP\database\proofs\20260727_rt02b2_schema_hotfix_parse_only.sql'
$outputPath = 'D:\QLHV_RT02_SQLDATA\RT02B2_SCHEMA_HOTFIX\schema_hotfix_parse_only.out.log'

& $sqlcmdPath `
    -S 'lpc:CSDLTTTC\QLHVRT02' `
    -E `
    -b `
    -r 1 `
    -i $inputPath `
    -o $outputPath

exit $LASTEXITCODE
