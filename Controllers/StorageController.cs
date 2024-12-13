using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

[ApiController]
[Route("api/storage")]
public class StorageController : ControllerBase
{
    private const long MaxFileSize = 50 * 1024 * 1024; // 50 MB
    private readonly IMongoCollection<FileMetadata> _fileCollection;
    private readonly IMongoCollection<SynchronizationMetadata> _syncCollection;
    private readonly string _storageDir;

    public StorageController(IMongoClient mongoClient)
    {
        var database = mongoClient.GetDatabase("FileDatabase");
        _fileCollection = database.GetCollection<FileMetadata>("Files");
        _syncCollection = database.GetCollection<SynchronizationMetadata>("Synchronizations");

        _storageDir = Path.Combine(Directory.GetCurrentDirectory(), "FileStorage");
        Directory.CreateDirectory(_storageDir); // Ensure storage directory exists
    }

    // POST /upload/file
    [HttpPost("upload/file")]
    public async Task<IActionResult> UploadFile(IFormFile file, [FromQuery] int user_id = 1)
    {
        if (!FileUtilities.IsValidFile(file, MaxFileSize, out var errorMessage))
            return BadRequest(new { Status = false, Message = errorMessage });

        if (Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { Status = false, Message = "Zip files are not allowed for this endpoint." });
        }

