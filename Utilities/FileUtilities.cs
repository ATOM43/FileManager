using System.Security.Cryptography;
using Microsoft.AspNetCore.StaticFiles;

public static class FileUtilities
{
    public static string CalculateChecksum(string filePath)
    {
        using (var stream = System.IO.File.OpenRead(filePath))
        using (var sha256 = SHA256.Create())
        {
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    public static string CalculateChecksum(Stream fileStream)
    {
        using (var sha256 = SHA256.Create())
        {
            var hash = sha256.ComputeHash(fileStream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    // Utility method to compare file contents
    public static bool FileContentsAreEqual(string path1, string path2)
    {
        // Validate inputs
        if (!System.IO.File.Exists(path1) || !System.IO.File.Exists(path2))
            return false;

        // Open files for reading
        using (var file1 = System.IO.File.OpenRead(path1))
        using (var file2 = System.IO.File.OpenRead(path2))
        {
            // Compare lengths
            if (file1.Length != file2.Length)
                return false;

            // Buffers for reading files
            var buffer1 = new byte[8192];
            var buffer2 = new byte[8192];

            // Compare contents
            int bytesRead1, bytesRead2;
            while ((bytesRead1 = file1.Read(buffer1, 0, buffer1.Length)) > 0 &&
                (bytesRead2 = file2.Read(buffer2, 0, buffer2.Length)) > 0)
            {
                if (bytesRead1 != bytesRead2 || !buffer1.AsSpan(0, bytesRead1).SequenceEqual(buffer2.AsSpan(0, bytesRead2)))
                    return false;
            }
        }

        return true;
    }

    // Saves a file and calculates its checksum
    public static async Task<string> SaveFileAndCalculateChecksum(IFormFile file, string filePath)
    {
        string checksum;
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
            checksum = CalculateChecksum(stream);
        }
        return checksum;
    }

    public static bool IsValidFile(IFormFile file, long MaxFileSize, out string errorMessage)
    {
        if (file == null || file.Length == 0)
        {
            errorMessage = "No file uploaded.";
            return false;
        }

        if (file.Length > MaxFileSize)
        {
            errorMessage = $"File size exceeds the limit of {MaxFileSize / (1024 * 1024)} MB.";
            return false;
        }

        errorMessage = "";
        return true;
    }



    public static string GetMimeType(string fileName)
    {
        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(fileName, out var contentType))
        {
            contentType = "application/octet-stream"; // Default MIME type
        }
        return contentType;
    }
}
