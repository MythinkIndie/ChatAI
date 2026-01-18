using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ChatApp
{
    public class ChatDbContext : DbContext
    {
        public DbSet<Conversation> Conversations { get; set; }
        public DbSet<Message> Messages { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // SQLite para desarrollo local - cambia esto a SQL Server si lo prefieres
            optionsBuilder.UseSqlite("Data Source=chatapp.db");

            // Para SQL Server, usa esto en su lugar:
            //optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ChatAppDb;Trusted_Connection=True;");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configuración de la relación entre Conversación y Mensajes
            modelBuilder.Entity<Conversation>()
                .HasMany(c => c.Messages)
                .WithOne(m => m.Conversation)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            // Índices para mejorar el rendimiento
            modelBuilder.Entity<Message>()
                .HasIndex(m => m.ConversationId);

            modelBuilder.Entity<Message>()
                .HasIndex(m => m.Timestamp);

            modelBuilder.Entity<Conversation>()
                .HasIndex(c => c.StartedAt);
        }
    }

    [Table("Conversations")]
    public class Conversation
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; }

        [Required]
        public DateTime StartedAt { get; set; }

        public DateTime? LastMessageAt { get; set; }

        // Relación con mensajes
        public virtual ICollection<Message> Messages { get; set; } = new List<Message>();

        [NotMapped]
        public int MessageCount => Messages?.Count ?? 0;

        [NotMapped]
        public string DisplayDate => StartedAt.Date == DateTime.Today
            ? $"Hoy {StartedAt:HH:mm}"
            : StartedAt.Date == DateTime.Today.AddDays(-1)
                ? $"Ayer {StartedAt:HH:mm}"
                : StartedAt.ToString("dd/MM/yyyy HH:mm");
    }

    [Table("Messages")]
    public class Message
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int ConversationId { get; set; }

        [Required]
        public string Content { get; set; }

        [Required]
        public bool IsUserMessage { get; set; }

        [Required]
        public DateTime Timestamp { get; set; }

        // Campos adicionales opcionales
        public string ApiResponse { get; set; }

        public int? ResponseTimeMs { get; set; }

        // Navegación a la conversación
        [ForeignKey("ConversationId")]
        public virtual Conversation Conversation { get; set; }

        [NotMapped]
        public string SenderRole => IsUserMessage ? "Usuario" : "Asistente";

        [NotMapped]
        public string FormattedTimestamp => Timestamp.ToString("dd/MM/yyyy HH:mm:ss");
    }
}