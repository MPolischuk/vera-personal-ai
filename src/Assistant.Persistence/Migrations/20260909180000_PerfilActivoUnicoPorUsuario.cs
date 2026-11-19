using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assistant.Persistence.Migrations;

/// <inheritdoc />
public partial class PerfilActivoUnicoPorUsuario : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ux_personality_profiles_activo_por_usuario",
            table: "personality_profiles",
            column: "user_id",
            unique: true,
            filter: "is_active");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_personality_profiles_activo_por_usuario",
            table: "personality_profiles");
    }
}
