namespace QLHV.Application.Sync.Connections;

/// <summary>Hệ nguồn dữ liệu có thể cấu hình kết nối.</summary>
public enum SourceSystem
{
    /// <summary>CSDT_V1.</summary>
    V1 = 1,

    /// <summary>CSDT_V2 - nguồn gốc chính xác cho đồng bộ một chiều.</summary>
    V2 = 2,
}
