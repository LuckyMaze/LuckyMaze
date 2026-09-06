using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LuckyMaze.Infrastructure.Services
{
    /// <summary>
    /// Writes a request file into a directory bind-mounted from the host (see
    /// compose.hardware.yml) - the host agent (scripts/hostagent/agent.sh), running outside
    /// Docker with the privilege the container doesn't have, watches this directory and performs
    /// the actual action. The request is just a filename match, not executed content, so there's
    /// no way for this to run anything beyond the fixed set of actions the agent already knows
    /// about.
    /// </summary>
    public class HostAgentService : IHostAgentService
    {
        private readonly string? _requestPath;
        private readonly ILogger<HostAgentService> _logger;

        public HostAgentService(IConfiguration configuration, ILogger<HostAgentService> logger)
        {
            _requestPath = configuration["Hardware:HostAgentRequestPath"];
            _logger = logger;
        }

        public Task RequestShutdownAsync() => WriteRequestAsync("shutdown");

        public Task RequestHotspotEnableAsync(bool permanent) =>
            WriteRequestAsync(permanent ? "hotspot-enable-permanent" : "hotspot-enable");

        public Task RequestHotspotDisableAsync() => WriteRequestAsync("hotspot-disable");

        private async Task WriteRequestAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(_requestPath))
            {
                _logger.LogInformation("[HOST AGENT MOCK] Requested: {Name}", name);
                return;
            }

            try
            {
                Directory.CreateDirectory(_requestPath);
                var path = Path.Combine(_requestPath, $"{name}.request");
                await File.WriteAllTextAsync(path, DateTimeOffset.UtcNow.ToString("O"));
                _logger.LogInformation("Requested host action: {Name}", name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write host agent request '{Name}' to {Path}.", name, _requestPath);
            }
        }
    }
}
