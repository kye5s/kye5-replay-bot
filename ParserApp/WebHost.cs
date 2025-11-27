using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;

namespace ParserApp
{
    public static class WebHost
    {
        public static void Start(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddCors(x =>
            {
                x.AddDefaultPolicy(p =>
                    p.AllowAnyOrigin()
                     .AllowAnyHeader()
                     .AllowAnyMethod());
            });

            var app = builder.Build();
            app.UseCors();

            // DEFAULT ROUTE
            app.MapGet("/", () => "Parser API is running ✔");

            // MAIN ENDPOINT — Discord bot will POST replay here
            app.MapPost("/parse-replay", async (HttpRequest request) =>
            {
                if (!request.HasFormContentType)
                    return Results.BadRequest("Expected multipart form-data.");

                var form = await request.ReadFormAsync();

                // PATCHED LINE — correct way to check if the file exists
                if (!form.Files.Any(f => f.Name == "replay_file"))
                    return Results.BadRequest("No replay file uploaded.");

                var file = form.Files["replay_file"];

                // save replay temporarily
                var temp = Path.GetTempFileName();
                using (var stream = File.Create(temp))
                    await file.CopyToAsync(stream);

                // run your EXE parser
                var exePath = Path.Combine(AppContext.BaseDirectory, "ParserApp.exe");

                var process = new Process();
                process.StartInfo.FileName = exePath;
                process.StartInfo.Arguments = $"\"{temp}\"";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.UseShellExecute = false;
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit();

                return Results.Text(output, "application/json");
            });

            app.Run();
        }
    }
}
