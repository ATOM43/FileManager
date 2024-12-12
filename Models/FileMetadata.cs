public class FileMetadata
{
    public string? Id { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public DateTime UploadDate { get; set; }
    public string? Checksum { get; set; }
    public DateTime? LastUpdated { get; set; }
}
