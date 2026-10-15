using Microsoft.EntityFrameworkCore;

namespace Musubi.Api.Platform.Data;

/// <summary>Contexto EF Core de MUSUBI (PostgreSQL; InMemory solo en local y pruebas).</summary>
/// <remarks>Sin entidades en la fundacion. NOTE (02-01): el modelo de datos nace en la spec 02-01.</remarks>
internal sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
