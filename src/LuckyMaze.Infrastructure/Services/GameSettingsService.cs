using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using LuckyMaze.Domain;

namespace LuckyMaze.Infrastructure.Services
{
    public class GameSettingsService : IGameSettingsService
    {
        private readonly LuckyMazeDbContext _dbContext;

        public GameSettingsService(LuckyMazeDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<GameSettings> GetSettingsAsync()
        {
            var settings = await _dbContext.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                // Fallback in case seed didn't run
                settings = new GameSettings();
                _dbContext.Settings.Add(settings);
                await _dbContext.SaveChangesAsync();
            }
            return settings;
        }

        public async Task UpdateSettingsAsync(GameSettings settings)
        {
            var existing = await _dbContext.Settings.FirstOrDefaultAsync();
            if (existing != null)
            {
                existing.MazeSize = settings.MazeSize;
                existing.GameSpeedMs = settings.GameSpeedMs;
                existing.MinBet = settings.MinBet;
                existing.MaxBet = settings.MaxBet;
                existing.PixelPitchMm = settings.PixelPitchMm;
                existing.OriginOffsetXMm = settings.OriginOffsetXMm;
                existing.OriginOffsetYMm = settings.OriginOffsetYMm;
                existing.InvertX = settings.InvertX;
                existing.InvertY = settings.InvertY;
                existing.StepFeedRateMmPerMin = settings.StepFeedRateMmPerMin;
                existing.TravelFeedRateMmPerMin = settings.TravelFeedRateMmPerMin;
                existing.AccelerationMmPerSec2 = settings.AccelerationMmPerSec2;
            }
            else
            {
                _dbContext.Settings.Add(settings);
            }
            
            await _dbContext.SaveChangesAsync();
        }
    }
}
