using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Persistence.Migrations;

/// <inheritdoc />
public partial class RenameActiveProfileIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameIndex(
            name: "ux_personality_profiles_activo_por_usuario",
            table: "personality_profiles",
            newName: "ux_personality_profiles_active_per_user");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameIndex(
            name: "ux_personality_profiles_active_per_user",
            table: "personality_profiles",
            newName: "ux_personality_profiles_activo_por_usuario");
    }
}