        try
        {
            var fileId = ObjectId.GenerateNewId().ToString();
            var filePath = Path.Combine(_storageDir, fileId);
            var checksum = await FileUtilities.SaveFileAndCalculateChecksum(file, filePath);

            var fileMetadata = new FileMetadata
            {
                Id = fileId,
                FileName = file.FileName,
                ContentType = file.ContentType,
                Size = file.Length,
                UploadDate = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                UserId = user_id,
                Checksum = checksum
            };

            await _fileCollection.InsertOneAsync(fileMetadata);

            return Ok(new
            {
                Status = true,
                Message = "File uploaded successfully",
                Data = new { FileId = fileId, file.FileName, fileMetadata.Checksum }
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error uploading file: {ex.Message}");
            return StatusCode(500, new { Status = false, Message = "Internal server error while uploading file" });
        }
    }

    // POST /upload/archive
    [HttpPost("upload/archive")]
    public async Task<IActionResult> UploadArchive(IFormFile archive, [FromQuery] int user_id = 1)
    {
        if (!FileUtilities.IsValidFile(archive, MaxFileSize, out var errorMessage))
            return BadRequest(new { Status = false, Message = errorMessage });

        if (!archive.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { Status = false, Message = "Only zip files are allowed." });

        var tempDir = Path.Combine(Path.GetTempPath(), $"Temp_{Guid.NewGuid()}");

        try
        {
            Directory.CreateDirectory(tempDir);

            // Extract archive
            using (var zip = new ZipArchive(archive.OpenReadStream()))
            {
                zip.ExtractToDirectory(tempDir, true);
            }

            var files = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);

            // Create metadata for all files
            var metadataList = files.Select(filePath =>
            {
                var fileId = ObjectId.GenerateNewId().ToString();
                var fileName = Path.GetFileName(filePath);
                var destinationPath = Path.Combine(_storageDir, fileId);

                // Copy file to the storage directory
                System.IO.File.Copy(filePath, destinationPath, true);

                // Generate metadata
                var fileInfo = new FileInfo(filePath);
                var checksum = FileUtilities.CalculateChecksum(filePath);

                return new FileMetadata
                {
                    Id = fileId,
                    FileName = fileName,
                    ContentType = FileUtilities.GetMimeType(fileName),
                    Size = fileInfo.Length,
                    UploadDate = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow,
                    UserId = user_id,
                    Checksum = checksum
                };
            }).ToList();

            // Insert all metadata in a single operation
            await _fileCollection.InsertManyAsync(metadataList);

            // Return file IDs and file names
            var responseFiles = metadataList.Select(meta => new
            {
                FileId = meta.Id,
                meta.FileName,
                meta.Checksum
            });

            return Ok(new
            {
                Status = true,
                Message = "Archive processed successfully",
                Data = responseFiles
            });
        }
        catch (Exception ex)
        {
            // Log error (placeholder)
            Console.Error.WriteLine($"Error uploading archive: {ex.Message}");
            return StatusCode(500, new { Status = false, Message = "Internal server error while uploading archive." });
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // List all uploaded files
    [HttpGet("list")]
    public async Task<IActionResult> ListFiles(int page = 1, int pageSize = 10, [FromQuery] int user_id = 1)
    {
        if (page < 1 || pageSize < 1)
        {
            return BadRequest(new { success = false, message = "Page and pageSize must be greater than zero." });
        }

        // Build the filter for the query
        var filter = Builders<FileMetadata>.Filter.Eq(f => f.UserId, user_id);

        var skip = (page - 1) * pageSize;

        // Fetch the files with pagination
        var files = await _fileCollection
            .Find(filter)
            .Skip(skip)
            .Limit(pageSize)
            .ToListAsync();

        // Get the total file count for the filter
        var totalFiles = await _fileCollection.CountDocumentsAsync(filter);

        return Ok(new
        {
            success = true,
            data = files,
            pagination = new
            {
                page,
                pageSize,
                totalFiles,
                totalPages = (int)Math.Ceiling((double)totalFiles / pageSize)
            }
        });
    }

    [HttpPost("update/file")]
    public async Task<IActionResult> UpdateFile(IFormFile file, [FromQuery] string file_id, [FromQuery] int user_id = 1)
    {
        if (string.IsNullOrEmpty(file_id))
            return BadRequest(new { Status = false, Message = "File ID is required." });

        if (!FileUtilities.IsValidFile(file, MaxFileSize, out var errorMessage))
            return BadRequest(new { Status = false, Message = errorMessage });

        if (Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { Status = false, Message = "Zip files are not allowed for this endpoint." });

        // Fetch existing file metadata
        var existingFile = await _fileCollection.Find(f => f.Id == file_id && f.UserId == user_id).FirstOrDefaultAsync();
        if (existingFile == null)
            return NotFound(new { Status = false, Message = "File not found." });

        string? newFilePath = null;
        string? newChecksum = null;

        // Save the new file and update checksum
        newFilePath = Path.Combine(_storageDir, file_id);
        newChecksum = await FileUtilities.SaveFileAndCalculateChecksum(file, newFilePath);

        // Update size and checksum
        existingFile.FileName = file.FileName;
        existingFile.Size = file.Length;
        existingFile.Checksum = newChecksum;
        existingFile.LastUpdated = DateTime.UtcNow;

        // Update the metadata in the database
        await _fileCollection.ReplaceOneAsync(f => f.Id == file_id && f.UserId == user_id, existingFile);

        return Ok(new
        {
            Status = true,
            Message = "File updated successfully",
            Data = new
            {
                FileId = existingFile.Id,
                FileName = existingFile.FileName,
                ContentType = existingFile.ContentType,
                Size = existingFile.Size,
                Checksum = existingFile.Checksum,
                Metadata = existingFile.Metadata
            }
        });
    }

    // POST /synchronize
    [HttpPost("synchronize")]
    public async Task<IActionResult> Synchronize([FromBody] List<FileSyncRequest> files, [FromQuery] int user_id = 1)
    {
        if (files == null || !files.Any())
            return BadRequest(new { Status = false, Message = "No files provided for synchronization." });

        var distinctFiles = files
        .GroupBy(f => f.FileId)
        .Select(g => g.First())
        .ToList();

        var response = new List<FileToUpdate>();

        foreach (var file in distinctFiles)
        {
            // Find the file metadata by FileId and UserId
            var metadata = await _fileCollection
                .Find(f => f.Id == file.FileId && f.UserId == user_id)
                .FirstOrDefaultAsync();

            // Skip if no metadata exists
            if (metadata == null)
                continue;

            // Check if the file needs to be updated

            if (metadata.LastUpdated < file.LastUpdated ||
    (metadata.Checksum != null && metadata.Checksum != file.Checksum))
            {
                response.Add(new FileToUpdate
                {
                    FileId = metadata.Id,
                    FileName = metadata.FileName
                });
            }
        }

        if (!response.Any())
            return Ok(new { Status = true, Message = "No files need to be uploaded." });

        // Create synchronization metadata
        var syncId = Guid.NewGuid().ToString();
        // Ensure FileId is not null before creating the dictionary
        var filesToUpdateDict = response
            .Where(r => !string.IsNullOrEmpty(r.FileId)) // Exclude null or empty FileId
            .ToDictionary(
                 r => r.FileName!,
                r => r.FileId!  // The `!` operator ensures that the compiler treats it as non-null

            );
        var syncMetadata = new SynchronizationMetadata
        {
            Id = syncId,
            UserId = user_id,
            FilesToUpdate = filesToUpdateDict,
            IsCompleted = false,
            LastUpdated = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        await _syncCollection.InsertOneAsync(syncMetadata);

        return Ok(new
        {
            Status = false,
            Message = "Files to upload.",
            Data = new
            {
                SynchronizationId = syncId,
                FilesToUpload = response
            }
        });
    }

    // GET /sync/incomplete
    [HttpGet("sync/incomplete")]
    public async Task<IActionResult> GetIncompleteSynchronizations([FromQuery] int user_id = 1)
    {
        try
        {
            // Fetch incomplete synchronizations using a lambda expression
            var incompleteSynchronizations = await _syncCollection
                .Find(sync => sync.UserId == user_id && !sync.IsCompleted)
                .ToListAsync();

            if (!incompleteSynchronizations.Any())
            {
                return Ok(new
                {
                    Status = true,
                    Message = "No incomplete synchronizations found.",
                    Data = new List<object>()
                });
            }

            // Format the response
            var response = incompleteSynchronizations.Select(sync => new
            {
                SynchronizationId = sync.Id,
                UserId = sync.UserId,
                FilesToUpdate = sync.FilesToUpdate,
                CreatedAt = sync.CreatedAt,
                LastUpdated = sync.LastUpdated
            });

            return Ok(new
            {
                Status = true,
                Message = "Incomplete synchronizations retrieved successfully.",
                Data = response
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error retrieving incomplete synchronizations: {ex.Message}");
            return StatusCode(500, new
            {
                Status = false,
                Message = "Internal server error while retrieving incomplete synchronizations."
            });
        }
    }


    // POST /upload/sync
    [HttpPost("upload/sync")]
    public async Task<IActionResult> UploadSync(IFormFile archive, [FromQuery] string synchronization_id, [FromQuery] int user_id = 1)
    {
        if (!FileUtilities.IsValidFile(archive, MaxFileSize, out var errorMessage))
            return BadRequest(new { Status = false, Message = errorMessage });

        if (!archive.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { Status = false, Message = "Only zip files are allowed." });

        // Verify synchronization metadata
        var syncMetadata = await _syncCollection
            .Find(s => s.Id == synchronization_id && s.UserId == user_id && !s.IsCompleted)
            .FirstOrDefaultAsync();

        if (syncMetadata == null)
            return NotFound(new { Status = false, Message = "Synchronization ID not found." });

        var tempDir = Path.Combine(Path.GetTempPath(), $"SyncTemp_{Guid.NewGuid()}");

        try
        {
            Directory.CreateDirectory(tempDir);

            // Extract archive
            using (var zip = new ZipArchive(archive.OpenReadStream()))
            {
                zip.ExtractToDirectory(tempDir, true);
            }

            var extractedFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories)
                .Select(filePath => new
                {
                    FileName = Path.GetFileName(filePath),
                    FilePath = filePath
                })
                .ToList();

            var synchronizedFiles = new List<string>();
            var pendingFiles = new Dictionary<string, string>();
            var bulkUpdates = new List<WriteModel<FileMetadata>>();

            // Ensure filesToUpdate is a dictionary for easy lookup
            var filesToUpdate = syncMetadata.FilesToUpdate ?? new Dictionary<string, string>();

            foreach (var file in extractedFiles)
            {
                if (filesToUpdate.TryGetValue(file.FileName, out var fileId))
                {
                    var destinationPath = Path.Combine(_storageDir, fileId);
                    System.IO.File.Copy(file.FilePath, destinationPath, true);

                    // Prepare metadata update
                    var fileInfo = new FileInfo(file.FilePath);
                    var checksum = FileUtilities.CalculateChecksum(file.FilePath);

                    var updateDefinition = Builders<FileMetadata>.Update
                        .Set(f => f.Size, fileInfo.Length)
                        .Set(f => f.LastUpdated, DateTime.UtcNow)
                        .Set(f => f.Checksum, checksum);

                    bulkUpdates.Add(new UpdateOneModel<FileMetadata>(
                        Builders<FileMetadata>.Filter.Where(f => f.Id == fileId && f.UserId == user_id),
                        updateDefinition
                    )
                    { IsUpsert = true });

                    synchronizedFiles.Add(file.FileName);
                }
            }

            // Perform bulk update for all synchronized files
            if (bulkUpdates.Any())
            {
                await _fileCollection.BulkWriteAsync(bulkUpdates);
            }

            // Identify pending files (present in FilesToUpdate but not in archive)
            pendingFiles = filesToUpdate
                .Where(ftu => !synchronizedFiles.Contains(ftu.Key))
                .ToDictionary(ftu => ftu.Key, ftu => ftu.Value);

            // Mark synchronization as complete or update pending files
            if (!pendingFiles.Any())
            {
                // Mark synchronization as completed
                var completionUpdate = Builders<SynchronizationMetadata>.Update
                    .Set(s => s.IsCompleted, true)
                    .Set(s => s.FilesToUpdate, new Dictionary<string, string>()) // Clear FilesToUpdate
                    .Set(s => s.LastUpdated, DateTime.UtcNow);

                await _syncCollection.UpdateOneAsync(
                    s => s.Id == synchronization_id && s.UserId == user_id,
                    completionUpdate
                );
                return Ok(new
                {
                    Status = true,
                    Message = "File synchronization completed.",
                    Data = new
                    {
                        SynchronizedFiles = synchronizedFiles,
                    }
                });
            }
            else
            {
                // Update FilesToUpdate with the pending files
                var pendingUpdate = Builders<SynchronizationMetadata>.Update
                    .Set(s => s.FilesToUpdate, pendingFiles)
                    .Set(s => s.LastUpdated, DateTime.UtcNow);

                await _syncCollection.UpdateOneAsync(
                    s => s.Id == synchronization_id && s.UserId == user_id,
                    pendingUpdate
                );
            }

            return Ok(new
            {
                Status = false,
                Message = "File synchronization Incompleted.",
                Data = new
                {
                    SynchronizedFiles = synchronizedFiles,
                    PendingFiles = pendingFiles.Select(p => new { FileId = p.Value, FileName = p.Key })
                }
            });
        }
        finally
        {
            // Clean up temporary directory
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // Delete a file by ID
    [HttpDelete("delete/{file_id}")]
    public async Task<IActionResult> DeleteFile(string file_id, [FromQuery] int user_id = 1)
    {
        var fileMetadata = await _fileCollection.Find(f => f.Id == file_id && f.UserId == user_id).FirstOrDefaultAsync();
        if (fileMetadata == null)
            return NotFound("File not found.");

        var filePath = Path.Combine(_storageDir, file_id);
        if (System.IO.File.Exists(filePath))
            System.IO.File.Delete(filePath);

        await _fileCollection.DeleteOneAsync(f => f.Id == file_id);

        return Ok(new
        {
            Status = true,
            Message = "File deleted successfully.",
        });

    }


}
