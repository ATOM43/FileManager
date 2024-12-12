using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.IO.Compression;


[ApiController]
[Route("api/files")]
public class FileController : ControllerBase
{
    private const long MaxFileSize = 50 * 1024 * 1024; // 50 MB
    private readonly IMongoCollection<FileMetadata> _fileCollection;
    private readonly string _fileStoragePath = Path.Combine(Directory.GetCurrentDirectory(), "FileStorage");

    public FileController(IMongoClient mongoClient)
    {
        var database = mongoClient.GetDatabase("FileDatabase");
        _fileCollection = database.GetCollection<FileMetadata>("Files");

        if (!Directory.Exists(_fileStoragePath))
            Directory.CreateDirectory(_fileStoragePath);
    }

    // Upload a file (only zip files allowed)
    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only zip files are allowed.");

        if (file.Length > MaxFileSize)
            return BadRequest($"File size exceeds the limit of {MaxFileSize / (1024 * 1024)} MB.");

        var fileId = ObjectId.GenerateNewId().ToString();
        var filePath = Path.Combine(_fileStoragePath, fileId);
        var checksum = await FileUtilities.SaveFileAndCalculateChecksum(file, filePath);


        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var fileMetadata = new FileMetadata
        {
            Id = fileId,
            FileName = file.FileName,
            ContentType = file.ContentType,
            Size = file.Length,
            UploadDate = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            Checksum = checksum
        };

        await _fileCollection.InsertOneAsync(fileMetadata);

