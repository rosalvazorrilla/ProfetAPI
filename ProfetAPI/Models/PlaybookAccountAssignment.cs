namespace ProfetAPI.Models;

/// <summary>
/// A qué cuenta(s) de un cliente aplica una secuencia (ActivityPlaybook). Antes una
/// secuencia pertenecía a una sola cuenta — ahora se da de alta a nivel cliente y se
/// asigna a una o varias de sus cuentas, cada una con su propio estado de "predeterminada"
/// (aplica sola a los leads nuevos de esa cuenta).
/// </summary>
public class PlaybookAccountAssignment
{
    public int PlaybookId { get; set; }
    public int AccountId  { get; set; }
    public bool IsDefault { get; set; } = false;

    public virtual ActivityPlaybook Playbook { get; set; } = null!;
    public virtual Account Account { get; set; } = null!;
}
