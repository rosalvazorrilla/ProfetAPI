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
    public string Message { get; set; } = null!;
    [Column("type")]
    public string? Type { get; set; }
}