        return Ok(new { FileId = fileId, Checksum = checksum, Message = "File uploaded successfully." });
    }

    // List all uploaded files
    [HttpGet("list")]
    public async Task<IActionResult> ListFiles(int page = 1, int pageSize = 10)
    {
        var skip = (page - 1) * pageSize;
        var files = await _fileCollection.Find(Builders<FileMetadata>.Filter.Empty)
                                        .Skip(skip)
                                        .Limit(pageSize)
                                        .ToListAsync();
        var totalFiles = await _fileCollection.CountDocumentsAsync(Builders<FileMetadata>.Filter.Empty);

        return Ok(new
        {
            success = true,
            data = files,
            pagination = new { page, pageSize, totalFiles }
        });
    }

    // Download a file by ID
    [HttpGet("download/{id}")]
    public async Task<IActionResult> DownloadFile(string id)
    {
        var fileMetadata = await _fileCollection.Find(f => f.Id == id).FirstOrDefaultAsync();
        if (fileMetadata == null)
            return NotFound("File not found.");

        var filePath = Path.Combine(_fileStoragePath, id);
        if (!System.IO.File.Exists(filePath))
            return NotFound("File not found on server.");

        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
        var contentType = fileMetadata.ContentType ?? "application/octet-stream"; // Default content type

        return File(fileBytes, contentType, fileMetadata.FileName);
    }

    // Get the checksum of a file by ID
    [HttpGet("checksum/{id}")]
    public async Task<IActionResult> GetFileChecksum(string id)
    {
        var fileMetadata = await _fileCollection.Find(f => f.Id == id).FirstOrDefaultAsync();
        if (fileMetadata == null)
            return NotFound("File not found.");

        return Ok(new { FileId = id, fileMetadata.Checksum });
    }


    [HttpPost("update/{id}")]
    public async Task<IActionResult> UpdateFile(string id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only zip files are allowed.");

        // Find existing file metadata
        var fileMetadata = await _fileCollection.Find(f => f.Id == id).FirstOrDefaultAsync();
        var filePath = Path.Combine(_fileStoragePath, id);

        // If file metadata or physical file does not exist, treat as added
        if (fileMetadata == null || !System.IO.File.Exists(filePath))
        {
            return await HandleFileAddition(id, file, filePath);
        }

        // Paths for temporary directories
        var tempDirectory = CreateTemporaryDirectory($"Temp_{id}");
        var previousTempDirectory = CreateTemporaryDirectory($"Previous_{id}");

        try
        {
            // Extract files to temporary directories
            ExtractZipToDirectory(file.OpenReadStream(), tempDirectory);
            ExtractZipToDirectory(System.IO.File.OpenRead(filePath), previousTempDirectory);

            // Compare directories and get changes
            var (addedFiles, deletedFiles, modifiedFiles) = CompareDirectories(tempDirectory, previousTempDirectory);

            // If no changes, return early
            if (!addedFiles.Any() && !deletedFiles.Any() && !modifiedFiles.Any())
            {
                return Ok(new { Message = "No changes detected." });
            }

            // Save new file to the server and calculate checksum
            var newChecksum = await FileUtilities.SaveFileAndCalculateChecksum(file, filePath);

            // Update metadata
            fileMetadata.FileName = file.FileName;
            fileMetadata.Checksum = newChecksum;
            fileMetadata.Size = file.Length;
            fileMetadata.LastUpdated = DateTime.UtcNow;

            await _fileCollection.ReplaceOneAsync(f => f.Id == id, fileMetadata);

            return Ok(new
            {
                Message = "File updated successfully.",
                Changes = new
                {
                    AddedFiles = addedFiles,
                    DeletedFiles = deletedFiles,
                    ModifiedFiles = modifiedFiles
                },
                Metadata = new
                {
                    fileMetadata.FileName,
                    fileMetadata.Size,
                    fileMetadata.Checksum,
                    fileMetadata.LastUpdated
                }
            });
        }
        finally
        {
            // Cleanup temporary directories
            CleanupDirectory(tempDirectory);
            CleanupDirectory(previousTempDirectory);
        }
    }

    // Handles adding a new file
    private async Task<IActionResult> HandleFileAddition(string id, IFormFile file, string filePath)
    {
        var checksum = await FileUtilities.SaveFileAndCalculateChecksum(file, filePath);

        // Create new metadata
        var fileMetadata = new FileMetadata
        {
            Id = id,
            FileName = file.FileName,
            ContentType = file.ContentType,
            Size = file.Length,
            UploadDate = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            Checksum = checksum
        };

        // Save metadata to MongoDB
        await _fileCollection.ReplaceOneAsync(f => f.Id == id, fileMetadata, new ReplaceOptions { IsUpsert = true });

        return Ok(new
        {
            Message = "File added successfully.",
            Changes = new
            {
                AddedFiles = new List<string> { file.FileName },
                DeletedFiles = new List<string>(),
                ModifiedFiles = new List<string>()
            },
            Metadata = new
            {
                fileMetadata.FileName,
                fileMetadata.Size,
                fileMetadata.Checksum,
                fileMetadata.LastUpdated
            }
        });
    }

    // Extracts a zip archive to a directory
    private void ExtractZipToDirectory(Stream zipStream, string destination)
    {
        using (var zipArchive = new ZipArchive(zipStream))
        {
            zipArchive.ExtractToDirectory(destination, overwriteFiles: true);
        }
    }

    // Compares two directories and returns added, deleted, and modified files
    private (List<string> Added, List<string> Deleted, List<string> Modified) CompareDirectories(string newDir, string oldDir)
    {
        var newFiles = Directory.GetFiles(newDir, "*", SearchOption.AllDirectories)
            .Select(f => f.Replace(newDir, "").TrimStart(Path.DirectorySeparatorChar))
            .ToHashSet();

        var oldFiles = Directory.GetFiles(oldDir, "*", SearchOption.AllDirectories)
            .Select(f => f.Replace(oldDir, "").TrimStart(Path.DirectorySeparatorChar))
            .ToHashSet();

        var addedFiles = newFiles.Except(oldFiles).ToList();
        var deletedFiles = oldFiles.Except(newFiles).ToList();

        var modifiedFiles = newFiles.Intersect(oldFiles)
            .Where(relativePath =>
            {
                var newFilePath = Path.Combine(newDir, relativePath);
                var oldFilePath = Path.Combine(oldDir, relativePath);
                return !FileUtilities.FileContentsAreEqual(newFilePath, oldFilePath);
            }).ToList();

        return (addedFiles, deletedFiles, modifiedFiles);
    }

    

    // Creates a temporary directory
    private string CreateTemporaryDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix);
        Directory.CreateDirectory(path);
        return path;
    }

    // Cleans up a directory
    private void CleanupDirectory(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
            Directory.Delete(directoryPath, true);
    }





    // Delete a file by ID
    [HttpDelete("delete/{id}")]
    public async Task<IActionResult> DeleteFile(string id)
    {
        var fileMetadata = await _fileCollection.Find(f => f.Id == id).FirstOrDefaultAsync();
        if (fileMetadata == null)
            return NotFound("File not found.");

        var filePath = Path.Combine(_fileStoragePath, id);
        if (System.IO.File.Exists(filePath))
            System.IO.File.Delete(filePath);

        await _fileCollection.DeleteOneAsync(f => f.Id == id);

        return Ok(new { Message = "File deleted successfully." });
    }

}
