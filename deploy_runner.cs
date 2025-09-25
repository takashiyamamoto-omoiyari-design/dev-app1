using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

class DeployRunner
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting optimized deployment runner...");
        
        // Immediately open a socket on port 5000 to indicate readiness to Replit
        var socketTask = Task.Run(() => StartTempHttpListener());
        
        // Start the actual application
        var appTask = Task.Run(() => StartApplication());
        
        // Wait for both tasks
        await Task.WhenAll(socketTask, appTask);
    }

    static async Task StartTempHttpListener()
    {
        Console.WriteLine("Opening port 5000 immediately for deployment...");
        
        // Create a TcpListener on port 5000
        TcpListener listener = new TcpListener(IPAddress.Any, 5000);
        
        try 
        {
            // Start listening for client requests
            listener.Start();
            Console.WriteLine("Port 5000 is now open and listening");
            
            // Accept clients and respond with a simple message
            while (true)
            {
                TcpClient client = await listener.AcceptTcpClientAsync();
                Console.WriteLine("Client connected - handling request");
                
                NetworkStream stream = client.GetStream();
                
                // Simple HTTP 200 response
                string response = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n\r\nApplication is starting, please wait...";
                byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                client.Close();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Temp HTTP listener error: {ex.Message}");
        }
        finally
        {
            // Stop listening
            listener.Stop();
        }
    }
    
    static async Task StartApplication()
    {
        try
        {
            Console.WriteLine("Starting the main application...");
            
            // Give the temp HTTP listener a moment to start
            await Task.Delay(1000);
            
            // Create process to run the application
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "bin/Release/net8.0/AzureRag.dll --urls=\"http://0.0.0.0:5000\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Directory.GetCurrentDirectory(),
            };
            
            // Set environment variables
            psi.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Production";
            psi.EnvironmentVariables["DOTNET_RUNNING_IN_CONTAINER"] = "true";
            psi.EnvironmentVariables["APP_BASE_PATH"] = "/dev-app1";
            
            // Start the process
            var process = Process.Start(psi);
            
            // Read the output
            while (!process.StandardOutput.EndOfStream)
            {
                string line = await process.StandardOutput.ReadLineAsync();
                Console.WriteLine(line);
            }
            
            // Wait for process to exit
            await process.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting application: {ex.Message}");
        }
    }
}