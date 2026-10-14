using Microsoft.EntityFrameworkCore;
using Monitoring.Core;

namespace Monitoring.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Dish> Dishes => Set<Dish>();
    public DbSet<DishIngredient> DishIngredients => Set<DishIngredient>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<CustomerOrder> Orders => Set<CustomerOrder>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderShortage> OrderShortages => Set<OrderShortage>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<ShiftDish> ShiftDishes => Set<ShiftDish>();
    public DbSet<ShiftHistory> ShiftHistory => Set<ShiftHistory>();
    public DbSet<PurchaseLine> PurchaseLines => Set<PurchaseLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.ToTable("users");
            entity.HasIndex(user => user.Login).IsUnique();
            entity.Property(user => user.Login).HasMaxLength(64);
            entity.Property(user => user.DisplayName).HasMaxLength(80);
            entity.Property(user => user.PasswordHash).HasMaxLength(256);
            entity.Property(user => user.Role).HasConversion<string>().HasMaxLength(16);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.Property(product => product.Name).HasMaxLength(160);
            entity.Property(product => product.Unit).HasConversion<string>().HasMaxLength(8);
            entity.Property(product => product.PurchasePrice).HasPrecision(18, 4);
        });

        modelBuilder.Entity<Dish>(entity =>
        {
            entity.ToTable("dishes");
            entity.Property(dish => dish.Name).HasMaxLength(160);
            entity.Property(dish => dish.SalePrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<DishIngredient>(entity =>
        {
            entity.ToTable("dish_ingredients", table =>
                table.HasCheckConstraint("ck_dish_ingredients_quantity", "quantity > 0"));
            entity.HasKey(line => new { line.DishId, line.ProductId });
            entity.Property(line => line.Quantity).HasPrecision(18, 3);
            entity.HasOne(line => line.Dish).WithMany(dish => dish.Ingredients).HasForeignKey(line => line.DishId);
            entity.HasOne(line => line.Product).WithMany(product => product.Ingredients).HasForeignKey(line => line.ProductId);
        });

        modelBuilder.Entity<Delivery>(entity =>
        {
            entity.ToTable("deliveries");
            entity.Property(delivery => delivery.Number).HasMaxLength(64);
            entity.Property(delivery => delivery.Supplier).HasMaxLength(160);
            entity.HasOne(delivery => delivery.ReceivedByUser).WithMany().HasForeignKey(delivery => delivery.ReceivedBy);
        });

        modelBuilder.Entity<Batch>(entity =>
        {
            entity.ToTable("batches", table =>
            {
                table.HasCheckConstraint("ck_batches_quantity_remaining", "quantity_remaining >= 0");
                table.HasCheckConstraint("ck_batches_quantity_initial", "quantity_initial > 0");
            });
            entity.Property(batch => batch.QuantityInitial).HasPrecision(18, 3);
            entity.Property(batch => batch.QuantityRemaining).HasPrecision(18, 3);
            entity.Property(batch => batch.UnitCost).HasPrecision(18, 4);
            entity.Property(batch => batch.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(batch => batch.CertificateNumber).HasMaxLength(64);
            entity.Property(batch => batch.CertificateIssuer).HasMaxLength(160);
            entity.Property(batch => batch.KitchenNote).HasMaxLength(300);
            entity.HasIndex(batch => new { batch.ProductId, batch.Status, batch.ExpiresAt });
            entity.HasOne(batch => batch.Delivery).WithMany(delivery => delivery.Batches).HasForeignKey(batch => batch.DeliveryId);
            entity.HasOne(batch => batch.Product).WithMany(product => product.Batches).HasForeignKey(batch => batch.ProductId);
        });

        modelBuilder.Entity<CustomerOrder>(entity =>
        {
            entity.ToTable("orders");
            entity.Property(order => order.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(order => order.RejectionReason).HasMaxLength(2000);
            entity.Property(order => order.TableNumber).HasMaxLength(16);
            entity.HasOne(order => order.CreatedByUser).WithMany().HasForeignKey(order => order.CreatedBy);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("order_items", table =>
                table.HasCheckConstraint("ck_order_items_portions", "portions > 0"));
            entity.HasIndex(item => new { item.OrderId, item.DishId }).IsUnique();
            entity.Property(item => item.KitchenStatus).HasConversion<string>().HasMaxLength(16);
            entity.Property(item => item.KitchenNote).HasMaxLength(300);
            entity.HasOne(item => item.Order).WithMany(order => order.Items).HasForeignKey(item => item.OrderId);
            entity.HasOne(item => item.Dish).WithMany().HasForeignKey(item => item.DishId);
        });

        modelBuilder.Entity<OrderShortage>(entity =>
        {
            entity.ToTable("order_shortages");
            entity.Property(shortage => shortage.Required).HasPrecision(18, 3);
            entity.Property(shortage => shortage.Available).HasPrecision(18, 3);
            entity.HasOne(shortage => shortage.Order).WithMany(order => order.Shortages).HasForeignKey(shortage => shortage.OrderId);
            entity.HasOne(shortage => shortage.Product).WithMany().HasForeignKey(shortage => shortage.ProductId);
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.ToTable("stock_movements", table =>
                table.HasCheckConstraint("ck_stock_movements_quantity", "quantity > 0"));
            entity.Property(movement => movement.Quantity).HasPrecision(18, 3);
            entity.Property(movement => movement.Type).HasConversion<string>().HasMaxLength(32);
            entity.Property(movement => movement.Reason).HasMaxLength(32);
            entity.HasIndex(movement => movement.CreatedAt);
            entity.HasOne(movement => movement.Batch).WithMany(batch => batch.Movements).HasForeignKey(movement => movement.BatchId);
        });

        modelBuilder.Entity<ShiftDish>(entity =>
        {
            entity.ToTable("shift_dishes");
            entity.HasKey(row => row.DishId);
            entity.Property(row => row.StopReason).HasMaxLength(32);
            entity.Property(row => row.StopNote).HasMaxLength(300);
            entity.HasOne(row => row.Dish).WithOne().HasForeignKey<ShiftDish>(row => row.DishId);
        });

        modelBuilder.Entity<ShiftHistory>(entity =>
        {
            entity.ToTable("shift_history");
            entity.Property(row => row.ActorLogin).HasMaxLength(64);
            entity.Property(row => row.Text).HasMaxLength(300);
            entity.HasIndex(row => row.DishId);
            entity.HasOne(row => row.Dish).WithMany().HasForeignKey(row => row.DishId);
        });

        modelBuilder.Entity<PurchaseLine>(entity =>
        {
            entity.ToTable("purchase_lines", table =>
                table.HasCheckConstraint("ck_purchase_lines_quantity", "quantity > 0"));
            entity.Property(line => line.Quantity).HasPrecision(18, 3);
            entity.Property(line => line.Note).HasMaxLength(300);
            entity.Property(line => line.Source).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(line => new { line.FulfilledAt, line.CreatedAt });
            entity.HasOne(line => line.Product).WithMany().HasForeignKey(line => line.ProductId);
            entity.HasOne(line => line.RequestedByUser).WithMany().HasForeignKey(line => line.RequestedBy);
        });
    }
}
