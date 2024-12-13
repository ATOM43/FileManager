// Models

public class FileMetadata
{
    public required string Id { get; set; }
    public required string FileName { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public DateTime UploadDate { get; set; }
    public DateTime? LastUpdated { get; set; }
    public required string Checksum { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public int UserId { get; set; }
}

public class FileSyncRequest
{
    public required string FileId { get; set; } // Optional, can be null
    public DateTime LastUpdated { get; set; } // Mandatory, default value will be 01/01/0001 if not set
    public string? Checksum { get; set; } // Optional, can be null
}

public class FileUploadSyncRequest
{
    public required string FileId { get; set; } // Optional, can be null
    public required string FileName { get; set; }
}



public class SynchronizationMetadata
{

    public required string Id { get; set; }
    public int UserId { get; set; }
    public required Dictionary<string, string> FilesToUpdate { get; set; }
    public required bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LastUpdated { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class FileToUpdate
{
    public required string FileId { get; set; } // Default value will be null
    public required string FileName { get; set; }
}
