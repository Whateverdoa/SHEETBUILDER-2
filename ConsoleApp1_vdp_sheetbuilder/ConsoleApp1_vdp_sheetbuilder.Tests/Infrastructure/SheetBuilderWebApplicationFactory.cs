using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ConsoleApp1_vdp_sheetbuilder.Tests.Infrastructure;

public sealed class SheetBuilderWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _webRoot;

    public SheetBuilderWebApplicationFactory()
    {
        _webRoot = Path.Combine(Path.GetTempPath(), "sheetbuilder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_webRoot);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTest");
        builder.UseSetting(WebHostDefaults.WebRootKey, _webRoot);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["FileStorage:Directory"] = "uploads",
                ["UploadReliability:EnforceProgressForLarge"] = "false",
                ["UploadReliability:LargeFileThresholdMb"] = "500"
            };

            config.AddInMemoryCollection(overrides);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            try
            {
                if (Directory.Exists(_webRoot))
                {
                    Directory.Delete(_webRoot, recursive: true);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}
