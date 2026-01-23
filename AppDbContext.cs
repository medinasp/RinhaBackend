using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace RinhaBackend;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Pessoa> Pessoas { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Pessoa>(entity =>
        {
            entity.HasKey(p => p.Id);

            // O Npgsql já converte List<string> para jsonb automaticamente
            entity.Property(p => p.Stack)
                .HasColumnType("jsonb")
                .Metadata.SetValueComparer(new ListComparer());

            entity.HasIndex(p => p.Apelido)
                .IsUnique();
            
            entity.HasIndex(p => p.Nome);
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Configura a conversão global para List<string>
        configurationBuilder.Properties<List<string>>()
            .HaveConversion<ListToStringConverter>();
    }

    private class ListToStringConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<string>, string>
    {
        public ListToStringConverter()
            : base(
                v => string.Join(",", v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
        {
        }
    }

    private class ListComparer : ValueComparer<List<string>>
    {
        public ListComparer()
            : base(
                (c1, c2) => c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList())
        {
        }
    }
}