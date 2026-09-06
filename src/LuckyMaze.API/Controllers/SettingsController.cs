using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LuckyMaze.Infrastructure.Services;
using LuckyMaze.Domain;

namespace LuckyMaze.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SettingsController : ControllerBase
    {
        private readonly IGameSettingsService _settingsService;

        public SettingsController(IGameSettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        [HttpGet]
        public async Task<ActionResult<GameSettings>> GetSettings()
        {
            var settings = await _settingsService.GetSettingsAsync();
            return Ok(settings);
        }

        [HttpPut]
        [Authorize(Policy = "Toamaisutaa.Admin")]
        public async Task<IActionResult> UpdateSettings([FromBody] GameSettings settings)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            await _settingsService.UpdateSettingsAsync(settings);
            return NoContent();
        }
    }
}
