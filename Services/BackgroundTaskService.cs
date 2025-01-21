//Services\BackgroundTaskService.cs
using System.Diagnostics;
using System.IO.Compression;

public class BackgroundTaskService
{
    private readonly string _storageDir = Path.Combine(Directory.GetCurrentDirectory(), "FileStorage");

    public void ExtractAndProcessZip(string zipPath, int userId, string folderId)
    {
        try
        {
            // Create the processing directory path
            var userDir = Path.Combine(_storageDir, userId.ToString());
            var processingDir = Path.Combine(userDir, "processing");
            Directory.CreateDirectory(processingDir);

            // Path for the extracted files
            var extractPath = Path.Combine(processingDir, folderId);
            Directory.CreateDirectory(extractPath);

            Console.WriteLine($"Extracting ZIP file: {zipPath} to {extractPath}");

            // Extract the ZIP file
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                zip.ExtractToDirectory(extractPath);
            }

            Console.WriteLine($"Extracted ZIP file to: {extractPath}");

            // Execute the Python script with the extracted directory
            ExecutePythonTask(extractPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error during extraction or processing: {ex.Message}");
        }
    }

    public void ExecutePythonTask(string datasetPath)
    {
        try
        {
            // Construct the Python environment activation and script execution command
            string pythonEnvActivation = "workon OneWare &&";
            string scriptPath = @"D:\Project\OneWare\OneAI\ONE_AI\one_worker.py";
            string command = $"{pythonEnvActivation} python \"{scriptPath}\" --gpu 0 --ram 0.2 --dir \"{datasetPath}\" --task t --time 5 --name Test_PCB";

            Console.WriteLine($"Executing task: {command}");

            // Set up process start info
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe", // Windows command prompt
                Arguments = $"/C {command}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(scriptPath) // Set working directory to script location
            };

            // Start the process
            using var process = Process.Start(processStartInfo);

            // Read output and error
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            // Wait for the process to exit
            process.WaitForExit();

            // Log output and errors
            Console.WriteLine($"Task completed with exit code {process.ExitCode}");
            if (!string.IsNullOrEmpty(output))
            {
                Console.WriteLine("Output:");
                Console.WriteLine(output);
            }

            if (!string.IsNullOrEmpty(error))
            {
                Console.Error.WriteLine("Error:");
                Console.Error.WriteLine(error);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error executing task: {ex.Message}");
        }
    }
}
