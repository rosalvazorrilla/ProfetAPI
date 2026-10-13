using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace ProfetAPI.Models;

public class Log
{
    [Key]
    [Column("id")]
    public int Id { get; set; }
    [Column("date")]
    public DateTime Date { get; set; }
    [Column("name")]
    public string Name { get; set; } = null!;
    [Column("message")]
    public string? Message { get; set; } // la columna admite NULL; con string no-nulo el visor de logs reventaba al leer esas filas
    [Column("type")]
    public string? Type { get; set; }
}