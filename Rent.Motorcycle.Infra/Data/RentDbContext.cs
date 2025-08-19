using Microsoft.EntityFrameworkCore;
using Rent.Motorcycle.Domain.Entities;


using Rider = Rent.Motorcycle.Domain.Entities.DeliveryRider;
using Moto = Rent.Motorcycle.Domain.Entities.Motorcycle;
using RentalEntity = Rent.Motorcycle.Domain.Entities.Rental;

namespace Rent.Motorcycle.Infra.Data
{
    public sealed class RentDbContext : DbContext
    {
        public RentDbContext(DbContextOptions<RentDbContext> options) : base(options) { }

        public DbSet<Rider> DeliveryRiders => Set<Rider>();
        public DbSet<Moto> Motorcycles => Set<Moto>();
        public DbSet<RentalEntity> Rentals => Set<RentalEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Rider>(b =>
            {
                b.ToTable("delivery_riders");
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).HasMaxLength(128).IsRequired();

                b.Property(x => x.Name).IsRequired();
                b.Property(x => x.CNPJ).IsRequired();
                b.HasIndex(x => x.CNPJ).IsUnique();
                b.Property(x => x.BirthDate).HasColumnType("timestamp with time zone");

                b.OwnsOne(x => x.Cnh, c =>
                {
                    c.Property(p => p.CnhNumber).HasColumnName("cnh_number").IsRequired();
                    c.Property(p => p.CnhImageUrl).HasColumnName("cnh_image_url");
                    c.Property(p => p.Type).HasColumnName("cnh_type").HasConversion<string>().IsRequired();
                    c.HasIndex(p => p.CnhNumber).IsUnique();
                });
            });

            modelBuilder.Entity<Moto>(b =>
            {
                b.ToTable("motorcycles");
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).HasMaxLength(128).IsRequired();

                b.Property(x => x.Model).IsRequired();
                b.Property(x => x.Plate).IsRequired();
                b.HasIndex(x => x.Plate).IsUnique();
            });

            modelBuilder.Entity<RentalEntity>(b =>
            {
                b.ToTable("rentals");
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).HasMaxLength(128).IsRequired();

                b.Property(x => x.IdDeliveryRider).HasMaxLength(128).IsRequired();
                b.Property(x => x.IdMotorcycle).HasMaxLength(128).IsRequired();

                b.Property(x => x.StartDate).HasColumnType("timestamp with time zone");
                b.Property(x => x.EndDate).HasColumnType("timestamp with time zone");
                b.Property(x => x.ExpectedEndDate).HasColumnType("timestamp with time zone");

                b.HasOne<Rider>()
                    .WithMany(r => r.Rentals)
                    .HasForeignKey(x => x.IdDeliveryRider)
                    .HasPrincipalKey(r => r.Id)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne<Moto>()
                    .WithMany()
                    .HasForeignKey(x => x.IdMotorcycle)
                    .HasPrincipalKey(m => m.Id)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}
