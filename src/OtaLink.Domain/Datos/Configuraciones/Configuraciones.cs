using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OtaLink.Domain.Entidades;

namespace OtaLink.Domain.Datos.Configuraciones;

// Cada tabla se asigna a su esquema. Los nombres de columna salen solos: la convención snake_case
// (se activa al registrar el DbContext) convierte NombreUsuarioNormalizado en nombre_usuario_normalizado.

internal sealed class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("usuarios", "seguridad");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
    }
}

internal sealed class CiudadConfiguration : IEntityTypeConfiguration<Ciudad>
{
    public void Configure(EntityTypeBuilder<Ciudad> builder)
    {
        builder.ToTable("ciudades", "smart");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
    }
}

internal sealed class InteresConfiguration : IEntityTypeConfiguration<Interes>
{
    public void Configure(EntityTypeBuilder<Interes> builder)
    {
        builder.ToTable("intereses", "smart");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
    }
}

internal sealed class PerfilConfiguration : IEntityTypeConfiguration<Perfil>
{
    public void Configure(EntityTypeBuilder<Perfil> builder)
    {
        builder.ToTable("perfiles", "proceso");
        builder.HasKey(p => p.UsuarioId);
        builder.Property(p => p.UsuarioId).ValueGeneratedNever();
        builder.Property(p => p.FechaNacimiento).HasColumnType("date");

        builder.HasOne<Usuario>().WithOne().HasForeignKey<Perfil>(p => p.UsuarioId);
        builder.HasOne<Ciudad>().WithMany().HasForeignKey(p => p.CiudadId);
        builder.HasMany(p => p.Intereses).WithOne().HasForeignKey(i => i.UsuarioId);
    }
}

internal sealed class PerfilInteresConfiguration : IEntityTypeConfiguration<PerfilInteres>
{
    public void Configure(EntityTypeBuilder<PerfilInteres> builder)
    {
        builder.ToTable("perfiles_intereses", "proceso");
        builder.HasKey(pi => new { pi.UsuarioId, pi.InteresId });
        builder.HasOne<Interes>().WithMany().HasForeignKey(pi => pi.InteresId);
    }
}

internal sealed class PerfilDetalleFilaConfiguration : IEntityTypeConfiguration<PerfilDetalleFila>
{
    public void Configure(EntityTypeBuilder<PerfilDetalleFila> builder)
    {
        // Resultado de una función, no una tabla: sin clave y sin tabla asociada.
        builder.HasNoKey();
        builder.ToView(null);
        builder.Property(p => p.FechaNacimiento).HasColumnType("date");
    }
}
