using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LuckyMaze.Domain;
using LuckyMaze.Domain.Enums;
using System;

namespace LuckyMaze.Infrastructure.DBConfigurations
{
    public class GameSettingsConfiguration : IEntityTypeConfiguration<GameSettings>
    {
        public void Configure(EntityTypeBuilder<GameSettings> builder)
        {
            builder.HasKey(s => s.Id);

            // Seed a single default global settings instance
            builder.HasData(new GameSettings
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                MazeSize = MazeSize.Grid21x21,
                GameSpeedMs = 850,
                MinBet = 1.00m,
                MaxBet = 500.00m,
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        }
    }
}
