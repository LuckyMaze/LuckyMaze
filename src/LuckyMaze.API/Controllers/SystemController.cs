using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LuckyMaze.Domain;
using LuckyMaze.Domain.Enums;
using LuckyMaze.Infrastructure.Services;

namespace LuckyMaze.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "Toamaisutaa.Admin")]
    public class SystemController(IHostAgentService hostAgent, IMazeHardwareService hardwareService) : ControllerBase
    {
        [HttpPost("shutdown")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        public async Task<IActionResult> Shutdown()
        {
            // Park the carriage safely before the host agent cuts power - the same center-park
            // G-code a normal round reset uses, over the already-open Klipper connection.
            await hardwareService.ResetAsync();
            await hostAgent.RequestShutdownAsync();
            return Accepted();
        }

        [HttpPost("network-mode")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SetNetworkMode([FromBody] NetworkModeRequest request)
        {
            switch (request.Mode)
            {
                case NetworkMode.Hotspot:
                    await hostAgent.RequestHotspotEnableAsync(request.Permanent);
                    break;
                case NetworkMode.Wifi:
                    await hostAgent.RequestHotspotDisableAsync();
                    break;
                default:
                    return BadRequest();
            }

            return Accepted();
        }
    }

    public class NetworkModeRequest
    {
        public NetworkMode Mode { get; set; }

        /// <summary>
        /// Only meaningful for Hotspot: skips the host agent's auto-revert-to-WiFi timeout. Leave
        /// false unless you've confirmed the hotspot actually works (e.g. from a phone already
        /// connected to it) - enabling it always drops remote access over the home network the
        /// moment it switches.
        /// </summary>
        public bool Permanent { get; set; }
    }
}
