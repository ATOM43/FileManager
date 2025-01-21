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
    private const long MaxFileSize = 500 * 1024 * 1024; // 50 MB
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



    // POST /upload/archive
    [HttpPost("upload/archive")]
    public async Task<IActionResult> UploadArchive(IFormFile archive, [FromQuery] int user_id = 1)
    {
        if (archive == null || archive.Length == 0)
            return BadRequest(new { Status = false, Message = "No file provided or file is empty." });

        if (!archive.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { Status = false, Message = "Only zip files are allowed." });

        var userDir = Path.Combine(_storageDir, user_id.ToString());
        Directory.CreateDirectory(userDir);

        var folderId = Guid.NewGuid().ToString();
        var zipPath = Path.Combine(userDir, $"{folderId}.zip");

        try
        {
            using (var stream = new FileStream(zipPath, FileMode.Create))
            {
                await archive.CopyToAsync(stream);
            }

            return Ok(new
            {
                Status = true,
                Message = "Archive processed successfully",
                UserId = user_id,
                FolderId = folderId
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error uploading archive: {ex.Message}");
            return StatusCode(500, new { Status = false, Message = "Internal server error while uploading archive." });
        }
    }

    // List all uploaded files
    [HttpGet("folders")]
    public IActionResult GetUserFolders([FromQuery] int user_id)
    {
        try
        {
            // Path to the user's directory
            var userDir = Path.Combine(_storageDir, user_id.ToString());

            // Check if user directory exists
            if (!Directory.Exists(userDir))
            {
                return NotFound(new
                {
                    Status = false,
                    Message = "User directory not found."
                });
            }

            // Get all folder names under the user's directory
            // Get all file names under the user's directory
            var fileNames = Directory.GetFiles(userDir)
                                      .Select(Path.GetFileName) // Get only file names
                                      .ToList();


            return Ok(new
            {
                Status = true,
                Message = "Folders retrieved successfully.",
                UserId = user_id,
                Folders = fileNames
            });
        }
        catch (Exception ex)
        {
            // Log error (placeholder)
            Console.Error.WriteLine($"Error retrieving folders for user {user_id}: {ex.Message}");
            return StatusCode(500, new
            {
                Status = false,
                Message = "Internal server error while retrieving folders."
            });
        }
    }


    // Delete a file by ID
    [HttpDelete("folder")]
    public IActionResult DeleteFolder([FromQuery] int user_id, [FromQuery] string folder_id)
    {
        try
        {
            // Path to the user's folder
            var userDir = Path.Combine(_storageDir, user_id.ToString());
            var folderPath = Path.Combine(userDir, folder_id);

            // Check if folder exists
            if (!Directory.Exists(folderPath))
            {
                return NotFound(new
                {
                    Status = false,
                    Message = "Folder not found."
                });
            }

            // Delete the folder and all its contents
            Directory.Delete(folderPath, true);

            return Ok(new
            {
                Status = true,
                Message = "Folder deleted successfully.",
                UserId = user_id,
                FolderId = folder_id
            });
        }
        catch (Exception ex)
        {
            // Log error (placeholder)
            Console.Error.WriteLine($"Error deleting folder {folder_id} for user {user_id}: {ex.Message}");
            return StatusCode(500, new
            {
                Status = false,
                Message = "Internal server error while deleting folder."
            });
        }
    }

}