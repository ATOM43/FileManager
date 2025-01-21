using Hangfire;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.IO.Compression;

[ApiController]
[Route("api/task")]
public class TaskController : ControllerBase
{
    private readonly string _storageDir = Path.Combine(Directory.GetCurrentDirectory(), "FileStorage");

    [HttpPost("run")]
    public IActionResult RunTask([FromQuery] string folder_id, [FromQuery] int user_id = 1)
    {
        if (string.IsNullOrEmpty(folder_id))
        {
            return BadRequest(new { Status = false, Message = "Invalid task request" });
        }

        // Resolve the ZIP file path
        var userDir = Path.Combine(_storageDir, user_id.ToString());
        var zipPath = Path.Combine(userDir, $"{folder_id}.zip");

        if (!System.IO.File.Exists(zipPath))
        {
            return NotFound(new { Status = false, Message = "ZIP file not found." });
        }

        // Generate a new folder ID for extraction
        var newFolderId = Guid.NewGuid().ToString();

        // Queue the background task with Hangfire
        var jobId = BackgroundJob.Enqueue<BackgroundTaskService>(taskService =>
            taskService.ExtractAndProcessZip(zipPath, user_id, newFolderId));

        return Ok(new
        {
            Status = true,
            Message = "Task has been queued",
            Processing_Id = newFolderId,
            JobId = jobId
        });
    }
    [HttpGet("status")]
    public IActionResult GetStatus([FromQuery] int user_id, [FromQuery] string folder_id)
    {
        if (string.IsNullOrEmpty(folder_id))
        {
            return BadRequest(new { Status = false, Message = "Invalid request" });
        }

        // Resolve the log file path
        var logFilePath = Path.Combine(_storageDir, user_id.ToString(), "processing", folder_id, "log", "log.txt");

        try
        {
            // Check if the log file exists
            if (!System.IO.File.Exists(logFilePath))
            {
                return Ok(new
                {
                    Status = true,
                    Message = "Log file not found. Task is starting.",
                    UserId = user_id,
                    FolderId = folder_id,
                    LogContent = "Starting"
                });
            }

            // Open and read the log file
            using (var fileStream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fileStream))
            {
                var logContent = reader.ReadToEnd();

                return Ok(new
                {
                    Status = true,
                    Message = "Log file retrieved successfully",
                    UserId = user_id,
                    FolderId = folder_id,
                    LogContent = logContent
                });
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error reading log file: {ex.Message}");
            return StatusCode(500, new { Status = false, Message = "Internal server error while retrieving log file." });
        }
    }
}
